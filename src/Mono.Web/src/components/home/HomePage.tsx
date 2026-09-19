import { useState, useRef, useEffect } from "react"
import type { HomeAlbum, InsightCard, LabelTile, LoungeCard as LoungeCardData } from "../../lib/homeData"
import { MonoIcon } from "../icons/MonoIcons"

// ── Lounge Card ──────────────────────────────────────────────────────────────
function LoungeCard({ card, isJoined, onJoin, onLeave }: {
  card: LoungeCardData
  isJoined: boolean
  onJoin: (id: string, e: React.MouseEvent) => void
  onLeave: (e: React.MouseEvent) => void
}) {
  const [btnHovered, setBtnHovered] = useState(false)

  const handleClick = (e: React.MouseEvent) => {
    e.stopPropagation()
    if (isJoined) {
      onLeave(e)
    } else {
      onJoin(card.id, e)
    }
  }

  // Button appearance
  let btnColor: string
  let btnBg: string
  let btnBorder: string
  if (isJoined && btnHovered) {
    btnColor = "var(--home-leave-text-hover)"
    btnBg = "var(--home-leave-bg-hover)"
    btnBorder = "1px solid var(--home-leave-border-hover)"
  } else if (isJoined) {
    btnColor = "var(--home-joined-text)"
    btnBg = "var(--home-joined-bg)"
    btnBorder = "1px solid var(--home-joined-border)"
  } else if (btnHovered) {
    btnColor = "var(--home-join-text-hover)"
    btnBg = "var(--home-join-bg-hover)"
    btnBorder = "1px solid var(--home-join-border-hover)"
  } else {
    btnColor = "var(--home-join-text)"
    btnBg = "var(--home-join-bg)"
    btnBorder = "1px solid var(--home-join-border)"
  }

  return (
    <div
      style={{
        width: 300, minWidth: 300, height: 170,
        background: "var(--surface-elevated)",
        border: isJoined ? "1px solid rgba(59,130,246,0.5)" : "1px solid var(--border-subtle)",
        borderRadius: 12, padding: 16,
        display: "flex", flexDirection: "column", justifyContent: "space-between",
        transition: "box-shadow 0.15s ease, transform 0.15s ease, border-color 0.15s, background 0.15s",
        flexShrink: 0,
      }}
      onMouseEnter={(e) => { (e.currentTarget as HTMLDivElement).style.boxShadow = "0 4px 16px rgba(0,0,0,0.3)"; (e.currentTarget as HTMLDivElement).style.borderColor = isJoined ? "rgba(59,130,246,0.6)" : "rgba(255,255,255,0.18)"; (e.currentTarget as HTMLDivElement).style.transform = "translateY(-1px)" }}
      onMouseLeave={(e) => { (e.currentTarget as HTMLDivElement).style.boxShadow = "none"; (e.currentTarget as HTMLDivElement).style.borderColor = isJoined ? "rgba(59,130,246,0.45)" : "rgba(255,255,255,0.08)"; (e.currentTarget as HTMLDivElement).style.transform = "translateY(0)" }}
    >
      <div className="flex items-start gap-3">
        <img src={card.art} alt={card.title} style={{ width: 48, height: 48, borderRadius: 8, objectFit: "cover", background: "var(--surface-hover)", flexShrink: 0 }} />
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontSize: 13, fontWeight: 600, color: "var(--text-primary)", lineHeight: "1.3", marginBottom: 2, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{card.title}</div>
          <div style={{ fontSize: 12, color: "var(--text-secondary)" }}>Host: {card.host}</div>
          <div className="flex items-center gap-1 mt-1.5" style={{ fontFamily: "'DM Mono', monospace", fontSize: 10.5, fontWeight: 500, color: "#3B82F6" }}>
            <span style={{ fontSize: 8 }}>●</span>
            {card.listeners} listening
          </div>
        </div>
      </div>

      <div style={{ fontSize: 12, color: "var(--text-secondary)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{card.track}</div>

      <div className="flex items-center justify-between">
        <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, fontWeight: 500, color: "var(--text-muted)", background: "var(--home-badge-bg)", border: "1px solid var(--home-badge-border)", borderRadius: 4, padding: "2px 6px" }}>{card.spec || "Live"}</span>
        <button
          onClick={handleClick}
          onMouseEnter={() => setBtnHovered(true)}
          onMouseLeave={() => setBtnHovered(false)}
          style={{
            fontSize: 12, fontWeight: 500,
            color: btnColor,
            background: btnBg,
            border: btnBorder,
            borderRadius: 7, padding: "5px 12px", cursor: "pointer",
            transition: "color 0.15s ease, background 0.15s ease, border-color 0.15s ease",
            fontFamily: "inherit",
            whiteSpace: "nowrap",
          }}
        >
          {isJoined ? (
            btnHovered ? (
              <span className="inline-flex items-center gap-1"><MonoIcon.Close size={11} /><span>Leave Lounge</span></span>
            ) : (
              <span className="inline-flex items-center gap-1"><MonoIcon.Check size={11} strokeWidth={2.5} /><span>Joined</span></span>
            )
          ) : (
            "Join Lounge"
          )}
        </button>
      </div>
    </div>
  )
}

