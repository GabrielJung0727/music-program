// DESIGN.md 4장의 모션 다섯 군데를 받치는 부품. 여기 없는 움직임은 화면에 없다.

import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from "react"
import { Pause, Play } from "lucide-react"
import { useApp } from "../state/app"

// ── 열림/닫힘 ────────────────────────────────────────────────────────────────

/**
 * 닫히는 동안에도 잠깐 그려 두기 위한 상태.
 * mounted: DOM 에 있는가, shown: 열린 모양(.is-open)인가.
 */
export function usePresence(open: boolean, ms: number): { mounted: boolean; shown: boolean } {
  const [mounted, setMounted] = useState(open)
  const [shown, setShown] = useState(false)

  useEffect(() => {
    if (open) {
      setMounted(true)
      // 닫힌 모양으로 한 프레임 그린 뒤에 열어야 전환이 일어난다.
      let second = 0
      const first = requestAnimationFrame(() => { second = requestAnimationFrame(() => setShown(true)) })
      return () => { cancelAnimationFrame(first); cancelAnimationFrame(second) }
    }
    setShown(false)
    const id = setTimeout(() => setMounted(false), ms)
    return () => clearTimeout(id)
  }, [open, ms])

  return { mounted, shown }
}

// ── 1. 재생 버튼 아이콘 전환 (140ms) ─────────────────────────────────────────

export function PlayPause({ playing, size = 18 }: { playing: boolean; size?: number }) {
  return (
    <span className={`swap ${playing ? "is-playing" : ""}`} style={{ width: size, height: size }} aria-hidden>
      <Play size={size} fill="currentColor" style={{ marginLeft: Math.round(size / 9) }} />
      <Pause size={size} fill="currentColor" />
    </span>
  )
}

// ── 2. 커버 색 추출 ──────────────────────────────────────────────────────────

/** [색상 0–360, 채도 0–1]. 무채색 커버면 채도가 0 이다. */
type Hue = [number, number]
const hueCache = new Map<string, Hue | null>()

/** 커버를 24×24 로 줄여 가장 넓게 깔린 색상대를 찾는다. */
function dominantHue(img: HTMLImageElement): Hue | null {
  const size = 24
  const canvas = document.createElement("canvas")
  canvas.width = canvas.height = size
  const ctx = canvas.getContext("2d", { willReadFrequently: true })
  if (!ctx) return null
  ctx.drawImage(img, 0, 0, size, size)
  const { data } = ctx.getImageData(0, 0, size, size)

  const bins = Array.from({ length: 12 }, () => ({ w: 0, x: 0, y: 0, s: 0 }))
  for (let i = 0; i < data.length; i += 4) {
    const r = data[i] / 255, g = data[i + 1] / 255, b = data[i + 2] / 255
    const max = Math.max(r, g, b), min = Math.min(r, g, b)
    const l = (max + min) / 2
    const d = max - min
    if (d < 0.06) continue
    const s = d / (1 - Math.abs(2 * l - 1) || 1)
    let h = max === r ? ((g - b) / d) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4
    h = (h * 60 + 360) % 360
    // 너무 어둡거나 밝은 픽셀은 색 인상에 덜 기여한다.
    const w = s * (1 - Math.abs(l - 0.5) * 1.4)
    if (w <= 0) continue
    const bin = bins[Math.floor(h / 30) % 12]
    const rad = (h * Math.PI) / 180
    bin.w += w
    bin.x += Math.cos(rad) * w
    bin.y += Math.sin(rad) * w
    bin.s += s * w
  }
  const top = bins.reduce((a, b) => (b.w > a.w ? b : a))
  if (top.w < 4) return [0, 0]
  return [(Math.atan2(top.y, top.x) * 180 / Math.PI + 360) % 360, top.s / top.w]
}

/**
 * 커버의 주요 색을 저채도·저명도로 눌러서 돌려준다. 배경 상단에 은은하게 깔 용도다.
 * 색을 읽지 못하면 transparent — 그 화면은 그냥 캔버스 색으로 보인다.
 */
export function useArtTint(src: string | null | undefined, dark: boolean): string {
  const [hue, setHue] = useState<Hue | null>(() => (src ? hueCache.get(src) ?? null : null))

  useEffect(() => {
    if (!src) { setHue(null); return }
    const hit = hueCache.get(src)
    if (hit !== undefined) { setHue(hit); return }
    let alive = true
    const img = new Image()
    img.decoding = "async"
    img.onload = () => {
      let result: Hue | null = null
      try { result = dominantHue(img) } catch { /* 다른 출처의 이미지라 픽셀을 읽을 수 없다 */ }
      hueCache.set(src, result)
      if (alive) setHue(result)
    }
    img.onerror = () => { hueCache.set(src, null) }
    img.src = src
    return () => { alive = false }
  }, [src])

  if (!hue) return "transparent"
  const [h, s] = hue
  const sat = Math.round(Math.min(s * 0.62, 0.42) * 100)
  return dark ? `hsl(${Math.round(h)} ${sat}% 19%)` : `hsl(${Math.round(h)} ${Math.min(sat + 6, 46)}% 86%)`
}

