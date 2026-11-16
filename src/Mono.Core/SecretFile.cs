using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mono.Core;

/// <summary>
/// 비밀이 든 파일을 이 PC 의 이 Windows 사용자만 풀 수 있게 감싸 쓴다(DPAPI, CurrentUser).
///
/// 막는 것: 파일이 백업 · 동기화 폴더에 섞여 나가거나, 다른 PC · 다른 사용자에게 건네지는 경우.
/// 막지 못하는 것: 같은 사용자로 도는 프로그램. 그 프로그램은 Mono 와 똑같이 풀 수 있다.
/// 백업을 다른 PC 에 되살리면 풀리지 않으므로 다시 로그인해야 한다.
///
/// Windows 가 아니면 그대로 쓴다. Core 는 net8.0 이라 다른 OS 에서도 빌드되지만 지금 배포는 Windows 뿐이다.
/// </summary>
public static class SecretFile
{
    /// <summary>감싼 파일의 표시. 이게 없는 JSON 은 예전 Mono 가 쓴 평문으로 본다.</summary>
    public const string Format = "mono-dpapi-v1";

    // 같은 사용자의 다른 DPAPI 데이터와 섞여 풀리지 않게 하는 추가 키. 비밀은 아니다.
    private static readonly byte[] Entropy = "Mono.Core secret file v1"u8.ToArray();

    public static void Write(string path, string content)
    {
        var text = content;
        if (OperatingSystem.IsWindows())
        {
            var sealedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(content), Entropy, DataProtectionScope.CurrentUser);
            text = JsonSerializer.Serialize(new { format = Format, data = Convert.ToBase64String(sealedBytes) });
        }

        // 쓰다 멈춰도 원래 파일이 반쯤 덮이지 않게 옆에 쓰고 바꿔 끼운다.
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// 파일의 내용. <c>WasPlain</c> 이면 감싸지 않은 예전 파일이다 — 부른 쪽이 감싸서 다시 쓰면 된다.
    /// 감쌌지만 풀 수 없으면(다른 사용자 · 다른 PC · 손상) 내용은 null 이다.
    /// </summary>
    public static (string? Content, bool WasPlain) Read(string path)
    {
        var text = File.ReadAllText(path);
        string? data;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format", out var format)
                || format.ValueKind != JsonValueKind.String
                || format.GetString() != Format)
            {
                return (text, true);
            }

            data = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
        }
        catch (JsonException)
        {
            return (text, true);
        }

        if (data is null || !OperatingSystem.IsWindows()) return (null, false);
        try
        {
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(data), Entropy, DataProtectionScope.CurrentUser);
            return (Encoding.UTF8.GetString(plain), false);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return (null, false);
        }
    }
}
