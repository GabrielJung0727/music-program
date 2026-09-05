using System.Text.Json;
using Mono.Shared;

namespace Mono.Protocol;

/// <summary>
/// Core <see cref="MessageTypes.RoomState"/> 페이로드의 타입 계약.
/// Control이 손파싱 대신 이걸 쓴다. 모르는 필드는 무시해 Core 선행 배포를 견딘다.
/// </summary>
public sealed class RoomSnapshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public RoomMode Mode { get; set; }
    public PlaybackSourceMode SourceMode { get; set; }
    public QualityPolicy QualityPolicy { get; set; }

    // DSP
    public DspPresetKind DspPreset { get; set; }
    public bool DspEnabled { get; set; }
    public bool DspLocked { get; set; }
    public string? ConvolutionIrPath { get; set; }
    public string? EasyEqJson { get; set; }
    public bool EasyEqGraphicMode { get; set; }
    public double HeadroomDb { get; set; }
    public double SpeakerDelayMsLeft { get; set; }
    public double SpeakerDelayMsRight { get; set; }
    public double SpeakerGainLeftDb { get; set; }
    public double SpeakerGainRightDb { get; set; }
    public string? DeviceEqProfile { get; set; }

    // 정책 플래그
    public bool SeekingAllowed { get; set; }
    public bool CommentsAllowed { get; set; }
    public bool ChatCollapsed { get; set; }
    public bool QueueLocked { get; set; }
    public bool FollowHostView { get; set; }
    public bool AutoAdvance { get; set; }
    public bool SmartAutoplay { get; set; }
    public int LinerPage { get; set; }
    public double LinerScrollY { get; set; }
    public int MaxMembers { get; set; }
    public string? InviteCode { get; set; }
    public DateTimeOffset? InviteExpiresAt { get; set; }

    // 아카이브 정책
    public bool ArchiveDefaultConsent { get; set; }
    public int CommentRetentionDays { get; set; }
    public bool AnonymizeArchive { get; set; }
    public bool CloudSyncOptIn { get; set; }

    // 재생 타임라인
    public bool Playing { get; set; }
    public int QueueIndex { get; set; }
    public string? HostPeerId { get; set; }
    public long ResyncEpoch { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public long MediaTimeMs { get; set; }
    public long DurationMs { get; set; }
    public long MediaOriginUnixMs { get; set; }
    public long MediaTimeAtOriginMs { get; set; }

    // 신뢰 UI
    public bool BitPerfect { get; set; }
    public bool SrcApplied { get; set; }
    public string? PathBadge { get; set; }

    public int CatalogCount { get; set; }

    /// <summary>깨진 JSON이면 null. 호출자가 화면을 유지할 수 있게 예외를 던지지 않는다.</summary>
    public static RoomSnapshot? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<RoomSnapshot>(json, LineFraming.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
