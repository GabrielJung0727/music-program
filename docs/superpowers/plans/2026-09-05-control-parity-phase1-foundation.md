# Control 패리티 1단계 — 기반 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Core 룸 스냅샷을 타입 계약으로 고정하고, GUI에서 도달 불가능하던 명령 전체를 `CoreSession`에 배선하며, 969줄 `MainViewModel`을 화면별로 분해한다 — **사용자에게 보이는 동작 변화는 없다.**

**Architecture:** 스냅샷 DTO(`RoomSnapshot`)는 Control이 아니라 **`Mono.Protocol`** 에 둔다. `02-아키텍처.md`가 Protocol을 3모듈 공유 계약층으로 규정하고, 테스트 프로젝트가 Avalonia 의존 없이 계약을 검증할 수 있기 때문이다. `CoreSession`은 도메인별 `partial`로 쪼개고, `MainViewModel`은 셸(내비·하단바·세션)만 남기고 화면 상태를 페이지 VM으로 옮긴다.

**Tech Stack:** C# / .NET 8 · Avalonia 11 · CommunityToolkit.Mvvm · System.Text.Json · xunit

**Spec:** [`docs/superpowers/specs/2026-09-05-control-full-parity-design.md`](../specs/2026-09-05-control-full-parity-design.md)

## Global Constraints

- 대상 프레임워크 `net8.0`. 새 NuGet 패키지를 추가하지 않는다.
- JSON은 항상 `LineFraming.JsonOptions`를 쓴다 — `PropertyNamingPolicy = CamelCase`, `DefaultIgnoreCondition = WhenWritingNull`. **`JsonStringEnumConverter`가 없으므로 모든 열거형은 정수로 직렬화된다.**
- 의존 방향: `Control` / `Output` / `Core` → `Protocol` → `Shared`. Control은 Core를 참조하지 않는다.
- `dotnet build Mono.slnx` 경고 0 유지. 현재 0이다.
- 기존 44개 테스트는 수정하지 않는다. 회귀 가드다.
- 사용자 대상 문자열은 한국어. 코드 주석도 기존 파일의 한국어 관례를 따른다.
- 1단계는 **기능을 추가하지 않는다.** 화면에 새 버튼이 생기면 안 된다. 명령은 배선만 하고 UI 노출은 2단계 이후다.
- 커밋 메시지 말미에 `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

---

### Task 1: 스냅샷 스칼라 계약

`RoomManager.Snapshot`이 내보내는 최상위 스칼라 필드를 타입으로 고정한다.

**Files:**
- Create: `src/Mono.Protocol/RoomSnapshot.cs`
- Test: `tests/Mono.Tests/SnapshotContractTests.cs`

**Interfaces:**
- Consumes: `Mono.Shared`의 `RoomMode` `PlaybackSourceMode` `QualityPolicy` `DspPresetKind` 열거형.
- Produces: `Mono.Protocol.RoomSnapshot` — 이후 모든 태스크가 쓰는 DTO. 정적 진입점은 `RoomSnapshot.Parse(string json)`, 반환 `RoomSnapshot?`.

- [x] **Step 1: 실패하는 테스트를 쓴다**

`tests/Mono.Tests/SnapshotContractTests.cs`:

```csharp
using Mono.Core;
using Mono.Protocol;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// Core가 내보내는 룸 스냅샷과 Control이 읽는 DTO의 계약을 고정한다.
/// Core가 필드를 바꾸거나 없애면 여기서 깨져야 한다.
/// </summary>
public class SnapshotContractTests
{
    private static (RoomManager Rooms, CatalogStore Catalog) NewStack()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        var history = new HistoryStore(Path.Combine(dir, "h.db"));
        var streaming = new StreamingHub(catalog);
        var endpoints = new EndpointRegistry(Path.Combine(dir, "e.db"));
        return (new RoomManager(catalog, history, streaming, endpoints), catalog);
    }

    [Fact]
    public void ScalarsSurviveRoundTrip()
    {
        var (rooms, _) = NewStack();
        var room = rooms.Create("host", "Night Lounge", RoomMode.Audiophile, "Host");

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room));

        Assert.NotNull(snap);
        Assert.Equal(room.Id, snap!.Id);
        Assert.Equal("Night Lounge", snap.Name);
        Assert.Equal(RoomMode.Audiophile, snap.Mode);
        Assert.Equal(QualityPolicy.RequireBitPerfect, snap.QualityPolicy);
        Assert.Equal("host", snap.HostPeerId);
        Assert.False(snap.Playing);
        Assert.False(snap.DspEnabled);
        Assert.False(snap.SeekingAllowed);
        Assert.True(snap.ChatCollapsed);
    }

    [Fact]
    public void PolicyFlagsSurviveRoundTrip()
    {
        var (rooms, _) = NewStack();
        var room = rooms.Create("host", "Open", RoomMode.OpenLounge, "Host");
        room.QueueLocked = true;
        room.MaxMembers = 12;
        room.InviteCode = "ABC123";
        room.AutoAdvance = false;

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room))!;

        Assert.True(snap.QueueLocked);
        Assert.Equal(12, snap.MaxMembers);
        Assert.Equal("ABC123", snap.InviteCode);
        Assert.False(snap.AutoAdvance);
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        // Core가 새 필드를 먼저 배포해도 구형 Control이 죽지 않아야 한다.
        var snap = RoomSnapshot.Parse("""{"id":"r1","name":"X","brandNewFieldFromFuture":42}""");
        Assert.NotNull(snap);
        Assert.Equal("r1", snap!.Id);
    }

    [Fact]
    public void MalformedJsonReturnsNull()
    {
        Assert.Null(RoomSnapshot.Parse("not json"));
        Assert.Null(RoomSnapshot.Parse(""));
    }
}
```

- [x] **Step 2: 실패를 확인한다**

```bash
dotnet test tests/Mono.Tests --filter SnapshotContractTests
```

기대: 컴파일 실패 — `RoomSnapshot`이 없다.

- [x] **Step 3: 최소 구현**

`src/Mono.Protocol/RoomSnapshot.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Mono.Shared;

namespace Mono.Protocol;

