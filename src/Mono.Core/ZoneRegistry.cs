using Mono.Shared;
using Microsoft.Data.Sqlite;

namespace Mono.Core;

/// <summary>
/// 멀티 디바이스 존 — 한 사용자가 가진 여러 출력기기(WASAPI/ASIO)를 묶어
/// 싱글 플레이로 관리한다. Sync 모드는 존의 모든 기기를 하나의 개인 방에 모으고,
/// Independent 모드는 기기마다 별도 개인 방을 배정해 각자 다른 곡을 듣게 한다.
/// 실제 방 배정/재생은 RoomManager가 하고, 이 클래스는 존의 구성만 기억한다.
/// </summary>
public sealed class ZoneRegistry
{
    private readonly string _dbPath;
    private readonly object _gate = new();

    public ZoneRegistry(string dbPath)
    {
        _dbPath = dbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        using var con = Open();
        con.Execute("""
            CREATE TABLE IF NOT EXISTS zones(
              id TEXT PRIMARY KEY, name TEXT, owner_peer_id TEXT, mode INT, sync_room_id TEXT);
            CREATE TABLE IF NOT EXISTS zone_members(zone_id TEXT, peer_id TEXT, PRIMARY KEY(zone_id, peer_id));
            CREATE TABLE IF NOT EXISTS zone_independent_rooms(zone_id TEXT, peer_id TEXT, room_id TEXT, PRIMARY KEY(zone_id, peer_id));
            """);
    }

    public Zone Create(string ownerPeerId, string name)
    {
        lock (_gate)
        {
            var zone = new Zone { Id = Guid.NewGuid().ToString("n")[..10], Name = name, OwnerPeerId = ownerPeerId };
            using var con = Open();
            con.Execute("INSERT INTO zones(id,name,owner_peer_id,mode,sync_room_id) VALUES($i,$n,$o,$m,NULL)",
                ("$i", zone.Id), ("$n", zone.Name), ("$o", zone.OwnerPeerId), ("$m", (int)zone.Mode));
            return zone;
        }
    }

    public Zone? Get(string zoneId)
    {
        lock (_gate)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT id,name,owner_peer_id,mode,sync_room_id FROM zones WHERE id=$i";
            cmd.Parameters.AddWithValue("$i", zoneId);
            using var r = cmd.ExecuteReader();
            if (!r.Read())
            {
                return null;
            }

            return Materialize(con, r);
        }
    }

    public IReadOnlyList<Zone> ForOwner(string ownerPeerId)
    {
        lock (_gate)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT id,name,owner_peer_id,mode,sync_room_id FROM zones WHERE owner_peer_id=$o";
            cmd.Parameters.AddWithValue("$o", ownerPeerId);
            var list = new List<Zone>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(Materialize(con, r));
            }

            return list;
        }
    }

    public bool Rename(string zoneId, string name)
    {
        lock (_gate)
        {
            using var con = Open();
            con.Execute("UPDATE zones SET name=$n WHERE id=$i", ("$n", name), ("$i", zoneId));
            return true;
        }
    }

    public bool SetMode(string zoneId, ZoneMode mode)
    {
        lock (_gate)
        {
            using var con = Open();
            con.Execute("UPDATE zones SET mode=$m WHERE id=$i", ("$m", (int)mode), ("$i", zoneId));
            return true;
        }
    }

    public void SetSyncRoom(string zoneId, string roomId)
    {
        lock (_gate)
        {
            using var con = Open();
            con.Execute("UPDATE zones SET sync_room_id=$r WHERE id=$i", ("$r", roomId), ("$i", zoneId));
        }
    }

    public void SetIndependentRoom(string zoneId, string peerId, string roomId)
    {
        lock (_gate)
        {
            using var con = Open();
            con.Execute(
                "INSERT INTO zone_independent_rooms(zone_id,peer_id,room_id) VALUES($z,$p,$r) ON CONFLICT(zone_id,peer_id) DO UPDATE SET room_id=$r",
                ("$z", zoneId), ("$p", peerId), ("$r", roomId));
        }
    }

    public void AddMember(string zoneId, string peerId)
    {
        lock (_gate)
        {
            using var con = Open();
            con.Execute("INSERT OR IGNORE INTO zone_members(zone_id,peer_id) VALUES($z,$p)", ("$z", zoneId), ("$p", peerId));
        }
    }

    public void RemoveMember(string zoneId, string peerId)
    {
        lock (_gate)
        {
            using var con = Open();
            con.Execute("DELETE FROM zone_members WHERE zone_id=$z AND peer_id=$p", ("$z", zoneId), ("$p", peerId));
            con.Execute("DELETE FROM zone_independent_rooms WHERE zone_id=$z AND peer_id=$p", ("$z", zoneId), ("$p", peerId));
        }
    }

    public void Delete(string zoneId)
    {
        lock (_gate)
        {
            using var con = Open();
            con.Execute("DELETE FROM zones WHERE id=$i", ("$i", zoneId));
            con.Execute("DELETE FROM zone_members WHERE zone_id=$i", ("$i", zoneId));
            con.Execute("DELETE FROM zone_independent_rooms WHERE zone_id=$i", ("$i", zoneId));
        }
    }

    private static Zone Materialize(SqliteConnection con, SqliteDataReader r)
    {
        var zone = new Zone
        {
            Id = r.GetString(0),
            Name = r.GetString(1),
            OwnerPeerId = r.GetString(2),
            Mode = (ZoneMode)r.GetInt32(3),
            SyncRoomId = r.IsDBNull(4) ? null : r.GetString(4)
        };

        using (var mcmd = con.CreateCommand())
        {
            mcmd.CommandText = "SELECT peer_id FROM zone_members WHERE zone_id=$z";
            mcmd.Parameters.AddWithValue("$z", zone.Id);
            using var mr = mcmd.ExecuteReader();
            while (mr.Read())
            {
                zone.MemberPeerIds.Add(mr.GetString(0));
            }
        }

        using (var icmd = con.CreateCommand())
        {
            icmd.CommandText = "SELECT peer_id,room_id FROM zone_independent_rooms WHERE zone_id=$z";
            icmd.Parameters.AddWithValue("$z", zone.Id);
            using var ir = icmd.ExecuteReader();
            while (ir.Read())
            {
                zone.IndependentRoomIds[ir.GetString(0)] = ir.GetString(1);
            }
        }

        return zone;
    }

    private SqliteConnection Open()
    {
        var con = new SqliteConnection($"Data Source={_dbPath}");
        con.Open();
        return con;
    }
}
