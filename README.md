# Mono

하이파이 오디오 라운지. **Core / Control / Output** 3단 구조의 분산 오디오 시스템.
혼자서 듣든 같이 듣든, 하나의 소리로 최고의 경험을.

- 기획: [`docs/01-기획명세서.md`](docs/01-기획명세서.md)
- 아키텍처: [`docs/02-아키텍처.md`](docs/02-아키텍처.md)
- 구현 현황: [`docs/03-구현현황.md`](docs/03-구현현황.md)
- UI/UX·**exe-only** 배포: [`docs/04-UIUX기획서.md`](docs/04-UIUX기획서.md)

## 최근 추가된 기능

- **Avalonia 네이티브 Control**: Roon형 사이드바 + 본문 + 하단 플레이어 바. 브라우저·콘솔 없이 `Mono.Control.exe`
- **프로세스 수명 관리**: Control이 Core·Output을 `WinExe` 백그라운드로 기동
- **온보딩**: Avalonia 6단계 (이름 → 라이브러리 → 출력/존 → 스트리밍 → 완료)
- 반응 히트맵 · 스마트 오토플레이 · 멀티 디바이스 존 · 다국어 검색 · 위키백과 이력

## 빠른 시작

```powershell
dotnet build Mono.slnx
dotnet run --project src/Mono.Control
```

Control이 Core를 찾아 기동하고 GUI를 엽니다. 소리는 하단 **「출력 연결」** 또는 온보딩에서 Output을 붙입니다.

| 포트 | 용도 |
| --- | --- |
| 7700 | Control ↔ Core (TCP JSON) |
| 7701 | MATP (오디오 + 클럭) |
| 7702 | REST·앨범 아트 API (레거시 웹 SPA는 참고용) |

```powershell
dotnet run --project src/Mono.Cli                              # 개발용 CLI
dotnet run --project src/Mono.Output -- --room=<룸ID>          # Output 수동 실행
```

Output 옵션: `--host=` `--room=` `--invite=` `--name=` `--device=` `--volume=0-100` `--shared` `--dsd`

출시 zip: `Mono.Control.exe` + `Mono.Core.exe` + `Mono.Output.exe` 동일 폴더. 규약은 [`docs/04-UIUX기획서.md`](docs/04-UIUX기획서.md) §5.

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

## 테스트

```powershell
dotnet test Mono.slnx
```
