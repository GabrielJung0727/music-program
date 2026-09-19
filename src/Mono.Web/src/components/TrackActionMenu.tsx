import { useState, useRef, useEffect, forwardRef, useImperativeHandle } from "react"

export interface TrackActionTarget {
  /** Core 의 트랙 id. 목업에서 온 항목에는 없어서 제목으로 되찾는다. */
  id?: string
  title: string
  artist?: string
  album?: string
  duration?: string
  format?: string
  dr?: string
  art?: string
  source?: "qobuz" | "tidal" | "local"
}

interface Props {
  track: TrackActionTarget
  onPlayNow: (track: TrackActionTarget) => void
  onPlayNext: (track: TrackActionTarget) => void
  onAddToQueue: (track: TrackActionTarget) => void
  isGuest?: boolean
  /** 제목 노드. 클릭 시 메뉴를 열거나(재생 중 다른 곡) 바로 Play Now. */
  title?: React.ReactNode
  /** true 면 제목 클릭이 메뉴를 연다. false/생략 이면 제목 클릭 = Play Now. */
  titleOpensMenu?: boolean
  /** 제목만 쓰는 칸에서는 케밥을 숨긴다. */
  hideKebab?: boolean
}

interface MenuPos { top: number; left: number; flipUp: boolean }

/** 행 전체 클릭이 제목 클릭과 같게 동작하게 한다. */
export type TrackActionHandle = {
  activate: (anchor: DOMRect) => void
}