/// <summary>
/// Core <see cref="MessageTypes.RoomState"/> 페이로드의 타입 계약.
/// Control이 손파싱 대신 이걸 쓴다. 모르는 필드는 무시해 Core 선행 배포를 견딘다.
/// </summary>
public sealed class RoomSnapshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public RoomMode Mode { get; set; }
    public PlaybackSourceMode SourceMode { get; set; }
    public QualityPolicy QualityPolicy { get; set; }

    // DSP
    public DspPresetKind DspPreset { get; set; }
    public bool DspEnabled { get; set; }
    public bool DspLocked { get; set; }
    public string? ConvolutionIrPath { get; set; }
    public string? EasyEqJson { get; set; }
    public bool EasyEqGraphicMode { get; set; }
    public double HeadroomDb { get; set; }
    public double SpeakerDelayMsLeft { get; set; }
    public double SpeakerDelayMsRight { get; set; }
    public double SpeakerGainLeftDb { get; set; }
    public double SpeakerGainRightDb { get; set; }
    public string? DeviceEqProfile { get; set; }

    // 정책 플래그
    public bool SeekingAllowed { get; set; }
    public bool CommentsAllowed { get; set; }
    public bool ChatCollapsed { get; set; }
    public bool QueueLocked { get; set; }
    public bool FollowHostView { get; set; }
    public bool AutoAdvance { get; set; }
    public bool SmartAutoplay { get; set; }
    public int LinerPage { get; set; }
    public double LinerScrollY { get; set; }
    public int MaxMembers { get; set; }
    public string? InviteCode { get; set; }
    public DateTimeOffset? InviteExpiresAt { get; set; }

    // 아카이브 정책
    public bool ArchiveDefaultConsent { get; set; }
    public int CommentRetentionDays { get; set; }
    public bool AnonymizeArchive { get; set; }
    public bool CloudSyncOptIn { get; set; }

    // 재생 타임라인
    public bool Playing { get; set; }
    public int QueueIndex { get; set; }
    public string? HostPeerId { get; set; }
    public long ResyncEpoch { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public long MediaTimeMs { get; set; }
    public long DurationMs { get; set; }
    public long MediaOriginUnixMs { get; set; }
    public long MediaTimeAtOriginMs { get; set; }

    // 신뢰 UI
    public bool BitPerfect { get; set; }
    public bool SrcApplied { get; set; }
    public string? PathBadge { get; set; }

    public int CatalogCount { get; set; }

    /// <summary>깨진 JSON이면 null. 호출자가 화면을 유지할 수 있게 예외를 던지지 않는다.</summary>
    public static RoomSnapshot? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<RoomSnapshot>(json, LineFraming.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

- [x] **Step 4: 통과를 확인한다**

```bash
dotnet test tests/Mono.Tests --filter SnapshotContractTests
```

기대: 4개 통과.

- [x] **Step 5: 커밋**

```bash
git add src/Mono.Protocol/RoomSnapshot.cs tests/Mono.Tests/SnapshotContractTests.cs
git commit -m "Add typed room snapshot contract for scalar fields

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: 스냅샷 컬렉션 계약

큐·요청·핀·반응·히트맵·채팅·멤버·출력을 타입으로 고정한다. Control이 지금 버리고 있는 데이터다.

**Files:**
- Modify: `src/Mono.Protocol/RoomSnapshot.cs`
- Modify: `tests/Mono.Tests/SnapshotContractTests.cs`

**Interfaces:**
- Consumes: Task 1의 `RoomSnapshot`.
- Produces: `SnapshotQueueItem` `SnapshotRequest` `SnapshotPin` `SnapshotReaction` `SnapshotHeatBucket` `SnapshotChatLine` `SnapshotMember` `SnapshotOutput` — 전부 `Mono.Protocol` 네임스페이스. `RoomSnapshot`의 목록 속성은 절대 null이 아니다(빈 목록으로 초기화).

- [x] **Step 1: 실패하는 테스트를 쓴다**

`SnapshotContractTests.cs`에 추가:

```csharp
    [Fact]
    public void QueueAndPinsAndReactionsSurviveRoundTrip()
    {
        var (rooms, _) = NewStack();
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "Host");
        rooms.Enqueue(room.Id, "host", "tr-blue-train");
        rooms.Pin(room.Id, "host", 12_000, "여기 색소폰");
        rooms.React(room.Id, "host", "❤️");

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room))!;

        var q = Assert.Single(snap.Queue);
        Assert.Equal("tr-blue-train", q.TrackId);
        Assert.Equal("host", q.AddedByPeerId);
        Assert.False(string.IsNullOrEmpty(q.Title));

        var pin = Assert.Single(snap.Pins);
        Assert.Equal(12_000, pin.MediaTimeMs);
        Assert.Equal("여기 색소폰", pin.Text);
        Assert.Equal("host", pin.PeerId);

        Assert.Contains(snap.Reactions, r => r.Emoji == "❤️");
        Assert.Equal(4, snap.AllowedReactionEmoji.Count);
        Assert.Contains("🔥", snap.AllowedReactionEmoji);
    }

    [Fact]
    public void MembersCarryRoleAndStats()
    {
        var (rooms, _) = NewStack();
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "Host");

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room))!;

        var host = Assert.Single(snap.Members, m => m.PeerId == "host");
        Assert.Equal(MemberRole.Host, host.Role);
        Assert.False(host.Spectator);
    }

    [Fact]
    public void CollectionsAreNeverNull()
    {
        // 최소 JSON에서도 목록을 순회할 수 있어야 한다 — UI가 null 검사를 안 하도록.
        var snap = RoomSnapshot.Parse("""{"id":"r1"}""")!;
        Assert.Empty(snap.Queue);
        Assert.Empty(snap.Pins);
        Assert.Empty(snap.Members);
        Assert.Empty(snap.Outputs);
        Assert.Empty(snap.Chat);
        Assert.Empty(snap.Heatmap);
        Assert.Empty(snap.Requests);
        Assert.Empty(snap.Reactions);
        Assert.Empty(snap.AllowedReactionEmoji);
        Assert.Empty(snap.Spectators);
    }
```

- [x] **Step 2: 실패를 확인한다**

```bash
dotnet test tests/Mono.Tests --filter SnapshotContractTests
```

기대: 컴파일 실패 — `Queue` `Pins` 등이 없다.

- [x] **Step 3: 최소 구현**

`RoomSnapshot.cs`의 `CatalogCount` 아래, `Parse` 위에 추가:

```csharp
    public List<SnapshotQueueItem> Queue { get; set; } = [];
    public List<SnapshotRequest> Requests { get; set; } = [];
    public List<SnapshotPin> Pins { get; set; } = [];
    public List<SnapshotReaction> Reactions { get; set; } = [];
    public Dictionary<string, int> ReactionCounts { get; set; } = [];
    public List<string> AllowedReactionEmoji { get; set; } = [];
    public List<SnapshotHeatBucket> Heatmap { get; set; } = [];
    public List<SnapshotChatLine> Chat { get; set; } = [];
    public List<SnapshotMember> Members { get; set; } = [];
    public List<SnapshotOutput> Outputs { get; set; } = [];
    public List<string> Spectators { get; set; } = [];
    public Dictionary<string, string> Peers { get; set; } = [];
```

같은 파일 맨 아래에 추가:

```csharp
public sealed class SnapshotQueueItem
{
    public string Id { get; set; } = "";
    public string TrackId { get; set; } = "";
    public string AddedByPeerId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Artist { get; set; }
    public long DurationMs { get; set; }
    public string? Badge { get; set; }
    public string? ArtUrl { get; set; }
}

public sealed class SnapshotRequest
{
    public string Id { get; set; } = "";
    public string TrackId { get; set; } = "";
    public string FromPeerId { get; set; } = "";
    public string? FromName { get; set; }
    public string? Title { get; set; }
}

