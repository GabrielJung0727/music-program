namespace Mono.Output;

/// <summary>DSD 네이티브 비트를 DoP (DSD over PCM) 24-bit 프레임으로 싸서 WASAPI로 보낸다.</summary>
public static class DopEncoder
{
    /// <summary>
    /// DSF 스타일 채널 블록 인터리브 DSD 바이트 → DoP24 인터리브 PCM.
    /// marker는 0x05 / 0xFA 교대.
    /// </summary>
    public static (byte[] Pcm, int Rate, int Depth, int Channels) Encode(byte[] dsd, int dsdRate, int channels)
    {
        channels = Math.Max(channels, 1);
        // 대략: 1 DSD 바이트/채널 = 8 샘플 → DoP는 샘플당 1 프레임
        var frames = dsd.Length / channels;
        if (frames <= 0) return ([], 176400, 24, channels);

        var pcm = new byte[frames * channels * 3];
        byte marker = 0x05;
        for (var f = 0; f < frames; f++)
        {
            for (var c = 0; c < channels; c++)
            {
                var dsdByte = dsd[f * channels + c];
                var i = (f * channels + c) * 3;
                pcm[i] = dsdByte;
                pcm[i + 1] = 0x00;
                pcm[i + 2] = marker;
            }
            marker = marker == 0x05 ? (byte)0xFA : (byte)0x05;
        }

        // DoP rate = DSD rate / 16 (DSD64→176.4k, DSD128→352.8k)
        var pcmRate = Math.Max(dsdRate / 16, 176400);
        return (pcm, pcmRate, 24, channels);
    }
}
