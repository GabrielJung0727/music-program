# Control 패리티 5단계 — 마무리 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 문서 패리티의 마지막 조각 — Now Playing 5모드, 설정 10트리, 키보드 단축키, 존 관리, 원격 페어링, 백업. 그리고 `03-구현현황.md`를 실제에 맞게 바로잡는다.

**Architecture:** 존·페어링 명령은 1단계에서 배선됐고 Now Playing에 필요한 `albumTracks`·위키는 이미 온다. 새 Core 작업은 **백업 하나뿐** — DB 파일을 zip으로 묶는 `backup` 명령이다. 설정은 새 기능을 만들기보다 **흩어진 것을 문서의 10트리로 정리**하고, 이미 전용 화면이 있는 항목(Audio·Folders)은 그 화면으로 보낸다.

**Tech Stack:** C# / .NET 8 · Avalonia 11 · CommunityToolkit.Mvvm · System.IO.Compression · xunit

**Spec:** [`docs/superpowers/specs/2026-09-05-control-full-parity-design.md`](../specs/2026-09-05-control-full-parity-design.md) §4 단계 5

**선행:** [4단계](2026-09-05-control-parity-phase4-lounge.md) 완료

## Global Constraints

- 대상 프레임워크 `net8.0`. 새 NuGet 패키지를 추가하지 않는다(`System.IO.Compression`은 BCL).
- JSON은 `LineFraming.JsonOptions` — CamelCase, null 필드는 실리지 않는다.
- 의존 방향: `Control` → `Protocol` → `Shared`. Control은 Core를 참조하지 않는다.
- `dotnet build Mono.slnx` 경고 0 유지. 기존 101개 테스트는 회귀 가드다.
- **백업에 음원 파일을 넣지 않는다** — 문서 §4.7이 명시한다. DB만 묶는다.
- 사용자 대상 문자열은 한국어.
- 커밋 메시지 말미에 `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## 확인된 현재 상태

| 항목 | 사실 |
| --- | --- |
| Now Playing | 탭 3개 — 가사(0) · 아티스트(1) · 크레딧(2). **앨범 · 연혁(위키) 없음** |
| 설정 | 표시 이름 · 라이브러리 경로 · 앱 업데이트 **3개뿐**. 문서는 10트리 |
| 단축키 | `Space` `/` `Esc` **미구현** |
| 존 | 명령은 1단계에서 배선됨(`RenameZone` `SetZoneMode` `ZoneAddMember` `ZoneRemoveMember` `DeleteZone`). **UI는 하단 바 플라이아웃의 읽기 전용 목록뿐** |
| 페어링 | `PairAsync` `RedeemAsync` 배선됨. **UI 없음** |
| 백업 | **Core에 전혀 없음.** DB는 `data/` 아래 `catalog.db` `history.db` `endpoints.db` `zones.db` |
| 스냅샷 | `AlbumTracks` 이미 실림. `WikiText`는 `wiki_bio` 응답으로 채워짐 |

---

### Task 1: 백업 명령 (Core)

**Files:**
- Modify: `src/Mono.Protocol/MessageTypes.cs`
- Create: `src/Mono.Core/BackupService.cs`
- Modify: `src/Mono.Core/Program.cs` · `src/Mono.Core/CommandProcessor.cs`
- Test: `tests/Mono.Tests/BackupTests.cs`

**Interfaces:**
- Produces:
  - `MessageTypes.Backup = "backup"` — 응답 본문은 만들어진 zip의 전체 경로
  - `BackupService(string dataDir)` · `string Create()` — zip 경로를 돌려준다
  - `CommandProcessor` 생성자 끝에 `BackupService backups` 추가

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`tests/Mono.Tests/BackupTests.cs`:

```csharp
using System.IO.Compression;
using Mono.Core;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 백업은 DB만 묶는다. 음원 파일을 넣으면 수십 GB가 되고, 문서가 금지한다.
/// </summary>
public class BackupTests
{
    private static string NewDataDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"), "data");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "catalog.db"), "catalog");
        File.WriteAllText(Path.Combine(dir, "history.db"), "history");
        File.WriteAllText(Path.Combine(dir, "endpoints.db"), "endpoints");
        File.WriteAllText(Path.Combine(dir, "zones.db"), "zones");
        return dir;
    }

    [Fact]
    public void BackupContainsEveryDatabase()
    {
        var dir = NewDataDir();

        var zipPath = new BackupService(dir).Create();

        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.Name).ToHashSet();
        Assert.Contains("catalog.db", names);
        Assert.Contains("history.db", names);
        Assert.Contains("endpoints.db", names);
        Assert.Contains("zones.db", names);
    }

    [Fact]
    public void BackupLeavesOutAudioFiles()
    {
        var dir = NewDataDir();
        var library = Path.Combine(dir, "library");
        Directory.CreateDirectory(library);
        File.WriteAllText(Path.Combine(library, "song.flac"), "not really audio");

        var zipPath = new BackupService(dir).Create();

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.DoesNotContain(zip.Entries, e => e.Name.EndsWith(".flac"));
    }

    [Fact]
    public void MissingDatabaseIsSkippedNotFatal()
    {
        // 아직 한 번도 안 쓴 DB 가 있어도 백업은 되어야 한다.
        var dir = NewDataDir();
        File.Delete(Path.Combine(dir, "zones.db"));

        var zipPath = new BackupService(dir).Create();

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, e => e.Name == "catalog.db");
        Assert.DoesNotContain(zip.Entries, e => e.Name == "zones.db");
    }

    [Fact]
    public void EachBackupGetsItsOwnFile()
    {
        var dir = NewDataDir();
        var service = new BackupService(dir);

        var first = service.Create();
        Thread.Sleep(1100);
        var second = service.Create();

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter BackupTests
```

기대: 컴파일 실패 — `BackupService`가 없다.

- [ ] **Step 3: 최소 구현**

`src/Mono.Core/BackupService.cs`:

```csharp
using System.IO.Compression;

namespace Mono.Core;

/// <summary>
/// 카탈로그·히스토리·기기·존 DB만 zip 으로 묶는다.
/// 음원 파일은 넣지 않는다 — 원본은 사용자 폴더에 있고, 백업이 수십 GB 가 될 이유가 없다.
/// </summary>
public sealed class BackupService
{
    private static readonly string[] Databases =
        ["catalog.db", "history.db", "endpoints.db", "zones.db"];

    private readonly string _dataDir;

    public BackupService(string dataDir) => _dataDir = dataDir;

    /// <summary>새 백업을 만들고 zip 경로를 돌려준다.</summary>
    public string Create()
    {
        var outDir = Path.Combine(_dataDir, "backups");
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, $"mono-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var db in Databases)
        {
            var source = Path.Combine(_dataDir, db);
            if (!File.Exists(source)) continue;

            // SQLite 가 열어 둔 파일이라 공유 읽기로 복사한다.
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var entry = zip.CreateEntry(db, CompressionLevel.Optimal).Open();
            input.CopyTo(entry);
        }

        return path;
    }
}
```

`src/Mono.Protocol/MessageTypes.cs`의 `Album` 줄 아래:

```csharp
    public const string Backup = "backup";
```

`src/Mono.Core/Program.cs` — `CommandProcessor` 등록의 `libraryRoots` 앞에 인자를 하나 더한다.
먼저 서비스를 만든다(`builder.Services.AddSingleton(new ZoneRegistry(...))` 아래):

```csharp
builder.Services.AddSingleton(new BackupService(data));
```

그리고 `new CommandProcessor(...)` 인자 목록의 `sp.GetRequiredService<WikipediaService>(),` 아래에:

```csharp
    sp.GetRequiredService<BackupService>(),
```

`src/Mono.Core/CommandProcessor.cs` — 생성자 파라미터를 `WikipediaService wiki,` 다음에 더한다:

```csharp
        BackupService backups,
```

필드와 대입:

```csharp
    private readonly BackupService _backups;
```

```csharp
        _backups = backups;
```

`case MessageTypes.Album:` 위에 케이스를 더한다:

```csharp
            case MessageTypes.Backup:
                return Direct(new MonoMessage
                {
                    Type = MessageTypes.Backup,
                    Ok = true,
                    Body = _backups.Create()
                });

```

- [ ] **Step 4: 통과를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter BackupTests
dotnet build Mono.slnx -v q --nologo
```

기대: 백업 4개 통과, 경고 0.

- [ ] **Step 5: 커밋**

```bash
git add src/ tests/
git commit -m "Back up the databases, leaving audio files alone

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Now Playing 다섯 모드

**Files:**
- Modify: `src/Mono.Control/ViewModels/MainViewModel.cs`
- Modify: `src/Mono.Control/ViewModels/MainViewModel.Messages.cs`
- Modify: `src/Mono.Control/Views/MainWindow.axaml`

**Interfaces:**
- Consumes: `RoomSnapshot.AlbumTracks` (1단계) · `MainViewModel.WikiText`.
- Produces: `MainViewModel.IsNpAlbum` · `.IsNpWiki` · `.AlbumTracks` (`ObservableCollection<CatalogTrack>`)

- [ ] **Step 1: 뷰모델에 두 모드를 더한다**

`MainViewModel.cs` 의 `IsNpCredits` 아래:

```csharp
    public bool IsNpAlbum => NowPlayingTab == 3;
    public bool IsNpWiki => NowPlayingTab == 4;
```

`OnNowPlayingTabChanged` 의 알림 묶음에 두 줄을 더한다:

```csharp
        OnPropertyChanged(nameof(IsNpAlbum));
        OnPropertyChanged(nameof(IsNpWiki));
```

컬렉션을 더한다 — `AutoplayChoices` 아래:

```csharp
    /// <summary>몰입 모드의 앨범 탭. 지금 곡이 실린 앨범의 수록곡이다.</summary>
    public ObservableCollection<CatalogTrack> AlbumTracks { get; } = new();
```

- [ ] **Step 2: 스냅샷에서 채운다**

`MainViewModel.Messages.cs` 의 `ApplyRoomState` 에서 `Lounge.CurrentArtistId = ...` 앞에:

```csharp
        AlbumTracks.Clear();
        foreach (var t in snap.AlbumTracks)
        {
            AlbumTracks.Add(new CatalogTrack
            {
                Id = t.Id,
                Title = t.Title,
                Artist = t.ArtistName,
                DurationMs = t.DurationMs,
                Badge = t.Badge
            });
        }
```

- [ ] **Step 3: 탭 버튼 두 개를 더한다**

`MainWindow.axaml` 의 크레딧 버튼(`CommandParameter="2"`) 블록 뒤에:

```xml
              <Button Classes="ghost" Command="{Binding SetNowPlayingTabCommand}" CommandParameter="3">
                <TextBlock Text="앨범" VerticalAlignment="Center" Foreground="White"/>
              </Button>
              <Button Classes="ghost" Command="{Binding SetNowPlayingTabCommand}" CommandParameter="4">
                <TextBlock Text="연혁" VerticalAlignment="Center" Foreground="White"/>
              </Button>
```

- [ ] **Step 4: 두 패널을 더한다**

크레딧 `ScrollViewer` 뒤에:

```xml
              <ScrollViewer MaxHeight="360" IsVisible="{Binding IsNpAlbum}">
                <ItemsControl ItemsSource="{Binding AlbumTracks}">
                  <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="models:CatalogTrack">
                      <Grid ColumnDefinitions="*,70" Margin="0,0,0,4">
                        <TextBlock Text="{Binding Title}" Foreground="White"
                                   TextTrimming="CharacterEllipsis"/>
                        <TextBlock Grid.Column="1" Text="{Binding DurationText}" Foreground="#CCFFFFFF"
                                   HorizontalAlignment="Right"/>
                      </Grid>
                    </DataTemplate>
                  </ItemsControl.ItemTemplate>
                </ItemsControl>
              </ScrollViewer>

              <ScrollViewer MaxHeight="360" IsVisible="{Binding IsNpWiki}">
                <StackPanel Spacing="6">
                  <TextBlock Text="{Binding WikiText}" Foreground="White" TextWrapping="Wrap"/>
                  <TextBlock Classes="muted" FontSize="11" Foreground="#99FFFFFF" TextWrapping="Wrap"
                             Text="위키백과 요약 — 한국어가 없으면 영어로 대체합니다."/>
                </StackPanel>
              </ScrollViewer>
```

- [ ] **Step 5: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: 하단 아트를 눌러 몰입 모드로 들어가면 탭이 **가사 · 아티스트 · 크레딧 · 앨범 · 연혁** 다섯 개다.
재생 중 앨범 탭에 수록곡이, 연혁 탭에 위키 요약이 보인다.

- [ ] **Step 6: 커밋**

```bash
git add src/Mono.Control/
git commit -m "Add the album and history modes to Now Playing

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: 키보드 단축키

**Files:**
- Modify: `src/Mono.Control/Views/MainWindow.axaml`
- Modify: `src/Mono.Control/Views/MainWindow.axaml.cs`

**Interfaces:**
- Consumes: `MainViewModel.PlayPauseCommand` · `.ShowNowPlaying` · `.ShowQueue` · `Library.SelectedAlbum` · `.ShowOnboarding`.
- Produces: `MainWindow.OnKeyDown` 처리 — `Space` 재생/일시정지 · `/` 검색 포커스 · `Esc` 최상단 오버레이 닫기.

- [ ] **Step 1: 검색 상자에 이름을 준다**

`MainWindow.axaml` 의 검색 `TextBox` 에 `x:Name="SearchBox"` 를 더한다:

```xml
          <TextBox Grid.Row="1" x:Name="SearchBox"
                   Watermark="트랙 · 앨범 · 아티스트 검색 (다국어 별칭)"
```

`Window` 여는 태그에 핸들러를 건다:

```xml
        KeyDown="OnKeyDown"
```

- [ ] **Step 2: 핸들러를 쓴다**

`MainWindow.axaml.cs` 에:

```csharp
    /// <summary>
    /// 문서 §6 단축키. 글자를 입력 중일 때는 Space·/ 를 가로채지 않는다 —
    /// 채팅이나 검색을 치다가 재생이 멈추면 안 된다.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var typing = FocusManager?.GetFocusedElement() is TextBox;

        switch (e.Key)
        {
            case Key.Space when !typing:
                _ = vm.PlayPauseCommand.ExecuteAsync(null);
                e.Handled = true;
                break;

            case Key.Escape:
                // 위에 덮인 것부터 닫는다.
                if (vm.ShowOnboarding) { /* 온보딩은 완료해야 닫힌다 */ }
                else if (vm.Library.SelectedAlbum is not null) vm.Library.CloseAlbumCommand.Execute(null);
                else if (vm.ShowNowPlaying) vm.CloseNowPlayingCommand.Execute(null);
                else if (vm.ShowQueue) vm.ToggleQueueCommand.Execute(null);
                else break;
                e.Handled = true;
                break;

            case Key.OemQuestion when !typing:
            case Key.Divide when !typing:
                SearchBox.Focus();
                e.Handled = true;
                break;
        }
    }
```

`using Avalonia.Input;` 이 있는지 확인한다. `CloseNowPlaying` 이 `[RelayCommand]` 인지 확인하고,
아니면 `vm.ShowNowPlaying = false;` 로 직접 바꾼다:

```bash
grep -n 'CloseNowPlaying\|ToggleQueue' src/Mono.Control/ViewModels/MainViewModel.cs
```

- [ ] **Step 3: 설정에 단축키 표를 넣을 준비**

Task 4의 Shortcuts 섹션에서 쓰므로 여기서는 코드만 끝낸다.

- [ ] **Step 4: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: `Space` 로 재생/일시정지 · `/` 로 검색창 포커스 · 몰입 모드에서 `Esc` 로 닫기 ·
**검색창에 타이핑 중 스페이스가 재생을 건드리지 않는다.**

- [ ] **Step 5: 커밋**

```bash
git add src/Mono.Control/
git commit -m "Add the Space, slash and Escape shortcuts

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: 설정 10트리

**Files:**
- Modify: `src/Mono.Control/ViewModels/MainViewModel.cs`
- Modify: `src/Mono.Control/ViewModels/MainViewModel.Messages.cs`
- Modify: `src/Mono.Control/Services/CoreSession.Library.cs`
- Rewrite: `src/Mono.Control/Views/Pages/SettingsPage.axaml`

**Interfaces:**
- Consumes: 기존 `DisplayName` `LibraryPath` `DarkTheme` `CloseToTray` `AppVersion` `UpdateStatus` `UpdateReady`
  `ScanLibraryCommand` `LinkTidalCommand` `LinkQobuzCommand` `ConnectOutputCommand`.
- Produces: `CoreSession.BackupAsync()` · `MainViewModel.BackupPath` · `BackupCommand` ·
  `GoToPageCommand(string navId)` — 설정에서 전용 화면으로 보낸다.

- [ ] **Step 1: 백업 명령과 페이지 이동을 더한다**

`src/Mono.Control/Services/CoreSession.Library.cs` 의 `AlbumAsync` 아래:

```csharp
    /// <summary>DB 백업. 음원 파일은 포함되지 않는다.</summary>
    public Task BackupAsync() => SendAsync(new MonoMessage { Type = MessageTypes.Backup });
```

`MainViewModel.cs` 에:

```csharp
    [ObservableProperty] private string _backupPath = "";

    [RelayCommand]
    private Task BackupAsync() => Safe(() => _session.BackupAsync());

    /// <summary>설정에서 전용 화면으로 보낸다 — 같은 내용을 두 곳에 만들지 않는다.</summary>
    [RelayCommand]
    private void GoToPage(string? navId)
    {
        var item = NavItems.FirstOrDefault(n => n.Id == navId);
        if (item is not null) SelectedNav = item;
    }
```

`MainViewModel.Messages.cs` 의 switch 에:

```csharp
            case MessageTypes.Backup:
                BackupPath = msg.Body ?? "";
                StatusText = string.IsNullOrEmpty(BackupPath) ? "백업 실패" : "백업 완료";
                break;
```

- [ ] **Step 2: 설정 화면을 10트리로 다시 쓴다**

`src/Mono.Control/Views/Pages/SettingsPage.axaml` 전체:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Mono.Control.ViewModels"
             x:Class="Mono.Control.Views.Pages.SettingsPage"
             x:DataType="vm:MainViewModel">
  <ScrollViewer>
    <StackPanel Spacing="18" MaxWidth="620" HorizontalAlignment="Left">

      <StackPanel Spacing="6">
        <TextBlock Text="General" FontWeight="SemiBold"/>
        <TextBlock Text="표시 이름" Classes="muted" FontSize="11"/>
        <TextBox Text="{Binding DisplayName}"/>
        <CheckBox Content="창을 닫아도 트레이에 남기기" IsChecked="{Binding CloseToTray}"/>
        <CheckBox Content="다크 테마" IsChecked="{Binding DarkTheme}"/>
      </StackPanel>

      <StackPanel Spacing="6">
        <TextBlock Text="Storage" FontWeight="SemiBold"/>
        <TextBlock Text="라이브러리 경로" Classes="muted" FontSize="11"/>
        <DockPanel>
          <Button DockPanel.Dock="Right" Classes="ghost" Content="폴더…" Margin="4,0,0,0"
                  Click="PickLibraryClick"/>
          <TextBox Text="{Binding LibraryPath}"/>
        </DockPanel>
        <StackPanel Orientation="Horizontal" Spacing="6">
          <Button Classes="ghost" Content="라이브러리 스캔" Command="{Binding ScanLibraryCommand}"/>
          <Button Classes="ghost" Content="폴더 목록 보기"
                  Command="{Binding GoToPageCommand}" CommandParameter="folders"/>
        </StackPanel>
        <TextBlock Classes="muted" FontSize="11" TextWrapping="Wrap"
                   Text="Mono 는 스캔한 폴더의 원본 파일을 수정하지 않습니다."/>
      </StackPanel>

      <StackPanel Spacing="6">
        <TextBlock Text="Services" FontWeight="SemiBold"/>
        <StackPanel Orientation="Horizontal" Spacing="6">
          <Button Classes="ghost" Content="Tidal 연동" Command="{Binding LinkTidalCommand}"/>
          <Button Classes="ghost" Content="Qobuz 연동" Command="{Binding LinkQobuzCommand}"/>
        </StackPanel>
        <TextBlock Classes="muted" FontSize="11" TextWrapping="Wrap"
                   Text="토큰은 Core 만 보관합니다. 파트너 키가 없으면 데모 카탈로그로 연결됩니다."/>
      </StackPanel>

      <StackPanel Spacing="6">
        <TextBlock Text="Audio" FontWeight="SemiBold"/>
        <StackPanel Orientation="Horizontal" Spacing="6">
          <Button Classes="primary" Content="이 PC에 출력 연결" Command="{Binding ConnectOutputCommand}"/>
          <Button Classes="ghost" Content="장치 · 존 설정"
                  Command="{Binding GoToPageCommand}" CommandParameter="devices"/>
        </StackPanel>
      </StackPanel>

      <StackPanel Spacing="6">
        <TextBlock Text="Library" FontWeight="SemiBold"/>
        <TextBlock Classes="muted" FontSize="11" TextWrapping="Wrap"
                   Text="같은 앨범의 로컬·스트리밍 버전은 하나로 병합해 표시합니다. 검색은 아티스트 별칭 테이블을 거치므로 한국어 표기로 원어 아티스트를 찾을 수 있습니다."/>
        <Button Classes="ghost" Content="작곡가 보기" HorizontalAlignment="Left"
                Command="{Binding GoToPageCommand}" CommandParameter="composers"/>
      </StackPanel>

      <StackPanel Spacing="6">
        <TextBlock Text="Lounge" FontWeight="SemiBold"/>
        <TextBlock Classes="muted" FontSize="11" TextWrapping="Wrap"
                   Text="룸 모드·초대·권한·보존 정책은 라운지 화면의 「룸 운영」 패널에서 바꿉니다."/>
        <Button Classes="ghost" Content="라운지 열기" HorizontalAlignment="Left"
                Command="{Binding GoToPageCommand}" CommandParameter="lounge"/>
      </StackPanel>

      <StackPanel Spacing="6">
        <TextBlock Text="DSP" FontWeight="SemiBold"/>
        <TextBlock Classes="muted" FontSize="11" TextWrapping="Wrap"
                   Text="Easy EQ · 크로스피드 · 룸 IR · 스피커 보정은 Audio 화면에 있습니다. Audiophile 룸에서는 DSP 가 꺼진 채 잠깁니다."/>
        <Button Classes="ghost" Content="DSP 설정" HorizontalAlignment="Left"
                Command="{Binding GoToPageCommand}" CommandParameter="devices"/>
      </StackPanel>

      <StackPanel Spacing="6">
        <TextBlock Text="Backups" FontWeight="SemiBold"/>
        <TextBlock Classes="muted" FontSize="11" TextWrapping="Wrap"
                   Text="카탈로그·히스토리·기기·존 DB 만 묶습니다. 음원 파일은 포함되지 않습니다."/>
        <Button Classes="ghost" Content="지금 백업" HorizontalAlignment="Left"
                Command="{Binding BackupCommand}"/>
        <TextBlock Text="{Binding BackupPath}" Classes="muted" FontSize="11" TextWrapping="Wrap"/>
      </StackPanel>

      <StackPanel Spacing="6">
        <TextBlock Text="Shortcuts" FontWeight="SemiBold"/>
        <Grid ColumnDefinitions="110,*" RowDefinitions="Auto,Auto,Auto">
          <TextBlock Text="Space" FontFamily="Consolas"/>
          <TextBlock Grid.Column="1" Text="재생 / 일시정지" Classes="muted"/>
          <TextBlock Grid.Row="1" Text="/" FontFamily="Consolas"/>
          <TextBlock Grid.Row="1" Grid.Column="1" Text="검색창으로 이동" Classes="muted"/>
          <TextBlock Grid.Row="2" Text="Esc" FontFamily="Consolas"/>
          <TextBlock Grid.Row="2" Grid.Column="1" Text="몰입 모드 · 큐 · 앨범 창 닫기" Classes="muted"/>
        </Grid>
      </StackPanel>

      <StackPanel Spacing="6">
        <TextBlock Text="About" FontWeight="SemiBold"/>
        <TextBlock Text="{Binding AppVersion, StringFormat=mono {0}}" Classes="muted"/>
        <TextBlock Classes="muted" FontSize="11" TextWrapping="Wrap"
                   Text="포트 7700 Control↔Core · 7701 MATP. 방화벽에서 두 포트를 허용해야 다른 기기가 붙습니다."/>
        <StackPanel Orientation="Horizontal" Spacing="6">
          <Button Classes="ghost" Content="{Binding UpdateButtonLabel}"
                  Command="{Binding CheckForUpdatesCommand}" IsVisible="{Binding !UpdateReady}"/>
          <Button Classes="primary" Content="업데이트 설치" Command="{Binding ApplyUpdateCommand}"
                  IsVisible="{Binding UpdateReady}"/>
        </StackPanel>
        <ProgressBar Value="{Binding UpdateProgress}" Maximum="100" IsVisible="{Binding UpdateBusy}"/>
        <TextBlock Text="{Binding UpdateStatus}" Classes="muted" FontSize="11" TextWrapping="Wrap"/>
      </StackPanel>

    </StackPanel>
  </ScrollViewer>
</UserControl>
```

`SettingsPage.axaml.cs` 의 `PickLibraryClick` 은 그대로 둔다.

- [ ] **Step 3: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: Settings 에 10개 섹션이 순서대로 있다 · 「지금 백업」이 zip 경로를 돌려준다 ·
「폴더 목록 보기」 「장치 · 존 설정」 등이 해당 화면으로 이동한다.

- [ ] **Step 4: 커밋**

```bash
git add src/Mono.Control/
git commit -m "Lay settings out as the ten sections the UI spec defines

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: 존 관리와 원격 페어링

**Files:**
- Modify: `src/Mono.Control/ViewModels/Pages/AudioViewModel.cs`
- Modify: `src/Mono.Control/ViewModels/MainViewModel.cs` · `MainViewModel.Messages.cs`
- Modify: `src/Mono.Control/Views/Pages/AudioPage.axaml`

**Interfaces:**
- Consumes: `CoreSession.RenameZoneAsync` · `SetZoneModeAsync` · `ZoneAddMemberAsync` ·
  `ZoneRemoveMemberAsync` · `DeleteZoneAsync` · `PairAsync` · `RedeemAsync` (1단계),
  `MainViewModel.Zones` (2단계) · `.Outputs`.
- Produces: `AudioViewModel.Zones` · `.SelectedZone` · `.ZoneRenameText` · `.PairingCode` · `.RedeemCode` ·
  `RenameZoneCommand` · `SetZoneSyncCommand` · `SetZoneIndependentCommand` · `DeleteZoneCommand` ·
  `AddDeviceToZoneCommand` · `RemoveDeviceFromZoneCommand` · `IssuePairingCommand` · `RedeemPairingCommand`

- [ ] **Step 1: Audio VM에 존·페어링 상태를 더한다**

`AudioViewModel.cs` 에:

```csharp
    public ObservableCollection<ZoneItem> Zones { get; } = new();
    public ObservableCollection<OutputDevice> Outputs { get; } = new();

    [ObservableProperty] private ZoneItem? _selectedZone;

    /// <summary>존 편집 블록 표시 여부. XAML 에서 컨버터를 쓰지 않도록 bool 로 낸다.</summary>
    public bool HasSelectedZone => SelectedZone is not null;

    partial void OnSelectedZoneChanged(ZoneItem? value) => OnPropertyChanged(nameof(HasSelectedZone));
    [ObservableProperty] private string _zoneRenameText = "";
    [ObservableProperty] private string _pairingCode = "";
    [ObservableProperty] private string _redeemCode = "";

    public void ApplyZones(IEnumerable<ZoneItem> zones)
    {
        var keep = SelectedZone?.Id;
        Zones.Clear();
        foreach (var z in zones) Zones.Add(z);
        SelectedZone = Zones.FirstOrDefault(z => z.Id == keep) ?? Zones.FirstOrDefault();
    }

    public void ApplyOutputs(IEnumerable<OutputDevice> outputs)
    {
        Outputs.Clear();
        foreach (var o in outputs) Outputs.Add(o);
    }

    public void ApplyPairingCode(string code) => PairingCode = code;

    [RelayCommand]
    private Task RenameZoneAsync()
        => SelectedZone is null || string.IsNullOrWhiteSpace(ZoneRenameText)
            ? Task.CompletedTask
            : Safe(() => Session.RenameZoneAsync(SelectedZone.Id, ZoneRenameText.Trim()));

    [RelayCommand]
    private Task SetZoneSyncAsync()
        => SelectedZone is null ? Task.CompletedTask
            : Safe(() => Session.SetZoneModeAsync(SelectedZone.Id, ZoneMode.Sync));

    [RelayCommand]
    private Task SetZoneIndependentAsync()
        => SelectedZone is null ? Task.CompletedTask
            : Safe(() => Session.SetZoneModeAsync(SelectedZone.Id, ZoneMode.Independent));

    [RelayCommand]
    private Task DeleteZoneAsync()
        => SelectedZone is null ? Task.CompletedTask : Safe(() => Session.DeleteZoneAsync(SelectedZone.Id));

    [RelayCommand]
    private Task AddDeviceToZoneAsync(OutputDevice? device)
        => SelectedZone is null || device is null
            ? Task.CompletedTask
            : Safe(() => Session.ZoneAddMemberAsync(SelectedZone.Id, device.PeerId));

    [RelayCommand]
    private Task RemoveDeviceFromZoneAsync(ZoneMember? member)
        => SelectedZone is null || member is null
            ? Task.CompletedTask
            : Safe(() => Session.ZoneRemoveMemberAsync(SelectedZone.Id, member.PeerId));

    [RelayCommand]
    private Task IssuePairingAsync() => Safe(() => Session.PairAsync());

    [RelayCommand]
    private Task RedeemPairingAsync()
        => string.IsNullOrWhiteSpace(RedeemCode)
            ? Task.CompletedTask
            : Safe(() => Session.RedeemAsync(RedeemCode.Trim()));
```

상단에 `using System.Collections.ObjectModel;` 과 `using Mono.Shared;` 를 더한다.

- [ ] **Step 2: 셸이 존·출력·페어링 코드를 넘긴다**

`MainViewModel.Messages.cs` 의 `LoadZones` 끝에 한 줄:

```csharp
            Audio.ApplyZones(Zones);
```

`ApplyRoomState` 에서 `Outputs` 를 채운 뒤:

```csharp
        Audio.ApplyOutputs(Outputs);
```

switch 에 페어링 응답을 더한다:

```csharp
            case MessageTypes.PairingIssued:
                Audio.ApplyPairingCode(msg.Body ?? msg.PairingCode ?? "");
                break;
            case MessageTypes.Redeem:
                StatusText = (msg.Ok ?? false) ? "페어링 완료" : (msg.Error ?? "페어링 실패");
                break;
```

- [ ] **Step 3: Audio 화면에 존·페어링 블록을 더한다**

`AudioPage.axaml` 의 존 만들기 블록 아래에:

```xml
        <TextBlock Text="존 관리" FontWeight="SemiBold" Margin="0,12,0,0"/>
        <ComboBox ItemsSource="{Binding Zones}" SelectedItem="{Binding SelectedZone}"
                  HorizontalAlignment="Stretch">
          <ComboBox.ItemTemplate>
            <DataTemplate x:DataType="vm:ZoneItem">
              <TextBlock Text="{Binding Name}"/>
            </DataTemplate>
          </ComboBox.ItemTemplate>
        </ComboBox>
        <StackPanel IsVisible="{Binding HasSelectedZone}" Spacing="6">
          <TextBlock Classes="muted" FontSize="11">
            <Run Text="{Binding SelectedZone.ModeLabel}"/><Run Text=" · "/><Run Text="{Binding SelectedZone.MemberSummary}"/>
          </TextBlock>
          <DockPanel>
            <Button DockPanel.Dock="Right" Classes="ghost" Content="이름 변경" Margin="4,0,0,0"
                    Command="{Binding RenameZoneCommand}"/>
            <TextBox Text="{Binding ZoneRenameText}" Watermark="새 이름"/>
          </DockPanel>
          <StackPanel Orientation="Horizontal" Spacing="6">
            <Button Classes="ghost" Content="동시 재생" Command="{Binding SetZoneSyncCommand}"
                    ToolTip.Tip="존의 모든 기기가 같은 곡을 동시에 냅니다"/>
            <Button Classes="ghost" Content="기기별 독립" Command="{Binding SetZoneIndependentCommand}"
                    ToolTip.Tip="기기마다 다른 곡을 재생합니다"/>
            <Button Classes="ghost" Content="존 삭제" Command="{Binding DeleteZoneCommand}"/>
          </StackPanel>

          <TextBlock Text="존의 기기" Classes="muted" FontSize="11" Margin="0,6,0,0"/>
          <ItemsControl ItemsSource="{Binding SelectedZone.Members}">
            <ItemsControl.ItemTemplate>
              <DataTemplate x:DataType="vm:ZoneMember">
                <DockPanel Margin="0,2">
                  <Button DockPanel.Dock="Right" Classes="ghost" Content="빼기" Padding="8,2" FontSize="11"
                          Command="{Binding $parent[UserControl].((pvm:AudioViewModel)DataContext).RemoveDeviceFromZoneCommand}"
                          CommandParameter="{Binding}"/>
                  <TextBlock Text="{Binding Name}" VerticalAlignment="Center"/>
                </DockPanel>
              </DataTemplate>
            </ItemsControl.ItemTemplate>
          </ItemsControl>

          <TextBlock Text="기기 추가" Classes="muted" FontSize="11" Margin="0,6,0,0"/>
          <ItemsControl ItemsSource="{Binding Outputs}">
            <ItemsControl.ItemTemplate>
              <DataTemplate x:DataType="vm:OutputDevice">
                <Button Classes="ghost" Margin="0,2,0,0" HorizontalAlignment="Stretch"
                        HorizontalContentAlignment="Left" Content="{Binding Name}"
                        Command="{Binding $parent[UserControl].((pvm:AudioViewModel)DataContext).AddDeviceToZoneCommand}"
                        CommandParameter="{Binding}"/>
              </DataTemplate>
            </ItemsControl.ItemTemplate>
          </ItemsControl>
        </StackPanel>

        <TextBlock Text="원격 Control 페어링" FontWeight="SemiBold" Margin="0,16,0,0"/>
        <TextBlock Classes="muted" FontSize="11" TextWrapping="Wrap"
                   Text="다른 PC 의 Mono 로 이 Core 에 붙을 때 씁니다. 코드를 발급해 그쪽에 입력하세요."/>
        <StackPanel Orientation="Horizontal" Spacing="6">
          <Button Classes="ghost" Content="코드 발급" Command="{Binding IssuePairingCommand}"/>
          <TextBlock Text="{Binding PairingCode}" VerticalAlignment="Center" FontWeight="SemiBold"/>
        </StackPanel>
        <DockPanel>
          <Button DockPanel.Dock="Right" Classes="ghost" Content="입력" Margin="4,0,0,0"
                  Command="{Binding RedeemPairingCommand}"/>
          <TextBox Text="{Binding RedeemCode}" Watermark="받은 페어링 코드"/>
        </DockPanel>
```

`AudioPage.axaml` 여는 태그에 `xmlns:pvm="using:Mono.Control.ViewModels.Pages"` 가 있는지 확인한다.

- [ ] **Step 4: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: Audio 화면에서 존을 만들고 고른 뒤 이름 변경·모드 전환·기기 추가/빼기·삭제가 된다 ·
「코드 발급」이 6자리를 돌려준다.

- [ ] **Step 5: 커밋**

```bash
git add src/Mono.Control/
git commit -m "Add zone management and remote pairing to the Audio screen

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: 구현 현황 문서를 사실에 맞춘다

스펙 §8이 요구한 마무리다. 현재 `03-구현현황.md`는 GUI가 없던 기능을 ✅로 적고 있었다.

**Files:**
- Modify: `docs/03-구현현황.md`
- Modify: `docs/04-UIUX기획서.md` (§8 우선순위)

- [ ] **Step 1: 표에 열을 하나 더한다**

`03-구현현황.md`의 각 표에 **「GUI」 열**을 더한다. 판정 기준을 문서 머리에 적는다:

```markdown
✅ 동작 · 🟡 동작하되 범위 제한 · ⬜ 미구현

**「Core」는 서버 로직이 있는지, 「GUI」는 `Mono.Control.exe` 에서 사용자가 실제로 도달할 수 있는지다.**
둘을 나눈 이유: 이전 판에서 Core 에만 있고 화면이 없는 기능이 ✅ 로 적혀 사실과 어긋났다.
```

각 행의 상태를 `Core` / `GUI` 두 칸으로 나눈다. 5단계까지 끝난 시점에서 GUI 열이 ⬜ 인 항목은
남아 있지 않아야 한다 — 있다면 그대로 ⬜ 로 적는다.

- [ ] **Step 2: 실제로 확인한다**

```bash
grep -ho 'MessageTypes\.[A-Za-z]*' src/Mono.Control/Services/CoreSession*.cs | sed 's/MessageTypes\.//' | sort -u
```

이 목록과 각 기능을 대조해 GUI 열을 채운다. 추측하지 않는다.

- [ ] **Step 3: UI/UX 기획서 §8 우선순위를 갱신한다**

P0·P1·P2가 모두 취소선으로 완료 표시돼 있다. 5단계까지의 결과를 반영해 한 줄을 더한다:

```markdown
| **완료** | 1–5단계: 스냅샷 계약 · 하단 바 · 라이브러리 화면 · 라운지 · Now Playing 5모드 · 설정 10트리 · 단축키 · 존 · 페어링 · 백업 |
| **남음** | 파트너 SDK 실계정 QA · LAN 2기기 sync_probe 실측 · ASIO/네이티브 DSD 청음 · 코드 서명 인증서 |
```

- [ ] **Step 4: 커밋**

```bash
git add docs/
git commit -m "Split the status doc into Core and GUI reachability

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## 5단계 완료 기준

- [ ] `dotnet build Mono.slnx` 경고 0, 오류 0
- [ ] `dotnet test Mono.slnx` 105개 통과 (4단계 101 + 백업 4)
- [ ] Now Playing 탭이 5개 — 가사 · 아티스트 · 크레딧 · 앨범 · 연혁
- [ ] 설정에 문서 §4.7의 10섹션이 전부 있다
- [ ] `Space` `/` `Esc` 가 동작하고, **입력 중에는 Space·/ 를 가로채지 않는다**
- [ ] 존 이름 변경·모드 전환·기기 추가/빼기·삭제가 된다
- [ ] 페어링 코드 발급·입력이 된다
- [ ] 백업 zip 에 DB 4종이 들어가고 음원 파일은 없다
- [ ] `03-구현현황.md` 가 Core 와 GUI 를 분리해 적는다
