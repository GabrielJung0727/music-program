using Mono.Shared;
using Microsoft.Data.Sqlite;

namespace Mono.Core;

/// <summary>
/// 로컬 우선 카탈로그. SQLite에 저장하고, 탐색용 그래프는 메모리에 둔다.
/// 하이퍼링크 탐색(아티스트→앨범→트랙→크레딧)이 클라우드 왕복 없이 끝나야 한다.
/// </summary>
public sealed class CatalogStore : IDisposable
{
    private readonly string _dbPath;
    private readonly object _gate = new();
    private readonly Dictionary<string, Track> _tracks = new();
    private readonly Dictionary<string, Album> _albums = new();
    private readonly Dictionary<string, Artist> _artists = new();

    public CatalogStore(string dbPath)
    {
        _dbPath = dbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        using var con = Open();
        con.Execute("""
            CREATE TABLE IF NOT EXISTS artists (id TEXT PRIMARY KEY, name TEXT, related TEXT, bio TEXT, aliases TEXT);
            CREATE TABLE IF NOT EXISTS albums (id TEXT PRIMARY KEY, title TEXT, artist_id TEXT, liner TEXT, label TEXT, year INT, credits TEXT, art TEXT);
            CREATE TABLE IF NOT EXISTS tracks (
              id TEXT PRIMARY KEY, title TEXT, album_id TEXT, artist_id TEXT,
              local_path TEXT, streaming_id TEXT, source INT, quality INT,
              sample_rate INT, bit_depth INT, channels INT, is_dsd INT, dsd_rate INT,
              duration_ms INT, lyrics TEXT, art TEXT, merged INT, track_no INT DEFAULT 0);
            CREATE INDEX IF NOT EXISTS idx_tracks_title ON tracks(title);
            CREATE INDEX IF NOT EXISTS idx_tracks_artist ON tracks(artist_id);
            CREATE INDEX IF NOT EXISTS idx_tracks_album ON tracks(album_id);
            """);
        Migrate(con);
        Load(con);
        if (_tracks.Count == 0)
        {
            Seed(con);
            Load(con);
        }
        ImportAliasFile();
    }

    public IReadOnlyDictionary<string, Track> Tracks { get { lock (_gate) return _tracks; } }
    public IReadOnlyDictionary<string, Album> Albums { get { lock (_gate) return _albums; } }
    public IReadOnlyDictionary<string, Artist> Artists { get { lock (_gate) return _artists; } }

    public void UpsertTrack(Track track, Album album, Artist artist)
    {
        lock (_gate)
        {
            using var con = Open();
            UpsertArtist(con, artist);
            UpsertAlbum(con, album);
            con.Execute("""
                INSERT INTO tracks(id,title,album_id,artist_id,local_path,streaming_id,source,quality,sample_rate,bit_depth,channels,is_dsd,dsd_rate,duration_ms,lyrics,art,merged,track_no)
                VALUES($id,$title,$album,$artist,$path,$sid,$src,$q,$sr,$bd,$ch,$dsd,$dr,$dur,$ly,$art,$mg,$no)
                ON CONFLICT(id) DO UPDATE SET
                  title=$title, album_id=$album, artist_id=$artist, local_path=$path, streaming_id=$sid,
                  source=$src, quality=$q, sample_rate=$sr, bit_depth=$bd, channels=$ch, is_dsd=$dsd,
                  dsd_rate=$dr, duration_ms=$dur, lyrics=$ly, art=$art, merged=$mg, track_no=$no
                """,
                ("$id", track.Id), ("$title", track.Title), ("$album", album.Id), ("$artist", artist.Id),
                ("$path", track.LocalPath), ("$sid", track.StreamingId), ("$src", (int)track.Source),
                ("$q", (int)track.StreamingQuality), ("$sr", track.SampleRate), ("$bd", track.BitDepth),
                ("$ch", track.Channels), ("$dsd", track.IsDsd ? 1 : 0), ("$dr", track.DsdRate),
                ("$dur", track.DurationMs), ("$ly", track.LyricsLrc), ("$art", track.ArtworkPath),
                ("$mg", track.MergedLocalAndStreaming ? 1 : 0), ("$no", track.TrackNumber));
            _artists[artist.Id] = artist;
            _albums[album.Id] = album;
            _tracks[track.Id] = track;
        }
    }

