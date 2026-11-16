namespace Mono.Core;

/// <summary>
/// 앨범 아트 캐시. 디코드·저장은 Core가 하고 Control은 캐시된 파일만 받는다
/// (아트 그리드 스크롤이 UI 스레드에서 디코드하지 않도록).
/// </summary>
public sealed class ArtworkService
{
    private readonly string _root;

    public ArtworkService(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "thumbs"));
    }

    /// <summary>태그에 박힌 커버를 꺼내 캐시에 쓰고 경로를 돌려준다.</summary>
    public string? ExtractFrom(TagLib.File file, string key)
    {
        var picture = file.Tag.Pictures.FirstOrDefault();
        if (picture is null || picture.Data.Count == 0)
            return null;

        var ext = picture.MimeType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
        var path = Path.Combine(_root, Sanitize(key) + ext);
        try
        {
            System.IO.File.WriteAllBytes(path, picture.Data.Data);
            return path;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// 폭을 지정한 요청에 내줄 파일. 지금은 원본을 그대로 돌려준다.
    ///
    /// 예전에는 폭마다 thumbs 폴더에 사본을 만들었는데, 크기를 줄이지 않는 단순 복사였고
    /// 사본 이름을 원본 파일 이름에서 땄다. 그래서 커버 파일이 전부 cover.jpg 인 라이브러리에서는
    /// 처음 요청된 앨범의 커버가 그 폭의 모든 앨범에 나갔고, 폭을 바꿔 요청할 때마다 사본이 늘었다.
    /// 실제로 줄인 썸네일이 필요해지면 원본 경로와 수정 시각으로 이름을 지어 여기서 만든다.
    /// </summary>
    public string? EnsureThumbnail(string sourcePath, int width) => sourcePath;

    /// <summary>앨범 폴더에 놓인 cover.jpg / folder.jpg 류를 찾는다.</summary>
    public static string? SidecarFor(string audioPath)
    {
        var dir = Path.GetDirectoryName(audioPath);
        if (dir is null) return null;

        string[] names = ["cover.jpg", "cover.png", "folder.jpg", "front.jpg", "album.jpg"];
        foreach (var name in names)
        {
            var candidate = Path.Combine(dir, name);
            if (System.IO.File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };

    private static string Sanitize(string key)
        => new(key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
}
