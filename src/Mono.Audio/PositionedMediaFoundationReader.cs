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
    private readonly string _path;
    private NativeReader _reader;
    private bool _hasRead;
    private long _position;
    private bool _pendingSeek;
    private long? _discardBefore;
    private byte[] _decoded = [];
    private int _offset;

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
            _pendingSeek = false;
        }

        var written = 0;
        while (written < count)
        {
            if (_offset < _decoded.Length)
            {
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
                if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0) break;
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
        return written;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _reader.Dispose();
        base.Dispose(disposing);
    }
}
