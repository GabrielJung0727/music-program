using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mono.Control.Services;
using Mono.Protocol;
using Mono.Shared;

namespace Mono.Control.ViewModels.Pages;

/// <summary>
/// Audio — DSP 체인과 동기화 측정. Audiophile 모드나 dsp_lock이면 Core가 변경을 거부하고
/// 사유를 error로 돌려주므로, 여기서는 막지 않고 그대로 보낸다.
/// </summary>
public sealed partial class AudioViewModel : PageViewModel
{
    public AudioViewModel(CoreSession session) : base(session) { }

    [ObservableProperty] private bool _eqGraphicMode;
    [ObservableProperty] private double _eqBand1;
    [ObservableProperty] private double _eqBand2;
    [ObservableProperty] private double _eqBand3;
    [ObservableProperty] private double _eqBand4;
    [ObservableProperty] private double _eqBand5;
    [ObservableProperty] private string _irPath = "";
    [ObservableProperty] private double _speakerDelayL;
    [ObservableProperty] private double _speakerDelayR;
    [ObservableProperty] private double _speakerGainL;
    [ObservableProperty] private double _speakerGainR;
    [ObservableProperty] private double _headroomDb = -3;
    [ObservableProperty] private string _deviceEqProfile = "harman";
    /// <summary>Core 가 돌려준 sync_probe 원문. 화면에는 요약만 보여 준다.</summary>
    [ObservableProperty] private string _syncProbeText = "";

    [ObservableProperty] private string _syncProbeSummary = "";

    public bool HasSyncProbe => !string.IsNullOrEmpty(SyncProbeSummary);

    partial void OnSyncProbeSummaryChanged(string value) => OnPropertyChanged(nameof(HasSyncProbe));

