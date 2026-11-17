using Mono.Shared;

namespace Mono.Output;

/// <summary>재생할 곡 하나. Core 가 알려 준 id 와, 이 엔드포인트가 직접 열 경로.</summary>
public readonly record struct TrackRef(string Id, string Path)
{
    public static TrackRef? From(string? id, string? path)
        => string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(path) ? null : new TrackRef(id, path);
}

/// <summary>장치로 내려갈 PCM 한 조각과 그 모양.</summary>
public sealed record LocalChunk(byte[] Pcm, PcmFormat Format);

/// <summary>
/// Clock-sync 로컬 재생기. 파일을 직접 열어 타임라인에 맞춰 읽고, 곡과 곡 사이를 잇는다.
///
/// 곡이 끝까지 재생되어 넘어갈 때 하는 일이 셋 중 하나다.
///  - 끊김 없이 잇기: 앞 곡의 마지막 샘플 바로 뒤에 다음 곡의 첫 샘플을 붙인다. 무음도 겹침도 없다.
///  - 크로스페이드: 앞 곡의 끝과 다음 곡의 처음을 겹치고, 한쪽은 내리고 한쪽은 올린다.
///  - 끔: 아무것도 잇지 않는다. Core 가 세대를 바꾸면 끊고 새로 연다(예전 동작).
///
/// 잇는 자리는 이 재생기가 정한다. Core 의 타임라인은 벽시계로 흐르고 출력은 버퍼만큼 늦게 나가므로,
/// Core 가 "곡이 바뀌었다"고 알리는 순간에 맞춰 자르면 앞 곡의 꼬리가 버퍼째 잘리고 다음 곡의
/// 머리는 파일을 여는 동안 지나가 버린다. 그래서 디코더가 실제로 끝에 닿는 자리에서 잇고,
/// Core 의 타임라인과의 차이는 <see cref="_biasMs"/> 로 기억해 둔다 — 소리는 샘플 단위로 이어지고,
/// 화면의 진행 표시와는 그만큼의 일정한 차이만 남는다.
///
/// 신호를 건드리는 건 두 곡이 겹치는 구간과 DSP 가 켜졌을 때뿐이다. 그 밖에는 디코더가 낸
/// 바이트를 그대로 넘긴다.
/// </summary>
public sealed class LocalPlayer : IDisposable
{
    /// <summary>
    /// 곡이 처음(0)부터 시작한 지 이 안이면, 지나간 만큼을 건너뛰지 않고 첫 샘플부터 낸다.
    /// 혼자 들을 때만 쓴다 — 여러 기기가 같이 듣는 방에서는 타임라인에 맞추는 쪽이 옳다.
    /// </summary>
    public const int HeadAnchorMs = 2_000;

    /// <summary>다음 곡을 이만큼 앞서 열어 둔다(겹치는 길이에 더해서).</summary>
    public const int PrepareLeadMs = 10_000;

    /// <summary>이보다 짧은 겹침은 크로스페이드가 아니라 그냥 이어 붙인다.</summary>
    private const int MinFadeMs = 50;

    /// <summary>다음 곡을 열 때 미리 디코드해 두는 길이. 첫 읽기의 지연을 여기서 치른다.</summary>
    private const int PrefetchMs = 250;

    private readonly Func<string, ILocalChunkSource?> _open;
    private readonly bool _backgroundOpen;
    private readonly DspChain _dsp = new();
    private readonly TpdfDither _dither = new();

    private Deck? _current;
    private Deck? _upcoming;
    private Task<Deck?>? _opening;
    private TrackRef? _openingFor;
    private TrackRef? _failed;

    /// <summary>타임라인 시각에서 이만큼을 빼면 지금 디코드 중인 곡 안에서의 위치가 된다.</summary>
    private double _biasMs;

    /// <summary>Core 가 아직 세대를 바꾸지 않았는데 이쪽은 이미 다음 곡으로 넘어온 횟수.</summary>
    private int _ahead;

    /// <summary>Core 는 세대를 바꿨는데 이쪽은 아직 앞 곡을 내고 있다.</summary>
    private bool _behind;

    private bool _fading;
    private long _fadeLength;
    private long _fadeDone;
    private long _fadeStartedAt;

