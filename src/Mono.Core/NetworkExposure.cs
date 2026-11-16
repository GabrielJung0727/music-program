using System.Net;
using System.Text.Json.Nodes;

namespace Mono.Core;

/// <summary>
/// 7700(Control TCP)과 7701(MATP)을 어디에 열지 정한다.
///
/// 두 포트는 인증이 약하다. 7701 에는 아예 없어서, LAN 의 누구든 출력 워커인 척 붙어
/// 방의 스냅샷과 오디오 복호 키를 받을 수 있다. 그래서 기본은 이 PC 에서만 받는다.
/// 다른 PC 의 출력 워커(또는 Mono.Cli)를 쓰는 사람만 "다른 PC 의 출력 허용"을 켜서 LAN 에 연다.
///
/// 값은 두 곳에서 온다. 화면이 쓰는 setup.json 의 <c>allowRemoteOutputs</c>, 그리고 그보다 앞서는
/// 설정 키 <c>Mono:AllowRemoteOutputs</c>(환경 변수 <c>Mono__AllowRemoteOutputs</c>). 리스너는 Core 가
/// 뜰 때 한 번 열리므로, 바꾼 값은 Core 를 다시 시작해야 적용된다.
/// </summary>
public static class NetworkExposure
{
    public const string ConfigKey = "Mono:AllowRemoteOutputs";
    public const string SetupKey = "allowRemoteOutputs";

    public static bool AllowRemoteOutputs(IConfiguration config, JsonObject setup)
    {
        if (bool.TryParse(config[ConfigKey], out var configured)) return configured;

        // setup.json 은 화면이 쓰는 자유 형식이다. true 가 아닌 것은 전부 "열지 않음"으로 읽는다.
        return setup[SetupKey] is JsonValue value
               && value.TryGetValue<bool>(out var allowed)
               && allowed;
    }

    public static IPAddress BindAddress(bool allowRemote) => allowRemote ? IPAddress.Any : IPAddress.Loopback;
}

/// <summary>Core 가 뜰 때 정해진 노출 범위. 리스너와 상태 응답이 같은 값을 본다.</summary>
public sealed record TcpExposure(bool AllowRemote)
{
    public IPAddress BindAddress => NetworkExposure.BindAddress(AllowRemote);
}