    /// <summary>
    /// 원시 JSON 대신 읽을 수 있는 한 줄로 바꾼다.
    /// Core 가 note 에 이미 한국어 안내를 담아 주므로 그걸 앞세운다.
    /// </summary>
    partial void OnSyncProbeTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) { SyncProbeSummary = ""; return; }
        try
        {
            var verdict = System.Text.Json.JsonDocument.Parse(value).RootElement.GetProperty("verdict");
            var peers = verdict.GetProperty("peerCount").GetInt32();
            var locked = verdict.GetProperty("lockedCount").GetInt32();
            var spread = verdict.GetProperty("offsetSpreadMs").GetDouble();
            var target = verdict.GetProperty("targetMs").GetDouble();
            var meets = verdict.GetProperty("meetsTarget").GetBoolean();
            var note = verdict.TryGetProperty("note", out var n) ? n.GetString() : null;

            var head = peers == 0
                ? "연결된 출력 기기가 없습니다."
                : $"기기 {peers}대 중 {locked}대 동기화 · 편차 {spread:0.0}ms (목표 {target:0}ms) · "
                  + (meets ? "목표 달성" : "목표 미달");

            SyncProbeSummary = string.IsNullOrWhiteSpace(note) ? head : head + Environment.NewLine + note;
        }
        catch
        {
            SyncProbeSummary = "동기화 측정 결과를 읽지 못했습니다.";
        }
    }
    [ObservableProperty] private string _artPerfText = "";

    public ObservableCollection<ZoneItem> Zones { get; } = new();
    public ObservableCollection<OutputDevice> Outputs { get; } = new();

    [ObservableProperty] private ZoneItem? _selectedZone;
    [ObservableProperty] private string _zoneRenameText = "";
    [ObservableProperty] private string _pairingCode = "";
    [ObservableProperty] private string _redeemCode = "";

    /// <summary>존 편집 블록 표시 여부. XAML 에서 컨버터를 쓰지 않도록 bool 로 낸다.</summary>
    public bool HasSelectedZone => SelectedZone is not null;

    partial void OnSelectedZoneChanged(ZoneItem? value) => OnPropertyChanged(nameof(HasSelectedZone));

    public void ApplyZones(IEnumerable<ZoneItem> zones)
    {
        var keep = SelectedZone?.Id;
        Zones.Clear();
        foreach (var z in zones) Zones.Add(z);
        SelectedZone = Zones.FirstOrDefault(z => z.Id == keep) ?? Zones.FirstOrDefault();
    }

    public void ApplyOutputs(IEnumerable<OutputDevice> outputs)
    {
        Outputs.Clear();
        foreach (var o in outputs) Outputs.Add(o);
    }

    public void ApplyPairingCode(string code) => PairingCode = code;

    public override void ApplySnapshot(RoomSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.ConvolutionIrPath))
            IrPath = snapshot.ConvolutionIrPath!;
    }

    public void SetIrPathFromPicker(string path)
    {
        IrPath = path;
        _ = Safe(() => Session.SetConvolutionIrAsync(path));
    }

    [RelayCommand]
    private Task SyncProbeAsync() => Safe(() => Session.SyncProbeAsync());

    [RelayCommand]
    private Task SetDspAsync(string? preset)
        => int.TryParse(preset, out var p) ? Safe(() => Session.SetDspAsync(p)) : Task.CompletedTask;

    [RelayCommand]
    private async Task ApplyEasyEqAsync()
    {
        var bands = new[]
        {
            new { f = 60f, g = (float)EqBand1, q = 0.7f },
            new { f = 250f, g = (float)EqBand2, q = 0.9f },
            new { f = 1000f, g = (float)EqBand3, q = 1.0f },
            new { f = 4000f, g = (float)EqBand4, q = 1.1f },
            new { f = 12000f, g = (float)EqBand5, q = 0.8f },
        };
        var json = JsonSerializer.Serialize(bands);
        await Safe(() => Session.SetEasyEqAsync(json, EqGraphicMode));
        StatusText = EqGraphicMode ? "Graphic EQ 적용" : "Parametric EQ 적용";
    }

    [RelayCommand]
    private Task ApplySpeakerSetupAsync()
    {
        var csv = string.Join(",",
            SpeakerDelayL.ToString(CultureInfo.InvariantCulture),
            SpeakerDelayR.ToString(CultureInfo.InvariantCulture),
            SpeakerGainL.ToString(CultureInfo.InvariantCulture),
            SpeakerGainR.ToString(CultureInfo.InvariantCulture));
        return Safe(() => Session.SetSpeakerSetupAsync(csv));
    }

    [RelayCommand]
    private Task ApplyHeadroomAsync() => Safe(() => Session.SetHeadroomAsync((float)HeadroomDb));

    [RelayCommand]
    private Task ApplyDeviceEqAsync(string? profile)
    {
        var p = string.IsNullOrWhiteSpace(profile) ? DeviceEqProfile : profile!;
        DeviceEqProfile = p;
        return Safe(() => Session.SetDeviceEqAsync(p));
    }

    [RelayCommand]
    private Task ClearIrAsync()
    {
        IrPath = "";
        return Safe(() => Session.SetConvolutionIrAsync(null));
    }

    // ── 존 관리 ─────────────────────────────────────────
    [RelayCommand]
    private Task RenameZoneAsync()
        => SelectedZone is null || string.IsNullOrWhiteSpace(ZoneRenameText)
            ? Task.CompletedTask
            : Safe(() => Session.RenameZoneAsync(SelectedZone.Id, ZoneRenameText.Trim()));

    [RelayCommand]
    private Task SetZoneSyncAsync()
        => SelectedZone is null ? Task.CompletedTask
            : Safe(() => Session.SetZoneModeAsync(SelectedZone.Id, ZoneMode.Sync));

    [RelayCommand]
    private Task SetZoneIndependentAsync()
        => SelectedZone is null ? Task.CompletedTask
            : Safe(() => Session.SetZoneModeAsync(SelectedZone.Id, ZoneMode.Independent));

    [RelayCommand]
    private Task DeleteZoneAsync()
        => SelectedZone is null ? Task.CompletedTask : Safe(() => Session.DeleteZoneAsync(SelectedZone.Id));

    [RelayCommand]
    private Task AddDeviceToZoneAsync(OutputDevice? device)
        => SelectedZone is null || device is null
            ? Task.CompletedTask
            : Safe(() => Session.ZoneAddMemberAsync(SelectedZone.Id, device.PeerId));

    [RelayCommand]
    private Task RemoveDeviceFromZoneAsync(ZoneMember? member)
        => SelectedZone is null || member is null
            ? Task.CompletedTask
            : Safe(() => Session.ZoneRemoveMemberAsync(SelectedZone.Id, member.PeerId));

    // ── 원격 페어링 ─────────────────────────────────────
    [RelayCommand]
    private Task IssuePairingAsync() => Safe(() => Session.PairAsync());

    [RelayCommand]
    private Task RedeemPairingAsync()
        => string.IsNullOrWhiteSpace(RedeemCode)
            ? Task.CompletedTask
            : Safe(() => Session.RedeemAsync(RedeemCode.Trim()));
}
