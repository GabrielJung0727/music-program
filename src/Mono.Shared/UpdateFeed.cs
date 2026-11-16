namespace Mono.Shared;

/// <summary>
/// 업데이트를 어디서 받을지. 기본은 GitHub 릴리스다.
///
/// 예전에는 <c>MONO_UPDATE_FEED</c> 환경 변수나 prefs.ini 의 <c>update_feed</c> 로 주소를 아무 곳으로나 바꿀 수 있었다.
/// 그 값을 적을 수 있는 프로그램이면 다음 업데이트로 아무 실행 파일이나 깔게 만들 수 있다는 뜻이다.
/// 로컬 피드로 업데이트를 시험하는 일은 개발 빌드에서만 하므로, 바꾸는 길은 그쪽에만 남긴다.
/// </summary>
public static class UpdateFeed
{
    /// <summary>
    /// 기본 피드 대신 쓸 주소. 없으면 null — GitHub 릴리스를 쓴다.
    /// <paramref name="allowOverride"/> 는 개발 빌드에서만 true 다(<c>AppUpdater</c> 의 <c>#if DEBUG</c>).
    /// </summary>
    public static string? Override(string? environment, string? prefs, bool allowOverride)
    {
        if (!allowOverride) return null;
        if (!string.IsNullOrWhiteSpace(environment)) return environment.Trim();
        if (!string.IsNullOrWhiteSpace(prefs)) return prefs.Trim();
        return null;
    }
}
