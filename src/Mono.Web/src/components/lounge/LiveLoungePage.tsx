import { useState, useRef, useCallback, useEffect } from "react"
import type { ShareData } from "../ShareModal"
import { type LoungeRoom } from "../../data/types"
import { MonoIcon } from "../icons/MonoIcons"
import { albumArt, onArtError } from "../../lib/artwork"


// ── Live Lounge Page ─────────────────────────────────────────────────────────
// ── Host Modal ───────────────────────────────────────────────────────────────
export interface HostLaunchOpts { visibility: "public" | "unlisted"; secretKey?: string; title?: string; note?: string }

interface HostModalProps {
  onClose: () => void
  onLaunch: (opts: HostLaunchOpts) => void
}

function HostModal({ onClose, onLaunch }: HostModalProps) {
  const [title, setTitle] = useState("")
  const [note, setNote] = useState("")
  const [privacy, setPrivacy] = useState<"public" | "unlisted">("public")
  const [silentSession, setSilentSession] = useState(false)

  return (
    <div
      style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,0.5)", zIndex: 200, display: "flex", alignItems: "center", justifyContent: "center" }}
      onClick={(e) => { if (e.target === e.currentTarget) onClose() }}
    >
      <div className="modal-host-container" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-start justify-between mb-5">
          <div>
            <div className="modal-host-title">Host a Mono Session</div>
            <div className="modal-host-subtitle">Broadcast bit-perfect stream across Qobuz &amp; TIDAL</div>
          </div>
          <button onClick={onClose} className="modal-host-close" aria-label="Close">
            <MonoIcon.Close size={16} />
          </button>
        </div>

        <div style={{ marginBottom: 14 }}>
          <label className="modal-host-label">Session Title</label>
          <input
            type="text"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder="e.g., Late Night Reference Masters"
            className="modal-host-input"
          />
        </div>

        <div style={{ marginBottom: 14 }}>
          <label className="modal-host-label">
            Curator&apos;s Note <span className="modal-host-label-muted">(Optional)</span>
          </label>
          <textarea
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Mastering dynamics, DR focus, or equipment notes..."
            rows={3}
            className="modal-host-input"
            style={{ resize: "none" }}
          />
        </div>

        <div className="modal-host-infobox">
          Source: Qobuz Studio (Up to 24/192) &nbsp;•&nbsp; Protocol: Lossless P2P / ISRC Cross-Sync
        </div>

        <div style={{ marginBottom: 16 }}>
          <label className="modal-host-label" style={{ marginBottom: 8 }}>Session Privacy</label>
          <div style={{ display: "flex", gap: 8 }}>
            {(["public", "unlisted"] as const).map((opt) => {
              const isSelected = privacy === opt
              const selectedBorder = opt === "unlisted" ? "#7C3AED" : "#2563EB"
              const selectedBg = opt === "unlisted" ? "rgba(109,40,217,0.08)" : "rgba(37,99,235,0.08)"
              const selectedText = opt === "unlisted" ? "#a78bfa" : "#60a5fa"
              return (
                <button
                  key={opt}
                  type="button"
                  onClick={() => setPrivacy(opt)}
                  style={{
                    flex: 1, padding: "9px 12px", borderRadius: 9, cursor: "pointer",
                    fontSize: 12.5, fontWeight: isSelected ? 600 : 400, fontFamily: "inherit",
                    textAlign: "left", display: "flex", alignItems: "flex-start", gap: 8,
                    transition: "all 0.15s",
                    border: `1.5px solid ${isSelected ? selectedBorder : "var(--modal-host-option-idle-border)"}`,
                    background: isSelected ? selectedBg : "var(--modal-host-option-idle-bg)",
                    color: isSelected ? selectedText : "var(--modal-host-option-idle-text)",
                  }}
                >
                  <span style={{ display: "flex", alignItems: "center", lineHeight: 1.2, flexShrink: 0 }}>
                    {opt === "public" ? <MonoIcon.Globe size={15} /> : <MonoIcon.Lock size={15} />}
                  </span>
                  <div>
                    <div style={{ fontWeight: isSelected ? 600 : 500, marginBottom: 1 }}>{opt === "public" ? "Public Session" : "Invite Only"}</div>
                    <div style={{ fontSize: 10.5, fontWeight: 400, color: "var(--modal-host-option-desc)", lineHeight: 1.4 }}>{opt === "public" ? "Listed on Live Lounges explore feed." : "Unlisted. Only accessible via secret link."}</div>
                  </div>
                </button>
              )
            })}
          </div>
        </div>

        <label className="flex items-center gap-2" style={{ cursor: "pointer", marginBottom: 24, userSelect: "none" }}>
          <input type="checkbox" checked={silentSession} onChange={(e) => setSilentSession(e.target.checked)} style={{ width: 15, height: 15, accentColor: "#2563EB", cursor: "pointer" }} />
          <span className="modal-host-checkbox-label">
            Silent Session <span className="modal-host-checkbox-muted">(Focus purely on audio without chat distraction)</span>
          </span>
        </label>

        <div className="flex items-center justify-end gap-3">
          <button onClick={onClose} className="modal-host-cancel">Cancel</button>
          <button
            onClick={() => { const secretKey = privacy === "unlisted" ? Math.random().toString(36).substring(2, 10) : undefined; onLaunch({ visibility: privacy, secretKey, title: title.trim() || undefined, note: note.trim() || undefined }) }}
            style={{ fontSize: 13, fontWeight: 600, color: "#FFFFFF", background: "#2563EB", border: "none", borderRadius: 8, padding: "9px 20px", cursor: "pointer", fontFamily: "inherit", display: "flex", alignItems: "center", gap: 7, transition: "background 0.15s" }}
            onMouseEnter={(e) => ((e.currentTarget as HTMLButtonElement).style.background = "#1D4ED8")}
            onMouseLeave={(e) => ((e.currentTarget as HTMLButtonElement).style.background = "#2563EB")}
          >
            <span style={{ width: 7, height: 7, borderRadius: "50%", background: "#fff", display: "inline-block", flexShrink: 0 }} />
            Start Broadcasting (ON AIR)
          </button>
        </div>
      </div>
    </div>
  )
}

