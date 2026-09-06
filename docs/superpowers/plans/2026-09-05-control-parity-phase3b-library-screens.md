# Control 패리티 3b단계 — 라이브러리 화면 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `04-UIUX기획서` §3.1 사이드바에서 빠져 있던 Composers·Compositions·Folders를 만들고, 가짜 데이터로 돌던 Genres를 실제 태그 집계로 바꾸며, History·Playlists에 전용 화면을 주고, 검색을 서버측으로 옮겨 다국어 별칭을 되살린다.

**Architecture:** 3a가 `catalog` 응답에 `genres` · `composers` · `workKey` · `workTitle`을 실어 놓았다. 새 조회 명령은 `folders` 하나뿐이고 나머지 화면은 이미 받은 카탈로그를 Control에서 묶는다. 화면별 상태는 1단계가 만든 페이지 VM 구조를 따라 `LibraryViewModel` 하나에 모은다 — Genres·Composers·Compositions·Folders는 같은 카탈로그를 다른 축으로 보는 것이라 상태를 나눌 경계가 없다.

**Tech Stack:** C# / .NET 8 · Avalonia 11 · CommunityToolkit.Mvvm · xunit

**Spec:** [`docs/superpowers/specs/2026-09-05-control-full-parity-design.md`](../specs/2026-09-05-control-full-parity-design.md) §4 단계 3 「Control 화면」

**선행:** [3a단계](2026-09-05-control-parity-phase3a-core-library.md) 완료 (커밋 `ff3bdbb`)

## Global Constraints

- 대상 프레임워크 `net8.0`. 새 NuGet 패키지를 추가하지 않는다.
- JSON은 `LineFraming.JsonOptions` — CamelCase, **null 필드는 실리지 않는다**(`WhenWritingNull`).
- 의존 방향: `Control` → `Protocol` → `Shared`. Control은 Core를 참조하지 않는다.
- `dotnet build Mono.slnx` 경고 0 유지. 기존 89개 테스트는 회귀 가드다.
- 컴파일된 바인딩(`AvaloniaUseCompiledBindingsByDefault`)이라 바인딩 경로 오류는 빌드가 잡는다.
- 사용자 대상 문자열은 한국어.
- 커밋 메시지 말미에 `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## 확인된 현재 상태

| 항목 | 사실 |
| --- | --- |
| 사이드바 | 12개 — home genres qobuz tidal lounge history albums artists tracks playlists devices settings. **composers · compositions · folders 없음** |
| Genres | `GenreTiles` 7개가 **생성자에 하드코딩**. `FilterByGenre`가 아티스트명에 `Coltrane\|Miles\|Brubeck\|Hiromi` 포함 여부로 Jazz 판정 |
| History · Playlists | nav만 있고 본문은 `IsLibraryGrid`로 트랙 그리드를 재활용. 응답은 받지만 **화면에 쓰이지 않는다** |
| 검색 | `OnSearchTextChanged` → `ApplyFilter()` — 클라이언트 `Contains()` 필터. 서버 `search`는 호출되지 않는다 |
| `CatalogTrack` | 3a가 추가한 `genres` `composers` `workKey` `workTitle`을 **아직 안 받는다** |
| History 응답 | `[{ id, trackId, roomId, roomName, heardAt, completed, title, artist }]` |
| Playlists 응답 | `[{ id, title, createdAt, fromArchiveId, tracks: [{ id, title }] }]` |
| `folders` 응답 | `[{ path, exists, trackCount }]` |

---

### Task 1: 카탈로그 모델이 새 필드를 받는다

**Files:**
- Modify: `src/Mono.Control/Models/CatalogTrack.cs`
- Test: `tests/Mono.Tests/LibraryGroupingTests.cs`

**Interfaces:**
- Produces: `CatalogTrack.Genres` (`List<string>`) · `.Composers` (`List<string>`) · `.WorkKey` (`string?`) · `.WorkTitle` (`string?`) — 목록은 절대 null이 아니다.

- [x] **Step 1: 실패하는 테스트를 쓴다**

Control 프로젝트는 테스트에서 참조하지 않으므로, **Core가 내보내는 JSON이 Control 모델의 필드 이름과 맞는지**를 프로토콜 수준에서 고정한다.

`tests/Mono.Tests/LibraryGroupingTests.cs`:

```csharp
using System.Text.Json;
using Mono.Core;
using Mono.Protocol;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 3b 화면들이 기대는 필드가 catalog 응답에 실제로 실리는지 고정한다.
/// Control 은 이 이름들로 역직렬화하므로 Core 가 이름을 바꾸면 화면이 조용히 빈다.
/// </summary>
public class LibraryGroupingTests
{
    private static List<Dictionary<string, JsonElement>> Catalog()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var store = new CatalogStore(Path.Combine(dir, "c.db"));
        var json = JsonSerializer.Serialize(store.CatalogView(), LineFraming.JsonOptions);
        return JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json)!;
    }

    [Fact]
    public void GenresScreenCanAggregateFromCatalog()
    {
        var genres = Catalog()
            .Where(t => t.ContainsKey("genres"))
            .SelectMany(t => t["genres"].EnumerateArray().Select(g => g.GetString()!))
            .Distinct()
            .ToList();

        Assert.Contains("Jazz", genres);
        Assert.Contains("J-Pop", genres);
    }

    [Fact]
    public void ComposersScreenCanAggregateFromCatalog()
    {
        var composers = Catalog()
            .Where(t => t.ContainsKey("composers"))
            .SelectMany(t => t["composers"].EnumerateArray().Select(c => c.GetString()!))
            .Distinct()
            .ToList();

        Assert.Contains("John Coltrane", composers);
        Assert.Contains("Miles Davis", composers);
    }

    [Fact]
    public void CompositionsScreenCanGroupByWorkKey()
    {
        // 같은 작곡가의 두 곡은 제목이 다르므로 서로 다른 작품이다 — 묶이면 안 된다.
        var works = Catalog()
            .Where(t => t.ContainsKey("workKey"))
            .GroupBy(t => t["workKey"].GetString())
            .ToList();

        Assert.NotEmpty(works);
        Assert.All(works, g => Assert.False(string.IsNullOrEmpty(g.Key)));
    }

    [Fact]
    public void QualityChipsAreNotGenres()
    {
        // Hi-Res·DSD 는 장르가 아니라 품질이다. 장르 목록에 섞이면 안 된다.
        var genres = Catalog()
            .Where(t => t.ContainsKey("genres"))
            .SelectMany(t => t["genres"].EnumerateArray().Select(g => g.GetString()!))
            .ToList();

        Assert.DoesNotContain("Hi-Res", genres);
        Assert.DoesNotContain("DSD", genres);
    }
}
```

- [x] **Step 2: 실패를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter LibraryGroupingTests
```

