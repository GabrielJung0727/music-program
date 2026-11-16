// 공용 부품. 모양은 styles/base.css 가 정하고, 여기는 동작만 얹는다.

import { useCallback, useEffect, useRef, useState, type CSSProperties, type ReactNode } from "react"
import { Disc3, X } from "lucide-react"
import { hueOf, initials, qualityOf, type Quality } from "../lib/model"

// ── 커버 아트 ────────────────────────────────────────────────────────────────

/**
 * 커버 한 장. 주소가 없거나 로드에 실패하면 레코드 무늬 자리표시를 그린다.
 * 깨진 이미지 아이콘은 어떤 경우에도 보이지 않는다.
 */
export function Art({
  src, size, className = "", round, lift, children, style, eager,
}: {
  src?: string | null
  size?: number
  className?: string
  round?: boolean
  lift?: boolean
  children?: ReactNode
  style?: CSSProperties
  eager?: boolean
}) {
  const [failed, setFailed] = useState(false)
  useEffect(() => setFailed(false), [src])
  const show = !!src && !failed
  return (
    <div
      className={`art ${round ? "art--round" : ""} ${lift ? "art--lift" : ""} ${className}`}
      style={size ? { width: size, height: size, ...style } : style}
    >
      {show ? (
        <img src={src!} alt="" loading={eager ? "eager" : "lazy"} draggable={false} onError={() => setFailed(true)} />
      ) : (
        <div className="art__empty">
          {(size ?? 100) >= 56 && <Disc3 size={Math.min(40, Math.max(18, (size ?? 120) / 5))} strokeWidth={1.25} />}
        </div>
      )}
      {children}
    </div>
  )
}

// ── 음질 태그 ────────────────────────────────────────────────────────────────

export function QualityTag({
  of, quality, pill, bare,
}: {
  of?: Parameters<typeof qualityOf>[0]
  quality?: Quality
  pill?: boolean
  /** 컨테이너(FLAC 등)를 빼고 해상도만 */
  bare?: boolean
}) {
  const q = quality ?? (of ? qualityOf(of) : null)
  if (!q || (!q.res && !q.container)) return null
  return (
    <span className={`q q--${q.tier} ${pill ? "q--pill" : ""}`}>
      {!bare && q.container && q.tier !== "dsd" && <b>{q.container}</b>}
      {q.tier === "dsd" ? <b>{q.res}</b> : q.res}
    </span>
  )
}

// ── 재생 중 표시 ─────────────────────────────────────────────────────────────

export function Eq({ paused }: { paused?: boolean }) {
  return (
    <span className={`eq ${paused ? "is-paused" : ""}`} aria-hidden>
      <i /><i /><i />
    </span>
  )
}

// ── 아바타 ──────────────────────────────────────────────────────────────────

/** 배경이 밝으면 어두운 글자, 어두우면 밝은 글자. 사용자가 고른 색이 무엇이든 읽힌다. */
function inkOn(hex: string): string {
  const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex)
  if (!m) return "#fff"
  const [r, g, b] = [m[1], m[2], m[3]].map((x) => parseInt(x, 16))
  return (r * 299 + g * 587 + b * 114) / 1000 > 150 ? "#1c1304" : "#ffffff"
}

export function Avatar({ name, size, host, color }: { name: string; size?: "sm" | "lg"; host?: boolean; color?: string }) {
  const hue = hueOf(name)
  return (
    <span
      className={`avatar ${size ? `avatar--${size}` : ""} ${host ? "avatar--host" : ""}`}
      style={{ background: color ?? `hsl(${hue} 28% 30%)`, color: color ? inkOn(color) : `hsl(${hue} 60% 88%)` }}
      title={name}
    >
      {initials(name)}
    </span>
  )
}

// ── 토글 ────────────────────────────────────────────────────────────────────

export function Toggle({ value, onChange, disabled, label }: { value: boolean; onChange: (v: boolean) => void; disabled?: boolean; label?: string }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={value}
      aria-label={label}
      disabled={disabled}
      className="toggle"
      onClick={() => onChange(!value)}
    />
  )
}

// ── 슬라이더 ────────────────────────────────────────────────────────────────

/**
 * 0–1 값을 다루는 레일. 끄는 동안에는 onInput, 손을 떼면 onCommit.
 *
 * 포인터 캡처를 쓴다. 3px 레일 밖에서 손을 떼도 pointerup 이 이 요소로 오므로,
 * "바가 그 자리에 붙어 버리는" 일이 생기지 않는다.
 */
