// 화면을 띄우지 않고(headless) 새 UI 를 돌려 보며 스크린샷을 남긴다.
//
//   npm run mock      # 모의 Core (7802)
//   $env:CORE = "http://127.0.0.1:7802"; npm run dev   # Vite (5273), 모의 Core 에 붙여서
//   npm run capture   # screenshots/ 에 PNG 저장
//
//   SCALE=2 npm run capture             고해상도
//   ONLY=home,album npm run capture     이름에 이 말이 든 장면만

import fs from "node:fs"
import path from "node:path"
import { fileURLToPath } from "node:url"
import puppeteer from "puppeteer-core"

const here = path.dirname(fileURLToPath(import.meta.url))
const BASE = process.env.BASE ?? "http://localhost:5273"
const CORE = process.env.CORE ?? "http://127.0.0.1:7802"
const OUT = path.resolve(here, "..", process.env.OUT ?? "screenshots")
const SCALE = Number(process.env.SCALE ?? 1)
const FORMAT = process.env.FORMAT === "jpg" ? "jpg" : "png"
const ONLY = (process.env.ONLY ?? "").split(",").map((s) => s.trim()).filter(Boolean)

const BROWSERS = [
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "C:/Program Files/Microsoft/Edge/Application/msedge.exe",
]
const executablePath = process.env.BROWSER ?? BROWSERS.find((p) => fs.existsSync(p))
if (!executablePath) throw new Error("Chrome 또는 Edge 를 찾지 못했습니다. BROWSER 환경 변수로 경로를 알려 주세요.")

fs.mkdirSync(OUT, { recursive: true })

/**
 * Mono.Control(WebView2 셸)이 심어 주는 window.mono 의 대역.
 * 이게 없으면 화면은 "브라우저에서 열렸다"고 보고 출력 장치 목록을 비워 둔다.
 */
function fakeShell() {
  const ok = async () => ({ ok: true })
  window.mono = {
    isShell: true,
    call: async () => null,
    on: () => () => {},
    pickFolder: async () => "E:\\Music\\New Arrivals",
    pickFile: async () => null,
    openExternal: async () => true,
    output: {
      start: ok, stop: ok, restart: ok,
      status: async () => ({ running: true, roomId: null, backend: "exclusive", coreRunning: true }),
      devices: async () => [
        { id: "dev-adi2", name: "RME ADI-2 DAC FS", driverType: "WASAPI_EXCLUSIVE", architecture: "USB Audio Class 2", subtitle: "", specs: ["32-bit / 768 kHz"], dsdSupport: "DSD256 (DoP)", bitPerfectVerified: true, isDefault: false },
        { id: "dev-adi2-asio", name: "ASIO MADIface USB", driverType: "ASIO", architecture: "RME 드라이버", subtitle: "", specs: ["32-bit / 768 kHz"], dsdSupport: "네이티브 DSD", bitPerfectVerified: true, isDefault: false },
        { id: "dev-hpa", name: "FiiO K9 Pro ESS", driverType: "WASAPI_EXCLUSIVE", architecture: "USB Audio Class 2", subtitle: "", specs: ["32-bit / 384 kHz"], dsdSupport: "DSD256 (DoP)", bitPerfectVerified: false, isDefault: false },
        { id: "dev-realtek", name: "스피커 (Realtek Audio)", driverType: "WASAPI_EXCLUSIVE", architecture: "내장 사운드", subtitle: "", specs: ["24-bit / 192 kHz"], dsdSupport: null, bitPerfectVerified: false, isDefault: true },
      ],
    },
    update: { check: async () => ({ message: "최신 버전을 쓰고 있습니다.", available: false, current: "0.6.0", installed: true }), apply: async () => true },
    app: { version: async () => ({ version: "0.6.0", installed: true }), logPath: async () => "", quit: async () => true, minimizeToTray: async () => true },
    discord: {
      set: async () => ({ ok: true, connected: true }), clear: async () => true,
      status: async () => ({ enabled: true, connected: true, hasAppId: true }),
      setEnabled: async (enabled) => ({ enabled }), setAppId: async () => ({ hasAppId: true }),
    },
  }
}

const wait = (ms) => new Promise((r) => setTimeout(r, ms))

const browser = await puppeteer.launch({
  executablePath,
  headless: true,
  args: ["--no-first-run", "--disable-extensions", "--force-color-profile=srgb", "--font-render-hinting=none"],
})

