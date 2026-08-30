using System.Text.Json;
using Mono.Shared;
using Microsoft.Data.Sqlite;

namespace Mono.Core;

/// <summary>
/// 개인 라이브러리(청음 히스토리 · 세션 아카이브 · 플레이리스트). 로컬 SQLite가 기본이고
/// 클라우드 동기화는 룸 정책의 명시적 opt-in에서만 다룬다.
/// </summary>
public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _dbPath;
    private readonly object _gate = new();

    public HistoryStore(string dbPath)
    {
        _dbPath = dbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        using var con = Open();
        con.Execute("""
            CREATE TABLE IF NOT EXISTS history(
              id TEXT PRIMARY KEY, peer_id TEXT, track_id TEXT, room_id TEXT, room_name TEXT, heard_at TEXT, completed INT);
            CREATE INDEX IF NOT EXISTS idx_hist_peer ON history(peer_id, heard_at);
            CREATE TABLE IF NOT EXISTS archives(
              id TEXT PRIMARY KEY, room_id TEXT, room_name TEXT, host_peer_id TEXT, ended_at TEXT, json TEXT);
            CREATE TABLE IF NOT EXISTS playlists(
              id TEXT PRIMARY KEY, title TEXT, owner_peer_id TEXT, created_at TEXT, from_archive TEXT, json TEXT);
            """);
    }

    public void Record(ListeningHistoryEntry entry)
    {
        lock (_gate)
        {
            using var con = Open();
            con.Execute(
                "INSERT OR REPLACE INTO history(id,peer_id,track_id,room_id,room_name,heard_at,completed) VALUES($i,$p,$t,$r,$n,$h,$c)",
                ("$i", entry.Id), ("$p", entry.PeerId), ("$t", entry.TrackId), ("$r", entry.RoomId),
                ("$n", entry.RoomName), ("$h", entry.HeardAt.ToString("o")), ("$c", entry.Completed ? 1 : 0));
        }
    }

    /// <summary>같은 룸·같은 트랙의 재기록은 완청 여부만 승격시킨다.</summary>
    public void RecordOrComplete(ListeningHistoryEntry entry)
    {
        lock (_gate)
        {
            using var con = Open();
            using var find = con.CreateCommand();
            find.CommandText = "SELECT id FROM history WHERE peer_id=$p AND track_id=$t AND IFNULL(room_id,'')=IFNULL($r,'') ORDER BY heard_at DESC LIMIT 1";
            find.Parameters.AddWithValue("$p", entry.PeerId);
            find.Parameters.AddWithValue("$t", entry.TrackId);
            find.Parameters.AddWithValue("$r", (object?)entry.RoomId ?? DBNull.Value);
            var existing = find.ExecuteScalar() as string;
            if (existing is not null)
            {
                con.Execute("UPDATE history SET completed=MAX(completed,$c), heard_at=$h WHERE id=$i",
                    ("$c", entry.Completed ? 1 : 0), ("$h", entry.HeardAt.ToString("o")), ("$i", existing));
                return;
            }

            con.Execute(
                "INSERT OR REPLACE INTO history(id,peer_id,track_id,room_id,room_name,heard_at,completed) VALUES($i,$p,$t,$r,$n,$h,$c)",
                ("$i", entry.Id), ("$p", entry.PeerId), ("$t", entry.TrackId), ("$r", entry.RoomId),
                ("$n", entry.RoomName), ("$h", entry.HeardAt.ToString("o")), ("$c", entry.Completed ? 1 : 0));
        }
    }

    public List<ListeningHistoryEntry> ForPeer(string peerId)
    {
        lock (_gate)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT id,peer_id,track_id,room_id,room_name,heard_at,completed FROM history WHERE peer_id=$p ORDER BY heard_at DESC LIMIT 200";
            cmd.Parameters.AddWithValue("$p", peerId);
            var list = new List<ListeningHistoryEntry>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new ListeningHistoryEntry
                {
                    Id = r.GetString(0),
                    PeerId = r.GetString(1),
                    TrackId = r.GetString(2),
                    RoomId = r.IsDBNull(3) ? null : r.GetString(3),
                    RoomName = r.IsDBNull(4) ? null : r.GetString(4),
                    HeardAt = DateTimeOffset.Parse(r.GetString(5)),
                    Completed = r.GetInt32(6) == 1
                });
            }

            return list;
        }
    }

    public SessionArchive SaveArchive(SessionArchive archive)
    {
        lock (_gate)
        {
            using var con = Open();
            con.Execute(
                "INSERT OR REPLACE INTO archives(id,room_id,room_name,host_peer_id,ended_at,json) VALUES($i,$r,$n,$h,$e,$j)",
                ("$i", archive.Id), ("$r", archive.RoomId), ("$n", archive.RoomName),
                ("$h", archive.HostPeerId), ("$e", archive.EndedAt.ToString("o")),
                ("$j", JsonSerializer.Serialize(archive, Json)));
            return archive;
        }
    }

    public IReadOnlyList<SessionArchive> Archives
    {
        get
        {
            lock (_gate)
            {
                using var con = Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "SELECT json FROM archives ORDER BY ended_at DESC LIMIT 100";
                var list = new List<SessionArchive>();
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var a = JsonSerializer.Deserialize<SessionArchive>(r.GetString(0), Json);
                    if (a is not null)
                    {
                        list.Add(a);
                    }
                }

                return list;
            }
        }
    }

    public SessionArchive? Archive(string id) => Archives.FirstOrDefault(a => a.Id == id);

    /// <summary>
    /// 한 트랙의 전체 재생 이력에 걸친 "가장 반응 많은 구간" — 유튜브 하이라이트 그래프처럼
    /// 과거 세션 전부의 핀·반응을 10초 버킷으로 합산한다. 온디맨드로만 호출한다(스냅샷마다 X).
    /// </summary>
    public IReadOnlyList<SegmentHit> TrackHeatmap(string trackId)
    {
        var buckets = new Dictionary<long, int>();
        foreach (var hit in Archives.SelectMany(a => a.Hits).Where(h => h.TrackId == trackId))
        {
            buckets[hit.BucketMs] = buckets.GetValueOrDefault(hit.BucketMs) + hit.Count;
        }

        return buckets.OrderBy(kv => kv.Key).Select(kv => new SegmentHit(trackId, kv.Key, kv.Value)).ToList();
    }

    public IReadOnlyList<UserPlaylist> Playlists
    {
        get
        {
            lock (_gate)
            {
                using var con = Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "SELECT json FROM playlists ORDER BY created_at DESC LIMIT 200";
                var list = new List<UserPlaylist>();
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var p = JsonSerializer.Deserialize<UserPlaylist>(r.GetString(0), Json);
                    if (p is not null)
                    {
                        list.Add(p);
                    }
                }

                return list;
            }
        }
    }

    public UserPlaylist SavePlaylist(UserPlaylist playlist)
    {
        lock (_gate)
        {
            using var con = Open();
            con.Execute(
                "INSERT OR REPLACE INTO playlists(id,title,owner_peer_id,created_at,from_archive,json) VALUES($i,$t,$o,$c,$a,$j)",
                ("$i", playlist.Id), ("$t", playlist.Title), ("$o", playlist.OwnerPeerId),
                ("$c", playlist.CreatedAt.ToString("o")), ("$a", playlist.FromArchiveId),
                ("$j", JsonSerializer.Serialize(playlist, Json)));
            return playlist;
        }
    }

    /// <summary>아카이브의 "좋았던 곡"(핀·♥·완청)만 모아 개인 플레이리스트를 만든다.</summary>
    public UserPlaylist PlaylistFromArchive(SessionArchive archive, string? ownerPeerId = null)
        => SavePlaylist(new UserPlaylist
        {
            Id = Guid.NewGuid().ToString("n")[..10],
            Title = $"{archive.RoomName} · highlights",
            TrackIds = archive.HighlightTrackIds.Count > 0 ? archive.HighlightTrackIds : archive.TrackIds,
            FromArchiveId = archive.Id,
            OwnerPeerId = ownerPeerId ?? archive.HostPeerId
        });

    public UserPlaylist CreatePlaylist(string title, IEnumerable<string> trackIds, string ownerPeerId)
        => SavePlaylist(new UserPlaylist
        {
            Id = Guid.NewGuid().ToString("n")[..10],
            Title = string.IsNullOrWhiteSpace(title) ? "Playlist" : title,
            TrackIds = trackIds.Distinct().ToList(),
            OwnerPeerId = ownerPeerId
        });

    public UserPlaylist? Playlist(string id) => Playlists.FirstOrDefault(p => p.Id == id);

    /// <summary>보존 기간이 지난 코멘트를 아카이브에서 지운다(룸 보존 정책).</summary>
    public int PruneComments(DateTimeOffset now)
    {
        var pruned = 0;
        foreach (var archive in Archives)
        {
            if (archive.RetentionDays <= 0)
            {
                continue;
            }

            var keepPins = archive.Pins.Where(p => (now - p.CreatedAt).TotalDays <= archive.RetentionDays).ToList();
            var keepChat = archive.Chat.Where(c => (now - c.At).TotalDays <= archive.RetentionDays).ToList();
            if (keepPins.Count == archive.Pins.Count && keepChat.Count == archive.Chat.Count)
            {
                continue;
            }

            var trimmed = new SessionArchive
            {
                Id = archive.Id,
                RoomId = archive.RoomId,
                RoomName = archive.RoomName,
                EndedAt = archive.EndedAt,
                StartedAt = archive.StartedAt,
                Mode = archive.Mode,
                HostPeerId = archive.HostPeerId,
                TrackIds = archive.TrackIds,
                HighlightTrackIds = archive.HighlightTrackIds,
                Pins = keepPins,
                Reactions = archive.Reactions,
                Chat = keepChat,
                Participants = archive.Participants,
                Hits = archive.Hits,
                Anonymized = archive.Anonymized,
                RetentionDays = archive.RetentionDays
            };
            SaveArchive(trimmed);
            pruned += archive.Pins.Count - keepPins.Count + archive.Chat.Count - keepChat.Count;
        }

        return pruned;
    }

    public string ExportM3u(UserPlaylist playlist, CatalogStore catalog)
    {
        var lines = new List<string> { "#EXTM3U", $"#PLAYLIST:{playlist.Title}" };
        foreach (var id in playlist.TrackIds)
        {
            if (!catalog.Tracks.TryGetValue(id, out var t))
            {
                continue;
            }

            var artist = catalog.Artists.GetValueOrDefault(t.ArtistId)?.Name ?? "";
            lines.Add($"#EXTINF:{t.DurationMs / 1000},{artist} - {t.Title}");
            lines.Add(t.LocalPath ?? t.StreamingId ?? id);
        }

        return string.Join('\n', lines);
    }

    /// <summary>세션 요약 공유 — 파일이 아니라 트랙 식별자와 메타만 나간다.</summary>
    public string ShareSummary(SessionArchive archive, CatalogStore catalog)
    {
        var lines = new List<string>
        {
            $"# {archive.RoomName}",
            $"{archive.StartedAt:yyyy-MM-dd HH:mm} → {archive.EndedAt:HH:mm} · {archive.Participants.Count}명",
            ""
        };
        foreach (var id in archive.TrackIds)
        {
            var t = catalog.Tracks.GetValueOrDefault(id);
            var artist = t is null ? "" : catalog.Artists.GetValueOrDefault(t.ArtistId)?.Name ?? "";
            var mark = archive.HighlightTrackIds.Contains(id) ? "♥ " : "  ";
            lines.Add($"{mark}{artist} — {t?.Title ?? id}  [{id}]");
        }

        if (archive.Pins.Count > 0)
        {
            lines.Add("");
            lines.Add("## 핀");
            foreach (var p in archive.Pins.OrderBy(p => p.MediaTimeMs))
            {
                lines.Add($"{TimeSpan.FromMilliseconds(p.MediaTimeMs):mm\\:ss} {p.Text}");
            }
        }

        return string.Join('\n', lines);
    }

    private SqliteConnection Open()
    {
        var con = new SqliteConnection($"Data Source={_dbPath}");
        con.Open();
        return con;
    }
}
