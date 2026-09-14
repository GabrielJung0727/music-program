using Mono.Protocol;

namespace Mono.Control.ViewModels;

/// <summary>하단 바 장치 목록의 한 줄. 스냅샷의 outputs 항목을 화면용으로 옮긴 것.</summary>
public sealed class OutputDevice
{
    public required string PeerId { get; init; }
    public required string Name { get; init; }
    public string? Badge { get; init; }
    public string? Note { get; init; }
    public int VolumePercent { get; init; }
    public bool HardwareVolume { get; init; }
    public bool Spectator { get; init; }
    public string Caps { get; init; } = "";

    public static OutputDevice From(SnapshotOutput o) => new()
    {
        PeerId = o.PeerId,
        Name = string.IsNullOrWhiteSpace(o.DisplayName) ? o.PeerId : o.DisplayName!,
        Badge = o.Badge,
        Note = o.Note,
        VolumePercent = o.VolumePercent,
        HardwareVolume = o.HardwareVolume,
        Spectator = o.Spectator,
        Caps = $"{o.MaxSampleRate / 1000}kHz · {o.MaxBitDepth}bit"
               + (o.SupportsDsd ? " · DSD" : "")
               + (o.ExclusiveMode ? " · Exclusive" : " · Shared")
    };

    /// <summary>참관 중이거나 포맷이 안 맞으면 사유가 능력 표시 뒤에 붙는다.</summary>
    public string Detail => string.IsNullOrWhiteSpace(Note) ? Caps : $"{Caps} · {Note}";
}
