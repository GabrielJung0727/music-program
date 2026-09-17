namespace Mono.Shared;

/// <summary>WAV <c>fmt </c> 청크가 말하는 샘플 인코딩.</summary>
public enum PcmEncoding
{
    /// <summary>정수 PCM. 8bit 는 부호 없음, 그 위는 부호 있는 리틀엔디안.</summary>
    Integer = 0,

    /// <summary>IEEE 부동소수 (-1.0 ~ +1.0). 32bit 또는 64bit.</summary>
    Float = 1
}

/// <summary>
/// 장치 큐로 내려보내기 전에 PCM 을 16 또는 24bit 정수로 맞춘다.
///
/// 출력 계층이 실제로 여는 규격은 16bit 아니면 24bit 둘뿐이다. 그런데 파일 헤더가 말하는
/// 뎁스는 8·32·64 도 된다. 그 숫자를 그대로 들고 내려가면 장치는 "24bit" 라고 들은 채로
/// 4바이트짜리 샘플이 든 바이트열을 3바이트씩 끊어 읽는다. 첫 샘플부터 모든 경계가 밀려
/// 좌우가 섞이고, 음악이 통째로 잡음이 된다 — 조용히 틀어지는 게 아니라 전 구간이 망가진다.
/// 그래서 뎁스를 접을 때는 바이트도 함께 접는다. 둘 중 하나만 하는 경로는 있어선 안 된다.
/// </summary>
public static class PcmSamples
{
    /// <summary>출력 계층이 실제로 열 수 있는 뎁스로 접는다. 16 아니면 24.</summary>
    public static int DeviceDepth(int sourceBits) => sourceBits <= 16 ? 16 : 24;

    /// <summary>
    /// 원본 뎁스·인코딩의 PCM 을 <see cref="DeviceDepth"/> 정수 PCM 으로 바꾼다.
    /// 이미 맞는 규격이면 배열을 그대로 돌려준다(복사 없음).
    /// </summary>
    public static byte[] ToDeviceDepth(byte[] pcm, int sourceBits, PcmEncoding encoding, out int deviceBits)
    {
        deviceBits = DeviceDepth(sourceBits);

        // 흔한 경우 두 가지는 손대지 않는다 — 비트퍼펙트 경로의 바이트를 건드릴 이유가 없다.
        if (encoding == PcmEncoding.Integer && (sourceBits == 16 || sourceBits == 24))
        {
            return pcm;
        }

        var sourceBytes = Math.Max(1, sourceBits / 8);
        var count = pcm.Length / sourceBytes;
        if (count == 0) return [];

        var targetBytes = deviceBits / 8;
        var outBytes = new byte[count * targetBytes];

        for (var i = 0; i < count; i++)
        {
            var value = ReadSample(pcm, i * sourceBytes, sourceBits, encoding, deviceBits);
            var o = i * targetBytes;
            if (targetBytes == 2)
            {
                outBytes[o] = (byte)value;
                outBytes[o + 1] = (byte)(value >> 8);
            }
            else
            {
                outBytes[o] = (byte)value;
                outBytes[o + 1] = (byte)(value >> 8);
                outBytes[o + 2] = (byte)(value >> 16);
            }
        }

        return outBytes;
    }

    /// <summary>원본 샘플 하나를 목표 뎁스의 정수 눈금으로 읽는다.</summary>
    private static int ReadSample(byte[] pcm, int at, int sourceBits, PcmEncoding encoding, int deviceBits)
    {
        var peak = deviceBits == 16 ? 32767f : 8388607f;
        var floor = deviceBits == 16 ? -32768 : -8388608;

        if (encoding == PcmEncoding.Float)
        {
            var sample = sourceBits == 64
                ? (float)BitConverter.ToDouble(pcm, at)
                : BitConverter.ToSingle(pcm, at);
            if (float.IsNaN(sample)) sample = 0;
            return (int)Math.Clamp(sample * peak, floor, peak);
        }

        // 정수 PCM — 부호를 살린 뒤 목표 뎁스만큼 자리를 옮긴다.
        var raw = sourceBits switch
        {
            8 => (pcm[at] - 128) << 16,                                          // 부호 없음 → 24bit 눈금
            16 => BitConverter.ToInt16(pcm, at) << 8,                            // → 24bit 눈금
            24 => Sign24(pcm[at] | (pcm[at + 1] << 8) | (pcm[at + 2] << 16)),
            32 => BitConverter.ToInt32(pcm, at) >> 8,                            // 32bit → 24bit
            _ => 0
        };

        return deviceBits == 16
            ? Math.Clamp(raw >> 8, floor, (int)peak)
            : Math.Clamp(raw, floor, (int)peak);
    }

    private static int Sign24(int v) => (v & 0x800000) != 0 ? v | unchecked((int)0xFF000000) : v;
}
