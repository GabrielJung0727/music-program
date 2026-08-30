using System.Collections.Concurrent;
using System.Text.Json;
using Mono.Protocol;
using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// Control에서 온 명령 하나를 룸 상태 변경으로 바꾸는 지점.
/// 전송 계층(TCP / SignalR)이 무엇이든 이 클래스만 거친다.
/// </summary>
public sealed class CommandProcessor
{
    private readonly RoomManager _rooms;
    private readonly CatalogStore _catalog;
    private readonly LibraryScanner _scanner;
    private readonly StreamingHub _streaming;
    private readonly PairingService _pairing;
    private readonly HistoryStore _history;
    private readonly EndpointRegistry _endpoints;
    private readonly string _libraryRoot;
    private readonly ConcurrentDictionary<string, string> _peerRoom = new();

    public CommandProcessor(
        RoomManager rooms,
        CatalogStore catalog,
        LibraryScanner scanner,
        StreamingHub streaming,
        PairingService pairing,
        HistoryStore history,
        EndpointRegistry endpoints,
        string libraryRoot)
    {
        _rooms = rooms;
        _catalog = catalog;
        _scanner = scanner;
        _streaming = streaming;
        _pairing = pairing;
        _history = history;
        _endpoints = endpoints;
        _libraryRoot = libraryRoot;
    }