    private bool _ended;
    private bool _startFromTop;
    private TrackRef? _coreNext;

    /// <param name="open">경로를 받아 소스를 연다. 열 수 없으면 null.</param>
    /// <param name="backgroundOpen">
    /// 다음 곡을 다른 스레드에서 연다. 파일을 여는 데 수십 ms, 네트워크 소스는 초 단위가 걸릴 수 있어
    /// 렌더 스레드에서 열면 그동안 지금 곡이 끊긴다. 테스트는 꺼서 순서를 고정한다.
    /// </param>
    public LocalPlayer(Func<string, ILocalChunkSource?> open, bool backgroundOpen = true)
    {
        _open = open;
        _backgroundOpen = backgroundOpen;
    }

    public TrackTransition Transition { get; set; } = TrackTransition.Gapless;
    public int CrossfadeMs { get; set; } = 5000;
    public DspSettings Dsp { get; set; } = DspSettings.Off;

    /// <summary>곡의 처음을 지나간 만큼 건너뛰지 않고 첫 샘플부터 낼지. 혼자 듣는 방에서만 켠다.</summary>
    public bool AnchorTrackStart { get; set; }

    /// <summary>장치가 이 샘플레이트를 받는가. DSP 의 2배 업샘플링을 켤지 정할 때 묻는다.</summary>
    public Func<int, bool>? CanOpenRate { get; set; }

    /// <summary>지금 디코드 중인 곡.</summary>
    public string? PlayingTrackId => _current?.Track.Id;

    /// <summary>지금 곡을 끝까지 읽었고 이어 붙일 곡이 없다. 장치에 남은 소리만 나가면 끝이다.</summary>
    public bool Ended => _ended;

    /// <summary>Core 는 이미 다음 곡으로 넘어갔는데 이쪽은 앞 곡을 내고 있다.</summary>
    public bool Behind => _behind;

    public bool Crossfading => _fading;

    /// <summary>재생이 끊겼다(사용자가 곡을 넘김, 탐색, 정지). 열어 둔 것을 모두 닫고 처음 상태로 돌아간다.</summary>
    public void Stop()
    {
        _current?.Dispose();
        _current = null;
        DropUpcoming();
        _biasMs = 0;
        _ahead = 0;
        _behind = false;
        _fading = false;
        _ended = false;
        _startFromTop = false;
        _failed = null;
        _dsp.Reset();
    }

    /// <summary>
    /// 이어 붙이지 못한 다음 곡을 새로 연다. 앞 곡을 끝까지 내느라 타임라인은 이미 그 곡 안으로
    /// 들어가 있지만, 지나간 만큼을 건너뛰지 않고 첫 샘플부터 낸다 — 건너뛰면 곡의 머리가 잘린다.
    /// </summary>
    public void RestartFromTop()
    {
        Stop();
        _startFromTop = true;
    }

    /// <summary>
    /// Core 가 세대를 바꿨다. 재생하던 것을 끊지 않고 이어 가도 되는가.
    ///
    /// 되는 경우는 둘이다. 이쪽이 이미 그 곡으로 넘어와 있거나(디코더가 Core 의 시계보다 먼저 끝에
    /// 닿았다), 그 곡이 이쪽이 이어 붙이려던 다음 곡이거나. 둘 다 아니면 다른 곡이므로 끊어야 한다.
    /// </summary>
    /// <param name="trackId">새 세대의 곡.</param>
    /// <param name="oldTimelineMs">앞 세대의 타임라인이 지금 가리키는 위치.</param>
    /// <param name="newTimelineMs">새 세대의 타임라인이 지금 가리키는 위치.</param>
    public bool AdoptSeamless(string? trackId, long oldTimelineMs, long newTimelineMs)
    {
        if (_current is null || trackId is null || Transition == TrackTransition.Off) return false;

        if (_ahead > 0)
        {
            if (_current.Track.Id != trackId) return false;
            _ahead--;
        }
        else
        {
            var joining = _fading && _upcoming?.Track.Id == trackId;
            if (_behind || (!joining && _coreNext?.Id != trackId)) return false;
            _behind = true;
        }

        // 타임라인의 숫자는 새 곡 기준으로 바뀌었지만 내고 있는 소리는 그대로 이어진다.
        // 방금까지 요청하던 자리가 그대로 유지되도록 두 타임라인의 차이를 옮겨 적는다.
        _biasMs += newTimelineMs - oldTimelineMs;
        return true;
    }

