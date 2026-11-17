// Mono 랜딩 페이지의 동작. 의존성 없이, 이 파일 하나다.
// 스크립트가 꺼져 있어도 페이지는 읽힌다: 첫 탭과 첫 단계가 HTML 에 그대로 놓여 있고 다운로드 링크도 살아 있다.
(() => {
  "use strict"

  const $ = (sel, root = document) => root.querySelector(sel)
  const $$ = (sel, root = document) => [...root.querySelectorAll(sel)]

  /** 보고 있는 기기의 운영체제. 받을 수 있는 건 Windows 뿐이라, 나머지는 안내만 바꾼다. */
  function detectOS() {
    const ua = navigator.userAgent || ""
    const platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || ""
    if (/android/i.test(ua)) return "mobile"
    // iPadOS 는 Mac 이라고 자기소개를 한다. 터치 점이 여러 개면 태블릿이다.
    if (/iphone|ipad|ipod/i.test(ua) || (/mac/i.test(platform) && navigator.maxTouchPoints > 1)) return "mobile"
    if (/win/i.test(platform) || /windows/i.test(ua)) return "windows"
    if (/mac/i.test(platform) || /mac os x/i.test(ua)) return "macos"
    return "other"
  }

  const OS_NOTE = {
    macos: "macOS에서 보고 계십니다. Mono는 아직 Windows용만 있습니다. 설치 파일은 Windows PC에서 실행하세요.",
    mobile: "휴대 기기에서 보고 계십니다. Mono는 Windows PC에 설치하는 앱입니다. 설치 파일은 PC에서 받으세요.",
    other: "Mono는 아직 Windows용만 있습니다. 설치 파일은 Windows 10 · 11 PC에서 실행하세요.",
  }

  /** 누른 버튼만 켜고, 같은 묶음의 나머지는 끈다. */
  function press(buttons, on, attr) {
    for (const b of buttons) {
      const active = b === on
      b.classList.toggle("is-on", active)
      b.setAttribute(attr, String(active))
      if (attr === "aria-selected") b.tabIndex = active ? 0 : -1
    }
  }

  /** 탭 묶음에 좌우 화살표 이동을 붙인다. */
  function arrows(buttons, select) {
    buttons.forEach((b, i) => {
      b.addEventListener("keydown", (e) => {
        if (e.key !== "ArrowRight" && e.key !== "ArrowLeft") return
        e.preventDefault()
        const next = buttons[(i + (e.key === "ArrowRight" ? 1 : buttons.length - 1)) % buttons.length]
        next.focus()
        select(next)
      })
    })
  }

  // ── Hero: 앨범 / 지금 재생 ─────────────────────────────────────────
  for (const root of $$("[data-tabs]")) {
    const tabs = $$("[data-tab]", root)
    const select = (tab) => {
      press(tabs, tab, "aria-selected")
      for (const panel of $$("[data-panel]", root)) panel.hidden = panel.dataset.panel !== tab.dataset.tab
    }
    tabs.forEach((t) => t.addEventListener("click", () => select(t)))
    arrows(tabs, select)
  }

  // ── 신호 경로: DSP 끔 / 켬 ─────────────────────────────────────────
  for (const root of $$("[data-signal]")) {
    const buttons = $$("[data-signal-set]", root)
    buttons.forEach((b) => b.addEventListener("click", () => {
      root.dataset.signal = b.dataset.signalSet
      press(buttons, b, "aria-pressed")
    }))
  }

  // ── 테마 미리보기 ──────────────────────────────────────────────────
  for (const root of $$("[data-themes]")) {
    const buttons = $$("[data-theme-set]", root)
    buttons.forEach((b) => b.addEventListener("click", () => {
      root.dataset.theme = b.dataset.themeSet
      press(buttons, b, "aria-pressed")
    }))
  }

  // ── 설치 단계 ──────────────────────────────────────────────────────
  for (const root of $$("[data-steps]")) {
    const steps = $$("[data-step]", root)
    steps.forEach((s) => s.addEventListener("click", () => {
      press(steps, s, "aria-pressed")
      for (const shot of $$("[data-step-shot]", root)) shot.hidden = shot.dataset.stepShot !== s.dataset.step
    }))
  }

  // ── 운영체제 탭 ────────────────────────────────────────────────────
  const os = detectOS()
  for (const root of $$("[data-os-tabs]")) {
    const tabs = $$("[data-os]", root)
    const select = (tab) => {
      press(tabs, tab, "aria-selected")
      for (const panel of $$("[data-os-panel]", root)) panel.hidden = panel.dataset.osPanel !== tab.dataset.os
    }
    tabs.forEach((t) => t.addEventListener("click", () => select(t)))
    arrows(tabs, select)
    // Mac 에서 왔으면 Mac 탭을 먼저 보여 준다. 거기에 "아직 없다"와 그동안 쓸 수 있는 방법이 적혀 있다.
    const mine = tabs.find((t) => t.dataset.os === os)
    if (mine) select(mine)
  }

  const note = $("[data-os-note]")
  if (note && OS_NOTE[os]) {
    note.textContent = OS_NOTE[os]
    note.hidden = false
  }

  // ── 스크롤 등장 ────────────────────────────────────────────────────
  // 화면에 들어온 요소에 is-in 을 한 번 붙이고 그만 지켜본다. 움직임 자체는 CSS 가 한다.
  for (const group of $$("[data-reveal-group]")) {
    [...group.children].forEach((child, i) => child.style.setProperty("--i", i))
  }
  const reveals = $$("[data-reveal], [data-reveal-group]")
  if (!("IntersectionObserver" in window)) {
    reveals.forEach((el) => el.classList.add("is-in"))
  } else {
    const seen = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (!entry.isIntersecting) continue
        entry.target.classList.add("is-in")
        seen.unobserve(entry.target)
      }
    }, { threshold: 0.12, rootMargin: "0px 0px -6% 0px" })
    reveals.forEach((el) => seen.observe(el))
  }

  // ── 다른 운영체제 메뉴: 바깥을 누르거나 Esc 를 누르면 닫는다 ───────
  const menu = $(".os-menu")
  if (menu) {
    document.addEventListener("click", (e) => {
      if (menu.open && !menu.contains(e.target)) menu.open = false
    })
    document.addEventListener("keydown", (e) => {
      if (e.key === "Escape" && menu.open) {
        menu.open = false
        $("summary", menu).focus()
      }
    })
    $$("a", menu).forEach((a) => a.addEventListener("click", () => { menu.open = false }))
  }
})()
