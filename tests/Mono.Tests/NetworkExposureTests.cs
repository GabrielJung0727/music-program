using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Mono.Core;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 7700(Control TCP)·7701(MATP) 은 인증이 약하다. 7701 에는 아예 없다.
/// 그래서 기본은 이 PC 에서만 받고, 다른 PC 의 출력 워커를 쓰는 사람만 LAN 에 연다.
/// </summary>
public class NetworkExposureTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value))
            .Build();

    [Fact]
    public void ByDefaultTheRawPortsListenOnLoopbackOnly()
    {
        Assert.False(NetworkExposure.AllowRemoteOutputs(Config(), new JsonObject()));
        Assert.Equal(IPAddress.Loopback, NetworkExposure.BindAddress(allowRemote: false));
    }

    [Fact]
    public void TheSettingInSetupOpensThemToTheLan()
    {
        var setup = new JsonObject { ["allowRemoteOutputs"] = true };
        Assert.True(NetworkExposure.AllowRemoteOutputs(Config(), setup));
        Assert.Equal(IPAddress.Any, NetworkExposure.BindAddress(allowRemote: true));
    }

    [Fact]
    public void AnExplicitConfigValueWinsOverTheSetupFile()
    {
        var setup = new JsonObject { ["allowRemoteOutputs"] = true };
        Assert.False(NetworkExposure.AllowRemoteOutputs(Config(("Mono:AllowRemoteOutputs", "false")), setup));
        Assert.True(NetworkExposure.AllowRemoteOutputs(Config(("Mono:AllowRemoteOutputs", "true")), new JsonObject()));
    }

    [Theory]
    [InlineData("\"yes\"")]
    [InlineData("1")]
    [InlineData("null")]
    public void AnythingButTrueInTheSetupFileKeepsThePortsLocal(string json)
    {
        var setup = new JsonObject { ["allowRemoteOutputs"] = JsonNode.Parse(json) };
        Assert.False(NetworkExposure.AllowRemoteOutputs(Config(), setup));
    }
}