// ── Mono Sessions Section ────────────────────────────────────────────────────
function SocialLoungesSection({ joinedLoungeId, onToggleJoin, onLeaveLounge, cards, archiveCount }: { joinedLoungeId: string | null; onToggleJoin: (id: string, e: React.MouseEvent) => void; onLeaveLounge: (e: React.MouseEvent) => void; cards: LoungeCardData[]; archiveCount: number }) {
  const railRef = useRef<HTMLDivElement>(null)
  const [activeSessionTab, setActiveSessionTab] = useState<"live" | "hof">("live")

  return (
    <section style={{ background: "var(--surface-card)", border: "1px solid rgba(255,255,255,0.07)", borderRadius: 16, padding: 24, boxShadow: "0 4px 32px rgba(0,0,0,0.4)" }}>
      <div className="flex items-start justify-between mb-4">
        <div>
          <h2 style={{ fontSize: 18, fontWeight: 700, color: "var(--text-primary)", marginBottom: 3 }}>Mono Sessions</h2>
          <p style={{ fontSize: 13, color: "var(--text-secondary)" }}>Real-time bit-perfect sync &amp; session archives</p>
        </div>
        <button style={{ fontSize: 13, fontWeight: 600, color: "#3B82F6", background: "none", border: "none", cursor: "pointer", whiteSpace: "nowrap", paddingTop: 2, fontFamily: "inherit" }}>See All ({cards.length + archiveCount}) →</button>
      </div>

      <div className="flex items-center gap-2 mb-5">
        <button
          onClick={() => setActiveSessionTab("live")}
          className={`session-pill ${activeSessionTab === "live" ? "session-pill-active" : "session-pill-inactive"}`}
        >
          <span style={{ width: 7, height: 7, borderRadius: "50%", background: activeSessionTab === "live" ? "currentColor" : "#3B82F6", display: "inline-block", flexShrink: 0 }} />
          Live Now ({cards.length})
        </button>
        <button
          onClick={() => setActiveSessionTab("hof")}
          className={`session-pill ${activeSessionTab === "hof" ? "session-pill-active" : "session-pill-inactive"}`}
        >
          <MonoIcon.Trophy size={13} className="text-amber-400" />
          <span>Hall of Fame</span>
        </button>
      </div>

      <div ref={railRef} style={{ display: "flex", gap: 16, overflowX: "hidden", paddingBottom: 4 }}>
        {cards.map((card) => (
          <LoungeCard key={card.id} card={card} isJoined={joinedLoungeId === card.roomId} onJoin={onToggleJoin} onLeave={onLeaveLounge} />
        ))}
        {cards.length === 0 && (
          <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 12, color: "var(--text-muted)", padding: "32px 4px" }}>
            지금 열려 있는 라운지가 없습니다. 직접 열어 보세요.
          </div>
        )}
      </div>
    </section>
  )
}

