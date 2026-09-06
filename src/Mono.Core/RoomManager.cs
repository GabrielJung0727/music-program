using System.Text.Json;
using Mono.Protocol;
using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// 룸 상태머신. 큐·타임라인·권한·정책의 단일 소스 오브 트루스.
/// 모든 변경은 하나의 락 안에서 일어나고, 바깥으로는 스냅샷만 나간다.
/// </summary>
public sealed class RoomManager
{
    private readonly CatalogStore _catalog;
    private readonly HistoryStore _history;
    private readonly StreamingHub? _streaming;
    private readonly EndpointRegistry? _endpoints;
    private readonly object _gate = new();
    private readonly Dictionary<string, ListeningRoom> _rooms = new();

    public RoomManager(
        CatalogStore catalog,
        HistoryStore history,
        StreamingHub? streaming = null,
        EndpointRegistry? endpoints = null)
    {
        _catalog = catalog;
        _history = history;
        _streaming = streaming;
        _endpoints = endpoints;
    }

    public IReadOnlyList<ListeningRoom> List()
    {
        lock (_gate) return _rooms.Values.ToList();
    }

    public ListeningRoom Create(string hostPeerId, string name, RoomMode mode, string? displayName)
    {
        lock (_gate)
        {
            var room = ListeningRoom.ForMode(Guid.NewGuid().ToString("n")[..12], name, hostPeerId, mode);
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                room.PeerNames[hostPeerId] = displayName;
            }

            _rooms[room.Id] = room;
            return room;
        }
    }

    public ListeningRoom? Get(string roomId)
    {
        lock (_gate) return _rooms.TryGetValue(roomId, out var room) ? room : null;
    }

    public IReadOnlyList<ListeningRoom> RoomsOf(string peerId)
    {
        lock (_gate)
        {
            return _rooms.Values
                .Where(r => r.ControlPeerIds.Contains(peerId) || r.OutputPeerIds.Contains(peerId) || r.SpectatorPeerIds.Contains(peerId))
                .ToList();
        }
    }

    public (ListeningRoom? Room, string? Error) Join(string roomId, string peerId, PeerRole role, string? invite, string? name)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                return (null, "room not found");
            }

            if (room.BannedPeerIds.Contains(peerId))
            {
                return (null, "kicked from this room");
            }

            var members = room.ControlPeerIds.Count + room.OutputPeerIds.Count;
            var alreadyIn = room.ControlPeerIds.Contains(peerId) || room.OutputPeerIds.Contains(peerId);
            if (!alreadyIn && members >= room.MaxMembers)
            {
                return (null, "room is full");
            }

            if (room.Mode == RoomMode.Invite && peerId != room.HostPeerId)
            {
                if (room.InviteCode is null)
                {
                    return (null, "invite revoked");
                }

                if (room.InviteExpiresAt is { } exp && exp < DateTimeOffset.UtcNow)
                {
                    return (null, "invite expired");
                }

                if (!string.Equals(invite, room.InviteCode, StringComparison.Ordinal))
                {
                    return (null, "invite code required");
                }
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                room.PeerNames[peerId] = name;
            }

            if (role == PeerRole.Control)
            {
                room.ControlPeerIds.Add(peerId);
            }
            else
            {
                room.OutputPeerIds.Add(peerId);
            }

            if (!room.Roles.ContainsKey(peerId))
            {
                room.Roles[peerId] = peerId == room.HostPeerId ? MemberRole.Host : MemberRole.Listener;
            }

            RefreshPath(room);
            return (room, null);
        }
    }

    public (ListeningRoom? Room, string? Error) Leave(string roomId, string peerId)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                return (null, "room not found");
            }

            room.ControlPeerIds.Remove(peerId);
            room.OutputPeerIds.Remove(peerId);
            room.SpectatorPeerIds.Remove(peerId);
            room.Outputs.Remove(peerId);
            room.Stats.Remove(peerId);
            if (room.HostPeerId == peerId)
            {
                var heir = room.Roles.FirstOrDefault(kv => kv.Value == MemberRole.Dj && room.ControlPeerIds.Contains(kv.Key)).Key
                           ?? room.ControlPeerIds.FirstOrDefault();
                if (heir is not null)
                {
                    room.HostPeerId = heir;
                    room.Roles[heir] = MemberRole.Host;
                }
            }

            RefreshPath(room);
            return (room, null);
        }
    }

    /// <summary>Output 연결이 끊겼을 때. 기기는 레지스트리에 오프라인으로 남는다.</summary>
    public ListeningRoom? DetachOutput(string peerId)
    {
        lock (_gate)
        {
            _endpoints?.Offline(peerId);
            return LeaveCurrentRoomAsOutput(peerId);
        }
    }

    /// <summary>
    /// Output을 지금 있는 방에서만 빼낸다 — 소켓은 계속 연결돼 있으므로 엔드포인트 레지스트리의
    /// 온라인 상태는 건드리지 않는다. 존(Zone) 재배정처럼 살아있는 기기를 다른 방으로 옮길 때 쓴다.
    /// </summary>
    public ListeningRoom? LeaveCurrentRoomAsOutput(string peerId)
    {
        lock (_gate)
        {
            var room = _rooms.Values.FirstOrDefault(r => r.Outputs.ContainsKey(peerId) || r.OutputPeerIds.Contains(peerId));
            if (room is null)
            {
                return null;
            }

            room.Outputs.Remove(peerId);
            room.OutputPeerIds.Remove(peerId);
            room.SpectatorPeerIds.Remove(peerId);
            room.Stats.Remove(peerId);
            RefreshPath(room);
            return room;
        }
    }

    public (ListeningRoom? Room, string? Error) RegisterOutput(string roomId, OutputCapability cap)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                return (null, "room not found");
            }

            room.Outputs[cap.PeerId] = cap;
            room.OutputPeerIds.Add(cap.PeerId);
            room.PeerNames[cap.PeerId] = cap.DisplayName;
            room.Roles.TryAdd(cap.PeerId, MemberRole.Listener);
            _endpoints?.Announce(cap, roomId);

            string? warn = null;
            var track = room.CurrentTrack(_catalog.Tracks);
            if (track is not null)
            {
                var decision = QualityPolicyEngine.Evaluate(room, track, cap);
                if (decision.Spectator)
                {
                    room.SpectatorPeerIds.Add(cap.PeerId);
                    room.OutputPeerIds.Remove(cap.PeerId);
                    room.Roles[cap.PeerId] = MemberRole.Spectator;
                    warn = decision.Error;
                }
                else
                {
                    room.SpectatorPeerIds.Remove(cap.PeerId);
                    room.OutputPeerIds.Add(cap.PeerId);
                }
            }

            RefreshPath(room);
            return (room, warn);
        }
    }

    public (ListeningRoom? Room, string? Error) Enqueue(string roomId, string peerId, string trackId)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (!_catalog.Tracks.TryGetValue(trackId, out var track))
            {
                return (null, "unknown track");
            }

            if (room.QueueLocked && !room.CanDirect(peerId))
            {
                return (null, "queue is locked");
            }

            if (room.Mode is RoomMode.HostQueue or RoomMode.Audiophile && !room.CanDirect(peerId))
            {
                return (null, "only the host may queue; use request_track");
            }

            if (room.Queue.Any(q => q.TrackId == trackId))
            {
                return (null, "duplicate track in queue");
            }

            if (SourceUnavailable(track) is { } unavailable)
            {
                return (null, unavailable);
            }

            room.Queue.Add(new QueueItem
            {
                Id = Guid.NewGuid().ToString("n")[..8],
                TrackId = trackId,
                AddedByPeerId = peerId
            });
            RefreshPath(room);
            return (room, null);
        }
    }

    /// <summary>스트리밍 트랙은 계정이 연결돼 있어야 큐에 들어간다(미구독 검증).</summary>
    private string? SourceUnavailable(Track track)
    {
        if (track.Source == StreamingProvider.Local)
        {
            return null;
        }

        if (_streaming is null || !_streaming.IsConnected(track.Source))
        {
            return $"{track.Source} 계정이 연결되지 않았습니다 — 이 트랙을 재생할 라이선스가 없습니다.";
        }

        return null;
    }

    public (ListeningRoom? Room, string? Error) Request(string roomId, string peerId, string trackId)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (!_catalog.Tracks.TryGetValue(trackId, out var track))
            {
                return (null, "unknown track");
            }

            if (SourceUnavailable(track) is { } unavailable)
            {
                return (null, unavailable);
            }

            if (room.Requests.Any(r => r.TrackId == trackId && r.FromPeerId == peerId))
            {
                return (null, "already requested");
            }

            room.Requests.Add(new QueueRequest
            {
                Id = Guid.NewGuid().ToString("n")[..8],
                TrackId = trackId,
                FromPeerId = peerId
            });
            return (room, null);
        }
    }

    public (ListeningRoom? Room, string? Error) Approve(string roomId, string peerId, string requestId)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (!room.CanDirect(peerId))
            {
                return (null, "only host can approve");
            }

            var req = room.Requests.FirstOrDefault(r => r.Id == requestId);
            if (req is null)
            {
                return (null, "request not found");
            }

            room.Requests.Remove(req);
            if (room.Queue.All(q => q.TrackId != req.TrackId))
            {
                room.Queue.Add(new QueueItem { Id = Guid.NewGuid().ToString("n")[..8], TrackId = req.TrackId, AddedByPeerId = req.FromPeerId });
            }

            RefreshPath(room);
            return (room, null);
        }
    }

    public (ListeningRoom? Room, string? Error) Reject(string roomId, string peerId, string requestId)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (!room.CanDirect(peerId))
            {
                return (null, "only host can reject");
            }

            room.Requests.RemoveAll(r => r.Id == requestId);
            return (room, null);
        }
    }

    public (ListeningRoom? Room, string? Error) RemoveQueue(string roomId, string peerId, int index)
    {
        lock (_gate)
        {
            if (!CanEditQueue(roomId, peerId, out var room, out var err))
            {
                return (null, err);
            }

            if (index < 0 || index >= room.Queue.Count)
            {
                return (null, "bad index");
            }

            room.Queue.RemoveAt(index);
            if (index < room.QueueIndex)
            {
                room.QueueIndex--;
            }

            if (room.QueueIndex >= room.Queue.Count)
            {
                room.QueueIndex = Math.Max(0, room.Queue.Count - 1);
            }

            RefreshPath(room);
            return (room, null);
        }
    }

    public (ListeningRoom? Room, string? Error) MoveQueue(string roomId, string peerId, int index, int delta)
    {
        lock (_gate)
        {
            if (!CanEditQueue(roomId, peerId, out var room, out var err))
            {
                return (null, err);
            }

            var dest = index + delta;
            if (index < 0 || dest < 0 || index >= room.Queue.Count || dest >= room.Queue.Count)
            {
                return (null, "bad index");
            }

            var item = room.Queue[index];
            room.Queue.RemoveAt(index);
            room.Queue.Insert(dest, item);
            if (room.QueueIndex == index)
            {
                room.QueueIndex = dest;
            }
            else if (index < room.QueueIndex && dest >= room.QueueIndex)
            {
                room.QueueIndex--;
            }
            else if (index > room.QueueIndex && dest <= room.QueueIndex)
            {
                room.QueueIndex++;
            }

            return (room, null);
        }
    }

    public (ListeningRoom? Room, string? Error) ClearQueue(string roomId, string peerId)
    {
        lock (_gate)
        {
            if (!CanEditQueue(roomId, peerId, out var room, out var err))
            {
                return (null, err);
            }

            room.Queue.Clear();
            room.QueueIndex = 0;
            room.Playing = false;
            room.MediaTimeAtOriginMs = 0;
            room.ResyncEpoch++;
            RefreshPath(room);
            return (room, null);
        }
    }

    /// <summary>큐의 특정 곡으로 바로 이동(곡 단위 이동은 Audiophile에서도 허용).</summary>
    public (ListeningRoom? Room, string? Error) JumpTo(string roomId, string peerId, int index)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (!room.CanDirect(peerId))
            {
                return (null, "only host may change the track");
            }

            if (index < 0 || index >= room.Queue.Count)
            {
                return (null, "bad index");
            }

            MarkComplete(room);
            room.QueueIndex = index;
            StartTrackTimeline(room, 0);
            return (room, null);
        }
    }

    public (ListeningRoom? Room, string? Error) Play(string roomId, string peerId)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (!room.CanDirect(peerId))
            {
                return (null, "only the host may start playback");
            }

            if (room.Queue.Count == 0)
            {
                return (null, "queue is empty");
            }

            var track = room.CurrentTrack(_catalog.Tracks);
            if (track is not null && room.Outputs.Count > 0)
            {
                ApplyPolicyToOutputs(room, track);
                if (room.QualityPolicy == QualityPolicy.RequireBitPerfect
                    && room.Outputs.Values.All(c => QualityPolicyEngine.Evaluate(room, track, c).Spectator))
                {
                    return (null, "no bit-perfect capable output");
                }
            }

            room.Playing = true;
            room.MediaOriginUnixMs = ClockSync.UnixMs();
            RefreshPath(room);
            NoteNowPlaying(room, track);
            return (room, null);
        }
    }

    public (ListeningRoom? Room, string? Error) Pause(string roomId, string peerId)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (!room.CanDirect(peerId))
            {
                return (null, "only the host may pause");
            }

            if (room.Playing)
            {
                room.MediaTimeAtOriginMs = room.CurrentMediaTimeMs();
                room.Playing = false;
            }

            return (room, null);
        }
    }

    public (ListeningRoom? Room, string? Error) Seek(string roomId, string peerId, long mediaTimeMs)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (room.Mode == RoomMode.Audiophile)
            {
                return (null, "audiophile mode: skip tracks instead of seeking");
            }

            if (!room.CanDirect(peerId) && (!room.SeekingAllowed || room.Mode == RoomMode.HostQueue))
            {
                return (null, "seeking is disabled");
            }

            var track = room.CurrentTrack(_catalog.Tracks);
            var duration = EffectiveDuration(track);
            room.MediaTimeAtOriginMs = Math.Clamp(mediaTimeMs, 0, duration);
            room.MediaOriginUnixMs = ClockSync.UnixMs();
            room.ResyncEpoch++;
            return (room, null);
        }
    }

    /// <summary>핀 클릭 → 해당 구간 이동. 시킹 정책은 룸이 정한다.</summary>
    public (ListeningRoom? Room, string? Error) SeekToPin(string roomId, string peerId, string pinId)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            var pin = room.Pins.FirstOrDefault(p => p.Id == pinId);
            if (pin is null)
            {
                return (null, "pin not found");
            }

            var currentTrack = room.CurrentTrack(_catalog.Tracks);
            if (currentTrack is not null && currentTrack.Id == pin.TrackId)
            {
                return Seek(roomId, peerId, pin.MediaTimeMs);
            }

            var index = room.Queue.FindIndex(q => q.TrackId == pin.TrackId);
            if (index < 0)
            {
                return (null, "pinned track is no longer queued");
            }

            if (!room.CanDirect(peerId))
            {
                return (null, "only host may change the track");
            }

            MarkComplete(room);
            room.QueueIndex = index;
            StartTrackTimeline(room, room.Mode == RoomMode.Audiophile ? 0 : pin.MediaTimeMs);
            return (room, null);
        }
    }

    public (ListeningRoom? Room, string? Error) Skip(string roomId, string peerId, int delta)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (!room.CanDirect(peerId))
            {
                return (null, "only host may skip");
            }

            MarkComplete(room);
            room.QueueIndex = Math.Clamp(room.QueueIndex + delta, 0, Math.Max(0, room.Queue.Count - 1));
            StartTrackTimeline(room, 0);
            return (room, null);
        }
    }

    /// <summary>버퍼가 풀렸을 때의 명시적 재동기화. Output은 epoch가 바뀌면 버퍼를 비운다.</summary>
    public (ListeningRoom? Room, string? Error) Resync(string roomId, string peerId)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            room.ResyncEpoch++;
            if (room.Playing)
            {
                room.MediaTimeAtOriginMs = room.CurrentMediaTimeMs();
                room.MediaOriginUnixMs = ClockSync.UnixMs();
            }

            foreach (var stat in room.Stats.Values)
            {
                stat.Resyncs++;
            }

            return (room, null);
        }
    }

    /// <summary>트랙이 끝난 룸을 다음 곡으로 넘긴다. 변경된 룸만 돌려준다.</summary>
    public IReadOnlyList<ListeningRoom> AdvanceFinished()
    {
        lock (_gate)
        {
            var changed = new List<ListeningRoom>();
            foreach (var room in _rooms.Values)
            {
                if (!room.Playing)
                {
                    continue;
                }

                var track = room.CurrentTrack(_catalog.Tracks);
                var duration = EffectiveDuration(track);
                if (track is null || room.CurrentMediaTimeMs() < duration)
                {
                    continue;
                }

                MarkComplete(room, forceComplete: true);
                var isLastQueued = room.QueueIndex + 1 >= room.Queue.Count;
                if (room.AutoAdvance && !isLastQueued)
                {
                    room.QueueIndex++;
                    StartTrackTimeline(room, 0);
                    NoteNowPlaying(room, room.CurrentTrack(_catalog.Tracks));
                }
                else if (room.AutoAdvance && isLastQueued && PickAutoplayNext(room) is { } nextTrackId)
                {
                    room.Queue.Add(new QueueItem { Id = Guid.NewGuid().ToString("n")[..8], TrackId = nextTrackId, AddedByPeerId = "autoplay" });
                    room.QueueIndex++;
                    StartTrackTimeline(room, 0);
                    NoteNowPlaying(room, room.CurrentTrack(_catalog.Tracks));
                }
                else
                {
                    room.Playing = false;
                    room.MediaTimeAtOriginMs = duration;
                    RefreshPath(room);
                }

                changed.Add(room);
            }

            return changed;
        }
    }

    private static string? PickAutoplayNext(ListeningRoom room)
    {
        if (room.AutoplayCandidateIds.Count == 0)
        {
            return null;
        }

        return room.AutoplayChosenId is { } chosen && room.AutoplayCandidateIds.Contains(chosen)
            ? chosen
            : room.AutoplayCandidateIds[0];
    }

    /// <summary>
    /// 스마트 선택형 오토플레이 — 곡이 끝나기 40초 전, 큐의 마지막 곡이면 후보 3곡을 제시한다.
    /// 무응답이면 1번 후보가 자동 재생되고, 선택하면 그 곡이 재생된다. 변경된 룸만 돌려준다.
    /// </summary>
    public const long AutoplayLeadTimeMs = 40_000;

    public IReadOnlyList<ListeningRoom> ProposeAutoplayCandidates()
    {
        lock (_gate)
        {
            var changed = new List<ListeningRoom>();
            foreach (var room in _rooms.Values)
            {
                if (!room.Playing || !room.SmartAutoplay)
                {
                    continue;
                }

                var track = room.CurrentTrack(_catalog.Tracks);
                if (track is null || room.QueueIndex + 1 < room.Queue.Count)
                {
                    continue;
                }

                if (room.AutoplayProposedForTrackId == track.Id)
                {
                    continue;
                }

                var duration = EffectiveDuration(track);
                var remaining = duration - room.CurrentMediaTimeMs();
                if (remaining > AutoplayLeadTimeMs || remaining <= 0)
                {
                    continue;
                }

                var exclude = room.PlayedTrackIds.Concat(room.Queue.Select(q => q.TrackId)).Append(track.Id);
                var candidates = _catalog.Recommend(track.Id, exclude, 3);
                if (candidates.Count == 0)
                {
                    continue;
                }

                room.AutoplayProposedForTrackId = track.Id;
                room.AutoplayCandidateIds.Clear();
                room.AutoplayCandidateIds.AddRange(candidates.Select(t => t.Id));
                room.AutoplayDeadlineUnixMs = ClockSync.UnixMs() + remaining;
                room.AutoplayChosenId = null;
                changed.Add(room);
            }

            return changed;
        }
    }

    public (ListeningRoom? Room, string? Error) ChooseAutoplay(string roomId, string peerId, string trackId)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (!room.AutoplayCandidateIds.Contains(trackId))
            {
                return (null, "이 곡은 오토플레이 후보가 아닙니다");
            }

            room.AutoplayChosenId = trackId;
            return (room, null);
        }
    }

    public (ListeningRoom? Room, string? Error) Pin(string roomId, string peerId, long mediaTimeMs, string text)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (!room.CommentsAllowed)
            {
                return (null, "comments disabled");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return (null, "empty pin");
            }

            var track = room.CurrentTrack(_catalog.Tracks);
            if (track is null)
            {
                return (null, "nothing is queued");
            }

            room.Pins.Add(new TimestampPin(
                Guid.NewGuid().ToString("n")[..8], peerId, track.Id, Math.Max(0, mediaTimeMs), text.Trim(), DateTimeOffset.UtcNow));
            return (room, null);
        }
    }

    public (ListeningRoom? Room, string? Error) RemovePin(string roomId, string peerId, string pinId)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            var pin = room.Pins.FirstOrDefault(p => p.Id == pinId);
            if (pin is null)
            {
                return (null, "pin not found");
            }

            if (pin.PeerId != peerId && peerId != room.HostPeerId)
            {
                return (null, "only the author or the host may remove a pin");
            }

            room.Pins.Remove(pin);
            return (room, null);
        }
    }

    public (ListeningRoom? Room, string? Error) Chat(string roomId, string peerId, string text)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return (null, "empty message");
            }

            room.Chat.Add(new ChatLine(peerId, text.Trim(), DateTimeOffset.UtcNow));
            if (room.Chat.Count > 200)
            {
                room.Chat.RemoveAt(0);
            }

            return (room, null);
        }
    }

    /// <summary>
    /// 허용된 긍정 반응 이모지만 받는다 — 혐오·불쾌 표현을 막기 위한 화이트리스트.
    /// 하트/축포/박수/불꽃으로 제한해 "가장 좋았던 구간" 집계가 오염되지 않게 한다.
    /// </summary>
    public static readonly IReadOnlyList<string> AllowedReactionEmoji = ["❤️", "🎉", "👏", "🔥"];

    /// <summary>곡당 유저당 반응 상한 — 진짜 좋았던 구간만 남도록 3개로 제한한다.</summary>
    public const int MaxReactionsPerUserPerTrack = 3;

    public (ListeningRoom? Room, string? Error) React(string roomId, string peerId, string emoji)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (!AllowedReactionEmoji.Contains(emoji))
            {
                return (null, "허용되지 않은 이모지입니다 (❤️ 🎉 👏 🔥 만 가능)");
            }

            var track = room.CurrentTrack(_catalog.Tracks);
            if (track is null)
            {
                return (null, "nothing is queued");
            }

            var used = room.Reactions.Count(r => r.PeerId == peerId && r.TrackId == track.Id);
            if (used >= MaxReactionsPerUserPerTrack)
            {
                return (null, $"이 곡에는 이미 {MaxReactionsPerUserPerTrack}번 반응했습니다");
            }

            room.Reactions.Add(new Reaction(peerId, track.Id, emoji, DateTimeOffset.UtcNow, room.CurrentMediaTimeMs()));
            return (room, null);
        }
    }

    /// <summary>
    /// 유튜브 "가장 많이 다시 본 구간"처럼, 현재 트랙에서 반응·핀이 몰린 구간을 10초 버킷으로 집계한다.
    /// 지금 룸의 반응뿐 아니라 과거 세션 아카이브까지 합쳐 트랙 전체 히스토리를 반영한다.
    /// </summary>
    public IReadOnlyList<SegmentHit> ReactionHeatmap(ListeningRoom room, IEnumerable<SegmentHit> archivedHits)
    {
        lock (_gate)
        {
            var track = room.CurrentTrack(_catalog.Tracks);
            if (track is null)
            {
                return [];
            }

            const long bucket = 10_000;
            var hits = new Dictionary<long, int>();
            foreach (var reaction in room.Reactions.Where(r => r.TrackId == track.Id))
            {
                var key = reaction.MediaTimeMs / bucket * bucket;
                hits[key] = hits.GetValueOrDefault(key) + 1;
            }

            foreach (var pin in room.Pins.Where(p => p.TrackId == track.Id))
            {
                var key = pin.MediaTimeMs / bucket * bucket;
                hits[key] = hits.GetValueOrDefault(key) + 1;
            }

            foreach (var hit in archivedHits.Where(h => h.TrackId == track.Id))
            {
                hits[hit.BucketMs] = hits.GetValueOrDefault(hit.BucketMs) + hit.Count;
            }

            return hits.OrderBy(kv => kv.Key)
                .Select(kv => new SegmentHit(track.Id, kv.Key, kv.Value))
                .ToList();
        }
    }

    public (ListeningRoom? Room, string? Error) Kick(string roomId, string hostId, string target)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (hostId != room.HostPeerId)
            {
                return (null, "only host may kick");
            }

            if (target == room.HostPeerId)
            {
                return (null, "the host cannot be kicked");
            }

            room.ControlPeerIds.Remove(target);
            room.OutputPeerIds.Remove(target);
            room.SpectatorPeerIds.Remove(target);
            room.Outputs.Remove(target);
            room.Roles.Remove(target);
            room.Stats.Remove(target);
            room.BannedPeerIds.Add(target);
            RefreshPath(room);
            return (room, null);
        }
    }

    public (ListeningRoom? Room, string? Error) TransferHost(string roomId, string hostId, string target)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (hostId != room.HostPeerId)
            {
                return (null, "only host may transfer");
            }

            if (!room.ControlPeerIds.Contains(target))
            {
                return (null, "target not in room");
            }

            room.Roles[hostId] = MemberRole.Listener;
            room.HostPeerId = target;
            room.Roles[target] = MemberRole.Host;
            return (room, null);
        }
    }

    /// <summary>권한 역할 부여(공동 DJ 위임 포함). 호스트 역할은 transfer_host로만 옮긴다.</summary>
    public (ListeningRoom? Room, string? Error) SetRole(string roomId, string hostId, string target, MemberRole role)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (hostId != room.HostPeerId)
            {
                return (null, "only host may set roles");
            }

            if (role == MemberRole.Host)
            {
                return (null, "use transfer_host");
            }

            if (target == room.HostPeerId)
            {
                return (null, "the host keeps the host role");
            }

            room.Roles[target] = role;
            if (role == MemberRole.Spectator)
            {
                room.SpectatorPeerIds.Add(target);
                room.OutputPeerIds.Remove(target);
            }
            else
            {
                room.SpectatorPeerIds.Remove(target);
                if (room.Outputs.ContainsKey(target))
                {
                    room.OutputPeerIds.Add(target);
                }
            }

            return (room, null);
        }
    }

    /// <summary>본인 의사로 참관(오디오 미수신) 전환.</summary>
    public (ListeningRoom? Room, string? Error) Spectate(string roomId, string peerId, bool spectate)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (spectate)
            {
                room.SpectatorPeerIds.Add(peerId);
                room.OutputPeerIds.Remove(peerId);
                room.Roles[peerId] = MemberRole.Spectator;
            }
            else
            {
                room.SpectatorPeerIds.Remove(peerId);
                room.Roles[peerId] = peerId == room.HostPeerId ? MemberRole.Host : MemberRole.Listener;
                if (room.Outputs.ContainsKey(peerId))
                {
                    room.OutputPeerIds.Add(peerId);
                }
            }

            return (room, null);
        }
    }

    public (ListeningRoom? Room, string? Error) ManageInvite(string roomId, string hostId, InviteAction action, int minutes)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (hostId != room.HostPeerId)
            {
                return (null, "only host may manage invites");
            }

            switch (action)
            {
                case InviteAction.Rotate:
                    room.InviteCode = Random.Shared.Next(100000, 999999).ToString();
                    room.InviteExpiresAt = DateTimeOffset.UtcNow.AddMinutes(minutes <= 0 ? 360 : minutes);
                    break;
                case InviteAction.Extend:
                    room.InviteCode ??= Random.Shared.Next(100000, 999999).ToString();
                    var from = room.InviteExpiresAt is { } e && e > DateTimeOffset.UtcNow ? e : DateTimeOffset.UtcNow;
                    room.InviteExpiresAt = from.AddMinutes(minutes <= 0 ? 60 : minutes);
                    break;
                case InviteAction.Revoke:
                    room.InviteCode = null;
                    room.InviteExpiresAt = null;
                    break;
            }

            return (room, null);
        }
    }

    /// <summary>
    /// 엔드포인트 볼륨. 하드웨어 볼륨이 없는 기기는 디지털 감쇠가 필요한데,
    /// Audiophile / Bit-perfect 룸에서는 이를 거부한다.
    /// </summary>
    public (ListeningRoom? Room, string? Error) SetVolume(string roomId, string actorId, string targetPeerId, int volume)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (actorId != targetPeerId && actorId != room.HostPeerId)
            {
                return (null, "only the host may set another endpoint's volume");
            }

            if (!room.Outputs.TryGetValue(targetPeerId, out var cap))
            {
                return (null, "unknown endpoint");
            }

            var wanted = Math.Clamp(volume, 0, 100);
            if (wanted < 100 && !cap.HardwareVolume && !QualityPolicyEngine.AllowsDigitalVolume(room, cap))
            {
                return (null, "이 룸은 디지털 볼륨 감쇠를 금지합니다 — 하드웨어 볼륨만 사용하세요.");
            }

            cap.VolumePercent = wanted;
            _endpoints?.SetVolume(targetPeerId, wanted);
            return (room, null);
        }
    }

    /// <summary>Output이 보고한 클럭 품질. 측정 UI가 이 값을 그린다.</summary>
    public ListeningRoom? ReportClock(string roomId, string peerId, double offsetMs, double jitterMs, double rttMs, int bufferMs, int resyncs, bool locked, int deviceState = 0, string? deviceError = null)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                return null;
            }

            room.Stats[peerId] = new PeerStats
            {
                OffsetMs = offsetMs,
                JitterMs = jitterMs,
                RttMs = rttMs,
                BufferMs = bufferMs,
                Resyncs = resyncs,
                Locked = locked,
                DeviceState = deviceState,
                DeviceError = deviceError,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            return room;
        }
    }

    public (ListeningRoom? Room, string? Error) ApplyHostSettings(string roomId, string hostId, Action<ListeningRoom> apply)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            if (hostId != room.HostPeerId)
            {
                return (null, "only host");
            }

            apply(room);
            if (room.Mode == RoomMode.Audiophile)
            {
                room.DspEnabled = false;
                room.DspPreset = DspPresetKind.Off;
                room.DspLocked = true;
                room.SeekingAllowed = false;
            }

            var track = room.CurrentTrack(_catalog.Tracks);
            if (track is not null)
            {
                ApplyPolicyToOutputs(room, track);
            }

            RefreshPath(room);
            return (room, null);
        }
    }

    /// <summary>"이 연주자 따라가기" — 호스트/DJ는 바로 큐에, 나머지는 요청으로 들어간다.</summary>
    public (ListeningRoom? Room, string? Error) FollowArtist(string roomId, string peerId, string artistId)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, err);
            }

            var candidates = _catalog.Tracks.Values
                .Where(t => t.ArtistId == artistId && room.Queue.All(q => q.TrackId != t.Id))
                .Where(t => SourceUnavailable(t) is null)
                .ToList();
            var next = candidates.FirstOrDefault();
            if (next is null)
            {
                return (null, "no related track");
            }

            if (room.CanDirect(peerId) || (room.Mode == RoomMode.OpenLounge && !room.QueueLocked))
            {
                room.Queue.Add(new QueueItem { Id = Guid.NewGuid().ToString("n")[..8], TrackId = next.Id, AddedByPeerId = peerId });
            }
            else
            {
                room.Requests.Add(new QueueRequest { Id = Guid.NewGuid().ToString("n")[..8], TrackId = next.Id, FromPeerId = peerId });
            }

            return (room, null);
        }
    }

    /// <summary>플레이리스트·아카이브·히스토리에서 큐로. replace=true면 큐를 갈아끼운다.</summary>
    public (ListeningRoom? Room, string? Error) LoadTracks(string roomId, string peerId, IEnumerable<string> trackIds, bool replace)
    {
        lock (_gate)
        {
            if (!CanEditQueue(roomId, peerId, out var room, out var err))
            {
                return (null, err);
            }

            var ids = trackIds.Where(id => _catalog.Tracks.ContainsKey(id)).ToList();
            if (ids.Count == 0)
            {
                return (null, "nothing to load");
            }

            if (replace)
            {
                room.Queue.Clear();
                room.QueueIndex = 0;
                room.Playing = false;
                room.MediaTimeAtOriginMs = 0;
                room.ResyncEpoch++;
            }

            foreach (var id in ids)
            {
                if (room.Queue.Any(q => q.TrackId == id))
                {
                    continue;
                }

                if (SourceUnavailable(_catalog.Tracks[id]) is not null)
                {
                    continue;
                }

                room.Queue.Add(new QueueItem { Id = Guid.NewGuid().ToString("n")[..8], TrackId = id, AddedByPeerId = peerId });
            }

            RefreshPath(room);
            return (room, null);
        }
    }

    public (SessionArchive? Archive, UserPlaylist? Playlist, string? Error) EndAndMaybeArchive(string roomId, string peerId, bool consent)
    {
        lock (_gate)
        {
            if (!TryRoom(roomId, out var room, out var err))
            {
                return (null, null, err);
            }

            if (peerId != room.HostPeerId)
            {
                return (null, null, "only the host may end the session");
            }

            MarkComplete(room);
            room.Playing = false;
            if (!consent && !room.ArchiveDefaultConsent)
            {
                _rooms.Remove(roomId);
                return (null, null, null);
            }

            var hearts = room.Reactions
                .Where(r => r.Emoji is "♥" or "heart" or "❤" or "❤️")
                .Select(r => r.TrackId);
            var completed = room.PlayedTrackIds.Distinct();
            var highlights = room.Pins.Select(p => p.TrackId)
                .Concat(hearts)
                .Distinct()
                .ToList();
            var trackIds = room.PlayedTrackIds
                .Concat(room.Queue.Select(q => q.TrackId))
                .Distinct()
                .ToList();
            var archive = new SessionArchive
            {
                Id = Guid.NewGuid().ToString("n")[..12],
                RoomId = room.Id,
                RoomName = room.Name,
                StartedAt = room.StartedAt,
                EndedAt = DateTimeOffset.UtcNow,
                Mode = room.Mode,
                HostPeerId = room.AnonymizeArchive ? null : room.HostPeerId,
                TrackIds = trackIds,
                HighlightTrackIds = highlights.Count > 0 ? highlights : completed.ToList(),
                Pins = room.AnonymizeArchive ? Anonymize(room.Pins) : [.. room.Pins],
                Reactions = room.AnonymizeArchive ? Anonymize(room.Reactions) : [.. room.Reactions],
                Chat = room.AnonymizeArchive ? [] : [.. room.Chat],
                Participants = room.AnonymizeArchive
                    ? []
                    : room.PeerNames.Where(kv => kv.Value is not null).Select(kv => kv.Value).Distinct().ToList(),
                Hits = SegmentHits(room),
                Anonymized = room.AnonymizeArchive,
                RetentionDays = room.CommentRetentionDays
            };
            _history.SaveArchive(archive);
            var playlist = _history.PlaylistFromArchive(archive, room.HostPeerId);
            _rooms.Remove(roomId);
            return (archive, playlist, null);
        }
    }

    private static List<TimestampPin> Anonymize(IEnumerable<TimestampPin> pins)
        => pins.Select(p => p with { PeerId = "anon" }).ToList();

    private static List<Reaction> Anonymize(IEnumerable<Reaction> reactions)
        => reactions.Select(r => r with { PeerId = "anon" }).ToList();

    /// <summary>핀·반응이 몰린 10초 버킷 = 그 세션에서 사람들이 반응한 구간.</summary>
    private static List<SegmentHit> SegmentHits(ListeningRoom room)
    {
        const long bucket = 10_000;
        var hits = new Dictionary<(string, long), int>();
        foreach (var pin in room.Pins)
        {
            var key = (pin.TrackId, pin.MediaTimeMs / bucket * bucket);
            hits[key] = hits.GetValueOrDefault(key) + 1;
        }

        foreach (var reaction in room.Reactions)
        {
            var key = (reaction.TrackId, reaction.MediaTimeMs / bucket * bucket);
            hits[key] = hits.GetValueOrDefault(key) + 1;
        }

        return hits.Select(kv => new SegmentHit(kv.Key.Item1, kv.Key.Item2, kv.Value))
            .OrderByDescending(h => h.Count)
            .ToList();
    }

    public string SnapshotJson(ListeningRoom room)
        => JsonSerializer.Serialize(Snapshot(room), LineFraming.JsonOptions);

    public object Snapshot(ListeningRoom room)
    {
        var tracks = _catalog.Tracks;
        var track = room.CurrentTrack(tracks);
        var album = track is null ? null : _catalog.Albums.GetValueOrDefault(track.AlbumId);
        var artist = track is null ? null : _catalog.Artists.GetValueOrDefault(track.ArtistId);
        var lyrics = LyricsParser.Parse(track?.LyricsLrc);
        var media = room.CurrentMediaTimeMs();
        var duration = EffectiveDuration(track);
        return new
        {
            room.Id,
            room.Name,
            room.Mode,
            room.SourceMode,
            room.QualityPolicy,
            room.DspPreset,
            room.DspEnabled,
            room.DspLocked,
            room.ConvolutionIrPath,
            room.EasyEqJson,
            room.EasyEqGraphicMode,
            room.HeadroomDb,
            room.SpeakerDelayMsLeft,
            room.SpeakerDelayMsRight,
            room.SpeakerGainLeftDb,
            room.SpeakerGainRightDb,
            room.DeviceEqProfile,
            room.SeekingAllowed,
            room.CommentsAllowed,
            room.ChatCollapsed,
            room.QueueLocked,
            room.FollowHostView,
            room.AutoAdvance,
            room.LinerPage,
            room.LinerScrollY,
            room.MaxMembers,
            room.InviteCode,
            room.InviteExpiresAt,
            room.Playing,
            room.QueueIndex,
            room.HostPeerId,
            room.ResyncEpoch,
            room.ArchiveDefaultConsent,
            room.CommentRetentionDays,
            room.AnonymizeArchive,
            room.CloudSyncOptIn,
            startedAt = room.StartedAt,
            mediaTimeMs = media,
            durationMs = duration,
            room.MediaOriginUnixMs,
            room.MediaTimeAtOriginMs,
            room.BitPerfect,
            room.SrcApplied,
            room.PathBadge,
            queue = room.Queue.Select(q => QueueView(q, tracks)),
            requests = room.Requests.Select(r => new
            {
                r.Id,
                r.TrackId,
                r.FromPeerId,
                fromName = room.PeerNames.GetValueOrDefault(r.FromPeerId, r.FromPeerId),
                title = tracks.GetValueOrDefault(r.TrackId)?.Title ?? r.TrackId
            }),
            pins = room.Pins.OrderBy(p => p.MediaTimeMs).Select(p => new
            {
                p.Id,
                p.PeerId,
                peerName = room.PeerNames.GetValueOrDefault(p.PeerId, p.PeerId),
                p.TrackId,
                p.MediaTimeMs,
                p.Text,
                p.CreatedAt,
                onCurrentTrack = track is not null && p.TrackId == track.Id
            }),
            reactions = room.Reactions.TakeLast(50),
            reactionCounts = track is null
                ? []
                : room.Reactions.Where(r => r.TrackId == track.Id)
                    .GroupBy(r => r.Emoji)
                    .ToDictionary(g => g.Key, g => g.Count()),
            allowedReactionEmoji = AllowedReactionEmoji,
            heatmap = track is null ? [] : ReactionHeatmap(room, []),
            smartAutoplay = room.SmartAutoplay,
            autoplay = room.AutoplayCandidateIds.Count == 0 ? null : new
            {
                candidates = room.AutoplayCandidateIds.Select(id => tracks.GetValueOrDefault(id)).Where(t => t is not null).Select(t => TrackView(t!)),
                deadlineUnixMs = room.AutoplayDeadlineUnixMs,
                chosenId = room.AutoplayChosenId
            },
            chat = room.Chat.TakeLast(60).Select(c => new
            {
                c.PeerId,
                peerName = room.PeerNames.GetValueOrDefault(c.PeerId, c.PeerId),
                c.Text,
                c.At
            }),
            members = AllPeers(room).Select(id => new
            {
                peerId = id,
                name = room.PeerNames.GetValueOrDefault(id, id),
                role = room.RoleOf(id),
                isOutput = room.Outputs.ContainsKey(id),
                spectator = room.SpectatorPeerIds.Contains(id),
                stats = room.Stats.GetValueOrDefault(id)
            }),
            outputs = room.Outputs.Values.Select(c => new
            {
                c.PeerId,
                c.DisplayName,
                c.MaxSampleRate,
                c.MaxBitDepth,
                c.SupportsDsd,
                c.ExclusiveMode,
                c.ReportedLatencyMs,
                c.HardwareVolume,
                c.VolumePercent,
                c.Device,
                spectator = room.SpectatorPeerIds.Contains(c.PeerId),
                badge = track is null ? "Idle" : QualityPolicyEngine.Evaluate(room, track, c).Badge,
                note = track is null ? null : QualityPolicyEngine.Evaluate(room, track, c).Error,
                stats = room.Stats.GetValueOrDefault(c.PeerId)
            }),
            spectators = room.SpectatorPeerIds,
            peers = room.PeerNames,
            currentTrack = track is null ? null : TrackView(track),
            album,
            artist,
            linerNotes = album?.LinerNotes,
            credits = album?.Credits,
            albumTracks = album is null
                ? Array.Empty<object>()
                : tracks.Values.Where(t => t.AlbumId == album.Id).OrderBy(t => t.TrackNumber).Select(t => (object)TrackView(t)).ToArray(),
            relatedArtists = artist?.RelatedArtistIds
                .Select(id => _catalog.Artists.GetValueOrDefault(id))
                .Where(a => a is not null),
            lyrics,
            currentLyric = LyricsParser.At(lyrics, media)?.Text,
            catalogCount = tracks.Count
        };
    }

    private object QueueView(QueueItem item, IReadOnlyDictionary<string, Track> tracks)
    {
        var t = tracks.GetValueOrDefault(item.TrackId);
        return new
        {
            item.Id,
            item.TrackId,
            item.AddedByPeerId,
            title = t?.Title ?? item.TrackId,
            artist = t is null ? null : _catalog.Artists.GetValueOrDefault(t.ArtistId)?.Name,
            durationMs = t?.DurationMs ?? 0,
            badge = t is null ? "" : QualityPolicyEngine.Badge(t, false),
            artUrl = t?.ArtworkPath is null ? null : $"/api/art/{t.Id}"
        };
    }

    private object TrackView(Track t) => new
    {
        t.Id,
        t.Title,
        t.AlbumId,
        t.ArtistId,
        artistName = _catalog.Artists.GetValueOrDefault(t.ArtistId)?.Name,
        albumTitle = _catalog.Albums.GetValueOrDefault(t.AlbumId)?.Title,
        t.SampleRate,
        t.BitDepth,
        t.Channels,
        t.IsDsd,
        t.DsdRate,
        t.DurationMs,
        t.Source,
        t.StreamingQuality,
        t.MergedLocalAndStreaming,
        hasLocal = t.LocalPath is not null,
        badge = QualityPolicyEngine.Badge(t, false),
        artUrl = t.ArtworkPath is null ? null : $"/api/art/{t.Id}"
    };

    public static IEnumerable<string> AllPeers(ListeningRoom room)
        => room.ControlPeerIds
            .Concat(room.OutputPeerIds)
            .Concat(room.SpectatorPeerIds)
            .Distinct();

    public long EffectiveDurationOf(ListeningRoom room)
        => EffectiveDuration(room.CurrentTrack(_catalog.Tracks));

    private static long EffectiveDuration(Track? track)
        => track is null ? 0 : track.DurationMs > 0 ? track.DurationMs : 180_000;

    private void ApplyPolicyToOutputs(ListeningRoom room, Track track)
    {
        foreach (var cap in room.Outputs.Values)
        {
            var decision = QualityPolicyEngine.Evaluate(room, track, cap);
            if (decision.Spectator)
            {
                room.SpectatorPeerIds.Add(cap.PeerId);
                room.OutputPeerIds.Remove(cap.PeerId);
            }
            else if (room.Roles.GetValueOrDefault(cap.PeerId) != MemberRole.Spectator)
            {
                room.SpectatorPeerIds.Remove(cap.PeerId);
                room.OutputPeerIds.Add(cap.PeerId);
            }
        }
    }

    private void StartTrackTimeline(ListeningRoom room, long mediaTimeMs)
    {
        room.MediaTimeAtOriginMs = Math.Max(0, mediaTimeMs);
        room.MediaOriginUnixMs = ClockSync.UnixMs();
        room.ResyncEpoch++;
        room.AutoplayProposedForTrackId = null;
        room.AutoplayCandidateIds.Clear();
        room.AutoplayDeadlineUnixMs = null;
        room.AutoplayChosenId = null;
        var track = room.CurrentTrack(_catalog.Tracks);
        if (track is not null)
        {
            ApplyPolicyToOutputs(room, track);
        }

        RefreshPath(room);
    }

    private void NoteNowPlaying(ListeningRoom room, Track? track)
    {
        if (track is null)
        {
            return;
        }

        if (room.PlayedTrackIds.LastOrDefault() != track.Id)
        {
            room.PlayedTrackIds.Add(track.Id);
        }

        foreach (var pid in room.ControlPeerIds)
        {
            _history.RecordOrComplete(new ListeningHistoryEntry
            {
                Id = Guid.NewGuid().ToString("n")[..12],
                PeerId = pid,
                TrackId = track.Id,
                RoomId = room.Id,
                RoomName = room.Name,
                HeardAt = DateTimeOffset.UtcNow
            });
        }
    }

    private void RefreshPath(ListeningRoom room)
    {
        var track = room.CurrentTrack(_catalog.Tracks);
        if (track is null)
        {
            room.PathBadge = "Idle";
            room.BitPerfect = true;
            room.SrcApplied = false;
            return;
        }

        var src = room.DspEnabled && room.DspPreset is DspPresetKind.Headphones or DspPresetKind.Speakers;
        if (room.QualityPolicy == QualityPolicy.LowestCommonFormat && room.Outputs.Count > 0)
        {
            var (rate, depth) = QualityPolicyEngine.CommonFormat(room.Outputs.Values, track);
            src = src || rate < track.SampleRate || depth < track.BitDepth;
        }

        room.SrcApplied = src;
        room.BitPerfect = !src && !room.DspEnabled;
        room.PathBadge = QualityPolicyEngine.Badge(track, src);
    }

    private void MarkComplete(ListeningRoom room, bool forceComplete = false)
    {
        var track = room.CurrentTrack(_catalog.Tracks);
        if (track is null)
        {
            return;
        }

        var played = room.CurrentMediaTimeMs();
        var done = forceComplete || played > EffectiveDuration(track) * 0.8;
        foreach (var pid in room.ControlPeerIds)
        {
            _history.RecordOrComplete(new ListeningHistoryEntry
            {
                Id = Guid.NewGuid().ToString("n")[..12],
                PeerId = pid,
                TrackId = track.Id,
                RoomId = room.Id,
                RoomName = room.Name,
                HeardAt = DateTimeOffset.UtcNow,
                Completed = done
            });
        }
    }

    private bool TryRoom(string roomId, out ListeningRoom room, out string? err)
    {
        if (_rooms.TryGetValue(roomId, out room!))
        {
            err = null;
            return true;
        }

        err = "room not found";
        room = null!;
        return false;
    }

    private bool CanEditQueue(string roomId, string peerId, out ListeningRoom room, out string? err)
    {
        if (!TryRoom(roomId, out room, out err))
        {
            return false;
        }

        if (room.Mode is RoomMode.HostQueue or RoomMode.Audiophile && !room.CanDirect(peerId))
        {
            err = "only the host may edit the queue";
            return false;
        }

        if (room.QueueLocked && !room.CanDirect(peerId))
        {
            err = "queue is locked";
            return false;
        }

        return true;
    }
}
