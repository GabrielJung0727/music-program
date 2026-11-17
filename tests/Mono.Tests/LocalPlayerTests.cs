using Mono.Output;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 곡과 곡 사이를 잇는 재생기. 디코더 대신 "프레임 번호가 곧 샘플 값"인 가짜 소스를 써서,
/// 나온 소리가 어느 곡의 몇 번째 프레임인지를 값으로 확인한다.
///
/// 샘플레이트를 1000Hz 로 두면 1프레임이 1ms 라 타임라인과 프레임을 같은 숫자로 읽을 수 있다.
/// </summary>
public class LocalPlayerTests
{
    private static readonly PcmFormat Format = new(1000, 24, 1);

    /// <summary>프레임 i 의 값이 offset + i 인 소스. offset 으로 어느 곡인지 구별한다.</summary>
    private sealed class FakeSource(PcmFormat format, int frames, int offset, Func<int, int>? value = null) : ILocalChunkSource
    {
        private long _next = -1;

        public int Seeks { get; private set; }
        public bool Disposed { get; private set; }
        public Action? OnRead { get; set; }

        public PcmFormat Format { get; } = format;
        public long TotalFrames => frames;
        public long Position => _next;

        public void Seek(long frame)
        {
            Seeks++;
            _next = Math.Clamp(frame, 0, frames);
        }

        public byte[] ReadNext(int count)
        {
            OnRead?.Invoke();
            if (_next < 0) _next = 0;
            var n = (int)Math.Min(count, frames - _next);
            if (n <= 0) return [];

            var bytesPerSample = Format.Depth / 8;
            var pcm = new byte[n * Format.BytesPerFrame];
            for (var i = 0; i < n; i++)
            {
                var v = value?.Invoke((int)_next + i) ?? offset + (int)_next + i;
                for (var c = 0; c < Format.Channels; c++)
                {
                    var at = (i * Format.Channels + c) * bytesPerSample;
                    if (bytesPerSample == 3) PcmMath.Write24(pcm, at, v);
                    else BitConverter.TryWriteBytes(pcm.AsSpan(at), (short)v);
                }
            }

            _next += n;
            return pcm;
        }

        public void Dispose() => Disposed = true;
    }

    private const int A = 1_000_000;
    private const int B = 2_000_000;
    private const int C = 3_000_000;

    private static readonly TrackRef TrackA = new("a", "a.flac");
    private static readonly TrackRef TrackB = new("b", "b.flac");
    private static readonly TrackRef TrackC = new("c", "c.flac");

    private sealed class Rig
    {
        public readonly Dictionary<string, FakeSource> Sources = [];
        public readonly LocalPlayer Player;
        public long Now;

        public Rig(params (TrackRef Track, FakeSource Source)[] tracks)
        {
            foreach (var (track, source) in tracks) Sources[track.Path] = source;
            Player = new LocalPlayer(path => Sources.GetValueOrDefault(path), backgroundOpen: false);
        }

        public LocalChunk? Read(TrackRef current, TrackRef? next, int wantMs = 30, bool top = false, bool empty = false)
            => Player.Read(() => Now, top, empty, current, next, wantMs);

        /// <summary>타임라인을 실시간으로 흘리며 끝까지 읽는다. 읽은 만큼 시계도 나아간다.</summary>
        public List<int> Play(TrackRef current, TrackRef? next, int maxFrames, int[]? sizes = null)
        {
            sizes ??= [30, 30, 13, 17, 10, 23, 30, 11];
            var heard = new List<int>();
            for (var i = 0; heard.Count < maxFrames && i < 100_000; i++)
            {
                var chunk = Read(current, next, sizes[i % sizes.Length]);
                if (chunk is null) break;
                var frames = chunk.Pcm.Length / chunk.Format.BytesPerFrame;
                for (var f = 0; f < frames; f++) heard.Add(PcmMath.Read24(chunk.Pcm, f * 3));
                Now += frames;
            }

            return heard;
        }
    }

