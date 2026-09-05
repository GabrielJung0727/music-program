# Control 패리티 2단계 — 하단 플레이어 바 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `04-UIUX기획서` §3.2가 정의한 하단 바를 완성한다 — 볼륨, 출력 존/장치 선택, ♥, 큐 드로어, 반응 히트맵과 핀 마커가 얹힌 시크바, 시그널 패스 팝오버.

**Architecture:** 1단계가 만든 `RoomSnapshot`이 이미 `outputs`(볼륨·능력·배지) · `heatmap` · `pins` · `reactionCounts`를 싣고 온다. 새 Core 작업은 없다. 시크바는 마커가 수백 개여도 60fps를 유지해야 하므로 요소를 생성하지 않고 직접 그리는 커스텀 `Control`로 만들고, 그리기에 쓰는 좌표 계산은 Avalonia에 의존하지 않는 순수 함수로 분리해 테스트한다.

**Tech Stack:** C# / .NET 8 · Avalonia 11 · CommunityToolkit.Mvvm · xunit

**Spec:** [`docs/superpowers/specs/2026-09-05-control-full-parity-design.md`](../specs/2026-09-05-control-full-parity-design.md) §4 단계 2

**선행:** [1단계](2026-09-05-control-parity-phase1-foundation.md) 완료 (커밋 `7e7c375`)

## Global Constraints

- 대상 프레임워크 `net8.0`. 새 NuGet 패키지를 추가하지 않는다.
- JSON은 `LineFraming.JsonOptions` — CamelCase, 열거형은 정수.
- 의존 방향: `Control` → `Protocol` → `Shared`. Control은 Core를 참조하지 않는다.
- `dotnet build Mono.slnx` 경고 0 유지. `dotnet test Mono.slnx` 65개는 회귀 가드다.
- 사용자 대상 문자열은 한국어.
- **정책상 막힌 동작은 버튼을 숨기지 않는다.** 비활성 + 사유 툴팁으로 노출한다 (문서 §6 신뢰 UI).
- 커밋 메시지 말미에 `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## Core 사실 (추측 금지 — 확인된 값)

| 항목 | 확인된 동작 |
| --- | --- |
| 히트맵 버킷 | **10,000ms 고정**. `SegmentHit(TrackId, BucketMs, Count)`. 반응 + 핀을 합산하고 과거 아카이브까지 더한다 |
| 반응 화이트리스트 | `❤️` `🎉` `👏` `🔥` 넷뿐. 그 외는 Core가 거부 |
| 반응 상한 | `MaxReactionsPerUserPerTrack = 3` — 곡당 유저당 3회. 초과 시 `"이 곡에는 이미 3번 반응했습니다"` |
| ♥의 정체 | 별도 즐겨찾기가 없다. `react` + `❤️`가 ♥다. 하이라이트 집계가 `♥/heart/❤/❤️`를 센다 |
| 디지털 볼륨 금지 | `AllowsDigitalVolume` = 하드웨어 볼륨이 없고 **Audiophile 모드가 아니며** `RequireBitPerfect`가 아닐 때만 허용. 위반 시 `"이 룸은 디지털 볼륨 감쇠를 금지합니다 — 하드웨어 볼륨만 사용하세요."` |
| 남의 볼륨 조절 | 호스트만 가능. 아니면 `"only the host may set another endpoint's volume"` |
| `list_zones` 응답 | `[{ id, name, mode, syncRoomId, members:[{peerId,name,online,roomId}] }]` |
| `endpoints` 응답 | `EndpointRecord[]` 직렬화 그대로 |

---

### Task 1: 색 토큰 보강

`04-UIUX기획서` §2.1이 정의한 Warn·Bad가 `App.axaml`에 없다. 핀 마커와 오류 표시가 이 색을 쓴다.

**Files:**
- Modify: `src/Mono.Control/App.axaml` (Light 딕셔너리 12–24행 부근, Dark 57–69행 부근)

**Interfaces:**
- Produces: `Mono.Warn` · `Mono.Bad` 정적 리소스. 이후 태스크가 `{DynamicResource Mono.Warn}`로 쓴다.

- [ ] **Step 1: Light 딕셔너리에 추가**

`<SolidColorBrush x:Key="Mono.GhostFg" Color="#1A1A1E"/>` 아래에:

```xml
          <SolidColorBrush x:Key="Mono.Warn" Color="#D9762F"/>
          <SolidColorBrush x:Key="Mono.Bad" Color="#CC4444"/>
```

- [ ] **Step 2: Dark 딕셔너리에 추가**

`<SolidColorBrush x:Key="Mono.GhostFg" Color="#F2F2F5"/>` 아래에:

```xml
          <SolidColorBrush x:Key="Mono.Warn" Color="#E39A5C"/>
          <SolidColorBrush x:Key="Mono.Bad" Color="#EE8888"/>
```

- [ ] **Step 3: 빌드**

```bash
dotnet build Mono.slnx -v q --nologo
```

기대: 경고 0, 오류 0.

- [ ] **Step 4: 커밋**

```bash
git add src/Mono.Control/App.axaml
git commit -m "Add the Warn and Bad colour tokens the UI spec defines

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: 시크바 좌표 계산 (순수 함수)

히트맵 버킷과 핀을 0–1 정규화 좌표로 바꾸는 계산. Avalonia에 의존하지 않으므로 테스트가 가볍다.

**Files:**
- Create: `src/Mono.Protocol/SeekLayers.cs`
- Test: `tests/Mono.Tests/SeekLayerTests.cs`

