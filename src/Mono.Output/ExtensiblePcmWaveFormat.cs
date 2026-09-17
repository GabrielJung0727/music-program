using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Mono.Output;

/// <summary>
/// WAVEFORMATEXTENSIBLE — 컨테이너 비트와 유효 비트를 따로 적는 PCM 규격.
///
/// 16비트를 넘어가면 WAVEFORMATEX 하나로는 규격이 정해지지 않는다. "24비트"라고만 적으면
/// 3바이트로 담으라는 뜻인지 4바이트 컨테이너의 상위 24비트라는 뜻인지 드라이버가 알 수 없다.
/// 그런데 IsFormatSupported 는 그 모호한 요청에도 "지원한다"고 답한다 — 실제로 NVIDIA HDMI
/// 엔드포인트는 3바이트로 받겠다고 해 놓고 배타 모드에서 프레임을 4바이트씩 읽어 갔다.
/// 큐가 실시간의 1.35배(= 32/24)로 빠지고, 첫 샘플부터 모든 경계가 밀려 음악 전체가 잡음이 된다.
///
/// 그래서 24비트는 항상 이 규격으로 연다 — 컨테이너 32비트, 유효 24비트, 상위 정렬.
/// 같은 장치에서 드레인이 정확히 1.00배로 돌아온다.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public class ExtensiblePcmWaveFormat : WaveFormat
{
    /// <summary>KSDATAFORMAT_SUBTYPE_PCM.</summary>
    private static readonly Guid SubtypePcm = new("00000001-0000-0010-8000-00aa00389b71");

    private readonly short _validBitsPerSample;
    private readonly int _channelMask;
    private readonly Guid _subFormat;

    public ExtensiblePcmWaveFormat(int rate, int containerBits, int validBits, int channelCount)
    {
        waveFormatTag = WaveFormatEncoding.Extensible;
        channels = (short)channelCount;
        sampleRate = rate;
        bitsPerSample = (short)containerBits;
        blockAlign = (short)(channelCount * containerBits / 8);
        averageBytesPerSecond = rate * blockAlign;
        extraSize = 22;                     // cbSize: WAVEFORMATEXTENSIBLE 의 추가 필드 크기
        _validBitsPerSample = (short)validBits;
        _channelMask = channelCount == 1 ? 0x4 : 0x3;   // 모노는 center, 그 외는 front L/R
        _subFormat = SubtypePcm;
    }

    public int ValidBitsPerSample => _validBitsPerSample;

    public override string ToString()
        => $"PCM {sampleRate}Hz {_validBitsPerSample}bit/{bitsPerSample}bit 컨테이너 {channels}ch";
}
