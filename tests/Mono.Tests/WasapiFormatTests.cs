using System.Runtime.InteropServices;
using Mono.Output;
using NAudio.Wave;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 장치에 넘기는 WaveFormat 계약.
///
/// 24bit 를 WAVEFORMATEX 로 요청하면 3바이트 패킹인지 4바이트 컨테이너인지가 정해지지 않고,
/// 드라이버는 그 모호한 요청에도 "지원한다"고 답한 뒤 배타 모드에서 4바이트씩 읽어 갈 수 있다.
/// 그러면 큐가 실시간의 4/3 속도로 빠지고 모든 샘플 경계가 밀려 음악 전체가 잡음이 된다.
/// (NVIDIA HDMI 엔드포인트에서 드레인 1.35배로 실측했다.)
/// </summary>
public class WasapiFormatTests
{
    [Fact]
    public void TwentyFourBitIsRequestedAsExtensibleNeverAsBareWaveFormatEx()
    {
        var format = WasapiOutputDevice.BuildFormat(44100, 24, 2);

        Assert.Equal(WaveFormatEncoding.Extensible, format.Encoding);
        var extensible = Assert.IsType<ExtensiblePcmWaveFormat>(format);
        Assert.Equal(24, extensible.ValidBitsPerSample);
        Assert.Equal(32, format.BitsPerSample);
        Assert.Equal(8, format.BlockAlign);
        Assert.Equal(44100 * 8, format.AverageBytesPerSecond);
    }

    /// <summary>16bit 는 컨테이너가 모호하지 않다 — 굳이 확장 규격으로 바꿔 호환성을 잃을 이유가 없다.</summary>
    [Fact]
    public void SixteenBitStaysAPlainWaveFormat()
    {
        var format = WasapiOutputDevice.BuildFormat(48000, 16, 2);

        Assert.Equal(WaveFormatEncoding.Pcm, format.Encoding);
        Assert.Equal(16, format.BitsPerSample);
        Assert.Equal(4, format.BlockAlign);
    }

    /// <summary>
    /// WAVEFORMATEXTENSIBLE 은 네이티브로 마샬링돼 드라이버에 그대로 전달된다.
    /// 구조체 크기가 어긋나면 cbSize 뒤의 필드가 쓰레기로 읽혀 조용히 잘못된 규격이 열린다.
    /// </summary>
    [Fact]
    public void ExtensibleFormatMarshalsToTheNativeStructSize()
    {
        var format = new ExtensiblePcmWaveFormat(96000, 32, 24, 2);

        // WAVEFORMATEX(18) + cbSize 내용 22 = 40 바이트
        Assert.Equal(40, Marshal.SizeOf(format));
    }

    /// <summary>24bit 패킹 샘플은 32bit 컨테이너의 상위에 놓여야 한다. 하위에 놓으면 −48dB 로 줄어든다.</summary>
    [Fact]
    public void WideningPlacesTheSampleInTheTopOfTheContainer()
    {
        // 리틀엔디안 24bit: 0x7F5A3C → 컨테이너 0x7F5A3C00
        byte[] packed = [0x3C, 0x5A, 0x7F, 0x00, 0x00, 0x80];

        var wide = WasapiOutputDevice.Widen24To32(packed);

        Assert.Equal(8, wide.Length);
        Assert.Equal(0x7F5A3C00, BitConverter.ToInt32(wide, 0));
        Assert.Equal(unchecked((int)0x80000000), BitConverter.ToInt32(wide, 4));
    }

    /// <summary>부호가 살아 있어야 한다 — 음수 샘플이 양수로 뒤집히면 파형이 접힌다.</summary>
    [Fact]
    public void WideningKeepsTheSign()
    {
        for (var value = -8388608; value < 8388607; value += 7919)
        {
            byte[] packed = [(byte)value, (byte)(value >> 8), (byte)(value >> 16)];
            var wide = WasapiOutputDevice.Widen24To32(packed);
            Assert.Equal(value, BitConverter.ToInt32(wide, 0) >> 8);
        }
    }
}
