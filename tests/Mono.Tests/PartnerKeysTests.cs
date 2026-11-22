using System.Text.Json;
using Mono.Core;
using Mono.Protocol;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 스트리밍 파트너 키를 다루는 규칙 — docs/09-스트리밍-키-설계.md.
/// 환경변수를 건드리지 않는다: 테스트가 병렬로 도는 동안 다른 테스트가 같은 변수를 읽는다.
/// 그래서 해석 규칙은 순수 함수로, 나머지는 "어느 PC 에서 돌려도 같은 결과"가 나오는 것만 확인한다.
/// </summary>
public class PartnerKeysTests
{
    // ── 해석 순서 ────────────────────────────────────────────────────────────────

    [Fact]
    public void OverrideBeatsBuildBeatsDefault()
    {
        Assert.Equal(("mine", KeySource.Override), PartnerKeys.Resolve("mine", "built", "dflt"));
        Assert.Equal(("built", KeySource.Build), PartnerKeys.Resolve(null, "built", "dflt"));
        Assert.Equal(("dflt", KeySource.Default), PartnerKeys.Resolve(null, "", "dflt"));
        Assert.Equal((null, KeySource.None), PartnerKeys.Resolve(null, "", null));
    }

    [Fact]
    public void BlankValuesDoNotCountAsKeys()
    {
        // 빌드가 키 없이 돌면 빈 문자열이 들어온다. 그것이 "키 있음"으로 읽히면 안 된다.
        Assert.Equal((null, KeySource.None), PartnerKeys.Resolve("  ", "\t", " "));
        Assert.Equal(("x", KeySource.Override), PartnerKeys.Resolve("  x ", "built"));
    }

    [Fact]
    public void SecretOnlyComesFromTheSameLayerAsTheAppId()
    {
        // 사용자가 자기 App ID 를 줬다면, 우리 빌드의 Secret 이 그 ID 와 함께 Qobuz 로 나가면 안 된다.
        Assert.Equal("user-secret", PartnerKeys.SecretFor(KeySource.Override, "user-secret", "build-secret"));
        Assert.Null(PartnerKeys.SecretFor(KeySource.Override, null, "build-secret"));
        Assert.Equal("build-secret", PartnerKeys.SecretFor(KeySource.Build, "user-secret", "build-secret"));
        Assert.Null(PartnerKeys.SecretFor(KeySource.None, "user-secret", "build-secret"));
        Assert.Null(PartnerKeys.SecretFor(KeySource.Default, "user-secret", "build-secret"));
    }

    [Fact]
    public void TidalAlwaysHasAClientIdBecausePkceNeedsNoSecret()
    {
        Assert.False(string.IsNullOrWhiteSpace(PartnerKeys.TidalClientId));
        Assert.NotEqual(KeySource.None, PartnerKeys.SourceOf(StreamingProvider.Tidal));
    }

    // ── Output 에는 키를 싣지 않는다 ─────────────────────────────────────────────

    [Fact]
    public void OutputProcessGetsNoPartnerEnvironment()
    {
        Assert.Empty(PartnerEnv.For(ProcessKind.Output));
    }

