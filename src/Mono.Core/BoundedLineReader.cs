using System.Text;

namespace Mono.Core;

/// <summary>
/// '\n' 으로 끝나는 UTF-8 한 줄씩 읽는다. 한 줄이 상한을 넘으면 연결을 포기한다.
///
/// StreamReader.ReadLineAsync 에는 상한이 없다. 줄바꿈 없이 바이트만 계속 보내는 상대가 있으면
/// 문자열이 메모리가 바닥날 때까지 자란다. 7700 과 7701 은 LAN 에 열려 있으므로 그 상대는
/// 인증을 거치지 않은 누구든 될 수 있다.
/// </summary>
public sealed class BoundedLineReader
{
    private readonly Stream _stream;
    private readonly int _maxLineBytes;
    private readonly byte[] _buffer = new byte[8192];
    private readonly MemoryStream _line = new();
    private int _start;
    private int _end;
    private bool _first = true;

    public BoundedLineReader(Stream stream, int maxLineBytes)
    {
        _stream = stream;
        _maxLineBytes = maxLineBytes;
    }

    /// <summary>다음 한 줄. 상대가 연결을 닫았으면 null.</summary>
    /// <exception cref="IOException">한 줄이 상한을 넘었다.</exception>
    public async ValueTask<string?> ReadLineAsync(CancellationToken ct)
    {
        _line.SetLength(0);
        while (true)
        {
            if (_start == _end)
            {
                _start = 0;
                _end = await _stream.ReadAsync(_buffer, ct);
                if (_end == 0)
                {
                    // 줄바꿈 없이 끝난 마지막 조각도 한 줄로 돌려준다(StreamReader 와 같은 동작).
                    return _line.Length > 0 ? Decode() : null;
                }
            }

            if (TakeFromBuffer()) return Decode();
        }
    }

    /// <summary>버퍼에서 줄 끝까지(또는 버퍼 끝까지) 옮긴다. 줄이 끝났으면 true.</summary>
    private bool TakeFromBuffer()
    {
        var pending = _buffer.AsSpan(_start, _end - _start);
        var newline = pending.IndexOf((byte)'\n');
        var take = newline < 0 ? pending.Length : newline;
        if (_line.Length + take > _maxLineBytes)
        {
            throw new IOException($"line exceeds {_maxLineBytes} bytes");
        }

        _line.Write(pending[..take]);
        _start += newline < 0 ? take : take + 1;
        return newline >= 0;
    }

    private string Decode()
    {
        var bytes = _line.GetBuffer().AsSpan(0, (int)_line.Length);
        if (bytes.Length > 0 && bytes[^1] == (byte)'\r') bytes = bytes[..^1];

        // 스트림 맨 앞의 UTF-8 BOM. StreamWriter 로 쓰는 클라이언트가 붙여 보낼 수 있다.
        if (_first)
        {
            _first = false;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) bytes = bytes[3..];
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