**Interfaces:**
- Consumes: `SnapshotHeatBucket` · `SnapshotPin` (1단계 Task 2에서 정의).
- Produces:
  - `readonly record struct HeatBand(double Start, double End, double Intensity)` — 전부 0–1
  - `readonly record struct PinMark(double Position, string Id, string Text, string? PeerName)` — `Position` 0–1
  - `static IReadOnlyList<HeatBand> SeekLayers.Bands(IEnumerable<SnapshotHeatBucket> buckets, long durationMs)`
  - `static IReadOnlyList<PinMark> SeekLayers.Marks(IEnumerable<SnapshotPin> pins, long durationMs)`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`tests/Mono.Tests/SeekLayerTests.cs`:

```csharp
using Mono.Protocol;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 시크바에 그릴 히트맵·핀의 좌표 계산. Core는 10초 버킷으로 집계해 보내고,
/// Control은 그걸 트랙 길이에 대한 0–1 비율로 바꾼다.
/// </summary>
public class SeekLayerTests
{
    private static SnapshotHeatBucket Bucket(long at, int count)
        => new() { TrackId = "t", BucketMs = at, Count = count };

    [Fact]
    public void BandsSpanTenSecondBucketsAsFractions()
    {
        // 100초 트랙. 0–10초 버킷은 0.0–0.1 을 차지한다.
        var bands = SeekLayers.Bands([Bucket(0, 1)], 100_000);

        var b = Assert.Single(bands);
        Assert.Equal(0.0, b.Start, 3);
        Assert.Equal(0.1, b.End, 3);
    }

    [Fact]
    public void IntensityIsRelativeToTheBusiestBucket()
    {
        var bands = SeekLayers.Bands([Bucket(0, 1), Bucket(10_000, 4), Bucket(20_000, 2)], 100_000);

        Assert.Equal(0.25, bands[0].Intensity, 3);
        Assert.Equal(1.00, bands[1].Intensity, 3);
        Assert.Equal(0.50, bands[2].Intensity, 3);
    }

    [Fact]
    public void BandsAreClampedToTheTrack()
    {
        // 마지막 버킷이 트랙 끝을 넘어가도 1.0 을 넘지 않는다.
        var bands = SeekLayers.Bands([Bucket(20_000, 1)], 25_000);

        Assert.Equal(0.8, bands[0].Start, 3);
        Assert.Equal(1.0, bands[0].End, 3);
    }

    [Fact]
    public void ZeroDurationYieldsNothing()
    {
        // 곡이 없을 때 0으로 나누지 않는다.
        Assert.Empty(SeekLayers.Bands([Bucket(0, 3)], 0));
        Assert.Empty(SeekLayers.Marks([new SnapshotPin { MediaTimeMs = 5 }], 0));
    }

    [Fact]
    public void MarksUseOnlyCurrentTrackPins()
    {
        var pins = new[]
        {
            new SnapshotPin { Id = "p1", MediaTimeMs = 30_000, Text = "여기", OnCurrentTrack = true },
            new SnapshotPin { Id = "p2", MediaTimeMs = 40_000, Text = "다른 곡", OnCurrentTrack = false }
        };

        var marks = SeekLayers.Marks(pins, 100_000);

        var m = Assert.Single(marks);
        Assert.Equal("p1", m.Id);
        Assert.Equal(0.3, m.Position, 3);
        Assert.Equal("여기", m.Text);
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter SeekLayerTests
```

기대: 컴파일 실패 — `SeekLayers`가 없다.

- [ ] **Step 3: 최소 구현**

`src/Mono.Protocol/SeekLayers.cs`:

```csharp
namespace Mono.Protocol;

/// <summary>시크바 위 반응 히트맵 구간. 좌표와 세기 모두 0–1이다.</summary>
public readonly record struct HeatBand(double Start, double End, double Intensity);

/// <summary>시크바 위 타임스탬프 핀. Position은 0–1이다.</summary>
public readonly record struct PinMark(double Position, string Id, string Text, string? PeerName);

/// <summary>
/// Core가 보낸 히트맵·핀을 시크바 좌표로 바꾼다.
/// Core는 10초 버킷으로 집계하므로(RoomManager.ReactionHeatmap) 여기서도 같은 폭을 쓴다.
/// </summary>
public static class SeekLayers
{
    /// <summary>Core의 히트맵 버킷 폭. RoomManager.ReactionHeatmap과 같아야 한다.</summary>
    public const long BucketMs = 10_000;

    public static IReadOnlyList<HeatBand> Bands(IEnumerable<SnapshotHeatBucket> buckets, long durationMs)
    {
        if (durationMs <= 0) return [];
        var list = buckets.Where(b => b.Count > 0).ToList();
        if (list.Count == 0) return [];

        var busiest = (double)list.Max(b => b.Count);
        return list
            .Select(b => new HeatBand(
                Math.Clamp(b.BucketMs / (double)durationMs, 0, 1),
                Math.Clamp((b.BucketMs + BucketMs) / (double)durationMs, 0, 1),
                b.Count / busiest))
            .ToList();
    }

    public static IReadOnlyList<PinMark> Marks(IEnumerable<SnapshotPin> pins, long durationMs)
    {
        if (durationMs <= 0) return [];
        return pins
            .Where(p => p.OnCurrentTrack)
            .Select(p => new PinMark(
                Math.Clamp(p.MediaTimeMs / (double)durationMs, 0, 1),
                p.Id,
                p.Text,
                p.PeerName))
            .ToList();
    }
}
```

- [ ] **Step 4: 통과를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter SeekLayerTests
```

기대: 5개 통과.

- [ ] **Step 5: 커밋**

```bash
git add src/Mono.Protocol/SeekLayers.cs tests/Mono.Tests/SeekLayerTests.cs
git commit -m "Compute seek-bar heatmap bands and pin marks

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: `SeekBar` 커스텀 컨트롤

핀이 수백 개여도 요소를 만들지 않고 직접 그린다. 문서 §2.3 시크바 스펙: 얇은 트랙 + Point 채움 + 핀 마커 + 히트맵 레이어.

