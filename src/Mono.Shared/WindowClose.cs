namespace Mono.Shared;

/// <summary>
/// 창의 닫기(X)를 눌렀을 때 Mono 를 끝낼지, 트레이로 숨길지.
///
/// 기본은 끝낸다. 예전에는 X 가 늘 트레이로 숨겨서 Core 와 출력 워커가 계속 돌고 음악이 이어졌는데,
/// 그걸 알려 주는 곳이 없어 사용자는 껐다고 생각했다. 트레이에서 계속 듣고 싶은 사람은 설정의
/// "창을 닫으면 트레이로 숨기기"를 켠다(prefs.ini 의 <see cref="PrefKey"/>).
/// </summary>
public static class WindowClose
{
    public const string PrefKey = "window.closeToTray";

    /// <summary>저장된 값은 "1" 일 때만 켜짐이다. 없거나 다른 값이면 기본(끝내기)이다.</summary>
    public static bool ParseSetting(string? stored) => stored == "1";

    /// <summary>
    /// 이번 닫기를 취소하고 트레이로 숨겨야 하는가.
    /// 트레이 메뉴의 "종료"(<paramref name="quitting"/>)와, 사용자가 아닌 쪽의 닫기(Windows 종료 · 로그오프 등)는
    /// 설정과 상관없이 끝낸다.
    /// </summary>
    public static bool HideToTray(bool userClosing, bool quitting, bool closeToTray)
        => userClosing && !quitting && closeToTray;
}