    /// <summary>
    /// 일시정지로 장치 버퍼를 비웠다. 버려진 만큼 디코더를 되돌려, 다시 재생할 때 멈춘 자리에서 잇는다.
    /// 두 곡이 겹치는 중이면 되돌리지 않는다 — 버퍼 깊이(0.1초 남짓)를 잃는 쪽이 겹침을 다시 맞추는 것보다 낫다.
    /// </summary>
    public void RewindAfterFlush(double flushedMs)
    {
        if (_current is null || _fading || flushedMs <= 0 || _current.Position < 0) return;
        var frames = (long)(flushedMs * _current.Format.Rate / 1000);
        _current.Seek(Math.Max(0, _current.Position - frames));
        _ended = false;
        _dsp.Reset();
    }

    /// <summary>
    /// 타임라인의 지금 자리에서 이어질 조각 하나를 읽는다.
    /// </summary>
    /// <param name="timelineNow">Core 타임라인의 현재 위치(ms). 읽기가 오래 걸리면 다시 묻는다.</param>
    /// <param name="startsAtTop">이 타임라인은 곡의 처음(0)에서 시작했는가.</param>
    /// <param name="deviceEmpty">장치에 아직 아무것도 없는가. 그렇다면 늦게 읽힌 조각을 버리고 다시 읽을 수 있다.</param>
    public LocalChunk? Read(
        Func<long> timelineNow, bool startsAtTop, bool deviceEmpty, TrackRef current, TrackRef? next, int wantMs)
    {
        _coreNext = next;
        if (_current is null)
        {
            var source = _open(current.Path);
            if (source is null) return null;
            _current = new Deck(current, source);
            _biasMs = 0;
            _ahead = 0;
            _behind = false;
            _ended = false;
        }

        var deck = _current;
        var rate = deck.Format.Rate;
        if (rate <= 0) return null;

        var now = timelineNow();
        var requested = RequestedFrame(now, rate);
        var placed = false;
        var anchored = false;

        if (deck.Position < 0)
        {
            anchored = requested >= 0 && startsAtTop && _biasMs == 0
                       && (_startFromTop || (AnchorTrackStart && requested * 1000 / rate <= HeadAnchorMs));
            _startFromTop = false;
            deck.Seek(anchored ? 0 : Math.Max(0, requested));
            placed = true;
        }
        else if (!_fading && RenderGate.LocalCursorLost(deck.Position, Math.Max(0, requested), rate))
        {
            // 타임라인에서 벗어났다. 따라잡거나 되감는다. 앞뒤가 이어지지 않으므로 필터의 기억도 지운다.
            deck.Seek(Math.Max(0, requested));
            _ended = false;
            _dsp.Reset();
            placed = true;
        }

        PrepareSuccessor(current);

        var frames = Math.Max(1, wantMs * rate / 1000);
        var segment = ReadSegment(frames);

        if (placed)
        {
            // 자리를 잡는 읽기는 오래 걸릴 수 있다. 디코더를 열고 그 자리를 찾아가는 동안 타임라인은 흘러간다.
            for (var retry = 0; retry < 3; retry++)
            {
                var after = timelineNow();
                if (anchored)
                {
                    // 첫 샘플부터 내기로 했다. 지금 시각을 이 곡의 0 으로 삼는다.
                    _biasMs = after;
                    break;
                }

                // 그사이 이 조각은 지난 소리가 됐다. 장치가 비어 있으면 버리고 지금 자리에서 다시 읽는다 —
                // 디코더가 이미 그 근처에 있어 이번에는 금방이다. 그대로 넣으면 지난 소리가 잠깐 났다가 건너뛴다.
                if (!deviceEmpty || _fading || segment is null
                    || !RenderGate.IsStaleLocalRead(after - now, wantMs))
                {
                    break;
                }

                now = after;
                _current.Seek(Math.Max(0, RequestedFrame(now, rate)));
                _ended = false;
                segment = ReadSegment(frames);
            }
        }

        return Finish(segment);
    }