기대: 4개 통과 — 3a가 이미 필드를 실어 놓았으므로 바로 통과한다.
**통과하지 않으면 3a가 덜 된 것이니 거기부터 본다.**

- [x] **Step 3: Control 모델에 필드를 더한다**

`src/Mono.Control/Models/CatalogTrack.cs`의 `[JsonPropertyName("hasLyrics")]` 줄 아래:

```csharp
    [JsonPropertyName("genres")] public List<string> Genres { get; set; } = [];
    [JsonPropertyName("composers")] public List<string> Composers { get; set; } = [];
    [JsonPropertyName("workKey")] public string? WorkKey { get; set; }
    [JsonPropertyName("workTitle")] public string? WorkTitle { get; set; }
```

- [x] **Step 4: 빌드**

```bash
dotnet build Mono.slnx -v q --nologo
```

기대: 경고 0, 오류 0.

- [x] **Step 5: 커밋**

```bash
git add src/Mono.Control/Models/CatalogTrack.cs tests/Mono.Tests/LibraryGroupingTests.cs
git commit -m "Receive genre, composer and work fields in the catalog model

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: 라이브러리 뷰모델과 세 개의 새 사이드바 항목

Genres·Composers·Compositions·Folders는 같은 카탈로그를 다른 축으로 보는 화면이라 상태를 한 곳에 모은다.

**Files:**
- Create: `src/Mono.Control/ViewModels/Pages/LibraryViewModel.cs`
- Create: `src/Mono.Control/Models/LibraryGroups.cs`
- Modify: `src/Mono.Control/ViewModels/MainViewModel.cs` (nav 추가 · 페이지 판정 · Library VM 배선)
- Modify: `src/Mono.Control/ViewModels/MainViewModel.Messages.cs` (`folders` 수신)
- Modify: `src/Mono.Control/Services/CoreSession.Library.cs` (`FoldersAsync`)

**Interfaces:**
- Consumes: `CatalogTrack.Genres`/`Composers`/`WorkKey`/`WorkTitle` (Task 1), `MessageTypes.Folders` (3a).
- Produces:
  - `GenreCount(string Name, int TrackCount)` · `ComposerEntry(string Name, int TrackCount, string Initials)` ·
    `CompositionEntry(string WorkKey, string Title, string Composer, int PerformanceCount)` ·
    `FolderEntry(string Path, bool Exists, int TrackCount)` — 전부 `Mono.Control.Models`
  - `LibraryViewModel.Rebuild(IEnumerable<CatalogTrack> tracks)` — 네 목록을 다시 만든다
  - `LibraryViewModel.ApplyFolders(IEnumerable<FolderEntry>)`
  - `MainViewModel.Library` (`LibraryViewModel`)
  - `CoreSession.FoldersAsync()`
  - nav id `composers` · `compositions` · `folders` 추가

- [x] **Step 1: 그룹 모델을 만든다**

`src/Mono.Control/Models/LibraryGroups.cs`:

```csharp
namespace Mono.Control.Models;

/// <summary>Genres 타일 한 장. 하드코딩이 아니라 실제 태그 집계다.</summary>
public sealed record GenreCount(string Name, int TrackCount)
{
    public string Subtitle => $"{TrackCount}곡";
}

/// <summary>Composers 아바타 한 장. 사진이 없으면 이니셜을 쓴다.</summary>
public sealed record ComposerEntry(string Name, int TrackCount)
{
    /// <summary>라틴 이름은 머리글자 두 개, 그 외(한중일 등)는 첫 글자 하나.</summary>
    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1 || !char.IsAscii(parts[0][0])) return parts[0][..1];
            return $"{parts[0][0]}{parts[^1][0]}";
        }
    }

    public string Subtitle => $"{TrackCount}곡";
}

/// <summary>Compositions 표의 한 줄. 같은 작품의 여러 연주를 묶는다.</summary>
public sealed record CompositionEntry(string WorkKey, string Title, string Composer, int PerformanceCount)
{
    public string PerformanceText => PerformanceCount == 1 ? "연주 1건" : $"연주 {PerformanceCount}건";
}

/// <summary>Folders 목록의 한 줄. Core 의 folders 응답을 그대로 받는다.</summary>
public sealed class FolderEntry
{
    [System.Text.Json.Serialization.JsonPropertyName("path")] public string Path { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("exists")] public bool Exists { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("trackCount")] public int TrackCount { get; set; }

    public string Status => Exists ? $"{TrackCount}곡" : "폴더를 찾을 수 없음";
}
```

- [x] **Step 2: 라이브러리 VM을 만든다**

`src/Mono.Control/ViewModels/Pages/LibraryViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mono.Control.Models;
using Mono.Control.Services;

namespace Mono.Control.ViewModels.Pages;

/// <summary>
/// Genres · Composers · Compositions · Folders — 같은 카탈로그를 다른 축으로 본다.
/// 집계는 전부 여기서 하고, 작품 그룹핑 기준(workKey)은 Core 가 정한 것을 그대로 쓴다.
/// </summary>
public sealed partial class LibraryViewModel : PageViewModel
{
    public LibraryViewModel(CoreSession session) : base(session) { }

    public ObservableCollection<GenreCount> Genres { get; } = new();
    public ObservableCollection<ComposerEntry> Composers { get; } = new();
    public ObservableCollection<CompositionEntry> Compositions { get; } = new();
    public ObservableCollection<FolderEntry> Folders { get; } = new();

    [ObservableProperty] private string _emptyGenresHint = "";

    /// <summary>안내 문구 표시 여부. XAML 에서 컨버터를 쓰지 않도록 bool 로 낸다.</summary>
    public bool HasEmptyGenresHint => !string.IsNullOrEmpty(EmptyGenresHint);

    partial void OnEmptyGenresHintChanged(string value) => OnPropertyChanged(nameof(HasEmptyGenresHint));

    public void Rebuild(IEnumerable<CatalogTrack> tracks)
    {
        var list = tracks.ToList();

        Genres.Clear();
        foreach (var g in list
                     .SelectMany(t => t.Genres)
                     .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Count())
                     .ThenBy(g => g.Key))
        {
            Genres.Add(new GenreCount(g.Key, g.Count()));
        }

        // 마이그레이션은 기존 행을 보존하므로, 재스캔 전에는 장르가 비어 있을 수 있다.
        EmptyGenresHint = Genres.Count == 0 && list.Count > 0
            ? "장르 태그가 아직 없습니다 — Settings 에서 「라이브러리 스캔」을 한 번 돌리세요."
            : "";