// ── Album Card ───────────────────────────────────────────────────────────────
function AlbumCard({ album, onPlay, onSelect }: { album: HomeAlbum; onPlay?: (album: HomeAlbum) => void; onSelect?: (album: HomeAlbum) => void }) {
  // DR 배지는 여기서 뺐다. 카드 폭이 좁아 포맷 배지와 나란히 두면 둘 다 잘렸고,
  // 잘린 숫자는 없는 것만 못하다. 다이나믹 레인지는 앨범 디테일에서 온전히 보여 준다.
  return (
    <div className="group flex flex-col cursor-pointer" onClick={() => onSelect?.(album)}>
      <div className="relative aspect-square w-full rounded-xl overflow-hidden bg-zinc-200">
        <img src={album.art} alt={album.title} className="w-full h-full object-cover transition-transform duration-300 group-hover:scale-105" />
        <div className="absolute inset-0 bg-black/20 opacity-0 group-hover:opacity-100 transition-opacity duration-200 pointer-events-none" />
        {onPlay && (
          <button
            type="button"
            onClick={(e) => { e.stopPropagation(); onPlay(album) }}
            aria-label={`Play ${album.title}`}
            className="absolute bottom-3 right-3 w-10 h-10 rounded-full text-white flex items-center justify-center shadow-lg backdrop-blur-sm opacity-0 translate-y-1 group-hover:opacity-100 group-hover:translate-y-0 transition-all duration-200 hover:scale-110 z-10"
            style={{ background: "var(--home-play-overlay-bg)" }}
          >
            <svg viewBox="0 0 16 16" className="w-4 h-4 fill-current ml-0.5" aria-hidden="true"><path d="M3 2.5l10 5.5-10 5.5V2.5z" /></svg>
          </button>
        )}
      </div>
      <div className="mt-2.5 flex flex-col">
        <span className="font-medium text-sm truncate" style={{ color: "var(--text-primary)" }}>{album.title}</span>
        <span className="text-xs truncate mt-0.5" style={{ color: "var(--text-secondary)" }}>{album.artist}</span>
        <div className="flex items-center gap-1.5 mt-2">
          <span
            className="text-[11px] font-mono px-2 py-0.5 rounded truncate min-w-0 flex-shrink"
            style={{ fontFamily: "'DM Mono', monospace", background: "var(--home-badge-bg)", color: "var(--home-badge-text)", border: "1px solid var(--home-badge-border)" }}
          >{album.format}</span>
        </div>
      </div>
    </div>
  )
}

type RepertoireSort = "rotation" | "fidelity" | "acquisition"

const REPERTOIRE_SORT_OPTIONS: { key: RepertoireSort; label: string }[] = [
  { key: "rotation",    label: "In Rotation (Default)" },
  { key: "fidelity",   label: "Audio Fidelity" },
  { key: "acquisition", label: "Acquisition Index" },
]

function fidelityRank(fmt: string): number {
  if (/DSD\s*512/i.test(fmt)) return 900
  if (/DSD\s*256/i.test(fmt)) return 800
  if (/DSD\s*128/i.test(fmt)) return 700
  if (/DSD\s*64|DSF|DFF|\bDSD\b/i.test(fmt)) return 600
  if (/DXD|384|352\.8/i.test(fmt)) return 500
  if (/192|176\.4/i.test(fmt)) return 400
  if (/96|88\.2/i.test(fmt)) return 300
  if (/\b48\b/i.test(fmt)) return 250
  if (/24[\/-]?bit|24\/44/i.test(fmt)) return 200
  if (/16[\/-]?44|FLAC|WAV|ALAC|VINYL/i.test(fmt)) return 100
  if (/320|MP3|AAC/i.test(fmt)) return 50
  return 10
}

