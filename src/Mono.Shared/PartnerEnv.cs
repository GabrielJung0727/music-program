namespace Mono.Shared;

public enum ProcessKind
{
    Core,
    Output
}

/// <summary>
/// 자식 프로세스에 실어 주는 스트리밍 환경변수.
///
/// 키와 시크릿은 스트리밍 어댑터가 사는 Core 에만 필요하다. Output 은 오디오를 장치로 내보낼 뿐이라
/// 쓸 일이 없다. 필요 없는 프로세스에 비밀을 넘기면 그 프로세스의 크래시 덤프 · 환경 조회로 새는 길만 하나 늘어난다.
/// </summary>
public static class PartnerEnv
{
    private static readonly string[] CoreNames =
    [
        "MONO_TIDAL_CLIENT_ID",
        "MONO_TIDAL_CLIENT_SECRET",
        "MONO_OAUTH_REDIRECT",
        "MONO_QOBUZ_APP_ID",
        "MONO_QOBUZ_APP_SECRET",
        "MONO_TIDAL_DEMO",
        "MONO_QOBUZ_DEMO"
    ];

    /// <summary>이 종류의 프로세스에 실어 줘도 되는 환경변수 이름.</summary>
    public static IReadOnlyList<string> For(ProcessKind kind)
        => kind == ProcessKind.Core ? CoreNames : [];
}