        Composers.Clear();
        foreach (var c in list
                     .SelectMany(t => t.Composers)
                     .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(c => c.Count())
                     .ThenBy(c => c.Key))
        {
            Composers.Add(new ComposerEntry(c.Key, c.Count()));
        }

        Compositions.Clear();
        foreach (var w in list
                     .Where(t => !string.IsNullOrEmpty(t.WorkKey))
                     .GroupBy(t => t.WorkKey!)
                     .OrderBy(w => w.First().WorkTitle))
        {
            var head = w.First();
            Compositions.Add(new CompositionEntry(
                w.Key,
                head.WorkTitle ?? head.Title,
                head.Composers.FirstOrDefault() ?? "",
                w.Count()));
        }
    }

    public void ApplyFolders(IEnumerable<FolderEntry> folders)
    {
        Folders.Clear();
        foreach (var f in folders) Folders.Add(f);
    }
}
```

- [x] **Step 3: `folders` 명령을 Control에 배선한다**

`src/Mono.Control/Services/CoreSession.Library.cs`의 `ScanAsync` 아래:

```csharp
    /// <summary>스캔 루트 목록. 존은 룸 스냅샷에 없듯 폴더도 별도 조회다.</summary>
    public Task FoldersAsync() => SendAsync(new MonoMessage { Type = MessageTypes.Folders });
```

- [x] **Step 4: 셸에 붙인다**

`MainViewModel.cs` 생성자에서 `Audio = new AudioViewModel(session);` 아래:

```csharp
        Library = new LibraryViewModel(session);
```

속성:

```csharp
    public LibraryViewModel Library { get; }
```

`NavItems` 목록에 세 항목을 더한다 — `playlists` 다음 줄에:

```csharp
            new("composers", "Composers", "My Library", "nav-artists"),
            new("compositions", "Compositions", "My Library", "nav-tracks"),
            new("folders", "Folders", "My Library", "nav-albums"),
```

아이콘 파일이 따로 없으므로 성격이 가까운 기존 아이콘을 재사용한다.

페이지 판정 속성을 더한다 — `IsGenresPage` 아래:

```csharp
    public bool IsComposersPage => SelectedNav?.Id == "composers";
    public bool IsCompositionsPage => SelectedNav?.Id == "compositions";
    public bool IsFoldersPage => SelectedNav?.Id == "folders";
    public bool IsHistoryPage => SelectedNav?.Id == "history";
    public bool IsPlaylistsPage => SelectedNav?.Id == "playlists";
```

`IsLibraryGrid`가 새 화면까지 먹지 않도록 바꾼다:

```csharp
    public bool IsLibraryGrid => IsContentLibrary && !IsHomePage && !IsGenresPage
                                && !IsComposersPage && !IsCompositionsPage && !IsFoldersPage
                                && !IsHistoryPage && !IsPlaylistsPage;
```

`OnSelectedNavChanged`의 `OnPropertyChanged` 묶음에 다섯 개를 더한다:

```csharp
        OnPropertyChanged(nameof(IsComposersPage));
        OnPropertyChanged(nameof(IsCompositionsPage));
        OnPropertyChanged(nameof(IsFoldersPage));
        OnPropertyChanged(nameof(IsHistoryPage));
        OnPropertyChanged(nameof(IsPlaylistsPage));
```

같은 메서드의 조회 트리거 줄 옆에 폴더 조회를 더한다:

```csharp
        if (value.Id is "folders") _ = Safe(() => _session.FoldersAsync());
```

`PageTitle` switch에 세 줄을 더한다:

```csharp
            "Composers" => "My Composers",
            "Compositions" => "My Compositions",
            "Folders" => "Folders",
```

- [x] **Step 5: 카탈로그를 받을 때 집계한다**

`MainViewModel.Messages.cs`의 `LoadCatalog` 끝, `ApplyFilter();` 아래:

```csharp
        Library.Rebuild(Tracks);
```

`HandleMessage` switch에 케이스를 더한다:

```csharp
            case MessageTypes.Folders:
                LoadFolders(msg.Body);
                break;
```

같은 파일에 메서드를 더한다:

```csharp
    private void LoadFolders(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) { Library.ApplyFolders([]); return; }
        try
        {
            Library.ApplyFolders(JsonSerializer.Deserialize<List<FolderEntry>>(body, Json) ?? []);
        }
        catch { /* 형식이 어긋나면 이전 목록을 유지한다 */ }
    }
```

- [x] **Step 6: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: 사이드바에 Composers · Compositions · Folders 가 생겼다. 아직 본문은 비어 있다(Task 3에서 만든다).

- [x] **Step 7: 커밋**

```bash
git add src/Mono.Control/
git commit -m "Aggregate genres, composers, works and folders in a library view model

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Genres·Composers·Compositions·Folders 화면

**Files:**
- Rewrite: `src/Mono.Control/Views/Pages/GenresPage.axaml` (+ `.axaml.cs` 유지)
- Create: `src/Mono.Control/Views/Pages/ComposersPage.axaml` (+ `.axaml.cs`)
- Create: `src/Mono.Control/Views/Pages/CompositionsPage.axaml` (+ `.axaml.cs`)
- Create: `src/Mono.Control/Views/Pages/FoldersPage.axaml` (+ `.axaml.cs`)
- Modify: `src/Mono.Control/Views/MainWindow.axaml`
- Modify: `src/Mono.Control/ViewModels/MainViewModel.cs` (하드코딩 타일 제거)

**Interfaces:**
- Consumes: `MainViewModel.Library` (Task 2), `SelectGenreCommand`.
- Produces: `MainViewModel.SelectGenreCommand`이 `GenreCount`를 받도록 시그니처 변경, `_genreFilter`가 실제 장르 이름을 담는다.

- [x] **Step 1: 하드코딩 장르를 걷어낸다**

`MainViewModel.cs` 생성자의 `GenreTiles = [...]` 블록 전체와 `public ObservableCollection<GenreTile> GenreTiles { get; }` 선언을 지운다.
`FilterByGenre` 메서드 전체를 지운다 — 아티스트 이름으로 장르를 추측하던 코드다.
`Models/CatalogTrack.cs`의 `GenreTile` 클래스도 지운다.

`SelectGenre` 커맨드를 실제 장르로 바꾼다:

```csharp
    [RelayCommand]
    private void SelectGenre(GenreCount? genre)
    {
        _genreFilter = genre?.Name;
        PageSubtitle = genre is null ? "" : $"{genre.Name} · {genre.TrackCount}곡";
        SelectedNav = NavItems.First(n => n.Id == "tracks");
        ApplyFilter();
    }
```