// ── Repertoire Section ────────────────────────────────────────────────────────
function RecentlyAddedSection({ onNavigateToTracks, onPlayAlbum, onSelectAlbum, albums, chips }: { onNavigateToTracks: () => void; onPlayAlbum?: (album: HomeAlbum) => void; onSelectAlbum?: (album: HomeAlbum) => void; albums: HomeAlbum[]; chips: string[] }) {
  const [activeChip, setActiveChip] = useState(chips[0] ?? "All")
  const [sortKey, setSortKey] = useState<RepertoireSort>("rotation")
  const [menuOpen, setMenuOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!menuOpen) return
    const handler = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false)
    }
    document.addEventListener("mousedown", handler)
    return () => document.removeEventListener("mousedown", handler)
  }, [menuOpen])

  const filtered = albums.filter((a) => {
    if (activeChip === "Hi-Res 24-Bit+") return /24.?bit|32.?bit|DXD|DSD/i.test(a.format)
    if (activeChip === "DSD Pure") return /DSD\s*\d+|DSF|DFF/i.test(a.format)
    return true
  })

  const sorted = [...filtered].sort((a, b) => {
    if (sortKey === "rotation")    return b.plays - a.plays
    if (sortKey === "fidelity")    return fidelityRank(b.format) - fidelityRank(a.format)
    if (sortKey === "acquisition") return b.added.localeCompare(a.added)
    return 0
  })

  const activeSortLabel = REPERTOIRE_SORT_OPTIONS.find((o) => o.key === sortKey)?.label ?? ""

  return (
    <section>
      <div className="flex items-center justify-between mb-5">
        <h2 style={{ fontSize: 18, fontWeight: 700, color: "var(--text-primary)" }}>Repertoire</h2>
        <div className="relative" ref={menuRef}>
          <button
            type="button"
            onClick={() => setMenuOpen((v) => !v)}
            title={activeSortLabel}
            style={{ fontSize: 18, color: "var(--text-muted)", background: "none", border: "none", cursor: "pointer", letterSpacing: 2, padding: "4px 8px", borderRadius: 8, transition: "color 0.15s, background 0.15s" }}
            onMouseEnter={(e) => { (e.currentTarget as HTMLButtonElement).style.color = "var(--text-primary)"; (e.currentTarget as HTMLButtonElement).style.background = "var(--surface-elevated)" }}
            onMouseLeave={(e) => { (e.currentTarget as HTMLButtonElement).style.color = "var(--text-muted)"; (e.currentTarget as HTMLButtonElement).style.background = "none" }}
          >
            •••
          </button>
          {menuOpen && (
            <div className="absolute right-0 top-full mt-2 w-48 text-xs font-mono py-1.5 rounded-xl shadow-2xl z-50" style={{ background: "var(--home-sort-menu-bg)", border: "1px solid var(--home-sort-menu-border)" }}>
              {REPERTOIRE_SORT_OPTIONS.map((opt) => (
                <button
                  key={opt.key}
                  onClick={() => { setSortKey(opt.key); setMenuOpen(false) }}
                  className="w-full text-left px-3.5 py-2 transition-colors"
                  style={{ background: "none", border: "none", cursor: "pointer", fontFamily: "inherit", fontWeight: sortKey === opt.key ? 700 : 400, color: sortKey === opt.key ? "var(--home-sort-menu-active)" : "var(--home-sort-menu-text)", display: "flex", alignItems: "center", gap: 8 }}
                  onMouseEnter={(e) => (e.currentTarget.style.background = "var(--home-sort-menu-hover-bg)")}
                  onMouseLeave={(e) => (e.currentTarget.style.background = "none")}
                >
                  <span style={{ width: 6, height: 6, borderRadius: "50%", background: sortKey === opt.key ? "var(--accent-violet)" : "transparent", border: sortKey === opt.key ? "none" : "1px solid var(--text-muted)", flexShrink: 0, display: "inline-block" }} />
                  {opt.label}
                </button>
              ))}
            </div>
          )}
        </div>
      </div>

      <div className="flex items-center gap-2 mb-6 flex-wrap">
        {chips.map((chip) => {
          const isActive = activeChip === chip
          return (
            <button
              key={chip}
              onClick={() => setActiveChip(chip)}
              className="text-xs font-medium rounded-full px-3.5 py-1 border transition-all whitespace-nowrap cursor-pointer"
              style={isActive ? {
                background: "var(--home-filter-chip-active-bg)",
                color: "var(--home-filter-chip-active-text)",
                border: "1px solid var(--home-filter-chip-active-border)",
                fontWeight: 600,
                boxShadow: "var(--home-filter-chip-active-shadow, 0 1px 4px rgba(0,0,0,0.15))",
                fontFamily: "inherit",
              } : {
                background: "var(--home-filter-chip-bg)",
                color: "var(--home-filter-chip-text)",
                border: "1px solid var(--home-filter-chip-border)",
                fontFamily: "inherit",
              }}
            >
              {chip}
            </button>
          )
        })}
      </div>

      {sorted.length === 0 ? (
        <div style={{ padding: "48px 0", textAlign: "center", color: "#9CA3AF", fontSize: 13, fontStyle: "italic" }}>No albums found in this repertoire filter.</div>
      ) : (
        <div className="grid grid-cols-5 gap-6 w-full">
          {sorted.map((album, i) => (
            <AlbumCard key={i} album={album} onPlay={onPlayAlbum} onSelect={onSelectAlbum} />
          ))}
        </div>
      )}
    </section>
  )
}

