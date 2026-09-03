using Mono.Shared;
using Xunit;

namespace Mono.Tests;

public class UpdateCheckTests
{
    [Fact]
    public void PrivateRepo404IsExplained()
    {
        var text = UpdateCheckErrors.Describe(
            new HttpRequestException("Response status code does not indicate success: 404 (Not Found)."));
        Assert.Contains("비공개", text);
        Assert.DoesNotContain("404", text);
    }

    [Fact]
    public void UnauthorizedIsExplained()
    {
        var text = UpdateCheckErrors.Describe(new HttpRequestException("401 (Unauthorized)"));
        Assert.Contains("권한", text);
    }
}