    private static FakeSource Song(int frames, int offset) => new(Format, frames, offset);

    // ── 끊김 없이 잇기 ───────────────────────────────────────────────────────

    /// <summary>
    /// 앞 곡의 마지막 프레임 바로 뒤에 다음 곡의 첫 프레임이 온다. 빠진 프레임도, 겹친 프레임도,
    /// 끼어든 무음도 없어야 한다 — 조각을 어떤 길이로 잘라 읽든.
    /// </summary>
    [Fact]
    public void GaplessJoinIsSampleExact()
    {
        var rig = new Rig((TrackA, Song(1000, A)), (TrackB, Song(700, B)));

        var heard = rig.Play(TrackA, TrackB, 5000);

        var expected = Enumerable.Range(A, 1000).Concat(Enumerable.Range(B, 700)).ToList();
        Assert.Equal(expected, heard);
        Assert.Equal("b", rig.Player.PlayingTrackId);
        Assert.True(rig.Player.Ended);
        Assert.True(rig.Sources["a.flac"].Disposed);
    }

    /// <summary>손대지 않은 구간은 디코더가 낸 바이트 그대로다. 디더도 걸리지 않는다.</summary>
    [Fact]
    public void UntouchedAudioIsPassedThroughBitForBit()
    {
        var rig = new Rig((TrackA, Song(2000, A)));
        var chunk = rig.Read(TrackA, null)!;

        Assert.Equal(Format, chunk.Format);
        Assert.Equal(Enumerable.Range(A, 30), Enumerable.Range(0, 30).Select(f => PcmMath.Read24(chunk.Pcm, f * 3)));
    }

    [Fact]
    public void TransitionOffNeverJoins()
    {
        var rig = new Rig((TrackA, Song(500, A)), (TrackB, Song(500, B)));
        rig.Player.Transition = TrackTransition.Off;

        var heard = rig.Play(TrackA, TrackB, 5000);

        Assert.Equal(Enumerable.Range(A, 500), heard);
        Assert.True(rig.Player.Ended);
        Assert.Equal("a", rig.Player.PlayingTrackId);
    }

    /// <summary>샘플레이트가 다르면 한 스트림에 이어 붙일 수 없다. 앞 곡을 끝까지 내고 멈춰서 기다린다.</summary>
    [Fact]
    public void DifferentSampleRatesAreNotJoined()
    {
        var other = new FakeSource(new PcmFormat(2000, 24, 1), 500, B);
        var rig = new Rig((TrackA, Song(500, A)), (TrackB, other));

        var heard = rig.Play(TrackA, TrackB, 5000);

        Assert.Equal(Enumerable.Range(A, 500), heard);
        Assert.True(rig.Player.Ended);
    }

    /// <summary>한 곡 반복: 같은 파일을 한 번 더 열어 끝과 처음을 잇는다.</summary>
    [Fact]
    public void RepeatOneLoopsWithoutAGap()
    {
        var opened = 0;
        var player = new LocalPlayer(_ => { opened++; return Song(300, A); }, backgroundOpen: false);
        long now = 0;
        var heard = new List<int>();
        while (heard.Count < 450)
        {
            var chunk = player.Read(() => now, false, false, TrackA, TrackA, 30)!;
            var frames = chunk.Pcm.Length / 3;
            for (var f = 0; f < frames; f++) heard.Add(PcmMath.Read24(chunk.Pcm, f * 3));
            now += frames;
        }

        Assert.Equal(Enumerable.Range(A, 300).Concat(Enumerable.Range(A, 150)), heard.Take(450));
        Assert.Equal(2, opened);
    }

    // ── 크로스페이드 ─────────────────────────────────────────────────────────

    private static FakeSource Level(int frames, double level)
        => new(Format, frames, 0, _ => (int)(level * PcmMath.Scale24));