// ── Listening Insights Section ───────────────────────────────────────────────
function ListeningInsightsSection({ cards }: { cards: InsightCard[] }) {
  return (
    <section>
      <div className="flex items-start justify-between mb-5">
        <div>
          <h2 style={{ fontSize: 18, fontWeight: 700, color: "var(--text-primary)", marginBottom: 4 }}>Listening Insights</h2>
          <p style={{ fontSize: 13, color: "var(--text-secondary)" }}>Weekly audiophile fidelity &amp; hardware telemetry</p>
        </div>
      </div>
      <div className="grid grid-cols-4 gap-5">
        {cards.map((card) => (
          <div key={card.title} style={{ background: "var(--surface-card)", border: "1px solid var(--border-subtle)", borderRadius: 14, padding: 20, boxShadow: "0 1px 8px rgba(0,0,0,0.12)", display: "flex", flexDirection: "column", gap: 6 }}>
            <div className="flex items-center justify-between">
              <span style={{ fontSize: 12, fontWeight: 500, color: "var(--text-secondary)" }}>{card.title}</span>
              {card.dot ? (
                <span style={{ width: 8, height: 8, borderRadius: "50%", background: card.dot, display: "inline-block", boxShadow: `0 0 0 3px ${card.dot}22` }} />
              ) : card.icon === "trend-up" ? (
                <MonoIcon.TrendUp size={16} color="#059669" />
              ) : card.icon === "wave" ? (
                <MonoIcon.Wave size={16} color="var(--text-secondary)" />
              ) : card.icon === "tag" ? (
                <MonoIcon.Tag size={16} color="var(--text-secondary)" />
              ) : (
                <span style={{ fontSize: 14, lineHeight: 1 }}>{card.icon}</span>
              )}
            </div>
            <div style={{ fontFamily: "'DM Mono', monospace", fontSize: card.value.length > 8 ? 20 : 26, fontWeight: 700, color: card.valueColor, lineHeight: 1.1, letterSpacing: "-0.5px" }}>{card.value}</div>
            <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10.5, color: "var(--text-muted)", lineHeight: 1.4 }}>{card.sub}</div>
          </div>
        ))}
      </div>
    </section>
  )
}

// ── Label Archives Section ───────────────────────────────────────────────────
const LABEL_BADGE_CLS: Record<string, string> = {
  "ECM Records":         "bg-sky-500/10 text-sky-600 dark:text-sky-400 border border-sky-500/20",
  "Blue Note Records":   "bg-amber-500/10 text-amber-600 dark:text-amber-400 border border-amber-500/20",
  "Deutsche Grammophon": "bg-yellow-500/10 text-yellow-600 dark:text-yellow-400 border border-yellow-500/20",
}

