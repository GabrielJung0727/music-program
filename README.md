# Auralis

하이파이 오디오 라운지. **Core / Control / Output** 3단 구조의 분산 오디오 시스템.

- 기획: [`docs/01-기획명세서.md`](docs/01-기획명세서.md)
- 아키텍처: [`docs/02-아키텍처.md`](docs/02-아키텍처.md)
- 구현 현황: [`docs/03-구현현황.md`](docs/03-구현현황.md)

## 빠른 시작

```powershell
dotnet build Auralis.slnx
dotnet run --project src/Auralis.Core
```

| 포트 | 용도 |
| --- | --- |
| 7700 | Control 컨트롤 플레인 (TCP, 줄 단위 JSON) |
| 7701 | AATP 엔드포인트 (컨트롤 + 오디오 데이터 플레인) |
| 7702 | Control 웹 UI · REST API |

Control UI: <http://127.0.0.1:7702> — 화면과 명령만 담당하고 **소리는 내지 않는다.**

```powershell
dotnet run --project src/Auralis.Control            # 같은 프로토콜의 CLI (help 로 명령 목록)
dotnet run --project src/Auralis.Output -- --room=<룸ID>   # DAC 엔드포인트
```

Output 옵션: `--host=` `--room=` `--invite=` `--name=` `--device=<이름 일부>` `--volume=0-100`
`--shared`(배타 모드 강제 해제) `--dsd`(DSD 네이티브 수신 선언)

## 라이브러리

로컬 음원은 `src/Auralis.Core/bin/Debug/net8.0/data/library` 에 넣고 Control에서 **라이브러리 스캔**.
`appsettings.json` 또는 환경변수로 경로·주기를 바꿀 수 있다.

```json
{ "Auralis": { "LibraryRoot": "D:\\Music", "ScanIntervalMinutes": 30, "ControlUrl": "http://0.0.0.0:7702" } }
```

- 포맷: FLAC / WAV / AIFF / ALAC / M4A / MP3 / AAC / DSF / DFF (태그·커버·트랙번호 인식)
- 가사: 같은 이름의 `.lrc` 사이드카 또는 태그 내장 가사
- 커버: 태그 내장 이미지 → `data/art` 캐시, 폴더의 `cover.jpg`/`folder.jpg`도 인식
- 스트리밍: Tidal / Qobuz 어댑터(현재 데모 카탈로그). 토큰은 Core에만 남고 Control로 나가지 않는다.

## 3단 스모크 테스트

터미널 3개:

```powershell
dotnet run --project src/Auralis.Core
dotnet run --project src/Auralis.Control
dotnet run --project src/Auralis.Output -- --room=<룸ID>
```

Control CLI 예시:

```
create audiophile Night Lounge
add tr-blue-train
play
pin 12000 horn entrance
heart
end yes
```

Output은 붙는 즉시 클럭을 맞추고 콘솔에 락 상태를 찍는다.

```
clock offset=-0.16ms jitter=0.33ms rtt=1.96ms target=6ms depth=20ms resync=0 drop=3 Exclusive
```

## REST API

| 경로 | 내용 |
| --- | --- |
| `GET /api/health` | 룸·트랙·온라인 엔드포인트 수 |
| `GET /api/rooms`, `/api/rooms/{id}` | 룸 목록 · 룸 스냅샷 |
| `GET /api/catalog`, `/api/graph/{artistId}` | 카탈로그 · 메타데이터 그래프 |
| `GET /api/endpoints` | 알려진 출력 엔드포인트(오프라인 포함) |
| `GET /api/archives`, `/api/playlists` | 세션 아카이브 · 개인 플레이리스트 |
| `GET /api/art/{trackId}` | 앨범 아트 캐시 |
| `GET /api/m3u/{playlistId}` | 플레이리스트 M3U 내보내기 |
| `GET /api/session/{archiveId}` | 세션 요약(트랙 식별자·메타만) |

## 테스트

```powershell
dotnet test Auralis.slnx
```