const TrackActionMenu = forwardRef<TrackActionHandle, Props>(function TrackActionMenu(
  { track, onPlayNow, onPlayNext, onAddToQueue, isGuest, title, titleOpensMenu, hideKebab },
  ref,
) {
  const [isOpen, setIsOpen] = useState(false)
  const [pos, setPos] = useState<MenuPos>({ top: 0, left: 0, flipUp: false })
  const btnRef = useRef<HTMLButtonElement>(null)
  const titleRef = useRef<HTMLElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!isOpen) return
    const handle = (e: MouseEvent) => {
      if (
        !btnRef.current?.contains(e.target as Node) &&
        !titleRef.current?.contains(e.target as Node) &&
        !menuRef.current?.contains(e.target as Node)
      ) setIsOpen(false)
    }
    document.addEventListener("mousedown", handle)
    return () => document.removeEventListener("mousedown", handle)
  }, [isOpen])

  useEffect(() => {
    if (!isOpen) return
    const handle = (e: KeyboardEvent) => { if (e.key === "Escape") setIsOpen(false) }
    document.addEventListener("keydown", handle)
    return () => document.removeEventListener("keydown", handle)
  }, [isOpen])

  const placeMenu = (anchor: DOMRect, alignLeft: boolean) => {
    const menuHeight = isGuest ? 110 : 168
    const flipUp = anchor.bottom + menuHeight + 8 > window.innerHeight
    const left = alignLeft ? anchor.left : anchor.right - 192
    setPos({
      top: flipUp ? anchor.top - menuHeight - 4 : anchor.bottom + 4,
      left: Math.max(8, left),
      flipUp,
    })
    setIsOpen(true)
  }

  const openFromKebab = (e: React.MouseEvent) => {
    e.stopPropagation()
    if (!btnRef.current) return
    if (isOpen) { setIsOpen(false); return }
    placeMenu(btnRef.current.getBoundingClientRect(), false)
  }

  const onTitleClick = (e: React.MouseEvent) => {
    e.stopPropagation()
    activate(e.currentTarget.getBoundingClientRect(), true)
  }

  const activate = (anchor: DOMRect, alignLeft = true) => {
    if (titleOpensMenu) {
      if (isOpen) { setIsOpen(false); return }
      placeMenu(anchor, alignLeft)
      return
    }
    onPlayNow(track)
  }

  useImperativeHandle(ref, () => ({
    activate: (anchor) => activate(anchor, true),
  }))

  const run = (fn: () => void) => { fn(); setIsOpen(false) }

  return (
    <>
      {title != null && (
        <span ref={titleRef as React.RefObject<HTMLSpanElement>} onClick={onTitleClick} style={{ cursor: "pointer" }}>
          {title}
        </span>
      )}
      {!hideKebab && (
      <button
        ref={btnRef}
        onClick={openFromKebab}
        aria-expanded={isOpen}
        className={`track-more-btn p-1.5 rounded-md transition-all cursor-pointer ${isOpen ? "opacity-100" : "opacity-0 group-hover:opacity-100"}`}
        title="More options"
        style={{
          lineHeight: 1,
          flexShrink: 0,
          border: isOpen ? "1px solid" : "none",
          background: "none",
        }}
      >
        <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
          <circle cx="5"  cy="12" r="1.5"/>
          <circle cx="12" cy="12" r="1.5"/>
          <circle cx="19" cy="12" r="1.5"/>
        </svg>
      </button>
      )}

      {isOpen && (
        <div
          ref={menuRef}
          style={{
            position: "fixed",
            top: pos.top,
            left: Math.max(8, pos.left),
            zIndex: 9999,
            width: 192,
          }}
        >
          <div className="track-dropdown-menu" style={{
            backdropFilter: "blur(12px)",
            borderRadius: 12,
            padding: 4,
            animation: "tam-in 0.1s ease",
          }}>
            <style>{`
              @keyframes tam-in {
                from { opacity: 0; transform: scale(0.95) translateY(${pos.flipUp ? "4px" : "-4px"}); }
                to   { opacity: 1; transform: scale(1) translateY(0); }
              }
            `}</style>

            <div className="track-menu-kicker">Selected track</div>
            <div className="track-menu-current">{track.title}</div>

            <MenuItem
              icon={
                <svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor"><polygon points="5,3 19,12 5,21"/></svg>
              }
              label="Play Now"
              sub="Start immediately"
              onClick={() => run(() => onPlayNow(track))}
            />

            <MenuItem
              icon={
                <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <polygon points="5,4 15,12 5,20" fill="currentColor" stroke="none"/>
                  <line x1="19" y1="5" x2="19" y2="19"/>
                </svg>
              }
              label="Play Next"
              sub={isGuest ? "Host controlled" : "Insert after current"}
              disabled={isGuest}
              onClick={() => run(() => onPlayNext(track))}
            />

            <MenuItem
              icon={
                <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
                </svg>
              }
              label="Add to End"
              sub={isGuest ? "Host controlled" : "Append to setlist"}
              disabled={isGuest}
              onClick={() => run(() => onAddToQueue(track))}
            />

            {isGuest && (
              <div style={{ padding: "6px 12px 4px", fontFamily: "'DM Mono', monospace", fontSize: 9.5, color: "#A1A1AA", borderTop: "1px solid #F4F4F5", marginTop: 2 }}>
                Controlled by session host
              </div>
            )}
          </div>
        </div>
      )}
    </>
  )
})

export default TrackActionMenu

function MenuItem({ icon, label, sub, disabled, onClick }: {
  icon: React.ReactNode
  label: string
  sub: string
  disabled?: boolean
  onClick: () => void
}) {
  return (
    <button
      onClick={disabled ? undefined : onClick}
      className="track-menu-item"
      style={{
        width: "100%", textAlign: "left", border: "none", cursor: disabled ? "default" : "pointer",
        background: "none", padding: "7px 10px", borderRadius: 8,
        display: "flex", alignItems: "center", gap: 10,
        opacity: disabled ? 0.38 : 1, transition: "background 0.1s",
        fontFamily: "inherit",
      }}
    >
      <span className="track-menu-icon" style={{ flexShrink: 0, display: "flex" }}>{icon}</span>
      <div>
        <div style={{ fontSize: 12, fontWeight: 500, lineHeight: 1.2 }}>{label}</div>
        <p style={{ fontFamily: "'DM Mono', monospace", fontSize: 9.5, marginTop: 1 }}>{sub}</p>
      </div>
    </button>
  )
}