function LabelBanner({ label }: { label: LabelTile }) {
  const badgeCls = LABEL_BADGE_CLS[label.name] ?? "bg-zinc-500/10 text-zinc-400 border border-zinc-500/20"
  return (
    <div
      className="group"
      style={{
        background: "var(--surface-card)",
        border: "1px solid var(--border-subtle)",
        borderRadius: 16, padding: "20px 24px",
        display: "flex", alignItems: "center", justifyContent: "space-between", gap: 16,
        cursor: "pointer", transition: "border-color 0.2s, box-shadow 0.2s, transform 0.15s",
        minHeight: 90,
      }}
      onMouseEnter={(e) => {
        ;(e.currentTarget as HTMLDivElement).style.borderColor = "var(--accent-violet)"
        ;(e.currentTarget as HTMLDivElement).style.boxShadow = "0 4px 20px var(--accent-glow)"
        ;(e.currentTarget as HTMLDivElement).style.transform = "translateY(-1px)"
      }}
      onMouseLeave={(e) => {
        ;(e.currentTarget as HTMLDivElement).style.borderColor = "var(--border-subtle)"
        ;(e.currentTarget as HTMLDivElement).style.boxShadow = "none"
        ;(e.currentTarget as HTMLDivElement).style.transform = "translateY(0)"
      }}
    >
      <div style={{ minWidth: 0 }}>
        <div style={{ fontSize: 16, fontWeight: 600, color: "var(--text-primary)", marginBottom: 4, letterSpacing: "-0.2px" }}>{label.name}</div>
        <div style={{ fontSize: 12, color: "var(--text-secondary)", overflow: "hidden", textOverflow: "ellipsis", maxWidth: 320, display: "-webkit-box", WebkitLineClamp: 2, WebkitBoxOrient: "vertical" }}>{label.subtitle}</div>
      </div>
      <div className="flex items-center gap-3" style={{ flexShrink: 0 }}>
        <span className={`${badgeCls} text-xs font-mono px-2.5 py-1 rounded-full`} style={{ fontFamily: "'DM Mono', monospace", fontWeight: 500 }}>{label.count}</span>
        <span style={{ fontSize: 16, color: "var(--text-muted)", fontWeight: 300, transition: "color 0.2s" }}>→</span>
      </div>
    </div>
  )
}

function LabelArchivesSection({ labels }: { labels: LabelTile[] }) {
  return (
    <section>
      <div className="flex items-center justify-between mb-5">
        <h2 style={{ fontSize: 18, fontWeight: 700, color: "var(--text-primary)" }}>Label Archives</h2>
      </div>
      <div className="grid grid-cols-3 gap-6 w-full">
        {labels.map((label, i) => (
          <LabelBanner key={i} label={label} />
        ))}
        {labels.length === 0 && (
          <div style={{ gridColumn: "span 3", fontFamily: "'DM Mono', monospace", fontSize: 12, color: "var(--text-muted)", padding: "24px 4px" }}>
            레이블 태그가 있는 음원이 아직 없습니다.
          </div>
        )}
      </div>
    </section>
  )
}

// ── Home Page ────────────────────────────────────────────────────────────────
export default function HomePage({
  joinedLoungeId, onToggleJoin, onLeaveLounge, onNavigateToTracks, onPlayAlbum, onSelectAlbum,
  displayName, loungeCards, archiveCount, albums, chips, insights, labels,
}: {
  joinedLoungeId: string | null
  onToggleJoin: (id: string, e: React.MouseEvent) => void
  onLeaveLounge: (e: React.MouseEvent) => void
  onNavigateToTracks: () => void
  onPlayAlbum?: (album: HomeAlbum) => void
  onSelectAlbum?: (album: HomeAlbum) => void
  displayName: string
  loungeCards: LoungeCardData[]
  archiveCount: number
  albums: HomeAlbum[]
  chips: string[]
  insights: InsightCard[]
  labels: LabelTile[]
}) {
  return (
    <div style={{ maxWidth: 1260, margin: "0 auto", padding: "40px 64px" }}>
      <h1 style={{ fontSize: 28, fontWeight: 700, color: "var(--text-primary)", marginBottom: 56, letterSpacing: "-0.4px" }}>Welcome, {displayName}!</h1>
      <div style={{ marginBottom: 56 }}>
        <SocialLoungesSection joinedLoungeId={joinedLoungeId} onToggleJoin={onToggleJoin} onLeaveLounge={onLeaveLounge} cards={loungeCards} archiveCount={archiveCount} />
      </div>
      <div style={{ marginBottom: 56 }}>
        <RecentlyAddedSection onNavigateToTracks={onNavigateToTracks} onPlayAlbum={onPlayAlbum} onSelectAlbum={onSelectAlbum} albums={albums} chips={chips} />
      </div>
      <div style={{ marginBottom: 56 }}>
        <ListeningInsightsSection cards={insights} />
      </div>
      <LabelArchivesSection labels={labels} />
      <div style={{ height: 32 }} />
    </div>
  )
}