`ApplyFilter`의 장르 분기를 실제 태그 매칭으로 바꾼다:

```csharp
        else if (_genreFilter is not null)
            src = Tracks.Where(t => t.Genres.Contains(_genreFilter, StringComparer.OrdinalIgnoreCase));
```

기존 `else if (nav is "genres" && _genreFilter is not null)` 분기를 위 코드로 교체한다 —
장르를 고르면 Tracks 화면으로 이동하므로 nav 조건이 필요 없다.

- [x] **Step 2: Genres 화면을 다시 쓴다**

`src/Mono.Control/Views/Pages/GenresPage.axaml` 전체:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Mono.Control.ViewModels"
             xmlns:pvm="using:Mono.Control.ViewModels.Pages"
             xmlns:models="using:Mono.Control.Models"
             x:Class="Mono.Control.Views.Pages.GenresPage"
             x:DataType="pvm:LibraryViewModel">
  <ScrollViewer>
    <StackPanel Spacing="16">
      <TextBlock Text="{Binding EmptyGenresHint}" Classes="muted" TextWrapping="Wrap"
                 IsVisible="{Binding HasEmptyGenresHint}"/>

      <ItemsControl ItemsSource="{Binding Genres}">
        <ItemsControl.ItemsPanel>
          <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
          <DataTemplate x:DataType="models:GenreCount">
            <Button Classes="genretile"
                    Command="{Binding $parent[Window].((vm:MainViewModel)DataContext).SelectGenreCommand}"
                    CommandParameter="{Binding}">
              <StackPanel VerticalAlignment="Bottom">
                <TextBlock Text="{Binding Name}" FontWeight="SemiBold" Foreground="White"/>
                <TextBlock Text="{Binding Subtitle}" Foreground="#CCFFFFFF" FontSize="11"/>
              </StackPanel>
            </Button>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

`Button.genretile` 스타일이 `Background`를 바인딩으로 받던 자리를 잃었으므로,
`MonoTheme.axaml`의 해당 스타일에 기본 배경을 준다:

```xml
    <Setter Property="Background" Value="{DynamicResource Mono.Panel}"/>
```

- [x] **Step 3: Composers 화면**

`src/Mono.Control/Views/Pages/ComposersPage.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:pvm="using:Mono.Control.ViewModels.Pages"
             xmlns:models="using:Mono.Control.Models"
             x:Class="Mono.Control.Views.Pages.ComposersPage"
             x:DataType="pvm:LibraryViewModel">
  <ScrollViewer>
    <ItemsControl ItemsSource="{Binding Composers}">
      <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
      </ItemsControl.ItemsPanel>
      <ItemsControl.ItemTemplate>
        <DataTemplate x:DataType="models:ComposerEntry">
          <StackPanel Width="130" Margin="0,0,12,20" HorizontalAlignment="Center">
            <Border Width="96" Height="96" CornerRadius="48"
                    Background="{DynamicResource Mono.Panel}"
                    BorderBrush="{DynamicResource Mono.Border}" BorderThickness="1">
              <TextBlock Text="{Binding Initials}" FontSize="30" FontWeight="SemiBold"
                         HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <TextBlock Text="{Binding Name}" FontWeight="SemiBold" Margin="0,8,0,0"
                       HorizontalAlignment="Center" TextTrimming="CharacterEllipsis"/>
            <TextBlock Text="{Binding Subtitle}" Classes="muted" FontSize="11"
                       HorizontalAlignment="Center"/>
          </StackPanel>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
  </ScrollViewer>
</UserControl>
```

- [x] **Step 4: Compositions 화면**

`src/Mono.Control/Views/Pages/CompositionsPage.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:pvm="using:Mono.Control.ViewModels.Pages"
             xmlns:models="using:Mono.Control.Models"
             x:Class="Mono.Control.Views.Pages.CompositionsPage"
             x:DataType="pvm:LibraryViewModel">
  <ScrollViewer>
    <StackPanel>
      <Grid ColumnDefinitions="*,220,110" Margin="12,0,12,8">
        <TextBlock Text="작품" Classes="muted" FontSize="11"/>
        <TextBlock Grid.Column="1" Text="작곡가" Classes="muted" FontSize="11"/>
        <TextBlock Grid.Column="2" Text="연주" Classes="muted" FontSize="11"/>
      </Grid>
      <ItemsControl ItemsSource="{Binding Compositions}">
        <ItemsControl.ItemTemplate>
          <DataTemplate x:DataType="models:CompositionEntry">
            <Border Classes="trackcard" Margin="0,0,0,6" Padding="12">
              <Grid ColumnDefinitions="*,220,110">
                <TextBlock Text="{Binding Title}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                <TextBlock Grid.Column="1" Text="{Binding Composer}" Classes="muted"
                           TextTrimming="CharacterEllipsis"/>
                <TextBlock Grid.Column="2" Text="{Binding PerformanceText}" Classes="muted"/>
              </Grid>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

- [x] **Step 5: Folders 화면**

`src/Mono.Control/Views/Pages/FoldersPage.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Mono.Control.ViewModels"
             xmlns:pvm="using:Mono.Control.ViewModels.Pages"
             xmlns:models="using:Mono.Control.Models"
             x:Class="Mono.Control.Views.Pages.FoldersPage"
             x:DataType="pvm:LibraryViewModel">
  <ScrollViewer>
    <StackPanel Spacing="8" MaxWidth="720" HorizontalAlignment="Left">
      <TextBlock Classes="muted" TextWrapping="Wrap"
                 Text="Mono 는 스캔한 폴더의 원본 파일을 수정하지 않습니다."/>
      <ItemsControl ItemsSource="{Binding Folders}">
        <ItemsControl.ItemTemplate>
          <DataTemplate x:DataType="models:FolderEntry">
            <Border Classes="trackcard" Margin="0,0,0,6" Padding="12">
              <StackPanel>
                <TextBlock Text="{Binding Path}" FontWeight="SemiBold" TextWrapping="Wrap"/>
                <TextBlock Text="{Binding Status}" Classes="muted" FontSize="11"/>
              </StackPanel>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
      <Button Classes="ghost" Content="라이브러리 스캔" HorizontalAlignment="Left"
              Command="{Binding $parent[Window].((vm:MainViewModel)DataContext).ScanLibraryCommand}"/>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

- [x] **Step 6: 코드비하인드 3개**

세 파일을 만든다. 클래스 이름과 파일 이름만 다르고 내용은 같다 — `ComposersPage` · `CompositionsPage` · `FoldersPage`:

```csharp
using Avalonia.Controls;

namespace Mono.Control.Views.Pages;

public partial class ComposersPage : UserControl
{
    public ComposersPage() => InitializeComponent();
}
```

