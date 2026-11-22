using Mono.Shared;

namespace Mono.Core;

internal enum KeySource
{
    /// <summary>키가 없다. 이 서비스는 연결할 수 없거나 데모뿐이다.</summary>
    None,

    /// <summary>소스에 있는 공개 기본값(Tidal 의 시험용 Client ID).</summary>
    Default,

    /// <summary>릴리스 빌드가 넣은 값.</summary>
    Build,

    /// <summary>사용자가 환경변수 · credentials.env 로 준 값.</summary>
    Override
}

/// <summary>
/// 스트리밍 파트너 키를 읽는 유일한 자리. 어느 서비스 어댑터도 환경변수를 직접 읽지 않는다.
///
/// 순서: 사용자 지정(환경변수 · credentials.env) → 빌드가 넣은 값(<see cref="BuildKeys"/>) → 공개 기본값 → 없음.
/// 사용자가 자기 키로 돌리는 길은 막지 않는다. 대신 화면이 어느 키를 쓰는지 보여 준다.
///
/// 비밀은 소스에 없다. <see cref="BuildKeys"/> 는 릴리스 빌드가 obj/ 에 만든다(Mono.Core.csproj).
/// 설치 파일은 공개 다운로드라, 그 안의 값은 숨겨진 것이 아니라 번거롭게 된 것일 뿐이다 — docs/09-스트리밍-키-설계.md.
/// </summary>
internal static class PartnerKeys
{
    private static string? Env(string key) => CredentialStore.Get(key);

    /// <summary>덮어쓴 값 → 빌드 값 → 기본값 순으로 첫 번째 비어 있지 않은 값. 순수 함수라 테스트로 고정한다.</summary>
    internal static (string? Value, KeySource Source) Resolve(string? overrideValue, string? buildValue, string? defaultValue = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideValue)) return (overrideValue.Trim(), KeySource.Override);
        if (!string.IsNullOrWhiteSpace(buildValue)) return (buildValue.Trim(), KeySource.Build);
        if (!string.IsNullOrWhiteSpace(defaultValue)) return (defaultValue.Trim(), KeySource.Default);
        return (null, KeySource.None);
    }

    // ── Tidal ─ PKCE 공개 클라이언트. 숨길 비밀이 없다 ──────────────────────────

    private static (string? Value, KeySource Source) TidalId()
        => Resolve(Env("MONO_TIDAL_CLIENT_ID"), BuildKeys.TidalClientId, TidalClient.EmbeddedClientId);

    public static string TidalClientId => TidalId().Value!;

    /// <summary>
    /// 사용자가 준 Secret. 빌드는 Tidal Secret 을 넣지 않는다 — PKCE 로 교환하므로 필요 없다.
    /// 사용자 지정 Client ID 가 기밀 클라이언트일 때를 위해서만 남겨 둔다.
    /// </summary>
    public static string? TidalClientSecret => Env("MONO_TIDAL_CLIENT_SECRET");

    // ── Qobuz ─ App Secret 이 있다 ──────────────────────────────────────────────

    private static (string? Value, KeySource Source) QobuzId()
        => Resolve(Env("MONO_QOBUZ_APP_ID"), BuildKeys.QobuzAppId);

    public static string? QobuzAppId => QobuzId().Value;

    /// <summary>
    /// App Secret 은 App ID 와 같은 출처에서만 가져온다. 사용자가 자기 App ID 를 줬는데 우리 빌드의 Secret 이
    /// 그 ID 와 함께 Qobuz 로 나가면 엉뚱한 곳에 우리 비밀을 보내는 셈이다.
    /// </summary>
    public static string? QobuzAppSecret
        => SecretFor(QobuzId().Source, Env("MONO_QOBUZ_APP_SECRET"), BuildKeys.QobuzAppSecret);

    /// <summary>App ID 가 어느 출처인지에 따라 같은 출처의 Secret 만 돌려준다. 순수 함수라 테스트로 고정한다.</summary>
    internal static string? SecretFor(KeySource appIdSource, string? overrideSecret, string? buildSecret) => appIdSource switch
    {
        KeySource.Override => string.IsNullOrWhiteSpace(overrideSecret) ? null : overrideSecret.Trim(),
        KeySource.Build => string.IsNullOrWhiteSpace(buildSecret) ? null : buildSecret.Trim(),
        _ => null
    };

    /// <summary>키가 없는 빌드에서도 개발자가 시험용 곡으로 방 기능을 돌려 볼 수 있게 여는 스위치.</summary>
    public static bool QobuzDemoAllowed
        => string.Equals(Env("MONO_QOBUZ_DEMO"), "1", StringComparison.OrdinalIgnoreCase);

    // ── 화면에 알려 주는 값 ─────────────────────────────────────────────────────

    public static KeySource SourceOf(StreamingProvider provider) => provider switch
    {
        StreamingProvider.Tidal => TidalId().Source,
        StreamingProvider.Qobuz => QobuzId().Source,
        _ => KeySource.None
    };

    /// <summary>Control UI 로 나가는 이름. 값은 절대 나가지 않고 출처만 나간다.</summary>
    public static string Label(KeySource source) => source switch
    {
        KeySource.Override => "override",
        KeySource.Build => "build",
        KeySource.Default => "default",
        _ => "none"
    };
}
