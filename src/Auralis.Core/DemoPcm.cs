namespace Auralis.Core;

/// <summary>실제 음원이 없는 트랙을 위한 합성 PCM. 동기화·경로 검증용.</summary>
public static class DemoPcm
{
    public static byte[] ToneChunk(string trackId, int sampleRate, int bitDepth, int channels, long startMs, int durationMs)
    {
        var frames = (int)((long)sampleRate * durationMs / 1000);
        var freq = 220 + Math.Abs(trackId.GetHashCode() % 400);
        if (bitDepth == 16)
        {
            var data = new byte[frames * channels * 2];
            for (var n = 0; n < frames; n++)
            {
                var t = (startMs / 1000.0) + n / (double)sampleRate;
                var s = (short)(Math.Sin(2 * Math.PI * freq * t) * 0.2 * 32767);
                for (var c = 0; c < channels; c++)
                {
                    BitConverter.TryWriteBytes(data.AsSpan((n * channels + c) * 2), s);
                }
            }

            return data;
        }

        var bytes = new byte[frames * channels * 3];
        for (var n = 0; n < frames; n++)
        {
            var t = (startMs / 1000.0) + n / (double)sampleRate;
            var v = (int)(Math.Sin(2 * Math.PI * freq * t) * 0.2 * 8388607);
            for (var c = 0; c < channels; c++)
            {
                var i = (n * channels + c) * 3;
                bytes[i] = (byte)v;
                bytes[i + 1] = (byte)(v >> 8);
                bytes[i + 2] = (byte)(v >> 16);
            }
        }

        return bytes;
    }
}
