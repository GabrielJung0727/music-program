using Mono.Protocol;

namespace Mono.Control.Services;

/// <summary>
/// 플레이리스트 · 세션 아카이브. 공유는 트랙 식별자·메타만 오간다(파일 재배포 아님).
/// 아카이브 생성 자체는 EndSessionAsync(consent)가 하고, Core가 archive 응답을 돌려준다.
/// </summary>
public sealed partial class CoreSession
{
    public Task CreatePlaylistAsync(string name, IReadOnlyList<string> trackIds)
        => SendAsync(new MonoMessage
        {
            Type = MessageTypes.CreatePlaylist,
            Text = name,
            TrackIds = [.. trackIds]
        });

    /// <summary>세션 아카이브의 하이라이트(♥·핀·완청)로 플레이리스트를 만든다.</summary>
    public Task CreatePlaylistFromArchiveAsync(string archiveId)
        => SendAsync(new MonoMessage { Type = MessageTypes.CreatePlaylist, ArchiveId = archiveId });

    /// <summary>replace=true면 큐를 비우고 싣는다.</summary>
    public Task LoadPlaylistAsync(string playlistId, bool replace = false)
        => SendAsync(new MonoMessage { Type = MessageTypes.LoadPlaylist, PlaylistId = playlistId, Flag = replace });

    public Task LoadArchiveAsync(string archiveId, bool replace = false)
        => SendAsync(new MonoMessage { Type = MessageTypes.LoadPlaylist, ArchiveId = archiveId, Flag = replace });

    /// <summary>playlistId가 null이면 Core가 첫 플레이리스트를 쓴다.</summary>
    public Task ExportM3uAsync(string? playlistId = null)
        => SendAsync(new MonoMessage { Type = MessageTypes.ExportM3u, PlaylistId = playlistId });

    /// <summary>archiveId가 null이면 Core가 최근 아카이브를 쓴다.</summary>
    public Task ShareSessionAsync(string? archiveId = null)
        => SendAsync(new MonoMessage { Type = MessageTypes.ShareSession, ArchiveId = archiveId });
}
