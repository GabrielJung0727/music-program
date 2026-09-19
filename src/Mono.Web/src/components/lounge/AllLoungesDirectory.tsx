import React, { useState, useMemo } from "react"
import type { LoungeRoom } from "../../data/types"
import { albumArt, onArtError } from "../../lib/artwork"

const MONO = "'DM Mono', ui-monospace, SFMono-Regular, Menlo, monospace"

const AUDIO_FILTERS = [
  { id: "all",     label: "All" },
  { id: "dsd",     label: "Bit-Perfect DSD" },
  { id: "hires",   label: "Hi-Res FLAC" },
  { id: "vinyl",   label: "Vinyl Rip" },
] as const
type AudioFilter = typeof AUDIO_FILTERS[number]["id"]

function matchesAudioFilter(room: LoungeRoom, filter: AudioFilter): boolean {
  if (filter === "all") return true
  const s = room.audioSpec.toUpperCase()
  if (filter === "dsd")   return s.includes("DSD")
  if (filter === "hires") return s.includes("FLAC") && (s.includes("24") || s.includes("192") || s.includes("96") || s.includes("48"))
  if (filter === "vinyl") return s.includes("VINYL") || s.includes("RIP")
  return true
}

const GLASS_PILL: React.CSSProperties = {
  display: "inline-flex",
  alignItems: "center",
  borderRadius: 9999,
  height: 22,
  whiteSpace: "nowrap",
  backdropFilter: "blur(8px)",
  WebkitBackdropFilter: "blur(8px)",
}

function LiveDot({ count }: { count: number }) {
  const intimate = count <= 5
  const dotColor = intimate ? "#fbbf24" : "#38bdf8"
  const dotGlow  = intimate ? "0 0 6px rgba(251,191,36,0.7)" : "0 0 6px rgba(56,189,248,0.7)"
  return (
    <span style={{
      ...GLASS_PILL,
      gap: 5,
      padding: "2px 10px 2px 8px",
      background: "rgba(14,15,23,0.72)",
      border: "1px solid rgba(255,255,255,0.15)",
      fontFamily: MONO,
      fontSize: 11,
      fontWeight: 600,
      color: "#f4f4f5",
    }}>
      <span style={{ width: 6, height: 6, borderRadius: "50%", background: dotColor, boxShadow: dotGlow, flexShrink: 0 }} />
      {intimate ? `${count} · Intimate` : `${count} live`}
    </span>
  )
}

function BadgePill({ badge }: { badge: LoungeRoom["badge"] }) {
  if (!badge) return null
  const map = {
    trending: { label: "Hot",   cls: "tag-hot",   tag: "hot",   bg: "rgba(244,63,94,0.20)",   border: "rgba(244,63,94,0.35)",   color: "#fda4af" },
    debut:    { label: "Debut", cls: "tag-debut",  tag: "debut", bg: "rgba(16,185,129,0.20)",  border: "rgba(16,185,129,0.35)",  color: "#6ee7b7" },
    indie:    { label: "Indie", cls: "tag-indie",  tag: "indie", bg: "rgba(139,92,246,0.20)",  border: "rgba(139,92,246,0.35)",  color: "#c4b5fd" },
    cozy:     { label: "Cozy",  cls: "tag-cozy",   tag: "cozy",  bg: "rgba(139,92,246,0.20)",  border: "rgba(139,92,246,0.35)",  color: "#c4b5fd" },
  } as const
  const c = map[badge]
  return (
    <span
      className={`lounge-badge-pill ${c.cls}`}
      data-tag={c.tag}
      style={{
        ...GLASS_PILL,
        height: 20,
        padding: "1px 8px",
        background: c.bg,
        border: `1px solid ${c.border}`,
        color: c.color,
        fontFamily: MONO,
        fontSize: 10,
        fontWeight: 500,
        letterSpacing: "0.02em",
        textTransform: "uppercase",
      }}
    >
      {c.label}
    </span>
  )
}

