import { useState, useEffect } from "react"
import { type QueueTrack } from "../data/types"
import { useMono, useMediaClock } from "../state/MonoProvider"
import { useLiveSession } from "../state/useLiveSession"
import { MonoIcon } from "./icons/MonoIcons"

export interface FullscreenPlayerProps {
  isOpen: boolean
  onClose: () => void
  currentTrack?: any
  isPlaying?: boolean
  onTogglePlay?: () => void
  currentTime?: number
  duration?: number
  onSeek?: (time: number) => void
  onOpenArtist: () => void
  onOpenAlbum: () => void
  onNavigateToLensSettings: () => void
  onSelectTrack?: (track: QueueTrack) => void
  /** When set, the standby console ✕ routes back to the active lounge instead of just closing */
  onReturnToLounge?: () => void
  isInLoungeSession?: boolean
}

export default function FullscreenPlayer({
  isOpen,
  onClose,
  currentTrack,
  onOpenArtist,
  onOpenAlbum,
  onNavigateToLensSettings,
  onSelectTrack,
  onReturnToLounge,
  isInLoungeSession,
}: FullscreenPlayerProps) {
  const { room } = useMono()
  const { positionMs } = useMediaClock()
  const { catalog } = useLiveSession()
  const lyrics = room?.lyrics ?? []

  /**
   * 크레딧은 "역할: 이름" 줄들이 줄바꿈이나 세미콜론으로 이어진 자유 텍스트다.
   * 형식이 아니면 통째로 한 줄로 보여 준다 — 억지로 쪼개면 더 읽기 어렵다.
   */
  const credits = (room?.credits ?? room?.album?.credits ?? "")
    .split(/[\r\n;]+/)
    .map((raw) => raw.trim())
    .filter(Boolean)
    .map((raw, i) => {
      const at = raw.indexOf(":")
      const role = at > 0 ? raw.slice(0, at).trim() : "Credit"
      const name = at > 0 ? raw.slice(at + 1).trim() : raw
      return { role, name, clickable: i === 0 && !!room?.artist }
    })

  const albumLine = [
    room?.album?.title,
    room?.album?.label,
    room?.album?.year ? String(room.album.year) : null,
  ].filter(Boolean).join(" · ")

  // 대기 화면의 빠른 선택은 라이브러리 앞쪽에서 가져온다.
  const quickPicks = catalog.slice(0, 12).map((t) => ({
    id: t.id,
    title: t.title,
    artist: t.artist ?? "Unknown Artist",
    album: t.album ?? "",
    format: t.isDsd ? `DSD ${t.dsdRate ?? 64}` : `FLAC ${t.bitDepth}-Bit / ${Math.round(t.sampleRate / 1000)}kHz`,
    dr: t.badge ?? "",
    art: t.artUrl ?? "",
    source: (t.hasLocal ? "local" : "qobuz") as "local" | "qobuz",
  }))
  const [sideTab, setSideTab] = useState<"lyrics" | "credits">("lyrics")
  const [searchQuery, setSearchQuery] = useState("")

  useEffect(() => {
    if (!isOpen) return
    const handler = (e: KeyboardEvent) => { if (e.key === "Escape") onClose() }
    window.addEventListener("keydown", handler)
    return () => window.removeEventListener("keydown", handler)
  }, [isOpen, onClose])

  const handleClose = () => {
    if (isInLoungeSession && onReturnToLounge) onReturnToLounge()
    else onClose()
  }

  if (!isOpen) return null

  /* ── Standby view ─────────────────────────────────────────────────────── */
  if (!currentTrack) {
    const filtered = searchQuery.trim()
      ? quickPicks.filter(
          (p) =>
            p.title.toLowerCase().includes(searchQuery.toLowerCase()) ||
            p.artist.toLowerCase().includes(searchQuery.toLowerCase()) ||
            p.album.toLowerCase().includes(searchQuery.toLowerCase()) ||
            p.format.toLowerCase().includes(searchQuery.toLowerCase())
        )
      : quickPicks
    return (
      <div className="studio-console-overlay fixed inset-x-0 top-16 bottom-20 z-40 flex flex-col overflow-hidden select-none">
        {/* Header */}
        <div className="flex items-center justify-between px-10 pt-5 pb-4 shrink-0 border-b console-header-border">
          <div className="flex items-center gap-3">
            <span className="text-[10px] font-mono tracking-widest text-zinc-400 uppercase">Mono Studio Console · Standby</span>
            {isInLoungeSession && (
              <button
                onClick={handleClose}
                className="inline-flex items-center gap-1.5 text-[10px] font-mono font-semibold text-blue-600 hover:text-blue-800 bg-blue-50 hover:bg-blue-100 border border-blue-200 px-2.5 py-1 rounded-full cursor-pointer transition-colors"
                style={{ background: undefined, border: undefined }}
              >
                <MonoIcon.ArrowLeft size={11} />
                <span>Return to Live Lounge</span>
              </button>
            )}
          </div>
          <button
            onClick={handleClose}
            className="text-zinc-400 hover:text-zinc-900 dark:hover:text-zinc-100 cursor-pointer transition-colors p-1 flex items-center justify-center"
            style={{ background: "none", border: "none" }}
            aria-label="Close"
          >
            <MonoIcon.Close size={15} />
          </button>
        </div>

        {/* Main content */}
        <div className="flex-1 overflow-y-auto px-10 py-10">
          {/* Search input */}
          <div className="max-w-xl w-full mx-auto mt-4 mb-10">
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Search repertoire, master recordings, artists, or formats (FLAC, DSD)..."
              className="console-search-input w-full rounded-2xl px-5 py-4 text-sm font-sans transition-all"
            />
          </div>

          {/* Quick picks */}
          <div className="max-w-3xl w-full mx-auto">
            <div className="text-[10px] font-mono tracking-widest uppercase text-zinc-400 mb-6 text-center">
              Master Repertoire Shortcuts
            </div>

            {filtered.length === 0 ? (
              <div className="text-center text-sm text-zinc-400 font-mono py-12">No matches for &ldquo;{searchQuery}&rdquo;</div>
            ) : (
              <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-4">
                {filtered.map((pick, i) => (
                  <button
                    key={i}
                    onClick={() => {
                      onSelectTrack?.({
                        id: `standby-pick-${i}`,
                        title: pick.title,
                        artist: pick.artist,
                        album: pick.album,
                        duration: "5:00",
                        format: pick.format,
                        dr: pick.dr,
                        art: pick.art,
                        source: pick.source,
                      })
                      // Do NOT call onClose() — the Full Player must stay mounted.
                      // Setting currentTrack via onSelectTrack causes the parent to
                      // update props, moving this component out of the standby branch
                      // and into the Now Playing view automatically.
                    }}
                    className="console-shortcut-card group flex flex-col items-start gap-3 rounded-2xl p-4 text-left transition-all cursor-pointer"
                    style={{}}
                  >
                    <div className="w-full aspect-square rounded-xl overflow-hidden bg-zinc-100 shrink-0">
                      <img
                        src={pick.art}
                        alt={pick.album}
                        className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                      />
                    </div>
                    <div className="min-w-0 w-full">
                      <div className="text-xs font-semibold text-zinc-900 truncate leading-tight">{pick.title}</div>
                      <div className="text-[11px] text-zinc-500 truncate leading-tight mt-0.5">{pick.artist}</div>
                      <div className="text-[10px] font-mono text-zinc-400 truncate mt-1.5">{pick.format.split(" ")[0]}</div>
                    </div>
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* Telemetry footer */}
        <div className="shrink-0 console-header-border border-t py-4 px-10">
          <div className="console-telemetry text-[11px] font-mono text-center">
            Direct Bitstream Engine Active · Holo Audio May DAC (Ready) · Buffer: 64 samples · ASIO/CoreAudio Lock
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="fixed inset-x-0 top-16 bottom-24 z-30 flex flex-col overflow-hidden select-none" style={{ background: "var(--chassis-bg)", color: "var(--text-primary)", transition: "background 0.2s ease", borderTop: "1px solid var(--border-subtle)" }}>

      {/* 2-column stage */}
        <div className="nowplaying-stage-bg flex-1 flex items-center justify-center overflow-hidden px-10 py-4 relative">
        {/* ✕ dismiss — floats top-right inside the stage */}
        <button
          onClick={onClose}
          className="absolute top-4 right-8 text-slate-400 hover:text-slate-900 dark:hover:text-slate-100 cursor-pointer transition z-10 p-1 flex items-center justify-center"
          style={{ background: "none", border: "none" }}
          title="Close Now Playing"
          aria-label="Close Now Playing"
        >
          <MonoIcon.Close size={18} />
        </button>

        <div className="max-w-5xl mx-auto w-full gap-12 lg:gap-16 flex items-start justify-center">

          {/* Left column — album art */}
          <div className="flex flex-col items-center shrink-0 relative">
            {/* Ambient glow */}
            <img
              src={currentTrack.art || currentTrack.coverUrl || ""}
              aria-hidden="true"
              className="absolute -inset-8 w-full h-full object-cover rounded-3xl pointer-events-none"
              style={{ zIndex: 0, opacity: 0.25, filter: "blur(48px)" }}
            />
            {/* Cover art */}
            <div
              onClick={() => { onOpenAlbum(); onClose(); }}
              title="Go to Album"
              aria-label="Go to Album"
              className="relative w-[320px] h-[320px] lg:w-[380px] lg:h-[380px] rounded-3xl overflow-hidden shadow-2xl cursor-pointer transition-all duration-300 hover:scale-[1.01] hover:brightness-105 active:scale-[0.98]"
              style={{ zIndex: 1, border: "1px solid var(--nowplaying-art-border)" }}
            >
              <img src={currentTrack.art || currentTrack.coverUrl || ""} alt={currentTrack.title} className="object-cover w-full h-full" />
            </div>
            {/* Track info */}
            <div className="mt-5 text-center" style={{ zIndex: 1, position: "relative" }}>
              <div className="text-3xl font-serif font-bold mb-1" style={{ color: "var(--nowplaying-title-text)" }}>{currentTrack.title}</div>
              <div className="text-base mt-2 mb-3" style={{ color: "var(--nowplaying-artist-text)" }}>
                <button
                  onClick={() => { onClose(); onOpenArtist() }}
                  className="cursor-pointer hover:underline transition-colors font-semibold"
                  style={{ background: "none", border: "none", padding: 0, fontFamily: "inherit", fontSize: "inherit", color: "inherit" }}
                >
                  {currentTrack.artist}
                </button>
                {currentTrack.album && (
                  <>
                    {" · "}
                    <button
                      onClick={onOpenAlbum}
                      title="View Album Master Tracklist & Specs"
                      className="cursor-pointer hover:underline transition font-semibold"
                      style={{ background: "none", border: "none", padding: 0, fontFamily: "inherit", fontSize: "inherit", color: "inherit" }}
                    >
                      {currentTrack.album}
                    </button>
                  </>
                )}
              </div>
              <div className="flex items-center justify-center gap-2">
                {currentTrack.format && (
                  <span className="text-[11px] font-mono px-2.5 py-0.5 rounded-full"
                    style={{ background: "var(--nowplaying-meta-pill-bg)", border: "1px solid var(--nowplaying-meta-pill-border)", color: "var(--nowplaying-meta-pill-text)" }}>
                    {currentTrack.format}
                  </span>
                )}
                {currentTrack.dr && (
                  <span className="text-[11px] font-mono px-2.5 py-0.5 rounded-full"
                    style={{ background: "var(--nowplaying-meta-pill-bg)", border: "1px solid var(--nowplaying-meta-pill-border)", color: "var(--nowplaying-meta-pill-text)" }}>
                    {currentTrack.dr}
                  </span>
                )}
              </div>
              <div className="flex items-center justify-center gap-2 mt-3">
                <button
                  type="button"
                  onClick={(e) => { e.stopPropagation(); onNavigateToLensSettings(); }}
                  className="text-xs font-mono px-3 py-1 rounded-full cursor-pointer transition inline-flex items-center gap-1.5"
                  style={{ background: "none", border: "1px solid var(--nowplaying-tab-idle-border)", color: "var(--nowplaying-meta-pill-text)" }}
                >
                  <MonoIcon.Sparkle size={12} />
                  <span>LENS DSP</span>
                </button>
              </div>
            </div>
          </div>

          {/* Right column — glass card */}
          <div className="nowplaying-panel-surface w-full max-w-[480px] h-[488px] lg:h-[548px] rounded-3xl flex flex-col overflow-hidden">
            {/* Tab header */}
            <div className="flex items-center gap-2 px-7 py-4 shrink-0" style={{ borderBottom: "1px solid var(--nowplaying-tab-divider)" }}>
              <button
                onClick={() => setSideTab("lyrics")}
                className="text-xs font-mono px-3.5 py-1.5 rounded-full cursor-pointer transition inline-flex items-center gap-1.5"
                style={sideTab !== "credits"
                  ? { background: "var(--nowplaying-tab-active-bg)", border: "1px solid var(--nowplaying-tab-active-border)", color: "var(--nowplaying-tab-active-text)" }
                  : { background: "none", border: "1px solid var(--nowplaying-tab-idle-border)", color: "var(--nowplaying-tab-idle-text)" }}
              >
                <MonoIcon.Lyrics size={13} />
                <span>Synced Lyrics</span>
              </button>
              <button
                onClick={() => setSideTab("credits")}
                className="text-xs font-mono px-3.5 py-1.5 rounded-full cursor-pointer transition inline-flex items-center gap-1.5"
                style={sideTab === "credits"
                  ? { background: "var(--nowplaying-tab-active-bg)", border: "1px solid var(--nowplaying-tab-active-border)", color: "var(--nowplaying-tab-active-text)" }
                  : { background: "none", border: "1px solid var(--nowplaying-tab-idle-border)", color: "var(--nowplaying-tab-idle-text)" }}
              >
                <MonoIcon.Clipboard size={12} />
                <span>Track Credits</span>
              </button>
              {sideTab !== "credits" && (
                <span className="ml-auto text-[10px] font-mono" style={{ color: "var(--nowplaying-credits-role)" }}>{lyrics.length > 0 ? "Synced from .lrc" : "No synced lyrics"}</span>
              )}
            </div>

            {/* Panel content */}
            {sideTab !== "credits" ? (
              /* Lyrics panel */
              <div className="flex-1 overflow-y-auto p-8 space-y-7" style={{ scrollBehavior: "smooth" }}>
                {lyrics.length > 0 ? (
                  lyrics.map((line, i) => {
                    // 현재 줄은 "지난 줄 중 가장 마지막". Core 가 .lrc 타임스탬프를 그대로 준다.
                    const nextTime = lyrics[i + 1]?.timeMs ?? Number.POSITIVE_INFINITY
                    const isActive = line.timeMs <= positionMs && positionMs < nextTime
                    const isPast = nextTime <= positionMs
                    return (
                      <div
                        key={`${line.timeMs}-${i}`}
                        className="font-serif cursor-pointer transition-colors"
                        style={isActive
                          ? { fontSize: "1.75rem", fontWeight: 700, color: "var(--nowplaying-title-text)", transform: "scale(1.02)", transformOrigin: "left" }
                          : isPast
                          ? { fontSize: "1.125rem", color: "var(--nowplaying-credits-role)" }
                          : { fontSize: "1.125rem", color: "var(--nowplaying-tab-idle-text)" }}
                      >
                        {line.text}
                      </div>
                    )
                  })
                ) : (
                  <div className="flex flex-col items-center justify-center h-48 text-center space-y-2">
                    <span className="text-xs font-mono uppercase tracking-widest inline-flex items-center gap-1.5" style={{ color: "var(--nowplaying-credits-role)" }}>
                      <MonoIcon.MasterRecording size={13} />
                      <span>Studio Master Recording</span>
                    </span>
                    <p className="text-sm font-serif italic" style={{ color: "var(--text-secondary)" }}>Instrumental performance · No vocal lyrics for this track</p>
                    <span className="text-[10px] font-mono" style={{ color: "var(--nowplaying-credits-role)" }}>{currentTrack?.format || "Hi-Res Audio"} · Bit-Perfect Stream</span>
                  </div>
                )}
              </div>
            ) : (
              /* Credits panel */
              <div className="flex-1 overflow-y-auto px-7 py-6 space-y-5">
                {credits.map((p) => (
                  <div key={p.role} className="flex items-baseline justify-between pb-4" style={{ borderBottom: "1px solid var(--nowplaying-credits-divider)" }}>
                    <span className="text-xs font-mono uppercase tracking-wide" style={{ color: "var(--nowplaying-credits-role)" }}>{p.role}</span>
                    {p.clickable ? (
                      <button
                        onClick={() => { onClose(); onOpenArtist() }}
                        className="text-sm font-semibold hover:underline cursor-pointer transition"
                        style={{ background: "none", border: "none", fontFamily: "inherit", padding: 0, color: "var(--nowplaying-credits-link)" }}
                      >
                        {p.name}
                      </button>
                    ) : (
                      <span className="text-sm font-semibold" style={{ color: "var(--nowplaying-credits-name)" }}>{p.name}</span>
                    )}
                  </div>
                ))}
                {credits.length === 0 && (
                  <p className="text-[11px] font-mono leading-relaxed" style={{ color: "var(--nowplaying-credits-role)" }}>
                    이 앨범에는 크레딧 정보가 없습니다.
                  </p>
                )}
                {albumLine && (
                  <p className="text-[11px] font-mono leading-relaxed pt-2" style={{ color: "var(--nowplaying-credits-role)" }}>{albumLine}</p>
                )}
              </div>
            )}
          </div>

        </div>
      </div>
    </div>
  )
}