async function newPage(width = 1440, height = 900) {
  const page = await browser.newPage()
  await page.setViewport({ width, height, deviceScaleFactor: SCALE })
  await page.evaluateOnNewDocument(fakeShell)
  page.on("pageerror", (e) => console.log("  [pageerror]", e.message))
  page.on("console", (m) => { if (m.type() === "error") console.log("  [console]", m.text().slice(0, 200)) })
  return page
}

let page = await newPage()
const taken = []

async function shot(name) {
  if (ONLY.length && !ONLY.some((o) => name.includes(o))) return
  await page.evaluate(() => document.fonts.ready)
  await wait(450)
  const file = path.join(OUT, `${name}.${FORMAT}`)
  await page.screenshot(FORMAT === "jpg" ? { path: file, type: "jpeg", quality: 92 } : { path: file })
  taken.push(name)
  console.log("  ✓", name)
}

/** 해시로 화면을 옮긴다. 앱은 hashchange 를 듣고 따라온다. */
async function go(hash) {
  await page.evaluate((h) => { location.hash = h }, hash)
  await wait(650)
}
async function open(hash = "#/home") {
  await page.goto(`${BASE}/${hash}`, { waitUntil: "networkidle2" })
  // 해시만 다른 주소로 가면 브라우저는 문서를 다시 읽지 않는다.
  await page.reload({ waitUntil: "networkidle2" })
  await wait(900)
}
async function click(selector, nth = 0) {
  const found = await page.evaluate((selector, nth) => {
    const el = [...document.querySelectorAll(selector)].filter((e) => e.offsetParent !== null || e.getClientRects().length)[nth]
    if (!el) return false
    el.click()
    return true
  }, selector, nth)
  if (!found) console.log("  (없음)", selector)
  await wait(450)
  return found
}
async function clickText(text, selector = "button") {
  const found = await page.evaluate((text, selector) => {
    const el = [...document.querySelectorAll(selector)].filter((e) => e.getClientRects().length && e.textContent.trim().includes(text)).pop()
    if (!el) return false
    el.click()
    return true
  }, text, selector)
  if (!found) console.log("  (없음)", text)
  await wait(450)
  return found
}
const scrollView = (y) => page.evaluate((y) => document.querySelector(".view, .room__main")?.scrollTo(0, y), y)
/** 설정 화면의 테마 버튼을 실제로 눌러서 바꾼다. */
const setTheme = async (label) => {
  await go("#/settings/general")
  await clickText(label, ".seg button")
}

// ── 장면 ────────────────────────────────────────────────────────────────────

await fetch(`${CORE}/api/setup`, { method: "POST" })

console.log("idle")
await open("#/home")
await shot("01-home")
await scrollView(620)
await shot("02-home-lower")

console.log("library")
await go("#/library/albums")
await shot("03-library-albums")
await go("#/library/artists")
await shot("04-library-artists")
await go("#/library/tracks")
await shot("05-library-tracks")
await go("#/library/composers")
await shot("06-library-composers")
await go("#/library/playlists")
await shot("07-library-playlists")

console.log("album + playback")
await go("#/album/al-paper-lanterns")
await shot("08-album")
await clickText("재생", ".detail-hero .btn--primary")
await wait(900)
await shot("09-album-playing")
await page.mouse.click(700, 640, { button: "right" })
await wait(400)
await shot("10-track-menu")
await page.keyboard.press("Escape")
await click('.tools [aria-label="대기열"]')
await shot("11-queue-panel")
await clickText("가사", ".dock .seg button")
await shot("12-lyrics-panel-empty")
await click('.dock [aria-label="패널 닫기"]')

console.log("now playing")
await go("#/album/al-garden-3am")
await clickText("재생", ".detail-hero .btn--primary")
await wait(700)
// 가사가 흐르는 지점으로 옮겨서 찍는다.
await page.evaluate(() => {
  const rail = document.querySelector(".scrub .slider")
  const box = rail.getBoundingClientRect()
  const at = { clientX: box.left + box.width * 0.34, clientY: box.top + box.height / 2, pointerId: 1, bubbles: true }
  rail.dispatchEvent(new PointerEvent("pointerdown", at))
  rail.dispatchEvent(new PointerEvent("pointerup", at))
})
await wait(700)
await shot("13-album-korean")
await click('.np__art')
await wait(900)
await shot("14-now-playing-lyrics")
await clickText("라이너 노트", ".np-full .tab")
await shot("15-now-playing-notes")
await clickText("수록곡", ".np-full .tab")
await shot("16-now-playing-tracks")
await page.keyboard.press("Escape")
await wait(400)