    public IReadOnlyList<Track> Search(string query)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return _tracks.Values.ToList();
            }

            var q = query.Trim();
            var qNorm = NormalizeForSearch(q);
            return _tracks.Values.Where(t =>
            {
                var album = _albums.GetValueOrDefault(t.AlbumId);
                var artist = _artists.GetValueOrDefault(t.ArtistId);
                return t.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (album?.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (album?.Label?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (album?.Credits?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (artist?.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || MatchesAlias(artist, qNorm);
            }).ToList();
        }
    }

    /// <summary>
    /// 다국어 아티스트 검색 — 한국어 표기로 원어(가나·한자·로마자) 아티스트를 찾을 수 있도록
    /// 별칭 목록(Artist.AlternateNames)을 함께 매칭한다. 공백/대소문자 차이를 무시한다.
    /// 완전한 자동 음역(발음 변환) 엔진이 아니라, 등록된 별칭 기반 매칭이다.
    /// </summary>
    private static bool MatchesAlias(Artist? artist, string qNorm)
    {
        if (artist is null || qNorm.Length == 0)
        {
            return false;
        }

        if (NormalizeForSearch(artist.Name).Contains(qNorm, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return artist.AlternateNames.Any(a => NormalizeForSearch(a).Contains(qNorm, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeForSearch(string s)
        => new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

    /// <summary>
    /// 아티스트에 다국어 별칭을 추가/치환한다. 라이브러리 스캔이나 관리자 도구에서 호출한다.
    /// </summary>
    public bool SetArtistAliases(string artistId, IEnumerable<string> aliases)
    {
        lock (_gate)
        {
            if (!_artists.TryGetValue(artistId, out var artist))
            {
                return false;
            }

            using var con = Open();
            var updated = new Artist
            {
                Id = artist.Id,
                Name = artist.Name,
                RelatedArtistIds = artist.RelatedArtistIds,
                Bio = artist.Bio,
                AlternateNames = aliases.Select(a => a.Trim()).Where(a => a.Length > 0).Distinct().ToList()
            };
            UpsertArtist(con, updated);
            _artists[artistId] = updated;
            return true;
        }
    }

    /// <summary>
    /// 스마트 오토플레이용 다음 곡 후보. 같은 아티스트 → 연관 아티스트 → 그 외 순으로
    /// 이미 재생한 트랙을 피해 최대 count개를 고른다.
    /// </summary>
    public IReadOnlyList<Track> Recommend(string? seedTrackId, IEnumerable<string> excludeTrackIds, int count)
    {
        lock (_gate)
        {
            var exclude = new HashSet<string>(excludeTrackIds);
            var seed = seedTrackId is null ? null : _tracks.GetValueOrDefault(seedTrackId);
            var picks = new List<Track>();

            if (seed is not null)
            {
                picks.AddRange(_tracks.Values
                    .Where(t => t.ArtistId == seed.ArtistId && !exclude.Contains(t.Id))
                    .OrderBy(_ => Random.Shared.Next()));

                if (_artists.TryGetValue(seed.ArtistId, out var artist))
                {
                    var relatedIds = new HashSet<string>(artist.RelatedArtistIds);
                    picks.AddRange(_tracks.Values
                        .Where(t => relatedIds.Contains(t.ArtistId) && !exclude.Contains(t.Id))
                        .OrderBy(_ => Random.Shared.Next()));
                }
            }

            picks.AddRange(_tracks.Values
                .Where(t => !exclude.Contains(t.Id))
                .OrderBy(_ => Random.Shared.Next()));

            return picks.DistinctBy(t => t.Id).Take(Math.Max(0, count)).ToList();
        }
    }

    /// <summary>Control이 그리는 카탈로그 뷰. 아트는 캐시 URL로만 넘긴다.</summary>
    public IReadOnlyList<object> CatalogView()
    {
        lock (_gate)
        {
            return _tracks.Values
                .OrderBy(t => _artists.GetValueOrDefault(t.ArtistId)?.Name)
                .ThenBy(t => _albums.GetValueOrDefault(t.AlbumId)?.Title)
                .ThenBy(t => t.TrackNumber)
                .Select(t => (object)new
                {
                    t.Id,
                    t.Title,
                    t.AlbumId,
                    t.ArtistId,
                    artist = _artists.GetValueOrDefault(t.ArtistId)?.Name,
                    artistAliases = _artists.GetValueOrDefault(t.ArtistId)?.AlternateNames ?? [],
                    album = _albums.GetValueOrDefault(t.AlbumId)?.Title,
                    year = _albums.GetValueOrDefault(t.AlbumId)?.Year,
                    label = _albums.GetValueOrDefault(t.AlbumId)?.Label,
                    t.SampleRate,
                    t.BitDepth,
                    t.IsDsd,
                    t.DsdRate,
                    t.DurationMs,
                    t.Source,
                    t.StreamingQuality,
                    t.MergedLocalAndStreaming,
                    hasLyrics = !string.IsNullOrWhiteSpace(t.LyricsLrc),
                    hasLocal = t.LocalPath is not null,
                    artUrl = t.ArtworkPath is null ? null : $"/api/art/{t.Id}",
                    badge = QualityPolicyEngine.Badge(t, false)
                })
                .ToList();
        }
    }

    /// <summary>메타데이터 그래프 질의 — 아티스트에서 앨범·트랙·연관 아티스트로.</summary>
    public object Graph(string artistId)
    {
        lock (_gate)
        {
            var artist = _artists.GetValueOrDefault(artistId);
            if (artist is null)
            {
                return new { error = "unknown artist" };
            }

            var albums = _albums.Values.Where(a => a.ArtistId == artistId)
                .Select(a => new
                {
                    a.Id,
                    a.Title,
                    a.Year,
                    a.Label,
                    a.Credits,
                    a.LinerNotes,
                    artUrl = _tracks.Values.FirstOrDefault(t => t.AlbumId == a.Id && t.ArtworkPath is not null) is { } at
                        ? $"/api/art/{at.Id}"
                        : null,
                    trackCount = _tracks.Values.Count(t => t.AlbumId == a.Id)
                })
                .ToList();
            var tracks = _tracks.Values.Where(t => t.ArtistId == artistId)
                .OrderBy(t => t.TrackNumber)
                .Select(t => new { t.Id, t.Title, t.AlbumId, t.DurationMs, badge = QualityPolicyEngine.Badge(t, false) })
                .ToList();
            var related = artist.RelatedArtistIds
                .Select(id => _artists.GetValueOrDefault(id))
                .Where(a => a is not null)
                .Select(a => new { a!.Id, a.Name, a.Bio })
                .ToList();

            // 같은 레이블·같은 시대의 아티스트도 그래프의 이웃이다.
            var era = albums.Select(a => a.Year).Where(y => y is not null).Select(y => y!.Value).ToList();
            var neighbours = _albums.Values
                .Where(a => a.ArtistId != artistId && a.Year is not null && era.Any(y => Math.Abs(y - a.Year!.Value) <= 3))
                .Select(a => _artists.GetValueOrDefault(a.ArtistId))
                .Where(a => a is not null)
                .DistinctBy(a => a!.Id)
                .Take(8)
                .Select(a => new { a!.Id, a.Name })
                .ToList();

            return new { artist, albums, tracks, related, neighbours };
        }
    }

    private static void Migrate(SqliteConnection con)
    {
        try
        {
            con.Execute("ALTER TABLE tracks ADD COLUMN track_no INT DEFAULT 0");
        }
        catch (SqliteException)
        {
            // 이미 있는 컬럼.
        }

        try
        {
            con.Execute("ALTER TABLE artists ADD COLUMN aliases TEXT");
        }
        catch (SqliteException)
        {
            // 이미 있는 컬럼.
        }
    }

    private void Load(SqliteConnection con)
    {
        _artists.Clear();
        _albums.Clear();
        _tracks.Clear();
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT id,name,related,bio,aliases FROM artists";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var related = r.IsDBNull(2) ? [] : r.GetString(2).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                var aliases = r.IsDBNull(4) ? [] : r.GetString(4).Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
                _artists[r.GetString(0)] = new Artist
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    RelatedArtistIds = related,
                    Bio = r.IsDBNull(3) ? null : r.GetString(3),
                    AlternateNames = aliases
                };
            }
        }

        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT id,title,artist_id,liner,label,year,credits,art FROM albums";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                _albums[r.GetString(0)] = new Album
                {
                    Id = r.GetString(0),
                    Title = r.GetString(1),
                    ArtistId = r.GetString(2),
                    LinerNotes = r.IsDBNull(3) ? null : r.GetString(3),
                    Label = r.IsDBNull(4) ? null : r.GetString(4),
                    Year = r.IsDBNull(5) ? null : r.GetInt32(5),
                    Credits = r.IsDBNull(6) ? null : r.GetString(6),
                    ArtworkPath = r.IsDBNull(7) ? null : r.GetString(7)
                };
            }
        }

        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT id,title,album_id,artist_id,local_path,streaming_id,source,quality,sample_rate,bit_depth,channels,is_dsd,dsd_rate,duration_ms,lyrics,art,merged,IFNULL(track_no,0) FROM tracks";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                _tracks[r.GetString(0)] = new Track
                {
                    Id = r.GetString(0),
                    Title = r.GetString(1),
                    AlbumId = r.GetString(2),
                    ArtistId = r.GetString(3),
                    LocalPath = r.IsDBNull(4) ? null : r.GetString(4),
                    StreamingId = r.IsDBNull(5) ? null : r.GetString(5),
                    Source = (StreamingProvider)r.GetInt32(6),
                    StreamingQuality = (StreamingQuality)r.GetInt32(7),
                    SampleRate = r.GetInt32(8),
                    BitDepth = r.GetInt32(9),
                    Channels = r.GetInt32(10),
                    IsDsd = r.GetInt32(11) == 1,
                    DsdRate = r.IsDBNull(12) ? null : r.GetInt32(12),
                    DurationMs = r.GetInt64(13),
                    LyricsLrc = r.IsDBNull(14) ? null : r.GetString(14),
                    ArtworkPath = r.IsDBNull(15) ? null : r.GetString(15),
                    MergedLocalAndStreaming = r.GetInt32(16) == 1,
                    TrackNumber = r.GetInt32(17)
                };
            }
        }
    }

    private void Seed(SqliteConnection con)
    {
        var coltrane = new Artist { Id = "ar-coltrane", Name = "John Coltrane", RelatedArtistIds = ["ar-miles"], Bio = "Saxophone, modal and sheets of sound." };
        var miles = new Artist { Id = "ar-miles", Name = "Miles Davis", RelatedArtistIds = ["ar-coltrane"], Bio = "Trumpet, Kind of Blue through fusion." };
        // 다국어 검색 예시: 한국어 표기(요네즈 켄시)로 원어 아티스트(米津玄師)를 찾을 수 있다.
        var yonezu = new Artist { Id = "ar-yonezu", Name = "米津玄師", AlternateNames = ["요네즈 켄시", "Kenshi Yonezu", "よねづ けんし"], Bio = "Singer-songwriter and vocaloid producer (Hachi)." };
        var utada = new Artist { Id = "ar-utada", Name = "宇多田ヒカル", AlternateNames = ["우타다 히카루", "Hikaru Utada", "うただ ひかる"], Bio = "J-pop songwriter." };
        var yoasobi = new Artist { Id = "ar-yoasobi", Name = "YOASOBI", AlternateNames = ["요아소비", "YOASOBI", "よあそび"], Bio = "Ayase × ikura." };
        var radwimps = new Artist { Id = "ar-radwimps", Name = "RADWIMPS", AlternateNames = ["라드윔프스", "래드윔프스"], Bio = "Japanese rock band." };
        var iu = new Artist { Id = "ar-iu", Name = "아이유", AlternateNames = ["IU", "이지은", "Lee Ji-eun"], Bio = "K-pop singer-songwriter." };
        UpsertArtist(con, coltrane);
        UpsertArtist(con, miles);
        UpsertArtist(con, yonezu);
        UpsertArtist(con, utada);
        UpsertArtist(con, yoasobi);
        UpsertArtist(con, radwimps);
        UpsertArtist(con, iu);
        var blue = new Album
        {
            Id = "al-blue-train",
            Title = "Blue Train",
            ArtistId = coltrane.Id,
            LinerNotes = "1957 Blue Note. Title track: the horn enters after a measured piano figure — listen for the handoff into the head.",
            Label = "Blue Note",
            Year = 1957,
            Credits = "Coltrane, Morgan, Fuller, Drew, Chambers, Jones"
        };
        var kob = new Album
        {
            Id = "al-kob",
            Title = "Kind of Blue",
            ArtistId = miles.Id,
            LinerNotes = "Modal frames. So What: bass riff as the room's pulse. Follow the piano voicings into the first solo.",
            Label = "Columbia",
            Year = 1959,
            Credits = "Davis, Coltrane, Adderley, Evans, Chambers, Cobb"
        };
        var stray = new Album
        {
            Id = "al-stray-sheep",
            Title = "STRAY SHEEP",
            ArtistId = yonezu.Id,
            Label = "Sony Music",
            Year = 2020,
            Credits = "米津玄師"
        };
        UpsertAlbum(con, blue);
        UpsertAlbum(con, kob);
        UpsertAlbum(con, stray);
        SeedTrack(con, "tr-blue-train", "Blue Train", blue, coltrane, 96000, 24, false, 180000, 1,
            "[00:00.00]Piano figure\n[00:12.00]Horn entrance — the room leans in\n[00:48.00]Head, full band\n[01:30.00]Tenor solo");
        SeedTrack(con, "tr-moment-notice", "Moment's Notice", blue, coltrane, 96000, 24, false, 160000, 2,
            "[00:00.00]Up-tempo turnaround\n[00:08.00]Unison line");
        SeedTrack(con, "tr-so-what", "So What", kob, miles, 44100, 16, false, 200000, 1,
            "[00:00.00]Bass riff\n[00:18.00]Piano answer\n[00:32.00]Horns, So What");
        SeedTrack(con, "tr-dsd-demo", "DSD Demo (native)", kob, miles, 2822400, 1, true, 120000, 2, null);
        SeedTrack(con, "tr-kanden", "感電", stray, yonezu, 44100, 16, false, 208000, 1, null);
    }

    /// <summary>
    /// data/aliases.json — [{ "id":"ar-yonezu", "aliases":["…"] }] 또는 [{ "name":"米津玄師", "aliases":["…"] }]
    /// </summary>
    private void ImportAliasFile()
    {
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(_dbPath) ?? "data", "aliases.json");
            if (!File.Exists(path))
            {
                var seed = """
                [
                  { "id": "ar-yonezu", "aliases": ["요네즈 켄시", "Kenshi Yonezu", "米津玄師", "よねず けんし"] },
                  { "id": "ar-utada", "aliases": ["우타다 히카루", "Hikaru Utada", "宇多田ヒカル"] },
                  { "name": "John Coltrane", "aliases": ["존 콜트레인", "콜트레인", "ジョン・コルトレーン", "John William Coltrane"] },
                  { "name": "Miles Davis", "aliases": ["마일스 데이비스", "마일즈 데이비스", "マイルス・デイビス", "Miles Dewey Davis"] },
                  { "name": "Dave Brubeck", "aliases": ["데이브 브루벡", "デイブ・ブルーベック"] },
                  { "name": "Hiromi", "aliases": ["히로미", "上原ひろみ", "Hiromi Uehara", "우에하라 히로미"] },
                  { "mbid": "b625448e-bf4a-41c3-a997-987a97342e02", "name": "John Coltrane", "aliases": ["Trane"] }
                ]
                """;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, seed);
            }

            var rows = System.Text.Json.JsonSerializer.Deserialize<List<AliasRow>>(File.ReadAllText(path),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            lock (_gate)
            {
                using var con = Open();
                foreach (var row in rows)
                {
                    Artist? artist = null;
                    if (!string.IsNullOrWhiteSpace(row.Id) && _artists.TryGetValue(row.Id, out var byId))
                        artist = byId;
                    else if (!string.IsNullOrWhiteSpace(row.Name))
                        artist = _artists.Values.FirstOrDefault(a =>
                            a.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
                    if (artist is null || row.Aliases is null) continue;
                    var merged = artist.AlternateNames.Concat(row.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    var updated = new Artist
                    {
                        Id = artist.Id,
                        Name = artist.Name,
                        RelatedArtistIds = artist.RelatedArtistIds,
                        Bio = artist.Bio,
                        AlternateNames = merged
                    };
                    UpsertArtist(con, updated);
                    _artists[updated.Id] = updated;
                }
            }
        }
        catch
        {
            // 별칭 파일 오류는 시드를 막지 않는다.
        }
    }

    private sealed class AliasRow
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public List<string>? Aliases { get; set; }
    }

    private static void SeedTrack(SqliteConnection con, string id, string title, Album album, Artist artist, int sr, int bd, bool dsd, long dur, int no, string? lrc)
        => con.Execute("""
            INSERT INTO tracks(id,title,album_id,artist_id,local_path,streaming_id,source,quality,sample_rate,bit_depth,channels,is_dsd,dsd_rate,duration_ms,lyrics,art,merged,track_no)
            VALUES($id,$title,$album,$artist,NULL,NULL,0,$q,$sr,$bd,2,$dsd,$dr,$dur,$ly,NULL,0,$no)
            """,
            ("$id", id), ("$title", title), ("$album", album.Id), ("$artist", artist.Id),
            ("$q", (int)StreamingQuality.Unknown), ("$sr", sr),
            ("$bd", bd), ("$dsd", dsd ? 1 : 0), ("$dr", dsd ? 64 : null),
            ("$dur", dur), ("$ly", lrc), ("$no", no));

    private static void UpsertArtist(SqliteConnection con, Artist artist)
        => con.Execute(
            "INSERT INTO artists(id,name,related,bio,aliases) VALUES($id,$n,$r,$b,$al) ON CONFLICT(id) DO UPDATE SET name=$n, related=$r, bio=$b, aliases=$al",
            ("$id", artist.Id), ("$n", artist.Name), ("$r", string.Join(',', artist.RelatedArtistIds)), ("$b", artist.Bio),
            ("$al", string.Join(';', artist.AlternateNames)));

    private static void UpsertAlbum(SqliteConnection con, Album album)
        => con.Execute(
            "INSERT INTO albums(id,title,artist_id,liner,label,year,credits,art) VALUES($id,$t,$a,$l,$lb,$y,$c,$art) ON CONFLICT(id) DO UPDATE SET title=$t, artist_id=$a, liner=IFNULL($l,liner), label=IFNULL($lb,label), year=IFNULL($y,year), credits=IFNULL($c,credits), art=IFNULL($art,art)",
            ("$id", album.Id), ("$t", album.Title), ("$a", album.ArtistId), ("$l", album.LinerNotes),
            ("$lb", album.Label), ("$y", album.Year), ("$c", album.Credits), ("$art", album.ArtworkPath));

    private SqliteConnection Open()
    {
        var con = new SqliteConnection($"Data Source={_dbPath}");
        con.Open();
        return con;
    }

    public void Dispose() { }
}

internal static class SqliteExt
{
    public static void Execute(this SqliteConnection con, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        cmd.ExecuteNonQuery();
    }
}