// ── Discovery Badge ───────────────────────────────────────────────────────────
function DiscoveryBadge({ badge }: { badge: LoungeRoom["badge"] }) {
  if (!badge) return null
  const configs = {
    trending: { label: "Hot Session", icon: <MonoIcon.Flame size={12} />, color: "#FCA5A5", bg: "rgba(127,29,29,0.5)", border: "rgba(185,28,28,0.5)" },
    debut:    { label: "Debut Host",  icon: <MonoIcon.Sprout size={12} />, color: "#6EE7B7", bg: "rgba(6,78,59,0.5)",   border: "rgba(5,150,105,0.5)" },
    indie:    { label: "Deep Indie",  icon: <MonoIcon.Guitar size={12} />, color: "#D8B4FE", bg: "rgba(59,7,100,0.5)",  border: "rgba(126,34,206,0.5)" },
    cozy:     { label: "Cozy Space",  icon: <MonoIcon.Cup size={12} />, color: "#FCD34D", bg: "rgba(120,53,15,0.5)", border: "rgba(180,83,9,0.5)" },
  } as const
  const c = configs[badge]
  return (
    <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 9.5, fontWeight: 600, color: c.color, background: c.bg, border: `1px solid ${c.border}`, borderRadius: 5, padding: "2px 7px", letterSpacing: "0.04em", display: "inline-flex", alignItems: "center", gap: 4, whiteSpace: "nowrap" }}>
      {c.icon}
      <span>{c.label}</span>
    </span>
  )
}

