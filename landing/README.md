# Mono 랜딩 페이지

Mono 를 소개하고 설치 파일로 안내하는 한 장짜리 사이트입니다.
빌드 단계가 없는 정적 파일이라, 이 폴더를 그대로 아무 정적 호스팅에 올리면 됩니다.

## 로컬에서 보기

`index.html` 을 더블클릭해도 열리지만, 글꼴과 이미지 경로를 실제 배포와 같게 보려면 서버로 띄우세요.

```powershell
# Node 가 있을 때
npx serve landing

# Python 이 있을 때
python -m http.server 4173 --directory landing
```

그다음 브라우저에서 `http://localhost:3000` (serve) 또는 `http://localhost:4173` (Python) 을 엽니다.

## 구조

```
landing/
  index.html                 페이지 전체. 문구는 전부 여기 있습니다
  assets/css/site.css        스타일. 색과 치수는 앱의 tokens.css 값을 옮긴 것
  assets/css/fonts.css       라틴 글꼴 3종의 @font-face
  assets/js/site.js          탭, 토글, 운영체제 감지. 의존성 없음
  assets/fonts/              앱이 싣는 것과 같은 글꼴 파일 (전부 SIL OFL 1.1)
  assets/img/favicon.svg     앱 아이콘
  public/screenshots/        앱 화면 캡처 (WebP, 1440px 과 2880px 두 벌)
```

## 디자인 기준

`src/Mono.Web/DESIGN.md` 와 `src/Mono.Web/src/styles/tokens.css` 를 따릅니다.

- 색, 글꼴, 모서리(카드 9px, 버튼 8px, 커버 3px)는 앱의 값 그대로입니다.
- 앱에 없는 값은 `site.css` 에 "파생" 이라고 적어 두었습니다. 헤드라인 크기, 섹션 간격, 네비게이션 높이처럼 웹 페이지라서 필요한 치수입니다.
- 호버는 색만 바뀝니다(120ms). 그 밖의 움직임은 없습니다.
- 앱 화면은 전부 실제 캡처입니다. CSS 로 그린 가짜 화면은 없습니다.

## 고칠 일이 생기면

**새 버전을 냈을 때**
다운로드 링크는 `releases/latest/download/Mono-win-Setup.exe` 라서 그대로 두면 됩니다.
`index.html` 에서 `v0.6.0` 을 찾아 두 군데(첫 화면의 메타 줄, 푸터)만 바꾸세요.

**앱 화면이 바뀌었을 때**
`src/Mono.Web` 에서 목 Core 와 개발 서버를 띄우고 `scripts/capture.mjs` 로 다시 찍은 뒤, WebP 로 줄여 `public/screenshots/` 의 같은 이름에 덮어씁니다.

| 파일 | 캡처 장면 |
| --- | --- |
| `album-*` | `09-album-playing` |
| `lyrics-*` | `14-now-playing-lyrics` |
| `home-dark-*` · `home-light-*` | `01-home` · `40-light-home` |
| `lounges-*` · `room-*` | `23-lounges` · `28-room-host` |
| `setup-1-*` · `setup-2-*` · `setup-3-*` | `60-onboarding-welcome` · `61-onboarding-folders` · `65-onboarding-done` |
| `signal-clean-*` · `signal-dsp-*` | 플레이어 바의 신호 경로 팝오버만 오려 낸 것 (DSP 끔 / 켬) |

## 올리기 전에 정할 것

- **다운로드 링크.** GitHub 릴리스(`GabrielJung0727/music-program`)를 가리킵니다. 저장소가 비공개면 방문자에게는 404 가 납니다. 저장소를 공개하거나, 설치 파일을 다른 곳에 올리고 `index.html` 의 `data-download` 링크 네 곳을 바꾸세요.
- **개인정보처리방침 · 이용약관.** 푸터의 두 링크는 아직 `#` 입니다. 문서가 생기면 주소를 넣으세요.
- **macOS.** 지금은 "아직 없음" 으로 적혀 있습니다. Mac 용이 나오면 첫 화면의 메뉴와 설치 방법의 macOS 탭을 채우면 됩니다.
- **og:image.** 공유 미리보기 이미지는 상대 경로입니다. 도메인이 정해지면 절대 주소로 바꾸세요.
