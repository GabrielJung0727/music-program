# Control 패리티 4단계 — 라운지 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mono만의 가치인 라운지를 완성한다 — 룸 관리자 패널, 큐 협업(Request→승인), 타임스탬프 핀, 반응 4종, 뮤지션 관계도, 세션 종료→아카이브.

**Architecture:** 명령은 1단계에서 전부 배선했고 스냅샷은 `members` · `requests` · `pins` · `reactions` · `inviteCode` · 정책 플래그를 이미 싣고 온다. 새 Core 작업은 없다. `LoungeViewModel`이 스냅샷에서 이 값들을 꺼내 쓰고, **호스트가 아닐 때 버튼을 숨기지 않고 비활성 + 사유 툴팁**으로 노출한다(문서 §6 신뢰 UI). 라운지 화면이 커지므로 관리자 패널은 별도 `UserControl`로 뗀다.

**Tech Stack:** C# / .NET 8 · Avalonia 11 · CommunityToolkit.Mvvm · xunit

**Spec:** [`docs/superpowers/specs/2026-09-05-control-full-parity-design.md`](../specs/2026-09-05-control-full-parity-design.md) §4 단계 4

**선행:** [3b단계](2026-09-05-control-parity-phase3b-library-screens.md) 완료 (커밋 `91b5629`)

## Global Constraints

- 대상 프레임워크 `net8.0`. 새 NuGet 패키지를 추가하지 않는다.
- JSON은 `LineFraming.JsonOptions` — CamelCase, **null 필드는 실리지 않는다**.
- 의존 방향: `Control` → `Protocol` → `Shared`. Control은 Core를 참조하지 않는다.
- `dotnet build Mono.slnx` 경고 0 유지. 기존 96개 테스트는 회귀 가드다.
- **정책상 막힌 동작은 버튼을 숨기지 않는다.** 비활성 + 사유 툴팁.
- 사용자 대상 문자열은 한국어.
- 커밋 메시지 말미에 `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## 확인된 Core 동작 (추측 금지)

| 명령 | Core 판정 |
| --- | --- |
| `set_room_flags` `set_policy` `set_source_mode` | `ApplyHostSettings` — 호스트가 아니면 `"only host"`. Audiophile 룸이면 적용 후 DSP를 강제로 끄고 잠근다 |
| `invite` | 호스트만. `Rotate`=6자리 코드 새로 발급(기본 360분) · `Extend`=만료 연장(기본 60분) · `Revoke`=코드 폐기 |
| `kick` `transfer_host` `set_role` | `TargetPeerId` 필요 |
| `request_track` | 아무나 가능. 모르는 트랙·미구독 소스면 거절 |
| `approve_request` `reject_request` | 요청 id를 `Text` 로 받는다 |
| `move_queue` | `Index` + `Delta` |
| `pin` | `MediaTimeMs` + `Text`. `remove_pin`·`seek_pin` 은 핀 id를 `Text` 로 |
| `react` | `❤️` `🎉` `👏` `🔥` 만. 곡당 유저당 3회 |
| `graph` | `Text` 에 artistId. 응답 `{ artist, albums, tracks, related, neighbours }` |
| `end_session` | `Consent` — true면 아카이브 저장 후 `archive` 응답에 `archiveId`·`playlistId` |

## 스냅샷에서 쓰는 필드 (1단계에서 타입화 완료)

`HostPeerId` · `Members[]{PeerId,Name,Role,IsOutput,Spectator,Stats}` ·
`Requests[]{Id,TrackId,FromPeerId,FromName,Title}` · `Pins[]{Id,PeerId,PeerName,MediaTimeMs,Text,OnCurrentTrack}` ·
`ReactionCounts{emoji:count}` · `AllowedReactionEmoji[]` · `InviteCode` · `InviteExpiresAt` ·
`SeekingAllowed` `CommentsAllowed` `ChatCollapsed` `QueueLocked` `AutoAdvance` `SmartAutoplay` `DspLocked` `MaxMembers` ·
`QualityPolicy` `SourceMode` `Mode`

---

### Task 1: 라운지가 스냅샷에서 권한과 멤버를 읽는다

관리자 UI의 근거가 되는 상태를 먼저 만든다. 화면은 Task 2부터.

**Files:**
- Modify: `src/Mono.Control/ViewModels/Pages/LoungeViewModel.cs`
- Modify: `src/Mono.Control/ViewModels/MainViewModel.cs` (`Lounge`에 자기 peerId 알려주기)
- Test: `tests/Mono.Tests/LoungeSnapshotTests.cs`

**Interfaces:**
- Consumes: `RoomSnapshot` (1단계).
- Produces:
  - `LoungeViewModel.SelfPeerId` (`string`) — 셸이 생성 시 넣어 준다
  - `.IsHost` (`bool`) · `.HostBlockedReason` (`string`) — 호스트가 아니면 `"호스트만 바꿀 수 있습니다"`
  - `.Members` (`ObservableCollection<MemberEntry>`) · `.Requests` (`ObservableCollection<RequestEntry>`) ·
    `.Pins` (`ObservableCollection<PinEntry>`)
  - `.InviteCodeText` · `.InviteExpiryText`
  - 정책 미러: `.SeekingAllowed` `.CommentsAllowed` `.QueueLocked` `.AutoAdvance` `.SmartAutoplay` `.DspLocked` `.MaxMembers` `.QualityPolicyIndex` `.SourceModeIndex`
  - `MemberEntry(string PeerId, string Name, MemberRole Role, bool IsOutput, bool Spectator, string SyncText)` ·
    `RequestEntry(string Id, string Title, string FromName)` ·
    `PinEntry(string Id, string Text, string PeerName, long MediaTimeMs, bool OnCurrentTrack)` — `Mono.Control.Models`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

Control은 테스트에서 참조하지 않으므로, **관리자 UI가 기대는 값이 스냅샷에 실제로 실리는지**를 프로토콜 수준에서 고정한다.

`tests/Mono.Tests/LoungeSnapshotTests.cs`:

```csharp
using Mono.Core;
using Mono.Protocol;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 라운지 관리자 패널이 읽는 값이 스냅샷에 실리는지 고정한다.
/// Core 가 이름이나 형태를 바꾸면 패널이 조용히 비활성으로 굳는다.
/// </summary>
public class LoungeSnapshotTests
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
    public void HostIsIdentifiableFromTheSnapshot()
    {
        var (rooms, _) = NewStack();
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "Host");

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room))!;

        Assert.Equal("host", snap.HostPeerId);
        Assert.Contains(snap.Members, m => m.PeerId == "host" && m.Role == MemberRole.Host);
    }

    [Fact]
    public void InviteRotationSurfacesCodeAndExpiry()
    {
        var (rooms, _) = NewStack();
        var room = rooms.Create("host", "Lounge", RoomMode.Invite, "Host");
        rooms.ManageInvite(room.Id, "host", InviteAction.Rotate, 30);

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room))!;

        Assert.False(string.IsNullOrEmpty(snap.InviteCode));
        Assert.NotNull(snap.InviteExpiresAt);
    }

    [Fact]
    public void GuestRequestsAppearForTheHost()
    {
        var (rooms, _) = NewStack();
        var room = rooms.Create("host", "DJ", RoomMode.HostQueue, "Host");
        rooms.Request(room.Id, "guest", "tr-blue-train");

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room))!;

        var req = Assert.Single(snap.Requests);
        Assert.Equal("tr-blue-train", req.TrackId);
        Assert.Equal("guest", req.FromPeerId);
        Assert.False(string.IsNullOrEmpty(req.Title));
    }

    [Fact]
    public void PolicyFlagsRoundTripForThePanel()
    {
        var (rooms, _) = NewStack();
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "Host");
        rooms.ApplyHostSettings(room.Id, "host", r =>
        {
            r.QueueLocked = true;
            r.SeekingAllowed = false;
            r.MaxMembers = 8;
            r.QualityPolicy = QualityPolicy.LowestCommonFormat;
            r.SourceMode = PlaybackSourceMode.FanOut;
        });

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room))!;

        Assert.True(snap.QueueLocked);
        Assert.False(snap.SeekingAllowed);
        Assert.Equal(8, snap.MaxMembers);
        Assert.Equal(QualityPolicy.LowestCommonFormat, snap.QualityPolicy);
        Assert.Equal(PlaybackSourceMode.FanOut, snap.SourceMode);
    }

    [Fact]
    public void NonHostSettingsAreRefused()
    {
        // Control 이 비활성으로 막기 전에 Core 가 최종 방어선이다.
        var (rooms, _) = NewStack();
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "Host");

        var result = rooms.ApplyHostSettings(room.Id, "guest", r => r.QueueLocked = true);

        Assert.Equal("only host", result.Error);
        Assert.False(rooms.Get(room.Id)!.QueueLocked);
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter LoungeSnapshotTests
```

기대: 5개 통과 — 1단계가 이미 타입화했으므로 바로 통과한다.
**통과하지 않으면 스냅샷 계약이 어긋난 것이니 거기부터 본다.**

- [ ] **Step 3: 멤버·요청·핀 모델을 만든다**

`src/Mono.Control/Models/LoungeEntries.cs`:

```csharp
using Mono.Shared;