**Files:**
- Create: `src/Mono.Control/Controls/SeekBar.cs`

**Interfaces:**
- Consumes: `SeekLayers.Bands` · `SeekLayers.Marks` (Task 2).
- Produces: `Mono.Control.Controls.SeekBar` — `StyledProperty` 6개와 이벤트 1개.
  - `double Value` · `double Maximum` (ms)
  - `IReadOnlyList<SnapshotHeatBucket>? Heatmap`
  - `IReadOnlyList<SnapshotPin>? Pins`
  - `bool SeekEnabled` (기본 true)
  - `event EventHandler<double>? Seeked` — 사용자가 놓은 위치(ms)

- [ ] **Step 1: 컨트롤을 구현한다**

`src/Mono.Control/Controls/SeekBar.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Mono.Protocol;

namespace Mono.Control.Controls;

/// <summary>
/// 시크바 — 트랙 · 재생 채움 · 반응 히트맵 · 타임스탬프 핀을 한 번에 그린다.
/// 마커마다 Border를 만들면 큰 곡에서 프레임이 떨어지므로 직접 렌더한다.
/// 시킹이 금지된 룸에서는 SeekEnabled=false 로 두어 표시는 하되 조작만 막는다.
/// </summary>
public sealed class SeekBar : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(Value));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(Maximum), 1d);

    public static readonly StyledProperty<IReadOnlyList<SnapshotHeatBucket>?> HeatmapProperty =
        AvaloniaProperty.Register<SeekBar, IReadOnlyList<SnapshotHeatBucket>?>(nameof(Heatmap));

    public static readonly StyledProperty<IReadOnlyList<SnapshotPin>?> PinsProperty =
        AvaloniaProperty.Register<SeekBar, IReadOnlyList<SnapshotPin>?>(nameof(Pins));

    public static readonly StyledProperty<bool> SeekEnabledProperty =
        AvaloniaProperty.Register<SeekBar, bool>(nameof(SeekEnabled), true);

    static SeekBar()
    {
        AffectsRender<SeekBar>(ValueProperty, MaximumProperty, HeatmapProperty, PinsProperty, SeekEnabledProperty);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public IReadOnlyList<SnapshotHeatBucket>? Heatmap { get => GetValue(HeatmapProperty); set => SetValue(HeatmapProperty, value); }
    public IReadOnlyList<SnapshotPin>? Pins { get => GetValue(PinsProperty); set => SetValue(PinsProperty, value); }
    public bool SeekEnabled { get => GetValue(SeekEnabledProperty); set => SetValue(SeekEnabledProperty, value); }

    /// <summary>사용자가 시킹을 마쳤을 때 media_time(ms)을 전달한다.</summary>
    public event EventHandler<double>? Seeked;

    private bool _dragging;

    public SeekBar()
    {
        Height = 26;
        Focusable = false;
    }

    private IBrush Res(string key, IBrush fallback)
        => this.TryFindResource(key, out var v) && v is IBrush b ? b : fallback;

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        if (w <= 0) return;

        var line = Res("Mono.Border", Brushes.Gainsboro);
        var accent = Res("Mono.Accent", Brushes.MediumSlateBlue);
        var warn = Res("Mono.Warn", Brushes.Orange);
        // 히트맵은 강조색을 옅게 깐다. 리소스가 단색이 아니면 기본색으로 물러난다.
        var heatColor = accent is ISolidColorBrush s ? s.Color : Colors.MediumSlateBlue;

        const double trackH = 4;
        var trackY = (Bounds.Height - trackH) / 2;
        var duration = (long)Math.Max(1, Maximum);

        // 히트맵을 트랙 뒤에 깔아 "많이 반응한 구간"을 먼저 읽히게 한다.
        foreach (var band in SeekLayers.Bands(Heatmap ?? [], duration))
        {
            var x = band.Start * w;
            var bw = Math.Max(1, (band.End - band.Start) * w);
            var h = 6 + band.Intensity * 12;
            ctx.FillRectangle(
                new ImmutableSolidColorBrush(heatColor, 0.10 + band.Intensity * 0.30),
                new Rect(x, trackY + trackH / 2 - h / 2, bw, h));
        }

        ctx.FillRectangle(line, new Rect(0, trackY, w, trackH), 2);

        var progress = Math.Clamp(Value / duration, 0, 1);
        if (progress > 0)
            ctx.FillRectangle(accent, new Rect(0, trackY, progress * w, trackH), 2);

        foreach (var mark in SeekLayers.Marks(Pins ?? [], duration))
            ctx.FillRectangle(warn, new Rect(mark.Position * w - 1, trackY - 5, 2, trackH + 10), 1);

        // 핸들은 시킹이 가능할 때만 — 못 만지는 컨트롤에 손잡이를 그리지 않는다.
        if (SeekEnabled)
            ctx.DrawEllipse(accent, null, new Point(progress * w, trackY + trackH / 2), 6, 6);
    }

    private double PositionToMs(double x)
        => Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1) * Math.Max(1, Maximum);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!SeekEnabled) return;
        _dragging = true;
        e.Pointer.Capture(this);
        Value = PositionToMs(e.GetPosition(this).X);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) Value = PositionToMs(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        Seeked?.Invoke(this, Value);
    }
}
```

- [ ] **Step 2: 빌드**

```bash
dotnet build Mono.slnx -v q --nologo
```

기대: 경고 0, 오류 0.

- [ ] **Step 3: 커밋**

