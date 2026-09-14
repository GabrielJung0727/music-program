using System.Text.Json.Serialization;
using Mono.Shared;

namespace Mono.Control.ViewModels;

/// <summary>list_zones 응답 한 건. 멀티 디바이스 존은 싱글 플레이 전용이다.</summary>
public sealed class ZoneItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("mode")] public ZoneMode Mode { get; set; }
    [JsonPropertyName("syncRoomId")] public string? SyncRoomId { get; set; }
    [JsonPropertyName("members")] public List<ZoneMember> Members { get; set; } = [];

    public string ModeLabel => Mode == ZoneMode.Sync ? "동시 재생" : "기기별 독립";

    public string MemberSummary => Members.Count == 0
        ? "기기 없음"
        : $"{Members.Count}대 · 온라인 {Members.Count(m => m.Online)}대";
}

public sealed class ZoneMember
{
    [JsonPropertyName("peerId")] public string PeerId { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("online")] public bool Online { get; set; }
    [JsonPropertyName("roomId")] public string? RoomId { get; set; }
}