- [x] **Step 7: MainWindow에 붙인다**

`MainWindow.axaml`의 `<pages:GenresPage .../>` 줄을 DataContext 주입형으로 바꾸고 세 줄을 더한다:

```xml
            <pages:GenresPage DataContext="{Binding Library}"
                              IsVisible="{Binding $parent[Window].((vm:MainViewModel)DataContext).IsGenresPage}"/>
            <pages:ComposersPage DataContext="{Binding Library}"
                                 IsVisible="{Binding $parent[Window].((vm:MainViewModel)DataContext).IsComposersPage}"/>
            <pages:CompositionsPage DataContext="{Binding Library}"
                                    IsVisible="{Binding $parent[Window].((vm:MainViewModel)DataContext).IsCompositionsPage}"/>
            <pages:FoldersPage DataContext="{Binding Library}"
                               IsVisible="{Binding $parent[Window].((vm:MainViewModel)DataContext).IsFoldersPage}"/>
```

- [x] **Step 8: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: Genres 에 Jazz(3) · Hard Bop(2) · Modal Jazz(1) · J-Pop(1) 타일 · 타일을 누르면 Tracks 로 이동해 그 장르만 남는다 ·
Composers 에 John Coltrane · Miles Davis · 米津玄師 아바타 · Compositions 에 작품 표 · Folders 에 스캔 루트.

기존 `catalog.db`를 쓰고 있으면 장르가 비어 있고 안내 문구가 뜬다 — 그게 의도한 동작이다.
새 DB로 보려면 `src/Mono.Control/bin/Debug/net8.0/data/catalog.db`(Control 이 띄운 Core 의 DB)를 지운다.

- [x] **Step 9: 커밋**

```bash
git add src/Mono.Control/
git commit -m "Replace the fake genre tiles and add composers, compositions and folders

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: History·Playlists 전용 화면

nav만 있고 트랙 그리드를 재활용하던 두 화면에 실제 본문을 준다.

**Files:**
- Create: `src/Mono.Control/Models/HistoryEntry.cs`
- Create: `src/Mono.Control/Views/Pages/HistoryPage.axaml` (+ `.axaml.cs`)
- Create: `src/Mono.Control/Views/Pages/PlaylistsPage.axaml` (+ `.axaml.cs`)
- Modify: `src/Mono.Control/ViewModels/Pages/LibraryViewModel.cs`
- Modify: `src/Mono.Control/ViewModels/MainViewModel.Messages.cs`
- Modify: `src/Mono.Control/Views/MainWindow.axaml`

**Interfaces:**
- Consumes: History 응답 `[{ id, trackId, roomId, roomName, heardAt, completed, title, artist }]`,
  Playlists 응답 `[{ id, title, createdAt, fromArchiveId, tracks: [{ id, title }] }]`,
  `CoreSession.LoadPlaylistAsync(string, bool)` · `ExportM3uAsync(string?)` · `EnqueueAsync(string)` (1단계).
- Produces: `HistoryEntry` · `PlaylistEntry` · `PlaylistTrack` 모델, `LibraryViewModel.History` · `.Playlists` 컬렉션,
  `LibraryViewModel.ReplayCommand` · `LoadPlaylistCommand` · `ExportM3uCommand`.

- [x] **Step 1: 모델을 만든다**

`src/Mono.Control/Models/HistoryEntry.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Mono.Control.Models;

/// <summary>청음 히스토리 한 줄. Core 의 history 응답 형태 그대로다.</summary>
public sealed class HistoryEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("trackId")] public string TrackId { get; set; } = "";
    [JsonPropertyName("roomName")] public string? RoomName { get; set; }
    [JsonPropertyName("heardAt")] public DateTimeOffset HeardAt { get; set; }
    [JsonPropertyName("completed")] public bool Completed { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("artist")] public string? Artist { get; set; }

    public string When => HeardAt.ToLocalTime().ToString("M월 d일 HH:mm");
    public string Detail => string.Join(" · ", new[]
    {
        Artist,
        string.IsNullOrWhiteSpace(RoomName) ? null : RoomName,
        Completed ? "완청" : null
    }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>플레이리스트 한 건.</summary>
public sealed class PlaylistEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("fromArchiveId")] public string? FromArchiveId { get; set; }
    [JsonPropertyName("tracks")] public List<PlaylistTrack> Tracks { get; set; } = [];

    public string Subtitle => $"{Tracks.Count}곡 · {CreatedAt.ToLocalTime():yyyy-MM-dd}"
                              + (string.IsNullOrEmpty(FromArchiveId) ? "" : " · 세션 아카이브");
}

public sealed class PlaylistTrack
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
}
```

- [x] **Step 2: VM에 컬렉션과 커맨드를 더한다**

`LibraryViewModel.cs`의 `Folders` 아래:

```csharp
    public ObservableCollection<HistoryEntry> History { get; } = new();
    public ObservableCollection<PlaylistEntry> Playlists { get; } = new();

    public void ApplyHistory(IEnumerable<HistoryEntry> entries)
    {
        History.Clear();
        foreach (var e in entries.OrderByDescending(e => e.HeardAt)) History.Add(e);
    }

    public void ApplyPlaylists(IEnumerable<PlaylistEntry> lists)
    {
        Playlists.Clear();
        foreach (var p in lists) Playlists.Add(p);
    }

    /// <summary>히스토리에서 다시 듣기 — 큐에 다시 싣는다.</summary>
    [RelayCommand]
    private Task ReplayAsync(HistoryEntry? entry)
        => entry is null ? Task.CompletedTask : Safe(() => Session.EnqueueAsync(entry.TrackId));

    /// <summary>플레이리스트를 큐에 싣는다. 큐를 비우지 않고 뒤에 붙인다.</summary>
    [RelayCommand]
    private Task LoadPlaylistAsync(PlaylistEntry? playlist)
        => playlist is null ? Task.CompletedTask : Safe(() => Session.LoadPlaylistAsync(playlist.Id));

    [RelayCommand]
    private Task ExportM3uAsync(PlaylistEntry? playlist)
        => playlist is null ? Task.CompletedTask : Safe(() => Session.ExportM3uAsync(playlist.Id));
```

파일 상단에 `using CommunityToolkit.Mvvm.Input;`을 더한다.

- [x] **Step 3: 응답을 받는다**

`MainViewModel.Messages.cs`의 `HandleMessage` switch에 두 케이스를 더한다:

```csharp
            case MessageTypes.History:
                LoadHistory(msg.Body);
                break;
            case MessageTypes.Playlists:
                LoadPlaylists(msg.Body);
                break;