```bash
git add src/Mono.Control/Controls/SeekBar.cs
git commit -m "Draw the seek bar, its heatmap and its pins in one control

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: 시크바를 하단 바에 붙인다

기존 `Slider`를 `SeekBar`로 바꾸고, 스냅샷의 히트맵·핀·시킹 정책을 물린다.

**Files:**
- Modify: `src/Mono.Control/ViewModels/MainViewModel.cs` (속성 추가)
- Modify: `src/Mono.Control/ViewModels/MainViewModel.Messages.cs` (`ApplyRoomState`)
- Modify: `src/Mono.Control/Views/MainWindow.axaml` (133–138행 부근 `Slider`)
- Modify: `src/Mono.Control/Views/MainWindow.axaml.cs` (`SeekLost` 교체)

**Interfaces:**
- Produces: `MainViewModel.Heatmap` (`IReadOnlyList<SnapshotHeatBucket>`) · `.Pins` (`IReadOnlyList<SnapshotPin>`) · `.SeekingAllowed` (`bool`) · `.SeekBlockedReason` (`string`).

- [ ] **Step 1: 뷰모델에 속성을 추가한다**

`MainViewModel.cs`의 `_currentSnapshot` 선언 아래:

```csharp
    [ObservableProperty] private IReadOnlyList<SnapshotHeatBucket> _heatmap = [];
    [ObservableProperty] private IReadOnlyList<SnapshotPin> _pins = [];
    [ObservableProperty] private bool _seekingAllowed = true;

    /// <summary>시킹이 막힌 이유. 비어 있으면 툴팁을 띄우지 않는다.</summary>
    public string SeekBlockedReason => SeekingAllowed ? "" : "호스트가 시킹을 잠갔습니다";

    partial void OnSeekingAllowedChanged(bool value) => OnPropertyChanged(nameof(SeekBlockedReason));
```

- [ ] **Step 2: 스냅샷에서 채운다**

`MainViewModel.Messages.cs`의 `ApplyRoomState`에서 `Lounge.ApplySnapshot(snap);` 바로 위에:

```csharp
        Heatmap = snap.Heatmap;
        Pins = snap.Pins;
        SeekingAllowed = snap.SeekingAllowed;
```

- [ ] **Step 3: XAML을 교체한다**

`MainWindow.axaml` 여는 태그에 네임스페이스를 추가한다:

```xml
        xmlns:ctl="using:Mono.Control.Controls"
```

기존 시크 `Grid`(133–138행 부근) 안의 `<Slider .../>` 한 줄을 이걸로 바꾼다:

```xml
              <ctl:SeekBar Grid.Column="1"
                           Maximum="{Binding SeekMaximum}"
                           Value="{Binding SeekValue, Mode=TwoWay}"
                           Heatmap="{Binding Heatmap}"
                           Pins="{Binding Pins}"
                           SeekEnabled="{Binding SeekingAllowed}"
                           ToolTip.Tip="{Binding SeekBlockedReason}"
                           Seeked="OnSeeked"/>
```

- [ ] **Step 4: 코드비하인드를 바꾼다**

`MainWindow.axaml.cs`의 `SeekLost` 메서드를 지우고 이걸 넣는다:

```csharp
    private void OnSeeked(object? sender, double mediaTimeMs)
    {
        if (DataContext is MainViewModel vm)
            _ = vm.SeekToCommand.ExecuteAsync(null);
    }
```

`using Avalonia.Input;`가 다른 곳에서 쓰이지 않으면 지운다.

- [ ] **Step 5: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: 하단 시크바가 그려진다 · 재생 중 채움이 움직인다 · 클릭하면 그 위치로 이동한다.
Audiophile 룸을 만들면 핸들이 사라지고 클릭이 먹지 않으며 툴팁에 사유가 뜬다.

- [ ] **Step 6: 커밋**

```bash
git add src/Mono.Control/
git commit -m "Put the heatmap and pin layers on the player bar seek control

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: 볼륨과 출력 장치 선택

`04-UIUX기획서` §3.2 우측 클러스터. 정책이 디지털 볼륨을 막으면 비활성 + 사유.

**Files:**
- Create: `src/Mono.Control/ViewModels/OutputDevice.cs`
- Modify: `src/Mono.Control/ViewModels/MainViewModel.cs`
- Modify: `src/Mono.Control/ViewModels/MainViewModel.Messages.cs`
- Modify: `src/Mono.Control/Views/MainWindow.axaml`

**Interfaces:**
- Consumes: `SnapshotOutput` · `CoreSession.SetVolumeAsync` (1단계 Task 8).
- Produces:
  - `Mono.Control.ViewModels.OutputDevice` — `PeerId` `Name` `Badge` `Note` `VolumePercent` `HardwareVolume` `Spectator` `Caps`
  - `MainViewModel.Outputs` (`ObservableCollection<OutputDevice>`) · `.SelectedOutput` · `.Volume` · `.VolumeEnabled` · `.VolumeBlockedReason` · `.OutputChip`

- [ ] **Step 1: 장치 모델을 만든다**

`src/Mono.Control/ViewModels/OutputDevice.cs`:

```csharp
using Mono.Protocol;

namespace Mono.Control.ViewModels;

/// <summary>하단 바 장치 목록의 한 줄. 스냅샷의 outputs 항목을 화면용으로 옮긴 것.</summary>
public sealed class OutputDevice
{
    public required string PeerId { get; init; }
    public required string Name { get; init; }
    public string? Badge { get; init; }
    public string? Note { get; init; }
    public int VolumePercent { get; init; }
    public bool HardwareVolume { get; init; }
    public bool Spectator { get; init; }
    public string Caps { get; init; } = "";

    public static OutputDevice From(SnapshotOutput o) => new()
    {
        PeerId = o.PeerId,
        Name = string.IsNullOrWhiteSpace(o.DisplayName) ? o.PeerId : o.DisplayName!,
        Badge = o.Badge,
        Note = o.Note,
        VolumePercent = o.VolumePercent,
        HardwareVolume = o.HardwareVolume,
        Spectator = o.Spectator,
        Caps = $"{o.MaxSampleRate / 1000}kHz · {o.MaxBitDepth}bit"
               + (o.SupportsDsd ? " · DSD" : "")
               + (o.ExclusiveMode ? " · Exclusive" : " · Shared")
    };

    /// <summary>참관 중이거나 포맷이 안 맞으면 사유가 배지 옆에 붙는다.</summary>
    public string Detail => string.IsNullOrWhiteSpace(Note) ? Caps : $"{Caps} · {Note}";
}
```