console.log("signal + output")
await click(".signal")
await shot("17-signal-path")
await click(".flyout .toggle")
await wait(500)
await shot("17b-signal-path-dsp")
await click(".flyout .toggle")
await click(".signal")
await click('.tools [aria-label="출력 장치"]')
await shot("18-output")
await click('.tools [aria-label="출력 장치"]')

console.log("artist / playlist / search / explore")
await go("#/artist/ar-mira-okafor-trio")
await shot("19-artist")
await go("#/playlist/pl-late")
await shot("20-playlist")
await go("#/search/night")
await wait(900)
await shot("21-search")
await go("#/explore")
await shot("22-streaming")

console.log("lounges")
await go("#/lounges")
await wait(500)
await shot("23-lounges")
await scrollView(560)
await shot("24-lounges-lower")
await scrollView(0)
await clickText("입장하기", ".lounge-feature .btn")
await wait(1200)
await shot("25-room-guest")
await clickText("함께 듣는 사람", ".tab")
await shot("26-room-people")
await clickText("나가기", ".room__head .btn")
await wait(700)

console.log("hosting")
await go("#/album/al-night-ferry")
await clickText("재생", ".detail-hero .btn--primary")
await wait(600)
await clickText("라운지 열기", ".tools .btn")
await shot("27-host-modal")
await clickText("방송 시작", ".modal .btn")
await wait(1200)
await page.type(".chat__input input", "오늘은 Bluewater 리마스터로 시작합니다. 편하게 들어오세요.")
await page.keyboard.press("Enter")
await wait(300)
await click(".reaction", 0)
await click(".reaction", 2)
await shot("28-room-host")
await clickText("초대", ".room__head .btn")
await shot("29-share-modal")
await page.keyboard.press("Escape")
await go("#/home")
await shot("30-home-on-air")
await go("#/room")
await clickText("방송 종료", ".room__head .btn")
await shot("31-end-confirm")
await clickText("방송 종료", ".modal .btn")
await wait(900)

console.log("settings")
for (const [i, tab] of ["audio", "dsp", "library", "accounts", "general"].entries()) {
  await go(`#/settings/${tab}`)
  await wait(400)
  await shot(`${32 + i}-settings-${tab}`)
}

console.log("light theme")
await setTheme("밝게")
await shot("39-light-settings")
await go("#/home")
await shot("40-light-home")
await go("#/album/al-night-ferry")
await shot("41-light-album")
await go("#/library/albums")
await shot("42-light-library")
await go("#/lounges")
await shot("43-light-lounges")
await clickText("입장하기", ".lounge-feature .btn")
await wait(1100)
await shot("44-light-room")
await clickText("나가기", ".room__head .btn")
await wait(500)
await setTheme("어둡게")

console.log("compact window")
await page.close()
page = await newPage(1180, 760)
await open("#/home")
await shot("50-compact-home")
await go("#/album/al-night-ferry")
await shot("51-compact-album")
await page.close()

console.log("onboarding")
page = await newPage()
await fetch(`${CORE}/api/setup`, { method: "DELETE" })
await page.goto(BASE, { waitUntil: "networkidle2" })
await page.evaluate(() => localStorage.clear())
await page.reload({ waitUntil: "networkidle2" })
await wait(700)
await shot("60-onboarding-welcome")
await clickText("시작하기", ".ob__foot .btn")
await clickText("폴더 추가", ".ob .btn")
await shot("61-onboarding-folders")
await clickText("다음", ".ob__foot .btn")
await wait(500)
await shot("62-onboarding-output")
await clickText("다음", ".ob__foot .btn")
await shot("63-onboarding-streaming")
await clickText("다음", ".ob__foot .btn")
await page.type(".ob input.input", "Gabriel")
await shot("64-onboarding-profile")
await clickText("다음", ".ob__foot .btn")
await shot("65-onboarding-done")
await fetch(`${CORE}/api/setup`, { method: "POST" })

await browser.close()
console.log(`\n${taken.length}장 → ${OUT}`)
