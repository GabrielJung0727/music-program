using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Mono.Control.Models;
using Mono.Control.Services;
using Mono.Protocol;
using Mono.Shared;

namespace Mono.Control.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (UpdateBusy) return;
        UpdateBusy = true;
        UpdateProgress = 0;
        try
        {
            UpdateStatus = "업데이트 확인 중…";
            UpdateStatus = await _updater.CheckAsync();
            UpdateReady = _updater.Pending is not null;
        }
        catch (Exception ex)
        {
            UpdateReady = false;
            UpdateStatus = UpdateCheckErrors.Describe(ex);
        }
        finally
        {
            UpdateBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        if (UpdateBusy) return;
        if (_updater.Pending is null)
        {
            await CheckForUpdatesAsync();
            if (_updater.Pending is null) return;
        }

        UpdateBusy = true;
        try
        {
            UpdateStatus = "업데이트 받는 중…";
            var progress = new Progress<int>(p =>
            {
                UpdateProgress = p;
                UpdateStatus = $"업데이트 받는 중… {p}%";
            });
            await _updater.DownloadAsync(progress);
            UpdateStatus = "창을 닫고 설치한 뒤 다시 켭니다…";
            await ShutdownAsync();
            _updater.ApplyAndRestart();
        }
        catch (Exception ex)
        {
            UpdateStatus = "업데이트 실패: " + ex.Message;
            UpdateBusy = false;
        }
    }

}