- [ ] **Step 2: 뷰모델에 상태를 더한다**

`MainViewModel.cs`의 `_pins` 아래:

```csharp
    [ObservableProperty] private int _volume = 100;
    [ObservableProperty] private bool _volumeEnabled = true;
    [ObservableProperty] private string _volumeBlockedReason = "";
    [ObservableProperty] private OutputDevice? _selectedOutput;
    [ObservableProperty] private string _outputChip = "출력 없음";

    public ObservableCollection<OutputDevice> Outputs { get; } = new();

    /// <summary>슬라이더를 놓을 때만 보낸다 — 드래그 중 매 픽셀마다 명령을 쏘지 않는다.</summary>
    [RelayCommand]
    private Task ApplyVolumeAsync()
        => SelectedOutput is null
            ? Task.CompletedTask
            : Safe(() => _session.SetVolumeAsync(SelectedOutput.PeerId, Volume));

    [RelayCommand]
    private Task SelectOutputAsync(OutputDevice? device)
    {
        if (device is null) return Task.CompletedTask;
        SelectedOutput = device;
        Volume = device.VolumePercent;
        return Task.CompletedTask;
    }
```

- [ ] **Step 3: 스냅샷에서 채운다**

`MainViewModel.Messages.cs`의 `ApplyRoomState`에서 `Heatmap = snap.Heatmap;` 아래:

```csharp
        var keep = SelectedOutput?.PeerId;
        Outputs.Clear();
        foreach (var o in snap.Outputs) Outputs.Add(OutputDevice.From(o));
        SelectedOutput = Outputs.FirstOrDefault(o => o.PeerId == keep) ?? Outputs.FirstOrDefault();

        if (SelectedOutput is { } sel)
        {
            Volume = sel.VolumePercent;
            OutputChip = $"{sel.Name} · {sel.Badge}";
            // Core의 AllowsDigitalVolume 과 같은 판정: 하드웨어 볼륨이 있으면 언제나 가능하고,
            // 없으면 Audiophile·Bit-perfect 전용 룸에서 막힌다.
            VolumeEnabled = sel.HardwareVolume
                            || (snap.Mode != Mono.Shared.RoomMode.Audiophile
                                && snap.QualityPolicy != Mono.Shared.QualityPolicy.RequireBitPerfect);
            VolumeBlockedReason = VolumeEnabled
                ? ""
                : "이 룸은 디지털 볼륨 감쇠를 금지합니다 — 하드웨어 볼륨을 쓰세요";
        }
        else
        {
            OutputChip = "출력 없음";
            VolumeEnabled = false;
            VolumeBlockedReason = "출력 장치가 없습니다 — 「출력 연결」을 누르세요";
        }
```

- [ ] **Step 4: 하단 바 우측을 다시 짠다**

`MainWindow.axaml`의 `Grid.Column="2"` `StackPanel`(RoomChip·SyncText·SignalPathText·출력 연결 버튼이 있는 곳)의 내용을 이걸로 바꾼다:

```xml
          <StackPanel Grid.Column="2" VerticalAlignment="Center" HorizontalAlignment="Right" Spacing="4">
            <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
              <Button Classes="ghost" Padding="8,4" ToolTip.Tip="시그널 패스">
                <TextBlock Text="{Binding PathBadge}" FontSize="11"/>
                <Button.Flyout>
                  <Flyout>
                    <StackPanel Spacing="6" MaxWidth="360">
                      <TextBlock Text="시그널 패스" FontWeight="SemiBold"/>
                      <TextBlock Text="{Binding SignalPathText}" TextWrapping="Wrap" Classes="muted"/>
                      <TextBlock Text="{Binding SyncText}" Classes="muted" FontSize="11"/>
                      <ItemsControl ItemsSource="{Binding Outputs}">
                        <ItemsControl.ItemTemplate>
                          <DataTemplate x:DataType="vm:OutputDevice">
                            <StackPanel Margin="0,6,0,0">
                              <TextBlock Text="{Binding Name}" FontWeight="SemiBold" FontSize="12"/>
                              <TextBlock Text="{Binding Detail}" Classes="muted" FontSize="11" TextWrapping="Wrap"/>
                            </StackPanel>
                          </DataTemplate>
                        </ItemsControl.ItemTemplate>
                      </ItemsControl>
                    </StackPanel>
                  </Flyout>
                </Button.Flyout>
              </Button>

              <Button Classes="ghost" Padding="8,4" ToolTip.Tip="출력 존/장치">
                <TextBlock Text="{Binding OutputChip}" FontSize="11" MaxWidth="150" TextTrimming="CharacterEllipsis"/>
                <Button.Flyout>
                  <Flyout>
                    <StackPanel Spacing="4" MinWidth="260">
                      <TextBlock Text="출력 장치" FontWeight="SemiBold"/>
                      <ItemsControl ItemsSource="{Binding Outputs}">
                        <ItemsControl.ItemTemplate>
                          <DataTemplate x:DataType="vm:OutputDevice">
                            <Button Classes="ghost" HorizontalAlignment="Stretch" HorizontalContentAlignment="Left"
                                    Margin="0,2,0,0"
                                    Command="{Binding $parent[Window].((vm:MainViewModel)DataContext).SelectOutputCommand}"
                                    CommandParameter="{Binding}">
                              <StackPanel>
                                <TextBlock Text="{Binding Name}" FontWeight="SemiBold" FontSize="12"/>
                                <TextBlock Text="{Binding Detail}" Classes="muted" FontSize="11"/>
                              </StackPanel>
                            </Button>
                          </DataTemplate>
                        </ItemsControl.ItemTemplate>
                      </ItemsControl>
                      <Button Classes="primary" Content="이 PC에 출력 연결" Margin="0,8,0,0"
                              HorizontalAlignment="Stretch"
                              Command="{Binding ConnectOutputCommand}"/>
                    </StackPanel>
                  </Flyout>
                </Button.Flyout>
              </Button>
            </StackPanel>

            <StackPanel Orientation="Horizontal" Spacing="6" HorizontalAlignment="Right"
                        ToolTip.Tip="{Binding VolumeBlockedReason}">
              <TextBlock Text="{Binding Volume, StringFormat='{}{0}%'}" Classes="muted" FontSize="11"
                         VerticalAlignment="Center" Width="34" TextAlignment="Right"/>
              <Slider Width="110" Minimum="0" Maximum="100"
                      Value="{Binding Volume, Mode=TwoWay}"
                      IsEnabled="{Binding VolumeEnabled}"
                      PointerCaptureLost="VolumeReleased"/>
            </StackPanel>

            <TextBlock Text="{Binding RoomChip}" Classes="muted" FontSize="11" HorizontalAlignment="Right"/>
          </StackPanel>
```