    /// <summary>
    /// 타임라인이 곡의 끝에서 멈췄다. 디코더에 남은 것을 끝까지 이어 읽는다 —
    /// 출력은 타임라인보다 늦게 나가므로 Core 가 멈췄다고 알릴 때 아직 낼 소리가 남아 있다.
    /// </summary>
    public LocalChunk? ReadTail(int wantMs)
    {
        if (_current is null || _ended || _current.Position < 0) return null;
        var frames = Math.Max(1, wantMs * _current.Format.Rate / 1000);
        return Finish(ReadSegment(frames));
    }

    private long RequestedFrame(long timelineMs, int rate) => (long)((timelineMs - _biasMs) * rate / 1000);

    // ── 다음 곡 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 지금 내고 있는 곡 뒤에 이어질 곡. 이쪽이 Core 와 같은 곡에 있으면 Core 가 말한 다음 곡이고,
    /// Core 가 먼저 넘어갔으면 Core 의 지금 곡이 그것이다. 이쪽이 먼저 넘어와 있으면 Core 는 아직
    /// 그다음을 말해 주지 않았다.
    /// </summary>
    private TrackRef? Successor(TrackRef coreCurrent)
        => _behind ? coreCurrent : _ahead > 0 ? null : _coreNext;

    private void PrepareSuccessor(TrackRef coreCurrent)
    {
        if (_fading || _current is null) return;

        var wanted = Transition == TrackTransition.Off ? null : Successor(coreCurrent);
        if (wanted is null)
        {
            DropUpcoming();
            return;
        }

        if (_upcoming is not null)
        {
            if (_upcoming.Track == wanted) return;
            DropUpcoming();
        }

        if (_opening is not null && _openingFor != wanted)
        {
            Abandon(_opening);
            _opening = null;
        }

        if (_opening is null)
        {
            if (_failed == wanted) return;

            // 곡의 끝이 가까워졌을 때 연다. 길이를 모르면 지금 연다.
            var total = _current.TotalFrames;
            if (total > 0)
            {
                var remainingMs = _current.Format.MsOf(total - Math.Max(0, _current.Position));
                var lead = PrepareLeadMs + (Transition == TrackTransition.Crossfade ? CrossfadeMs : 0);
                if (remainingMs > lead) return;
            }

            var track = wanted.Value;
            _openingFor = track;
            _opening = _backgroundOpen ? Task.Run(() => OpenDeck(track)) : Task.FromResult(OpenDeck(track));
        }

        if (!_opening.IsCompleted) return;

        var opened = _opening.IsCompletedSuccessfully ? _opening.Result : null;
        _opening = null;
        if (opened is null)
        {
            // 열리지 않는 파일을 조각마다 다시 열려고 하지 않는다.
            _failed = wanted;
            return;
        }

        _upcoming = opened;
    }

    private Deck? OpenDeck(TrackRef track)
    {
        ILocalChunkSource? source = null;
        try
        {
            source = _open(track.Path);
            if (source is null) return null;
            var deck = new Deck(track, source);
            deck.Prefetch(PrefetchMs);
            return deck;
        }
        catch
        {
            source?.Dispose();
            return null;
        }
    }

    private static void Abandon(Task<Deck?> opening)
        => opening.ContinueWith(
            t => { if (t.IsCompletedSuccessfully) t.Result?.Dispose(); },
            TaskScheduler.Default);

    private void DropUpcoming()
    {
        _upcoming?.Dispose();
        _upcoming = null;
        if (_opening is not null)
        {
            Abandon(_opening);
            _opening = null;
        }

        _openingFor = null;
    }

    /// <summary>열어 둔 다음 곡을 지금 스트림에 그대로 이어 붙일 수 있는가.</summary>
    private Deck? JoinableSuccessor()
        => Transition != TrackTransition.Off
           && _upcoming is { } next
           && _current is { } current
           && next.Format == current.Format
            ? next
            : null;

