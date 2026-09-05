# Control 패리티 3a단계 — Core 라이브러리 확장 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `04-UIUX기획서` §3.1의 Genres·Composers·Compositions·Folders 화면이 기댈 데이터를 Core에 만든다 — 장르·작곡가 태그 스캔, 작품(Work) 그룹핑, 복수 스캔 루트, 그리고 검색 응답 형태 통일.

**Architecture:** 화면은 3b단계에서 만든다. 여기서는 **데이터만** 다룬다. 새 조회 명령을 늘리지 않고 `CatalogView()` 한 곳에 `genres` · `composers` · `workKey` · `workTitle`를 실어 Control이 클라이언트에서 묶게 한다 — 작품명 정규화는 Core에만 두어 단일 진실을 유지한다. 스캔 루트 목록만 Core 지식이므로 `folders` 명령 하나를 새로 만든다.

**Tech Stack:** C# / .NET 8 · SQLite (Microsoft.Data.Sqlite + Dapper) · TagLibSharp · xunit

**Spec:** [`docs/superpowers/specs/2026-09-05-control-full-parity-design.md`](../specs/2026-09-05-control-full-parity-design.md) §4 단계 3 「Core 선행 작업」

**선행:** 2단계 완료 (`v0.1.4` 릴리스)

## Global Constraints

