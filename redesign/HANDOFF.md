# 인수인계

2026-11-16 저녁 기준. 다른 PC 에서 이어서 하기 위한 문서다.

함께 읽을 것: [`DESIGN.md`](DESIGN.md)(디자인 규칙), [`README.md`](README.md)(실행 방법), [`../docs/07-백엔드-점검.md`](../docs/07-백엔드-점검.md)(Core 점검 결과).

## 0. 맨 먼저 할 일

```powershell
git fetch origin
git checkout fix/core-hardening
dotnet test Mono.slnx
```

**테스트를 한 번도 돌리지 못한 C# 변경이 이 브랜치에 들어 있다.** 작업한 PC 는 Windows 애플리케이션 제어 정책이
새로 빌드한 테스트 DLL 로딩을 막았다. 빌드는 통과했다(.NET 10 SDK, 경고 0, 오류 0). 그게 확인의 전부다.

테스트가 깨지면 4장을 본다. 깨질 만한 곳을 적어 두었다.

## 1. 지금 상태

| 브랜치 | 내용 | 상태 |
| --- | --- | --- |
| `development` | `redesign/` 폴더가 들어가 있다(PR #4) | 원격과 같다 |
| `feature/web-redesign` | 위 + 이 문서의 이전 판 | 원격과 같다. 더 쓰지 않는다 |
| `fix/core-hardening` | `development` + Core 보안·안정성 수정 + 새 화면을 그 수정에 맞춘 것 + 이 문서 | **이어서 할 곳** |

`fix/core-hardening` 의 커밋:

1. `Stop trusting whoever connects to Core.` — C# 변경 전부와 `src/Mono.Web` 의 `controlClient.ts` · `protocol.ts`.
2. `Follow the Core changes in the redesign, and write the audit down.` — `redesign/` 을 새 규약에 맞춤, `docs/07`.
3. 이 문서를 고친 커밋.

확인된 것과 안 된 것:

| 항목 | 상태 |
| --- | --- |
| C# 빌드 | 통과 |
| C# 테스트(기존 + 새로 쓴 `HardeningTests.cs`) | **안 돌림** |
| 새 화면 타입 검사 · 프로덕션 빌드 | 통과 |
| 새 화면을 모의 Core(`mock/core.mjs`)에 붙여 51개 장면 캡처 | 통과 |
| 새 화면을 **실제 Core** 에 붙이기 | **한 번도 안 함** |
| 고친 Core 를 실제로 띄우기 | **한 번도 안 함** |
| 모션 다섯 군데 | 정지 캡처로만 봄 |

## 2. 작업 순서

1. **테스트.** 0장. 통과할 때까지 고친다.
2. **고친 Core 를 띄워 기존 화면으로 확인.** `dotnet run --project src/Mono.Control`. 3장의 "Core 변경" 목록.
   기존 화면(`src/Mono.Web`)이 그대로 돌아가야 한다. 여기서 문제가 나오면 화면이 아니라 Core 수정의 문제다.
3. **새 화면을 실제 Core 에 붙여 확인.** 3장의 "새 화면" 목록.
   ```powershell
   dotnet run --project src/Mono.Core
   cd redesign; npm install
   $env:CORE = "http://127.0.0.1:7702"; npm run dev     # http://127.0.0.1:5373
   ```
4. **Core 변경을 먼저 합친다.** 화면 이식과 별개로 릴리스할 수 있다. 보안 수정이라 기다릴 이유가 없다.
5. **화면 이식.** 6장.
6. **릴리스.** 7장.

## 3. 확인 목록

### Core 변경 (기존 화면으로)

- [ ] 화면이 뜨고 사이드의 연결 표시가 정상이다. (Origin · Host 검사를 통과해야 한다.)
- [ ] 재생, 일시정지, 탐색, 다음 곡, 대기열 순서 바꾸기.
- [ ] 라운지 열기와 닫기. 다른 창이나 `Mono.Cli` 로 손님 입장.
- [ ] 손님이 나간 뒤 다시 들어올 수 있다. 호스트가 방을 닫은 뒤 손님이 "나가기"를 눌러도 오류가 뜨지 않는다.
- [ ] DSP 끄기가 실제로 꺼진다(신호 경로가 비트 퍼펙트로 돌아온다). 예전에는 꺼지지 않았을 것이다.
- [ ] TIDAL 로그인. (`/oauth/callback` 이 state 를 확인한다.)
- [ ] 설정에서 폴더 추가 → 스캔. 없는 경로를 넣으면 폴더가 생기지 않고 0곡으로 끝난다.
- [ ] 큰 라이브러리에서 Core 가 전보다 빨리 뜬다. (첫 스캔을 기다리지 않는다.)
- [ ] 대기열 마지막 곡에서 자동 재생 후보가 뜬다. (살펴보는 곡 수를 48로 줄였다.)
- [ ] 출력 워커가 붙고 소리가 난다. 다른 PC 의 출력 워커를 쓴다면 그 구성도. (7700 · 7701 의 줄 읽기를 바꿨다.)
- [ ] 네트워크가 잠깐 끊겼다 붙어도 방에 그대로 있다. (같은 peer id 를 비밀값으로 되찾는다.)
- [ ] 아무 사이트나 열고 개발자 콘솔에서 `new WebSocket("ws://127.0.0.1:7702/ws/control")` → 거절된다.

### 새 화면 (실제 Core 에 붙여서)

브라우저에서는 `window.mono`(셸 브리지)가 없어 출력 연결 · 장치 목록 · 폴더 선택이 막힌다. 그 항목은 이식 뒤 `Mono.Control` 로 본다.

- [ ] 사이드바에 `Core 연결됨 · N곡`, 라이브러리가 채워진다.
- [ ] 앨범 재생 → 플레이어 바의 칩이 `24/96 · BIT-PERFECT`. 출력이 없으면 `출력 없음`.
- [ ] 신호 경로 팝오버의 DSP 토글: 켜면 칩이 앰버 `DSP`, 끄면 초록.
- [ ] 대기열: 끌어 옮기기, 빼기, 비우기, "다음에 재생".
- [ ] 진행 바를 끌어 옮기면 그 자리에 머문다.
- [ ] 라운지 열기 → `ON AIR`, 링이 한 번 퍼진다. 방송 종료 후에도 듣던 곡이 이어진다.
- [ ] 손님 화면에서 트랜스포트가 막힌다.
- [ ] 라운지 목록의 표지가 커버 콜라주로 나온다(`list_rooms` 의 `covers`).
- [ ] 태그에 커버가 없는 곡에 깨진 이미지가 보이지 않는다.
- [ ] 실제 커버(JPEG/PNG)에서 앨범 헤더의 색이 뽑힌다(`src/ui/motion.tsx` 의 `useArtTint`).
- [ ] 검색 3글자 이상 → TIDAL 결과가 섞여 들어온다.
- [ ] 아티스트 화면의 소개글(위키).
- [ ] 큰 라이브러리에서 앨범 탭이 버벅이지 않는가. 가상 스크롤이 없다.
- [ ] 모션 다섯 군데가 `DESIGN.md` 4장대로 보인다.

알려진 한계: **창을 새로 고치면 peer id 가 바뀐다.** 웹 클라이언트가 id 를 메모리에만 들고 있어서다.
열어 둔 라운지의 호스트 자리를 잃는다. 원래부터 그랬고 이번에 고치지 않았다(`docs/07` 6장).

## 4. 테스트가 깨질 만한 곳

기존 테스트를 읽어 보고 충돌이 없다고 판단했지만 돌려 보지는 못했다. 깨진다면 아래 중 하나일 가능성이 높다.

| 바꾼 것 | 깨질 수 있는 경우 | 위치 |
| --- | --- | --- |
| 방 id 를 지정한 명령은 멤버만 | 방에 들어가지 않은 peer 로 `commands.Execute(..., RoomId = ...)` 를 부르고 성공을 기대하는 테스트 | `CommandProcessor.NeedRoom`, `RoomManager.IsMember` |
| `leave_room` 을 멤버가 아닌 사람이 보내면 응답이 없다(`default`) | 오류 응답을 기대하는 테스트 | `CommandProcessor` LeaveRoom |
| 솔로 방은 Control 로 주인만 입장 | 솔로 방에 두 번째 Control 이 `Join` 하는 테스트. 출력(`PeerRole.Output`)은 그대로 된다 | `RoomManager.Join` |
| 자동 재생 후보가 없어도 `AutoplayProposedForTrackId` 를 적는다 | 후보가 없을 때 이 값이 null 이길 기대하는 테스트 | `RoomManager.ProposeAutoplayCandidates` |
| `Recommend` 에 카탈로그 전체 수 대신 48 을 넘긴다 | 후보가 49번째 이후에만 있는 경우를 다루는 테스트 | `RoomManager.AutoplayPoolSize` |
| `Scan` 이 없는 폴더를 만들지 않는다 | 폴더가 생기길 기대하는 테스트 | `LibraryScanner.Scan` |
| `EnsureThumbnail` 이 원본 경로를 그대로 돌려준다 | `thumbs` 폴더에 파일이 생기길 기대하는 테스트 | `ArtworkService` |
| `set_dsp` 가 `flag=false` 면 프리셋을 Off 로 | `flag=false` 와 프리셋을 같이 보내고 프리셋이 남길 기대하는 테스트 | `CommandProcessor` SetDsp |
| `Execute` 에 `bool local = true` 인자 추가 | 리플렉션이나 델리게이트로 시그니처를 고정한 곳 | `CommandProcessor.Execute` |
| `MonoMessage` 에 `Secret` 추가 | 필드 수를 세는 테스트 | `MonoMessage.cs` |

새로 쓴 `tests/Mono.Tests/HardeningTests.cs` 가 실패하면, 테스트의 가정이 틀렸을 수도 있다. 특히:
`ChatLinesAreClipped`(채팅이 한 줄로 그대로 들어간다고 가정), `TurningDspOffWithTheFlagClearsThePreset`(솔로 방에서 DSP 가 켜진다고 가정).

## 5. Core 규약에서 알아 둘 것

| 사실 | 화면에서 한 일 |
| --- | --- |
| welcome 이 `secret` 을 준다. 주인이 있는 peer id 는 hello 에 그 값을 돌려줘야 되찾는다 | `controlClient.ts`(양쪽 화면 모두) |
| `list_rooms` 가 `covers` · `nowTitle` · `nowArtist` · `upcoming` 을 싣는다 | 라운지 표지와 "지금 곡" 한 줄. 필드가 없으면(예전 Core) 패턴 표지 |
| `set_dsp` 는 프리셋이 Off(0)면 끈다. 이제 `flag=false` 도 끄기로 받는다 | 끌 때 프리셋 0 을 보낸다. 마지막 프리셋은 `localStorage`(`mono_dsp_preset`) |
| `artUrl` 은 곡에 커버가 없으면 앨범 커버로 물러난다 | 따로 할 일 없음 |
| `/api/wiki/{id}` 는 `extract` · `sourceUrl` 을 보낸다(`summary` 가 아니다) | `rest.ts` 타입, 아티스트 화면 |
| `badge` 는 `24/96000 · Bit-perfect Local` 형식. DR 값이 아니다 | 음질은 `sampleRate` · `bitDepth` 에서 계산 |
| `set_room_flags` 는 호스트만. 자동 재생 플래그는 `smart_autoplay` | 그대로 사용 |
| `set_volume` 은 대상이 없으면 보낸 사람 자신에게 건다 | 출력이 없으면 볼륨을 막아 둠 |
| REST 는 이 PC 에서 온 요청만 받는다(커버 · 상태 확인 · 스트림 프록시 제외) | 따로 할 일 없음 |
| 방을 닫거나 쫓아내도 당사자에게 알림이 가지 않는다 | 화면은 다음 명령이 실패할 때 알아챈다. 고치지 않음 |
| `list_rooms` 는 바뀔 때 밀어 주지 않는다 | 라운지 화면이 8초마다 다시 묻는다 |

원본과 다른 화면 쪽 파일: `lib/rest.ts`(WikiBio 타입), `lib/model.ts`(새 파일), `lib/dsp.ts`(새 파일).
`controlClient.ts` 와 `protocol.ts` 는 `src/Mono.Web` 과 `redesign` 을 똑같이 고쳤다.
`adapters.ts` 와 `data/types.ts` 는 새 화면이 쓰지 않는다. `MonoProvider` 가 가져다 쓰기 때문에 남겨 두었다.

## 6. 화면 이식 순서

Core 변경이 `development` 에 들어간 뒤에 한다. 시작 전에 지금 화면에 태그를 단다(`git tag web-v1`).

1. **빌드 설정.** `src/Mono.Web/package.json` 에서 `tailwindcss`, `@tailwindcss/vite` 를 빼고 `redesign/package.json` 의 글꼴 패키지를 넣는다.
   `vite.config.ts` 는 Tailwind 플러그인만 빼고 출력 경로(`../Mono.Core/wwwroot`) · 포트(5273) · 프록시(7702)는 그대로 둔다.
   `index.html` 에 `data-theme="dark"` 를 단다(원본은 `html.dark` 클래스였다).
2. **추가 파일.** `lib/model.ts`, `lib/dsp.ts`, `rest.ts` 의 `WikiBio`.
3. **스타일과 공용 부품.** `styles/`, `ui/`, `state/app.tsx`.
4. **화면 교체.** `shell/`, `pages/`, `modals/`, `App.tsx`, `main.tsx` 를 넣고 `components/` 와 `index.css` 를 지운다.
5. **정리.** `lib/adapters.ts`, `lib/homeData.ts`, `lib/libraryData.ts`, `state/useLiveSession.ts`, `data/types.ts` 를 지운다.
   그 전에 `MonoProvider` 에서 `albums` 와 `groupIntoAlbums` 를 걷어낸다.
6. **개발 도구와 문서.** `mock/`, `scripts/capture.mjs` 를 옮긴다. `DESIGN_SYSTEM.md` · `COMPONENTS.md`(루트와 `docs/` 에 두 벌)를 `DESIGN.md` 로 바꾼다.
   저장소 `README.md` 의 "React 19 + Vite + Tailwind v4" 문구를 고친다.
7. **`redesign/` 삭제.**

커밋마다 `npm run typecheck`, `npm run build`, `dotnet test Mono.slnx`.
마지막에 `Mono.Control` 로 띄워, 브라우저에서 못 본 것을 본다: 출력 연결, 설정의 장치 목록, 폴더 추가, 업데이트 확인, Discord, 트레이로 숨기기, 첫 실행 마법사.

주의:

- `publish.ps1` 은 `node_modules` 가 없으면 `npm ci` 를 돌린다. `package-lock.json` 을 새로 만들어 같이 커밋하지 않으면 CI 에서 멈춘다.
- 글꼴을 번들에 실어 `wwwroot` 가 약 6.4MB 가 된다. 원본은 Google Fonts 를 온라인으로 받았다.
- `localStorage` 키는 원본과 같다(`mono_theme`, `mono_setup`, `mono.displayName`). 새로 생긴 것은 `mono_dsp_preset`.

## 7. 릴리스

1. `Directory.Build.props` 의 `<Version>` 을 올린다. `publish.ps1` 에는 `0.5.9.x` 를 `0.5.10-rc.x` 로 바꾸는 표가 있다.
   `0.6.0` 처럼 세 자리로 올리면 표를 건드릴 필요가 없다.
2. `CHANGELOG.md` 에 같은 버전의 절을 쓴다. 그 절이 GitHub 릴리스 본문으로 나간다. `docs/07` 2장의 "동작이 달라지는 점"을 옮겨 적는다.
3. `development` 에 합친 뒤 `git tag v0.6.0 && git push origin v0.6.0`, 또는 Actions 의 `release` 를 수동 실행한다.

Core 변경만 먼저 낼 수도 있다. 그 경우 화면은 기존 것이 나간다.

## 8. 정해지지 않은 것

### Core (`docs/07` 3 · 5장)

1. **7701 포트.** 인증이 없고 LAN 전체에 열려 있다. 기본을 루프백으로 바꾸고 LAN 은 설정으로 켤지. 다른 PC 의 출력 워커를 쓰는 구성이 영향을 받는다.
2. **토큰 저장.** 스트리밍 토큰과 GitHub 토큰이 평문이다. DPAPI 로 감쌀지, GitHub 토큰은 아예 안 쓸지.
3. **계정.** 사람별 기록 · 플레이리스트 분리, 쫓아낸 사람의 재입장 차단. 라운지를 인터넷으로 열 계획이 있는지에 달렸다.
4. **코드 서명.** CI 빌드가 서명되지 않는다.
5. **곡 id 체계.** 파일 이름에서 나와서 같은 이름의 파일이 서로 덮어쓴다. 고치려면 기존 DB 이전이 필요하다.
6. **.NET 10.** .NET 8 지원이 2026-11-10 에 끝났다. 테스트가 도는 환경에서 올린다.

### 화면

1. **원본에서 뺀 기능을 되살릴지.** 로컬 다중 프로필, LENS 의 PEQ · 크로스피드 프리셋과 RAM 버퍼 같은 설정 항목,
   플레이리스트 공유 카드, 초대 링크 입장 시의 "스트리밍 계정 필요 / 데스크톱 앱 필요" 확인, 라운지 목록의 표 보기.
   대부분 Core 에 저장되지 않던 화면 전용 상태다.
2. **바뀐 조작 방식.** 곡 재생이 "행 클릭 → 메뉴"에서 "더블클릭 재생 + ⋯ 메뉴(우클릭)"로 바뀌었다. 라운지 중에도 지금 재생 화면을 열 수 있다.
3. **릴리스 버전.**

확정된 것(다시 묻지 않는다): 색 · 글꼴 · 모션 다섯 군데 · 버튼 8px, 라운지 표지의 "서로 다른 앨범 커버 우선 + 다음 N곡",
원형으로 남는 요소(재생 버튼 · 채팅 전송 버튼 · 토글 · 아바타), 빨간 `ON AIR` 배지와 인디고 액션 버튼, 지금 재생 화면의 어두운 캔버스, `DESIGN.md` 의 파생 색.

## 9. 손볼 때 지킬 것

- 색 · 글꼴 · 반지름 · 모션은 `DESIGN.md` 와 `src/styles/tokens.css` 에 확정된 값만 쓴다. 새 값이 필요하면 임의로 고르지 말고 정한 뒤 두 곳에 같이 적는다.
- 모션은 다섯 군데뿐이다. 등장 페이드, 호버에서 떠오르거나 커지는 효과, 깜빡이는 램프는 넣지 않는다.
- 앰버는 DSP · SRC 개입에만, 빨강은 On Air 에만 쓴다.
- 화면 문구에 엠대시(—)를 쓰지 않는다. 이모지를 아이콘으로 쓰지 않는다.
- Core 가 보내지 않는 정보를 화면에 지어내지 않는다.
- **모의 Core 는 실제 Core 와 같게 유지한다.** 모의 쪽이 너그러우면 버그가 가려진다. DSP 끄기와 위키 필드가 그렇게 가려져 있었다.
- Core 의 규약을 바꾸면 C# 을 먼저 고치고 `protocol.ts` 를 양쪽 화면에서 같이 고친다.

## 10. 작업한 PC 에만 있는 것

- **캡처.** `redesign/screenshots/` 는 저장소에 없다. 새 화면은 `npm run capture` 로 다시 만든다(모의 Core 와 개발 서버를 먼저 띄운다).
  기존 화면 캡처(`before`)는 스크립트로 만들 수 없다.
- **임시 .NET SDK.** 작업한 PC 의 임시 폴더에만 받아 썼다. 시스템에는 설치하지 않았다.
- **작업 메모.** 대화 기록과 메모는 옮겨지지 않는다. 필요한 것은 이 문서와 `DESIGN.md`, `docs/07` 에 적었다.
