using Mono.Shared;
using Microsoft.Data.Sqlite;

namespace Mono.Core;

/// <summary>
/// Core가 아는 출력 엔드포인트 목록. 연결이 끊겨도 기기는 목록에 남고
/// 마지막 능력·볼륨·상태를 기억한다(Core 서비스 운영: 엔드포인트 상태).
/// </summary>
public sealed class EndpointRegistry
{
    private readonly string _dbPath;
    private readonly object _gate = new();

    public EndpointRegistry(string dbPath)
    {
        _dbPath = dbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        using var con = Open();
        con.Execute("""
            CREATE TABLE IF NOT EXISTS endpoints(
              peer_id TEXT PRIMARY KEY, name TEXT, max_rate INT, max_depth INT, dsd INT,
              exclusive INT, latency INT, hw_volume INT, volume INT, device TEXT,
              room_id TEXT, online INT, last_seen TEXT);
            """);
        using var reset = con.CreateCommand();
        reset.CommandText = "UPDATE endpoints SET online=0";
        reset.ExecuteNonQuery();
    }

    public void Announce(OutputCapability cap, string? roomId)
    {
        lock (_gate)
        {
            using var con = Open();
            con.Execute("""
                INSERT INTO endpoints(peer_id,name,max_rate,max_depth,dsd,exclusive,latency,hw_volume,volume,device,room_id,online,last_seen)
                VALUES($p,$n,$r,$d,$dsd,$x,$l,$hv,$v,$dev,$room,1,$t)
                ON CONFLICT(peer_id) DO UPDATE SET
                  name=$n, max_rate=$r, max_depth=$d, dsd=$dsd, exclusive=$x, latency=$l,
                  hw_volume=$hv, volume=$v, device=$dev, room_id=$room, online=1, last_seen=$t
                """,
                ("$p", cap.PeerId), ("$n", cap.DisplayName), ("$r", cap.MaxSampleRate), ("$d", cap.MaxBitDepth),
                ("$dsd", cap.SupportsDsd ? 1 : 0), ("$x", cap.ExclusiveMode ? 1 : 0), ("$l", cap.ReportedLatencyMs),
                ("$hv", cap.HardwareVolume ? 1 : 0), ("$v", cap.VolumePercent), ("$dev", cap.Device),
                ("$room", roomId), ("$t", DateTimeOffset.UtcNow.ToString("o")));
        }
    }

    public void Offline(string peerId)
    {
        lock (_gate)
        {
            using var con = Open();
            con.Execute("UPDATE endpoints SET online=0, room_id=NULL, last_seen=$t WHERE peer_id=$p",
                ("$t", DateTimeOffset.UtcNow.ToString("o")), ("$p", peerId));
        }
    }

    public void SetVolume(string peerId, int volume)
    {
        lock (_gate)
        {
            using var con = Open();
            con.Execute("UPDATE endpoints SET volume=$v WHERE peer_id=$p", ("$v", Math.Clamp(volume, 0, 100)), ("$p", peerId));
        }
    }

    public IReadOnlyList<EndpointRecord> All()
    {
        lock (_gate)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT peer_id,name,max_rate,max_depth,dsd,exclusive,latency,hw_volume,volume,device,room_id,online,last_seen FROM endpoints ORDER BY online DESC, last_seen DESC";
            var list = new List<EndpointRecord>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new EndpointRecord
                {
                    PeerId = r.GetString(0),
                    DisplayName = r.IsDBNull(1) ? r.GetString(0) : r.GetString(1),
                    MaxSampleRate = r.GetInt32(2),
                    MaxBitDepth = r.GetInt32(3),
                    SupportsDsd = r.GetInt32(4) == 1,
                    ExclusiveMode = r.GetInt32(5) == 1,
                    LatencyMs = r.GetInt64(6),
                    HardwareVolume = r.GetInt32(7) == 1,
                    VolumePercent = r.GetInt32(8),
                    Device = r.IsDBNull(9) ? null : r.GetString(9),
                    RoomId = r.IsDBNull(10) ? null : r.GetString(10),
                    Online = r.GetInt32(11) == 1,
                    LastSeen = DateTimeOffset.Parse(r.GetString(12))
                });
            }

            return list;
        }
    }

    private SqliteConnection Open()
    {
        var con = new SqliteConnection($"Data Source={_dbPath}");
        con.Open();
        return con;
    }
}
