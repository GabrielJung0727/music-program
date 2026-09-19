using DiscordRPC;
using DiscordRPC.Logging;

namespace Mono.Control;

/// <summary>
/// Discord 활동 카드. 친구 목록에 "Listening to Mono" + 곡 제목·아티스트·진행 바가 뜬다.
///
/// Discord 가 살아 있지 않으면 조용히 쉰다. 오디오 경로와는 완전히 별개다.
/// Application ID 는 Discord Developer Portal 에서 만든 앱의 숫자다 — 없으면 보내지 않는다.
/// </summary>
public sealed class DiscordPresence : IDisposable
{
    /// <summary>
    /// Discord 앱 "Mono" 의 Application ID.
    /// Portal 에서 앱을 만들고 여기(또는 prefs.ini discord.appId)에 넣으면 바로 나간다.
    /// </summary>
    public const string DefaultApplicationId = "1550196127557615667";

    private DiscordRpcClient? _client;
    private string? _appId;
    private bool _enabled = Prefs.Get("discord.enabled", "1") != "0";

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            Prefs.Set("discord.enabled", value ? "1" : "0");
            if (!value) Clear();
        }
    }

    public bool Connected => _client is { IsInitialized: true, IsDisposed: false };

    public string ApplicationId
    {
        get
        {
            var stored = Prefs.Get("discord.appId", "");
            return !string.IsNullOrWhiteSpace(stored) ? stored.Trim() : DefaultApplicationId;
        }
        set
        {
            Prefs.Set("discord.appId", value.Trim());
            DisposeClient();
        }
    }

    public void SetNowPlaying(
        string? title,
        string? artist,
        string? album,
        string? artUrl,
        bool playing,
        long mediaOriginUnixMs,
        long mediaTimeAtOriginMs,
        long durationMs)
    {
        if (!_enabled)
        {
            Clear();
            return;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            Clear();
            return;
        }

        if (!EnsureClient()) return;

        var presence = new RichPresence
        {
            Type = ActivityType.Listening,
            Details = Clip(title),
            State = Clip(string.IsNullOrWhiteSpace(artist)
                ? album
                : string.IsNullOrWhiteSpace(album) ? artist : $"{artist} · {album}"),
            Assets = new Assets
            {
                LargeImageKey = PublicArt(artUrl) ?? "logo",
                LargeImageText = Clip(album ?? title, 128),
            },
        };

        if (playing && durationMs > 0 && mediaOriginUnixMs > 0)
        {
            var started = DateTimeOffset.FromUnixTimeMilliseconds(mediaOriginUnixMs - mediaTimeAtOriginMs);
            presence.Timestamps = new Timestamps
            {
                Start = started.UtcDateTime,
                End = started.AddMilliseconds(durationMs).UtcDateTime,
            };
        }

        try { _client!.SetPresence(presence); }
        catch (Exception ex) { AppLog.Write("discord", ex.Message); }
    }

    public void Clear()
    {
        try { _client?.ClearPresence(); }
        catch (Exception ex) { AppLog.Write("discord", ex.Message); }
    }

    public void Dispose()
    {
        DisposeClient();
    }

    private bool EnsureClient()
    {
        var id = ApplicationId;
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (_client is { IsInitialized: true, IsDisposed: false } && _appId == id) return true;

        DisposeClient();
        try
        {
            _client = new DiscordRpcClient(id)
            {
                Logger = new NullLogger(),
                SkipIdenticalPresence = true,
            };
            if (!_client.Initialize())
            {
                AppLog.Write("discord", "Discord 에 연결하지 못했습니다 — 데스크톱 앱이 켜져 있는지 보세요.");
                DisposeClient();
                return false;
            }

            _appId = id;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("discord", ex.Message);
            DisposeClient();
            return false;
        }
    }

    private void DisposeClient()
    {
        try { _client?.Dispose(); }
        catch { /* Discord 가 이미 꺼졌으면 여기도 죽을 수 있다 */ }
        _client = null;
        _appId = null;
    }

    private static string? PublicArt(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("https" or "http")) return null;
        if (uri.IsLoopback) return null;
        return uri.AbsoluteUri.Length <= 256 ? uri.AbsoluteUri : null;
    }

    private static string? Clip(string? text, int max = 128)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