    // ── 읽기 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 한 조각. 손대지 않은 바이트(Pcm)이거나, 두 곡을 섞은 float(Mix) 둘 중 하나다.
    /// 섞인 구간과 그렇지 않은 구간을 한 조각에 담지 않는다 — 섞지 않은 샘플은 그대로 나가야 한다.
    /// </summary>
    private sealed record Segment(byte[]? Pcm, float[]? Mix, PcmFormat Format);

    private Segment? ReadSegment(int frames)
    {
        var deck = _current!;
        if (_fading) return ReadOverlap(frames);

        var next = JoinableSuccessor();
        if (next is not null && Transition == TrackTransition.Crossfade && deck.TotalFrames > 0)
        {
            var fade = (long)CrossfadeMs * deck.Format.Rate / 1000;
            fade = Math.Min(fade, deck.TotalFrames / 2);
            if (next.TotalFrames > 0) fade = Math.Min(fade, next.TotalFrames / 2);

            if (fade >= (long)MinFadeMs * deck.Format.Rate / 1000)
            {
                var fadeStart = deck.TotalFrames - fade;
                if (deck.Position >= fadeStart)
                {
                    // 겹침을 시작한다. 탐색으로 겹칠 구간 한가운데에 떨어졌으면 남은 만큼만 겹친다.
                    // 길이 정보가 실제보다 짧아 끝을 이미 지났더라도 계단처럼 바꾸지는 않는다.
                    _fading = true;
                    _fadeStartedAt = deck.Position;
                    _fadeLength = Math.Max((long)MinFadeMs * deck.Format.Rate / 1000, deck.TotalFrames - deck.Position);
                    _fadeDone = 0;
                    if (next.Position < 0) next.Seek(0);
                    return ReadOverlap(frames);
                }

                // 겹침이 시작되는 자리에서 조각을 끊는다. 그 앞은 손대지 않은 바이트로 나간다.
                frames = (int)Math.Min(frames, fadeStart - deck.Position);
            }
        }

        var pcm = deck.ReadNext(frames);
        var got = pcm.Length / deck.Format.BytesPerFrame;
        if (got >= frames) return new Segment(pcm, null, deck.Format);

        // 곡의 끝이다.
        if (next is null)
        {
            _ended = true;
            return got == 0 ? null : new Segment(pcm, null, deck.Format);
        }

        // 끊김 없이 잇기: 마지막 샘플 바로 뒤에 다음 곡의 첫 샘플.
        var format = deck.Format;
        SwitchTo(next, deck.Position);
        var rest = _current!.ReadNext(frames - got);
        if (pcm.Length == 0) return rest.Length == 0 ? null : new Segment(rest, null, format);
        if (rest.Length == 0) return new Segment(pcm, null, format);

        var joined = new byte[pcm.Length + rest.Length];
        pcm.CopyTo(joined, 0);
        rest.CopyTo(joined, pcm.Length);
        return new Segment(joined, null, format);
    }

    /// <summary>
    /// 두 곡이 겹치는 구간. 같은 세기의 곡선(cos/sin)으로 섞는다 — 서로 다른 두 곡은 상관이 없는
    /// 신호라, 직선으로 섞으면 가운데에서 소리가 3dB 꺼진다. 이 곡선은 합친 에너지를 일정하게 지킨다.
    /// </summary>
    private Segment? ReadOverlap(int frames)
    {
        var outgoing = _current!;
        var incoming = _upcoming!;
        var format = outgoing.Format;
        var ch = Math.Max(1, format.Channels);

        var count = (int)Math.Min(frames, _fadeLength - _fadeDone);
        if (count <= 0)
        {
            SwitchTo(incoming, _fadeStartedAt);
            return ReadSegment(frames);
        }

        // 한쪽이 먼저 끝나면(길이 정보가 실제와 다를 때) 모자란 만큼은 무음으로 본다.
        var a = PcmMath.ToFloat(outgoing.ReadNext(count), format.Depth);
        var b = PcmMath.ToFloat(incoming.ReadNext(count), format.Depth);
        var mix = new float[count * ch];

        for (var f = 0; f < count; f++)
        {
            var t = (_fadeDone + f + 0.5) / _fadeLength;
            var down = (float)Math.Cos(t * Math.PI / 2);
            var up = (float)Math.Sin(t * Math.PI / 2);
            for (var c = 0; c < ch; c++)
            {
                var i = f * ch + c;
                mix[i] = (i < a.Length ? a[i] * down : 0f) + (i < b.Length ? b[i] * up : 0f);
            }
        }

        _fadeDone += count;
        if (_fadeDone >= _fadeLength)
        {
            SwitchTo(incoming, _fadeStartedAt);
        }

        return new Segment(null, mix, format);
    }

