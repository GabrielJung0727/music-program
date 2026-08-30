using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Auralis.Protocol;

/// <summary>
/// AATP 데이터 플레인 한 청크. 소셜 메시지는 이 경로에 실리지 않는다.
/// </summary>
public readonly record struct AatpAudio(
    long PtsMs,
    int SampleRate,
    int BitDepth,
    int Channels,
    bool IsDsd,
    long Epoch,
    byte[] Payload);

public static class AatpFrame
{
    public static readonly byte[] Magic = "AATP"u8.ToArray();

    private const int HeaderSize = 44;
    private const int FlagEncrypted = 1;
    private const int FlagDsd = 2;

    public static byte[] Encode(AatpAudio audio, byte[]? aesKey)
    {
        var payload = audio.Payload;
        var flags = audio.IsDsd ? FlagDsd : 0;
        if (aesKey is { Length: 32 })
        {
            payload = Encrypt(aesKey, payload);
            flags |= FlagEncrypted;
        }

        var frame = new byte[HeaderSize + payload.Length];
        var span = frame.AsSpan();
        Magic.CopyTo(span);
        span[4] = 2;
        BinaryPrimitives.WriteInt64LittleEndian(span[8..], audio.PtsMs);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], audio.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[20..], audio.BitDepth);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], audio.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], flags);
        BinaryPrimitives.WriteInt64LittleEndian(span[32..], audio.Epoch);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], payload.Length);
        payload.CopyTo(span[HeaderSize..]);
        return frame;
    }

    public static bool TryDecode(byte[] frame, byte[]? aesKey, out AatpAudio audio)
    {
        audio = default;
        if (frame.Length < HeaderSize || !frame.AsSpan(0, 4).SequenceEqual(Magic))
        {
            return false;
        }

        var span = frame.AsSpan();
        var pts = BinaryPrimitives.ReadInt64LittleEndian(span[8..]);
        var rate = BinaryPrimitives.ReadInt32LittleEndian(span[16..]);
        var depth = BinaryPrimitives.ReadInt32LittleEndian(span[20..]);
        var channels = BinaryPrimitives.ReadInt32LittleEndian(span[24..]);
        var flags = BinaryPrimitives.ReadInt32LittleEndian(span[28..]);
        var epoch = BinaryPrimitives.ReadInt64LittleEndian(span[32..]);
        var len = BinaryPrimitives.ReadInt32LittleEndian(span[40..]);
        if (len < 0 || frame.Length < HeaderSize + len)
        {
            return false;
        }

        var payload = span.Slice(HeaderSize, len).ToArray();
        if ((flags & FlagEncrypted) != 0)
        {
            if (aesKey is not { Length: 32 })
            {
                return false;
            }

            payload = Decrypt(aesKey, payload);
        }

        audio = new AatpAudio(pts, rate, depth, channels, (flags & FlagDsd) != 0, epoch, payload);
        return true;
    }

    private static byte[] Encrypt(byte[] key, byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var gcm = new AesGcm(key, 16);
        gcm.Encrypt(nonce, plain, cipher, tag);
        var packed = new byte[12 + 16 + cipher.Length];
        nonce.CopyTo(packed, 0);
        tag.CopyTo(packed, 12);
        cipher.CopyTo(packed, 28);
        return packed;
    }

    private static byte[] Decrypt(byte[] key, byte[] packed)
    {
        var nonce = packed.AsSpan(0, 12);
        var tag = packed.AsSpan(12, 16);
        var cipher = packed.AsSpan(28);
        var plain = new byte[cipher.Length];
        using var gcm = new AesGcm(key, 16);
        gcm.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}
