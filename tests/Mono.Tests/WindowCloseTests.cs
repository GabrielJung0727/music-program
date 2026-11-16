using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 창의 닫기(X)를 누르면 Mono 가 끝나야 한다. 예전에는 늘 트레이로 숨어 음악이 계속 났고,
/// 사용자는 껐다고 생각했다. 트레이에 남기는 것은 설정으로 고른 사람만이다.
/// </summary>
public class WindowCloseTests
{
    [Fact]
    public void ClosingTheWindowQuitsByDefault()
        => Assert.False(WindowClose.HideToTray(userClosing: true, quitting: false, closeToTray: WindowClose.ParseSetting(null)));

    [Fact]
    public void TheSettingKeepsMonoRunningInTheTray()
        => Assert.True(WindowClose.HideToTray(userClosing: true, quitting: false, closeToTray: true));

    [Fact]
    public void QuitFromTheTrayMenuAlwaysQuits()
        => Assert.False(WindowClose.HideToTray(userClosing: true, quitting: true, closeToTray: true));

    [Fact]
    public void ShutdownOrLogoffAlwaysQuits()
        => Assert.False(WindowClose.HideToTray(userClosing: false, quitting: false, closeToTray: true));

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("yes", false)]
    public void OnlyAnExplicitOneTurnsTheSettingOn(string? stored, bool expected)
        => Assert.Equal(expected, WindowClose.ParseSetting(stored));
}
