import { useState, useRef, useEffect } from "react"

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
}

interface MenuPos { top: number; left: number; flipUp: boolean }

export default function TrackActionMenu({ track, onPlayNow, onPlayNext, onAddToQueue, isGuest }: Props) {
  const [isOpen, setIsOpen] = useState(false)
  const [pos, setPos] = useState<MenuPos>({ top: 0, left: 0, flipUp: false })
  const btnRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)

  // Close on outside click
  useEffect(() => {
    if (!isOpen) return
    const handle = (e: MouseEvent) => {
      if (
        !btnRef.current?.contains(e.target as Node) &&
        !menuRef.current?.contains(e.target as Node)
      ) setIsOpen(false)
    }
    document.addEventListener("mousedown", handle)
    return () => document.removeEventListener("mousedown", handle)
  }, [isOpen])

  // Close on Escape
  useEffect(() => {
    if (!isOpen) return
    const handle = (e: KeyboardEvent) => { if (e.key === "Escape") setIsOpen(false) }
    document.addEventListener("keydown", handle)
    return () => document.removeEventListener("keydown", handle)
  }, [isOpen])

  const openMenu = (e: React.MouseEvent) => {
    e.stopPropagation()
    if (!btnRef.current) return
    const r = btnRef.current.getBoundingClientRect()
    const menuHeight = isGuest ? 80 : 132
    const flipUp = r.bottom + menuHeight + 8 > window.innerHeight
    // Align right edge of menu with right edge of button
    setPos({ top: flipUp ? r.top - menuHeight - 4 : r.bottom + 4, left: r.right - 192, flipUp })
    setIsOpen(v => !v)
  }

  const run = (fn: () => void) => { fn(); setIsOpen(false) }

  return (
    <>
      <button
        ref={btnRef}
        onClick={openMenu}
        className={`p-1.5 rounded-md text-zinc-400 hover:text-zinc-800 hover:bg-zinc-100 transition-all cursor-pointer ${isOpen ? "opacity-100 bg-zinc-100 text-zinc-800" : "opacity-0 group-hover:opacity-100"}`}
        title="More options"
        style={{ border: "none", background: isOpen ? undefined : "none", lineHeight: 1, flexShrink: 0 }}
      >
        <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
          <circle cx="5"  cy="12" r="1.5"/>
          <circle cx="12" cy="12" r="1.5"/>
          <circle cx="19" cy="12" r="1.5"/>
        </svg>
      </button>

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
          <div style={{
            background: "rgba(255,255,255,0.97)", backdropFilter: "blur(12px)",
            border: "1px solid #E4E4E7",
            borderRadius: 12,
            boxShadow: "0 10px 25px -5px rgba(0,0,0,0.08), 0 8px 10px -6px rgba(0,0,0,0.04)",
            padding: 4,
            animation: "tam-in 0.1s ease",
          }}>
            <style>{`
              @keyframes tam-in {
                from { opacity: 0; transform: scale(0.95) translateY(${pos.flipUp ? "4px" : "-4px"}); }
                to   { opacity: 1; transform: scale(1) translateY(0); }
              }
            `}</style>

            {/* Play Now */}
            <MenuItem
              icon={
                <svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor"><polygon points="5,3 19,12 5,21"/></svg>
              }
              label="Play Now"
              sub="Start immediately"
              onClick={() => run(() => onPlayNow(track))}
            />

            {/* Play Next */}
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

            {/* Add to Queue */}
            <MenuItem
              icon={
                <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/>
                  <line x1="8" y1="18" x2="21" y2="18"/>
                  <line x1="3" y1="6"  x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/>
                  <line x1="3" y1="18" x2="3.01" y2="18"/>
                </svg>
              }
              label="Add to Queue"
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
}

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
      style={{
        width: "100%", textAlign: "left", border: "none", cursor: disabled ? "default" : "pointer",
        background: "none", padding: "7px 10px", borderRadius: 8,
        display: "flex", alignItems: "center", gap: 10,
        opacity: disabled ? 0.38 : 1, transition: "background 0.1s",
        fontFamily: "inherit",
      }}
      onMouseEnter={(e) => { if (!disabled) (e.currentTarget as HTMLButtonElement).style.background = "rgba(244,244,245,0.8)" }}
      onMouseLeave={(e) => { (e.currentTarget as HTMLButtonElement).style.background = "none" }}
    >
      <span style={{ color: "#71717A", flexShrink: 0, display: "flex" }}>{icon}</span>
      <div>
        <div style={{ fontSize: 12, fontWeight: 500, color: "#18181B", lineHeight: 1.2 }}>{label}</div>
        <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 9.5, color: "#A1A1AA", marginTop: 1 }}>{sub}</div>
      </div>
    </button>
  )
}
