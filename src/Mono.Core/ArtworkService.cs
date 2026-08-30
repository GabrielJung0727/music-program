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
    }

    /// <summary>태그에 박힌 커버를 꺼내 캐시에 쓰고 경로를 돌려준다.</summary>
    public string? ExtractFrom(TagLib.File file, string key)
    {
        var picture = file.Tag.Pictures.FirstOrDefault();
        if (picture is null || picture.Data.Count == 0)
        {
            return null;
        }

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

    /// <summary>앨범 폴더에 놓인 cover.jpg / folder.jpg 류를 찾는다.</summary>
    public static string? SidecarFor(string audioPath)
    {
        var dir = Path.GetDirectoryName(audioPath);
        if (dir is null)
        {
            return null;
        }

        string[] names = ["cover.jpg", "cover.png", "folder.jpg", "front.jpg", "album.jpg"];
        foreach (var name in names)
        {
            var candidate = Path.Combine(dir, name);
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }
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