- [ ] **Step 5: 볼륨 커밋 핸들러를 더한다**

`MainWindow.axaml.cs`의 `OnSeeked` 아래:

```csharp
    private void VolumeReleased(object? sender, PointerCaptureLostEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            _ = vm.ApplyVolumeCommand.ExecuteAsync(null);
    }
```

`using Avalonia.Input;`가 필요하다 — 지웠다면 되돌린다.

- [ ] **Step 6: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: 하단 우측에 시그널 패스 칩·출력 칩·볼륨이 뜬다 · 「출력 연결」 후 장치가 목록에 나타난다 ·
볼륨을 움직이고 놓으면 반영된다 · Audiophile 룸에서는 슬라이더가 비활성이고 툴팁에 사유가 뜬다.

- [ ] **Step 7: 커밋**

```bash
git add src/Mono.Control/
git commit -m "Add volume, output picker and signal path to the player bar

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: 존 목록을 장치 선택기에 합친다

스펙은 "스냅샷의 `outputs`와 `list_zones` 응답을 합쳐 현재 출력 대상을 전환"하라고 한다.
존은 룸 스냅샷에 실리지 않으므로 별도 조회다.

**Files:**
- Create: `src/Mono.Control/ViewModels/ZoneItem.cs`
- Modify: `src/Mono.Control/ViewModels/MainViewModel.cs`
- Modify: `src/Mono.Control/ViewModels/MainViewModel.Messages.cs`
- Modify: `src/Mono.Control/Views/MainWindow.axaml`

**Interfaces:**
- Consumes: `CoreSession.ListZonesAsync` (1단계) · `MessageTypes.ListZones` 응답 본문.
- Produces: `Mono.Control.ViewModels.ZoneItem` (`Id` `Name` `Mode` `ModeLabel` `MemberSummary`) ·
  `MainViewModel.Zones` (`ObservableCollection<ZoneItem>`).

- [ ] **Step 1: 존 모델을 만든다**

`src/Mono.Control/ViewModels/ZoneItem.cs` — Core `ZonesMessage`의 형태를 그대로 받는다:

```csharp
using System.Text.Json.Serialization;
using Mono.Shared;

namespace Mono.Control.ViewModels;

/// <summary>list_zones 응답 한 건. 멀티 디바이스 존은 싱글 플레이 전용이다.</summary>
public sealed class ZoneItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("mode")] public ZoneMode Mode { get; set; }
    [JsonPropertyName("syncRoomId")] public string? SyncRoomId { get; set; }
    [JsonPropertyName("members")] public List<ZoneMember> Members { get; set; } = [];

    public string ModeLabel => Mode == ZoneMode.Sync ? "동시 재생" : "기기별 독립";

    public string MemberSummary => Members.Count == 0
        ? "기기 없음"
        : $"{Members.Count}대 · 온라인 {Members.Count(m => m.Online)}대";
}

public sealed class ZoneMember
{
    [JsonPropertyName("peerId")] public string PeerId { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("online")] public bool Online { get; set; }
    [JsonPropertyName("roomId")] public string? RoomId { get; set; }
}
```

- [ ] **Step 2: 뷰모델에 목록을 더한다**

`MainViewModel.cs`의 `public ObservableCollection<OutputDevice> Outputs { get; } = new();` 아래:

```csharp
    public ObservableCollection<ZoneItem> Zones { get; } = new();
```

- [ ] **Step 3: `list_zones` 응답을 받는다**

`MainViewModel.Messages.cs`의 `HandleMessage` switch에 케이스를 더한다:

```csharp
            case MessageTypes.ListZones:
                LoadZones(msg.Body);
                break;
```

같은 파일에 메서드를 더한다:

```csharp
    private void LoadZones(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) { Zones.Clear(); return; }
        try
        {
            var list = JsonSerializer.Deserialize<List<ZoneItem>>(body, Json) ?? [];
            Zones.Clear();
            foreach (var z in list) Zones.Add(z);
        }
        catch { /* 형식이 어긋나면 이전 목록을 유지한다 */ }
    }