export function Slider({
  value, onInput, onCommit, disabled, accent, label, className = "",
}: {
  value: number
  onInput?: (v: number) => void
  onCommit?: (v: number) => void
  disabled?: boolean
  accent?: boolean
  label?: string
  className?: string
}) {
  const ref = useRef<HTMLDivElement>(null)
  const [drag, setDrag] = useState<number | null>(null)
  // 누르자마자 떼면 state 가 아직 반영되기 전에 pointerup 이 온다. 판정은 ref 로 한다.
  const dragging = useRef(false)
  const shown = Math.max(0, Math.min(1, drag ?? value))

  const at = useCallback((clientX: number) => {
    const box = ref.current!.getBoundingClientRect()
    return Math.max(0, Math.min(1, (clientX - box.left) / box.width))
  }, [])

  return (
    <div
      ref={ref}
      role="slider"
      tabIndex={disabled ? -1 : 0}
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(shown * 100)}
      className={`slider ${drag !== null ? "is-drag" : ""} ${disabled ? "is-disabled" : ""} ${accent ? "slider--accent" : ""} ${className}`}
      style={{ "--pct": `${shown * 100}%` } as CSSProperties}
      onPointerDown={(e) => {
        if (disabled) return
        try { e.currentTarget.setPointerCapture(e.pointerId) } catch { /* 합성 이벤트에는 캡처할 포인터가 없다 */ }
        dragging.current = true
        const v = at(e.clientX)
        setDrag(v)
        onInput?.(v)
      }}
      onPointerMove={(e) => {
        if (!dragging.current) return
        const v = at(e.clientX)
        setDrag(v)
        onInput?.(v)
      }}
      onPointerUp={(e) => {
        if (!dragging.current) return
        dragging.current = false
        const v = at(e.clientX)
        setDrag(null)
        onCommit?.(v)
      }}
      onPointerCancel={() => { dragging.current = false; setDrag(null) }}
      onKeyDown={(e) => {
        if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return
        const v = Math.max(0, Math.min(1, value + (e.key === "ArrowRight" ? 0.02 : -0.02)))
        onInput?.(v)
        onCommit?.(v)
      }}
    >
      <div className="slider__rail">
        <div className="slider__fill" />
      </div>
      <div className="slider__thumb" />
    </div>
  )
}

// ── 모달 ────────────────────────────────────────────────────────────────────

export function Modal({
  title, subtitle, onClose, children, footer, width, icon,
}: {
  title: string
  subtitle?: ReactNode
  onClose: () => void
  children?: ReactNode
  footer?: ReactNode
  width?: number
  icon?: ReactNode
}) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose() }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [onClose])

  return (
    <div className="scrim" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <div className="modal" role="dialog" aria-modal aria-label={title} style={width ? { maxWidth: width } : undefined}>
        <div className="modal__head">
          {icon}
          <div className="grow">
            <h2 className="t-h2">{title}</h2>
            {subtitle && <p className="t-small dim" style={{ marginTop: 5, lineHeight: 1.55 }}>{subtitle}</p>}
          </div>
          <button className="icon-btn icon-btn--sm" onClick={onClose} aria-label="닫기" style={{ margin: "-4px -6px 0 0" }}>
            <X size={17} />
          </button>
        </div>
        {children && <div className="modal__body">{children}</div>}
        {footer && <div className="modal__foot">{footer}</div>}
      </div>
    </div>
  )
}

// ── 빈 상태 ─────────────────────────────────────────────────────────────────

export function Empty({ icon, title, children, action }: { icon?: ReactNode; title: string; children?: ReactNode; action?: ReactNode }) {
  return (
    <div className="empty">
      {icon}
      <h3>{title}</h3>
      {children && <p>{children}</p>}
      {action && <div style={{ marginTop: 10 }}>{action}</div>}
    </div>
  )
}

// ── 바깥 클릭 ───────────────────────────────────────────────────────────────

export function useDismiss(open: boolean, onClose: () => void) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) onClose()
    }
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose() }
    // 팝오버를 연 그 클릭이 곧바로 닫지 않도록 다음 틱에 건다.
    const id = setTimeout(() => window.addEventListener("mousedown", onDown), 0)
    window.addEventListener("keydown", onKey)
    return () => {
      clearTimeout(id)
      window.removeEventListener("mousedown", onDown)
      window.removeEventListener("keydown", onKey)
    }
  }, [open, onClose])
  return ref
}
