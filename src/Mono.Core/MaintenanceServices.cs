using System.Text.Json;
using Mono.Protocol;

namespace Mono.Core;

/// <summary>라이브러리 스캔 스케줄. 기동 직후 1회, 이후 주기적으로 훑는다.</summary>
public sealed class ScanScheduler : BackgroundService
{
    private readonly LibraryScanner _scanner;
    private readonly CatalogStore _catalog;
    private readonly RoomBroadcaster _broadcaster;
    private readonly ILogger<ScanScheduler> _log;
    private readonly string _root;
    private readonly TimeSpan _interval;

    public ScanScheduler(
        LibraryScanner scanner,
        CatalogStore catalog,
        RoomBroadcaster broadcaster,
        IConfiguration config,
        ILogger<ScanScheduler> log,
        string root)
    {
        _scanner = scanner;
        _catalog = catalog;
        _broadcaster = broadcaster;
        _log = log;
        _root = root;
        var minutes = config.GetValue<int?>("Mono:ScanIntervalMinutes") ?? 30;
        _interval = TimeSpan.FromMinutes(Math.Max(1, minutes));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var before = _catalog.Tracks.Count;
                var count = _scanner.Scan(_root);
                if (_catalog.Tracks.Count != before)
                {
                    _log.LogInformation("라이브러리 스캔: 파일 {Count}개, 카탈로그 {Tracks}곡", count, _catalog.Tracks.Count);
                    await _broadcaster.PushCatalogAsync(new MonoMessage
                    {
                        Type = MessageTypes.Catalog,
                        Ok = true,
                        Index = _catalog.Tracks.Count,
                        Body = JsonSerializer.Serialize(_catalog.CatalogView(), LineFraming.JsonOptions)
                    });
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "라이브러리 스캔 실패");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}

/// <summary>보존 정책 집행 — 기간이 지난 코멘트를 아카이브에서 지운다.</summary>
public sealed class RetentionService : BackgroundService
{
    private readonly HistoryStore _history;
    private readonly ILogger<RetentionService> _log;

    public RetentionService(HistoryStore history, ILogger<RetentionService> log)
    {
        _history = history;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pruned = _history.PruneComments(DateTimeOffset.UtcNow);
                if (pruned > 0)
                {
                    _log.LogInformation("보존 정책: 코멘트 {Count}건 정리", pruned);
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "retention sweep");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }
}
