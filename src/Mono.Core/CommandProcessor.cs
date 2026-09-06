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
    private readonly ZoneRegistry _zones;
    private readonly WikipediaService _wiki;
    private readonly BackupService _backups;
    private readonly IReadOnlyList<string> _libraryRoots;
    private readonly ConcurrentDictionary<string, string> _peerRoom = new();

    public CommandProcessor(
        RoomManager rooms,
        CatalogStore catalog,
        LibraryScanner scanner,
        StreamingHub streaming,
        PairingService pairing,
        HistoryStore history,
        EndpointRegistry endpoints,
        ZoneRegistry zones,
        WikipediaService wiki,
        BackupService backups,
        IReadOnlyList<string> libraryRoots)
    {
        _rooms = rooms;
        _catalog = catalog;
        _scanner = scanner;
        _streaming = streaming;
        _pairing = pairing;
        _history = history;
        _endpoints = endpoints;
        _zones = zones;
        _wiki = wiki;
        _backups = backups;
        _libraryRoots = libraryRoots;
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

            case MessageTypes.LinerScroll:
                return From(_rooms.ApplyHostSettings(NeedRoom(peerId, msg), peerId, r =>
                    r.LinerScrollY = Math.Max(0, msg.OffsetMs ?? 0)));

            // ── 스마트 오토플레이 ────────────────────────────────
            case MessageTypes.ChooseAutoplay:
                return From(_rooms.ChooseAutoplay(NeedRoom(peerId, msg), peerId, msg.TrackId ?? ""));

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

            case MessageTypes.SetConvolutionIr:
                return From(_rooms.ApplyHostSettings(NeedRoom(peerId, msg), peerId, r =>
                {
                    if (r.DspLocked) return;
                    r.ConvolutionIrPath = msg.Path;
                    if (!string.IsNullOrWhiteSpace(msg.Path))
                    {
                        r.DspPreset = DspPresetKind.RoomIr;
                        r.DspEnabled = true;
                    }
                }));

            case MessageTypes.SetEasyEq:
                return From(_rooms.ApplyHostSettings(NeedRoom(peerId, msg), peerId, r =>
                {
                    if (r.DspLocked) return;
                    r.EasyEqJson = msg.Body ?? msg.Text;
                    r.EasyEqGraphicMode = msg.Flag ?? false;
                    r.DspPreset = DspPresetKind.Parametric;
                    r.DspEnabled = true;
                }));

            case MessageTypes.SetSpeakerSetup:
                return From(_rooms.ApplyHostSettings(NeedRoom(peerId, msg), peerId, r =>
                {
                    if (r.DspLocked) return;
                    // text: "delayL,delayR,gainL,gainR"
                    var parts = (msg.Text ?? "").Split(',', StringSplitOptions.TrimEntries);
                    if (parts.Length >= 1 && float.TryParse(parts[0], out var dL)) r.SpeakerDelayMsLeft = dL;
                    if (parts.Length >= 2 && float.TryParse(parts[1], out var dR)) r.SpeakerDelayMsRight = dR;
                    if (parts.Length >= 3 && float.TryParse(parts[2], out var gL)) r.SpeakerGainLeftDb = gL;
                    if (parts.Length >= 4 && float.TryParse(parts[3], out var gR)) r.SpeakerGainRightDb = gR;
                    r.DspPreset = DspPresetKind.Speakers;
                    r.DspEnabled = true;
                }));

            case MessageTypes.SetHeadroom:
                return From(_rooms.ApplyHostSettings(NeedRoom(peerId, msg), peerId, r =>
                {
                    if (r.DspLocked) return;
                    if (float.TryParse(msg.Text, out var db)) r.HeadroomDb = Math.Clamp(db, -24, 0);
                }));

            case MessageTypes.SetDeviceEq:
                return From(_rooms.ApplyHostSettings(NeedRoom(peerId, msg), peerId, r =>
                {
                    if (r.DspLocked) return;
                    r.DeviceEqProfile = msg.Text;
                    r.DspPreset = DspPresetKind.Headphones;
                    r.DspEnabled = true;
                }));

            case MessageTypes.SyncProbe:
            {
                var rooms = _rooms.List();
                var stats = rooms.SelectMany(r => r.Stats.Select(kv => new
                {
                    roomId = r.Id,
                    peerId = kv.Key,
                    offsetMs = kv.Value.OffsetMs,
                    jitterMs = kv.Value.JitterMs,
                    rttMs = kv.Value.RttMs,
                    locked = kv.Value.Locked
                }));
                return new CommandResult(
                    null,
                    new MonoMessage
                    {
                        Type = MessageTypes.SyncProbe,
                        Ok = true,
                        Body = JsonSerializer.Serialize(new
                        {
                            endpoints = _endpoints.All().Select(e => new
                            {
                                e.PeerId,
                                e.DisplayName,
                                e.Online,
                                e.ExclusiveMode,
                                e.LatencyMs,
                                e.RoomId,
                                e.SupportsDsd
                            }),
                            clocks = stats,
                            verdict = BuildSyncVerdict(stats.ToList())
                        }, LineFraming.JsonOptions)
                    });
            }

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
                        case "smart_autoplay": r.SmartAutoplay = msg.Flag ?? !r.SmartAutoplay; break;
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

            // ── 반응 히트맵 · 위키백과 ────────────────────────────
            case MessageTypes.ReactionHeatmap:
                return Direct(new MonoMessage
                {
                    Type = MessageTypes.ReactionHeatmap,
                    TrackId = msg.TrackId,
                    Body = JsonSerializer.Serialize(_history.TrackHeatmap(msg.TrackId ?? ""), LineFraming.JsonOptions)
                });

            case MessageTypes.WikiBio:
            {
                var wikiArtist = _catalog.Artists.GetValueOrDefault(msg.Text ?? "");
                if (wikiArtist is null)
                {
                    return Fail("unknown artist");
                }

                // 위키 API 호출은 이 커맨드 처리 스레드에서만 블로킹된다 — 오디오 경로(FanOut/DSP)와 분리되어 있어 안전하다.
                var candidates = new List<string> { wikiArtist.Name };
                candidates.AddRange(wikiArtist.AlternateNames);
                var summary = _wiki.GetSummaryAsync(wikiArtist.Id, candidates).GetAwaiter().GetResult();
                return Direct(new MonoMessage
                {
                    Type = MessageTypes.WikiBio,
                    Text = wikiArtist.Id,
                    Ok = summary is not null,
                    Body = summary is null ? null : JsonSerializer.Serialize(summary, LineFraming.JsonOptions)
                });
            }

            // ── 멀티 디바이스 존 (싱글 플레이) ────────────────────
            case MessageTypes.CreateZone:
                _zones.Create(peerId, string.IsNullOrWhiteSpace(msg.Text) ? "My Zone" : msg.Text);
                return Direct(ZonesMessage(peerId));

            case MessageTypes.RenameZone:
                if (msg.ZoneId is null) return Fail("zoneId required");
                _zones.Rename(msg.ZoneId, msg.Text ?? "Zone");
                return Direct(ZonesMessage(peerId));

            case MessageTypes.SetZoneMode:
                if (msg.ZoneId is null) return Fail("zoneId required");
                _zones.SetMode(msg.ZoneId, msg.ZoneMode ?? Mono.Shared.ZoneMode.Sync);
                return Direct(ZonesMessage(peerId));

            case MessageTypes.ZoneAddMember:
            {
                if (msg.ZoneId is null || string.IsNullOrWhiteSpace(msg.TargetPeerId))
                {
                    return Fail("zoneId and targetPeerId required");
                }

                var zone = _zones.Get(msg.ZoneId);
                if (zone is null || zone.OwnerPeerId != peerId)
                {
                    return Fail("zone not found");
                }

                _zones.AddMember(zone.Id, msg.TargetPeerId);
                var (movedRoom, moveErr) = MoveDeviceIntoZone(zone, msg.TargetPeerId);
                return moveErr is not null
                    ? new CommandResult(movedRoom, ZonesMessage(peerId), null)
                    : new CommandResult(movedRoom, ZonesMessage(peerId));
            }

            case MessageTypes.ZoneRemoveMember:
            {
                if (msg.ZoneId is null || string.IsNullOrWhiteSpace(msg.TargetPeerId))
                {
                    return Fail("zoneId and targetPeerId required");
                }

                _zones.RemoveMember(msg.ZoneId, msg.TargetPeerId);
                var leftRoom = _rooms.LeaveCurrentRoomAsOutput(msg.TargetPeerId);
                return new CommandResult(leftRoom, ZonesMessage(peerId));
            }

            case MessageTypes.DeleteZone:
                if (msg.ZoneId is null) return Fail("zoneId required");
                foreach (var member in _zones.Get(msg.ZoneId)?.MemberPeerIds ?? [])
                {
                    _rooms.LeaveCurrentRoomAsOutput(member);
                }
                _zones.Delete(msg.ZoneId);
                return Direct(ZonesMessage(peerId));

            case MessageTypes.ListZones:
                return Direct(ZonesMessage(peerId));

            case MessageTypes.ScanLibrary:
            {
                var scanned = string.IsNullOrWhiteSpace(msg.Path)
                    ? _scanner.ScanAll(_libraryRoots)
                    : _scanner.Scan(msg.Path);
                return new CommandResult(null, new MonoMessage
                {
                    Type = MessageTypes.ScanLibrary,
                    Ok = true,
                    Index = scanned,
                    Body = $"scanned {scanned} files · catalog {_catalog.Tracks.Count} tracks"
                }, CatalogMessage());
            }

            case MessageTypes.Backup:
                return Direct(new MonoMessage
                {
                    Type = MessageTypes.Backup,
                    Ok = true,
                    Body = _backups.Create()
                });

            case MessageTypes.Album:
            {
                var detail = _catalog.AlbumDetail(msg.Text ?? "");
                return detail is null
                    ? Fail("album not found")
                    : Direct(new MonoMessage
                    {
                        Type = MessageTypes.Album,
                        Body = JsonSerializer.Serialize(detail, LineFraming.JsonOptions)
                    });
            }

            case MessageTypes.Folders:
                return Direct(new MonoMessage
                {
                    Type = MessageTypes.Folders,
                    Body = JsonSerializer.Serialize(_libraryRoots.Select(root => new
                    {
                        path = root,
                        exists = Directory.Exists(root),
                        trackCount = _catalog.Tracks.Values.Count(t =>
                            t.LocalPath is not null &&
                            t.LocalPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    }), LineFraming.JsonOptions)
                });

            case MessageTypes.LinkStreaming:
            {
                var provider = msg.Provider ?? StreamingProvider.Tidal;
                if (string.Equals(msg.Text, "oauth", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(msg.Token, "oauth", StringComparison.OrdinalIgnoreCase))
                {
                    var begin = _streaming.BeginOAuth(provider);
                    return Direct(new MonoMessage
                    {
                        Type = MessageTypes.LinkStreaming,
                        Ok = true,
                        Provider = provider,
                        Body = JsonSerializer.Serialize(begin, LineFraming.JsonOptions)
                    });
                }

                if (string.Equals(msg.Text, "oauth_complete", StringComparison.OrdinalIgnoreCase))
                {
                    var acc = _streaming.CompleteOAuth(provider, msg.Token, msg.PairingCode, msg.DisplayName);
                    return new CommandResult(null, new MonoMessage
                    {
                        Type = MessageTypes.LinkStreaming,
                        Ok = acc.Connected,
                        Provider = provider,
                        Body = JsonSerializer.Serialize(_streaming.AccountViews, LineFraming.JsonOptions)
                    }, CatalogMessage());
                }

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

                var linked = _streaming.Link(provider, msg.Token, msg.DisplayName);
                return new CommandResult(null, new MonoMessage
                {
                    Type = MessageTypes.LinkStreaming,
                    Ok = linked.Connected,
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

    private MonoMessage ZonesMessage(string ownerPeerId) => new()
    {
        Type = MessageTypes.ListZones,
        Body = JsonSerializer.Serialize(_zones.ForOwner(ownerPeerId).Select(z => new
        {
            z.Id,
            z.Name,
            z.Mode,
            z.SyncRoomId,
            members = z.MemberPeerIds.Select(id => new
            {
                peerId = id,
                name = _endpoints.All().FirstOrDefault(e => e.PeerId == id)?.DisplayName ?? id,
                online = _endpoints.All().FirstOrDefault(e => e.PeerId == id)?.Online ?? false,
                roomId = z.Mode == Mono.Shared.ZoneMode.Sync ? z.SyncRoomId : z.IndependentRoomIds.GetValueOrDefault(id)
            })
        }), LineFraming.JsonOptions)
    };

    private static object BuildSyncVerdict(IReadOnlyList<object> clockRows)
    {
        // anonymous projections — re-parse via JSON for simplicity
        var json = JsonSerializer.Serialize(clockRows);
        using var doc = JsonDocument.Parse(json);
        var offsets = new List<double>();
        var rtts = new List<double>();
        var jitters = new List<double>();
        var locked = 0;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.TryGetProperty("offsetMs", out var o)) offsets.Add(o.GetDouble());
            if (el.TryGetProperty("rttMs", out var r)) rtts.Add(r.GetDouble());
            if (el.TryGetProperty("jitterMs", out var j)) jitters.Add(j.GetDouble());
            if (el.TryGetProperty("locked", out var l) && l.GetBoolean()) locked++;
        }

        var peerCount = doc.RootElement.GetArrayLength();
        var offsetSpread = offsets.Count >= 2 ? offsets.Max() - offsets.Min() : offsets.FirstOrDefault();
        var maxRtt = rtts.Count > 0 ? rtts.Max() : 0;
        var maxJitter = jitters.Count > 0 ? jitters.Max() : 0;
        var meets5ms = peerCount >= 2 && Math.Abs(offsetSpread) < 5 && maxRtt < 20;
        return new
        {
            peerCount,
            lockedCount = locked,
            offsetSpreadMs = Math.Round(offsetSpread, 3),
            maxRttMs = Math.Round(maxRtt, 3),
            maxJitterMs = Math.Round(maxJitter, 3),
            targetMs = 5,
            meetsTarget = meets5ms,
            note = peerCount < 2
                ? "물리 기기 Output 2대 이상을 같은 룸에 붙인 뒤 sync_probe를 다시 실행하세요."
                : meets5ms
                    ? "오프셋 스프레드 < 5ms — 목표 충족."
                    : "오프셋/RTT가 목표(5ms)를 넘습니다. Exclusive·유선 LAN·동일 스위치를 확인하세요."
        };
    }

    /// <summary>
    /// 존 멤버 기기를 존이 관리하는 방으로 옮긴다. Sync면 존 전체가 공유하는 개인 방으로,
    /// Independent면 그 기기만의 개인 방으로 — 필요하면 방을 새로 만든다.
    /// 기기가 지금 연결돼 있지 않으면(캐패빌리티를 모르면) 구성만 저장해두고 다음 접속 때 반영한다.
    /// </summary>
    private (ListeningRoom? Room, string? Error) MoveDeviceIntoZone(Zone zone, string devicePeerId)
    {
        var cap = _rooms.List().SelectMany(r => r.Outputs.Values).FirstOrDefault(c => c.PeerId == devicePeerId);
        if (cap is null)
        {
            var record = _endpoints.All().FirstOrDefault(e => e.PeerId == devicePeerId);
            if (record is null || !record.Online)
            {
                return (null, null); // 아직 연결되지 않은 기기 — 등록만 해 두고 다음 접속 때 반영
            }

            cap = new OutputCapability
            {
                PeerId = record.PeerId,
                DisplayName = record.DisplayName,
                MaxSampleRate = record.MaxSampleRate,
                MaxBitDepth = record.MaxBitDepth,
                SupportsDsd = record.SupportsDsd,
                ExclusiveMode = record.ExclusiveMode,
                ReportedLatencyMs = record.LatencyMs,
                HardwareVolume = record.HardwareVolume,
                VolumePercent = record.VolumePercent,
                Device = record.Device
            };
        }

        _rooms.LeaveCurrentRoomAsOutput(devicePeerId);

        string targetRoomId;
        if (zone.Mode == Mono.Shared.ZoneMode.Sync)
        {
            if (zone.SyncRoomId is null || _rooms.Get(zone.SyncRoomId) is null)
            {
                var created = _rooms.Create(zone.OwnerPeerId, zone.Name, RoomMode.OpenLounge, null);
                _zones.SetSyncRoom(zone.Id, created.Id);
                targetRoomId = created.Id;
            }
            else
            {
                targetRoomId = zone.SyncRoomId;
            }
        }
        else
        {
            var existing = zone.IndependentRoomIds.GetValueOrDefault(devicePeerId);
            if (existing is null || _rooms.Get(existing) is null)
            {
                var created = _rooms.Create(zone.OwnerPeerId, $"{zone.Name} · {cap.DisplayName}", RoomMode.OpenLounge, null);
                _zones.SetIndependentRoom(zone.Id, devicePeerId, created.Id);
                targetRoomId = created.Id;
            }
            else
            {
                targetRoomId = existing;
            }
        }

        _rooms.Join(targetRoomId, devicePeerId, PeerRole.Output, null, cap.DisplayName);
        var (room, error) = _rooms.RegisterOutput(targetRoomId, cap);
        return (room, error);
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
