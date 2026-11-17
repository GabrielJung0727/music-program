using System.Text.Json.Serialization;
using Mono.Shared;

namespace Mono.Protocol;

public sealed class MonoMessage
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("peerId")]
    public string? PeerId { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("roomId")]
    public string? RoomId { get; set; }

    [JsonPropertyName("roomName")]
    public string? RoomName { get; set; }

    [JsonPropertyName("mode")]
    public RoomMode? Mode { get; set; }

    [JsonPropertyName("trackId")]
    public string? TrackId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("mediaTimeMs")]
    public long? MediaTimeMs { get; set; }

    [JsonPropertyName("consent")]
    public bool? Consent { get; set; }

    [JsonPropertyName("ok")]
    public bool? Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("t1")]
    public long? T1 { get; set; }

    [JsonPropertyName("t2")]
    public long? T2 { get; set; }

    [JsonPropertyName("t3")]
    public long? T3 { get; set; }

    [JsonPropertyName("t4")]
    public long? T4 { get; set; }

    [JsonPropertyName("offsetMs")]
    public double? OffsetMs { get; set; }

    [JsonPropertyName("jitterMs")]
    public double? JitterMs { get; set; }

    [JsonPropertyName("rttMs")]
    public double? RttMs { get; set; }

    [JsonPropertyName("bufferMs")]
    public int? BufferMs { get; set; }

    [JsonPropertyName("resyncs")]
    public int? Resyncs { get; set; }

    [JsonPropertyName("locked")]
    public bool? Locked { get; set; }

    /// <summary>출력 장치 상태 머신의 현재 값.</summary>
    public int? DeviceState { get; set; }

    /// <summary>재생을 보류한 사유(점유 충돌·장치 분리 등).</summary>
    public string? DeviceError { get; set; }

    [JsonPropertyName("epoch")]
    public long? Epoch { get; set; }

    [JsonPropertyName("mediaOriginUnixMs")]
    public long? MediaOriginUnixMs { get; set; }

    [JsonPropertyName("mediaTimeAtOriginMs")]
    public long? MediaTimeAtOriginMs { get; set; }

    [JsonPropertyName("playing")]
    public bool? Playing { get; set; }

    [JsonPropertyName("sampleRate")]
    public int? SampleRate { get; set; }

    [JsonPropertyName("bitDepth")]
    public int? BitDepth { get; set; }

    [JsonPropertyName("channels")]
    public int? Channels { get; set; }

    [JsonPropertyName("isDsd")]
    public bool? IsDsd { get; set; }

    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; set; }

    [JsonPropertyName("localPath")]
    public string? LocalPath { get; set; }

    /// <summary>
    /// 지금 곡이 끝까지 재생되면 이어질 곡. 출력이 미리 열어 두었다가 끊김 없이 잇거나 겹친다.
    /// 정해지지 않았으면(큐의 끝, 자동 재생 후보를 아직 고르지 않음) 비어 있다.
    /// </summary>
    [JsonPropertyName("nextTrackId")]
    public string? NextTrackId { get; set; }

    [JsonPropertyName("nextLocalPath")]
    public string? NextLocalPath { get; set; }

    [JsonPropertyName("transition")]
    public TrackTransition? Transition { get; set; }

    [JsonPropertyName("crossfadeMs")]
    public int? CrossfadeMs { get; set; }

    /// <summary>
    /// 재생하던 것을 끊지 않고 이어 가도 되는 세대. <see cref="Epoch"/> 와 같을 때만 뜻이 있다 —
    /// 그 세대는 사용자가 곡을 넘긴 게 아니라 곡이 끝까지 재생되어 넘어간 것이다.
    /// </summary>
    [JsonPropertyName("seamlessEpoch")]
    public long? SeamlessEpoch { get; set; }

    [JsonPropertyName("inviteCode")]
    public string? InviteCode { get; set; }

    [JsonPropertyName("inviteAction")]
    public InviteAction? InviteAction { get; set; }

    [JsonPropertyName("minutes")]
    public int? Minutes { get; set; }

    [JsonPropertyName("targetPeerId")]
    public string? TargetPeerId { get; set; }

    [JsonPropertyName("member")]
    public MemberRole? Member { get; set; }

    [JsonPropertyName("index")]
    public int? Index { get; set; }

    /// <summary>
    /// 큐 항목의 id. 칸 번호(index)는 보내는 사이에 큐가 바뀌면 다른 곡을 가리킨다 —
    /// 같이 실려 오면 Core 는 이 id 로 항목을 찾는다.
    /// </summary>
    [JsonPropertyName("itemId")]
    public string? ItemId { get; set; }

    [JsonPropertyName("delta")]
    public int? Delta { get; set; }

    [JsonPropertyName("flag")]
    public bool? Flag { get; set; }

    [JsonPropertyName("volume")]
    public int? Volume { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("provider")]
    public StreamingProvider? Provider { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    /// <summary>
    /// peer id 의 주인임을 증명하는 값. Core 가 welcome 에 실어 주고, 같은 id 로 다시 붙을 때
    /// hello 에 실어 돌려준다. 룸 상태나 다른 사람에게 가는 메시지에는 절대 싣지 않는다.
    /// </summary>
    [JsonPropertyName("secret")]
    public string? Secret { get; set; }

    [JsonPropertyName("dsp")]
    public DspPresetKind? Dsp { get; set; }

    [JsonPropertyName("policy")]
    public QualityPolicy? Policy { get; set; }

    [JsonPropertyName("sourceMode")]
    public PlaybackSourceMode? SourceMode { get; set; }

    [JsonPropertyName("emoji")]
    public string? Emoji { get; set; }

    [JsonPropertyName("maxSampleRate")]
    public int? MaxSampleRate { get; set; }

    [JsonPropertyName("maxBitDepth")]
    public int? MaxBitDepth { get; set; }

    [JsonPropertyName("supportsDsd")]
    public bool? SupportsDsd { get; set; }

    [JsonPropertyName("exclusiveMode")]
    public bool? ExclusiveMode { get; set; }

    [JsonPropertyName("latencyMs")]
    public long? LatencyMs { get; set; }

    [JsonPropertyName("hardwareVolume")]
    public bool? HardwareVolume { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }

    [JsonPropertyName("pairingCode")]
    public string? PairingCode { get; set; }

    [JsonPropertyName("playlistId")]
    public string? PlaylistId { get; set; }

    [JsonPropertyName("archiveId")]
    public string? ArchiveId { get; set; }

    [JsonPropertyName("trackIds")]
    public List<string>? TrackIds { get; set; }

    [JsonPropertyName("zoneId")]
    public string? ZoneId { get; set; }

    [JsonPropertyName("zoneMode")]
    public ZoneMode? ZoneMode { get; set; }

    [JsonPropertyName("deadlineUnixMs")]
    public long? DeadlineUnixMs { get; set; }

    public static MonoMessage Event(string type) => new() { Type = type };
}
