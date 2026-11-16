# Mono Control UI 디자인 리뉴얼

`src/Mono.Web` 과 같은 Core 규약(`/ws/control`, `/api/*`)을 쓰는 별도 프런트엔드다. 원본은 건드리지 않는다.
디자인 규칙은 [`DESIGN.md`](DESIGN.md).

```powershell
npm install
npm run mock       # 모의 Core, http://127.0.0.1:7802 (데모 카탈로그 24장 · 168곡)
npm run dev        # http://127.0.0.1:5373
npm run capture    # 창을 띄우지 않고 screenshots/ 에 캡처 (mock · dev 가 떠 있어야 한다)
```

진짜 Core 에 붙일 때는 `$env:CORE = "http://127.0.0.1:7702"; npm run dev`.

| 경로 | 내용 |
| --- | --- |
| `src/styles/tokens.css` | 색·글꼴·치수 토큰 |
| `src/shell`, `src/pages`, `src/modals`, `src/ui` | 새 화면 |
| `src/lib`, `src/state/MonoProvider.tsx` | 원본에서 그대로 가져온 프로토콜·상태 계층 |
| `mock/` | 모의 Core 와 데모 커버 생성기. 배포물이 아니다 |
| `screenshots/` | `npm run capture` 가 만드는 캡처. 저장소에는 싣지 않는다 |