```

메서드를 더한다:

```csharp
    private void LoadHistory(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) { Library.ApplyHistory([]); return; }
        try { Library.ApplyHistory(JsonSerializer.Deserialize<List<HistoryEntry>>(body, Json) ?? []); }
        catch { /* 형식이 어긋나면 이전 목록을 유지한다 */ }
    }

    private void LoadPlaylists(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) { Library.ApplyPlaylists([]); return; }
        try { Library.ApplyPlaylists(JsonSerializer.Deserialize<List<PlaylistEntry>>(body, Json) ?? []); }
        catch { /* 형식이 어긋나면 이전 목록을 유지한다 */ }
    }
```

- [x] **Step 4: History 화면**

`src/Mono.Control/Views/Pages/HistoryPage.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:pvm="using:Mono.Control.ViewModels.Pages"
             xmlns:models="using:Mono.Control.Models"
             x:Class="Mono.Control.Views.Pages.HistoryPage"
             x:DataType="pvm:LibraryViewModel">
  <ScrollViewer>
    <StackPanel MaxWidth="720" HorizontalAlignment="Left">
      <ItemsControl ItemsSource="{Binding History}">
        <ItemsControl.ItemTemplate>
          <DataTemplate x:DataType="models:HistoryEntry">
            <Border Classes="trackcard" Margin="0,0,0,6" Padding="12">
              <DockPanel>
                <Button DockPanel.Dock="Right" Classes="ghost" Content="다시 듣기"
                        Command="{Binding $parent[UserControl].((pvm:LibraryViewModel)DataContext).ReplayCommand}"
                        CommandParameter="{Binding}"/>
                <StackPanel>
                  <TextBlock Text="{Binding Title}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                  <TextBlock Text="{Binding Detail}" Classes="muted" FontSize="11"/>
                  <TextBlock Text="{Binding When}" Classes="muted" FontSize="11"/>
                </StackPanel>
              </DockPanel>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

- [x] **Step 5: Playlists 화면**

`src/Mono.Control/Views/Pages/PlaylistsPage.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:pvm="using:Mono.Control.ViewModels.Pages"
             xmlns:models="using:Mono.Control.Models"
             x:Class="Mono.Control.Views.Pages.PlaylistsPage"
             x:DataType="pvm:LibraryViewModel">
  <ScrollViewer>
    <StackPanel MaxWidth="720" HorizontalAlignment="Left">
      <ItemsControl ItemsSource="{Binding Playlists}">
        <ItemsControl.ItemTemplate>
          <DataTemplate x:DataType="models:PlaylistEntry">
            <Border Classes="trackcard" Margin="0,0,0,8" Padding="12">
              <StackPanel Spacing="6">
                <DockPanel>
                  <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" Spacing="6">
                    <Button Classes="ghost" Content="큐에 싣기"
                            Command="{Binding $parent[UserControl].((pvm:LibraryViewModel)DataContext).LoadPlaylistCommand}"
                            CommandParameter="{Binding}"/>
                    <Button Classes="ghost" Content="M3U"
                            Command="{Binding $parent[UserControl].((pvm:LibraryViewModel)DataContext).ExportM3uCommand}"
                            CommandParameter="{Binding}"/>
                  </StackPanel>
                  <StackPanel>
                    <TextBlock Text="{Binding Title}" FontWeight="SemiBold"/>
                    <TextBlock Text="{Binding Subtitle}" Classes="muted" FontSize="11"/>
                  </StackPanel>
                </DockPanel>
                <ItemsControl ItemsSource="{Binding Tracks}">
                  <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="models:PlaylistTrack">
                      <TextBlock Text="{Binding Title}" Classes="muted" FontSize="11" Margin="0,2,0,0"/>
                    </DataTemplate>
                  </ItemsControl.ItemTemplate>
                </ItemsControl>
              </StackPanel>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

- [x] **Step 6: 코드비하인드 2개 + MainWindow 배선**

`HistoryPage.axaml.cs` · `PlaylistsPage.axaml.cs` — 아래 형태로, 클래스 이름을 각각 `HistoryPage` · `PlaylistsPage` 로 둔다:

```csharp
using Avalonia.Controls;

namespace Mono.Control.Views.Pages;

public partial class HistoryPage : UserControl
{
    public HistoryPage() => InitializeComponent();
}
```

`MainWindow.axaml`에 두 줄을 더한다:

```xml
            <pages:HistoryPage DataContext="{Binding Library}"
                               IsVisible="{Binding $parent[Window].((vm:MainViewModel)DataContext).IsHistoryPage}"/>
            <pages:PlaylistsPage DataContext="{Binding Library}"
                                 IsVisible="{Binding $parent[Window].((vm:MainViewModel)DataContext).IsPlaylistsPage}"/>
```

- [x] **Step 7: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: 라운지에서 곡을 재생한 뒤 History 에 줄이 생기고 「다시 듣기」가 큐에 넣는다 ·
세션을 저장하며 종료하면 Playlists 에 하이라이트 리스트가 뜬다.

- [x] **Step 8: 커밋**

```bash
git add src/Mono.Control/
git commit -m "Give history and playlists their own screens

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: 검색을 서버측으로 옮긴다

기획의 핵심인 다국어 별칭 검색(`요네즈 켄시` → `米津玄師`)이 GUI에서 죽어 있다.

**Files:**
- Modify: `src/Mono.Control/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `CoreSession.SearchAsync(string)` (1단계) · `CoreSession.CatalogAsync()`.
- Produces: 없음 — 기존 `SearchText` 동작만 바뀐다.

- [x] **Step 1: 디바운스와 서버 호출을 넣는다**

`MainViewModel.cs`의 필드에 타이머를 더한다:

```csharp
    private CancellationTokenSource? _searchCts;
```

`OnSearchTextChanged`를 바꾼다:

```csharp
    /// <summary>
    /// 검색은 Core 가 한다 — 아티스트 별칭 테이블을 거쳐야 "요네즈 켄시"가 米津玄師를 찾는다.
    /// 타자마다 쏘지 않도록 250ms 묶는다. 빈 문자열이면 전체 카탈로그로 돌아간다.
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        var q = value?.Trim() ?? "";

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, ct);
                await (q.Length == 0 ? _session.CatalogAsync() : _session.SearchAsync(q));
            }
            catch (OperationCanceledException) { /* 다음 타자가 덮어썼다 */ }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => StatusText = ex.Message);
            }
        }, ct);
    }
```

`ApplyFilter`에서 클라이언트 문자열 필터를 뺀다 — 서버가 이미 걸러 보냈다:

```csharp
        var list = src.Take(500).ToList();
