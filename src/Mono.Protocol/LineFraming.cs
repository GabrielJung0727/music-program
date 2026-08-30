using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Mono.Protocol;

public static class LineFraming
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static byte[] Encode(MonoMessage message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        return Encoding.UTF8.GetBytes(json + "\n");
    }

    public static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out MonoMessage? message)
    {
        message = null;
        var reader = new SequenceReader<byte>(buffer);
        if (!reader.TryReadTo(out ReadOnlySequence<byte> line, (byte)'\n'))
        {
            return false;
        }

        buffer = reader.UnreadSequence;
        var json = Encoding.UTF8.GetString(line.ToArray());
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        message = JsonSerializer.Deserialize<MonoMessage>(json, JsonOptions);
        return true;
    }
}