- 대상 프레임워크 `net8.0`. 새 NuGet 패키지를 추가하지 않는다.
- JSON은 `LineFraming.JsonOptions` — CamelCase, 열거형은 정수.
- 의존 방향: `Control` / `Output` / `Core` → `Protocol` → `Shared`.
- `dotnet build Mono.slnx` 경고 0 유지. 기존 70개 테스트는 회귀 가드다.
- **기존 `catalog.db`는 재스캔 없이 열려야 한다.** 컬럼 추가만 하고 기존 행은 보존한다.
- **작품 그룹핑은 보수적으로.** 확신이 없으면 묶지 않는다 — 잘못된 병합이 미병합보다 나쁘다.
- 사용자 대상 문자열은 한국어.
- 커밋 메시지 말미에 `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## 확인된 현재 상태 (추측 금지)

| 항목 | 사실 |
| --- | --- |
| `tracks` 스키마 | `id title album_id artist_id local_path streaming_id source quality sample_rate bit_depth channels is_dsd dsd_rate duration_ms lyrics art merged track_no` — **genre·composer 없음** |
| 마이그레이션 패턴 | `Migrate(con)`에서 `ALTER TABLE ... ADD COLUMN`을 `try/catch (SqliteException)`로 감싼다. 이미 있으면 무시 |
| 스캐너 태그 | `tag.FirstAlbumArtist` `tag.Album` `tag.Title` `tag.Year` `tag.JoinedPerformers` `tag.Comment` `tag.Publisher` `tag.Lyrics` `tag.Track` — **`tag.Genres` · `tag.Composers` 미사용** |
| 스캔 루트 | `Mono:LibraryRoot` **단수 하나**. `CommandProcessor`가 `string libraryRoot`로 받음. `LibraryScanner.Scan(string root)` |
| `CatalogView()` | 리치 DTO — `artist` `artistAliases` `album` `year` `label` `hasLyrics` `hasLocal` `artUrl` `badge` 등 |
| **`Search()`** | **원시 `Track`을 반환** — `artist`·`album`·`artUrl`·`badge`가 없고 `localPath`가 새어 나간다. Control은 `Catalog`와 같은 파서로 받으므로 서버측 검색으로 바꾸면 화면이 비어 보인다 |
| 별칭 검색 | `Search()` 안의 `MatchesAlias(artist, qNorm)`로 이미 동작 — 다국어 매칭 로직 자체는 있다 |

---

### Task 1: 작품명 정규화 (순수 함수)

같은 작품의 여러 악장·연주를 묶는 키를 만든다. Core에만 두어 Control이 같은 로직을 다시 쓰지 않게 한다.

**Files:**
- Create: `src/Mono.Core/CompositionGrouping.cs`
- Test: `tests/Mono.Tests/CompositionGroupingTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct WorkId(string Key, string Title)` — `Key`는 그룹핑용 정규화 문자열, `Title`은 화면 표시용
  - `static WorkId? CompositionGrouping.Identify(string trackTitle, string? composer)` — 묶을 근거가 없으면 `null`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`tests/Mono.Tests/CompositionGroupingTests.cs`:

```csharp
using Mono.Core;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 작품(Work) 그룹핑. 같은 작품의 여러 악장·연주를 한 줄로 묶되,
/// 확신이 없으면 묶지 않는다 — 잘못된 병합이 미병합보다 나쁘다.
/// </summary>
public class CompositionGroupingTests
{
    [Fact]
    public void MovementsOfOneWorkShareAKey()
    {
        var first = CompositionGrouping.Identify(
            "Symphony No. 5 in C minor, Op. 67: I. Allegro con brio", "Ludwig van Beethoven");
        var second = CompositionGrouping.Identify(
            "Symphony No. 5 in C minor, Op. 67: II. Andante con moto", "Ludwig van Beethoven");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Value.Key, second!.Value.Key);
        Assert.Equal("Symphony No. 5 in C minor, Op. 67", first.Value.Title);
    }

    [Fact]
    public void DifferentComposersNeverShareAKey()
    {
        var a = CompositionGrouping.Identify("Requiem: Introitus", "Mozart");
        var b = CompositionGrouping.Identify("Requiem: Introitus", "Verdi");

        Assert.NotEqual(a!.Value.Key, b!.Value.Key);
    }

    [Fact]
    public void NoComposerMeansNoGrouping()
    {
        // 작곡가 태그가 없으면 제목만으로 묶지 않는다.
        Assert.Null(CompositionGrouping.Identify("Blue Train", null));
        Assert.Null(CompositionGrouping.Identify("Blue Train", "   "));
    }

    [Fact]
    public void NonMovementSuffixIsNotStripped()
    {
        // "Live at Carnegie Hall"은 악장이 아니다. 잘라내면 다른 곡과 잘못 묶인다.
        var w = CompositionGrouping.Identify("Take Five: Live at Carnegie Hall", "Paul Desmond");

        Assert.Equal("Take Five: Live at Carnegie Hall", w!.Value.Title);
    }

    [Fact]
    public void ArabicNumeralMovementsAlsoSplit()
    {
        var a = CompositionGrouping.Identify("The Four Seasons, Op. 8: 1. Spring", "Vivaldi");
        var b = CompositionGrouping.Identify("The Four Seasons, Op. 8: 4. Winter", "Vivaldi");

        Assert.Equal(a!.Value.Key, b!.Value.Key);
        Assert.Equal("The Four Seasons, Op. 8", a.Value.Title);
    }

    [Fact]
    public void KeyIgnoresCaseAndSpacing()
    {
        var a = CompositionGrouping.Identify("Nocturne  in  E-flat", "Chopin");
        var b = CompositionGrouping.Identify("NOCTURNE IN E-FLAT", "chopin");

        Assert.Equal(a!.Value.Key, b!.Value.Key);
    }

    [Fact]
    public void EmptyTitleIsNotGrouped()
    {
        Assert.Null(CompositionGrouping.Identify("", "Bach"));
        Assert.Null(CompositionGrouping.Identify("   ", "Bach"));
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter CompositionGroupingTests
```

기대: 컴파일 실패 — `CompositionGrouping`이 없다.

- [ ] **Step 3: 최소 구현**

`src/Mono.Core/CompositionGrouping.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Mono.Core;

/// <summary>작품 식별자. Key는 그룹핑용, Title은 화면 표시용이다.</summary>
public readonly record struct WorkId(string Key, string Title);

/// <summary>
/// 같은 작품(Work)의 여러 악장·연주를 묶는다.
/// 클래식 표기 "작품명: I. 악장"에서 악장 꼬리만 떼되, 악장으로 보이지 않으면 건드리지 않는다.
/// 작곡가를 모르면 아예 묶지 않는다 — 제목만으로 묶으면 서로 다른 곡이 합쳐진다.
/// </summary>
public static partial class CompositionGrouping
{
    /// <summary>"I." "IV." "1." 처럼 로마 숫자나 아라비아 숫자로 시작하는 꼬리만 악장으로 본다.</summary>
    [GeneratedRegex(@"^\s*(?:[IVXLC]+|\d{1,2})\s*[\.\)]\s+\S", RegexOptions.IgnoreCase)]
    private static partial Regex MovementHead();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static WorkId? Identify(string trackTitle, string? composer)
    {
        if (string.IsNullOrWhiteSpace(trackTitle)) return null;
        if (string.IsNullOrWhiteSpace(composer)) return null;

        var title = trackTitle.Trim();
        var cut = title.LastIndexOf(':');
        if (cut > 0 && cut < title.Length - 1)
        {
            var tail = title[(cut + 1)..];
            if (MovementHead().IsMatch(tail))
            {
                title = title[..cut].TrimEnd();
            }
        }

        var key = Normalize(composer) + "|" + Normalize(title);
        return new WorkId(key, title);
    }

    private static string Normalize(string s)
        => Whitespace().Replace(s.Trim(), " ").ToLowerInvariant();
}
```

- [ ] **Step 4: 통과를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter CompositionGroupingTests
```

기대: 7개 통과.

- [ ] **Step 5: 커밋**

```bash
git add src/Mono.Core/CompositionGrouping.cs tests/Mono.Tests/CompositionGroupingTests.cs
git commit -m "Group tracks into works by composer and normalised title

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: 장르·작곡가 도메인과 스키마

`Track`에 필드를 더하고, 기존 `catalog.db`가 재스캔 없이 열리는지 테스트로 고정한다.

**Files:**
- Modify: `src/Mono.Shared/Catalog.cs` (`Track` 클래스)
- Modify: `src/Mono.Core/CatalogStore.cs` (스키마 · `Migrate` · `Load` · 저장)
- Test: `tests/Mono.Tests/CatalogMigrationTests.cs`

**Interfaces:**
- Consumes: 없음.
- Produces: `Track.Genres` (`List<string>`) · `Track.Composers` (`List<string>`) — 둘 다 절대 null이 아니다. DB에는 `genres` · `composers` 컬럼에 ``(unit separator)로 이어 붙여 저장한다.

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`tests/Mono.Tests/CatalogMigrationTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Mono.Core;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 스키마가 늘어도 기존 사용자가 재스캔을 강요당하면 안 된다.
/// 구 스키마 DB를 열어 새 컬럼이 빈 값으로 시작하는지 확인한다.
/// </summary>
public class CatalogMigrationTests
{
    private static string TempDb()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "c.db");
    }

    [Fact]
    public void OldSchemaOpensWithoutRescan()
    {
        var path = TempDb();

        // genres/composers 가 없던 시절의 스키마로 DB를 만들고 트랙 한 줄을 넣는다.
        using (var con = new SqliteConnection($"Data Source={path}"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE artists (id TEXT PRIMARY KEY, name TEXT, related TEXT, bio TEXT, aliases TEXT);
                CREATE TABLE albums (id TEXT PRIMARY KEY, title TEXT, artist_id TEXT, liner TEXT, label TEXT, year INT, credits TEXT, art TEXT);
                CREATE TABLE tracks (
                  id TEXT PRIMARY KEY, title TEXT, album_id TEXT, artist_id TEXT,
                  local_path TEXT, streaming_id TEXT, source INT, quality INT,
                  sample_rate INT, bit_depth INT, channels INT, is_dsd INT, dsd_rate INT,
                  duration_ms INT, lyrics TEXT, art TEXT, merged INT, track_no INT DEFAULT 0);
                INSERT INTO artists (id, name) VALUES ('ar-1', 'Old Artist');
                INSERT INTO albums (id, title, artist_id) VALUES ('al-1', 'Old Album', 'ar-1');
                INSERT INTO tracks (id, title, album_id, artist_id, duration_ms)
                  VALUES ('tr-old', 'Old Track', 'al-1', 'ar-1', 1000);
                """;
            cmd.ExecuteNonQuery();
        }

        var store = new CatalogStore(path);

        var track = store.Tracks["tr-old"];
        Assert.Equal("Old Track", track.Title);
        Assert.Empty(track.Genres);
        Assert.Empty(track.Composers);
    }

    [Fact]
    public void GenresAndComposersRoundTripThroughTheDatabase()
    {
        var path = TempDb();
        var store = new CatalogStore(path);
        var seeded = store.Tracks.Values.First();
        seeded.Genres.Add("Jazz");
        seeded.Genres.Add("Hard Bop");
        seeded.Composers.Add("John Coltrane");
        store.UpsertTrack(seeded, store.Albums[seeded.AlbumId], store.Artists[seeded.ArtistId]);

        // 같은 파일을 다시 열어 값이 살아남았는지 본다.
        var reopened = new CatalogStore(path);
        var again = reopened.Tracks[seeded.Id];

        Assert.Equal(["Jazz", "Hard Bop"], again.Genres);
        Assert.Equal(["John Coltrane"], again.Composers);
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter CatalogMigrationTests
```

기대: 컴파일 실패 — `Track.Genres`가 없다. `SaveTrack`이 없으면 그것도 함께 뜬다.

- [ ] **Step 3: 도메인에 필드를 더한다**

`src/Mono.Shared/Catalog.cs`의 `Track` 클래스, `TrackNumber` 아래에 추가:

```csharp
    /// <summary>파일 태그의 장르. Genres 화면과 장르 타일 집계에 쓴다.</summary>
    public List<string> Genres { get; init; } = [];

    /// <summary>파일 태그의 작곡가. Composers·Compositions 화면의 근거다.</summary>
    public List<string> Composers { get; init; } = [];
```

- [ ] **Step 4: 스키마와 마이그레이션을 더한다**

`src/Mono.Core/CatalogStore.cs`의 `CREATE TABLE tracks` 마지막 컬럼을 바꾼다:

```
              duration_ms INT, lyrics TEXT, art TEXT, merged INT, track_no INT DEFAULT 0,
              genres TEXT, composers TEXT);
```

`Migrate(SqliteConnection con)` 안, 기존 두 블록과 같은 모양으로 두 개를 더 추가한다:

```csharp
        try
        {
            con.Execute("ALTER TABLE tracks ADD COLUMN genres TEXT");
        }
        catch (SqliteException)
        {
            // 이미 있는 컬럼.
        }

        try
        {
            con.Execute("ALTER TABLE tracks ADD COLUMN composers TEXT");
        }
        catch (SqliteException)
        {
            // 이미 있는 컬럼.
        }
```

- [ ] **Step 5: 읽기·쓰기를 잇는다**

`CatalogStore.cs`에 목록 직렬화 헬퍼를 더한다(클래스 안 아무 곳):

```csharp
    /// <summary>목록 컬럼 구분자. 장르·작곡가 이름에 나올 수 없는 문자를 쓴다.</summary>
    private const char ListSep = '';

    private static string PackList(IEnumerable<string> items)
        => string.Join(ListSep, items.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));

    private static List<string> UnpackList(string? packed)
        => string.IsNullOrWhiteSpace(packed)
            ? []
            : packed.Split(ListSep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
```

`Load(con)`에서 트랙을 만드는 곳에 두 줄을 더한다 — 기존 컬럼 읽기와 같은 방식으로 `genres` · `composers`를 읽어 `UnpackList`로 채운다. 컬럼이 없는 구 DB에서도 `Migrate`가 먼저 돌아 항상 존재한다.

트랙 INSERT 는 **두 곳**에 있다. 둘 다 고쳐야 한다 — 하나만 고치면 시드 데이터나 스캔 결과 중
한쪽에서 값이 사라진다:

```bash
grep -n 'INSERT INTO tracks' src/Mono.Core/CatalogStore.cs
```

각 INSERT 의 컬럼 목록 끝에 `,genres,composers` 를, `VALUES` 목록 끝에 `,$genres,$composers` 를 더하고,
파라미터로 `PackList(track.Genres)` · `PackList(track.Composers)` 를 넘긴다.

공개 저장 API 는 이미 있다 — 새로 만들지 않는다:

```csharp
public void UpsertTrack(Track track, Album album, Artist artist)
```

테스트와 스캐너는 이걸 쓴다.

- [ ] **Step 6: 통과를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter CatalogMigrationTests
dotnet test Mono.slnx --nologo -v q
```

기대: 마이그레이션 2개 통과, 전체도 통과(기존 70 + 작품 7 + 마이그레이션 2 = 79).

- [ ] **Step 7: 커밋**

```bash
git add src/Mono.Shared/Catalog.cs src/Mono.Core/CatalogStore.cs tests/Mono.Tests/CatalogMigrationTests.cs
git commit -m "Store genre and composer tags, migrating old catalogs in place

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: 스캐너가 장르·작곡가를 읽는다

**Files:**
- Modify: `src/Mono.Core/LibraryScanner.cs` (`Import` 메서드)
- Test: `tests/Mono.Tests/ScannerTagTests.cs`

**Interfaces:**
- Consumes: `Track.Genres` · `Track.Composers` (Task 2).
- Produces: 스캔한 파일의 `tag.Genres` → `Track.Genres`, `tag.Composers` → `Track.Composers`.

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`tests/Mono.Tests/ScannerTagTests.cs`:

```csharp
using Mono.Core;
using Xunit;

namespace Mono.Tests;

/// <summary>스캐너가 장르·작곡가 태그를 카탈로그로 옮기는지 확인한다.</summary>
public class ScannerTagTests
{
    [Fact]
    public void ScanReadsGenreAndComposerTags()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var lib = Path.Combine(dir, "library");
        Directory.CreateDirectory(lib);

        var wav = Path.Combine(lib, "tagged.wav");
        WriteSilentWav(wav);
        using (var tf = TagLib.File.Create(wav))
        {
            tf.Tag.Title = "Symphony No. 5 in C minor, Op. 67: I. Allegro con brio";
            tf.Tag.Genres = ["Classical", "Symphony"];
            tf.Tag.Composers = ["Ludwig van Beethoven"];
            tf.Tag.Album = "Beethoven: Symphonies";
            tf.Save();
        }

        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        var scanner = new LibraryScanner(catalog, new ArtworkService(Path.Combine(dir, "art")));
        scanner.Scan(lib);

        var track = catalog.Tracks.Values.Single(t => t.LocalPath == wav);
        Assert.Equal(["Classical", "Symphony"], track.Genres);
        Assert.Equal(["Ludwig van Beethoven"], track.Composers);
    }

    private static void WriteSilentWav(string path)
    {
        const int rate = 44100, channels = 2, bits = 16, seconds = 1;
        var dataLen = rate * channels * (bits / 8) * seconds;
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataLen);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(rate);
        w.Write(rate * channels * (bits / 8));
        w.Write((short)(channels * (bits / 8)));
        w.Write((short)bits);
        w.Write("data"u8.ToArray());
        w.Write(dataLen);
        w.Write(new byte[dataLen]);
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter ScannerTagTests
```

기대: 실패 — `track.Genres`가 비어 있다.

`ArtworkService`의 생성자 시그니처가 `(string)`이 아니면 실제 시그니처에 맞춘다:

```bash
grep -n 'public ArtworkService' src/Mono.Core/ArtworkService.cs
```

- [ ] **Step 3: 스캐너를 고친다**

`src/Mono.Core/LibraryScanner.cs`의 `Import` 안, `Track`을 만드는 객체 초기화자에 두 줄을 더한다:

```csharp
            Genres = [.. tag.Genres.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim())],
            Composers = [.. tag.Composers.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())],
```

기존 트랙을 갱신하는 경로가 따로 있으면 거기에도 같은 값을 넣는다 — 재스캔 시 태그가 반영돼야 한다.

- [ ] **Step 4: 통과를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter ScannerTagTests
```

기대: 1개 통과.

- [ ] **Step 5: 커밋**

```bash
git add src/Mono.Core/LibraryScanner.cs tests/Mono.Tests/ScannerTagTests.cs
git commit -m "Read genre and composer tags during library scan

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: 복수 스캔 루트

문서 §4.7 Storage는 폴더 여러 개의 추가·제거·상태를 요구한다. 지금은 루트가 하나다.

**Files:**
- Modify: `src/Mono.Core/LibraryScanner.cs`
- Modify: `src/Mono.Core/Program.cs` (설정 읽기)
- Modify: `src/Mono.Core/CommandProcessor.cs` (생성자 · `ScanLibrary` · 새 `Folders` 케이스)
- Modify: `src/Mono.Protocol/MessageTypes.cs`
- Test: `tests/Mono.Tests/ScanRootTests.cs`

**Interfaces:**
- Consumes: `LibraryScanner.Scan(string root)` (기존).
- Produces:
  - `int LibraryScanner.ScanAll(IEnumerable<string> roots)` — 각 루트를 훑고 합계를 돌려준다
  - `MessageTypes.Folders = "folders"` — 응답 본문은 `[{ path, trackCount, exists }]`
  - `CommandProcessor` 생성자 마지막 인자가 `string libraryRoot` → `IReadOnlyList<string> libraryRoots`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`tests/Mono.Tests/ScanRootTests.cs`:

```csharp
using Mono.Core;
using Xunit;

namespace Mono.Tests;

/// <summary>스캔 루트 복수화. 폴더를 여러 개 걸어도 한 카탈로그로 모인다.</summary>
public class ScanRootTests
{
    [Fact]
    public void ScanAllCoversEveryRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        var a = Path.Combine(dir, "a");
        var b = Path.Combine(dir, "b");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        WriteSilentWav(Path.Combine(a, "one.wav"));
        WriteSilentWav(Path.Combine(b, "two.wav"));

        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        var scanner = new LibraryScanner(catalog, new ArtworkService(Path.Combine(dir, "art")));

        var scanned = scanner.ScanAll([a, b]);

        Assert.Equal(2, scanned);
        Assert.Contains(catalog.Tracks.Values, t => t.LocalPath == Path.Combine(a, "one.wav"));
        Assert.Contains(catalog.Tracks.Values, t => t.LocalPath == Path.Combine(b, "two.wav"));
    }

    [Fact]
    public void MissingRootIsSkippedNotFatal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        var real = Path.Combine(dir, "real");
        Directory.CreateDirectory(real);
        WriteSilentWav(Path.Combine(real, "one.wav"));

        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        var scanner = new LibraryScanner(catalog, new ArtworkService(Path.Combine(dir, "art")));

        // 없는 폴더가 섞여 있어도 나머지는 스캔된다.
        var scanned = scanner.ScanAll([Path.Combine(dir, "gone"), real]);

        Assert.Equal(1, scanned);
    }

    private static void WriteSilentWav(string path)
    {
        const int rate = 44100, channels = 2, bits = 16, seconds = 1;
        var dataLen = rate * channels * (bits / 8) * seconds;
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataLen);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(rate);
        w.Write(rate * channels * (bits / 8));
        w.Write((short)(channels * (bits / 8)));
        w.Write((short)bits);
        w.Write("data"u8.ToArray());
        w.Write(dataLen);
        w.Write(new byte[dataLen]);
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter ScanRootTests
```

기대: 컴파일 실패 — `ScanAll`이 없다.

- [ ] **Step 3: `ScanAll`을 더한다**

`src/Mono.Core/LibraryScanner.cs`의 `Scan(string root)` 아래:

```csharp
    /// <summary>
    /// 여러 루트를 차례로 훑는다. 없는 폴더는 건너뛴다 —
    /// 외장 드라이브가 빠져 있다고 나머지 스캔까지 멈출 이유가 없다.
    /// </summary>
    public int ScanAll(IEnumerable<string> roots)
    {
        var total = 0;
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct())
        {
            if (!Directory.Exists(root)) continue;
            total += Scan(root);
        }

        LastScan = DateTimeOffset.UtcNow;
        LastCount = total;
        return total;
    }
```

`Scan`이 없는 폴더를 만들어 버리는 현재 동작(`Directory.CreateDirectory(root)`)은 그대로 둔다 — 단일 루트 기본 경로를 준비하는 용도다. `ScanAll`은 그 앞에서 걸러낸다.

- [ ] **Step 4: 설정을 복수로 읽는다**

`src/Mono.Core/Program.cs`의 `var library = ...` 줄을 바꾼다:

```csharp
// Mono:LibraryRoots 배열이 우선. 없으면 기존 단수 Mono:LibraryRoot 를 그대로 읽는다.
var configuredRoots = builder.Configuration.GetSection("Mono:LibraryRoots").Get<string[]>() ?? [];
var library = builder.Configuration["Mono:LibraryRoot"] ?? Path.Combine(data, "library");
var libraryRoots = configuredRoots.Length > 0 ? configuredRoots.ToList() : new List<string> { library };
```

`Directory.CreateDirectory(library);`는 그대로 둔다(기본 루트 준비).
`CommandProcessor`를 만드는 곳에서 마지막 인자를 `libraryRoots`로 바꾼다.

- [ ] **Step 5: `CommandProcessor`를 고친다**

생성자 마지막 파라미터를 바꾼다:

```csharp
        IReadOnlyList<string> libraryRoots)
```

필드도 바꾼다:

```csharp
    private readonly IReadOnlyList<string> _libraryRoots;
```

```csharp
        _libraryRoots = libraryRoots;
```

`ScanLibrary` 케이스를 바꾼다 — 경로가 오면 그것만, 아니면 전부:

```csharp
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
```

- [ ] **Step 6: `folders` 명령을 더한다**

`src/Mono.Protocol/MessageTypes.cs`의 `ScanLibrary` 줄 아래:

```csharp
    public const string Folders = "folders";
```

`CommandProcessor`의 `ScanLibrary` 케이스 아래:

```csharp
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
```

- [ ] **Step 7: 빌드하고 전체 테스트**

```bash
dotnet build Mono.slnx -v q --nologo
dotnet test Mono.slnx --nologo -v q
```

기대: 경고 0. 테스트 81개 통과(79 + 스캔 루트 2).
`CommandProcessor` 생성자를 부르는 다른 곳(테스트 포함)이 있으면 컴파일러가 잡아준다 — 거기도 목록으로 바꾼다.

- [ ] **Step 8: 커밋**

```bash
git add src/Mono.Core/ src/Mono.Protocol/MessageTypes.cs tests/Mono.Tests/ScanRootTests.cs
git commit -m "Scan multiple library roots and report them over folders

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: 카탈로그 뷰에 장르·작곡가·작품을 싣고 검색 형태를 통일한다

3b단계 화면이 전부 이 한 응답에서 나온다. 그리고 지금 `Search`가 `Catalog`와 다른 모양을 내보내는 버그를 고친다.

**Files:**
- Modify: `src/Mono.Core/CatalogStore.cs` (`CatalogView` · `Search`)
- Test: `tests/Mono.Tests/CatalogViewTests.cs`

**Interfaces:**
- Consumes: `CompositionGrouping.Identify` (Task 1) · `Track.Genres`/`Composers` (Task 2).
- Produces:
  - `CatalogView()` 항목에 `genres` (`string[]`) · `composers` (`string[]`) · `workKey` (`string?`) · `workTitle` (`string?`) 추가
  - `Search(string)` 반환 타입이 `IReadOnlyList<Track>` → `IReadOnlyList<object>` — `CatalogView()`와 **같은 모양**

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`tests/Mono.Tests/CatalogViewTests.cs`:

```csharp
using System.Text.Json;
using Mono.Core;
using Mono.Protocol;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// Control 은 catalog 와 search 응답을 같은 파서로 읽는다.
/// 두 응답의 모양이 어긋나면 검색 결과에서 아티스트·아트·배지가 사라진다.
/// </summary>
public class CatalogViewTests
{
    private static CatalogStore NewStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return new CatalogStore(Path.Combine(dir, "c.db"));
    }

    private static HashSet<string> FieldsOf(object item)
    {
        var json = JsonSerializer.Serialize(item, LineFraming.JsonOptions);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!.Keys.ToHashSet();
    }

    [Fact]
    public void SearchReturnsTheSameShapeAsCatalog()
    {
        var store = NewStore();

        var catalogFields = FieldsOf(store.CatalogView()[0]);
        var searchFields = FieldsOf(store.Search("")[0]);

        Assert.Equal(catalogFields, searchFields);
    }

    [Fact]
    public void SearchStillMatchesArtistAliases()
    {
        var store = NewStore();
        var artist = store.Artists.Values.First();
        artist.AlternateNames.Add("요네즈 켄시");

        var hits = store.Search("요네즈 켄시");

        Assert.NotEmpty(hits);
    }

    [Fact]
    public void SearchDoesNotLeakLocalPaths()
    {
        // 뷰 모양으로 통일되면 파일 경로는 hasLocal 불리언으로만 나간다.
        var store = NewStore();

        Assert.DoesNotContain("localPath", FieldsOf(store.Search("")[0]));
        Assert.Contains("hasLocal", FieldsOf(store.Search("")[0]));
    }

    [Fact]
    public void CatalogViewCarriesGenresComposersAndWork()
    {
        var store = NewStore();
        var track = store.Tracks.Values.First();
        track.Genres.Add("Jazz");
        track.Composers.Add("Ludwig van Beethoven");
        track.Title = "Symphony No. 5 in C minor, Op. 67: I. Allegro con brio";
        store.UpsertTrack(track, store.Albums[track.AlbumId], store.Artists[track.ArtistId]);

        var item = store.CatalogView().Single(v => FieldsOf(v).Contains("workKey")
            && JsonSerializer.Serialize(v, LineFraming.JsonOptions).Contains("Symphony No. 5"));
        var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            JsonSerializer.Serialize(item, LineFraming.JsonOptions))!;

        Assert.Equal("Jazz", json["genres"][0].GetString());
        Assert.Equal("Ludwig van Beethoven", json["composers"][0].GetString());
        Assert.Equal("Symphony No. 5 in C minor, Op. 67", json["workTitle"].GetString());
        Assert.False(string.IsNullOrEmpty(json["workKey"].GetString()));
    }

    [Fact]
    public void TracksWithoutComposerHaveNoWork()
    {
        var store = NewStore();
        var item = store.CatalogView()
            .Select(v => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                JsonSerializer.Serialize(v, LineFraming.JsonOptions))!)
            .First(d => d["composers"].GetArrayLength() == 0);

        Assert.Equal(JsonValueKind.Null, item["workKey"].ValueKind);
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter CatalogViewTests
```

기대: 실패 — `Search`가 다른 모양을 내고 `workKey`가 없다.

- [ ] **Step 3: 뷰를 한 곳으로 모은다**

`src/Mono.Core/CatalogStore.cs`에서 `CatalogView()`의 `Select` 안 익명 객체를 꺼내 재사용 가능한 private 메서드로 만든다:

```csharp
    /// <summary>catalog 와 search 가 공유하는 트랙 뷰. 한 곳에서만 만든다.</summary>
    private object TrackView(Track t)
    {
        var composer = t.Composers.FirstOrDefault();
        var work = CompositionGrouping.Identify(t.Title, composer);
        return new
        {
            t.Id,
            t.Title,
            t.AlbumId,
            t.ArtistId,
            artist = _artists.GetValueOrDefault(t.ArtistId)?.Name,
            artistAliases = _artists.GetValueOrDefault(t.ArtistId)?.AlternateNames ?? [],
            album = _albums.GetValueOrDefault(t.AlbumId)?.Title,
            year = _albums.GetValueOrDefault(t.AlbumId)?.Year,
            label = _albums.GetValueOrDefault(t.AlbumId)?.Label,
            t.SampleRate,
            t.BitDepth,
            t.IsDsd,
            t.DsdRate,
            t.DurationMs,
            t.Source,
            t.StreamingQuality,
            t.MergedLocalAndStreaming,
            genres = t.Genres,
            composers = t.Composers,
            workKey = work?.Key,
            workTitle = work?.Title,
            hasLyrics = !string.IsNullOrWhiteSpace(t.LyricsLrc),
            hasLocal = t.LocalPath is not null,
            artUrl = t.ArtworkPath is null ? null : $"/api/art/{t.Id}",
            badge = QualityPolicyEngine.Badge(t, false)
        };
    }
```

`CatalogView()`의 `.Select(t => (object)new { ... })`를 `.Select(TrackView)`로 바꾼다.

`Search(string query)`의 반환 타입을 `IReadOnlyList<object>`로 바꾸고, 두 `return` 모두 뷰를 거치게 한다:

```csharp
    public IReadOnlyList<object> Search(string query)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return _tracks.Values.Select(TrackView).ToList();
            }

            var q = query.Trim();
            var qNorm = NormalizeForSearch(q);
            return _tracks.Values.Where(t =>
            {
                var album = _albums.GetValueOrDefault(t.AlbumId);
                var artist = _artists.GetValueOrDefault(t.ArtistId);
                return t.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (album?.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (album?.Label?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (album?.Credits?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (artist?.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || t.Genres.Any(g => g.Contains(q, StringComparison.OrdinalIgnoreCase))
                    || t.Composers.Any(c => c.Contains(q, StringComparison.OrdinalIgnoreCase))
                    || MatchesAlias(artist, qNorm);
            }).Select(TrackView).ToList();
        }
    }
```

`Search`를 쓰는 다른 호출자가 `Track` 속성을 기대하고 있으면 컴파일러가 잡아준다:

```bash
grep -rn '\.Search(' src/ --include=*.cs | grep -v obj
```

- [ ] **Step 4: 통과를 확인한다**

```bash
dotnet test tests/Mono.Tests --nologo -v q --filter CatalogViewTests
dotnet test Mono.slnx --nologo -v q
```

기대: 뷰 5개 통과, 전체 86개 통과(81 + 5).

- [ ] **Step 5: 커밋**

```bash
git add src/Mono.Core/CatalogStore.cs tests/Mono.Tests/CatalogViewTests.cs
git commit -m "Give search the catalog's shape and carry genre, composer and work

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## 3a단계 완료 기준

- [ ] `dotnet build Mono.slnx` 경고 0, 오류 0
- [ ] `dotnet test Mono.slnx` 87개 통과 (2단계 70 + 작품 7 + 마이그레이션 2 + 스캐너 1 + 스캔 루트 2 + 뷰 5)
- [ ] 구 스키마 `catalog.db`가 재스캔 없이 열리고 새 컬럼은 빈 값이다
- [ ] `search`와 `catalog` 응답의 필드 집합이 완전히 같다
- [ ] `folders` 명령이 루트별 경로·존재 여부·트랙 수를 돌려준다
- [ ] 작곡가 태그가 없는 트랙은 `workKey`가 null이다 (묶이지 않는다)

## 다음 단계

3b — Control 화면: Genres(하드코딩 제거)·Composers·Compositions·Folders·History·Playlists 전용 뷰,
앨범 상세, 서버측 검색 전환(디바운스 250ms). 3a가 만든 필드 위에서만 의미가 있으므로 3a 완료 후 쓴다.