    [Fact]
    public void CrossfadeOverlapsTheTwoTracks()
    {
        var rig = new Rig((TrackA, Level(2000, 0.5)), (TrackB, Level(2000, 0.25)));
        rig.Player.Transition = TrackTransition.Crossfade;
        rig.Player.CrossfadeMs = 500;

        var heard = rig.Play(TrackA, TrackB, 10_000).Select(v => v / (double)PcmMath.Scale24).ToList();

        // 두 곡이 500ms 겹치므로 전체 길이는 그만큼 짧다.
        Assert.Equal(2000 + 2000 - 500, heard.Count);

        // 겹치기 전은 앞 곡 그대로, 겹친 뒤는 다음 곡 그대로다(계산을 거치지 않으므로 값이 정확하다).
        Assert.All(heard.Take(1500), v => Assert.Equal(0.5, v));
        Assert.All(heard.Skip(2000), v => Assert.Equal(0.25, v));

        // 겹치는 구간의 한가운데: 두 곡 모두 cos(45°) = sin(45°) = 0.7071 배.
        var mid = heard[1750];
        Assert.InRange(mid, (0.5 + 0.25) * 0.7071 - 0.002, (0.5 + 0.25) * 0.7071 + 0.002);

        // 겹침의 양 끝은 각각 앞 곡과 다음 곡에 붙는다. 계단이 없다.
        Assert.InRange(heard[1500], 0.495, 0.505);
        Assert.InRange(heard[1999], 0.245, 0.255);
    }

    /// <summary>같은 세기의 곡선: 겹치는 내내 두 게인의 제곱합이 1 이다. 직선으로 섞으면 가운데가 3dB 꺼진다.</summary>
    [Fact]
    public void CrossfadeKeepsEqualPower()
    {
        // 앞 곡만 소리가 있을 때와 다음 곡만 소리가 있을 때의 게인을 따로 잰다.
        static List<double> Gains(bool outgoing)
        {
            var rig = new Rig(
                (TrackA, Level(1000, outgoing ? 0.5 : 0.0)),
                (TrackB, Level(1000, outgoing ? 0.0 : 0.5)));
            rig.Player.Transition = TrackTransition.Crossfade;
            rig.Player.CrossfadeMs = 400;
            return rig.Play(TrackA, TrackB, 10_000).Skip(600).Take(400).Select(v => v / (0.5 * PcmMath.Scale24)).ToList();
        }

        var down = Gains(outgoing: true);
        var up = Gains(outgoing: false);

        for (var i = 0; i < 400; i++)
        {
            Assert.InRange(down[i] * down[i] + up[i] * up[i], 0.995, 1.005);
        }

        Assert.True(down[0] > 0.99 && down[^1] < 0.01);
        Assert.True(up[0] < 0.01 && up[^1] > 0.99);
    }

    /// <summary>겹침은 곡 길이의 절반을 넘지 않는다. 짧은 곡이 들어오자마자 다시 나가면 안 된다.</summary>
    [Fact]
    public void CrossfadeIsLimitedToHalfTheShorterTrack()
    {
        var rig = new Rig((TrackA, Level(2000, 0.5)), (TrackB, Level(400, 0.25)));
        rig.Player.Transition = TrackTransition.Crossfade;
        rig.Player.CrossfadeMs = 12_000;

        var heard = rig.Play(TrackA, TrackB, 10_000);

        Assert.Equal(2000 + 400 - 200, heard.Count);
    }

    // ── Core 의 세대 전환과 맞물리기 ─────────────────────────────────────────

