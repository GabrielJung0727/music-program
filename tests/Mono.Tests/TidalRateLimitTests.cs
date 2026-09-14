using Mono.Core;
using System.Net;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// Tidal 검색은 "국가 × URL 모양" 조합을 순서대로 훑는다(최대 3 × 3 = 9회).
/// 라이브러리 가져오기는 그런 검색을 시드 4개에 대해 연달아 돌리므로
/// 한 번에 40회 가까운 요청이 나간다. 429 를 만나도 그냥 다음 조합으로 넘어가
/// 남은 요청까지 전부 429 를 받고, 결과적으로 카탈로그가 조용히 비게 된다.
/// </summary>
public class TidalRateLimitTests
{
    /// <summary>요청을 세고 정해진 응답을 돌려주는 가짜 핸들러.</summary>
    private sealed class FakeHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public List<string> Urls { get; } = [];

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            return respond(++Calls);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(Send(request, ct));
    }

    private static HttpResponseMessage TooManyRequests(TimeSpan? retryAfter = null)
    {
        var res = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"errors\":[{\"detail\":\"rate limit\"}]}")
        };
        if (retryAfter is { } wait)
        {
            res.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(wait);
        }

        return res;
    }

    private static HttpResponseMessage Empty()
        => new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[],\"included\":[]}") };

    /// <summary>
    /// 429 를 받으면 그 질의는 즉시 포기해야 한다. 남은 조합을 계속 두드려 봐야
    /// 전부 429 를 받을 뿐이고, 그 사이 한도는 더 깊이 파인다.
    /// </summary>
    [Fact]
    public void SearchStopsAfterRateLimitInsteadOfWalkingEveryFallback()
    {
        var handler = new FakeHandler(_ => TooManyRequests());
        var client = new TidalClient(new HttpClient(handler));

        var hits = client.Search("token", "Daft Punk", "KR", 12);

        Assert.Empty(hits);
        Assert.True(
            handler.Calls <= 2,
            $"429 이후 조합을 계속 시도했다 — 요청 {handler.Calls}회:\n" + string.Join("\n", handler.Urls));
    }

    /// <summary>429 는 호출자가 알아볼 수 있어야 한다 — 곡이 없는 것과 다른 상황이다.</summary>
    [Fact]
    public void RateLimitIsReportedDistinctlyFromEmptyResults()
    {
        var limited = new TidalClient(new HttpClient(new FakeHandler(_ => TooManyRequests())));
        limited.Search("token", "Miles Davis", "KR", 12);
        Assert.True(limited.RateLimited);
        Assert.Contains("429", limited.LastError);

        var empty = new TidalClient(new HttpClient(new FakeHandler(_ => Empty())));
        empty.Search("token", "Miles Davis", "KR", 12);
        Assert.False(empty.RateLimited);
    }

    /// <summary>
    /// Retry-After 가 짧게 오면 한 번은 기다렸다 다시 시도한다.
    /// 일시적인 한도 때문에 가져오기 전체가 빈손이 되면 안 된다.
    /// </summary>
    [Fact]
    public void ShortRetryAfterIsHonouredOnce()
    {
        var handler = new FakeHandler(call => call == 1
            ? TooManyRequests(TimeSpan.FromMilliseconds(20))
            : Empty());
        var client = new TidalClient(new HttpClient(handler));

        client.Search("token", "Hiromi", "KR", 12);

        Assert.True(handler.Calls >= 2, "Retry-After 를 무시하고 재시도하지 않았다");
        Assert.False(client.RateLimited);
    }

    /// <summary>
    /// Retry-After 가 너무 길면 기다리지 않는다 — 사용자를 몇 분씩 세워 둘 수는 없다.
    /// 대신 한도에 걸렸다고 보고하고 끝낸다.
    /// </summary>
    [Fact]
    public void LongRetryAfterIsNotWaitedOut()
    {
        var handler = new FakeHandler(_ => TooManyRequests(TimeSpan.FromMinutes(5)));
        var client = new TidalClient(new HttpClient(handler));

        var started = DateTimeOffset.UtcNow;
        client.Search("token", "Kind of Blue", "KR", 12);
        var elapsed = DateTimeOffset.UtcNow - started;

        Assert.True(elapsed < TimeSpan.FromSeconds(2), $"긴 Retry-After 를 실제로 기다렸다: {elapsed}");
        Assert.True(client.RateLimited);
    }
}
