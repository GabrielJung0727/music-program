namespace Mono.Shared;

/// <summary>
/// WAV <c>fmt </c>/<c>data</c> 청크 한 벌.
///
/// Core 의 팬아웃 슬라이서와 Output 의 클럭싱크 리더가 같은 파일을 각자 연다. 헤더를 따로
/// 읽으면 한쪽만 float 를 못 알아보는 식으로 어긋나고, 그 어긋남은 잡음으로만 드러난다.
/// 그래서 파싱은 여기 한 곳에만 둔다.
/// </summary>
public sealed record WavHeader(
    long DataPosition,
    long DataSize,
    int SampleRate,
    int SourceBits,
    PcmEncoding Encoding,
    int Channels)
{
    /// <summary>파일에 적힌 그대로의 프레임당 바이트 수.</summary>
    public int SourceBytesPerFrame => Math.Max(1, Channels) * Math.Max(1, SourceBits / 8);

    /// <summary>장치로 내려갈 때의 뎁스. 16 아니면 24.</summary>
    public int DeviceBits => PcmSamples.DeviceDepth(SourceBits);

    public long DurationMs => SourceBytesPerFrame == 0 || SampleRate == 0
        ? 0
        : DataSize / SourceBytesPerFrame * 1000 / SampleRate;

    /// <summary>헤더를 읽는다. RIFF/WAVE 가 아니거나 data 청크가 없으면 null.</summary>
    public static WavHeader? Parse(FileStream fs)
    {
        try
        {
            using var br = new BinaryReader(fs, System.Text.Encoding.ASCII, leaveOpen: true);
            fs.Position = 0;
            if (new string(br.ReadChars(4)) != "RIFF") return null;
            br.ReadUInt32();
            if (new string(br.ReadChars(4)) != "WAVE") return null;

            var rate = 44100;
            var bits = 16;
            var channels = 2;
            var encoding = PcmEncoding.Integer;
            var sawFormat = false;

            while (fs.Position <= fs.Length - 8)
            {
                var id = new string(br.ReadChars(4));
                var size = br.ReadUInt32();

                if (id == "fmt " && size >= 16)
                {
                    var tag = br.ReadUInt16();
                    channels = br.ReadUInt16();
                    rate = br.ReadInt32();
                    br.ReadInt32();     // avgBytesPerSec
                    br.ReadUInt16();    // blockAlign
                    bits = br.ReadUInt16();

                    // WAVE_FORMAT_EXTENSIBLE(0xFFFE)은 실제 인코딩을 SubFormat GUID 앞 2바이트에 둔다.
                    // 여기를 안 보면 32bit float 파일이 32bit 정수로 읽혀 통째로 잡음이 된다.
                    if (tag == 0xFFFE && size >= 40)
                    {
                        br.ReadUInt16();            // cbSize
                        br.ReadUInt16();            // wValidBitsPerSample
                        br.ReadUInt32();            // dwChannelMask
                        tag = br.ReadUInt16();      // SubFormat GUID Data1 하위 16비트
                        br.ReadBytes((int)size - 26);
                    }
                    else if (size > 16)
                    {
                        br.ReadBytes((int)size - 16);
                    }

                    encoding = tag == 3 ? PcmEncoding.Float : PcmEncoding.Integer;
                    sawFormat = true;
                }
                else if (id == "data")
                {
                    if (!sawFormat) return null;
                    var pos = fs.Position;
                    // 스트리밍으로 쓰인 WAV 는 data 크기를 0 이나 0xFFFFFFFF 로 둔다 — 파일 끝까지로 본다.
                    var declared = size is 0 or 0xFFFFFFFF ? long.MaxValue : size;
                    var available = fs.Length - pos;
                    var bpf = Math.Max(1, channels) * Math.Max(1, bits / 8);
                    var usable = Math.Min(declared, available) / bpf * bpf;
                    if (usable <= 0) return null;
                    return new WavHeader(pos, usable, rate, bits, encoding, Math.Max(channels, 1));
                }
                else
                {
                    // RIFF 청크는 짝수 바이트로 정렬된다. 홀수 크기 뒤에는 패딩 1바이트가 붙는다.
                    fs.Position += size + (size % 2);
                }
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }
}
