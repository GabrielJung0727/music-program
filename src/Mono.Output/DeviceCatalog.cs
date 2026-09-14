using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using NAudio.CoreAudioApi;

namespace Mono.Output;

/// <summary>이 PC 에 실제로 달린 출력 하나.</summary>
public sealed record DeviceListing(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("driverType")] string DriverType,
    [property: JsonPropertyName("architecture")] string Architecture,
    [property: JsonPropertyName("subtitle")] string Subtitle,
    [property: JsonPropertyName("specs")] string[] Specs,
    [property: JsonPropertyName("dsdSupport")] string? DsdSupport,
    [property: JsonPropertyName("bitPerfectVerified")] bool BitPerfectVerified,
    [property: JsonPropertyName("isDefault")] bool IsDefault);

/// <summary>
/// 설치된 출력 장치 목록. 마법사의 "Detected Hardware" 가 이걸 그린다.
///
/// 열거는 Output 프로세스가 한다. NAudio 를 들고 있는 게 여기뿐이고, 셸이 같은 열거를
/// 한 벌 더 들면 "마법사에는 보이는데 재생은 안 되는 장치" 같은 어긋남이 생긴다.
/// 셸은 <c>--list-devices</c> 로 이 프로세스를 잠깐 띄워 stdout 한 줄을 읽는다.
/// </summary>
public static class DeviceCatalog
{
    public static List<DeviceListing> Enumerate()
    {
        var list = new List<DeviceListing>();

        // ASIO 가 먼저다 — 배타 경로가 있는 장치를 위에 보여 주는 편이 고르기 쉽다.
        foreach (var driver in AsioAudioRenderer.DriverNames())
        {
            list.Add(new DeviceListing(
                Id: "asio:" + driver,
                Name: driver,
                DriverType: "ASIO",
                Architecture: "ASIO Direct",
                Subtitle: "ASIO Direct · 드라이버 버퍼 직접 접근",
                Specs: ["32-Bit", "DSD Capable"],
                DsdSupport: "DSD",
                BitPerfectVerified: true,
                IsDefault: false));
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? defaultId = null;
            try { defaultId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; }
            catch (Exception ex) when (ex is COMException or InvalidOperationException) { /* 기본 장치가 없을 수도 있다 */ }

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                var format = SafeMixFormat(device);
                var rate = format?.SampleRate ?? 48000;
                var depth = format?.BitsPerSample ?? 24;

                list.Add(new DeviceListing(
                    Id: "wasapi:" + device.ID,
                    Name: device.FriendlyName,
                    DriverType: "WASAPI_EXCLUSIVE",
                    Architecture: "WASAPI Exclusive",
                    Subtitle: $"Windows WASAPI Endpoint · {device.DataFlow}",
                    Specs: [$"{depth}-Bit", $"PCM {rate / 1000}kHz"],
                    DsdSupport: null,
                    // 배타 모드를 실제로 열어 보기 전까지는 확언할 수 없다. 여는 건 재생 시작할 때다.
                    BitPerfectVerified: false,
                    IsDefault: device.ID == defaultId));
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // 열거가 통째로 실패하면 ASIO 목록만이라도 돌려준다.
        }

        return list;
    }

    public static string ToJson() =>
        JsonSerializer.Serialize(Enumerate(), new JsonSerializerOptions { WriteIndented = false });

    private static NAudio.Wave.WaveFormat? SafeMixFormat(MMDevice device)
    {
        try { return device.AudioClient.MixFormat; }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { return null; }
    }
}