    /// <summary>
    /// 디코더가 Core 의 시계보다 먼저 끝에 닿아 이미 다음 곡을 내고 있다. Core 가 뒤늦게 세대를 바꿔도
    /// 끊지 않고 이어 가야 하고, 그 뒤로도 같은 자리에서 계속 읽어야 한다.
    /// </summary>
    [Fact]
    public void LateEpochFlipIsAdoptedWithoutASkip()
    {
        var rig = new Rig((TrackA, Song(1000, A)), (TrackB, Song(2000, B)), (TrackC, Song(100, C)));
        var heard = rig.Play(TrackA, TrackB, 1100);
        Assert.Equal("b", rig.Player.PlayingTrackId);

        // Core 의 새 타임라인은 다음 곡의 0 에서 조금 지난 자리다. 이쪽이 실제로 낸 양과는 조금 다르다.
        Assert.True(rig.Player.AdoptSeamless("b", oldTimelineMs: rig.Now, newTimelineMs: 60));
        rig.Now = 60;

        heard.AddRange(rig.Play(TrackB, TrackC, 400));

        // 세대가 바뀐 자리에서 프레임이 빠지거나 되풀이되지 않는다.
        Assert.Equal(Enumerable.Range(A, 1000).Concat(Enumerable.Range(B, heard.Count - 1000)), heard);
        Assert.Equal(0, rig.Sources["b.flac"].Seeks - 1);   // 미리 열 때 한 번 자리를 잡은 것 말고는 옮기지 않았다
    }

    /// <summary>Core 가 먼저 세대를 바꿨다. 이쪽은 앞 곡을 끝까지 낸 뒤 자기 자리에서 잇는다.</summary>
    [Fact]
    public void EarlyEpochFlipLetsTheCurrentTrackFinish()
    {
        var rig = new Rig((TrackA, Song(1000, A)), (TrackB, Song(500, B)), (TrackC, Song(100, C)));
        var heard = rig.Play(TrackA, TrackB, 900);

        Assert.Equal("a", rig.Player.PlayingTrackId);
        Assert.True(rig.Player.AdoptSeamless("b", oldTimelineMs: rig.Now, newTimelineMs: 5));
        Assert.True(rig.Player.Behind);
        rig.Now = 5;

        // 이제 Core 의 "지금 곡"은 b 이고 "다음 곡"은 c 다.
        heard.AddRange(rig.Play(TrackB, TrackC, 400));

        Assert.Equal(Enumerable.Range(A, 1000).Concat(Enumerable.Range(B, heard.Count - 1000)), heard);
        Assert.False(rig.Player.Behind);
    }

    /// <summary>이어 붙이려던 곡이 아닌 다른 곡으로 세대가 바뀌면 끊어야 한다(사용자가 곡을 바꿨다).</summary>
    [Fact]
    public void EpochFlipToAnotherTrackIsNotSeamless()
    {
        var rig = new Rig((TrackA, Song(1000, A)), (TrackB, Song(500, B)));
        rig.Play(TrackA, TrackB, 300);

        Assert.False(rig.Player.AdoptSeamless("c", rig.Now, 0));
    }

    [Fact]
    public void NothingIsSeamlessWhenTransitionsAreOff()
    {
        var rig = new Rig((TrackA, Song(1000, A)), (TrackB, Song(500, B)));
        rig.Player.Transition = TrackTransition.Off;
        rig.Play(TrackA, TrackB, 300);

        Assert.False(rig.Player.AdoptSeamless("b", rig.Now, 0));
    }

    /// <summary>이어 붙이지 못한 채 끝났는데 Core 는 다음 곡으로 넘어갔다. 그 사실을 알려 새로 열게 한다.</summary>
    [Fact]
    public void UnjoinableSuccessorIsReportedAsBehind()
    {
        var other = new FakeSource(new PcmFormat(2000, 24, 1), 500, B);
        var rig = new Rig((TrackA, Song(300, A)), (TrackB, other));
        rig.Play(TrackA, TrackB, 5000);

        Assert.True(rig.Player.Ended);
        Assert.True(rig.Player.AdoptSeamless("b", rig.Now, 10));
        Assert.True(rig.Player.Behind);

        // 앞 곡을 끝까지 내는 동안 타임라인은 새 곡의 5초 지점까지 갔다. 그래도 새 곡은 처음부터 낸다.
        rig.Player.RestartFromTop();
        rig.Now = 5000;
        var chunk = rig.Read(TrackB, null, top: true)!;
        Assert.Equal(2000, chunk.Format.Rate);
        Assert.Equal(B, PcmMath.Read24(chunk.Pcm, 0));

        // 그 뒤로도 건너뛰지 않고 이어진다(2000Hz 라 30ms 는 60프레임이다).
        rig.Now += 30;
        Assert.Equal(B + 60, PcmMath.Read24(rig.Read(TrackB, null, top: true)!.Pcm, 0));
    }