```

위 줄 앞의 `var q = SearchText.Trim(); if (q.Length > 0) { src = src.Where(...); }` 블록 전체를 지운다.

- [x] **Step 2: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: 검색창에 **`요네즈 켄시`** 를 치면 `感電`이 나온다(별칭 매칭 — 이전에는 안 됐다) ·
`Hard Bop` 을 치면 Coltrane 두 곡이 나온다(장르 매칭) · 지우면 전체가 돌아온다.

- [x] **Step 3: 커밋**

```bash
git add src/Mono.Control/ViewModels/MainViewModel.cs
git commit -m "Search through Core so multilingual artist aliases work again

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: 앨범 상세

앨범을 누르면 수록곡·라이너·크레딧을 본다. 라이너와 크레딧은 `catalog` 응답에 없다 —
트랙마다 실으면 큰 라이브러리에서 낭비이므로 **필요할 때 한 건만** 가져오는 명령을 만든다.

**Files:**
- Modify: `src/Mono.Protocol/MessageTypes.cs`
- Modify: `src/Mono.Core/CatalogStore.cs` (`AlbumDetail`)
- Modify: `src/Mono.Core/CommandProcessor.cs`
- Create: `src/Mono.Control/Models/AlbumDetail.cs`
- Modify: `src/Mono.Control/Services/CoreSession.Library.cs`
- Modify: `src/Mono.Control/ViewModels/Pages/LibraryViewModel.cs`
- Modify: `src/Mono.Control/ViewModels/MainViewModel.Messages.cs`
- Modify: `src/Mono.Control/Views/Pages/LibraryPage.axaml`
- Modify: `src/Mono.Control/Views/MainWindow.axaml`
- Test: `tests/Mono.Tests/AlbumDetailTests.cs`

**Interfaces:**
- Produces:
  - `MessageTypes.Album = "album"` — 요청은 `Text` 에 albumId, 응답 본문은
    `{ id, title, artist, year, label, linerNotes, credits, tracks: [TrackView] }`
  - `object? CatalogStore.AlbumDetail(string albumId)` — 없으면 null
  - `CoreSession.AlbumAsync(string albumId)`
  - `LibraryViewModel.SelectedAlbum` (`AlbumDetail?`) · `OpenAlbumCommand` · `CloseAlbumCommand`

- [x] **Step 1: 실패하는 테스트를 쓴다**

`tests/Mono.Tests/AlbumDetailTests.cs`:

```csharp
using System.Text.Json;
using Mono.Core;
using Mono.Protocol;
using Xunit;

namespace Mono.Tests;

/// <summary>앨범 상세는 라이너·크레딧을 담는다 — catalog 응답에는 없는 것들이다.</summary>
public class AlbumDetailTests
{
    private static CatalogStore NewStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return new CatalogStore(Path.Combine(dir, "c.db"));
    }

    private static Dictionary<string, JsonElement> AsDict(object item)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            JsonSerializer.Serialize(item, LineFraming.JsonOptions))!;

    [Fact]
    public void AlbumDetailCarriesLinerNotesAndTracks()
    {
        var store = NewStore();

        var detail = AsDict(store.AlbumDetail("al-kob")!);

        Assert.Equal("Kind of Blue", detail["title"].GetString());
        Assert.False(string.IsNullOrEmpty(detail["linerNotes"].GetString()));
        Assert.True(detail["tracks"].GetArrayLength() >= 2);
    }

    [Fact]
    public void UnknownAlbumIsNull()
    {
        Assert.Null(NewStore().AlbumDetail("al-nope"));
    }

    [Fact]
    public void AlbumTracksUseTheSameShapeAsCatalog()
    {
        var store = NewStore();
        var catalogFields = AsDict(store.CatalogView()[0]).Keys.ToHashSet();

        var track = AsDict(store.AlbumDetail("al-kob")!)["tracks"][0];
        var trackFields = JsonSerializer
            .Deserialize<Dictionary<string, JsonElement>>(track.GetRawText())!.Keys.ToHashSet();

        Assert.Equal(catalogFields, trackFields);
    }
}
```

- [x] **Step 2: 실패를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter AlbumDetailTests
```

기대: 컴파일 실패 — `AlbumDetail` 이 없다.

- [x] **Step 3: Core에 구현한다**

`src/Mono.Core/CatalogStore.cs`의 `CatalogView()` 아래:

```csharp
    /// <summary>앨범 한 장의 상세. 라이너·크레딧은 여기서만 나간다 — 트랙마다 실으면 낭비다.</summary>
    public object? AlbumDetail(string albumId)
    {
        lock (_gate)
        {
            if (!_albums.TryGetValue(albumId, out var album)) return null;
            return new
            {
                album.Id,
                album.Title,
                artist = _artists.GetValueOrDefault(album.ArtistId)?.Name,
                album.Year,
                album.Label,
                album.LinerNotes,
                album.Credits,
                tracks = _tracks.Values
                    .Where(t => t.AlbumId == albumId)
                    .OrderBy(t => t.TrackNumber)
                    .Select(TrackView)
                    .ToList()
            };
        }
    }
```

`src/Mono.Protocol/MessageTypes.cs`의 `Folders` 줄 아래:

```csharp
    public const string Album = "album";
```

`src/Mono.Core/CommandProcessor.cs`의 `case MessageTypes.Folders:` 바로 위에:

```csharp
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

```

- [x] **Step 4: 통과를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter AlbumDetailTests
```

기대: 3개 통과.

- [x] **Step 5: Control 모델과 명령을 더한다**

`src/Mono.Control/Models/AlbumDetail.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Mono.Control.Models;

/// <summary>앨범 상세 오버레이가 보여 주는 내용.</summary>
public sealed class AlbumDetail
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("artist")] public string? Artist { get; set; }
    [JsonPropertyName("year")] public int? Year { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("linerNotes")] public string? LinerNotes { get; set; }
    [JsonPropertyName("credits")] public string? Credits { get; set; }
    [JsonPropertyName("tracks")] public List<CatalogTrack> Tracks { get; set; } = [];

    public string Subtitle => string.Join(" · ", new[]
    {
        Artist,
        Year?.ToString(),
        Label,
        $"{Tracks.Count}곡"
    }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public bool HasLinerNotes => !string.IsNullOrWhiteSpace(LinerNotes);
    public bool HasCredits => !string.IsNullOrWhiteSpace(Credits);
}
```

`src/Mono.Control/Services/CoreSession.Library.cs`의 `FoldersAsync` 아래:

