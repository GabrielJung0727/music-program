using Auralis.Shared;
using Microsoft.Data.Sqlite;

namespace Auralis.Core;

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
            CREATE TABLE IF NOT EXISTS artists (id TEXT PRIMARY KEY, name TEXT, related TEXT, bio TEXT);
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
            return _tracks.Values.Where(t =>
            {
                var album = _albums.GetValueOrDefault(t.AlbumId);
                var artist = _artists.GetValueOrDefault(t.ArtistId);
                return t.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (album?.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (album?.Label?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (album?.Credits?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (artist?.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
            }).ToList();
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
    }

    private void Load(SqliteConnection con)
    {
        _artists.Clear();
        _albums.Clear();
        _tracks.Clear();
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT id,name,related,bio FROM artists";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var related = r.IsDBNull(2) ? [] : r.GetString(2).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                _artists[r.GetString(0)] = new Artist
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    RelatedArtistIds = related,
                    Bio = r.IsDBNull(3) ? null : r.GetString(3)
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
        UpsertArtist(con, coltrane);
        UpsertArtist(con, miles);
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
        UpsertAlbum(con, blue);
        UpsertAlbum(con, kob);
        SeedTrack(con, "tr-blue-train", "Blue Train", blue, coltrane, 96000, 24, false, 180000, 1,
            "[00:00.00]Piano figure\n[00:12.00]Horn entrance — the room leans in\n[00:48.00]Head, full band\n[01:30.00]Tenor solo");
        SeedTrack(con, "tr-moment-notice", "Moment's Notice", blue, coltrane, 96000, 24, false, 160000, 2,
            "[00:00.00]Up-tempo turnaround\n[00:08.00]Unison line");
        SeedTrack(con, "tr-so-what", "So What", kob, miles, 44100, 16, false, 200000, 1,
            "[00:00.00]Bass riff\n[00:18.00]Piano answer\n[00:32.00]Horns, So What");
        SeedTrack(con, "tr-dsd-demo", "DSD Demo (native)", kob, miles, 2822400, 1, true, 120000, 2, null);
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
            "INSERT INTO artists(id,name,related,bio) VALUES($id,$n,$r,$b) ON CONFLICT(id) DO UPDATE SET name=$n, related=$r, bio=$b",
            ("$id", artist.Id), ("$n", artist.Name), ("$r", string.Join(',', artist.RelatedArtistIds)), ("$b", artist.Bio));

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