namespace Mono.Control.Models;

/// <summary>라운지 참가자 한 명.</summary>
public sealed record MemberEntry(
    string PeerId, string Name, MemberRole Role, bool IsOutput, bool Spectator, string SyncText)
{
    public string RoleLabel => Role switch
    {
        MemberRole.Host => "호스트",
        MemberRole.Dj => "DJ",
        MemberRole.Spectator => "참관",
        _ => "청취자"
    };

    public string Detail => string.Join(" · ", new[]
    {
        RoleLabel,
        IsOutput ? "출력" : null,
        Spectator ? "참관 중" : null,
        string.IsNullOrEmpty(SyncText) ? null : SyncText
    }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>Host Queue 모드에서 게스트가 올린 선곡 요청.</summary>
public sealed record RequestEntry(string Id, string Title, string FromName);

/// <summary>타임스탬프 핀 하나.</summary>
public sealed record PinEntry(string Id, string Text, string PeerName, long MediaTimeMs, bool OnCurrentTrack)
{
    public string At => TimeSpan.FromMilliseconds(MediaTimeMs).ToString(@"m\:ss");
    public string Detail => $"{PeerName} · {At}";
}
```

- [ ] **Step 4: 라운지 VM이 스냅샷을 읽는다**

`LoungeViewModel.cs`에 더한다 — `using Mono.Shared;` 를 상단에 추가:

```csharp
    /// <summary>내 peerId. 호스트 여부 판정에 쓴다. 셸이 생성 직후 넣어 준다.</summary>
    public string SelfPeerId { get; set; } = "";

    public ObservableCollection<MemberEntry> Members { get; } = new();
    public ObservableCollection<RequestEntry> Requests { get; } = new();
    public ObservableCollection<PinEntry> Pins { get; } = new();

    [ObservableProperty] private bool _isHost;
    [ObservableProperty] private string _inviteCodeText = "초대 코드 없음";
    [ObservableProperty] private string _inviteExpiryText = "";
    [ObservableProperty] private bool _seekingAllowed = true;
    [ObservableProperty] private bool _commentsAllowed = true;
    [ObservableProperty] private bool _queueLocked;
    [ObservableProperty] private bool _autoAdvance = true;
    [ObservableProperty] private bool _smartAutoplay = true;
    [ObservableProperty] private bool _dspLocked;
    [ObservableProperty] private int _maxMembers = 16;
    [ObservableProperty] private int _qualityPolicyIndex;
    [ObservableProperty] private int _sourceModeIndex;
    [ObservableProperty] private string _pinText = "";
    [ObservableProperty] private bool _inRoom;

    /// <summary>호스트가 아닐 때 왜 못 바꾸는지. 버튼을 숨기지 않고 사유를 붙인다.</summary>
    public string HostBlockedReason => IsHost ? "" : "호스트만 바꿀 수 있습니다";

    partial void OnIsHostChanged(bool value) => OnPropertyChanged(nameof(HostBlockedReason));
```

`ApplySnapshot` 을 바꾼다 — 기존 큐·채팅 처리는 그대로 두고 뒤에 이어 붙인다:

```csharp
    public override void ApplySnapshot(RoomSnapshot snapshot)
    {
        QueueTracks.Clear();
        foreach (var q in snapshot.Queue)
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

        ChatLines.Clear();
        foreach (var c in snapshot.Chat) ChatLines.Add($"{c.PeerName ?? c.PeerId}: {c.Text}");

        InRoom = !string.IsNullOrEmpty(snapshot.Id);
        IsHost = !string.IsNullOrEmpty(SelfPeerId) && snapshot.HostPeerId == SelfPeerId;

        Members.Clear();
        foreach (var m in snapshot.Members)
        {
            var sync = m.Stats is { } st ? $"{st.OffsetMs:+0.0;-0.0}ms" : "";
            Members.Add(new MemberEntry(m.PeerId, m.Name ?? m.PeerId, m.Role, m.IsOutput, m.Spectator, sync));
        }

        Requests.Clear();
        foreach (var r in snapshot.Requests)
            Requests.Add(new RequestEntry(r.Id, r.Title ?? r.TrackId, r.FromName ?? r.FromPeerId));

        Pins.Clear();
        foreach (var p in snapshot.Pins)
            Pins.Add(new PinEntry(p.Id, p.Text, p.PeerName ?? p.PeerId, p.MediaTimeMs, p.OnCurrentTrack));

        InviteCodeText = string.IsNullOrEmpty(snapshot.InviteCode) ? "초대 코드 없음" : snapshot.InviteCode!;
        InviteExpiryText = snapshot.InviteExpiresAt is { } exp
            ? $"{exp.ToLocalTime():HH:mm} 까지"
            : "";

        SeekingAllowed = snapshot.SeekingAllowed;
        CommentsAllowed = snapshot.CommentsAllowed;
        QueueLocked = snapshot.QueueLocked;
        AutoAdvance = snapshot.AutoAdvance;
        SmartAutoplay = snapshot.SmartAutoplay;
        DspLocked = snapshot.DspLocked;
        MaxMembers = snapshot.MaxMembers;
        QualityPolicyIndex = (int)snapshot.QualityPolicy;
        SourceModeIndex = (int)snapshot.SourceMode;
    }
```

- [ ] **Step 5: 셸이 peerId를 넣어 준다**

`MainViewModel.cs` 생성자의 `Lounge = new LoungeViewModel(session);` 다음 줄:

```csharp
        Lounge.SelfPeerId = session.PeerId;
```

- [ ] **Step 6: 빌드하고 테스트**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet test Mono.slnx --nologo -v q
```

기대: 경고 0. 테스트 101개 통과(96 + 5).

- [ ] **Step 7: 커밋**

```bash
git add src/Mono.Control/ tests/Mono.Tests/LoungeSnapshotTests.cs
git commit -m "Read members, requests, pins and policy from the room snapshot

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: 관리자 패널

**Files:**
- Modify: `src/Mono.Control/ViewModels/Pages/LoungeViewModel.cs` (커맨드)
- Create: `src/Mono.Control/Views/Pages/LoungeAdminPanel.axaml` (+ `.axaml.cs`)
- Modify: `src/Mono.Control/Views/Pages/LoungePage.axaml`

**Interfaces:**
- Consumes: `CoreSession.InviteAsync` · `KickAsync` · `TransferHostAsync` · `SetRoleAsync` · `SpectateAsync` ·
  `SetPolicyAsync` · `SetSourceModeAsync` · `SetRoomFlagAsync` (1단계).
- Produces: `LoungeViewModel` 의 `RotateInviteCommand` · `ExtendInviteCommand` · `RevokeInviteCommand` ·
  `KickCommand` · `MakeHostCommand` · `MakeDjCommand` · `MakeListenerCommand` · `ToggleSpectateCommand` ·
  `SetFlagCommand` · `ApplyPolicyCommand` · `ApplySourceModeCommand` · `ApplyMaxMembersCommand`

- [ ] **Step 1: 커맨드를 더한다**

`LoungeViewModel.cs` 끝에:

```csharp
    // ── 초대 ────────────────────────────────────────────
    [RelayCommand]
    private Task RotateInviteAsync() => Safe(() => Session.InviteAsync(InviteAction.Rotate, 360));

    [RelayCommand]
    private Task ExtendInviteAsync() => Safe(() => Session.InviteAsync(InviteAction.Extend, 60));

    [RelayCommand]
    private Task RevokeInviteAsync() => Safe(() => Session.InviteAsync(InviteAction.Revoke));

    // ── 멤버 ────────────────────────────────────────────
    [RelayCommand]
    private Task KickAsync(MemberEntry? m)
        => m is null ? Task.CompletedTask : Safe(() => Session.KickAsync(m.PeerId));

    [RelayCommand]
    private Task MakeHostAsync(MemberEntry? m)
        => m is null ? Task.CompletedTask : Safe(() => Session.TransferHostAsync(m.PeerId));

    [RelayCommand]
    private Task MakeDjAsync(MemberEntry? m)
        => m is null ? Task.CompletedTask : Safe(() => Session.SetRoleAsync(m.PeerId, MemberRole.Dj));

    [RelayCommand]
    private Task MakeListenerAsync(MemberEntry? m)
        => m is null ? Task.CompletedTask : Safe(() => Session.SetRoleAsync(m.PeerId, MemberRole.Listener));

    /// <summary>참관으로 전환하면 오디오를 받지 않는다 — 포맷이 안 맞을 때 쓴다.</summary>
    [RelayCommand]
    private Task ToggleSpectateAsync(string? on)
        => Safe(() => Session.SpectateAsync(on == "1"));

    // ── 정책 ────────────────────────────────────────────
    /// <summary>flag 이름은 Core CommandProcessor 가 받는 값 그대로다.</summary>
    [RelayCommand]
    private Task SetFlagAsync(string? flag)
        => string.IsNullOrEmpty(flag) ? Task.CompletedTask : Safe(() => Session.SetRoomFlagAsync(flag));

    [RelayCommand]
    private Task ApplyPolicyAsync()
        => Safe(() => Session.SetPolicyAsync((QualityPolicy)QualityPolicyIndex));

    [RelayCommand]
    private Task ApplySourceModeAsync()
        => Safe(() => Session.SetSourceModeAsync((PlaybackSourceMode)SourceModeIndex));

    [RelayCommand]
    private Task ApplyMaxMembersAsync()
        => Safe(() => Session.SetRoomFlagAsync("max", index: MaxMembers));
```

`using Mono.Shared;` 가 상단에 있는지 확인한다.

- [ ] **Step 2: 패널 UserControl을 만든다**

`src/Mono.Control/Views/Pages/LoungeAdminPanel.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:pvm="using:Mono.Control.ViewModels.Pages"
             xmlns:models="using:Mono.Control.Models"
             x:Class="Mono.Control.Views.Pages.LoungeAdminPanel"
             x:DataType="pvm:LoungeViewModel">
  <StackPanel Spacing="10" IsVisible="{Binding InRoom}">

    <TextBlock Text="룸 운영" FontWeight="SemiBold"/>
    <TextBlock Text="{Binding HostBlockedReason}" Classes="muted" FontSize="11"
               IsVisible="{Binding !IsHost}"/>

    <!-- 초대 -->
    <Border Classes="panel" CornerRadius="10" Padding="10">
      <StackPanel Spacing="6">
        <DockPanel>
          <TextBlock DockPanel.Dock="Right" Text="{Binding InviteExpiryText}" Classes="muted" FontSize="11"/>
          <TextBlock Text="{Binding InviteCodeText}" FontWeight="SemiBold"/>
        </DockPanel>
        <StackPanel Orientation="Horizontal" Spacing="6">
          <Button Classes="ghost" Content="새 코드" Command="{Binding RotateInviteCommand}"
                  IsEnabled="{Binding IsHost}" ToolTip.Tip="{Binding HostBlockedReason}"/>
          <Button Classes="ghost" Content="연장" Command="{Binding ExtendInviteCommand}"
                  IsEnabled="{Binding IsHost}" ToolTip.Tip="{Binding HostBlockedReason}"/>
          <Button Classes="ghost" Content="폐기" Command="{Binding RevokeInviteCommand}"
                  IsEnabled="{Binding IsHost}" ToolTip.Tip="{Binding HostBlockedReason}"/>
        </StackPanel>
      </StackPanel>
    </Border>

    <!-- 정책 토글 -->
    <Border Classes="panel" CornerRadius="10" Padding="10">
      <StackPanel Spacing="4">
        <CheckBox Content="시킹 허용" IsChecked="{Binding SeekingAllowed}"
                  Command="{Binding SetFlagCommand}" CommandParameter="seek"
                  IsEnabled="{Binding IsHost}" ToolTip.Tip="{Binding HostBlockedReason}"/>
        <CheckBox Content="코멘트 허용" IsChecked="{Binding CommentsAllowed}"
                  Command="{Binding SetFlagCommand}" CommandParameter="comments"
                  IsEnabled="{Binding IsHost}" ToolTip.Tip="{Binding HostBlockedReason}"/>
        <CheckBox Content="큐 잠금" IsChecked="{Binding QueueLocked}"
                  Command="{Binding SetFlagCommand}" CommandParameter="queue_lock"
                  IsEnabled="{Binding IsHost}" ToolTip.Tip="{Binding HostBlockedReason}"/>
        <CheckBox Content="자동 다음곡" IsChecked="{Binding AutoAdvance}"
                  Command="{Binding SetFlagCommand}" CommandParameter="auto_advance"
                  IsEnabled="{Binding IsHost}" ToolTip.Tip="{Binding HostBlockedReason}"/>
        <CheckBox Content="스마트 오토플레이" IsChecked="{Binding SmartAutoplay}"
                  Command="{Binding SetFlagCommand}" CommandParameter="smart_autoplay"
                  IsEnabled="{Binding IsHost}" ToolTip.Tip="{Binding HostBlockedReason}"/>
        <CheckBox Content="DSP 잠금" IsChecked="{Binding DspLocked}"
                  Command="{Binding SetFlagCommand}" CommandParameter="dsp_lock"
                  IsEnabled="{Binding IsHost}" ToolTip.Tip="{Binding HostBlockedReason}"/>
      </StackPanel>
    </Border>

    <!-- 품질 정책 · 소스 모드 · 인원 -->
    <Border Classes="panel" CornerRadius="10" Padding="10">
      <StackPanel Spacing="6">
        <TextBlock Text="품질 정책" FontSize="11" Classes="muted"/>
        <ComboBox SelectedIndex="{Binding QualityPolicyIndex}" HorizontalAlignment="Stretch"
                  IsEnabled="{Binding IsHost}" ToolTip.Tip="{Binding HostBlockedReason}">
          <ComboBoxItem Content="Bit-perfect 전용"/>
          <ComboBoxItem Content="최저 공통 포맷"/>
          <ComboBoxItem Content="불가 시 참관"/>
        </ComboBox>
        <Button Classes="ghost" Content="정책 적용" Command="{Binding ApplyPolicyCommand}"
                IsEnabled="{Binding IsHost}" HorizontalAlignment="Left"/>

        <TextBlock Text="소스 모드" FontSize="11" Classes="muted" Margin="0,6,0,0"/>
        <ComboBox SelectedIndex="{Binding SourceModeIndex}" HorizontalAlignment="Stretch"
                  IsEnabled="{Binding IsHost}" ToolTip.Tip="{Binding HostBlockedReason}">
          <ComboBoxItem Content="Clock-sync (각자 재생)"/>
          <ComboBoxItem Content="Fan-out (호스트가 전송)"/>
        </ComboBox>
        <Button Classes="ghost" Content="모드 적용" Command="{Binding ApplySourceModeCommand}"
                IsEnabled="{Binding IsHost}" HorizontalAlignment="Left"/>

        <TextBlock Text="최대 인원" FontSize="11" Classes="muted" Margin="0,6,0,0"/>
        <StackPanel Orientation="Horizontal" Spacing="6">
          <NumericUpDown Value="{Binding MaxMembers}" Minimum="1" Maximum="128" Width="110"
                         IsEnabled="{Binding IsHost}"/>
          <Button Classes="ghost" Content="적용" Command="{Binding ApplyMaxMembersCommand}"
                  IsEnabled="{Binding IsHost}"/>
        </StackPanel>
      </StackPanel>
    </Border>

    <!-- 참관 -->
    <StackPanel Orientation="Horizontal" Spacing="6">
      <Button Classes="ghost" Content="참관으로" Command="{Binding ToggleSpectateCommand}" CommandParameter="1"
              ToolTip.Tip="오디오를 받지 않고 화면만 봅니다"/>
      <Button Classes="ghost" Content="청취로" Command="{Binding ToggleSpectateCommand}" CommandParameter="0"/>
    </StackPanel>

    <!-- 멤버 -->
    <TextBlock Text="참가자" FontWeight="SemiBold" Margin="0,6,0,0"/>
    <ItemsControl ItemsSource="{Binding Members}">
      <ItemsControl.ItemTemplate>
        <DataTemplate x:DataType="models:MemberEntry">
          <Border Classes="panel" CornerRadius="8" Padding="8" Margin="0,0,0,4">
            <StackPanel Spacing="4">
              <TextBlock Text="{Binding Name}" FontWeight="SemiBold" FontSize="12"/>
              <TextBlock Text="{Binding Detail}" Classes="muted" FontSize="11"/>
              <StackPanel Orientation="Horizontal" Spacing="4">
                <Button Classes="ghost" Content="DJ" Padding="8,2" FontSize="11"
                        Command="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).MakeDjCommand}"
                        CommandParameter="{Binding}"
                        IsEnabled="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).IsHost}"/>
                <Button Classes="ghost" Content="청취자" Padding="8,2" FontSize="11"
                        Command="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).MakeListenerCommand}"
                        CommandParameter="{Binding}"
                        IsEnabled="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).IsHost}"/>
                <Button Classes="ghost" Content="호스트 위임" Padding="8,2" FontSize="11"
                        Command="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).MakeHostCommand}"
                        CommandParameter="{Binding}"
                        IsEnabled="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).IsHost}"/>
                <Button Classes="ghost" Content="킥" Padding="8,2" FontSize="11"
                        Command="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).KickCommand}"
                        CommandParameter="{Binding}"
                        IsEnabled="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).IsHost}"/>
              </StackPanel>
            </StackPanel>
          </Border>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
  </StackPanel>
</UserControl>
```

- [ ] **Step 3: 코드비하인드**

`src/Mono.Control/Views/Pages/LoungeAdminPanel.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace Mono.Control.Views.Pages;

public partial class LoungeAdminPanel : UserControl
{
    public LoungeAdminPanel() => InitializeComponent();
}
```

- [ ] **Step 4: 라운지 우측 열에 붙인다**

`LoungePage.axaml` 의 우측 320px 열(채팅이 있는 `Border`) 안, 채팅 위에 넣는다.
열이 길어지므로 `ScrollViewer` 로 감싼다:

```xml
        <ScrollViewer Grid.Column="1">
          <StackPanel Spacing="12">
            <local:LoungeAdminPanel/>
            <!-- 기존 채팅 블록 -->
          </StackPanel>
        </ScrollViewer>
```

여는 태그에 네임스페이스를 더한다:

```xml
             xmlns:local="using:Mono.Control.Views.Pages"
```

- [ ] **Step 5: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: 라운지에서 룸을 만들면 우측에 운영 패널이 뜨고 초대 코드가 보인다 ·
「새 코드」로 6자리가 바뀐다 · 참가자 목록에 자신이 호스트로 보인다.

- [ ] **Step 6: 커밋**

```bash
git add src/Mono.Control/
git commit -m "Add the lounge admin panel for invites, roles and policy

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: 큐 협업 — Request → 승인/거절, 순서 변경

**Files:**
- Modify: `src/Mono.Control/ViewModels/Pages/LoungeViewModel.cs`
- Modify: `src/Mono.Control/Views/Pages/LoungePage.axaml`

**Interfaces:**
- Consumes: `CoreSession.RequestTrackAsync` · `ApproveRequestAsync` · `RejectRequestAsync` ·
  `RemoveQueueAsync` · `MoveQueueAsync` · `JumpToAsync` (1단계).
- Produces: `ApproveCommand` · `RejectCommand` · `MoveUpCommand` · `MoveDownCommand` ·
  `RemoveFromQueueCommand` · `JumpToCommand`

- [ ] **Step 1: 커맨드를 더한다**

`LoungeViewModel.cs` 에:

```csharp
    // ── 큐 협업 ─────────────────────────────────────────
    [RelayCommand]
    private Task ApproveAsync(RequestEntry? r)
        => r is null ? Task.CompletedTask : Safe(() => Session.ApproveRequestAsync(r.Id));

    [RelayCommand]
    private Task RejectAsync(RequestEntry? r)
        => r is null ? Task.CompletedTask : Safe(() => Session.RejectRequestAsync(r.Id));

    [RelayCommand]
    private Task MoveUpAsync(CatalogTrack? t) => MoveAsync(t, -1);

    [RelayCommand]
    private Task MoveDownAsync(CatalogTrack? t) => MoveAsync(t, 1);

    private Task MoveAsync(CatalogTrack? track, int delta)
    {
        var i = track is null ? -1 : QueueTracks.IndexOf(track);
        return i < 0 ? Task.CompletedTask : Safe(() => Session.MoveQueueAsync(i, delta));
    }

    [RelayCommand]
    private Task RemoveFromQueueAsync(CatalogTrack? track)
    {
        var i = track is null ? -1 : QueueTracks.IndexOf(track);
        return i < 0 ? Task.CompletedTask : Safe(() => Session.RemoveQueueAsync(i));
    }

    [RelayCommand]
    private Task JumpToAsync(CatalogTrack? track)
    {
        var i = track is null ? -1 : QueueTracks.IndexOf(track);
        return i < 0 ? Task.CompletedTask : Safe(() => Session.JumpToAsync(i));
    }
```

- [ ] **Step 2: 요청 목록과 큐 버튼을 화면에 붙인다**

`LoungePage.axaml` 의 큐 블록 위에 요청 목록을 넣는다:

```xml
                <TextBlock Text="선곡 요청" FontWeight="SemiBold" Margin="0,16,0,6"
                           IsVisible="{Binding Requests.Count}"/>
                <ItemsControl ItemsSource="{Binding Requests}">
                  <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="models:RequestEntry">
                      <Border Classes="trackcard" Margin="0,0,0,4" Padding="10">
                        <DockPanel>
                          <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" Spacing="4"
                                      VerticalAlignment="Center">
                            <Button Classes="primary" Content="승인" Padding="10,2" FontSize="11"
                                    Command="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).ApproveCommand}"
                                    CommandParameter="{Binding}"/>
                            <Button Classes="ghost" Content="거절" Padding="10,2" FontSize="11"
                                    Command="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).RejectCommand}"
                                    CommandParameter="{Binding}"/>
                          </StackPanel>
                          <StackPanel>
                            <TextBlock Text="{Binding Title}" FontWeight="SemiBold"/>
                            <TextBlock Text="{Binding FromName}" Classes="muted" FontSize="11"/>
                          </StackPanel>
                        </DockPanel>
                      </Border>
                    </DataTemplate>
                  </ItemsControl.ItemTemplate>
                </ItemsControl>
```

큐 항목 템플릿 안에 조작 버튼을 더한다 — 기존 제목 표시 옆:

```xml
                          <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" Spacing="2"
                                      VerticalAlignment="Center">
                            <Button Classes="ghost" Content="▲" Padding="6,2" FontSize="11" ToolTip.Tip="위로"
                                    Command="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).MoveUpCommand}"
                                    CommandParameter="{Binding}"/>
                            <Button Classes="ghost" Content="▼" Padding="6,2" FontSize="11" ToolTip.Tip="아래로"
                                    Command="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).MoveDownCommand}"
                                    CommandParameter="{Binding}"/>
                            <Button Classes="ghost" Content="▶" Padding="6,2" FontSize="11" ToolTip.Tip="이 곡으로"
                                    Command="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).JumpToCommand}"
                                    CommandParameter="{Binding}"/>
                            <Button Classes="ghost" Content="✕" Padding="6,2" FontSize="11" ToolTip.Tip="제거"
                                    Command="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).RemoveFromQueueCommand}"
                                    CommandParameter="{Binding}"/>
                          </StackPanel>
```

`LoungePage.axaml` 여는 태그에 `xmlns:pvm="using:Mono.Control.ViewModels.Pages"` 가 있는지 확인한다.

- [ ] **Step 3: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: 큐 항목에 ▲▼▶✕ 가 붙고 순서가 바뀐다 · Host Queue 모드에서 요청이 오면 승인/거절이 뜬다.

- [ ] **Step 4: 커밋**

```bash
git add src/Mono.Control/
git commit -m "Wire queue collaboration: requests, reordering and jump-to

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: 타임스탬프 핀과 반응 4종

**Files:**
- Modify: `src/Mono.Control/ViewModels/Pages/LoungeViewModel.cs`
- Modify: `src/Mono.Control/Views/Pages/LoungePage.axaml`

**Interfaces:**
- Consumes: `CoreSession.PinAsync` · `RemovePinAsync` · `SeekPinAsync` · `ReactAsync` (1단계),
  `MainViewModel.SeekValue` (현재 재생 위치).
- Produces: `LoungeViewModel.CurrentMediaMs` (셸이 갱신) · `AddPinCommand` · `RemovePinCommand` · `SeekPinCommand`

- [ ] **Step 1: 커맨드를 더한다**

`LoungeViewModel.cs` 에:

```csharp
    /// <summary>핀을 꽂을 현재 재생 위치. 셸이 스냅샷마다 넣어 준다.</summary>
    [ObservableProperty] private long _currentMediaMs;

    // ── 핀 ──────────────────────────────────────────────
    [RelayCommand]
    private Task AddPinAsync()
    {
        var text = PinText.Trim();
        if (text.Length == 0) return Task.CompletedTask;
        PinText = "";
        return Safe(() => Session.PinAsync(CurrentMediaMs, text));
    }

    [RelayCommand]
    private Task RemovePinAsync(PinEntry? pin)
        => pin is null ? Task.CompletedTask : Safe(() => Session.RemovePinAsync(pin.Id));

    /// <summary>핀 위치로 이동. 시킹이 잠긴 룸이면 Core 가 사유를 돌려준다.</summary>
    [RelayCommand]
    private Task SeekPinAsync(PinEntry? pin)
        => pin is null ? Task.CompletedTask : Safe(() => Session.SeekPinAsync(pin.Id));
```

`ApplySnapshot` 끝에 한 줄 더한다:

```csharp
        CurrentMediaMs = snapshot.MediaTimeMs;
```

- [ ] **Step 2: 핀·반응 UI를 붙인다**

`LoungePage.axaml` 의 채팅 블록 위에:

```xml
              <TextBlock Text="반응" FontWeight="SemiBold" Margin="0,12,0,4"/>
              <StackPanel Orientation="Horizontal" Spacing="4">
                <Button Classes="ghost" Content="❤️" FontSize="16" Padding="10,4"
                        Command="{Binding ReactCommand}" CommandParameter="❤️"/>
                <Button Classes="ghost" Content="🎉" FontSize="16" Padding="10,4"
                        Command="{Binding ReactCommand}" CommandParameter="🎉"/>
                <Button Classes="ghost" Content="👏" FontSize="16" Padding="10,4"
                        Command="{Binding ReactCommand}" CommandParameter="👏"/>
                <Button Classes="ghost" Content="🔥" FontSize="16" Padding="10,4"
                        Command="{Binding ReactCommand}" CommandParameter="🔥"/>
              </StackPanel>
              <TextBlock Classes="muted" FontSize="11" Margin="0,2,0,0"
                         Text="곡당 3번까지 남길 수 있습니다."/>

              <TextBlock Text="핀" FontWeight="SemiBold" Margin="0,12,0,4"/>
              <DockPanel>
                <Button DockPanel.Dock="Right" Classes="ghost" Content="꽂기" Margin="4,0,0,0"
                        Command="{Binding AddPinCommand}"/>
                <TextBox Text="{Binding PinText}" Watermark="이 구간 감상"/>
              </DockPanel>
              <ItemsControl ItemsSource="{Binding Pins}" Margin="0,6,0,0">
                <ItemsControl.ItemTemplate>
                  <DataTemplate x:DataType="models:PinEntry">
                    <Border Classes="panel" CornerRadius="8" Padding="8" Margin="0,0,0,4">
                      <DockPanel>
                        <Button DockPanel.Dock="Right" Classes="ghost" Content="✕" Padding="6,2" FontSize="11"
                                Command="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).RemovePinCommand}"
                                CommandParameter="{Binding}"/>
                        <Button Classes="ghost" Background="Transparent" BorderThickness="0"
                                HorizontalContentAlignment="Left" ToolTip.Tip="이 구간으로 이동"
                                Command="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).SeekPinCommand}"
                                CommandParameter="{Binding}">
                          <StackPanel>
                            <TextBlock Text="{Binding Text}" FontSize="12"/>
                            <TextBlock Text="{Binding Detail}" Classes="muted" FontSize="11"/>
                          </StackPanel>
                        </Button>
                      </DockPanel>
                    </Border>
                  </DataTemplate>
                </ItemsControl.ItemTemplate>
              </ItemsControl>
```

- [ ] **Step 3: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: 재생 중 「꽂기」로 핀이 생기고 시크바에 주황 마커가 나타난다(2단계 `SeekBar`) ·
핀을 누르면 그 위치로 이동한다 · 반응 4종을 누르면 히트맵이 자란다.

- [ ] **Step 4: 커밋**

```bash
git add src/Mono.Control/
git commit -m "Add timestamp pins and the four reaction buttons

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: 뮤지션 관계도와 세션 아카이브

**Files:**
- Create: `src/Mono.Control/Models/ArtistGraph.cs`
- Modify: `src/Mono.Control/ViewModels/Pages/LoungeViewModel.cs`
- Modify: `src/Mono.Control/ViewModels/MainViewModel.cs` · `MainViewModel.Messages.cs`
- Modify: `src/Mono.Control/Views/Pages/LoungePage.axaml`

**Interfaces:**
- Consumes: `CoreSession.GraphAsync(string)` · `FollowArtistAsync(string)` · `EndSessionAsync(bool)` (1단계).
  `graph` 응답 `{ artist{id,name,bio}, albums[], tracks[], related[{id,name,bio}], neighbours[{id,name}] }`,
  `archive` 응답 `{ ok, consent, archiveId, playlistId, body }`.
- Produces: `ArtistGraph` · `GraphArtist` 모델, `LoungeViewModel.Graph` · `.ArchiveResult`,
  `ShowGraphCommand` · `FollowArtistCommand` · `EndSessionSaveCommand` · `EndSessionDiscardCommand`

- [ ] **Step 1: 그래프 모델**

`src/Mono.Control/Models/ArtistGraph.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Mono.Control.Models;

/// <summary>graph 응답. Core 로컬 그래프 쿼리이므로 클라우드 왕복이 없다.</summary>
public sealed class ArtistGraph
{
    [JsonPropertyName("artist")] public GraphArtist? Artist { get; set; }
    [JsonPropertyName("related")] public List<GraphArtist> Related { get; set; } = [];
    [JsonPropertyName("neighbours")] public List<GraphArtist> Neighbours { get; set; } = [];

    public bool HasRelated => Related.Count > 0;
    public bool HasNeighbours => Neighbours.Count > 0;
}

public sealed class GraphArtist
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("bio")] public string? Bio { get; set; }
}
```

- [ ] **Step 2: VM에 상태와 커맨드**

`LoungeViewModel.cs` 에:

```csharp
    [ObservableProperty] private ArtistGraph? _graph;
    [ObservableProperty] private string _archiveResult = "";

    /// <summary>그래프 패널 표시 여부. XAML 에서 컨버터를 쓰지 않도록 bool 로 낸다.</summary>
    public bool HasGraph => Graph?.Artist is not null;

    partial void OnGraphChanged(ArtistGraph? value) => OnPropertyChanged(nameof(HasGraph));

    public void ApplyGraph(ArtistGraph? graph) => Graph = graph;

    public void ApplyArchive(string message) => ArchiveResult = message;

    /// <summary>현재 곡 아티스트의 관계도. 셸이 artistId 를 넘긴다.</summary>
    [RelayCommand]
    private Task ShowGraphAsync(string? artistId)
        => string.IsNullOrEmpty(artistId) ? Task.CompletedTask : Safe(() => Session.GraphAsync(artistId));

    /// <summary>"이 연주자 따라가기" — 다음 큐 후보로 제안한다.</summary>
    [RelayCommand]
    private Task FollowArtistAsync(GraphArtist? artist)
        => artist is null ? Task.CompletedTask : Safe(() => Session.FollowArtistAsync(artist.Id));

    [RelayCommand]
    private Task EndSessionSaveAsync() => Safe(() => Session.EndSessionAsync(true));

    [RelayCommand]
    private Task EndSessionDiscardAsync() => Safe(() => Session.EndSessionAsync(false));
```

- [ ] **Step 3: 셸이 응답을 넘긴다**

`MainViewModel.Messages.cs` 의 switch 에:

```csharp
            case MessageTypes.Graph:
                try { Lounge.ApplyGraph(JsonSerializer.Deserialize<ArtistGraph>(msg.Body ?? "", Json)); }
                catch { Lounge.ApplyGraph(null); }
                break;
            case MessageTypes.Archive:
                Lounge.ApplyArchive(string.IsNullOrWhiteSpace(msg.PlaylistId)
                    ? (msg.Body ?? "세션을 종료했습니다")
                    : "세션을 저장하고 하이라이트 플레이리스트를 만들었습니다");
                break;
```

`ApplyRoomState` 에서 현재 아티스트를 라운지에 알려 준다 — `Lounge.ApplySnapshot(snap);` 앞:

```csharp
        Lounge.CurrentArtistId = snap.CurrentTrack?.ArtistId ?? "";
```

`LoungeViewModel` 에 속성을 더한다:

```csharp
    [ObservableProperty] private string _currentArtistId = "";
```

- [ ] **Step 4: 관계도·아카이브 UI**

`LoungePage.axaml` 의 핀 블록 아래:

```xml
              <TextBlock Text="뮤지션 관계도" FontWeight="SemiBold" Margin="0,12,0,4"/>
              <Button Classes="ghost" Content="현재 아티스트 탐색" HorizontalAlignment="Left"
                      Command="{Binding ShowGraphCommand}" CommandParameter="{Binding CurrentArtistId}"/>
              <StackPanel IsVisible="{Binding HasGraph}" Margin="0,6,0,0" Spacing="4">
                <TextBlock Text="{Binding Graph.Artist.Name}" FontWeight="SemiBold" FontSize="12"/>
                <TextBlock Text="{Binding Graph.Artist.Bio}" Classes="muted" FontSize="11"
                           TextWrapping="Wrap" MaxHeight="80"/>
                <TextBlock Text="연관 아티스트" Classes="muted" FontSize="11" Margin="0,4,0,0"
                           IsVisible="{Binding Graph.HasRelated}"/>
                <ItemsControl ItemsSource="{Binding Graph.Related}">
                  <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="models:GraphArtist">
                      <Button Classes="ghost" Margin="0,2,0,0" HorizontalAlignment="Stretch"
                              HorizontalContentAlignment="Left" Content="{Binding Name}"
                              ToolTip.Tip="따라가기 — 다음 큐 후보로 제안"
                              Command="{Binding $parent[UserControl].((pvm:LoungeViewModel)DataContext).FollowArtistCommand}"
                              CommandParameter="{Binding}"/>
                    </DataTemplate>
                  </ItemsControl.ItemTemplate>
                </ItemsControl>
              </StackPanel>

              <TextBlock Text="세션 종료" FontWeight="SemiBold" Margin="0,16,0,4"/>
              <TextBlock Classes="muted" FontSize="11" TextWrapping="Wrap"
                         Text="저장하면 좋았던 곡(♥·핀·완청)이 하이라이트 플레이리스트가 됩니다."/>
              <StackPanel Orientation="Horizontal" Spacing="6" Margin="0,4,0,0">
                <Button Classes="primary" Content="저장하고 종료" Command="{Binding EndSessionSaveCommand}"/>
                <Button Classes="ghost" Content="저장 없이 종료" Command="{Binding EndSessionDiscardCommand}"/>
              </StackPanel>
              <TextBlock Text="{Binding ArchiveResult}" Classes="muted" FontSize="11"
                         TextWrapping="Wrap" Margin="0,4,0,0"/>
```

- [ ] **Step 5: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: 재생 중 「현재 아티스트 탐색」으로 관계도가 뜬다 · 연관 아티스트를 누르면 따라가기가 동작한다 ·
「저장하고 종료」 후 Playlists 화면에 하이라이트 리스트가 생긴다.

- [ ] **Step 6: 커밋**

```bash
git add src/Mono.Control/
git commit -m "Add the artist graph and session archive controls

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## 4단계 완료 기준

- [ ] `dotnet build Mono.slnx` 경고 0, 오류 0
- [ ] `dotnet test Mono.slnx` 101개 통과 (3b 96 + 라운지 스냅샷 5)
- [ ] 관리자 패널에 문서 §관리자 기능이 전부 있다 — 초대(발급·연장·폐기) · 킥 · 역할 · 호스트 위임 · 품질 정책 · 소스 모드 · 룸 플래그 6종 · 최대 인원 · 참관
- [ ] 호스트가 아니면 관리자 버튼이 **숨겨지지 않고** 비활성 + `"호스트만 바꿀 수 있습니다"` 툴팁
- [ ] 큐 항목에 순서 변경·제거·바로 이동, Host Queue 모드에서 요청 승인·거절
- [ ] 핀을 꽂으면 2단계 시크바에 주황 마커가 나타난다
- [ ] 반응 4종 버튼과 곡당 3회 안내
- [ ] 세션 종료 시 저장/휘발을 고르고, 저장하면 플레이리스트가 생긴다

## 다음 단계

5단계 — Now Playing 5모드 · 설정 10트리 · 단축키 · 존 관리 · 페어링 · 백업. 별도 계획으로 쓴다.
