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

/// <summary>
/// 빈 방 청소부.
///
/// 나가기는 멤버만 지우고 방은 남긴다. 지우는 경로가 없던 동안 한 번 만들어진 방은 Core 가
/// 죽을 때까지 라운지 목록에 남았고, 재생할 때마다 유령 라운지가 하나씩 쌓였다.
/// 유예를 두는 건 새로고침·재접속으로 잠깐 비는 순간에 듣던 큐까지 날리지 않기 위해서다.
/// </summary>
public sealed class RoomJanitor : BackgroundService
{
    /// <summary>비어 있어도 이 시간까지는 기다린다. 새로고침 한 번은 여기 안에서 끝난다.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly RoomManager _rooms;
    private readonly ILogger<RoomJanitor> _log;

    public RoomJanitor(RoomManager rooms, ILogger<RoomJanitor> log)
    {
        _rooms = rooms;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var removed = _rooms.SweepEmpty(Grace, DateTimeOffset.UtcNow);
                if (removed.Count > 0)
                {
                    _log.LogInformation("빈 방 {Count}개 정리: {Ids}", removed.Count, string.Join(", ", removed));
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "room sweep");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