function CardView({ rooms, onJoin, emptyLabel }: { rooms: LoungeRoom[]; onJoin: (room: LoungeRoom) => void; emptyLabel: string }) {
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
      {rooms.map((room) => (
        <div
          key={room.id}
          className="lounge-room-card lounge-directory-card"
          style={{ display: "flex", flexDirection: "column", cursor: "default" }}
        >
          <div style={{ height: 72, position: "relative", overflow: "hidden", flexShrink: 0, background: "#0f172a" }}>
            <img src={albumArt(room.art)} onError={onArtError} alt={room.title} style={{ width: "100%", height: "100%", objectFit: "cover", opacity: 0.35 }} />
            <div style={{ position: "absolute", inset: 0, padding: "8px 10px", display: "flex", alignItems: "flex-start", justifyContent: "space-between" }}>
              <LiveDot count={room.listenerCount} />
              <BadgePill badge={room.badge} />
            </div>
          </div>
          <div style={{ padding: "12px 14px 14px", flex: 1, display: "flex", flexDirection: "column", gap: 3 }}>
            <div style={{ fontSize: 14, fontWeight: 600, color: "var(--lounge-text-title)", lineHeight: 1.3, overflow: "hidden", display: "-webkit-box", WebkitLineClamp: 2, WebkitBoxOrient: "vertical" }}>
              {room.title}
            </div>
            <div style={{ fontSize: 12, color: "var(--lounge-text-muted)" }}>
              <span className="lounge-host-label">Host: </span><span className="lounge-host-name">{room.hostName}</span>
            </div>
            <div style={{ fontSize: 11.5, color: "var(--lounge-text-muted)", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
              {room.currentTrackTitle} — {room.currentArtist}
            </div>
            {room.hostGear && (
              <div style={{ fontFamily: MONO, fontSize: 9.5, color: "var(--lounge-text-faint)", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", marginTop: 1 }}>
                {room.hostGear}
              </div>
            )}
            <div style={{ marginTop: "auto", paddingTop: 10, display: "flex", alignItems: "center", justifyContent: "space-between" }}>
              <span className="lounge-spec-pill">{room.audioSpec}</span>
              <button onClick={() => onJoin(room)} className="btn-join-lounge" style={{ fontSize: 11, padding: "4px 12px", height: 28 }}>
                Join
              </button>
            </div>
          </div>
        </div>
      ))}
      {rooms.length === 0 && (
        <div className="col-span-3" style={{ padding: "48px 0", textAlign: "center", color: "#71717a", fontFamily: MONO, fontSize: 12 }}>
          {emptyLabel}
        </div>
      )}
    </div>
  )
}

function ListRow({ room, onJoin }: { room: LoungeRoom; onJoin: (room: LoungeRoom) => void }) {
  const [hovered, setHovered] = useState(false)
  const intimate = room.listenerCount <= 5
  const dotColor = intimate ? "#d97706" : "#38bdf8"

  return (
    <div
      className="lounge-list-row"
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        padding: "10px 16px",
        transition: "background 0.12s",
        background: hovered ? undefined : "transparent",
      }}
    >
      {/* Col 1 — Status */}
      <div className="lounge-list-status">
        <span style={{ width: 7, height: 7, borderRadius: "50%", background: dotColor, boxShadow: `0 0 5px ${dotColor}`, flexShrink: 0, animation: "pulse 2s ease-in-out infinite" }} />
        <span style={{ fontFamily: MONO, fontSize: 10.5, fontWeight: 600, color: dotColor, whiteSpace: "nowrap" }}>
          {intimate ? `${room.listenerCount} · Cozy` : `${room.listenerCount} live`}
        </span>
        {room.badge && <BadgePill badge={room.badge} />}
      </div>

      {/* Col 2 — Room & Host */}
      <div style={{ minWidth: 0 }}>
        <div style={{ fontSize: 13, fontWeight: 600, color: "var(--lounge-text-title)", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
          {room.title}
        </div>
        <div style={{ fontSize: 12, color: "var(--lounge-text-muted)", marginTop: 1 }}>
          <span className="lounge-host-label">Host: </span><span className="lounge-host-name">{room.hostName}</span>
        </div>
      </div>

      {/* Col 3 — Now Playing */}
      <div style={{ minWidth: 0 }}>
        <div style={{ fontFamily: MONO, fontSize: 11.5, color: "var(--lounge-text-title)", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
          {room.currentTrackTitle}
        </div>
        <div style={{ fontFamily: MONO, fontSize: 10.5, color: "var(--lounge-text-muted)", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", marginTop: 1 }}>
          {room.currentArtist}
        </div>
      </div>

      {/* Col 4 — Audio Chain */}
      <div style={{ display: "flex", flexDirection: "column", gap: 3, alignItems: "flex-start" }}>
        <span className="lounge-spec-pill" style={{ fontSize: 9.5 }}>{room.audioSpec}</span>
        {room.hostGear && (
          <span style={{ fontFamily: MONO, fontSize: 9, color: "var(--lounge-text-faint)", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", maxWidth: "100%" }}>
            {room.hostGear}
          </span>
        )}
      </div>

      {/* Col 5 — Action */}
      <div style={{ display: "flex", justifyContent: "flex-end" }}>
        <button
          onClick={() => onJoin(room)}
          className="btn-join-lounge"
          style={{ fontSize: 11, padding: "4px 12px", height: 28, whiteSpace: "nowrap" }}
        >
          Join Lounge
        </button>
      </div>
    </div>
  )
}

function ListView({ rooms, onJoin, emptyLabel }: { rooms: LoungeRoom[]; onJoin: (room: LoungeRoom) => void; emptyLabel: string }) {
  return (
    <div className="lounge-list-table flex flex-col">
      {/* Header */}
      <div className="lounge-list-header" style={{ padding: "6px 16px 8px", borderBottom: "1px solid var(--lounge-border-row)" }}>
        {["STATUS", "ROOM & HOST", "NOW PLAYING", "AUDIO CHAIN", ""].map((h, i) => (
          <div key={i} style={{ fontFamily: MONO, fontSize: 9.5, fontWeight: 600, color: "var(--lounge-text-faint)", letterSpacing: "0.06em" }}>
            {h}
          </div>
        ))}
      </div>
      <div
        className="flex flex-col lounge-list-divider"
        style={{ maxHeight: "calc(100vh - 170px)", overflowY: "auto", paddingBottom: 24 }}
      >
        {rooms.map((room) => (
          <ListRow key={room.id} room={room} onJoin={onJoin} />
        ))}
        {rooms.length === 0 && (
          <div style={{ padding: "40px 0", textAlign: "center", color: "#71717a", fontFamily: MONO, fontSize: 12 }}>
            {emptyLabel}
          </div>
        )}
      </div>
    </div>
  )
}

const GRID_ICON = (
  <svg width="13" height="13" viewBox="0 0 16 16" fill="currentColor">
    <rect x="1" y="1" width="6" height="6" rx="1"/><rect x="9" y="1" width="6" height="6" rx="1"/>
    <rect x="1" y="9" width="6" height="6" rx="1"/><rect x="9" y="9" width="6" height="6" rx="1"/>
  </svg>
)
const LIST_ICON = (
  <svg width="13" height="13" viewBox="0 0 16 16" fill="currentColor">
    <rect x="1" y="2" width="14" height="2" rx="1"/><rect x="1" y="7" width="14" height="2" rx="1"/>
    <rect x="1" y="12" width="14" height="2" rx="1"/>
  </svg>
)

/**
 * 열려 있는 라운지 전체 목록. Live Lounges 첫 화면은 큐레이션(스포트라이트·카테고리)이라
 * 방이 늘어나면 다 담지 못한다. 여기는 그냥 전부 보여 주고 검색·필터로 좁힌다.
 *
 * 방 목록은 Core 가 진실이다 — 이 화면은 자기 목록을 따로 들고 있지 않는다.
 */
export default function AllLoungesDirectory({ rooms, onJoin, onBack }: {
  /** Core 의 실제 룸 목록. */
  rooms: LoungeRoom[]
  onJoin: (roomId: string) => void
  onBack: () => void
}) {
  const [viewMode, setViewMode] = useState<"grid" | "list">("grid")
  const [search, setSearch] = useState("")
  const [audioFilter, setAudioFilter] = useState<AudioFilter>("all")

  // 비공개 방은 링크를 아는 사람만 들어간다. 목록에 실으면 비공개가 아니다.
  const publicRooms = useMemo(() => rooms.filter(r => r.visibility !== "unlisted"), [rooms])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    return publicRooms.filter((r) => {
      if (!matchesAudioFilter(r, audioFilter)) return false
      if (!q) return true
      return (
        r.title.toLowerCase().includes(q) ||
        r.hostName.toLowerCase().includes(q) ||
        r.currentTrackTitle.toLowerCase().includes(q) ||
        r.currentArtist.toLowerCase().includes(q)
      )
    })
  }, [publicRooms, search, audioFilter])

  const handleJoin = (room: LoungeRoom) => onJoin(room.id)
  const emptyLabel = publicRooms.length === 0
    ? "지금 열려 있는 라운지가 없습니다."
    : "이 필터에 맞는 방이 없습니다."

  return (
    <div
      className="lounge-directory-container"
      style={{ width: "100%", maxWidth: 1440, margin: "0 auto", padding: "24px 32px" }}
    >
      {/* Page header */}
      <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 22 }}>
        <button
          onClick={onBack}
          style={{ background: "none", border: "none", cursor: "pointer", color: "var(--lounge-text-muted)", padding: "4px 0", display: "flex", alignItems: "center", gap: 5, fontSize: 12, fontFamily: "inherit" }}
        >
          <svg width="12" height="12" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M10 3L5 8l5 5"/></svg>
          Live Lounges
        </button>
        <span style={{ color: "var(--lounge-text-faint)", fontSize: 12 }}>/</span>
        <span style={{ fontSize: 14, fontWeight: 600, color: "var(--lounge-text-title)" }}>All Rooms Directory</span>
      </div>

      {/* Control toolbar */}
      <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 16, flexWrap: "wrap" }}>
        {/* Search */}
        <div style={{ position: "relative", flexShrink: 0 }}>
          <svg
            width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"
            style={{ position: "absolute", left: 9, top: "50%", transform: "translateY(-50%)", color: "var(--lounge-text-faint)", pointerEvents: "none" }}
          >
            <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
          </svg>
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Filter lounges by title, host, artist..."
            className="lounge-directory-search"
            style={{ height: 34, width: 280, paddingLeft: 30, paddingRight: 10, borderRadius: 6, fontSize: 12, fontFamily: "inherit", outline: "none" }}
          />
        </div>

        {/* Audio filter pills */}
        <div style={{ display: "flex", alignItems: "center", gap: 4 }}>
          {AUDIO_FILTERS.map(({ id, label }) => {
            const active = audioFilter === id
            return (
              <button
                key={id}
                onClick={() => setAudioFilter(id)}
                className={`lounge-directory-filter-pill${active ? " active" : ""}`}
                style={{ fontSize: 11, padding: "4px 10px", borderRadius: 20, fontFamily: "inherit", cursor: "pointer" }}
              >
                {label}
              </button>
            )
          })}
        </div>

        {/* Room count */}
        <span
          className="lounge-directory-count"
          style={{ fontFamily: MONO, fontSize: 10.5, marginLeft: 4 }}
        >
          Showing {filtered.length} Active Room{filtered.length !== 1 ? "s" : ""}
        </span>

        {/* View toggle — pushed right */}
        <div style={{ marginLeft: "auto", display: "flex", alignItems: "center", gap: 1, background: "var(--lounge-dir-toggle-track)", borderRadius: 7, padding: 2, border: "1px solid var(--lounge-dir-toggle-border)" }}>
          {(["grid", "list"] as const).map((mode) => {
            const active = viewMode === mode
            return (
              <button
                key={mode}
                onClick={() => setViewMode(mode)}
                title={mode === "grid" ? "Card view" : "List view"}
                className={`lounge-dir-view-btn${active ? " active" : ""}`}
                style={{ width: 28, height: 28, borderRadius: 5, display: "flex", alignItems: "center", justifyContent: "center", cursor: "pointer" }}
              >
                {mode === "grid" ? GRID_ICON : LIST_ICON}
              </button>
            )
          })}
        </div>
      </div>

      {/* Content */}
      {viewMode === "grid"
        ? <CardView rooms={filtered} onJoin={handleJoin} emptyLabel={emptyLabel} />
        : <ListView rooms={filtered} onJoin={handleJoin} emptyLabel={emptyLabel} />
      }
    </div>
  )
}
