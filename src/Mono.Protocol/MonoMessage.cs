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

    public static MonoMessage Event(string type) => new() { Type = type };
}
