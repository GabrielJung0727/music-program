# 인수인계: Control UI 리디자인

2026-11-16 기준. 다음 작업은 **실제 Core 에 붙여 확인 → `src/Mono.Web` 에 이식 → 릴리스** 순서다.
디자인 규칙은 [`DESIGN.md`](DESIGN.md), 실행 방법은 [`README.md`](README.md).

## 1. 지금 상태

- `redesign/` 은 `development` 에 들어가 있다(PR #4). 이 문서와 그 뒤의 작업은 브랜치 `fix/core-hardening` 에 있다.
- `redesign/` 은 원본과 같은 Core 규약(`/ws/control`, `/api/*`)을 쓰는 별도 프런트엔드다.
- **`fix/core-hardening` 은 Core(C#)도 고친다.** 보안 점검에서 나온 것들이고, 내용과 남은 문제는 [`docs/07-백엔드-점검.md`](../docs/07-백엔드-점검.md) 에 있다.
  빌드는 통과했지만 **테스트는 돌리지 못했다.** 집에서 가장 먼저 `dotnet test Mono.slnx` 를 돌린다.
- 타입 검사(`npm run typecheck`)와 프로덕션 빌드(`npm run build`)는 통과한다.
- **실제 Core 에는 한 번도 붙여 보지 않았다.** 작업한 PC 에 .NET SDK 가 없어서 `mock/core.mjs`(모의 Core)로만 돌렸다. 모션 다섯 군데도 정지 캡처로만 확인했다.
- 캡처(`screenshots/`)는 저장소에 없다. `npm run capture` 로 다시 만든다. 기존 화면 캡처(`before`)는 스크립트로 만들 수 없고 작업한 PC 에만 있다.

### 원본과 다른 파일

`src/lib`, `src/state/MonoProvider.tsx`, `src/state/useDebounced.ts`, `src/data/types.ts` 는 원본과 같다(`controlClient.ts` 와 `protocol.ts` 는 Core 변경에 맞춰 양쪽을 똑같이 고쳤다). 다른 것은 셋뿐이다.

| 파일 | 내용 |
| --- | --- |
| `src/lib/rest.ts` | `WikiBio` 타입을 Core 가 실제로 보내는 필드(`extract`, `sourceUrl`)로 고침 |
| `src/lib/model.ts` (새 파일) | 카탈로그에서 앨범·아티스트를 접는 뷰 모델. 곡을 제목이 아니라 id 로 다룬다 |
| `src/lib/dsp.ts` (새 파일) | DSP 켜고 끄기. 아래 3장 참고 |

`adapters.ts` 와 `data/types.ts` 는 새 화면이 쓰지 않는다. `MonoProvider` 가 `groupIntoAlbums` 를 가져다 쓰기 때문에 남겨 두었다.

## 2. 0단계: 실제 Core 로 확인 (이식 전에 한다)

```powershell
dotnet run --project src/Mono.Core          # 또는 설치된 Mono 실행
cd redesign
npm install
$env:CORE = "http://127.0.0.1:7702"; npm run dev     # http://127.0.0.1:5373
```

브라우저에서는 `window.mono`(셸 브리지)가 없어 출력 연결·장치 목록·폴더 선택이 막힌다. 그 항목은 4단계에서 `Mono.Control` 로 다시 본다.

### 확인할 것

- [ ] 접속하면 사이드바에 `Core 연결됨 · N곡` 이 뜨고 라이브러리가 채워진다.
- [ ] 앨범 재생 → 플레이어 바의 칩이 `24/96 · BIT-PERFECT` 로 뜬다. 출력이 없으면 `출력 없음`.
- [ ] 신호 경로 팝오버의 DSP 토글: 켜면 칩이 앰버 `DSP`, 끄면 초록으로 돌아온다.
- [ ] 대기열: 순서 끌어 옮기기, 빼기, 비우기, "다음에 재생".
- [ ] 진행 바를 끌어 옮기면 그 자리에 머문다(되돌아가지 않는다).
- [ ] 라운지 열기 → 플레이어 바에 `ON AIR`, 링이 한 번 퍼진다. 방송 종료 후에도 듣던 곡이 이어진다.
- [ ] 다른 창(또는 `Mono.Cli`)으로 같은 방에 들어가면 게스트 화면이 뜨고 트랜스포트가 막힌다.
- [ ] 창을 새로 고쳐도 호스트/게스트가 그대로다(`hostPeerId` 와 내 `peerId` 비교로 판정한다. `src/lib/model.ts` 의 `sessionRole`).
- [ ] 라운지 목록의 표지가 커버 콜라주로 나온다(`list_rooms` 의 `covers` 에서 온다).
- [ ] 다른 사이트에서 연 페이지로는 Core 에 붙지 못한다. 예: 아무 사이트의 개발자 콘솔에서 `new WebSocket("ws://127.0.0.1:7702/ws/control")` 가 거절된다.
- [ ] 연결이 잠깐 끊겼다 다시 붙어도 같은 peer id 를 유지한다(welcome 의 `secret` 을 hello 에 돌려준다).
- [ ] 태그에 커버가 없는 곡: `artUrl` 이 null 이라 자리표시 그림이 나온다. 깨진 이미지가 보이면 안 된다.
- [ ] 실제 커버(JPEG/PNG)에서 앨범 헤더의 색이 뽑힌다(`src/ui/motion.tsx` 의 `useArtTint`).
- [ ] 자동 재생 후보가 대기열 패널에 뜨고 고를 수 있다(Core 는 곡이 끝나기 30초 전에 보낸다).
- [ ] 검색 3글자 이상 → TIDAL 결과가 섞여 들어온다.
- [ ] TIDAL 연결(OAuth 왕복), Qobuz 토큰 연결, 연결 끊기.
- [ ] 아티스트 화면의 소개글(위키).
- [ ] 큰 라이브러리에서 앨범 탭이 버벅이지 않는가. 가상 스크롤이 없어서 앨범을 전부 그린다. 곡 탭은 200개씩 끊어 그린다.
- [ ] 모션 다섯 군데가 `DESIGN.md` 4장대로 보인다.

문제가 나오면 `redesign/` 에서 먼저 고치고, **모의 Core 도 실제와 같게 맞춘다**. 모의 Core 가 실제보다 너그러우면 버그가 가려진다(아래 3장의 DSP 가 그렇게 가려져 있었다).

## 3. Core 소스와 대조해서 알아낸 것

| 사실 | 근거 | 화면에서 한 일 |
| --- | --- | --- |
| `set_dsp` 는 0.5.9.3 까지 `flag` 를 보지 않았다. 프리셋이 Off(0)가 아니면 켜졌다 | `CommandProcessor.cs` SetDsp | 화면은 끌 때 프리셋 0 을 보내고, 마지막 프리셋은 `localStorage`(`mono_dsp_preset`)에 적어 둔다. Core 도 `flag=false` 를 끄기로 받게 고쳤다 |
| 원본 화면은 끌 때 현재 프리셋과 `flag=false` 를 보낸다 | `src/Mono.Web/.../PlayerBar.tsx:718` | 예전 Core 에서는 꺼지지 않았을 것이다. 고친 Core 에서는 꺼진다 |
| peer id 는 접속하는 쪽이 골랐고, 방의 권한은 전부 그 id 비교였다 | `ControlPlane.cs`, `RoomManager.cs` | welcome 이 `secret` 을 준다. 같은 id 로 다시 붙으려면 hello 에 돌려줘야 한다(`controlClient.ts`) |
| `/api/wiki/{id}` 는 `extract` · `sourceUrl` 을 보낸다 | `Mono.Shared/Catalog.cs` ArtistWikiSummary | `rest.ts` 타입과 아티스트 화면을 맞춤 |
| `badge` 는 `24/96000 · Bit-perfect Local` 형식이다. DR 값이 아니다 | `QualityPolicyEngine.Badge` | 음질은 `sampleRate` · `bitDepth` 에서 직접 계산. DR 표기는 뺐다 |
| `set_room_flags` 는 호스트만 된다. 자동 재생 플래그 이름은 `smart_autoplay` | `RoomManager.ApplyHostSettings` | 그대로 사용 |
| `list_rooms` 에는 곡 정보가 없었다 | `CommandProcessor.cs` ListRooms | Core 가 `covers` · `nowTitle` · `nowArtist` · `upcoming` 을 싣게 고쳤고, 화면은 그걸 쓴다 |
| `GET /api/rooms/{id}` 는 초대 전용 방의 스냅샷도 코드 없이 준다 | `Program.cs` | 화면은 더 부르지 않는다. 이제 이 PC 에서 온 요청만 받는다 |
| 창을 새로 고치면 peer id 가 바뀐다(메모리에만 있다) | `controlClient.ts` | 열어 둔 라운지의 호스트 자리를 잃는다. 원래부터 그랬다. 2장의 "새로 고쳐도 그대로다" 항목은 이 때문에 실패할 수 있다 |
| `set_volume` 은 대상이 없으면 보낸 사람 자신에게 건다 | `CommandProcessor.cs` SetVolume | 출력이 없으면 볼륨을 막아 둠 |

## 4. 이식 순서

`feature/web-redesign` 에서 이어서 한다. 시작 전에 지금 화면에 태그를 단다(`git tag web-v1 development`).

0. **Core 변경을 먼저 합친다.** `fix/core-hardening` 의 C# 변경은 화면 이식과 별개로 릴리스할 수 있다. 테스트가 통과하면 이것부터 `development` 에 넣는다.
1. **빌드 설정.** `src/Mono.Web/package.json` 에서 `tailwindcss`, `@tailwindcss/vite` 를 빼고 글꼴 넷(`@fontsource-variable/plus-jakarta-sans`, `@fontsource-variable/inter`, `@fontsource-variable/jetbrains-mono`, `wanted-sans`)과 `pretendard` 를 넣는다. `vite.config.ts` 는 Tailwind 플러그인만 빼고 출력 경로(`../Mono.Core/wwwroot`)·포트(5273)·프록시(7702)는 그대로 둔다. `index.html` 에 `data-theme="dark"` 를 단다(원본은 `html.dark` 클래스였다).
2. **추가 파일.** `lib/model.ts`, `lib/dsp.ts`, `rest.ts` 의 `WikiBio`.
3. **스타일과 공용 부품.** `styles/`, `ui/`, `state/app.tsx`.
4. **화면 교체.** `shell/`, `pages/`, `modals/`, `App.tsx`, `main.tsx` 를 넣고 `components/` 와 `index.css` 를 지운다.
5. **정리.** `lib/adapters.ts`, `lib/homeData.ts`, `lib/libraryData.ts`, `state/useLiveSession.ts`, `data/types.ts` 를 지운다. 그 전에 `MonoProvider` 에서 `albums` 와 `groupIntoAlbums` 를 걷어내야 한다.
6. **개발 도구와 문서.** `mock/`, `scripts/capture.mjs` 를 옮긴다. `DESIGN_SYSTEM.md` · `COMPONENTS.md`(루트와 `docs/` 에 두 벌)를 `DESIGN.md` 로 바꾼다. 저장소 `README.md` 의 "React 19 + Vite + Tailwind v4" 문구를 고친다.
7. **`redesign/` 삭제.**

커밋마다: `npm run typecheck`, `npm run build`, `dotnet test Mono.slnx`.
마지막에: `dotnet run --project src/Mono.Control` 로 WebView2 에서 2장의 목록을 다시 보고, 브라우저에서 못 본 것(출력 연결, 설정의 장치 목록, 폴더 추가, 업데이트 확인, Discord, 트레이로 숨기기, 첫 실행 마법사)을 본다.

주의할 점:

- `publish.ps1` 은 `node_modules` 가 없으면 `npm ci` 를 돌린다. `package-lock.json` 을 새로 만들어 같이 커밋하지 않으면 CI 에서 멈춘다.
- 글꼴을 번들에 실어서 `wwwroot` 가 약 6.4MB 가 된다. 원본은 Google Fonts 를 온라인으로 받았다.
- `localStorage` 키는 원본과 같다(`mono_theme`, `mono_setup`, `mono.displayName`). 새로 생긴 것은 `mono_dsp_preset` 하나다.
- Core, 프로토콜, `setup.json` 형식은 바뀌지 않는다. 되돌릴 때는 머지 커밋만 되돌리면 된다.

## 5. 릴리스

1. `Directory.Build.props` 의 `<Version>` 을 올린다. `publish.ps1` 에는 `0.5.9.x` 를 `0.5.10-rc.x` 로 바꾸는 표가 있다. `0.6.0` 처럼 세 자리로 올리면 표를 건드릴 필요가 없다.
2. `CHANGELOG.md` 에 같은 버전의 절을 쓴다. 그 절이 GitHub 릴리스 본문으로 나간다.
3. `development` 에 합친 뒤 `git tag v0.6.0 && git push origin v0.6.0`, 또는 Actions 의 `release` 를 수동 실행한다.

## 6. 정해지지 않은 것

1. **원본에서 뺀 기능을 되살릴지.** 대부분 Core 에 저장되지 않던 화면 전용 상태다.
   - 로컬 다중 프로필
   - LENS 의 PEQ·크로스피드 프리셋, RAM 버퍼, 메모리 재생 같은 설정 항목
   - 플레이리스트 공유 카드
   - 초대 링크로 들어올 때의 "스트리밍 계정 필요 / 데스크톱 앱 필요" 확인 단계
   - 라운지 전체 목록의 표 보기
2. **바뀐 조작 방식을 유지할지.** 곡 재생이 "행 클릭 → 메뉴"에서 "더블클릭 재생 + ⋯ 메뉴(우클릭)"로 바뀌었다. 라운지 중에도 지금 재생 화면을 열 수 있다.
3. **릴리스 버전.**

## 7. 디자인을 손볼 때 지킬 것

- 색·글꼴·반지름·모션은 `DESIGN.md` 와 `src/styles/tokens.css` 에 확정된 값만 쓴다. 새 값이 필요하면 임의로 고르지 말고 정한 뒤 두 곳에 같이 적는다.
- 모션은 다섯 군데뿐이다. 등장 페이드, 호버에서 떠오르거나 커지는 효과, 깜빡이는 램프는 넣지 않는다.
- 앰버는 DSP·SRC 개입에만, 빨강은 On Air 에만 쓴다.
- 화면 문구에 엠대시(—)를 쓰지 않는다. 이모지를 아이콘으로 쓰지 않는다.
- Core 가 보내지 않는 정보를 화면에 지어내지 않는다.
- 원형으로 남기기로 한 것: 재생 버튼, 채팅 전송 버튼, 토글, 아바타.