```

`MainViewModel.Messages.cs` 상단에 `using Mono.Control.ViewModels;`는 필요 없다 — 같은 네임스페이스다.

- [ ] **Step 4: 하단 바가 열릴 때 존을 조회한다**

`MainViewModel.cs`의 `SelectOutputAsync` 아래에 더한다:

```csharp
    /// <summary>출력 선택기를 열 때 존 목록을 새로 받는다 — 존은 룸 스냅샷에 실리지 않는다.</summary>
    [RelayCommand]
    private Task RefreshZonesAsync() => Safe(() => _session.ListZonesAsync());
```

Task 5 Step 4에서 만든 출력 칩 `Button`에 `Click` 대신 Flyout 열림을 쓰기 어려우므로,
칩 버튼에 `Command="{Binding RefreshZonesCommand}"`를 더한다. Flyout은 그대로 열린다.

- [ ] **Step 5: 장치 플라이아웃에 존 구역을 더한다**

Task 5 Step 4의 출력 플라이아웃 `StackPanel` 안, 「이 PC에 출력 연결」 버튼 **위에** 넣는다:

```xml
                      <TextBlock Text="존" FontWeight="SemiBold" Margin="0,12,0,0"
                                 IsVisible="{Binding Zones.Count}"/>
                      <ItemsControl ItemsSource="{Binding Zones}">
                        <ItemsControl.ItemTemplate>
                          <DataTemplate x:DataType="vm:ZoneItem">
                            <Border Classes="panel" CornerRadius="8" Padding="8" Margin="0,2,0,0">
                              <StackPanel>
                                <TextBlock Text="{Binding Name}" FontWeight="SemiBold" FontSize="12"/>
                                <TextBlock Classes="muted" FontSize="11">
                                  <Run Text="{Binding ModeLabel}"/><Run Text=" · "/><Run Text="{Binding MemberSummary}"/>
                                </TextBlock>
                              </StackPanel>
                            </Border>
                          </DataTemplate>
                        </ItemsControl.ItemTemplate>
                      </ItemsControl>
```

존을 **만들고 편집하는** UI는 5단계(존 관리)다. 여기서는 하단 바에서 현재 존 구성을 보는 것까지다.

- [ ] **Step 6: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: Audio 화면에서 존을 만든 뒤 하단 출력 칩을 누르면 플라이아웃에 존이 모드·기기 수와 함께 뜬다.

- [ ] **Step 7: 커밋**

```bash
git add src/Mono.Control/
git commit -m "Show zones alongside outputs in the player bar picker

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: ♥ 반응과 큐 드로어

**Files:**
- Modify: `src/Mono.Control/ViewModels/MainViewModel.cs`
- Modify: `src/Mono.Control/ViewModels/MainViewModel.Messages.cs`
- Modify: `src/Mono.Control/Views/MainWindow.axaml`

**Interfaces:**
- Consumes: `CoreSession.ReactAsync` · `Lounge.QueueTracks` · `CoreSession.RemoveQueueAsync` · `JumpToAsync` (1단계).
- Produces: `MainViewModel.HeartCount` · `.HeartsLeft` · `.CanReact` · `.ReactBlockedReason` · `.ShowQueue` · `HeartCommand` · `ToggleQueueCommand` · `RemoveFromQueueCommand` · `JumpToQueueCommand`.

- [ ] **Step 1: 뷰모델에 상태를 더한다**

`MainViewModel.cs`의 `_outputChip` 아래:

```csharp
    [ObservableProperty] private int _heartCount;
    [ObservableProperty] private int _heartsLeft = 3;
    [ObservableProperty] private bool _showQueue;

    /// <summary>Core의 MaxReactionsPerUserPerTrack 과 같은 값이어야 한다.</summary>
    private const int MaxReactionsPerTrack = 3;

    public bool CanReact => HeartsLeft > 0 && CurrentSnapshot?.CurrentTrack is not null;

    public string ReactBlockedReason => CurrentSnapshot?.CurrentTrack is null
        ? "재생 중인 곡이 없습니다"
        : HeartsLeft > 0 ? $"♥ 남은 횟수 {HeartsLeft}회" : "이 곡에는 이미 3번 반응했습니다";

    partial void OnHeartsLeftChanged(int value)
    {
        OnPropertyChanged(nameof(CanReact));
        OnPropertyChanged(nameof(ReactBlockedReason));
    }

    /// <summary>하단 바의 ♥ — 지금 재생 위치에 하트 반응을 남긴다. 토글이 아니다.</summary>
    [RelayCommand]
    private Task HeartAsync() => Safe(() => _session.ReactAsync("❤️"));

    [RelayCommand]
    private void ToggleQueue() => ShowQueue = !ShowQueue;

    [RelayCommand]
    private Task RemoveFromQueueAsync(CatalogTrack? track)
    {
        var i = track is null ? -1 : Lounge.QueueTracks.IndexOf(track);
        return i < 0 ? Task.CompletedTask : Safe(() => _session.RemoveQueueAsync(i));
    }

    [RelayCommand]
    private Task JumpToQueueAsync(CatalogTrack? track)
    {
        var i = track is null ? -1 : Lounge.QueueTracks.IndexOf(track);
        return i < 0 ? Task.CompletedTask : Safe(() => _session.JumpToAsync(i));
    }
```

- [ ] **Step 2: 스냅샷에서 하트 수를 센다**

`MainViewModel.Messages.cs`의 `ApplyRoomState`에서 `SeekingAllowed = snap.SeekingAllowed;` 아래:

```csharp
        HeartCount = snap.ReactionCounts.GetValueOrDefault("❤️");
        var mine = snap.CurrentTrack is null
            ? 0
            : snap.Reactions.Count(r => r.PeerId == _session.PeerId && r.TrackId == snap.CurrentTrack.Id);
        HeartsLeft = Math.Max(0, MaxReactionsPerTrack - mine);
        OnPropertyChanged(nameof(CanReact));
        OnPropertyChanged(nameof(ReactBlockedReason));
```

