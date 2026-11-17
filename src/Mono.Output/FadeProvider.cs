using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Mono.Output;

/// <summary>
/// 장치가 읽어 가는 길목에 놓는 짧은 페이드.
///
/// 일시정지나 탐색은 재생 중인 파형을 아무 데서나 끊는다. 끊긴 자리의 값이 0 이 아니면 스피커는
/// 그 값에서 0 으로 한 샘플 만에 떨어지고, 그게 딸깍 소리다. 끊기 직전 8ms 동안 0 으로 내려 주고,
/// 곡 중간에서 다시 시작할 때 8ms 동안 올려 주면 사라진다.
///
/// 평소에는 아무것도 하지 않는다. 버퍼에서 읽은 바이트를 그대로 넘기므로 비트퍼펙트 경로는
/// 그대로다 — 손대는 건 끊고 잇는 그 8ms 뿐이다. 곡이 처음부터 시작할 때나 곡이 끊김 없이
/// 이어질 때는 걸지 않는다.
/// </summary>
public sealed class FadeProvider : IWaveProvider
{
    public const int RampMs = 8;

    private enum Mode
    {
        Pass,
        FadingOut,
        Silent,
        FadingIn
    }

    private readonly IWaveProvider _source;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _silent = new(false);
    private readonly int _rampFrames;
    private readonly int _bytesPerSample;
    private readonly int _channels;
    private readonly bool _float;
    private Mode _mode = Mode.Pass;
    private int _position;

    public FadeProvider(IWaveProvider source)
    {
        _source = source;
        WaveFormat = source.WaveFormat;
        _channels = Math.Max(1, WaveFormat.Channels);
        _bytesPerSample = Math.Max(1, WaveFormat.BitsPerSample / 8);
        _float = WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat
                 || (WaveFormat is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));
        _rampFrames = Math.Max(1, WaveFormat.SampleRate * RampMs / 1000);
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>지금부터 읽히는 소리를 0 으로 내린다. 다 내려가면 <see cref="WaitSilent"/> 가 풀린다.</summary>
    public void BeginFadeOut()
    {
        lock (_gate)
        {
            if (_mode == Mode.Silent) return;
            // 올라가던 중이었다면 그 높이에서 내려간다. 처음부터 다시 내리면 값이 튄다.
            _position = _mode == Mode.FadingIn ? _rampFrames - _position : 0;
            _silent.Reset();
            _mode = Mode.FadingOut;
        }
    }

    /// <summary>페이드아웃이 끝날 때까지 기다린다. 장치가 읽어 가지 않으면 시간만 채우고 돌아온다.</summary>
    public bool WaitSilent(int timeoutMs) => _silent.Wait(timeoutMs);

    /// <summary>다음에 읽히는 소리를 0 에서부터 올린다. 곡 중간에서 다시 시작할 때 쓴다.</summary>
    public void ArmFadeIn()
    {
        lock (_gate)
        {
            _position = 0;
            _mode = Mode.FadingIn;
        }
    }

    /// <summary>페이드 없이 그대로 통과시키는 상태로 되돌린다.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _position = 0;
            _mode = Mode.Pass;
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);

        lock (_gate)
        {
            if (_mode == Mode.Pass || read <= 0) return read;

            var frameBytes = _bytesPerSample * _channels;
            var frames = read / frameBytes;
            var span = buffer.AsSpan(offset, frames * frameBytes);

            for (var f = 0; f < frames; f++)
            {
                var gain = NextGain();
                if (gain >= 1f) continue;
                Scale(span.Slice(f * frameBytes, frameBytes), gain);
            }

            return read;
        }
    }

    /// <summary>이번 프레임에 걸 게인을 내고 한 프레임 나아간다.</summary>
    private float NextGain()
    {
        switch (_mode)
        {
            case Mode.Silent:
                return 0f;

            case Mode.FadingOut:
            {
                var gain = Curve(1f - (float)_position / _rampFrames);
                if (++_position >= _rampFrames)
                {
                    _mode = Mode.Silent;
                    _silent.Set();
                }

                return gain;
            }

            case Mode.FadingIn:
            {
                var gain = Curve((float)_position / _rampFrames);
                if (++_position >= _rampFrames) _mode = Mode.Pass;
                return gain;
            }

            default:
                return 1f;
        }
    }

    /// <summary>올림 코사인. 양 끝에서 기울기가 0 이라 직선 경사보다 귀에 덜 걸린다.</summary>
    private static float Curve(float t) => 0.5f - 0.5f * MathF.Cos(MathF.PI * Math.Clamp(t, 0f, 1f));

    private void Scale(Span<byte> frame, float gain)
    {
        if (_float && _bytesPerSample == 4)
        {
            foreach (ref var sample in MemoryMarshal.Cast<byte, float>(frame)) sample *= gain;
            return;
        }

        switch (_bytesPerSample)
        {
            case 2:
                foreach (ref var sample in MemoryMarshal.Cast<byte, short>(frame))
                {
                    sample = (short)MathF.Round(sample * gain);
                }

                break;

            case 4:
                foreach (ref var sample in MemoryMarshal.Cast<byte, int>(frame))
                {
                    sample = (int)Math.Round(sample * (double)gain);
                }

                break;

            case 3:
                for (var i = 0; i + 2 < frame.Length; i += 3)
                {
                    var v = frame[i] | (frame[i + 1] << 8) | ((sbyte)frame[i + 2] << 16);
                    v = (int)MathF.Round(v * gain);
                    frame[i] = (byte)v;
                    frame[i + 1] = (byte)(v >> 8);
                    frame[i + 2] = (byte)(v >> 16);
                }

                break;
        }
    }
}
