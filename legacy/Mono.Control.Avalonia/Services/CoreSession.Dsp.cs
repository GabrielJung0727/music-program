using Mono.Protocol;

namespace Mono.Control.Services;

/// <summary>DSP 체인. Audiophile 모드나 dsp_lock이면 Core가 거부한다.</summary>
public sealed partial class CoreSession
{
    public Task SetDspAsync(int preset) => SendAsync(new MonoMessage { Type = MessageTypes.SetDsp, Dsp = (Mono.Shared.DspPresetKind)preset });
    public Task SetEasyEqAsync(string json, bool graphic) => SendAsync(new MonoMessage
    {
        Type = MessageTypes.SetEasyEq,
        Body = json,
        Flag = graphic
    });
    public Task SetConvolutionIrAsync(string? path) => SendAsync(new MonoMessage
    {
        Type = MessageTypes.SetConvolutionIr,
        Path = path
    });
    public Task SetSpeakerSetupAsync(string csv) => SendAsync(new MonoMessage
    {
        Type = MessageTypes.SetSpeakerSetup,
        Text = csv
    });
    public Task SetHeadroomAsync(float db) => SendAsync(new MonoMessage
    {
        Type = MessageTypes.SetHeadroom,
        Text = db.ToString(System.Globalization.CultureInfo.InvariantCulture)
    });
    public Task SetDeviceEqAsync(string profile) => SendAsync(new MonoMessage
    {
        Type = MessageTypes.SetDeviceEq,
        Text = profile
    });
}
