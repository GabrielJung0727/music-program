using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 업데이트 주소를 환경 변수나 prefs.ini 로 바꿀 수 있으면, 그 값을 적을 수 있는 무엇이든
/// 다음 업데이트로 아무 실행 파일이나 깔게 만들 수 있다. 바꾸는 길은 개발 빌드에만 남긴다.
/// </summary>
public class UpdateFeedTests
{
    [Fact]
    public void AReleaseBuildIgnoresEveryOverride()
    {
        Assert.Null(UpdateFeed.Override(environment: @"C:\feed", prefs: "https://evil.example/feed", allowOverride: false));
    }

    [Fact]
    public void ADevelopmentBuildTakesTheEnvironmentFirst()
    {
        Assert.Equal(@"C:\feed", UpdateFeed.Override(environment: @" C:\feed ", prefs: "https://other", allowOverride: true));
        Assert.Equal("https://other", UpdateFeed.Override(environment: null, prefs: " https://other ", allowOverride: true));
    }

    [Fact]
    public void BlankValuesMeanTheDefaultGitHubFeed()
    {
        Assert.Null(UpdateFeed.Override(environment: "  ", prefs: "", allowOverride: true));
    }
}