    public CommandResult Execute(string peerId, MonoMessage msg, string? displayName)
    {
        switch (msg.Type)
        {
            // ── 룸 ──────────────────────────────────────────────
            case MessageTypes.CreateRoom:
            {
                var created = _rooms.Create(peerId, msg.RoomName ?? "Hi-Fi Lounge", msg.Mode ?? RoomMode.OpenLounge, displayName);
                Bind(peerId, created.Id);
                return Broadcast(created);
            }

            case MessageTypes.JoinRoom:
            {
                var joined = _rooms.Join(msg.RoomId ?? "", peerId, PeerRole.Control, msg.InviteCode, displayName);
                if (joined.Room is not null)
                {
                    Bind(peerId, joined.Room.Id);
                }

                return From(joined);
            }

            case MessageTypes.LeaveRoom:
            {
                var roomId = NeedRoom(peerId, msg);
                var left = _rooms.Leave(roomId, peerId);
                _peerRoom.TryRemove(peerId, out _);
                return From(left);
            }

            case MessageTypes.ListRooms:
                return Direct(new MonoMessage
                {
                    Type = MessageTypes.ListRooms,
                    Body = JsonSerializer.Serialize(_rooms.List().Select(r => new
                    {
                        r.Id,
                        r.Name,
                        r.Mode,
                        r.SourceMode,
                        r.QualityPolicy,
                        r.Playing,
                        host = r.PeerNames.GetValueOrDefault(r.HostPeerId, r.HostPeerId),
                        needsInvite = r.Mode == RoomMode.Invite,
                        members = r.ControlPeerIds.Count,
                        outputs = r.Outputs.Count,
                        queued = r.Queue.Count,
                        badge = r.PathBadge
                    }), LineFraming.JsonOptions)
                });

            case MessageTypes.Invite:
                return From(_rooms.ManageInvite(NeedRoom(peerId, msg), peerId, msg.InviteAction ?? InviteAction.Rotate, msg.Minutes ?? 0));

            case MessageTypes.Kick:
                return From(_rooms.Kick(NeedRoom(peerId, msg), peerId, msg.TargetPeerId ?? ""));

            case MessageTypes.TransferHost:
                return From(_rooms.TransferHost(NeedRoom(peerId, msg), peerId, msg.TargetPeerId ?? ""));

            case MessageTypes.SetRole:
                return From(_rooms.SetRole(NeedRoom(peerId, msg), peerId, msg.TargetPeerId ?? "", msg.Member ?? MemberRole.Listener));

            case MessageTypes.Spectate:
                return From(_rooms.Spectate(NeedRoom(peerId, msg), peerId, msg.Flag ?? true));

            // ── 큐 ──────────────────────────────────────────────
            case MessageTypes.Enqueue:
                return From(_rooms.Enqueue(NeedRoom(peerId, msg), peerId, msg.TrackId ?? ""));

            case MessageTypes.RequestTrack:
                return From(_rooms.Request(NeedRoom(peerId, msg), peerId, msg.TrackId ?? ""));

            case MessageTypes.ApproveRequest:
                return From(_rooms.Approve(NeedRoom(peerId, msg), peerId, msg.Text ?? ""));

            case MessageTypes.RejectRequest:
                return From(_rooms.Reject(NeedRoom(peerId, msg), peerId, msg.Text ?? ""));

            case MessageTypes.RemoveQueue:
                return From(_rooms.RemoveQueue(NeedRoom(peerId, msg), peerId, msg.Index ?? 0));

            case MessageTypes.MoveQueue:
                return From(_rooms.MoveQueue(NeedRoom(peerId, msg), peerId, msg.Index ?? 0, msg.Delta ?? (int?)msg.MediaTimeMs ?? 1));

            case MessageTypes.ClearQueue:
                return From(_rooms.ClearQueue(NeedRoom(peerId, msg), peerId));

            case MessageTypes.JumpTo:
                return From(_rooms.JumpTo(NeedRoom(peerId, msg), peerId, msg.Index ?? 0));

            // ── 트랜스포트 ──────────────────────────────────────
            case MessageTypes.Play:
                return From(_rooms.Play(NeedRoom(peerId, msg), peerId));

            case MessageTypes.Pause:
                return From(_rooms.Pause(NeedRoom(peerId, msg), peerId));

            case MessageTypes.Seek:
                return From(_rooms.Seek(NeedRoom(peerId, msg), peerId, msg.MediaTimeMs ?? 0));

            case MessageTypes.SeekPin:
                return From(_rooms.SeekToPin(NeedRoom(peerId, msg), peerId, msg.Text ?? ""));

            case MessageTypes.Skip:
                return From(_rooms.Skip(NeedRoom(peerId, msg), peerId, msg.Index ?? 1));

            case MessageTypes.Resync:
                return From(_rooms.Resync(NeedRoom(peerId, msg), peerId));

            // ── 소셜 ────────────────────────────────────────────
            case MessageTypes.Pin:
                return From(_rooms.Pin(NeedRoom(peerId, msg), peerId, msg.MediaTimeMs ?? 0, msg.Text ?? ""));

            case MessageTypes.RemovePin:
                return From(_rooms.RemovePin(NeedRoom(peerId, msg), peerId, msg.Text ?? ""));

            case MessageTypes.Chat:
                return From(_rooms.Chat(NeedRoom(peerId, msg), peerId, msg.Text ?? ""));

            case MessageTypes.React:
                return From(_rooms.React(NeedRoom(peerId, msg), peerId, msg.Emoji ?? "♥"));

            case MessageTypes.FollowArtist:
                return From(_rooms.FollowArtist(NeedRoom(peerId, msg), peerId, msg.Text ?? ""));

            case MessageTypes.FollowHost:
                return From(_rooms.ApplyHostSettings(NeedRoom(peerId, msg), peerId, r => r.FollowHostView = msg.Flag ?? true));

            case MessageTypes.LinerPage:
                return From(_rooms.ApplyHostSettings(NeedRoom(peerId, msg), peerId, r => r.LinerPage = msg.Index ?? 0));

            // ── 호스트 설정 ─────────────────────────────────────
            case MessageTypes.SetDsp:
                return From(_rooms.ApplyHostSettings(NeedRoom(peerId, msg), peerId, r =>
                {
                    if (r.DspLocked)
                    {
                        return;
                    }

                    r.DspPreset = msg.Dsp ?? DspPresetKind.Off;
                    r.DspEnabled = r.DspPreset != DspPresetKind.Off;
                }));

            case MessageTypes.SetPolicy:
                return From(_rooms.ApplyHostSettings(NeedRoom(peerId, msg), peerId, r =>
                {
                    if (msg.Policy is { } p)
                    {
                        r.QualityPolicy = p;
                    }

                    if (msg.Consent is bool c)
                    {
                        r.ArchiveDefaultConsent = c;
                    }

                    if (msg.Minutes is int days)
                    {
                        r.CommentRetentionDays = Math.Max(0, days);
                    }

                    if (msg.Flag is bool anon)
                    {
                        r.AnonymizeArchive = anon;
                    }
                }));

            case MessageTypes.SetSourceMode:
                return From(_rooms.ApplyHostSettings(NeedRoom(peerId, msg), peerId, r =>
                {
                    if (msg.SourceMode is { } sm)
                    {
                        r.SourceMode = sm;
                        r.ResyncEpoch++;
                    }
                }));

            case MessageTypes.SetRoomFlags:
                return From(_rooms.ApplyHostSettings(NeedRoom(peerId, msg), peerId, r =>
                {
                    switch (msg.Text)
                    {
                        case "seek": r.SeekingAllowed = msg.Flag ?? r.SeekingAllowed; break;
                        case "comments": r.CommentsAllowed = msg.Flag ?? r.CommentsAllowed; break;
                        case "chat": r.ChatCollapsed = msg.Flag ?? r.ChatCollapsed; break;
                        case "queue_lock": r.QueueLocked = msg.Flag ?? !r.QueueLocked; break;
                        case "follow_host": r.FollowHostView = msg.Flag ?? r.FollowHostView; break;
                        case "auto_advance": r.AutoAdvance = msg.Flag ?? !r.AutoAdvance; break;
                        case "cloud": r.CloudSyncOptIn = msg.Flag ?? false; break;
                        case "dsp_lock": r.DspLocked = msg.Flag ?? !r.DspLocked; break;
                        case "max": r.MaxMembers = Math.Clamp(msg.Index ?? r.MaxMembers, 1, 128); break;
                        case "name" when !string.IsNullOrWhiteSpace(msg.RoomName): r.Name = msg.RoomName!; break;
                    }
                }));

            case MessageTypes.SetVolume:
                return From(_rooms.SetVolume(
                    NeedRoom(peerId, msg),
                    peerId,
                    string.IsNullOrWhiteSpace(msg.TargetPeerId) ? peerId : msg.TargetPeerId,
                    msg.Volume ?? 100));

            // ── 카탈로그 ────────────────────────────────────────
            case MessageTypes.Catalog:
                return Direct(CatalogMessage());

            case MessageTypes.Search:
                return Direct(new MonoMessage
                {
                    Type = MessageTypes.Search,
                    Body = JsonSerializer.Serialize(_catalog.Search(msg.Text ?? ""), LineFraming.JsonOptions)
                });

            case MessageTypes.Graph:
                return Direct(new MonoMessage
                {
                    Type = MessageTypes.Graph,
                    Body = JsonSerializer.Serialize(_catalog.Graph(msg.Text ?? ""), LineFraming.JsonOptions)
                });

            case MessageTypes.ScanLibrary:
            {
                var scanned = _scanner.Scan(string.IsNullOrWhiteSpace(msg.Path) ? _libraryRoot : msg.Path);
                return new CommandResult(null, new MonoMessage
                {
                    Type = MessageTypes.ScanLibrary,
                    Ok = true,
                    Index = scanned,
                    Body = $"scanned {scanned} files · catalog {_catalog.Tracks.Count} tracks"
                }, CatalogMessage());
            }

            case MessageTypes.LinkStreaming:
            {
                var provider = msg.Provider ?? StreamingProvider.Tidal;
                if (string.IsNullOrWhiteSpace(msg.Token))
                {
                    _streaming.Unlink(provider);
                    return new CommandResult(null, new MonoMessage
                    {
                        Type = MessageTypes.LinkStreaming,
                        Ok = false,
                        Provider = provider,
                        Body = $"{provider} 연결 해제"
                    }, CatalogMessage());
                }

                var acc = _streaming.Link(provider, msg.Token, msg.DisplayName);
                return new CommandResult(null, new MonoMessage
                {
                    Type = MessageTypes.LinkStreaming,
                    Ok = acc.Connected,
                    Provider = provider,
                    Body = JsonSerializer.Serialize(_streaming.AccountViews, LineFraming.JsonOptions)
                }, CatalogMessage());
            }

            // ── 개인 라이브러리 ─────────────────────────────────
            case MessageTypes.History:
                return Direct(new MonoMessage
                {
                    Type = MessageTypes.History,
                    Body = JsonSerializer.Serialize(_history.ForPeer(peerId).Select(h => new
                    {
                        h.Id,
                        h.TrackId,
                        h.RoomId,
                        h.RoomName,
                        h.HeardAt,
                        h.Completed,
                        title = _catalog.Tracks.GetValueOrDefault(h.TrackId)?.Title ?? h.TrackId,
                        artist = _catalog.Tracks.TryGetValue(h.TrackId, out var ht)
                            ? _catalog.Artists.GetValueOrDefault(ht.ArtistId)?.Name
                            : null
                    }), LineFraming.JsonOptions)
                });

            case MessageTypes.Archives:
                return Direct(new MonoMessage
                {
                    Type = MessageTypes.Archives,
                    Body = JsonSerializer.Serialize(_history.Archives.Select(a => new
                    {
                        a.Id,
                        a.RoomName,
                        a.Mode,
                        a.StartedAt,
                        a.EndedAt,
                        a.Participants,
                        tracks = a.TrackIds.Select(id => new
                        {
                            id,
                            title = _catalog.Tracks.GetValueOrDefault(id)?.Title ?? id,
                            highlight = a.HighlightTrackIds.Contains(id)
                        }),
                        pins = a.Pins.Count,
                        hits = a.Hits.Take(5)
                    }), LineFraming.JsonOptions)
                });

            case MessageTypes.Playlists:
                return Direct(new MonoMessage
                {
                    Type = MessageTypes.Playlists,
                    Body = JsonSerializer.Serialize(_history.Playlists.Select(p => new
                    {
                        p.Id,
                        p.Title,
                        p.CreatedAt,
                        p.FromArchiveId,
                        tracks = p.TrackIds.Select(id => new
                        {
                            id,
                            title = _catalog.Tracks.GetValueOrDefault(id)?.Title ?? id
                        })
                    }), LineFraming.JsonOptions)
                });

            case MessageTypes.CreatePlaylist:
            {
                var ids = msg.TrackIds ?? [];
                if (ids.Count == 0 && msg.ArchiveId is not null)
                {
                    var archive = _history.Archive(msg.ArchiveId);
                    if (archive is null)
                    {
                        return Fail("archive not found");
                    }

                    var created = _history.PlaylistFromArchive(archive, peerId);
                    return Direct(new MonoMessage { Type = MessageTypes.CreatePlaylist, Ok = true, PlaylistId = created.Id, Body = created.Title });
                }

                if (ids.Count == 0)
                {
                    return Fail("no tracks");
                }

                var playlist = _history.CreatePlaylist(msg.Text ?? "Playlist", ids, peerId);
                return Direct(new MonoMessage { Type = MessageTypes.CreatePlaylist, Ok = true, PlaylistId = playlist.Id, Body = playlist.Title });
            }

            case MessageTypes.LoadPlaylist:
            {
                var ids = msg.TrackIds ?? [];
                if (ids.Count == 0 && msg.PlaylistId is not null)
                {
                    ids = _history.Playlist(msg.PlaylistId)?.TrackIds ?? [];
                }

                if (ids.Count == 0 && msg.ArchiveId is not null)
                {
                    ids = _history.Archive(msg.ArchiveId)?.TrackIds ?? [];
                }

                if (ids.Count == 0 && msg.TrackId is not null)
                {
                    ids = [msg.TrackId];
                }

                return From(_rooms.LoadTracks(NeedRoom(peerId, msg), peerId, ids, msg.Flag ?? false));
            }

            case MessageTypes.ExportM3u:
            {
                var pl = msg.PlaylistId is not null ? _history.Playlist(msg.PlaylistId) : _history.Playlists.FirstOrDefault();
                return Direct(new MonoMessage
                {
                    Type = MessageTypes.ExportM3u,
                    Ok = pl is not null,
                    PlaylistId = pl?.Id,
                    Body = pl is null ? "" : _history.ExportM3u(pl, _catalog)
                });
            }

            case MessageTypes.ShareSession:
            {
                var archive = msg.ArchiveId is not null ? _history.Archive(msg.ArchiveId) : _history.Archives.FirstOrDefault();
                return Direct(new MonoMessage
                {
                    Type = MessageTypes.ShareSession,
                    Ok = archive is not null,
                    ArchiveId = archive?.Id,
                    Body = archive is null ? "아카이브가 없습니다" : _history.ShareSummary(archive, _catalog)
                });
            }

            case MessageTypes.EndSession:
            {
                var ended = _rooms.EndAndMaybeArchive(NeedRoom(peerId, msg), peerId, msg.Consent ?? false);
                if (ended.Error is not null)
                {
                    return Fail(ended.Error);
                }

                _peerRoom.TryRemove(peerId, out _);
                return Direct(new MonoMessage
                {
                    Type = MessageTypes.Archive,
                    Ok = true,
                    Consent = msg.Consent,
                    ArchiveId = ended.Archive?.Id,
                    PlaylistId = ended.Playlist?.Id,
                    Body = ended.Archive is null
                        ? "세션을 저장하지 않고 종료했습니다 (휘발)"
                        : JsonSerializer.Serialize(new { ended.Archive, ended.Playlist }, LineFraming.JsonOptions)
                });
            }

            // ── 운영 ────────────────────────────────────────────
            case MessageTypes.Endpoints:
                return Direct(new MonoMessage
                {
                    Type = MessageTypes.Endpoints,
                    Body = JsonSerializer.Serialize(_endpoints.All(), LineFraming.JsonOptions)
                });

            case MessageTypes.Pair:
            {
                var token = _pairing.Issue(peerId);
                return Direct(new MonoMessage
                {
                    Type = MessageTypes.PairingIssued,
                    PairingCode = token.Code,
                    Body = $"{token.Code} · {token.ExpiresAt:HH:mm:ss}까지 유효"
                });
            }

            case MessageTypes.Redeem:
            {
                var (session, issuedBy) = _pairing.Redeem(msg.PairingCode ?? msg.Text ?? "");
                return session is null
                    ? Fail("페어링 코드가 유효하지 않습니다")
                    : Direct(new MonoMessage { Type = MessageTypes.Redeem, Ok = true, Token = session, PeerId = issuedBy });
            }

            default:
                return Fail($"unknown type {msg.Type}");
        }
    }