    [Fact]
    public void CoreProcessGetsTheKeyNamesAndNothingElse()
    {
        var names = PartnerEnv.For(ProcessKind.Core);
        Assert.Contains("MONO_QOBUZ_APP_SECRET", names);
        Assert.Contains("MONO_QOBUZ_APP_ID", names);
        Assert.Contains("MONO_TIDAL_CLIENT_ID", names);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, n => Assert.StartsWith("MONO_", n));
        // 빌드 주입용 이름은 런타임 이름과 달라야 한다 — 개발자의 개인 키가 빌드에 구워지지 않게.
        Assert.DoesNotContain(names, n => n.StartsWith("MONO_BUILD_", StringComparison.Ordinal));
    }

    // ── 로그 ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GET https://www.qobuz.com/api.json/0.2/user/login?app_id=1&app_secret=S3CR3T&code=ok", "S3CR3T")]
    [InlineData("track/getFileUrl?user_auth_token=TOK123&track_id=9", "TOK123")]
    [InlineData("form access_token=AAA.BBB-CCC&refresh_token=RRR111", "RRR111")]
    [InlineData("{\"access_token\":\"eyJhbGciOi\",\"expires_in\":3600}", "eyJhbGciOi")]
    [InlineData("Authorization: Bearer abc.def-ghi_123", "abc.def-ghi_123")]
    [InlineData("code_verifier=VERIFIER_xyz", "VERIFIER_xyz")]
    public void LogRedactHidesTheValue(string line, string secret)
    {
        var redacted = LogRedact.Line(line);
        Assert.DoesNotContain(secret, redacted);
        Assert.Contains("***", redacted);
    }

    [Fact]
    public void LogRedactKeepsTheRestOfTheLineReadable()
    {
        var redacted = LogRedact.Line("429 https://x/api?app_id=77&app_secret=ZZZ&limit=20 too many");
        Assert.Contains("app_id=77", redacted);
        Assert.Contains("limit=20", redacted);
        Assert.Contains("too many", redacted);
        Assert.DoesNotContain("ZZZ", redacted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("clock offset=-0.16ms jitter=0.33ms rtt=1.96ms")]
    [InlineData("started Mono.Core")]
    public void LogRedactLeavesOrdinaryLinesAlone(string line)
    {
        Assert.Equal(line, LogRedact.Line(line));
    }

    [Fact]
    public void LogRedactToleratesNull()
    {
        Assert.Equal("", LogRedact.Line(null));
    }

    // ── 화면에 나가는 상태 ──────────────────────────────────────────────────────

    private static (CommandProcessor Commands, StreamingHub Streaming) NewStack()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        var history = new HistoryStore(Path.Combine(dir, "h.db"));
        var streaming = new StreamingHub(catalog, Path.Combine(dir, "streaming.json"));
        var endpoints = new EndpointRegistry(Path.Combine(dir, "e.db"));
        var zones = new ZoneRegistry(Path.Combine(dir, "z.db"));
        var rooms = new RoomManager(catalog, history, streaming, endpoints);
        var scanner = new LibraryScanner(catalog, new ArtworkService(Path.Combine(dir, "art")));
        var pairing = new PairingService();
        var wiki = new WikipediaService(new HttpClient(), Path.Combine(dir, "wiki"));
        var backups = new BackupService(dir);
        var commands = new CommandProcessor(
            rooms, catalog, scanner, streaming, pairing, history, endpoints, zones, wiki, backups,
            [Path.Combine(dir, "library")]);
        return (commands, streaming);
    }

    private sealed record AccountView(
        StreamingProvider Provider,
        bool Connected,
        bool LiveSdk,
        bool Available,
        string? KeySource);

    private static List<AccountView> Accounts(MonoMessage msg)
        => JsonSerializer.Deserialize<List<AccountView>>(msg.Body!, LineFraming.JsonOptions)!;

    [Fact]
    public void AccountListNamesEveryServiceEvenWhenNothingIsLinked()
    {
        var (commands, _) = NewStack();
        var reply = commands.Execute("peer", new MonoMessage { Type = MessageTypes.StreamingAccounts }, "Listener").Direct!;
        var accounts = Accounts(reply);

        Assert.Equal(
            [StreamingProvider.Tidal, StreamingProvider.Qobuz],
            accounts.Select(a => a.Provider).ToArray());
        Assert.All(accounts, a => Assert.False(a.Connected));
        Assert.True(accounts.Single(a => a.Provider == StreamingProvider.Tidal).Available);
        Assert.All(accounts, a => Assert.Contains(a.KeySource, new[] { "override", "build", "default", "none" }));
    }

    [Fact]
    public void AccountListNeverCarriesTheKeyValues()
    {
        var (commands, _) = NewStack();
        var reply = commands.Execute("peer", new MonoMessage { Type = MessageTypes.StreamingAccounts }, "Listener").Direct!;

        // 출처만 나간다. 값이 나가면 이 문자열들이 본문에 있을 것이다.
        Assert.DoesNotContain(TidalClient.EmbeddedClientId, reply.Body!);
        if (PartnerKeys.QobuzAppId is { Length: > 0 } id) Assert.DoesNotContain(id, reply.Body!);
        if (PartnerKeys.QobuzAppSecret is { Length: > 0 } secret) Assert.DoesNotContain(secret, reply.Body!);
    }

    // ── 키 없는 빌드의 Qobuz ────────────────────────────────────────────────────

    [Fact]
    public void QobuzIsRefusedWhenTheBuildHasNoKey()
    {
        var (commands, streaming) = NewStack();
        // 키를 가진 개발 PC 에서는 이 시나리오가 성립하지 않는다.
        if (streaming.IsAvailable(StreamingProvider.Qobuz)) return;

        foreach (var attempt in new[]
                 {
                     new MonoMessage { Type = MessageTypes.LinkStreaming, Provider = StreamingProvider.Qobuz, Token = "any-token" },
                     new MonoMessage { Type = MessageTypes.LinkStreaming, Provider = StreamingProvider.Qobuz, Text = "oauth" }
                 })
        {
            var reply = commands.Execute("peer", attempt, "Listener").Direct!;
            Assert.False(reply.Ok);
            Assert.Contains("준비 중", reply.Error);
            Assert.False(streaming.IsConnected(StreamingProvider.Qobuz));
        }
    }

    [Fact]
    public void UnlinkingStaysPossibleEvenWhenLinkingIsClosed()
    {
        var (commands, streaming) = NewStack();
        // 키가 있던 시절에 연결해 둔 사용자가, 키 없는 빌드로 업데이트한 경우.
        streaming.Link(StreamingProvider.Qobuz, "old-token", "Qobuz");
        Assert.True(streaming.IsConnected(StreamingProvider.Qobuz));

        commands.Execute("peer", new MonoMessage { Type = MessageTypes.LinkStreaming, Provider = StreamingProvider.Qobuz }, "Listener");

        Assert.False(streaming.IsConnected(StreamingProvider.Qobuz));
    }

    [Fact]
    public void TidalLinkingIsNotAffected()
    {
        var (commands, streaming) = NewStack();

        var result = commands.Execute("peer", new MonoMessage
        {
            Type = MessageTypes.LinkStreaming,
            Provider = StreamingProvider.Tidal,
            Token = "token-abc"
        }, "Listener");

        Assert.True(result.Direct!.Ok);
        Assert.True(streaming.IsConnected(StreamingProvider.Tidal));
    }
}