- [ ] **Step 3: 하단 바 좌측에 ♥를, 중앙에 큐 토글을 더한다**

`MainWindow.axaml`에서 좌측 `Button`(아트·제목) 을 감싼 `StackPanel Grid.Column="0"` 뒤, 같은 셀 안에 ♥를 붙인다.
좌측 셀의 `<Button Classes="ghost" ... OpenNowPlayingCommand>` 를 아래 `Grid`로 감싼다:

```xml
          <Grid ColumnDefinitions="*,Auto">
            <Button Classes="ghost" Background="Transparent" BorderThickness="0" HorizontalContentAlignment="Left"
                    Command="{Binding OpenNowPlayingCommand}">
              <!-- 기존 아트·제목·아티스트 StackPanel 을 그대로 둔다 -->
            </Button>
            <Button Grid.Column="1" Classes="ghost" Padding="8,6" VerticalAlignment="Center"
                    Command="{Binding HeartCommand}"
                    IsEnabled="{Binding CanReact}"
                    ToolTip.Tip="{Binding ReactBlockedReason}">
              <StackPanel Orientation="Horizontal" Spacing="4">
                <TextBlock Text="♥" FontSize="14"/>
                <TextBlock Text="{Binding HeartCount}" FontSize="11" VerticalAlignment="Center"/>
              </StackPanel>
            </Button>
          </Grid>
```

트랜스포트 버튼 줄(prev/play/next)의 `next` 뒤에 큐 토글을 더한다:

```xml
              <Button Classes="ghost" Command="{Binding ToggleQueueCommand}" ToolTip.Tip="큐" Padding="12,8">
                <TextBlock Text="큐" VerticalAlignment="Center"/>
              </Button>
```

- [ ] **Step 4: 큐 드로어를 더한다**

`MainWindow.axaml` 최상위 `<Grid>` 안, 몰입 Now Playing `<Border>` **앞에** 넣는다(온보딩·몰입이 위에 오도록):

```xml
    <Border IsVisible="{Binding ShowQueue}" Background="{DynamicResource Mono.Panel}"
            HorizontalAlignment="Right" Width="360" Margin="0,0,0,92"
            BorderBrush="{DynamicResource Mono.Border}" BorderThickness="1,0,0,1">
      <DockPanel Margin="16">
        <DockPanel DockPanel.Dock="Top">
          <Button DockPanel.Dock="Right" Classes="ghost" Content="닫기" Command="{Binding ToggleQueueCommand}"/>
          <TextBlock Text="재생 큐" FontWeight="SemiBold" VerticalAlignment="Center"/>
        </DockPanel>
        <ScrollViewer Margin="0,12,0,0">
          <ItemsControl ItemsSource="{Binding Lounge.QueueTracks}">
            <ItemsControl.ItemTemplate>
              <DataTemplate x:DataType="models:CatalogTrack">
                <Border Classes="trackcard" Margin="0,0,0,6" Padding="10">
                  <DockPanel>
                    <Button DockPanel.Dock="Right" Classes="ghost" Content="✕" Padding="8,4"
                            ToolTip.Tip="큐에서 제거"
                            Command="{Binding $parent[Window].((vm:MainViewModel)DataContext).RemoveFromQueueCommand}"
                            CommandParameter="{Binding}"/>
                    <Button Classes="ghost" Background="Transparent" BorderThickness="0"
                            HorizontalContentAlignment="Left"
                            ToolTip.Tip="이 곡으로 이동"
                            Command="{Binding $parent[Window].((vm:MainViewModel)DataContext).JumpToQueueCommand}"
                            CommandParameter="{Binding}">
                      <StackPanel>
                        <TextBlock Text="{Binding Title}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                        <TextBlock Text="{Binding Subtitle}" Classes="muted" FontSize="11" TextTrimming="CharacterEllipsis"/>
                      </StackPanel>
                    </Button>
                  </DockPanel>
                </Border>
              </DataTemplate>
            </ItemsControl.ItemTemplate>
          </ItemsControl>
        </ScrollViewer>
      </DockPanel>
    </Border>
```

- [ ] **Step 5: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: 라운지에서 룸을 만들고 곡을 큐에 넣은 뒤 재생 → ♥를 세 번 누르면 카운트가 오르고 네 번째는
비활성 + "이 곡에는 이미 3번 반응했습니다" 툴팁 · 「큐」 버튼으로 드로어가 열리고 ✕ 로 제거,
곡을 누르면 그 곡으로 이동 · 하트를 남긴 위치가 시크바 히트맵에 나타난다.

- [ ] **Step 6: 커밋**

```bash
git add src/Mono.Control/
git commit -m "Add the heart reaction and queue drawer to the player bar

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## 2단계 완료 기준

- [ ] `dotnet build Mono.slnx` 경고 0, 오류 0
- [ ] `dotnet test Mono.slnx` 70개 통과 (1단계 65 + 시크 레이어 5)
- [ ] 출력 플라이아웃에 존 목록이 모드·기기 수와 함께 보인다
- [ ] 하단 바에 문서 §3.2의 요소가 전부 있다: 아트·곡명·아티스트·**♥** / prev·play·next·**시크(+히트맵+핀)**·**큐 토글** / **시그널 패스**·**출력 존/장치**·**볼륨**·라운지 칩
- [ ] Audiophile 룸에서 볼륨 슬라이더와 시크바가 비활성이고 각각 사유 툴팁이 뜬다
- [ ] ♥ 4회째가 비활성 + 사유 툴팁
- [ ] 콘솔 창 0개

## 다음 단계

3단계(라이브러리)는 Core 확장(장르·작곡가·작품·복수 스캔 루트)이 선행되므로 별도 계획으로 쓴다.