    public MonoMessage CatalogMessage() => new()
    {
        Type = MessageTypes.Catalog,
        Ok = true,
        Index = _catalog.Tracks.Count,
        Body = JsonSerializer.Serialize(_catalog.CatalogView(), LineFraming.JsonOptions)
    };

    private static CommandResult From((ListeningRoom? Room, string? Error) r)
        => r.Error is not null && r.Room is null
            ? Fail(r.Error)
            : r.Error is not null
                ? new CommandResult(r.Room, new MonoMessage { Type = MessageTypes.Error, Ok = false, Error = r.Error })
                : Broadcast(r.Room!);

    private static CommandResult Broadcast(ListeningRoom room) => new(room, null);
    private static CommandResult Direct(MonoMessage msg) => new(null, msg);
    private static CommandResult Fail(string error) => new(null, new MonoMessage { Type = MessageTypes.Error, Ok = false, Error = error });

    public void Bind(string peerId, string roomId) => _peerRoom[peerId] = roomId;

    public string? RoomOf(string peerId) => _peerRoom.GetValueOrDefault(peerId);

    public void Forget(string peerId) => _peerRoom.TryRemove(peerId, out _);

    private string NeedRoom(string peerId, MonoMessage msg)
    {
        if (!string.IsNullOrWhiteSpace(msg.RoomId))
        {
            _peerRoom[peerId] = msg.RoomId;
            return msg.RoomId;
        }

        return _peerRoom.GetValueOrDefault(peerId) ?? "";
    }
}

/// <summary>
/// 명령 하나의 결과. 룸 브로드캐스트 / 요청자 직접 응답 / 전체 Control 브로드캐스트.
/// </summary>
public readonly record struct CommandResult(
    ListeningRoom? BroadcastRoom,
    MonoMessage? Direct,
    MonoMessage? BroadcastAll = null);
