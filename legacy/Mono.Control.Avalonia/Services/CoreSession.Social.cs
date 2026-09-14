using Mono.Protocol;

namespace Mono.Control.Services;

/// <summary>타임스탬프 핀 · 반응 · 관계도 — 전부 컨트롤 채널이다. 오디오 경로와 무관하다.</summary>
public sealed partial class CoreSession
{
    public Task PinAsync(long mediaTimeMs, string text)
        => SendAsync(new MonoMessage { Type = MessageTypes.Pin, MediaTimeMs = mediaTimeMs, Text = text });

    public Task RemovePinAsync(string pinId)
        => SendAsync(new MonoMessage { Type = MessageTypes.RemovePin, Text = pinId });

    /// <summary>핀 위치로 이동. 시킹 정책이 막으면 Core가 error를 돌려준다.</summary>
    public Task SeekPinAsync(string pinId)
        => SendAsync(new MonoMessage { Type = MessageTypes.SeekPin, Text = pinId });

    /// <summary>세션 전체를 합산한 트랙 히트맵. 룸 스냅샷의 heatmap은 현재 룸만 담는다.</summary>
    public Task ReactionHeatmapAsync(string trackId)
        => SendAsync(new MonoMessage { Type = MessageTypes.ReactionHeatmap, TrackId = trackId });

    public Task FollowArtistAsync(string artistId)
        => SendAsync(new MonoMessage { Type = MessageTypes.FollowArtist, Text = artistId });

    /// <summary>뮤지션 관계도 조회. Core 로컬 그래프 쿼리이므로 클라우드 왕복이 없다.</summary>
    public Task GraphAsync(string artistId)
        => SendAsync(new MonoMessage { Type = MessageTypes.Graph, Text = artistId });
}
