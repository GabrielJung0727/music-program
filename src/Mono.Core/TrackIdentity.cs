using System.Security.Cryptography;
using System.Text;
using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// 스캔 한 번 동안 "이 파일은 어느 곡 id 인가"를 정한다.
///
/// 예전에는 파일 이름에서 id 를 계산했다. 앨범마다 "01 Intro.flac" 이 있으면 모두 같은 id 가 되어
/// 마지막에 스캔된 곡만 남았고, 재생 기록 · 플레이리스트 · 커버 캐시가 엉뚱한 곡을 가리켰다.
/// 이제 id 는 한 번 발급하면 그 파일을 따라간다.
///
/// 1. 같은 경로의 곡이 이미 있으면 그 id.
/// 2. 없으면, 파일이 사라진 곡 중 태그(앨범 · 트랙 번호 · 제목 · 길이)가 같은 곡이 <b>꼭 하나</b>일 때 그 id 를 이어 준다.
///    파일을 옮기거나 이름을 바꾼 경우다. 태그가 비었거나 후보가 둘 이상이면 잇지 않는다 — 틀린 곡에 붙으면 되돌릴 수 없다.
///    디스크 번호는 곡 모델에 없어서 보지 않는다. 위 넷이 같고 디스크만 다른 곡은 드물다.
/// 3. 둘 다 아니면 새 id. 예전과 같은 이름 기반 id 가 비어 있으면 그것을 쓴다 — 겹치지 않는 곡은 예전 id 를 그대로 가져서
///    이미 쌓인 기록이 맞는다. 비어 있지 않으면 경로에서 뽑은 짧은 해시를 붙인다.
///
/// 이미 덮어써진 채 쌓인 기록은 되돌릴 수 없다. 그 id 는 지금 그 경로에 있는 곡이 계속 갖는다.
/// </summary>
internal sealed class TrackIdentity
{
    /// <summary>태그의 길이와 디코더가 잰 길이는 조금 어긋날 수 있다.</summary>
    private const long DurationToleranceMs = 1500;

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly CatalogStore _catalog;
    private readonly Dictionary<string, string> _byPath = new(PathComparer);
    private readonly HashSet<string> _issued = new(StringComparer.Ordinal);

    /// <summary>파일이 사라진 로컬 곡. 경로로 못 찾은 파일이 처음 나올 때 한 번 만든다 — 바뀐 게 없는 재스캔은 이 비용을 치르지 않는다.</summary>
    private List<Track>? _missing;

    public TrackIdentity(CatalogStore catalog)
    {
        _catalog = catalog;
        foreach (var track in catalog.Tracks.Values.ToList())
        {
            if (track.Source == StreamingProvider.Local && !string.IsNullOrWhiteSpace(track.LocalPath))
                _byPath.TryAdd(Normalize(track.LocalPath), track.Id);
        }
    }

    /// <summary>옮긴 파일을 알아보는 데 쓰는 태그. 이름 · 폴더에서 끌어온 값이 아니라 파일에 적힌 값이어야 한다.</summary>
    public readonly record struct Tags(string? Title, string? AlbumId, int TrackNumber, long DurationMs)
    {
        public bool Complete => !string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(AlbumId)
                                && TrackNumber > 0 && DurationMs > 0;
    }

    /// <summary>이 파일의 곡 id. <paramref name="tags"/> 는 태그를 못 읽었으면 null.</summary>
    public string Resolve(string file, Tags? tags)
    {
        var path = Normalize(file);
        if (_byPath.TryGetValue(path, out var known)) return known;

        var id = (tags is { Complete: true } t ? Reattach(t) : null) ?? Issue(file, path);
        _byPath[path] = id;
        _issued.Add(id);
        return id;
    }

    private string? Reattach(Tags tags)
    {
        _missing ??= _catalog.Tracks.Values
            .Where(t => t.Source == StreamingProvider.Local
                        && !string.IsNullOrWhiteSpace(t.LocalPath)
                        && !File.Exists(t.LocalPath))
            .ToList();

        var matches = _missing.Where(t =>
                string.Equals(t.AlbumId, tags.AlbumId, StringComparison.Ordinal)
                && t.TrackNumber == tags.TrackNumber
                && string.Equals(t.Title, tags.Title, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(t.DurationMs - tags.DurationMs) <= DurationToleranceMs)
            .Take(2)
            .ToList();
        if (matches.Count != 1) return null;

        // 한 곡은 한 파일에만 이어 준다. 같은 태그의 두 번째 파일은 새 곡이 된다.
        _missing.Remove(matches[0]);
        return matches[0].Id;
    }

    private string Issue(string file, string path)
    {
        var legacy = "tr-" + Slug(Path.GetFileNameWithoutExtension(file));
        if (IsFree(legacy)) return legacy;

        var hashed = legacy + "-" + ShortHash(path);
        for (var n = 2; !IsFree(hashed); n++) hashed = legacy + "-" + ShortHash(path) + "-" + n;
        return hashed;
    }

    private bool IsFree(string id) => !_catalog.Tracks.ContainsKey(id) && !_issued.Contains(id);

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    /// <summary>경로에서 뽑은 8자리. 같은 경로면 늘 같다(Windows 에서는 대소문자를 가리지 않는다).</summary>
    private static string ShortHash(string path)
    {
        var key = OperatingSystem.IsWindows() ? path.ToLowerInvariant() : path;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8].ToLowerInvariant();
    }

    internal static string Slug(string s)
    {
        var chars = s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }
}
