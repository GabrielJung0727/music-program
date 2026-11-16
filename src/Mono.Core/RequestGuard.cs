using System.Net;

namespace Mono.Core;

/// <summary>
/// HTTP 7702 로 들어오는 요청을 걸러 낸다. 인증이 없는 로컬 서버가 브라우저에게 열려 있을 때
/// 생기는 세 가지 길을 막는다.
///
/// 1. 다른 사이트의 페이지가 ws://127.0.0.1:7702/ws/control 을 여는 것. WebSocket 에는 CORS 가
///    적용되지 않아서, Origin 을 서버가 직접 보지 않으면 방문한 아무 페이지나 Core 에 명령을 낸다.
/// 2. DNS 리바인딩. 공격자 도메인이 127.0.0.1 을 가리키게 바꾸면 브라우저는 그 페이지를
///    같은 출처로 취급한다. 이때 Host 헤더에는 도메인 이름이 실려 오므로 이름을 거절하면 끝난다.
/// 3. ControlUrl 을 0.0.0.0 으로 열었을 때 LAN 의 다른 기기가 REST 를 그대로 쓰는 것.
/// </summary>
public static class RequestGuard
{
    /// <summary>
    /// Host 헤더가 IP 주소이거나 localhost 인가. 그 밖의 이름은 <paramref name="extraHosts"/> 에
    /// 적어 둔 것만 받는다(Mono:AllowedHosts).
    /// </summary>
    public static bool IsAllowedHost(string? host, IReadOnlyCollection<string> extraHosts)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var name = host.Trim().Trim('[', ']');
        if (name.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (IPAddress.TryParse(name, out _)) return true;
        return extraHosts.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Origin 헤더가 받아도 되는 출처인가.
    ///
    /// 헤더가 없으면 브라우저의 교차 출처 요청이 아니다(네이티브 클라이언트, 같은 출처의 GET).
    /// 있으면 이 PC 의 루프백 출처이거나(개발 서버 포함), 요청이 온 Host 와 정확히 같아야 한다.
    /// </summary>
    public static bool IsAllowedOrigin(string? origin, string? requestHost, int? requestPort)
    {
        if (string.IsNullOrWhiteSpace(origin)) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;

        var originHost = uri.Host.Trim('[', ']');
        if (originHost.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (IPAddress.TryParse(originHost, out var ip) && IPAddress.IsLoopback(ip)) return true;

        var host = requestHost?.Trim().Trim('[', ']');
        return !string.IsNullOrEmpty(host)
               && originHost.Equals(host, StringComparison.OrdinalIgnoreCase)
               && uri.Port == (requestPort ?? (uri.Scheme == "https" ? 443 : 80));
    }

    /// <summary>
    /// 루프백이 아닌 곳에서 온 요청에 열어 줄 경로인가.
    /// 화면 번들, 페어링을 거치는 컨트롤 소켓, 커버 아트, 상태 확인, 스트림 프록시만 남긴다.
    /// 설정을 읽고 쓰는 /api/setup, 초대 코드가 실린 /api/rooms 같은 것은 이 PC 에서만 쓴다.
    /// </summary>
    public static bool IsOpenToRemote(string method, string path)
    {
        var read = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
        if (path.StartsWith("/ws/control", StringComparison.OrdinalIgnoreCase)) return true;
        if (!read) return false;
        if (path.StartsWith("/api/art/", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWith("/api/stream/", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Equals("/api/health", StringComparison.OrdinalIgnoreCase)) return true;
        return !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
               && !path.StartsWith("/hub", StringComparison.OrdinalIgnoreCase)
               && !path.StartsWith("/oauth/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLoopback(IPAddress? remote)
        => remote is null || IPAddress.IsLoopback(remote) || (remote.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(remote.MapToIPv4()));

    /// <summary>정적 파일과 엔드포인트보다 앞에 건다.</summary>
    public static IApplicationBuilder UseRequestGuard(this IApplicationBuilder app, IReadOnlyCollection<string> extraHosts)
        => app.Use(async (http, next) =>
        {
            var request = http.Request;
            string? refused =
                !IsAllowedHost(request.Host.Host, extraHosts) ? "host not allowed"
                : !IsAllowedOrigin(request.Headers.Origin.ToString(), request.Host.Host, request.Host.Port) ? "origin not allowed"
                : !IsLoopback(http.Connection.RemoteIpAddress) && !IsOpenToRemote(request.Method, request.Path.Value ?? "/") ? "local only"
                : null;

            if (refused is not null)
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                await http.Response.WriteAsync(refused);
                return;
            }

            await next();
        });
}
