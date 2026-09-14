# Mono

하이파이 오디오 라운지. **Core / Control / Output** 3단 구조의 분산 오디오 시스템.
혼자서 듣든 같이 듣든, 하나의 소리로 최고의 경험을.

- 기획: [`docs/01-기획명세서.md`](docs/01-기획명세서.md)
- 아키텍처: [`docs/02-아키텍처.md`](docs/02-아키텍처.md)
- 구현 현황: [`docs/03-구현현황.md`](docs/03-구현현황.md)
- UI/UX·**exe-only** 배포: [`docs/04-UIUX기획서.md`](docs/04-UIUX기획서.md)
- 로드맵(v1.0 까지): [`docs/06-로드맵.md`](docs/06-로드맵.md)

## Control UI

Control 화면은 **React 19 + Vite + Tailwind v4** (`src/Mono.Web`)이고,
`Mono.Control.exe` 는 그 화면을 띄우는 **WebView2 셸**입니다.

```
src/Mono.Web      React 앱 (화면 전부)
  └ vite build →  src/Mono.Core/wwwroot   Core 가 7702 에서 정적 서빙
src/Mono.Control  WebView2 창 + Core·Output 프로세스 관리 + 트레이·업데이트
```

셸은 `window.mono` 브리지로 브라우저가 못 하는 일만 열어 줍니다 —
폴더 선택, Output 워커 기동/재시작, 외부 링크, 업데이트, 트레이.
브라우저로 `http://127.0.0.1:7702` 를 열어도 같은 화면이 뜨며,
그때는 브리지가 없어 출력 연결만 막힙니다.

이전 Avalonia UI 는 `legacy/Mono.Control.Avalonia` 에 보관돼 있습니다(빌드 대상 아님).

## 빠른 시작

```powershell
cd src/Mono.Web; npm install; npm run build; cd ../..
dotnet build Mono.slnx
dotnet run --project src/Mono.Control
```

Control이 Core를 찾아 기동하고 창을 엽니다. 소리는 설정 → **오디오 엔진 → 「출력 연결」**
또는 하단 출력 팝오버에서 Output을 붙입니다.

UI만 고칠 때는 Core를 따로 띄우고 Vite 개발 서버를 씁니다(HMR·프록시 설정 포함):

```powershell
dotnet run --project src/Mono.Core
cd src/Mono.Web; npm run dev     # http://localhost:5273
```

| 포트 | 용도 |
| --- | --- |
| 7700 | Control ↔ Core (TCP JSON) |
| 7701 | MATP (오디오 + 클럭) |
| 7702 | Control UI(정적) · REST · `/ws/control` · SignalR `/hub` |
| 5273 | Vite 개발 서버 (개발 중에만) |

### 컨트롤 플레인

웹 UI는 `/ws/control` WebSocket 으로 **TCP 7700 과 똑같은 `MonoMessage` 규약**을 씁니다.
두 전송 계층은 `ControlSession`(`src/Mono.Core/ControlPlane.cs`) 한 곳을 공유하므로
네이티브 Control 과 웹 UI 가 기능 차이 없이 같은 명령을 냅니다.
TypeScript 쪽 거울은 `src/Mono.Web/src/lib/protocol.ts` 입니다.

```powershell
dotnet run --project src/Mono.Cli                              # 개발용 CLI
dotnet run --project src/Mono.Output -- --room=<룸ID>          # Output 수동 실행
```

Output 옵션: `--host=` `--room=` `--invite=` `--name=` `--device=` `--volume=0-100` `--shared` `--dsd`

출시 zip: `Mono.Control.exe` + `Mono.Core.exe` + `Mono.Output.exe` + `wwwroot/` 동일 폴더.
`publish.ps1` 이 React 번들을 먼저 빌드한 뒤 publish 합니다. 규약은 [`docs/04-UIUX기획서.md`](docs/04-UIUX기획서.md) §5.

> WebView2 런타임이 필요합니다. Windows 11 에는 기본 포함돼 있습니다.

## 라이브러리

로컬 음원은 Core `data/library` 또는 설정 경로. Control에서 **라이브러리 스캔**.

```json
{ "Mono": { "LibraryRoot": "D:\\Music", "ScanIntervalMinutes": 30, "ControlUrl": "http://0.0.0.0:7702" } }
```

- 포맷: FLAC / WAV / AIFF / ALAC / M4A / MP3 / AAC / DSF / DFF
- 가사: `.lrc` 또는 태그 내장 · 커버: 태그/`cover.jpg`
- 스트리밍: Tidal / Qobuz 어댑터(데모 카탈로그)

## 개발용 CLI 스모크

```powershell
dotnet run --project src/Mono.Core
dotnet run --project src/Mono.Cli
dotnet run --project src/Mono.Output -- --room=<룸ID>
```

```
create audiophile Night Lounge
add tr-blue-train
play
```

## REST API (Core)

| 경로 | 내용 |
| --- | --- |
| `GET /api/health` | 룸·트랙·엔드포인트 |
| `GET /api/catalog` | 카탈로그 |
| `GET /api/art/{trackId}` | 앨범 아트 |
| `GET /api/reactions/{trackId}` | 반응 히트맵 |
| `GET /api/wiki/{artistId}` | 위키 요약 |
| `GET /api/endpoints` | 등록된 출력 엔드포인트 |
| `WS /ws/control` | Control 컨트롤 플레인 (MonoMessage) |

## 테스트

```powershell
dotnet test Mono.slnx
cd src/Mono.Web; npm run typecheck
```
