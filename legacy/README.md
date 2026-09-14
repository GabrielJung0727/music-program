# legacy

솔루션(`Mono.slnx`)에서 빠져 있는 보관용 코드. 빌드 대상이 아니다.

## Mono.Control.Avalonia

2026-09 이전의 Avalonia 네이티브 Control UI. 새 Control 은
`src/Mono.Web` (React) + `src/Mono.Control` (WebView2 셸) 조합으로 대체됐다.

XAML 페이지와 `MainViewModel` 이 Core 의 어떤 명령을 어떤 순서로 쓰는지
참고해야 할 때만 열어 본다. `Services/CoreSession.*.cs` 가 특히
MonoMessage 규약의 살아 있는 예제다 — 새 UI 는 같은 규약을
`/ws/control` WebSocket 으로 말한다.