```csharp
    /// <summary>앨범 한 장의 상세. 라이너·크레딧은 catalog 에 없으므로 눌렀을 때만 받는다.</summary>
    public Task AlbumAsync(string albumId) => SendAsync(new MonoMessage { Type = MessageTypes.Album, Text = albumId });
```

`LibraryViewModel.cs` 에:

```csharp
    [ObservableProperty] private AlbumDetail? _selectedAlbum;

    /// <summary>오버레이 표시 여부. XAML 에서 컨버터를 쓰지 않도록 bool 로 낸다.</summary>
    public bool HasSelectedAlbum => SelectedAlbum is not null;

    partial void OnSelectedAlbumChanged(AlbumDetail? value) => OnPropertyChanged(nameof(HasSelectedAlbum));

    public void ApplyAlbum(AlbumDetail? album) => SelectedAlbum = album;

    [RelayCommand]
    private Task OpenAlbumAsync(CatalogTrack? track)
        => string.IsNullOrEmpty(track?.AlbumId)
            ? Task.CompletedTask
            : Safe(() => Session.AlbumAsync(track.AlbumId!));

    [RelayCommand]
    private void CloseAlbum() => SelectedAlbum = null;
```

`MainViewModel.Messages.cs` 의 switch 에:

```csharp
            case MessageTypes.Album:
                LoadAlbum(msg.Body);
                break;
```

```csharp
    private void LoadAlbum(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) { Library.ApplyAlbum(null); return; }
        try { Library.ApplyAlbum(JsonSerializer.Deserialize<AlbumDetail>(body, Json)); }
        catch { /* 형식이 어긋나면 열지 않는다 */ }
    }
```

- [x] **Step 6: 앨범 카드에 여는 동작을 붙인다**

`LibraryPage.axaml` 의 트랙 카드 안에서, 아트를 담은 `<Border Classes="artslot" Height="140">` 를
아래 버튼으로 감싸고 대응하는 `</Border>` 뒤에 `</Button>` 을 닫는다.
제목 클릭의 기존 재생 동작은 그대로 두고, **아트만** 눌러 앨범을 연다:

```xml
                          <Button Classes="ghost" Padding="0" BorderThickness="0" Background="Transparent"
                                  ToolTip.Tip="앨범 열기"
                                  Command="{Binding $parent[Window].((vm:MainViewModel)DataContext).Library.OpenAlbumCommand}"
                                  CommandParameter="{Binding}">
```

- [x] **Step 7: 오버레이를 만든다**

`MainWindow.axaml` 의 큐 드로어 `<!-- Queue drawer -->` 앞에 넣는다:

```xml
    <!-- Album detail -->
    <Border IsVisible="{Binding Library.HasSelectedAlbum}"
            Background="{DynamicResource Mono.Onboarding}">
      <Border Classes="obcard" MaxWidth="720" MaxHeight="640"
              HorizontalAlignment="Center" VerticalAlignment="Center">
        <DockPanel Margin="24">
          <DockPanel DockPanel.Dock="Top">
            <Button DockPanel.Dock="Right" Classes="ghost" Content="닫기"
                    Command="{Binding Library.CloseAlbumCommand}"/>
            <StackPanel DataContext="{Binding Library.SelectedAlbum}">
              <TextBlock Text="{Binding Title}" Classes="h1" FontSize="22"/>
              <TextBlock Text="{Binding Subtitle}" Classes="muted"/>
            </StackPanel>
          </DockPanel>
          <ScrollViewer Margin="0,16,0,0" DataContext="{Binding Library.SelectedAlbum}">
            <StackPanel Spacing="12">
              <ItemsControl ItemsSource="{Binding Tracks}">
                <ItemsControl.ItemTemplate>
                  <DataTemplate x:DataType="models:CatalogTrack">
                    <Grid ColumnDefinitions="*,60" Margin="0,0,0,4">
                      <TextBlock Text="{Binding Title}" TextTrimming="CharacterEllipsis"/>
                      <TextBlock Grid.Column="1" Text="{Binding DurationText}" Classes="muted"
                                 HorizontalAlignment="Right"/>
                    </Grid>
                  </DataTemplate>
                </ItemsControl.ItemTemplate>
              </ItemsControl>
              <TextBlock Text="라이너 노트" FontWeight="SemiBold" IsVisible="{Binding HasLinerNotes}"/>
              <TextBlock Text="{Binding LinerNotes}" Classes="muted" TextWrapping="Wrap"
                         IsVisible="{Binding HasLinerNotes}"/>
              <TextBlock Text="크레딧" FontWeight="SemiBold" IsVisible="{Binding HasCredits}"/>
              <TextBlock Text="{Binding Credits}" Classes="muted" TextWrapping="Wrap"
                         IsVisible="{Binding HasCredits}"/>
            </StackPanel>
          </ScrollViewer>
        </DockPanel>
      </Border>
    </Border>

```

- [x] **Step 8: 빌드하고 띄운다**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet run --project src/Mono.Control
```

확인: Albums 화면에서 아트를 누르면 오버레이가 열려 수록곡과 라이너가 보이고 「닫기」로 닫힌다.

- [x] **Step 9: 커밋**

```bash
git add src/ tests/
git commit -m "Add an album detail overlay with liner notes and credits

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## 3b단계 완료 기준

- [x] `dotnet build Mono.slnx` 경고 0, 오류 0
- [x] `dotnet test Mono.slnx` **96개** 통과 (3a 89 + 라이브러리 집계 4 + 앨범 상세 3)
- [x] 사이드바 15개 — 문서 §3.1 항목이 전부 있다 (**Composers · Compositions · Folders** 추가)
- [x] Genres 타일이 실제 태그 집계다. `FilterByGenre`와 `GenreTile`이 완전히 사라졌다 (grep 0건)
- [x] 검색창에 `요네즈 켄시`를 치면 `感電`이 나온다 — TCP 프로브로 확인
- [x] History·Playlists 가 전용 화면이다
- [x] `album` 명령이 라이너와 함께 응답한다 — TCP 프로브로 확인

실측(신규 DB, TCP 프로브):
```
search "요네즈 켄시" -> [tr-kanden]
album al-kob        -> Kind of Blue | 2곡 | liner: True
folders             -> [(library, 0)]
```

**검증하지 못한 것:** 화면 제어 권한이 없어 **새 화면 6개의 렌더 결과를 눈으로 보지 못했다.**
컴파일된 바인딩이라 바인딩 경로 오류는 빌드가 잡고, 앱은 예외 없이 뜬다 — 거기까지다.
**레이아웃과 클릭 동작은 사람이 한 번 봐야 한다.**

## 다음 단계

4단계(라운지) — 관리자 패널·큐 협업·핀·반응·관계도·아카이브. 별도 계획으로 쓴다.
