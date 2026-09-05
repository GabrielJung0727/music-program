using Mono.Protocol;

namespace Mono.Control.Services;

/// <summary>카탈로그·검색·스캔·히스토리·스트리밍 연동. 검색은 Core가 다국어 별칭까지 매칭한다.</summary>
public sealed partial class CoreSession
{
    public Task CatalogAsync() => SendAsync(new MonoMessage { Type = MessageTypes.Catalog });
    public Task SearchAsync(string q) => SendAsync(new MonoMessage { Type = MessageTypes.Search, Text = q });
    public Task ScanAsync(string? path) => SendAsync(new MonoMessage { Type = MessageTypes.ScanLibrary, Path = path });
    public Task HistoryAsync() => SendAsync(new MonoMessage { Type = MessageTypes.History });
    public Task PlaylistsAsync() => SendAsync(new MonoMessage { Type = MessageTypes.Playlists });
    public Task ArchivesAsync() => SendAsync(new MonoMessage { Type = MessageTypes.Archives });
    public Task WikiAsync(string artistId) => SendAsync(new MonoMessage { Type = MessageTypes.WikiBio, Text = artistId });
    public Task EndpointsAsync() => SendAsync(new MonoMessage { Type = MessageTypes.Endpoints });
    public Task LinkStreamingAsync(int provider) => SendAsync(new MonoMessage { Type = MessageTypes.LinkStreaming, Provider = (Mono.Shared.StreamingProvider)provider, Token = "demo-token" });
    public Task BeginStreamingOAuthAsync(int provider) => SendAsync(new MonoMessage
    {
        Type = MessageTypes.LinkStreaming,
        Provider = (Mono.Shared.StreamingProvider)provider,
        Text = "oauth",
        Token = "oauth"
    });
}
