namespace Mono.Shared;

public static class UpdateCheckErrors
{
    public static string Describe(Exception ex)
    {
        var msg = ex.Message ?? "";
        if (msg.Contains("404", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Not Found", StringComparison.OrdinalIgnoreCase))
        {
            return "업데이트 목록을 찾지 못했습니다. GitHub 저장소가 비공개이면 이 PC에서 gh 로그인이 되어 있어야 합니다.";
        }

        if (msg.Contains("401", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("403", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return "업데이트 권한이 없습니다. GitHub 토큰 또는 gh 로그인을 확인하세요.";
        }

        return "업데이트 확인 실패: " + msg;
    }
}