// ── Lounge Grid Card ──────────────────────────────────────────────────────────
function LoungeGridCard({ room, onJoin, onShare }: { room: LoungeRoom; onJoin: () => void; onShare: (room: LoungeRoom) => void }) {
  const isIntimate = room.listenerCount <= 5
  const listenerColor = isIntimate ? "#D97706" : "#60a5fa"

  return (
    <div className="lounge-room-card">
      <div style={{ height: 76, background: "#0F172A", position: "relative", overflow: "hidden", flexShrink: 0 }}>
        <img src={albumArt(room.art)} onError={onArtError} alt={room.title} style={{ width: "100%", height: "100%", objectFit: "cover", opacity: 0.35 }} />
        <div style={{ position: "absolute", top: 9, left: 10, right: 10, display: "flex", alignItems: "center", justifyContent: "space-between" }}>
          <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, fontWeight: 600, color: listenerColor, background: "rgba(0,0,0,0.55)", borderRadius: 20, padding: "3px 9px", display: "inline-flex", alignItems: "center", gap: 5 }}>
            {isIntimate ? <MonoIcon.Cup size={11} /> : <span style={{ fontSize: 8 }}>●</span>}
            <span>{room.listenerCount} {isIntimate ? "listening" : "live"}</span>
          </span>
          {room.badge && <DiscoveryBadge badge={room.badge} />}
        </div>
      </div>
      <div style={{ padding: "13px 15px 15px", flex: 1, display: "flex", flexDirection: "column", gap: 4 }}>
        <div className="leading-snug line-clamp-2" style={{ fontSize: 15, fontWeight: 600, color: "var(--lounge-text-title)", lineHeight: 1.35 }}>
          {room.title}
        </div>
        <div style={{ fontSize: 12, color: "var(--lounge-text-muted)" }}>
          Host: <span style={{ color: "var(--lounge-text-title)", fontWeight: 500 }}>{room.hostName}</span>
        </div>
        <div style={{ fontSize: 12, color: "var(--lounge-text-muted)", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
          {room.currentTrackTitle} — {room.currentArtist}
        </div>
        {room.hostGear && (
          <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, color: "var(--lounge-text-faint)", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
            {room.hostGear}
          </div>
        )}
        <div style={{ marginTop: "auto", paddingTop: 10, display: "flex", alignItems: "center", justifyContent: "space-between", gap: 8 }}>
          <span className="lounge-spec-pill shrink-0">{room.audioSpec}</span>
          <div style={{ display: "flex", alignItems: "center", gap: 6, flexShrink: 0 }}>
            <button
              onClick={(e) => { e.stopPropagation(); onShare(room) }}
              title="Share Session"
              className="lounge-btn-load flex items-center"
              style={{ padding: "4px 8px", borderRadius: "0.5rem" }}
            >
              <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <circle cx="18" cy="5" r="3"/><circle cx="6" cy="12" r="3"/><circle cx="18" cy="19" r="3"/>
                <line x1="8.59" y1="13.51" x2="15.42" y2="17.49"/><line x1="15.41" y1="6.51" x2="8.59" y2="10.49"/>
              </svg>
            </button>
            <button onClick={onJoin} className="btn-join-lounge">
              Join Lounge
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}

// ── Spotlight entries ────────────────────────────────────────────────────────

interface SpotlightEntry {
  channel: "trending" | "debut" | "vault"
  badgeLabel: string
  badgeStyle: { color: string; bg: string; border: string }
  title: string
  subtitle: string
  specs: string[]
  listenerStat: string
  ctaLabel: string
  art: string
  roomId: string
}

const SPOTLIGHT_CHANNELS = [
  {
    channel: "trending" as const,
    badgeLabel: "FEATURED: HEADLINER",
    badgeStyle: { color: "#7C3AED", bg: "rgba(124,58,237,0.15)", border: "rgba(124,58,237,0.3)" },
    ctaLabel: "Join Spotlight Room",
  },
  {
    channel: "debut" as const,
    badgeLabel: "DEBUT SPOTLIGHT: NEW SELECTOR",
    badgeStyle: { color: "#059669", bg: "rgba(5,150,105,0.15)", border: "rgba(5,150,105,0.3)" },
    ctaLabel: "Say Hi and Join Debut",
  },
  {
    channel: "vault" as const,
    badgeLabel: "CURATOR VAULT: DEEP DIG",
    badgeStyle: { color: "#B45309", bg: "rgba(180,83,9,0.15)", border: "rgba(180,83,9,0.3)" },
    ctaLabel: "Explore Rare Vault",
  },
]

/**
 * 세 칸의 스포트라이트를 실제 룸으로 채운다 — 청취자 많은 순, 신규 호스트, 그 외.
 * 채울 룸이 없는 칸은 아예 만들지 않는다(빈 배너보다 없는 편이 낫다).
 */
function buildSpotlights(rooms: LoungeRoom[]): SpotlightEntry[] {
  if (rooms.length === 0) return []
  const byListeners = [...rooms].sort((a, b) => b.listenerCount - a.listenerCount)
  const picks = [
    byListeners[0],
    byListeners.find((r) => r.category === "debut" && r.id !== byListeners[0].id),
    byListeners.find((r) => r.id !== byListeners[0].id && r.category !== "debut"),
  ]

  return SPOTLIGHT_CHANNELS.flatMap((c, i) => {
    const room = picks[i]
    if (!room) return []
    return [{
      ...c,
      title: room.title,
      subtitle: [
        `Host: ${room.hostName}`,
        room.currentArtist ? `${room.currentArtist} — ${room.currentTrackTitle}` : room.currentTrackTitle,
      ].filter(Boolean).join(" · "),
      specs: [room.audioSpec, room.hostGear].filter((x): x is string => !!x),
      listenerStat: `${room.listenerCount} listening`,
      art: room.art,
      roomId: room.id,
    }]
  })
}

// ── Live Lounges Page ────────────────────────────────────────────────────────
export default function LiveLoungePage({ onJoin, onGoLive, onOpenShare, rooms, archives, mostPlayed, onLoadArchive }: {
  /** roomId 없이 부르면 첫 라운지로 들어간다(디자인의 기본 CTA). */
  onJoin: (roomId?: string) => void
  onGoLive: (opts: HostLaunchOpts) => void
  onOpenShare: (data: ShareData) => void
  /** Core 의 실제 룸 목록. */
  rooms: LoungeRoom[]
  /** 종료된 세션 아카이브 — Hall of Fame. */
  archives: { id: string; title: string; host: string; tracks: number; date: string; art: string }[]
  /** 청취 이력에서 집계한 재생 순위. */
  mostPlayed: { rank: number; track: string; artist: string; plays: number; dr: string }[]
  onLoadArchive: (archiveId: string) => void
}) {
  const [selectedCategory, setSelectedCategory] = useState<"all" | "popular" | "debut" | "indie" | "cozy">("all")
  const [isHostModalOpen, setIsHostModalOpen] = useState(false)

  const handleLaunch = (opts: HostLaunchOpts) => {
    setIsHostModalOpen(false)
    onGoLive(opts)
  }

  const getDisplayedRooms = (): LoungeRoom[] => {
    const publicRooms = rooms.filter(r => r.visibility !== "unlisted")
    if (selectedCategory !== "all") {
      return publicRooms.filter(r =>
        selectedCategory === "popular" ? r.listenerCount >= 20 : r.category === selectedCategory
      )
    }
    const popular = publicRooms.filter(r => r.listenerCount >= 20)
    const discovery = publicRooms.filter(r => r.listenerCount < 20)
    const result: LoungeRoom[] = []
    let pIdx = 0
    let dIdx = 0
    while (pIdx < popular.length || dIdx < discovery.length) {
      if (pIdx < popular.length) result.push(popular[pIdx++])
      if (pIdx < popular.length) result.push(popular[pIdx++])
      if (dIdx < discovery.length) result.push(discovery[dIdx++])
    }
    return result
  }

  const displayedRooms = getDisplayedRooms()

  const [activeSpotlightIndex, setActiveSpotlightIndex] = useState<number>(0)
  const SPOTLIGHT_ENTRIES = buildSpotlights(rooms)
  const [isAutoCyclePlaying, setIsAutoCyclePlaying] = useState<boolean>(true)
  const [isSpotlightHovered, setIsSpotlightHovered] = useState<boolean>(false)
  const [spotlightFade, setSpotlightFade] = useState<boolean>(true)

  const spotlightIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null)

  const advanceSpotlight = useCallback((direction: number = 1) => {
    setSpotlightFade(false)
    setTimeout(() => {
      setActiveSpotlightIndex((prev) => (prev + direction + SPOTLIGHT_ENTRIES.length) % SPOTLIGHT_ENTRIES.length)
      setSpotlightFade(true)
    }, 160)
  }, [])

  useEffect(() => {
    if (spotlightIntervalRef.current) clearInterval(spotlightIntervalRef.current)
    if (isAutoCyclePlaying && !isSpotlightHovered) {
      spotlightIntervalRef.current = setInterval(() => advanceSpotlight(1), 8000)
    }
    return () => { if (spotlightIntervalRef.current) clearInterval(spotlightIntervalRef.current) }
  }, [isAutoCyclePlaying, isSpotlightHovered, advanceSpotlight])

  const activeSpotlight = SPOTLIGHT_ENTRIES[activeSpotlightIndex % Math.max(1, SPOTLIGHT_ENTRIES.length)]

  return (
    <div style={{ maxWidth: 1260, margin: "0 auto", padding: "40px 64px" }}>
      {isHostModalOpen && (
        <HostModal onClose={() => setIsHostModalOpen(false)} onLaunch={handleLaunch} />
      )}

      <div className="flex items-start justify-between mb-8">
        <div>
          <h1 className="lounge-header-title">Live Lounges</h1>
          <p className="lounge-header-subtitle">Real-time bit-perfect collective listening rooms</p>
        </div>
        <button
          onClick={() => setIsHostModalOpen(true)}
          className="lounge-btn-host"
        >
          + Host New Lounge
        </button>
      </div>

      {/* Spotlight Hero — 띄울 룸이 없으면 배너 자체를 내린다. */}
      {activeSpotlight && (
      <div
        onMouseEnter={() => setIsSpotlightHovered(true)}
        onMouseLeave={() => setIsSpotlightHovered(false)}
        className="lounge-spotlight-banner"
      >
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 18 }}>
          <span className="spotlight-category-tag" style={{ transition: "opacity 0.3s" }}>
            {activeSpotlight.badgeLabel}
          </span>
          <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
            {([{ index: 0, label: "Trending" }, { index: 1, label: "Debut Spotlight" }, { index: 2, label: "Curator's Vault" }] as { index: number; label: string }[]).map(({ index, label }) => (
              <button
                key={index}
                type="button"
                onClick={(e) => { e.stopPropagation(); setSpotlightFade(false); setTimeout(() => { setActiveSpotlightIndex(index); setSpotlightFade(true) }, 160); setIsAutoCyclePlaying(false) }}
                style={{ background: activeSpotlightIndex === index ? "var(--lounge-spotlight-tab-active-bg)" : "none", backdropFilter: activeSpotlightIndex === index ? "blur(8px)" : "none", border: "none", borderRadius: 20, cursor: "pointer", fontSize: 11.5, fontWeight: activeSpotlightIndex === index ? 600 : 400, color: activeSpotlightIndex === index ? "var(--lounge-spotlight-tab-active-text)" : "var(--lounge-spotlight-tab-idle-text)", padding: "4px 10px", fontFamily: "inherit", transition: "background 0.2s, color 0.2s, font-weight 0.15s" }}
                onMouseEnter={(e) => { if (activeSpotlightIndex !== index) (e.currentTarget as HTMLButtonElement).style.color = "var(--lounge-spotlight-tab-hover-text)" }}
                onMouseLeave={(e) => { if (activeSpotlightIndex !== index) (e.currentTarget as HTMLButtonElement).style.color = "var(--lounge-spotlight-tab-idle-text)" }}
              >
                {label}
              </button>
            ))}
            <div style={{ width: 1, height: 14, background: "var(--lounge-spotlight-divider)", margin: "0 2px" }} />
            <button
              type="button"
              title={isAutoCyclePlaying ? "Pause rotation" : "Resume auto-rotation"}
              onClick={(e) => { e.stopPropagation(); setIsAutoCyclePlaying((prev) => !prev) }}
              style={{ background: isAutoCyclePlaying ? "none" : "rgba(255,255,255,0.1)", border: "none", borderRadius: "50%", width: 26, height: 26, display: "flex", alignItems: "center", justifyContent: "center", cursor: "pointer", transition: "background 0.2s, color 0.2s", color: isAutoCyclePlaying ? "#6B7280" : "#34D399" }}
              onMouseEnter={(e) => { (e.currentTarget as HTMLButtonElement).style.background = "rgba(255,255,255,0.12)"; if (isAutoCyclePlaying) (e.currentTarget as HTMLButtonElement).style.color = "#FFFFFF" }}
              onMouseLeave={(e) => { const btn = e.currentTarget as HTMLButtonElement; btn.style.background = isAutoCyclePlaying ? "none" : "rgba(255,255,255,0.1)"; btn.style.color = isAutoCyclePlaying ? "#6B7280" : "#34D399" }}
            >
              {isAutoCyclePlaying ? (
                <svg width="10" height="11" viewBox="0 0 10 12" fill="currentColor"><rect x="0" y="0" width="3.5" height="12" rx="1" /><rect x="6.5" y="0" width="3.5" height="12" rx="1" /></svg>
              ) : (
                <svg width="10" height="11" viewBox="0 0 10 12" fill="currentColor"><path d="M0 0 L10 6 L0 12 Z" /></svg>
              )}
            </button>
          </div>
        </div>

        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 24, opacity: spotlightFade ? 1 : 0, transition: "opacity 0.16s ease" }}>
          <div style={{ display: "flex", alignItems: "center", gap: 18 }}>
            <img src={albumArt(activeSpotlight.art)} onError={onArtError} alt={activeSpotlight.title} style={{ width: 76, height: 76, borderRadius: 12, objectFit: "cover", flexShrink: 0, border: "2px solid var(--lounge-spotlight-art-border)" }} />
            <div>
              <div style={{ fontSize: 18, fontWeight: 700, marginBottom: 4, letterSpacing: "-0.3px", color: "var(--lounge-spotlight-title)" }}>{activeSpotlight.title}</div>
              <div style={{ fontSize: 12.5, marginBottom: 10, color: "var(--lounge-spotlight-meta)" }}>{activeSpotlight.subtitle}</div>
              <div style={{ display: "flex", alignItems: "center", gap: 6, flexWrap: "wrap" }}>
                {activeSpotlight.specs.map((spec) => (
                  <span key={spec} className="spotlight-spec-tag">{spec}</span>
                ))}
              </div>
            </div>
          </div>
          <div style={{ display: "flex", flexDirection: "column", alignItems: "flex-end", gap: 10, flexShrink: 0 }}>
            <span className="spotlight-listener-stat">{activeSpotlight.listenerStat}</span>
            <button
              type="button"
              onClick={() => onJoin(activeSpotlight.roomId)}
              className="lounge-spotlight-btn"
            >
              {activeSpotlight.ctaLabel}
            </button>
            <div style={{ display: "flex", gap: 5, marginTop: 2 }}>
              {SPOTLIGHT_ENTRIES.map((_, i) => (
                <button
                  key={i}
                  type="button"
                  onClick={(e) => { e.stopPropagation(); setSpotlightFade(false); setTimeout(() => { setActiveSpotlightIndex(i); setSpotlightFade(true) }, 160); setIsAutoCyclePlaying(false) }}
                  style={{ width: i === activeSpotlightIndex ? 16 : 5, height: 5, borderRadius: 3, border: "none", padding: 0, cursor: "pointer", background: i === activeSpotlightIndex ? "var(--lounge-spotlight-dot-active)" : "var(--lounge-spotlight-dot-idle)", transition: "width 0.3s ease, background 0.3s ease" }}
                />
              ))}
            </div>
          </div>
        </div>
      </div>
      )}

      {/* Category filter pills */}
      <div className="flex items-center gap-2 mb-6 flex-wrap">
        {(
          [
            { id: "all",     label: "All Lounges",  icon: null },
            { id: "popular", label: "Trending",     icon: <MonoIcon.Flame size={14} className="text-orange-500" /> },
            { id: "debut",   label: "Debut Host",   icon: <MonoIcon.Sprout size={14} className="text-emerald-500" /> },
            { id: "indie",   label: "Deep Indie",   icon: <MonoIcon.Guitar size={14} className="text-violet-400" /> },
            { id: "cozy",    label: "Cozy (1–5)",   icon: <MonoIcon.Cup size={14} className="text-amber-500" /> },
          ] as { id: typeof selectedCategory; label: string; icon: React.ReactNode | null }[]
        ).map(({ id, label, icon }) => {
          const active = selectedCategory === id
          return (
            <button
              key={id}
              onClick={() => setSelectedCategory(id)}
              className={["lounge-filter-pill", active ? "lounge-filter-pill-active" : ""].join(" ")}
            >
              {icon && <span className="flex items-center">{icon}</span>}
              <span>{label}</span>
            </button>
          )
        })}
        <span className="text-xs text-zinc-400 font-medium ml-auto">{displayedRooms.length} room{displayedRooms.length !== 1 ? "s" : ""}</span>
      </div>

      {/* Lounge Grid */}
      <div className="grid grid-cols-3 gap-6 mb-12">
        {displayedRooms.map((room) => (
          <LoungeGridCard
            key={room.id}
            room={room}
            onJoin={onJoin}
            onShare={(r) => onOpenShare({
              type: "lounge",
              id: r.id,
              title: r.title,
              subtitle: `${r.currentArtist} — ${r.currentTrackTitle}`,
              coverUrl: r.art,
              audioSpec: `${r.audioSpec} · Bit-Perfect Host Stream`,
              hostGear: r.hostGear ? `Host: ${r.hostName} · ${r.hostGear}` : `Host: ${r.hostName}`,
            })}
          />
        ))}
        {displayedRooms.length === 0 && (
          <div className="col-span-3" style={{ textAlign: "center", padding: "48px 0", color: "#9CA3AF", fontFamily: "'DM Mono', monospace", fontSize: 13 }}>
            No lounges in this category right now.
          </div>
        )}
      </div>

      {/* Hall of Fame + Most Played */}
      <div className="grid grid-cols-12 gap-8 mb-12">
        <div className="col-span-7">
          <h2 className="lounge-section-title mb-4 flex items-center gap-2">
            <MonoIcon.Trophy size={18} className="text-amber-400" />
            <span>Hall of Fame Sessions</span>
          </h2>
          <div className="flex flex-col gap-3">
            {archives.map((s) => (
              <div key={s.id} className="lounge-row p-4 flex items-center gap-4">
                <img src={albumArt(s.art)} onError={onArtError} alt={s.title} style={{ width: 44, height: 44, borderRadius: 8, objectFit: "cover", flexShrink: 0, background: "rgba(255,255,255,0.05)" }} />
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ fontSize: 13.5, fontWeight: 600, color: "var(--lounge-text-title)", marginBottom: 2, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{s.title}</div>
                  <div style={{ fontSize: 12, color: "var(--lounge-text-muted)" }}>Host: {s.host} &nbsp;·&nbsp; {s.tracks} tracks &nbsp;·&nbsp; {s.date}</div>
                </div>
                <button className="lounge-btn-load" style={{ fontFamily: "inherit" }} onClick={() => onLoadArchive(s.id)}>Load Session</button>
              </div>
            ))}
            {archives.length === 0 && (
              <div className="lounge-muted-text" style={{ padding: "24px 4px" }}>아직 보관된 세션이 없습니다.</div>
            )}
          </div>
        </div>

        <div className="col-span-5">
          <h2 className="lounge-section-title mb-4 flex items-center gap-2">
            <MonoIcon.Flame size={18} className="text-orange-400" />
            <span>Lounge Most Played</span>
          </h2>
          <div className="flex flex-col gap-2">
            {mostPlayed.map((item) => (
              <div key={item.rank} className="lounge-row flex items-center gap-3" style={{ padding: "11px 14px" }}>
                <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, fontWeight: 700, color: item.rank === 1 ? "#60a5fa" : "var(--lounge-text-faint)", minWidth: 18, textAlign: "center" }}>#{item.rank}</span>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ fontSize: 13, fontWeight: 600, color: "var(--lounge-text-title)", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{item.track}</div>
                  <div style={{ fontSize: 12, color: "var(--lounge-text-muted)" }}>{item.artist}</div>
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: 8, flexShrink: 0 }}>
                  <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, padding: "2px 6px", borderRadius: 4, background: "rgba(52,211,153,0.10)", border: "1px solid rgba(52,211,153,0.22)", color: "#34d399" }}>{item.dr}</span>
                  <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 10.5, color: "var(--lounge-text-faint)" }}>{item.plays} plays</span>
                  <button style={{ background: "none", border: "none", cursor: "pointer", color: "var(--lounge-text-faint)", padding: "0 2px", display: "flex", alignItems: "center" }} title="Preview">
                    <MonoIcon.PlayMini size={12} />
                  </button>
                </div>
              </div>
            ))}
            {mostPlayed.length === 0 && (
              <div className="lounge-muted-text" style={{ padding: "24px 4px" }}>재생 이력이 쌓이면 순위가 나타납니다.</div>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