public sealed class SnapshotPin
{
    public string Id { get; set; } = "";
    public string PeerId { get; set; } = "";
    public string? PeerName { get; set; }
    public string TrackId { get; set; } = "";
    public long MediaTimeMs { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public bool OnCurrentTrack { get; set; }
}

public sealed class SnapshotReaction
{
    public string PeerId { get; set; } = "";
    public string TrackId { get; set; } = "";
    public string Emoji { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public long MediaTimeMs { get; set; }
}

/// <summary>반응 히트맵의 10초 버킷.</summary>
public sealed class SnapshotHeatBucket
{
    public string TrackId { get; set; } = "";
    public long BucketMs { get; set; }
    public int Count { get; set; }
}

public sealed class SnapshotChatLine
{
    public string PeerId { get; set; } = "";
    public string? PeerName { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public sealed class SnapshotMember
{
    public string PeerId { get; set; } = "";
    public string? Name { get; set; }
    public MemberRole Role { get; set; }
    public bool IsOutput { get; set; }
    public bool Spectator { get; set; }
    public PeerStats? Stats { get; set; }
}

public sealed class SnapshotOutput
{
    public string PeerId { get; set; } = "";
    public string? DisplayName { get; set; }
    public int MaxSampleRate { get; set; }
    public int MaxBitDepth { get; set; }
    public bool SupportsDsd { get; set; }
    public bool ExclusiveMode { get; set; }
    public long ReportedLatencyMs { get; set; }
    public bool HardwareVolume { get; set; }
    public int VolumePercent { get; set; }
    public string? Device { get; set; }
    public bool Spectator { get; set; }
    public string? Badge { get; set; }
    public string? Note { get; set; }
    public PeerStats? Stats { get; set; }
}
```

- [x] **Step 4: 통과를 확인한다**

```bash
dotnet test tests/Mono.Tests --filter SnapshotContractTests
```

기대: 7개 통과.

- [x] **Step 5: 커밋**

```bash
git add src/Mono.Protocol/RoomSnapshot.cs tests/Mono.Tests/SnapshotContractTests.cs
git commit -m "Type the room snapshot collections Control was discarding

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: 스냅샷 중첩 객체 계약

현재 트랙·앨범·아티스트·가사·오토플레이를 고정하고, 전체 라운드트립을 한 번에 검증한다.

**Files:**
- Modify: `src/Mono.Protocol/RoomSnapshot.cs`
- Modify: `tests/Mono.Tests/SnapshotContractTests.cs`

**Interfaces:**
- Consumes: Task 1–2의 `RoomSnapshot`.
- Produces: `SnapshotTrack` `SnapshotAlbum` `SnapshotArtist` `SnapshotLyricLine` `SnapshotAutoplay`. `RoomSnapshot.CurrentTrack`은 곡이 없으면 null이다.

- [x] **Step 1: 실패하는 테스트를 쓴다**

`SnapshotContractTests.cs`에 추가:

```csharp
    [Fact]
    public void CurrentTrackAndAlbumSurviveRoundTrip()
    {
        var (rooms, catalog) = NewStack();
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "Host");
        rooms.Enqueue(room.Id, "host", "tr-blue-train");
        rooms.Play(room.Id, "host");

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room))!;
        var expected = catalog.Tracks["tr-blue-train"];

        Assert.NotNull(snap.CurrentTrack);
        Assert.Equal("tr-blue-train", snap.CurrentTrack!.Id);
        Assert.Equal(expected.Title, snap.CurrentTrack.Title);
        Assert.Equal(expected.SampleRate, snap.CurrentTrack.SampleRate);
        Assert.False(string.IsNullOrEmpty(snap.CurrentTrack.ArtistName));
        Assert.NotNull(snap.Album);
        Assert.Equal(expected.AlbumId, snap.Album!.Id);
        Assert.NotNull(snap.Artist);
        Assert.NotEmpty(snap.AlbumTracks);
    }

    [Fact]
    public void EmptyRoomHasNoCurrentTrack()
    {
        var (rooms, _) = NewStack();
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "Host");

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room))!;

        Assert.Null(snap.CurrentTrack);
        Assert.Null(snap.Autoplay);
    }
```

- [x] **Step 2: 실패를 확인한다**

```bash
dotnet test tests/Mono.Tests --filter SnapshotContractTests
```

기대: 컴파일 실패 — `CurrentTrack`이 없다.

- [x] **Step 3: 최소 구현**

`RoomSnapshot.cs`의 `Peers` 아래에 추가:

```csharp
    public SnapshotTrack? CurrentTrack { get; set; }
    public SnapshotAlbum? Album { get; set; }
    public SnapshotArtist? Artist { get; set; }
    public List<SnapshotTrack> AlbumTracks { get; set; } = [];
    public List<SnapshotArtist> RelatedArtists { get; set; } = [];
    public string? LinerNotes { get; set; }
    public string? Credits { get; set; }
    public List<SnapshotLyricLine> Lyrics { get; set; } = [];
    public string? CurrentLyric { get; set; }
    public SnapshotAutoplay? Autoplay { get; set; }
```

파일 맨 아래에 추가:

```csharp
public sealed class SnapshotTrack
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? AlbumId { get; set; }
    public string? ArtistId { get; set; }
    public string? ArtistName { get; set; }
    public string? AlbumTitle { get; set; }
    public int SampleRate { get; set; }
    public int BitDepth { get; set; }
    public int Channels { get; set; }
    public bool IsDsd { get; set; }
    public int? DsdRate { get; set; }
    public long DurationMs { get; set; }
    public StreamingProvider Source { get; set; }
    public StreamingQuality StreamingQuality { get; set; }
    public bool MergedLocalAndStreaming { get; set; }
    public bool HasLocal { get; set; }
    public string? Badge { get; set; }
    public string? ArtUrl { get; set; }
}

public sealed class SnapshotAlbum
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? ArtistId { get; set; }
    public string? LinerNotes { get; set; }
    public string? Label { get; set; }
    public int? Year { get; set; }
    public string? Credits { get; set; }
    public string? ArtworkPath { get; set; }
}

public sealed class SnapshotArtist
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> RelatedArtistIds { get; set; } = [];
    public string? Bio { get; set; }
    public List<string> AlternateNames { get; set; } = [];
}

public sealed class SnapshotLyricLine
{
    public long TimeMs { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>곡 종료 약 40초 전 제시되는 스마트 오토플레이 후보.</summary>
public sealed class SnapshotAutoplay
{
    public List<SnapshotTrack> Candidates { get; set; } = [];
    public long DeadlineUnixMs { get; set; }
    public string? ChosenId { get; set; }
}
```

- [x] **Step 4: 통과를 확인한다**

```bash
dotnet test tests/Mono.Tests --filter SnapshotContractTests
dotnet test Mono.slnx
```

기대: 스냅샷 9개 통과, 전체 스위트도 통과(기존 44개 + 9개).

- [x] **Step 5: 커밋**

```bash
git add src/Mono.Protocol/RoomSnapshot.cs tests/Mono.Tests/SnapshotContractTests.cs
git commit -m "Type the nested track, album, artist and autoplay snapshot objects

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: `ApplyRoomState`를 DTO로 교체

`MainViewModel`의 `JsonNode` 손파싱을 걷어내고 `RoomSnapshot`을 쓴다. **화면 동작은 그대로다.**

**Files:**
- Modify: `src/Mono.Control/ViewModels/MainViewModel.cs` (`ApplyRoomState` 메서드 전체)

**Interfaces:**
- Consumes: `RoomSnapshot.Parse`.
- Produces: `MainViewModel.CurrentSnapshot` (타입 `RoomSnapshot?`) — 이후 페이지 VM들이 읽는 단일 상태원.

- [x] **Step 1: 현재 동작을 기록한다**

교체 전에 `ApplyRoomState`가 세팅하는 속성을 전부 적어 둔다. 빠뜨리면 조용히 화면이 죽는다.

```bash
sed -n '/private void ApplyRoomState/,/^    private void LoadCatalog/p' src/Mono.Control/ViewModels/MainViewModel.cs > /tmp/before-applyroomstate.txt
grep -oE '^\s+([A-Z][A-Za-z]+) =' /tmp/before-applyroomstate.txt | sort -u
```

이 목록이 Step 3 이후에도 전부 세팅돼야 한다.

이 메서드가 쓰는 기존 멤버는 다음이 전부다. **새로 만들지 말고 이것들을 그대로 호출한다:**

| 멤버 | 용도 |
| --- | --- |
| `_baseMedia` `_baseLocal` `_playingClock` | `TickClock()`이 200ms마다 시크바를 보간하는 기준점 |
| `UpdateTimeTexts(long media, long duration)` | `ElapsedText` / `RemainText` 갱신 |
| `RefreshNowArtAsync(string url)` | `ArtCache`로 `NowArt` 갱신 |
| `NowArtUrl` | `"http://127.0.0.1:7702" + artUrl` 형태의 절대 URL |
| `_suppressLinerScrollSend` | `LinerScrollY` 갱신이 Core로 되쏘이는 것을 막는 플래그 |
| `Safe(Func<Task>)` | 명령 실패를 `StatusText`로 흘리는 래퍼 |

- [x] **Step 2: `CurrentSnapshot` 속성을 추가한다**

`MainViewModel`의 `[ObservableProperty]` 블록 끝(`_artPerfText` 다음 줄)에 추가:

```csharp
    [ObservableProperty] private RoomSnapshot? _currentSnapshot;
```

파일 상단 `using`에 `using Mono.Protocol;`이 이미 있는지 확인하고, 없으면 추가한다.

- [x] **Step 3: `ApplyRoomState` 본문을 교체한다**

`private void ApplyRoomState(string? body)`의 몸통 전체를 아래로 바꾼다. Step 1에서 적어 둔 속성이 모두 남아 있는지 대조하면서 옮긴다.

```csharp
    private void ApplyRoomState(string? body)
    {
        var snap = RoomSnapshot.Parse(body);
        if (snap is null) return;
        CurrentSnapshot = snap;

        CurrentRoomId = snap.Id;
        RoomChip = $"{(string.IsNullOrEmpty(snap.Name) ? "룸" : snap.Name)} · {snap.Id[..Math.Min(6, snap.Id.Length)]}";
        IsPlaying = snap.Playing;
        PathBadge = snap.PathBadge ?? (snap.BitPerfect ? "Bit-perfect" : "Processed");
        NowBadge = PathBadge;

        SignalPathText = snap.DspEnabled
            ? $"Decode → DSP({snap.DspPreset}"
              + (string.IsNullOrWhiteSpace(snap.DeviceEqProfile) ? "" : "/" + snap.DeviceEqProfile)
              + (string.IsNullOrWhiteSpace(snap.ConvolutionIrPath) ? "" : "+IR")
              + $") → Output · {PathBadge}"
            : $"Decode → Bit-perfect → Output · {PathBadge}";

        if (!string.IsNullOrWhiteSpace(snap.ConvolutionIrPath)) IrPath = snap.ConvolutionIrPath!;

        var duration = Math.Max(1, snap.DurationMs);
        SeekMaximum = duration;
        _baseMedia = snap.MediaTimeMs;
        _baseLocal = Environment.TickCount64;
        _playingClock = IsPlaying;
        SeekValue = snap.MediaTimeMs;
        UpdateTimeTexts(snap.MediaTimeMs, duration);

        LinerNotes = snap.LinerNotes ?? "";
        CreditsText = snap.Credits ?? "";

        _suppressLinerScrollSend = true;
        FollowHostView = snap.FollowHostView;
        LinerScrollY = snap.LinerScrollY;
        _suppressLinerScrollSend = false;

        CurrentLyric = snap.CurrentLyric ?? "";
        LyricLines.Clear();
        foreach (var line in snap.Lyrics)
            if (!string.IsNullOrWhiteSpace(line.Text)) LyricLines.Add(line.Text);

        if (snap.CurrentTrack is { } ct)
        {
            NowTitle = string.IsNullOrWhiteSpace(ct.Title) ? "트랙" : ct.Title;
            NowArtist = string.Join(" · ", new[] { ct.ArtistName, ct.AlbumTitle }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            NowArtUrl = string.IsNullOrWhiteSpace(ct.ArtUrl) ? "" : "http://127.0.0.1:7702" + ct.ArtUrl;
            _ = RefreshNowArtAsync(NowArtUrl);
            if (!string.IsNullOrWhiteSpace(ct.ArtistId) && string.IsNullOrWhiteSpace(ArtistBio))
                _ = Safe(() => _session.WikiAsync(ct.ArtistId!));
        }

        ArtistBio = snap.Artist?.Bio ?? ArtistBio;

        QueueTracks.Clear();
        foreach (var q in snap.Queue)
        {
            QueueTracks.Add(new CatalogTrack
            {
                Id = q.TrackId,
                Title = q.Title,
                Artist = q.Artist,
                DurationMs = q.DurationMs,
                Badge = q.Badge,
                ArtUrl = q.ArtUrl
            });
        }

        // 기존 동작 유지: 통계가 있는 첫 멤버를 쓰고, 없으면 직전 값을 그대로 둔다.
        foreach (var m in snap.Members)
        {
            if (m.Stats is not { } st) continue;
            SyncText = $"sync {st.OffsetMs:+0.00;-0.00}ms · jitter {st.JitterMs:0.00}ms";
            break;
        }

        ChatLines.Clear();
        foreach (var c in snap.Chat) ChatLines.Add($"{c.PeerName ?? c.PeerId}: {c.Text}");

        AutoplayChoices.Clear();
        if (snap.Autoplay is { } ap && ap.Candidates.Count > 0)
        {
            foreach (var c in ap.Candidates)
            {
                AutoplayChoices.Add(new CatalogTrack
                {
                    Id = c.Id,
                    Title = c.Title,
                    Artist = c.ArtistName,
                    DurationMs = c.DurationMs,
                    Badge = c.Badge,
                    ArtUrl = c.ArtUrl
                });
            }
        }
        // 기존 코드는 candidates 배열이 비어 있어도 카드를 띄웠다. 빈 카드는 띄우지 않는다.
        ShowAutoplay = AutoplayChoices.Count > 0;
    }
```

Step 1 목록의 속성 중 위 코드에 없는 것이 있으면 **지금 되살린다.**

교체 후 `MainViewModel.cs`에서 `using System.Text.Json.Nodes;`가 더 이상 안 쓰이면 지운다.
`LoadCatalog` / `LoadRooms`는 계속 `JsonSerializer`를 쓰므로 `System.Text.Json`은 남는다.

- [x] **Step 4: 빌드하고 전체 테스트를 돌린다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet test Mono.slnx --nologo -v q
```

기대: 경고 0, 오류 0. 테스트 53개 통과.

- [x] **Step 5: 실제로 띄워서 회귀를 확인한다**

```bash
dotnet run --project src/Mono.Control
```

확인: 창이 뜬다 · Core에 연결된다 · 카탈로그가 보인다 · 라운지 생성 후 큐에 곡을 넣으면 하단 바에 제목/아트가 뜬다 · 재생 시 시크바가 움직인다. 확인 후 창을 닫는다.

- [x] **Step 6: 커밋**

```bash
git add src/Mono.Control/ViewModels/MainViewModel.cs
git commit -m "Parse room state through the typed snapshot instead of JsonNode

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: `CoreSession` 도메인 분할

기능 변화 없이 파일만 쪼갠다. 이후 태스크가 명령을 추가할 자리를 만든다.

**Files:**
- Modify: `src/Mono.Control/Services/CoreSession.cs` (연결·송수신만 남긴다)
- Create: `src/Mono.Control/Services/CoreSession.Transport.cs`
- Create: `src/Mono.Control/Services/CoreSession.Library.cs`
- Create: `src/Mono.Control/Services/CoreSession.Room.cs`
- Create: `src/Mono.Control/Services/CoreSession.Dsp.cs`

**Interfaces:**
- Consumes: 기존 `SendAsync(MonoMessage)`.
- Produces: `public sealed partial class CoreSession` — 이후 태스크는 새 partial 파일에 메서드를 추가한다. 기존 메서드 시그니처는 하나도 바뀌지 않는다.

- [x] **Step 1: 클래스를 `partial`로 바꾼다**

`CoreSession.cs`:

```csharp
public sealed partial class CoreSession : IAsyncDisposable
```

- [x] **Step 2: 메서드를 도메인별 파일로 옮긴다**

각 새 파일의 머리는 이 형태다:

```csharp
using Mono.Protocol;

namespace Mono.Control.Services;

public sealed partial class CoreSession
{
    // 여기에 옮긴 메서드
}
```

배치:
- `CoreSession.Transport.cs` ← `PlayAsync` `PauseAsync` `SkipAsync` `SeekAsync` `ResyncAsync` `SyncProbeAsync` `ChooseAutoplayAsync`
- `CoreSession.Library.cs` ← `CatalogAsync` `SearchAsync` `ScanAsync` `HistoryAsync` `PlaylistsAsync` `ArchivesAsync` `WikiAsync` `EndpointsAsync` `LinkStreamingAsync` `BeginStreamingOAuthAsync`
- `CoreSession.Room.cs` ← `ListRoomsAsync` `CreateRoomAsync` `JoinRoomAsync` `LeaveRoomAsync` `EnqueueAsync` `ClearQueueAsync` `ReactAsync` `ChatAsync` `EndSessionAsync` `LinerPageAsync` `LinerScrollAsync` `FollowHostAsync` `ListZonesAsync` `CreateZoneAsync`
- `CoreSession.Dsp.cs` ← `SetDspAsync` `SetEasyEqAsync` `SetConvolutionIrAsync` `SetSpeakerSetupAsync` `SetHeadroomAsync` `SetDeviceEqAsync`

`CoreSession.cs`에는 필드·이벤트·`ConnectAsync`·수신 루프·`SendAsync`·`DisconnectAsync`·`DisposeAsync`만 남긴다.

- [x] **Step 3: 빌드해서 아무것도 안 깨졌는지 본다**

```bash
dotnet build Mono.slnx -v q --nologo
```

기대: 경고 0, 오류 0. 순수 이동이므로 다른 변화가 있으면 안 된다.

- [x] **Step 4: 이동이 손실 없는지 확인한다**

```bash
git diff --stat
```

기대: `CoreSession.cs` 삭제 줄 수 ≈ 새 파일 4개의 추가 줄 수(각 파일 머리 5줄 제외). 크게 다르면 메서드를 빠뜨린 것이다.

- [x] **Step 5: 커밋**

```bash
git add src/Mono.Control/Services/
git commit -m "Split CoreSession into transport, library, room and DSP partials

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: 룸 관리자 · 큐 협업 명령

`04-UIUX기획서` §4.6과 `01-기획명세서` §관리자 기능이 요구하지만 GUI에서 도달 불가능한 명령을 배선한다.

**Files:**
- Create: `src/Mono.Control/Services/CoreSession.Admin.cs`
- Modify: `src/Mono.Control/Services/CoreSession.Room.cs`
- Create: `tests/Mono.Tests/CommandContractTests.cs`

**Interfaces:**
- Consumes: `SendAsync(MonoMessage)`, `Mono.Shared`의 `InviteAction` `MemberRole` `QualityPolicy` `PlaybackSourceMode`.
- Produces: 아래 메서드들. 2단계 이후 UI가 호출한다.
  - `InviteAsync(InviteAction action, int? minutes = null)`
  - `KickAsync(string targetPeerId)`
  - `TransferHostAsync(string targetPeerId)`
  - `SetRoleAsync(string targetPeerId, MemberRole role)`
  - `SpectateAsync(bool on)`
  - `SetPolicyAsync(QualityPolicy policy)`
  - `SetSourceModeAsync(PlaybackSourceMode mode)`
  - `SetRoomFlagAsync(string flag, bool? value = null, int? index = null, string? roomName = null)`
  - `RequestTrackAsync(string trackId)`
  - `ApproveRequestAsync(string requestId)`
  - `RejectRequestAsync(string requestId)`
  - `RemoveQueueAsync(int index)`
  - `MoveQueueAsync(int index, int delta)`
  - `JumpToAsync(int index)`

- [x] **Step 1: 실패하는 테스트를 쓴다**

명령이 **Core가 실제로 파싱하는 형태**로 나가는지 고정한다. `MessageTypes` 문자열과 `MonoMessage` 필드 이름이 어긋나면 런타임에 조용히 무시되므로, 이 테스트가 그걸 막는다.

`tests/Mono.Tests/CommandContractTests.cs`:

```csharp
using System.Text.Json;
using Mono.Protocol;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// Control이 보내는 명령이 Core가 읽는 필드에 실리는지 고정한다.
/// 타입이 맞아도 필드가 비면 Core는 조용히 무시한다 — 그걸 여기서 잡는다.
/// </summary>
public class CommandContractTests
{
    private static MonoMessage RoundTrip(MonoMessage msg)
    {
        var json = JsonSerializer.Serialize(msg, LineFraming.JsonOptions);
        return JsonSerializer.Deserialize<MonoMessage>(json, LineFraming.JsonOptions)!;
    }

    [Fact]
    public void InviteCarriesActionAndMinutes()
    {
        var m = RoundTrip(new MonoMessage
        {
            Type = MessageTypes.Invite,
            InviteAction = InviteAction.Extend,
            Minutes = 30
        });
        Assert.Equal("invite", m.Type);
        Assert.Equal(InviteAction.Extend, m.InviteAction);
        Assert.Equal(30, m.Minutes);
    }

    [Fact]
    public void SetRoleCarriesTargetAndRole()
    {
        var m = RoundTrip(new MonoMessage
        {
            Type = MessageTypes.SetRole,
            TargetPeerId = "guest-1",
            Member = MemberRole.Dj
        });
        Assert.Equal("set_role", m.Type);
        Assert.Equal("guest-1", m.TargetPeerId);
        Assert.Equal(MemberRole.Dj, m.Member);
    }

    [Fact]
    public void MoveQueueCarriesIndexAndDelta()
    {
        var m = RoundTrip(new MonoMessage { Type = MessageTypes.MoveQueue, Index = 3, Delta = -1 });
        Assert.Equal("move_queue", m.Type);
        Assert.Equal(3, m.Index);
        Assert.Equal(-1, m.Delta);
    }

    [Fact]
    public void SetRoomFlagCarriesNameAndValue()
    {
        var m = RoundTrip(new MonoMessage
        {
            Type = MessageTypes.SetRoomFlags,
            Text = "queue_lock",
            Flag = true
        });
        Assert.Equal("set_room_flags", m.Type);
        Assert.Equal("queue_lock", m.Text);
        Assert.True(m.Flag);
    }

    [Fact]
    public void SetPolicyAndSourceModeCarryEnums()
    {
        var p = RoundTrip(new MonoMessage
        {
            Type = MessageTypes.SetPolicy,
            Policy = QualityPolicy.LowestCommonFormat
        });
        Assert.Equal(QualityPolicy.LowestCommonFormat, p.Policy);

        var s = RoundTrip(new MonoMessage
        {
            Type = MessageTypes.SetSourceMode,
            SourceMode = PlaybackSourceMode.FanOut
        });
        Assert.Equal(PlaybackSourceMode.FanOut, s.SourceMode);
    }
}
```

- [x] **Step 2: 실패를 확인한다**

```bash
dotnet test tests/Mono.Tests --filter CommandContractTests
```

기대: 5개 통과 — 이 테스트는 프로토콜만 검증하므로 바로 통과한다. **통과하지 않으면 `MonoMessage`나 `MessageTypes`에 필드가 빠진 것이니 거기부터 고친다.**

- [x] **Step 3: 명령을 구현한다**

`src/Mono.Control/Services/CoreSession.Admin.cs`:

```csharp
using Mono.Protocol;
using Mono.Shared;

namespace Mono.Control.Services;

/// <summary>룸 운영 제어 — 호스트만 유효하다. 권한 판정은 Core가 하고 실패는 error로 돌아온다.</summary>
public sealed partial class CoreSession
{
    public Task InviteAsync(InviteAction action, int? minutes = null)
        => SendAsync(new MonoMessage { Type = MessageTypes.Invite, InviteAction = action, Minutes = minutes });

    public Task KickAsync(string targetPeerId)
        => SendAsync(new MonoMessage { Type = MessageTypes.Kick, TargetPeerId = targetPeerId });

    public Task TransferHostAsync(string targetPeerId)
        => SendAsync(new MonoMessage { Type = MessageTypes.TransferHost, TargetPeerId = targetPeerId });

    public Task SetRoleAsync(string targetPeerId, MemberRole role)
        => SendAsync(new MonoMessage { Type = MessageTypes.SetRole, TargetPeerId = targetPeerId, Member = role });

    public Task SpectateAsync(bool on)
        => SendAsync(new MonoMessage { Type = MessageTypes.Spectate, Flag = on });

    public Task SetPolicyAsync(QualityPolicy policy)
        => SendAsync(new MonoMessage { Type = MessageTypes.SetPolicy, Policy = policy });

    public Task SetSourceModeAsync(PlaybackSourceMode mode)
        => SendAsync(new MonoMessage { Type = MessageTypes.SetSourceMode, SourceMode = mode });

    /// <summary>
    /// 룸 플래그 토글. flag 이름은 Core CommandProcessor가 받는 값 그대로다:
    /// seek · comments · chat · queue_lock · follow_host · auto_advance ·
    /// smart_autoplay · cloud · dsp_lock · max · name
    /// </summary>
    public Task SetRoomFlagAsync(string flag, bool? value = null, int? index = null, string? roomName = null)
        => SendAsync(new MonoMessage
        {
            Type = MessageTypes.SetRoomFlags,
            Text = flag,
            Flag = value,
            Index = index,
            RoomName = roomName
        });
}
```

`src/Mono.Control/Services/CoreSession.Room.cs` 끝에 추가:

```csharp
    public Task RequestTrackAsync(string trackId)
        => SendAsync(new MonoMessage { Type = MessageTypes.RequestTrack, TrackId = trackId });

    public Task ApproveRequestAsync(string requestId)
        => SendAsync(new MonoMessage { Type = MessageTypes.ApproveRequest, Text = requestId });

    public Task RejectRequestAsync(string requestId)
        => SendAsync(new MonoMessage { Type = MessageTypes.RejectRequest, Text = requestId });

    public Task RemoveQueueAsync(int index)
        => SendAsync(new MonoMessage { Type = MessageTypes.RemoveQueue, Index = index });

    public Task MoveQueueAsync(int index, int delta)
        => SendAsync(new MonoMessage { Type = MessageTypes.MoveQueue, Index = index, Delta = delta });

    public Task JumpToAsync(int index)
        => SendAsync(new MonoMessage { Type = MessageTypes.JumpTo, Index = index });
```

- [x] **Step 4: Core가 이 필드를 읽는지 대조한다**

추측으로 두지 않는다. `ApproveRequest` `RejectRequest`가 요청 id를 `Text`에서 읽는지 확인한다:

```bash
grep -n 'ApproveRequest\|RejectRequest\|RemoveQueue\|MoveQueue\|JumpTo\|Spectate\|SetPolicy' -A 6 src/Mono.Core/CommandProcessor.cs
```

Core가 다른 필드(`TrackId`, `Index` 등)를 읽고 있으면 **위 코드를 Core에 맞춘다.** Core는 고치지 않는다.

- [x] **Step 5: 빌드하고 전체 테스트**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet test Mono.slnx --nologo -v q
```

기대: 경고 0. 테스트 58개 통과.

- [x] **Step 6: 커밋**

```bash
git add src/Mono.Control/Services/ tests/Mono.Tests/CommandContractTests.cs
git commit -m "Wire room admin and queue collaboration commands into CoreSession

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: 소셜 · 플레이리스트 · 아카이브 명령

**Files:**
- Create: `src/Mono.Control/Services/CoreSession.Social.cs`
- Create: `src/Mono.Control/Services/CoreSession.Playlist.cs`
- Modify: `tests/Mono.Tests/CommandContractTests.cs`

**Interfaces:**
- Consumes: `SendAsync(MonoMessage)`.
- Produces:
  - `PinAsync(long mediaTimeMs, string text)` · `RemovePinAsync(string pinId)` · `SeekPinAsync(string pinId)`
  - `ReactionHeatmapAsync(string trackId)` · `FollowArtistAsync(string artistId)` · `GraphAsync(string artistId)`
  - `CreatePlaylistAsync(string name)` · `LoadPlaylistAsync(string playlistId)` · `ExportM3uAsync(string playlistId)`
  - `ArchiveAsync()` · `ShareSessionAsync(string archiveId)`

- [x] **Step 1: 실패하는 테스트를 쓴다**

`CommandContractTests.cs`에 추가:

```csharp
    [Fact]
    public void PinCarriesMediaTimeAndText()
    {
        var m = RoundTrip(new MonoMessage
        {
            Type = MessageTypes.Pin,
            MediaTimeMs = 42_000,
            Text = "이 구간"
        });
        Assert.Equal("pin", m.Type);
        Assert.Equal(42_000, m.MediaTimeMs);
        Assert.Equal("이 구간", m.Text);
    }

    [Fact]
    public void PlaylistCommandsCarryPlaylistId()
    {
        var m = RoundTrip(new MonoMessage { Type = MessageTypes.LoadPlaylist, PlaylistId = "pl-1" });
        Assert.Equal("load_playlist", m.Type);
        Assert.Equal("pl-1", m.PlaylistId);
    }

    [Fact]
    public void ReactCarriesOnlyWhitelistedEmoji()
    {
        // Core가 화이트리스트를 강제하지만, 보내는 쪽도 같은 목록을 안다.
        foreach (var emoji in new[] { "❤️", "🎉", "👏", "🔥" })
        {
            var m = RoundTrip(new MonoMessage { Type = MessageTypes.React, Emoji = emoji });
            Assert.Equal(emoji, m.Emoji);
        }
    }
```

- [x] **Step 2: 실패를 확인한다**

```bash
dotnet test tests/Mono.Tests --filter CommandContractTests
```

기대: 8개 통과(프로토콜 검증이므로 즉시 통과). 실패하면 `MonoMessage`에 `PlaylistId`가 없는 것이니 거기부터 본다.

- [x] **Step 3: 명령을 구현한다**

`src/Mono.Control/Services/CoreSession.Social.cs`:

```csharp
using Mono.Protocol;

namespace Mono.Control.Services;

/// <summary>타임스탬프 핀 · 반응 · 그래프 — 전부 컨트롤 채널이다. 오디오 경로와 무관하다.</summary>
public sealed partial class CoreSession
{
    public Task PinAsync(long mediaTimeMs, string text)
        => SendAsync(new MonoMessage { Type = MessageTypes.Pin, MediaTimeMs = mediaTimeMs, Text = text });

    public Task RemovePinAsync(string pinId)
        => SendAsync(new MonoMessage { Type = MessageTypes.RemovePin, Text = pinId });

    public Task SeekPinAsync(string pinId)
        => SendAsync(new MonoMessage { Type = MessageTypes.SeekPin, Text = pinId });

    public Task ReactionHeatmapAsync(string trackId)
        => SendAsync(new MonoMessage { Type = MessageTypes.ReactionHeatmap, TrackId = trackId });

    public Task FollowArtistAsync(string artistId)
        => SendAsync(new MonoMessage { Type = MessageTypes.FollowArtist, Text = artistId });

    public Task GraphAsync(string artistId)
        => SendAsync(new MonoMessage { Type = MessageTypes.Graph, Text = artistId });
}
```

`src/Mono.Control/Services/CoreSession.Playlist.cs`:

```csharp
using Mono.Protocol;

namespace Mono.Control.Services;

/// <summary>플레이리스트 · 세션 아카이브. 공유는 트랙 식별자·메타만 오간다(파일 재배포 아님).</summary>
public sealed partial class CoreSession
{
    public Task CreatePlaylistAsync(string name)
        => SendAsync(new MonoMessage { Type = MessageTypes.CreatePlaylist, Text = name });

    public Task LoadPlaylistAsync(string playlistId)
        => SendAsync(new MonoMessage { Type = MessageTypes.LoadPlaylist, PlaylistId = playlistId });

    public Task ExportM3uAsync(string playlistId)
        => SendAsync(new MonoMessage { Type = MessageTypes.ExportM3u, PlaylistId = playlistId });

    public Task ArchiveAsync()
        => SendAsync(new MonoMessage { Type = MessageTypes.Archive });

    public Task ShareSessionAsync(string archiveId)
        => SendAsync(new MonoMessage { Type = MessageTypes.ShareSession, Text = archiveId });
}
```

- [x] **Step 4: Core가 읽는 필드와 대조한다**

```bash
grep -n 'Pin\b\|RemovePin\|SeekPin\|CreatePlaylist\|LoadPlaylist\|ExportM3u\|ShareSession\|Graph\|FollowArtist' -A 6 src/Mono.Core/CommandProcessor.cs
```

Core가 다른 필드에서 읽으면 Control 쪽을 맞춘다.

- [x] **Step 5: 빌드하고 전체 테스트**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet test Mono.slnx --nologo -v q
```

기대: 경고 0. 테스트 61개 통과.

- [x] **Step 6: 커밋**

```bash
git add src/Mono.Control/Services/ tests/Mono.Tests/CommandContractTests.cs
git commit -m "Wire pin, reaction, graph, playlist and archive commands

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: 존 · 볼륨 · 페어링 명령

**Files:**
- Create: `src/Mono.Control/Services/CoreSession.Zone.cs`
- Modify: `src/Mono.Control/Services/CoreSession.Transport.cs`
- Modify: `tests/Mono.Tests/CommandContractTests.cs`

**Interfaces:**
- Consumes: `SendAsync(MonoMessage)`, `Mono.Shared.ZoneMode`.
- Produces:
  - `RenameZoneAsync(string zoneId, string name)` · `SetZoneModeAsync(string zoneId, ZoneMode mode)`
  - `ZoneAddMemberAsync(string zoneId, string peerId)` · `ZoneRemoveMemberAsync(string zoneId, string peerId)`
  - `DeleteZoneAsync(string zoneId)`
  - `SetVolumeAsync(string targetPeerId, int percent)` — percent는 0–100으로 클램프된다
  - `PairAsync()` · `RedeemAsync(string pairingCode)`

- [x] **Step 1: 실패하는 테스트를 쓴다**

`CommandContractTests.cs`에 추가:

```csharp
    [Fact]
    public void SetVolumeCarriesTargetAndPercent()
    {
        var m = RoundTrip(new MonoMessage
        {
            Type = MessageTypes.SetVolume,
            TargetPeerId = "out-1",
            Volume = 70
        });
        Assert.Equal("set_volume", m.Type);
        Assert.Equal("out-1", m.TargetPeerId);
        Assert.Equal(70, m.Volume);
    }

    [Fact]
    public void ZoneModeCarriesEnum()
    {
        var m = RoundTrip(new MonoMessage
        {
            Type = MessageTypes.SetZoneMode,
            Text = "zone-1",
            Index = (int)ZoneMode.Independent
        });
        Assert.Equal("set_zone_mode", m.Type);
        Assert.Equal("zone-1", m.Text);
        Assert.Equal((int)ZoneMode.Independent, m.Index);
    }

    [Fact]
    public void RedeemCarriesPairingCode()
    {
        var m = RoundTrip(new MonoMessage { Type = MessageTypes.Redeem, PairingCode = "482913" });
        Assert.Equal("redeem", m.Type);
        Assert.Equal("482913", m.PairingCode);
    }
```

- [x] **Step 2: 실패를 확인한다**

```bash
dotnet test tests/Mono.Tests --filter CommandContractTests
```

기대: 11개 통과.

- [x] **Step 3: Core가 존 명령을 어떻게 읽는지 먼저 확인한다**

존 명령은 필드 관례가 갈리기 쉽다. **구현 전에** 본다:

```bash
grep -n 'RenameZone\|SetZoneMode\|ZoneAddMember\|ZoneRemoveMember\|DeleteZone\|SetVolume\|Redeem\|MessageTypes.Pair' -A 8 src/Mono.Core/CommandProcessor.cs
```

Core가 zoneId를 `Text`에서 읽는지 `RoomId`에서 읽는지, 모드를 `Index`에서 읽는지 확인하고 Step 4를 거기에 맞춘다.

- [x] **Step 4: 명령을 구현한다**

아래는 zoneId=`Text`, peerId=`TargetPeerId`, 모드=`Index` 관례를 가정한다. **Step 3 결과가 다르면 그쪽에 맞춘다.**

`src/Mono.Control/Services/CoreSession.Zone.cs`:

```csharp
using Mono.Protocol;
using Mono.Shared;

namespace Mono.Control.Services;

/// <summary>멀티 디바이스 존 — 싱글 플레이 전용. 실제 방 재배정은 Core가 한다.</summary>
public sealed partial class CoreSession
{
    public Task RenameZoneAsync(string zoneId, string name)
        => SendAsync(new MonoMessage { Type = MessageTypes.RenameZone, Text = zoneId, RoomName = name });

    public Task SetZoneModeAsync(string zoneId, ZoneMode mode)
        => SendAsync(new MonoMessage { Type = MessageTypes.SetZoneMode, Text = zoneId, Index = (int)mode });

    public Task ZoneAddMemberAsync(string zoneId, string peerId)
        => SendAsync(new MonoMessage { Type = MessageTypes.ZoneAddMember, Text = zoneId, TargetPeerId = peerId });

    public Task ZoneRemoveMemberAsync(string zoneId, string peerId)
        => SendAsync(new MonoMessage { Type = MessageTypes.ZoneRemoveMember, Text = zoneId, TargetPeerId = peerId });

    public Task DeleteZoneAsync(string zoneId)
        => SendAsync(new MonoMessage { Type = MessageTypes.DeleteZone, Text = zoneId });
}
```

`src/Mono.Control/Services/CoreSession.Transport.cs` 끝에 추가:

```csharp
    /// <summary>출력 기기 볼륨. 하드웨어 볼륨이 있으면 Core가 그쪽을 쓴다.
    /// Bit-perfect 정책이 디지털 감쇠를 막으면 error로 돌아온다.</summary>
    public Task SetVolumeAsync(string targetPeerId, int percent)
        => SendAsync(new MonoMessage
        {
            Type = MessageTypes.SetVolume,
            TargetPeerId = targetPeerId,
            Volume = Math.Clamp(percent, 0, 100)
        });

    public Task PairAsync()
        => SendAsync(new MonoMessage { Type = MessageTypes.Pair });

    public Task RedeemAsync(string pairingCode)
        => SendAsync(new MonoMessage { Type = MessageTypes.Redeem, PairingCode = pairingCode });
```

- [x] **Step 5: 도달 불가 명령이 남았는지 기계적으로 확인한다**

```bash
grep -o 'MessageTypes\.[A-Za-z]*' src/Mono.Control/Services/CoreSession*.cs | sed 's/.*MessageTypes\.//' | sort -u > /tmp/ctrl.txt
grep -o 'string [A-Za-z]* =' src/Mono.Protocol/MessageTypes.cs | sed 's/string //;s/ =//' | sort -u > /tmp/proto.txt
comm -13 /tmp/ctrl.txt /tmp/proto.txt
```

남아야 하는 것은 수신·전송 전용뿐이다:
`AutoplayCandidates` `Caps` `ClockPing` `ClockPong` `ClockReport` `Error` `MatpAudio` `OutputHello`
`PairingIssued` `RoomState` `Snapshot` `Timeline` `Volume` `Welcome`

이 목록에 없는 게 남아 있으면 배선을 빠뜨린 것이다.

- [x] **Step 6: 빌드하고 전체 테스트**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet test Mono.slnx --nologo -v q
```

기대: 경고 0. 테스트 64개 통과.

- [x] **Step 7: 커밋**

```bash
git add src/Mono.Control/Services/ tests/Mono.Tests/CommandContractTests.cs
git commit -m "Wire zone, volume and pairing commands; close the command gap

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 9: 페이지 뷰 분해

675줄 `MainWindow.axaml`에서 화면을 `UserControl`로 꺼낸다. **마크업은 그대로 옮긴다 — 다시 쓰지 않는다.**

**Files:**
- Create: `src/Mono.Control/Views/Pages/HomePage.axaml` (+ `.axaml.cs`)
- Create: `src/Mono.Control/Views/Pages/GenresPage.axaml` (+ `.axaml.cs`)
- Create: `src/Mono.Control/Views/Pages/LibraryPage.axaml` (+ `.axaml.cs`)
- Create: `src/Mono.Control/Views/Pages/LoungePage.axaml` (+ `.axaml.cs`)
- Create: `src/Mono.Control/Views/Pages/AudioPage.axaml` (+ `.axaml.cs`)
- Create: `src/Mono.Control/Views/Pages/SettingsPage.axaml` (+ `.axaml.cs`)
- Modify: `src/Mono.Control/Views/MainWindow.axaml`

**Interfaces:**
- Consumes: 각 페이지의 `DataContext`는 `MainViewModel` 그대로다. 1단계에서는 VM을 쪼개지 않는다 — 뷰만 옮긴다.
- Produces: `Mono.Control.Views.Pages` 네임스페이스의 `UserControl` 6개. 10번 태스크가 여기에 VM을 주입한다.

- [x] **Step 1: 페이지 하나를 먼저 옮긴다 (SettingsPage)**

가장 작은 화면부터 한다. `MainWindow.axaml`의 `<!-- Settings -->` 주석부터 대응하는 `</ScrollViewer>`까지를 잘라낸다.

`src/Mono.Control/Views/Pages/SettingsPage.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Mono.Control.ViewModels"
             x:Class="Mono.Control.Views.Pages.SettingsPage"
             x:DataType="vm:MainViewModel">
  <!-- MainWindow.axaml에서 잘라낸 Settings ScrollViewer의 내용을 여기에 그대로 붙인다.
       바깥 ScrollViewer의 IsVisible="{Binding IsSettingsPage}" 는 제거한다 —
       표시 여부는 MainWindow가 이 UserControl에 걸어 준다. -->
</UserControl>
```

`src/Mono.Control/Views/Pages/SettingsPage.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace Mono.Control.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();
}
```

`MainWindow.axaml`의 원래 자리에:

```xml
<pages:SettingsPage IsVisible="{Binding IsSettingsPage}"/>
```

`MainWindow.axaml` 여는 태그에 네임스페이스를 추가한다:

```xml
xmlns:pages="using:Mono.Control.Views.Pages"
```

- [x] **Step 2: 빌드하고 실제로 띄워 본다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: Settings 화면이 이전과 똑같이 보이고, 표시 이름·라이브러리 경로·업데이트 버튼이 동작한다.

`x:DataType` 없이 옮기면 컴파일 바인딩이 깨진다. 빌드 경고가 하나라도 뜨면 여기서 잡는다.

- [x] **Step 3: 나머지 5개를 같은 방식으로 옮긴다**

한 번에 하나씩, 각각 빌드해서 확인한다. `MainWindow.axaml`에서 잘라낼 구간:

| 페이지 | 잘라낼 구간 | MainWindow에 남길 것 |
| --- | --- | --- |
| `HomePage` | `<!-- Home rails -->` ScrollViewer | `<pages:HomePage IsVisible="{Binding IsHomePage}"/>` |
| `GenresPage` | `<!-- Genres tiles -->` ScrollViewer | `<pages:GenresPage IsVisible="{Binding IsGenresPage}"/>` |
| `LibraryPage` | `<!-- Library grid -->` ScrollViewer | `<pages:LibraryPage IsVisible="{Binding IsLibraryGrid}"/>` |
| `LoungePage` | `<!-- Lounge -->` Grid | `<pages:LoungePage IsVisible="{Binding IsLoungePage}"/>` |
| `AudioPage` | `<!-- Devices / Easy EQ / Signal path -->` ScrollViewer | `<pages:AudioPage IsVisible="{Binding IsDevicesPage}"/>` |

`MainWindow.axaml`에는 셸(사이드바·헤더·하단 플레이어 바·몰입 Now Playing·온보딩)만 남는다.

`$parent[Window]` 바인딩이 있는 마크업은 `UserControl` 안에서 깨진다. 그런 바인딩은
`$parent[UserControl].((vm:MainViewModel)DataContext)`로 바꾼다 — `DataContext`는 어차피 같은 `MainViewModel`이다.

- [x] **Step 4: 전체를 띄워 모든 화면을 눌러 본다**

```bash
dotnet run --project src/Mono.Control
```

확인: 사이드바 12개 항목을 전부 눌러 각 화면이 이전과 같이 뜬다 · 라운지에서 룸 생성·채팅이 된다 · Audio에서 EQ 슬라이더가 움직인다 · 하단 바가 그대로다.

- [x] **Step 5: 줄 수를 확인한다**

```bash
wc -l src/Mono.Control/Views/MainWindow.axaml src/Mono.Control/Views/Pages/*.axaml
```

기대: `MainWindow.axaml`이 675줄에서 250줄 안팎으로 준다. 페이지 합계가 원래와 비슷해야 한다.

- [x] **Step 6: 커밋**

```bash
git add src/Mono.Control/Views/
git commit -m "Extract six page UserControls out of MainWindow

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 10: 셸 / 페이지 뷰모델 분리

`MainViewModel` 969줄에서 화면별 상태를 페이지 VM으로 옮긴다.

**Files:**
- Create: `src/Mono.Control/ViewModels/Pages/LoungeViewModel.cs`
- Create: `src/Mono.Control/ViewModels/Pages/AudioViewModel.cs`
- Create: `src/Mono.Control/ViewModels/Pages/SettingsViewModel.cs`
- Modify: `src/Mono.Control/ViewModels/MainViewModel.cs`
- Modify: `src/Mono.Control/Views/Pages/LoungePage.axaml`, `AudioPage.axaml`, `SettingsPage.axaml`

**Interfaces:**
- Consumes: `CoreSession`, `MainViewModel.CurrentSnapshot`.
- Produces: `MainViewModel.Lounge` (`LoungeViewModel`), `.Audio` (`AudioViewModel`), `.Settings` (`SettingsViewModel`). 각 페이지 VM 생성자는 `(CoreSession session)`을 받는다. 셸이 스냅샷을 받으면 `ApplySnapshot(RoomSnapshot snap)`으로 밀어 넣는다.

- [x] **Step 1: 페이지 VM 기반을 만든다**

`src/Mono.Control/ViewModels/Pages/PageViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Mono.Control.Services;
using Mono.Protocol;

namespace Mono.Control.ViewModels.Pages;

/// <summary>
/// 화면 하나의 상태와 명령. 셸이 스냅샷을 밀어 넣고, 페이지는 CoreSession에만 의존한다.
/// 페이지끼리는 서로 참조하지 않는다.
/// </summary>
public abstract partial class PageViewModel : ObservableObject
{
    protected readonly CoreSession Session;

    protected PageViewModel(CoreSession session) => Session = session;

    /// <summary>셸이 룸 스냅샷을 받을 때마다 호출한다.</summary>
    public virtual void ApplySnapshot(RoomSnapshot snapshot) { }

    /// <summary>명령 실패가 UI를 죽이지 않게 감싼다. 사유는 Core가 error로 돌려준다.</summary>
    protected async Task Safe(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    [ObservableProperty] private string _statusText = "";
}
```

- [x] **Step 2: SettingsViewModel부터 옮긴다**

`MainViewModel`에서 설정 전용 속성(`DisplayName` `LibraryPath` `DarkTheme` `CloseToTray` `AppVersion` `UpdateStatus` `UpdateBusy` `UpdateReady` `UpdateProgress`)과 관련 커맨드(`ScanLibraryAsync` `CheckForUpdatesAsync` 등)를 `SettingsViewModel`로 옮긴다.

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mono.Control.Services;

namespace Mono.Control.ViewModels.Pages;

public sealed partial class SettingsViewModel : PageViewModel
{
    public SettingsViewModel(CoreSession session) : base(session)
    {
        LibraryPath = Prefs.Get("library_path", "");
        DisplayName = Prefs.Get("name", Environment.UserName);
        CloseToTray = Prefs.GetBool("close_to_tray");
    }

    [ObservableProperty] private string _displayName = Environment.UserName;
    [ObservableProperty] private string _libraryPath = "";
    [ObservableProperty] private bool _closeToTray;

    partial void OnLibraryPathChanged(string value) => Prefs.Set("library_path", value ?? "");
    partial void OnCloseToTrayChanged(bool value) => Prefs.SetBool("close_to_tray", value);

    [RelayCommand]
    private Task ScanLibraryAsync() => Safe(() =>
        Session.ScanAsync(string.IsNullOrWhiteSpace(LibraryPath) ? null : LibraryPath));
}
```

`Prefs`는 현재 `MainViewModel.cs` 파일 맨 아래(929줄~)에 있는 `internal static class`다.
페이지 VM에서 쓰려면 별도 파일로 꺼낸다 — **내용은 한 줄도 바꾸지 않는다:**

1. `MainViewModel.cs`에서 `internal static class Prefs { ... }` 블록 전체를 잘라낸다.
2. `src/Mono.Control/ViewModels/Prefs.cs`를 만들고 이렇게 감싼다:

```csharp
namespace Mono.Control.ViewModels;

// (잘라낸 Prefs 클래스 본문을 그대로 붙인다)
```

네임스페이스를 `Mono.Control.ViewModels`로 유지하면 `MainViewModel`은 `using` 없이 계속 쓴다.
`Mono.Control.ViewModels.Pages`의 페이지 VM에는 `using Mono.Control.ViewModels;`를 추가한다.

테마 토글(`DarkTheme` `ApplyTheme` `MonoIcons.ClearCache`)은 앱 전역이므로 **셸에 남긴다** — 페이지로 옮기지 않는다.

- [x] **Step 3: `MainViewModel`에 페이지 VM을 붙인다**

생성자에서:

```csharp
        Lounge = new LoungeViewModel(session);
        Audio = new AudioViewModel(session);
        Settings = new SettingsViewModel(session);
```

속성:

```csharp
    public LoungeViewModel Lounge { get; }
    public AudioViewModel Audio { get; }
    public SettingsViewModel Settings { get; }
```

`ApplyRoomState` 끝(Task 4에서 만든 메서드)에 추가:

```csharp
        Lounge.ApplySnapshot(snap);
        Audio.ApplySnapshot(snap);
```

- [x] **Step 4: 페이지 XAML의 `DataContext`를 바꾼다**

`SettingsPage.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:pvm="using:Mono.Control.ViewModels.Pages"
             x:Class="Mono.Control.Views.Pages.SettingsPage"
             x:DataType="pvm:SettingsViewModel">
```

`MainWindow.axaml`에서는 이렇게 쓴다:

```xml
<pages:SettingsPage DataContext="{Binding Settings}"
                    IsVisible="{Binding $parent[Window].((vm:MainViewModel)DataContext).IsSettingsPage}"/>
```

`DataContext`를 페이지 VM으로 바꾸는 순간 `IsVisible="{Binding IsSettingsPage}"` 같은 짧은 형태는
`SettingsViewModel`에서 속성을 찾다 실패한다. `IsSettingsPage`는 셸 VM의 속성이므로
반드시 위처럼 `$parent[Window]`를 거쳐야 한다. 여섯 페이지 모두 같다.

- [x] **Step 5: Lounge·Audio도 같은 방식으로 옮긴다**

- `LoungeViewModel` ← `RoomName` `RoomMode` `JoinRoomId` `InviteCode` `ChatInput` `Rooms` `ChatLines` `QueueTracks` `CurrentRoomId` `FollowHostView` `LinerScrollY` `LinerNotes` `CreditsText` + 관련 커맨드
- `AudioViewModel` ← `EqGraphicMode` `EqBand1`–`EqBand5` `IrPath` `SpeakerDelayL/R` `SpeakerGainL/R` `HeadroomDb` `DeviceEqProfile` `SyncProbeText` `ZoneName` + 관련 커맨드

하단 바가 쓰는 것(`NowTitle` `NowArtist` `NowArt` `IsPlaying` `SeekValue` `SyncText` `SignalPathText` `RoomChip` `AutoplayChoices` `ShowAutoplay`)은 **셸에 남긴다.** 하단 바는 페이지가 아니다.

- [x] **Step 6: 빌드하고 전 화면을 눌러 본다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet test Mono.slnx --nologo -v q
dotnet run --project src/Mono.Control
```

확인: 사이드바 12개 항목 전부 · 라운지 생성/참여/채팅 · Audio EQ·IR·Sync Probe · Settings 스캔/업데이트 · 온보딩(헤더 「가이드」) · 하단 바 재생 · 몰입 Now Playing.

- [x] **Step 7: 줄 수를 확인한다**

```bash
wc -l src/Mono.Control/ViewModels/MainViewModel.cs src/Mono.Control/ViewModels/Pages/*.cs
```

기대: `MainViewModel.cs`가 969줄에서 500줄 안팎으로 준다.

- [x] **Step 8: 커밋**

```bash
git add src/Mono.Control/ViewModels/ src/Mono.Control/Views/ src/Mono.Control/Services/
git commit -m "Split lounge, audio and settings state into page view models

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## 1단계 완료 기준

- [x] `dotnet build Mono.slnx` 경고 0, 오류 0
- [x] `dotnet test Mono.slnx` **65개** 통과 (기존 44 + 스냅샷 9 + 명령 12)
- [x] Task 8 Step 5의 `comm` 결과가 수신·전송 전용 **15개**만 남는다 (`Archive`가 인바운드 명령이 아니라 `end_session` 응답으로 판명되어 하나 늘었다)
- [x] `MainWindow.axaml` **306줄**(675에서), `MainViewModel.cs` **552줄**(969에서) — 목표를 각각 6줄·2줄 넘겼다. 더 줄이려면 실재하지 않는 경계를 만들어야 해서 멈췄다(Task 10 커밋 메시지 참조)
- [x] 앱을 띄워 Core 연결·창 생성·6개 페이지 로드 확인. 컴파일된 바인딩이라 바인딩 오류는 빌드에서 잡힌다
- [x] 콘솔 창 0개(세 exe 모두 `WinExe`), 브라우저 자동 실행 없음

**1단계에서 하지 않은 것:** 명령에 **발신자**가 생겼을 뿐, 대부분은 아직 **화면에 버튼이 없다.**
사용자가 실제로 누를 수 있게 만드는 것은 2–5단계다.

---

## 다음 단계

2–5단계는 각각 별도 계획으로 쓴다. 1단계가 만든 `RoomSnapshot`·명령 배선·페이지 구조 위에서만 의미가 있으므로, **1단계를 끝내고 실제 구조를 본 뒤에** 작성한다.

| 단계 | 내용 | 선행 |
| --- | --- | --- |
| 2 | 하단 바 완성 — 볼륨·존/장치·♥·큐 드로어·시크바 히트맵/핀·시그널 패스 팝오버 | Task 2, 8 |
| 3 | 라이브러리 — Core에 장르·작곡가·작품·복수 스캔 루트 추가 후 화면 5종 | Task 1–3 |
| 4 | 라운지 — 관리자 패널·큐 협업·핀·반응·관계도·아카이브 | Task 6, 7, 10 |
| 5 | Now Playing 5모드 · 설정 10트리 · 단축키 · 존 관리 · 페어링 · 백업 | 전부 |