    // ── 자리 잡기 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 혼자 들을 때: 파일을 여는 동안 타임라인이 300ms 흘렀어도 곡의 첫 샘플부터 낸다.
    /// 예전에는 지나간 만큼을 건너뛰어 곡마다 앞부분이 잘렸다.
    /// </summary>
    [Fact]
    public void SoloPlaybackStartsFromTheFirstSample()
    {
        var rig = new Rig((TrackA, Song(5000, A)));
        rig.Player.AnchorTrackStart = true;
        rig.Now = 300;

        var heard = new List<int>();
        for (var i = 0; i < 10; i++)
        {
            var chunk = rig.Read(TrackA, null, top: true)!;
            for (var f = 0; f < 30; f++) heard.Add(PcmMath.Read24(chunk.Pcm, f * 3));
            rig.Now += 30;
        }

        Assert.Equal(Enumerable.Range(A, 300), heard);
    }

    /// <summary>탐색해서 들어온 자리는 곡의 처음이 아니다. 그 자리에서 낸다.</summary>
    [Fact]
    public void SeekingNearTheStartIsHonoured()
    {
        var rig = new Rig((TrackA, Song(5000, A)));
        rig.Player.AnchorTrackStart = true;
        rig.Now = 300;

        var chunk = rig.Read(TrackA, null, top: false)!;

        Assert.Equal(A + 300, PcmMath.Read24(chunk.Pcm, 0));
    }

    /// <summary>여럿이 듣는 방은 첫 샘플이 아니라 타임라인에 맞춘다. 그래야 기기끼리 맞는다.</summary>
    [Fact]
    public void SharedRoomsSyncToTheTimeline()
    {
        var rig = new Rig((TrackA, Song(5000, A)));
        rig.Now = 300;

        var chunk = rig.Read(TrackA, null, top: true)!;

        Assert.Equal(A + 300, PcmMath.Read24(chunk.Pcm, 0));
    }

    [Fact]
    public void ALateCallContinuesAndAFarOneCatchesUp()
    {
        var rig = new Rig((TrackA, Song(10_000, A)));
        rig.Read(TrackA, null);                       // 0..29

        rig.Now = 130;                                // 100ms 늦게 불렸다. 이어 읽는다.
        Assert.Equal(A + 30, PcmMath.Read24(rig.Read(TrackA, null)!.Pcm, 0));

        rig.Now = 1000;                               // 한참 뒤처졌다. 따라잡는다.
        Assert.Equal(A + 1000, PcmMath.Read24(rig.Read(TrackA, null)!.Pcm, 0));

        rig.Now = 200;                                // 타임라인이 되감겼다.
        Assert.Equal(A + 200, PcmMath.Read24(rig.Read(TrackA, null)!.Pcm, 0));
    }

    /// <summary>
    /// 자리를 잡는 첫 읽기가 400ms 걸렸다. 장치가 비어 있으면 그 조각(이미 지난 소리)을 버리고
    /// 지금 자리에서 다시 읽는다. 장치에 소리가 있으면 이어져야 하므로 버리지 않는다.
    /// </summary>
    [Theory]
    [InlineData(true, 400)]
    [InlineData(false, 0)]
    public void ASlowPlacingReadIsRetriedOnlyWhenTheDeviceIsEmpty(bool deviceEmpty, int expectedFrame)
    {
        var source = Song(10_000, A);
        var rig = new Rig((TrackA, source));
        var slow = true;
        source.OnRead = () =>
        {
            if (!slow) return;
            slow = false;
            rig.Now += 400;
        };

        var chunk = rig.Read(TrackA, null, empty: deviceEmpty)!;

        Assert.Equal(A + expectedFrame, PcmMath.Read24(chunk.Pcm, 0));
    }

