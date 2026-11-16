# Mono Control UI

Mono 의 화면이다. React 19 + Vite, 스타일은 일반 CSS(`src/styles`)다. `npm run build` 의 결과가 `../Mono.Core/wwwroot` 로 떨어지고,
Core 가 7702 에서 그대로 내보낸다. 데스크톱 앱(`Mono.Control`)은 그 주소를 WebView2 로 띄울 뿐이다.
디자인 규칙은 [`DESIGN.md`](DESIGN.md).

```powershell
npm install
npm run dev        # http://localhost:5273 — 진짜 Core(7702)에 붙는다
npm run build      # ../Mono.Core/wwwroot
npm run typecheck
```

Core 없이 볼 때는 모의 Core 를 쓴다.

```powershell
npm run mock                                          # 모의 Core, http://127.0.0.1:7802 (데모 카탈로그 24장 · 168곡)
$env:CORE = "http://127.0.0.1:7802"; npm run dev      # 개발 서버를 모의 Core 에 붙인다
npm run capture                                       # 창 없이 screenshots/ 에 캡처 (위 둘이 떠 있어야 한다)
```

캡처는 `ONLY=home,album` 으로 장면을 고르고 `SCALE=2` 로 해상도를 올린다. Chrome 이나 Edge 가 있어야 한다.

| 경로 | 내용 |
| --- | --- |
| `src/styles/tokens.css` | 색 · 글꼴 · 치수 토큰. `DESIGN.md` 와 같이 고친다 |
| `src/shell`, `src/pages`, `src/modals`, `src/ui` | 화면 |
| `src/lib`, `src/state/MonoProvider.tsx` | Core 규약(`protocol.ts`)과 연결 · 상태 계층 |
| `mock/` | 모의 Core 와 데모 커버 생성기. 배포물이 아니다 |
| `screenshots/` | `npm run capture` 가 만드는 캡처. 저장소에는 싣지 않는다 |

브라우저로 열면 `window.mono`(셸 브리지)가 없어 출력 연결 · 장치 목록 · 폴더 선택 · 업데이트가 막힌다. 그 부분은 `Mono.Control` 로 띄워서 본다.
이 화면으로 바꾸기 전의 화면은 `web-v1` 태그에 있다.
