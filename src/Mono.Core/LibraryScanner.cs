using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// 로컬 폴더/NAS 스캔. 태그·커버·LRC를 함께 읽어 카탈로그에 병합한다.
/// 같은 앨범의 스트리밍 버전이 이미 있으면 하나로 표시되도록 표시만 남긴다.
/// </summary>
public sealed class LibraryScanner
{
    private static readonly string[] Ext =
        [".flac", ".wav", ".aiff", ".aif", ".dsf", ".dff", ".alac", ".m4a", ".mp3", ".aac"];

    private readonly CatalogStore _catalog;
    private readonly ArtworkService _artwork;

    public LibraryScanner(CatalogStore catalog, ArtworkService artwork)
    {
        _catalog = catalog;
        _artwork = artwork;
    }

    public DateTimeOffset? LastScan { get; private set; }
    public int LastCount { get; private set; }

    public int Scan(string root)
    {
        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
            LastScan = DateTimeOffset.UtcNow;
            LastCount = 0;
            return 0;
        }

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => Ext.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
        {
            try
            {
                Import(file);
                count++;
            }
            catch (Exception)
            {
                ImportBare(file);
                count++;
            }
        }

        LastScan = DateTimeOffset.UtcNow;
        LastCount = count;
        return count;
    }

    /// <summary>
    /// 여러 루트를 차례로 훑는다. 없는 폴더는 건너뛴다 —
    /// 외장 드라이브가 빠져 있다고 나머지 스캔까지 멈출 이유가 없다.
    /// </summary>
    public int ScanAll(IEnumerable<string> roots)
    {
        var total = 0;
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct())
        {
            if (!Directory.Exists(root)) continue;
            total += Scan(root);
        }

        LastScan = DateTimeOffset.UtcNow;
        LastCount = total;
        return total;
    }

    private void Import(string file)
    {
        using var tf = TagLib.File.Create(file);
        var tag = tf.Tag;
        // 트랙의 아티스트는 참여 음악가(performer)가 먼저다. 컴필레이션에서 앨범 아티스트를
        // 앞세우면 모든 곡이 "Various Artists" 로 보여 누가 연주했는지 알 수 없다.
        // 앨범을 묶는 키는 그대로 앨범 아티스트다 — 참여 음악가로 묶으면 컴필레이션 한 장이
        // 참여자 수만큼 쪼개진다.
        var albumArtistName = Blank(tag.FirstAlbumArtist) ?? Blank(tag.FirstPerformer) ?? "Unknown Artist";
        var artistName = Blank(tag.FirstPerformer) ?? albumArtistName;
        var albumTitle = string.IsNullOrWhiteSpace(tag.Album) ? Path.GetFileNameWithoutExtension(file) : tag.Album;
        var title = string.IsNullOrWhiteSpace(tag.Title) ? Path.GetFileNameWithoutExtension(file) : tag.Title;
        var ext = Path.GetExtension(file).ToLowerInvariant();
        var isDsd = ext is ".dsf" or ".dff";
        var artistId = "ar-" + Slug(artistName);
        var albumArtistId = "ar-" + Slug(albumArtistName);
        var albumId = "al-" + Slug(albumArtistName + "-" + albumTitle);
        var trackId = "tr-" + Slug(Path.GetFileNameWithoutExtension(file));

        var artist = _catalog.Artists.GetValueOrDefault(artistId) ?? new Artist { Id = artistId, Name = artistName };
        var albumArtist = albumArtistId == artistId
            ? artist
            : _catalog.Artists.GetValueOrDefault(albumArtistId) ?? new Artist { Id = albumArtistId, Name = albumArtistName };
        var album = _catalog.Albums.GetValueOrDefault(albumId) ?? new Album
        {
            Id = albumId,
            Title = albumTitle,
            ArtistId = albumArtistId
        };
        album.Year ??= tag.Year == 0 ? null : (int)tag.Year;
        album.Credits ??= string.IsNullOrWhiteSpace(tag.JoinedPerformers) ? null : tag.JoinedPerformers;
        album.LinerNotes ??= string.IsNullOrWhiteSpace(tag.Comment) ? null : tag.Comment;
        album.Label ??= string.IsNullOrWhiteSpace(tag.Publisher) ? null : tag.Publisher;

        var art = _artwork.ExtractFrom(tf, trackId) ?? ArtworkService.SidecarFor(file);
        album.ArtworkPath ??= art;

        var existing = _catalog.Tracks.GetValueOrDefault(trackId);
        var streamingTwin = _catalog.Tracks.Values.FirstOrDefault(t =>
            t.Source != StreamingProvider.Local &&
            t.Title.Contains(title, StringComparison.OrdinalIgnoreCase));

        var track = new Track
        {
            Id = trackId,
            Title = title,
            AlbumId = albumId,
            ArtistId = artistId,
            LocalPath = file,
            StreamingId = existing?.StreamingId ?? streamingTwin?.StreamingId,
            Source = StreamingProvider.Local,
            StreamingQuality = streamingTwin?.StreamingQuality ?? existing?.StreamingQuality ?? StreamingQuality.Unknown,
            SampleRate = tf.Properties.AudioSampleRate > 0 ? tf.Properties.AudioSampleRate : 44100,
            BitDepth = tf.Properties.BitsPerSample > 0 ? tf.Properties.BitsPerSample : (isDsd ? 1 : 16),
            Channels = tf.Properties.AudioChannels > 0 ? tf.Properties.AudioChannels : 2,
            IsDsd = isDsd,
            DsdRate = isDsd ? DsdRateOf(tf.Properties.AudioSampleRate) : null,
            DurationMs = DurationOf(tf, file),
            LyricsLrc = SidecarLyrics(file) ?? (string.IsNullOrWhiteSpace(tag.Lyrics) ? null : tag.Lyrics),
            ArtworkPath = art,
            TrackNumber = (int)tag.Track,
            Genres = [.. tag.Genres.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim())],
            Composers = [.. tag.Composers.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())],
            MergedLocalAndStreaming = (existing?.StreamingId ?? streamingTwin?.StreamingId) is not null
        };
        _catalog.UpsertTrack(track, album, artist, albumArtist);
    }

    /// <summary>태그를 못 읽은 파일의 길이. 디코더가 열리지 않으면 0 이다.</summary>
    private static long ProbeDuration(string file)
    {
        try
        {
            using var slicer = AudioSourceFactory.Open(new Track
            {
                Id = "probe",
                Title = "probe",
                AlbumId = "",
                ArtistId = "",
                LocalPath = file
            });
            return Math.Max(0, slicer.DurationMs);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// 길이는 반드시 채운다. 0 으로 들어오면 재생 타임라인이 트랙의 끝을 알 수 없고,
    /// 추정치로 때우면 그 지점을 넘겨 탐색했을 때 곡이 끝난 것으로 처리된다.
    /// 태그가 비면 디코더에게 직접 묻는다.
    /// </summary>
    private static long DurationOf(TagLib.File tf, string file)
    {
        var tagged = (long)tf.Properties.Duration.TotalMilliseconds;
        return tagged > 0 ? tagged : ProbeDuration(file);
    }

    private void ImportBare(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var artist = _catalog.Artists.GetValueOrDefault("ar-local") ?? new Artist { Id = "ar-local", Name = "Local" };
        var album = _catalog.Albums.GetValueOrDefault("al-local") ?? new Album { Id = "al-local", Title = "Imported", ArtistId = artist.Id };
        var track = new Track
        {
            Id = "tr-" + Slug(name),
            Title = name,
            AlbumId = album.Id,
            ArtistId = artist.Id,
            LocalPath = file,
            Source = StreamingProvider.Local,
            LyricsLrc = SidecarLyrics(file),
            ArtworkPath = ArtworkService.SidecarFor(file),
            DurationMs = ProbeDuration(file)
        };
        _catalog.UpsertTrack(track, album, artist);
    }

    /// <summary>같은 이름의 .lrc 파일이 있으면 동기화 가사로 쓴다.</summary>
    private static string? SidecarLyrics(string audioPath)
    {
        var lrc = Path.ChangeExtension(audioPath, ".lrc");
        return File.Exists(lrc) ? File.ReadAllText(lrc) : null;
    }

    private static int DsdRateOf(int sampleRate) => sampleRate switch
    {
        >= 11289600 => 256,
        >= 5644800 => 128,
        _ => 64
    };

    private static string Slug(string s)
    {
        var chars = s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }
}