    /// <summary>
    /// 다음 곡이 지금 곡이 된다. <paramref name="joinedAt"/> 은 새 곡의 첫 샘플이 놓인 자리를
    /// 앞 곡의 프레임으로 센 것 — 그만큼을 타임라인과의 차이에 더해 두면, 타임라인이 계속 앞 곡 기준으로
    /// 흐르더라도 요청하는 자리는 새 곡 안에서 이어진다.
    /// </summary>
    private void SwitchTo(Deck next, long joinedAt)
    {
        var previous = _current!;
        _biasMs += previous.Format.MsOf(joinedAt);
        previous.Dispose();

        _current = next;
        _upcoming = null;
        _openingFor = null;
        _fading = false;
        _ended = false;

        if (_behind) _behind = false;
        else _ahead++;
    }

    /// <summary>
    /// 조각을 장치로 내려갈 모양으로 만든다. 섞지도 가공하지도 않은 조각은 바이트를 그대로 넘긴다.
    /// 계산을 거친 조각은 정수로 되돌릴 때 한 번만 디더를 건다.
    /// </summary>
    private LocalChunk? Finish(Segment? segment)
    {
        if (segment is null) return null;

        var format = segment.Format;
        if (segment.Mix is null && !Dsp.Active)
        {
            return new LocalChunk(segment.Pcm!, format);
        }

        var samples = segment.Mix ?? PcmMath.ToFloat(segment.Pcm!, format.Depth);
        var depth = format.Depth;
        var rate = format.Rate;

        if (Dsp.Active)
        {
            var upsample = Dsp.Upsamples && (CanOpenRate?.Invoke(rate * 2) ?? false);
            samples = _dsp.Process(samples, rate, format.Channels, Dsp, upsample, out rate);
            depth = 24;
        }

        return new LocalChunk(PcmMath.FromFloat(samples, depth, _dither), new PcmFormat(rate, depth, format.Channels));
    }

    public void Dispose() => Stop();

    /// <summary>
    /// 열어 둔 곡 하나. 미리 디코드해 둔 머리가 있으면 그것부터 내준다.
    /// </summary>
    private sealed class Deck(TrackRef track, ILocalChunkSource source) : IDisposable
    {
        private byte[] _head = [];
        private int _headAt;

        public TrackRef Track { get; } = track;
        public PcmFormat Format => source.Format;
        public long TotalFrames => source.TotalFrames;

        private int HeadFrames => (_head.Length - _headAt) / Format.BytesPerFrame;

        /// <summary>다음에 내줄 프레임. 아직 자리를 잡지 않았으면 -1.</summary>
        public long Position => source.Position < 0 ? -1 : source.Position - HeadFrames;

        public void Seek(long frame)
        {
            _head = [];
            _headAt = 0;
            source.Seek(frame);
        }

        /// <summary>처음부터 조금 읽어 둔다. 디코더를 데우는 값을 재생이 넘어오기 전에 치른다.</summary>
        public void Prefetch(int ms)
        {
            source.Seek(0);
            _head = source.ReadNext(Math.Max(1, ms * Format.Rate / 1000));
            _headAt = 0;
        }

        public byte[] ReadNext(int frames)
        {
            var bpf = Format.BytesPerFrame;
            var available = _head.Length - _headAt;
            if (available <= 0) return source.ReadNext(frames);

            var want = frames * bpf;
            if (want <= available)
            {
                var slice = _head.AsSpan(_headAt, want).ToArray();
                _headAt += want;
                return slice;
            }

            var rest = source.ReadNext(frames - available / bpf);
            var joined = new byte[available + rest.Length];
            _head.AsSpan(_headAt, available).CopyTo(joined);
            rest.CopyTo(joined, available);
            _headAt = _head.Length;
            return joined;
        }

        public void Dispose() => source.Dispose();
    }
}
