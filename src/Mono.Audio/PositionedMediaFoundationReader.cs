using System.Runtime.InteropServices;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace Mono.Audio;

/// <summary>
/// Media Foundation may seek to an earlier compressed frame. Trim decoded
/// samples using their timestamps rather than labelling that frame as the
/// requested position. The decoder is created and consumed on MTA threads.
/// </summary>
internal sealed class PositionedMediaFoundationReader : WaveStream
{
    /// <summary>
    /// 이 길이까지의 앞쪽 이동은 디코더를 새로 열지 않고 그 사이를 디코드해서 버린다.
    ///
    /// 한 번 읽은 디코더로 다시 탐색하려면 리더를 통째로 새로 열어야 하는데(아래 Read 참고),
    /// 그게 수십~수백 ms 다. 재생 헤드를 따라잡으려는 짧은 앞쪽 이동까지 그 값을 치르면
    /// 따라잡는 동안 또 뒤처져서 끝없이 다시 열게 된다. 이어서 디코드해 버리는 쪽은
    /// 재생 속도의 수백 배라 몇 ms 로 끝나고, 자리도 바이트 단위로 정확하다.
    /// </summary>
    private const int ForwardSkipSeconds = 10;

    private readonly string _path;
    private NativeReader _reader;
    private bool _hasRead;
    private long _position;
    private bool _pendingSeek;
    private long? _discardBefore;
    private byte[] _decoded = [];
    private int _offset;
    /// <summary>디코더가 실제로 서 있는 자리(다음에 내줄 바이트). 믿을 수 없으면 -1.</summary>
    private long _decoderPosition = -1;
    /// <summary>앞쪽 이동으로 내주기 전에 버려야 할 바이트.</summary>
    private long _skip;

    public PositionedMediaFoundationReader(string path)
    {
        _path = path;
        _reader = new NativeReader(path);
    }

    public override WaveFormat WaveFormat => _reader.WaveFormat;
    public override long Length => _reader.Length;

    private sealed class NativeReader(string path)
        : MediaFoundationReader(path, new MediaFoundationReaderSettings { SingleReaderObject = true })
    {
        public IMFSourceReader Source { get; private set; } = null!;
        protected override IMFSourceReader CreateReader(MediaFoundationReaderSettings settings)
            => Source = base.CreateReader(settings);
    }

    public override long Position
    {
        get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value / WaveFormat.BlockAlign * WaveFormat.BlockAlign;
            _pendingSeek = true;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_pendingSeek)
        {
            var ahead = _position - _decoderPosition;
            if (_decoderPosition >= 0 && ahead >= 0
                && ahead <= (long)WaveFormat.AverageBytesPerSecond * ForwardSkipSeconds)
            {
                // 조금 앞이다. 지금 디코더를 그대로 두고 그 사이만 버린다.
                _skip = ahead;
            }
            else
            {
                // Some FLAC transforms retain decoded samples even after Flush +
                // SetCurrentPosition. A new reader gives timestamps a clean origin.
                if (_hasRead)
                {
                    _reader.Dispose();
                    _reader = new NativeReader(_path);
                }
                _reader.Position = _position;
                // Apply NAudio's deferred native seek without consuming audio.
                _reader.Read(Array.Empty<byte>(), 0, 0);
                _discardBefore = _position;
                _decoded = [];
                _offset = 0;
                _skip = 0;
            }
            _pendingSeek = false;
        }

        // 읽는 도중 예외가 나면 디코더가 어디까지 갔는지 알 수 없다. 끝까지 읽은 뒤에만 다시 적는다.
        _decoderPosition = -1;
        var ended = false;
        var written = 0;
        while (written < count)
        {
            if (_offset < _decoded.Length)
            {
                if (_skip > 0)
                {
                    var drop = (int)Math.Min(_skip, _decoded.Length - _offset);
                    _offset += drop;
                    _skip -= drop;
                    continue;
                }

                var size = Math.Min(count - written, _decoded.Length - _offset);
                Array.Copy(_decoded, _offset, buffer, offset + written, size);
                _offset += size;
                written += size;
                continue;
            }

            _hasRead = true;
            _reader.Source.ReadSample(MediaFoundationInterop.MF_SOURCE_READER_FIRST_AUDIO_STREAM, 0,
                out _, out var flags, out var timestamp, out var sample);
            try
            {
                if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    ended = true;
                    break;
                }
                if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0)
                    throw new InvalidDataException("Audio format changed while decoding.");
                if (sample is null) continue;
                sample.ConvertToContiguousBuffer(out var mediaBuffer);
                try
                {
                    mediaBuffer.Lock(out var data, out _, out var length);
                    try
                    {
                        _decoded = new byte[length];
                        Marshal.Copy(data, _decoded, 0, length);
                    }
                    finally { mediaBuffer.Unlock(); }
                }
                finally { if (OperatingSystem.IsWindows()) Marshal.ReleaseComObject(mediaBuffer); }

                _offset = 0;
                if (_discardBefore is { } target)
                {
                    var firstFrame = ((long)timestamp * WaveFormat.SampleRate + 5_000_000) / 10_000_000;
                    var skip = Math.Max(0, target - firstFrame * WaveFormat.BlockAlign);
                    _offset = (int)Math.Min(_decoded.Length, skip);
                    if (_offset < _decoded.Length) _discardBefore = null;
                }
            }
            finally { if (sample is not null && OperatingSystem.IsWindows()) Marshal.ReleaseComObject(sample); }
        }
        _position += written;
        // 끝에 닿았거나 찾던 자리에 아직 못 갔으면 디코더 위치를 모른다 — 다음 이동은 새로 연다.
        _decoderPosition = ended || _discardBefore is not null || _skip > 0 ? -1 : _position;
        return written;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _reader.Dispose();
        base.Dispose(disposing);
    }
}
