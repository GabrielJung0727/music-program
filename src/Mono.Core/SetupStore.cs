using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mono.Core;

/// <summary>
/// 첫 실행 마법사가 고른 값들. 이 PC 한 대에 매인 설정이라 Core 의 data 폴더에 둔다.
///
/// WebView 의 localStorage 에 두면 안 되는 이유가 둘 있다. 하나는 사라진다는 것 —
/// 프로필 폴더를 지우거나 캐시를 비우면 마법사가 다시 뜬다. 다른 하나는 Core 가
/// 못 읽는다는 것 — 다음 실행 때 이 값대로 라이브러리를 스캔하고 출력을 붙이려면
/// 브라우저 밖에서도 읽히는 곳에 있어야 한다.
///
/// 내용은 UI 가 정하는 자유 형식 JSON 이다. Core 는 무엇이 들었는지 알 필요가 없고,
/// 필드가 늘 때마다 여기를 고치게 만들고 싶지도 않다. 저장하고 돌려주기만 한다.
/// </summary>
public sealed class SetupStore
{
    private readonly string _path;
    private readonly object _gate = new();

    public SetupStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "setup.json");
    }

    /// <summary>저장된 설정. 아직 마법사를 끝내지 않았으면 completed=false 인 빈 객체.</summary>
    public JsonObject Read()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return Empty();
            try
            {
                return JsonNode.Parse(File.ReadAllText(_path)) as JsonObject ?? Empty();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // 깨진 파일 하나 때문에 앱이 못 뜨는 편보다, 마법사를 한 번 더 보는 편이 낫다.
                return Empty();
            }
        }
    }

    /// <summary>들어온 필드만 덮어쓴다. 보내지 않은 필드는 그대로 남는다.</summary>
    public JsonObject Merge(JsonObject patch)
    {
        lock (_gate)
        {
            var current = Read();
            foreach (var (key, value) in patch)
                current[key] = value?.DeepClone();

            current["savedAt"] = DateTimeOffset.UtcNow.ToString("o");
            File.WriteAllText(_path, current.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return current;
        }
    }

    /// <summary>마법사를 처음부터 다시 보고 싶을 때. Settings 의 초기화가 부른다.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            try { File.Delete(_path); } catch (IOException) { /* 이미 없으면 그만이다 */ }
        }
    }

    private static JsonObject Empty() => new() { ["completed"] = false };
}
