using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// User 환경변수가 자식 프로세스에 안 실리는 경우를 대비해
/// %LOCALAPPDATA%\Mono\credentials.env 도 읽는다.
/// </summary>
internal static class CredentialStore
{
    private static readonly object Gate = new();
    private static Dictionary<string, string>? _fileValues;

    public static string? Get(string key)
    {
        var env = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        EnsureLoaded();
        lock (Gate)
        {
            return _fileValues is not null && _fileValues.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
                ? v.Trim()
                : null;
        }
    }

    public static void Write(IReadOnlyDictionary<string, string?> values)
    {
        var path = Path();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var lines = values
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => $"{kv.Key}={kv.Value!.Trim()}");
        File.WriteAllLines(path, lines);
        lock (Gate) _fileValues = null;
    }

    private static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_fileValues is not null) return;
            _fileValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = Path();
                if (!File.Exists(path)) return;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#') || !line.Contains('=')) continue;
                    var i = line.IndexOf('=');
                    var k = line[..i].Trim();
                    var v = line[(i + 1)..].Trim();
                    if (k.Length > 0) _fileValues[k] = v;
                }
            }
            catch
            {
                // 파일 손상 시 env 만 쓴다.
            }
        }
    }

    private static string Path() => UserPaths.Resolve("credentials.env");
}
