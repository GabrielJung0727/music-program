using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mono.Control.Services;
using Mono.Protocol;

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
    [ObservableProperty] private string _syncProbeText = "";
    [ObservableProperty] private string _artPerfText = "";

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
}