    /// <summary>일시정지로 버퍼를 비우면 버린 만큼 되돌아가서, 다시 재생할 때 멈춘 자리에서 잇는다.</summary>
    [Fact]
    public void RewindAfterFlushResumesWhereTheListenerStopped()
    {
        var rig = new Rig((TrackA, Song(10_000, A)));
        rig.Play(TrackA, null, 300);

        rig.Player.RewindAfterFlush(110);
        var chunk = rig.Player.ReadTail(30)!;

        Assert.InRange(PcmMath.Read24(chunk.Pcm, 0), A + 185, A + 215);
    }

    /// <summary>타임라인이 곡의 끝에서 멈춘 뒤에도 디코더에 남은 것은 끝까지 읽힌다.</summary>
    [Fact]
    public void TailIsReadToTheEndAfterTheTimelineStops()
    {
        var rig = new Rig((TrackA, Song(400, A)));
        var heard = rig.Play(TrackA, null, 300);

        while (rig.Player.ReadTail(30) is { } tail)
        {
            for (var f = 0; f < tail.Pcm.Length / 3; f++) heard.Add(PcmMath.Read24(tail.Pcm, f * 3));
        }

        Assert.Equal(Enumerable.Range(A, 400), heard);
        Assert.True(rig.Player.Ended);
    }

    // ── DSP ─────────────────────────────────────────────────────────────────

    /// <summary>혼자 들을 때도 DSP 가 실제로 걸린다. 예전에는 화면만 "DSP 켜짐"이고 소리는 원본이었다.</summary>
    [Fact]
    public void DspIsAppliedToLocalPlayback()
    {
        var format = new PcmFormat(44100, 16, 2);
        FakeSource Tone() => new(format, 44100, 0, i => (int)(8000 * Math.Sin(2 * Math.PI * 1000 * i / 44100)));

        var dry = new LocalPlayer(_ => Tone(), backgroundOpen: false);
        var wet = new LocalPlayer(_ => Tone(), backgroundOpen: false)
        {
            Dsp = new DspSettings(true, DspPresetKind.Parametric, 0, """[{"f":1000,"g":-12,"q":1}]""", 0, 0, 0, 0, null, null)
        };

        long now = 0;
        LocalChunk? a = null, b = null;
        for (var i = 0; i < 20; i++)
        {
            a = dry.Read(() => now, false, false, TrackA, null, 30);
            b = wet.Read(() => now, false, false, TrackA, null, 30);
            now += 30;
        }

        Assert.Equal(16, a!.Format.Depth);
        Assert.Equal(24, b!.Format.Depth);   // 가공한 신호는 24bit 로 낸다

        var dryPeak = Enumerable.Range(0, a.Pcm.Length / 2).Max(i => Math.Abs((int)BitConverter.ToInt16(a.Pcm, i * 2))) / 32768.0;
        var wetPeak = Enumerable.Range(0, b.Pcm.Length / 3).Max(i => Math.Abs(PcmMath.Read24(b.Pcm, i * 3))) / 8388608.0;
        Assert.InRange(wetPeak / dryPeak, 0.2, 0.32);   // -12dB ≈ 0.25
    }

    /// <summary>업샘플링 프리셋은 장치가 2배 레이트를 받을 때만 올린다. 못 받으면 원래 레이트로 낸다.</summary>
    [Theory]
    [InlineData(true, 2000)]
    [InlineData(false, 1000)]
    public void UpsamplingDependsOnWhatTheDeviceAccepts(bool deviceAccepts, int expectedRate)
    {
        var rig = new Rig((TrackA, Song(5000, A)));
        rig.Player.Dsp = new DspSettings(true, DspPresetKind.Headphones, 0, null, 0, 0, 0, 0, null, null);
        rig.Player.CanOpenRate = _ => deviceAccepts;

        var chunk = rig.Read(TrackA, null)!;

        Assert.Equal(expectedRate, chunk.Format.Rate);
        Assert.Equal(30 * expectedRate / 1000 * 3, chunk.Pcm.Length);
    }
}