/** 페이지 맨 위에 까는 커버 색. 스포티파이의 앨범 헤더와 같은 구실이다. */
export function Tint({ src, className = "tint" }: { src?: string | null; className?: string }) {
  const { theme } = useApp()
  const tint = useArtTint(src, theme === "dark")
  return <div className={className} style={{ "--tint": tint } as CSSProperties} aria-hidden />
}

// ── 4. 가사 스크롤 (320ms cubic-bezier(0.25, 1, 0.5, 1)) ─────────────────────

/** CSS 의 cubic-bezier 와 같은 곡선. 브라우저의 smooth 스크롤은 곡선을 고를 수 없어 직접 굴린다. */
export function cubicBezier(x1: number, y1: number, x2: number, y2: number): (t: number) => number {
  const cx = 3 * x1, bx = 3 * (x2 - x1) - cx, ax = 1 - cx - bx
  const cy = 3 * y1, by = 3 * (y2 - y1) - cy, ay = 1 - cy - by
  const x = (t: number) => ((ax * t + bx) * t + cx) * t
  const y = (t: number) => ((ay * t + by) * t + cy) * t
  const dx = (t: number) => (3 * ax * t + 2 * bx) * t + cx
  return (p) => {
    let t = p
    for (let i = 0; i < 6; i++) {
      const err = x(t) - p
      if (Math.abs(err) < 1e-4) break
      const slope = dx(t)
      if (Math.abs(slope) < 1e-6) break
      t -= err / slope
    }
    return y(Math.max(0, Math.min(1, t)))
  }
}

const LYRIC_EASE = cubicBezier(0.25, 1, 0.5, 1)
const running = new WeakMap<HTMLElement, number>()

export function scrollParent(el: HTMLElement): HTMLElement | null {
  let p = el.parentElement
  while (p && !/(auto|scroll)/.test(getComputedStyle(p).overflowY)) p = p.parentElement
  return p
}

/** el 이 스크롤 상자의 세로 가운데에 오도록 320ms 동안 옮긴다. */
export function centerInScroll(el: HTMLElement, ms = 320) {
  const box = scrollParent(el)
  if (!box) return
  const a = el.getBoundingClientRect()
  const b = box.getBoundingClientRect()
  const from = box.scrollTop
  const to = Math.max(0, Math.min(box.scrollHeight - box.clientHeight, from + (a.top - b.top) - (box.clientHeight - a.height) / 2))
  if (Math.abs(to - from) < 1) return

  cancelAnimationFrame(running.get(box) ?? 0)
  if (matchMedia("(prefers-reduced-motion: reduce)").matches) { box.scrollTop = to; return }
  const start = performance.now()
  const step = (now: number) => {
    const p = Math.min(1, (now - start) / ms)
    box.scrollTop = from + (to - from) * LYRIC_EASE(p)
    if (p < 1) running.set(box, requestAnimationFrame(step))
  }
  running.set(box, requestAnimationFrame(step))
}

// ── 크레딧 ──────────────────────────────────────────────────────────────────

/**
 * 태그에서 온 크레딧("이름 — 역할" 줄들)을 이름·역할 두 칸으로 나눠 그린다.
 * 구분 기호(—, –, -, :)는 화면에 내보내지 않는다.
 */
export function Credits({ text }: { text: string }): ReactNode {
  const lines = text.split(/\r?\n/).map((l) => l.trim()).filter(Boolean)
  return (
    <dl className="credits">
      {lines.map((line, i) => {
        const m = line.match(/^(.+?)\s+[—–\-:]\s+(.+)$/)
        return m
          ? <div key={i}><dt>{m[1]}</dt><dd>{m[2]}</dd></div>
          : <div key={i}><dt style={{ gridColumn: "1 / -1" }}>{line}</dt></div>
      })}
    </dl>
  )
}

/** 방금 true 가 됐는지. 방송 시작 링처럼 한 번만 도는 효과에 쓴다. */
export function useJustBecame(value: boolean, ms: number): boolean {
  const prev = useRef(value)
  const [fresh, setFresh] = useState(false)
  useEffect(() => {
    const was = prev.current
    prev.current = value
    if (!value || was) return
    setFresh(true)
    const id = setTimeout(() => setFresh(false), ms)
    return () => clearTimeout(id)
  }, [value, ms])
  return fresh
}
