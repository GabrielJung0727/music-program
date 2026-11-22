using System.Text.RegularExpressions;

namespace Mono.Shared;

/// <summary>
/// 로그와 화면에 나가는 한 줄에서 토큰 · 시크릿을 가린다.
///
/// 사용자가 로그 파일을 그대로 첨부하는 길이 있고(로드맵 M6), Qobuz 호출은 시크릿과 토큰을 URL 쿼리에 싣는다.
/// 지금 로그에 그런 줄이 찍히는 곳은 찾지 못했다 — 이 함수는 나중에 누가 URL 을 로그에 넣어도
/// 값이 나가지 않게 하는 마지막 그물이다. 모든 형태를 잡는다고 약속하지는 않는다.
/// </summary>
public static partial class LogRedact
{
    private const string Mask = "***";

    // key=value (쿼리 · 폼)
    [GeneratedRegex(@"(?<k>\b(?:app_secret|user_auth_token|access_token|refresh_token|client_secret|code_verifier)=)[^&\s""']+", RegexOptions.IgnoreCase)]
    private static partial Regex Pair();

    // "key":"value" (JSON)
    [GeneratedRegex(@"(?<k>""(?:app_secret|user_auth_token|access_token|refresh_token|client_secret)""\s*:\s*"")[^""]*", RegexOptions.IgnoreCase)]
    private static partial Regex Json();

    // Authorization 헤더 값
    [GeneratedRegex(@"(?<k>\b(?:Bearer|Basic)\s+)[A-Za-z0-9._~+/=\-]+", RegexOptions.IgnoreCase)]
    private static partial Regex Auth();

    public static string Line(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var s = Pair().Replace(text, "${k}" + Mask);
        s = Json().Replace(s, "${k}" + Mask);
        return Auth().Replace(s, "${k}" + Mask);
    }
}
