import { useMemo, useState } from "react"
import { useMono, useMonoCommands } from "../state/MonoProvider"
import { useLiveSession } from "../state/useLiveSession"
import { libAlbums, libArtists, libComposers, libPlaylists, libTracks, type LibTrack } from "../lib/libraryData"
import TrackActionMenu, { type TrackActionTarget } from "./TrackActionMenu"
import type { ShareData } from "./ShareModal"
import { MonoIcon } from "./icons/MonoIcons"

function getAudioFidelityRank(formatStr: string = ""): number {
  if (!formatStr) return 0
  const s = formatStr

  if (/DSD\s*512|22\.[56]MHZ/i.test(s)) return 900
  if (/DSD\s*256|11\.2MHZ/i.test(s)) return 800
  if (/DSD\s*128|5\.6MHZ/i.test(s)) return 700
  if (/DSD\s*64|2\.8MHZ|DSF|DFF|\bDSD\b/i.test(s)) return 600

  if (/DXD|384|352\.8/i.test(s)) return 500

  if (/192|176\.4/i.test(s)) return 400
  if (/96|88\.2/i.test(s)) return 300
  if (/\b48\b/i.test(s)) return 250
  if (/24[\/-]?BIT|24\/44\.1/i.test(s)) return 200

  if (/16[\/-]?44\.1|16[\/-]?BIT|VINYL|FLAC|WAV|ALAC/i.test(s)) return 100

  if (/320|MP3|AAC/i.test(s)) return 50

  return 10
}

type PlaylistEntry = {
  id: string
  name: string
  description: string
  trackCount: number
  duration: string
  art: string
  arts?: string[]
  tracks: unknown[]
  dr?: string
  spec?: string
}

interface LibraryPageProps {
  onPlayNow?: (track: TrackActionTarget) => void
  onPlayNext?: (track: TrackActionTarget) => void
  onAddToQueue?: (track: TrackActionTarget) => void
  defaultTab?: "albums" | "artists" | "composers" | "playlists" | "tracks"
  onOpenShare?: (data: ShareData) => void
  onSelectAlbum?: (album: any) => void
  onSelectArtist?: (artist: any) => void
}

const SHARE_ICON = (
  <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
    <circle cx="18" cy="5" r="3"/><circle cx="6" cy="12" r="3"/><circle cx="18" cy="19" r="3"/>
    <line x1="8.59" y1="13.51" x2="15.42" y2="17.49"/><line x1="15.41" y1="6.51" x2="8.59" y2="10.49"/>
  </svg>
)

export default function MyLibraryPage({ onPlayNow, onPlayNext, onAddToQueue, defaultTab = "albums", onOpenShare, onSelectAlbum, onSelectArtist }: LibraryPageProps = {}) {
  const [libraryTab, setLibraryTab] = useState<"albums" | "artists" | "composers" | "playlists" | "tracks">(defaultTab)
  const { catalog, playlists: corePlaylists, catalogLoaded } = useMono()
  const cmd = useMonoCommands()
  const { albums: liveAlbums } = useLiveSession()

  const LIB_ALBUMS = useMemo(() => libAlbums(liveAlbums, catalog), [liveAlbums, catalog])
  const LIB_ARTISTS = useMemo(() => libArtists(catalog), [catalog])
  const LIB_COMPOSERS = useMemo(() => libComposers(catalog), [catalog])
  const LIB_TRACKS = useMemo(() => libTracks(catalog), [catalog])

  // 플레이리스트는 Core 가 소유한다. 새로 만들면 Core 가 목록을 다시 밀어 준다.
  const playlists: PlaylistEntry[] = useMemo(
    () => libPlaylists(corePlaylists, catalog) as unknown as PlaylistEntry[],
    [corePlaylists, catalog],
  )
  const [isNewPlaylistModalOpen, setIsNewPlaylistModalOpen] = useState(false)
  const [newTitle, setNewTitle] = useState("")
  const [newDesc, setNewDesc] = useState("")
  const [formatFilter, setFormatFilter] = useState<"all" | "hires" | "dsd" | "vinyl">("all")
  const [sortField, setSortField] = useState<"album" | "title" | "artist" | "format" | "dr">("album")
  const [sortOrder, setSortOrder] = useState<"asc" | "desc">("asc")
  const [hoveredRow, setHoveredRow] = useState<number | null>(null)

  const CATEGORY_TABS: Array<{ id: typeof libraryTab; label: string }> = [
    { id: "albums", label: "Albums" },
    { id: "artists", label: "Artists" },
    { id: "composers", label: "Composers" },
    { id: "playlists", label: "Playlists" },
    { id: "tracks", label: "Tracks" },
  ]

  const FORMAT_PILLS: Array<{ id: typeof formatFilter; label: string }> = [
    { id: "all", label: "All" },
    { id: "hires", label: "Hi-Res 24-Bit" },
    { id: "dsd", label: "DSD / SACD" },
    { id: "vinyl", label: "Vinyl Rips" },
  ]

  const filteredAlbums = formatFilter === "all" ? LIB_ALBUMS : LIB_ALBUMS.filter((a) => a.format === formatFilter)

  const handleHeaderClick = (field: "album" | "title" | "artist" | "format" | "dr") => {
    if (field === sortField) {
      setSortOrder(sortOrder === "asc" ? "desc" : "asc")
    } else {
      setSortField(field)
      setSortOrder(field === "format" ? "desc" : "asc")
    }
  }

  const sortArrow = (field: "album" | "title" | "artist" | "format" | "dr") =>
    sortField === field ? (sortOrder === "asc" ? " ↑" : " ↓") : ""

  const formatHeaderTooltip = sortField === "format"
    ? (sortOrder === "desc" ? "Highest Quality First (DSD / 24-Bit)" : "Standard Quality First (16-Bit / CD)")
    : "Sort by audio fidelity"

  const TRACK_COL = "32px 1fr 1fr 1fr 120px 56px 52px 28px"

  const sortedTracks = [...LIB_TRACKS].sort((a, b) => {
    if (sortField === "format") {
      const rankA = getAudioFidelityRank(a.format ?? "")
      const rankB = getAudioFidelityRank(b.format ?? "")
      if (rankA !== rankB) return sortOrder === "desc" ? rankB - rankA : rankA - rankB
      const ac = (a.artist ?? "").localeCompare(b.artist ?? "")
      if (ac !== 0) return ac
      return (a.title ?? "").localeCompare(b.title ?? "")
    }
    let cmp = 0
    if (sortField === "title")       cmp = a.title.localeCompare(b.title)
    else if (sortField === "artist") cmp = a.artist.localeCompare(b.artist) || a.title.localeCompare(b.title)
    else if (sortField === "dr")     cmp = b.dr - a.dr
    return sortOrder === "desc" && sortField !== "dr" ? -cmp : cmp
  })

  const albumGroups: Array<{ album: string; artist: string; year: number; format: string; dr: number; art: string; tracks: LibTrack[] }> = []
  for (const t of LIB_TRACKS) {
    const g = albumGroups.find((g) => g.album === t.album)
    if (g) g.tracks.push(t)
    else albumGroups.push({ album: t.album, artist: t.artist, year: t.year, format: t.format, dr: t.dr, art: t.art, tracks: [t] })
  }

  const TrackRow = ({ t, idx }: { t: LibTrack; idx: number }) => {
    const hovered = hoveredRow === idx
    const trackPayload: TrackActionTarget = { id: t.id, title: t.title, artist: t.artist, album: t.album, duration: t.time, format: t.format, dr: String(t.dr), art: t.art }
    const playTrack = (e: React.MouseEvent) => {
      e.stopPropagation()
      onPlayNow?.(trackPayload)
    }
    return (
      <div
        className="group"
        onMouseEnter={() => setHoveredRow(idx)}
        onMouseLeave={() => setHoveredRow(null)}
        onClick={playTrack}
        style={{
          display: "grid", gridTemplateColumns: TRACK_COL, alignItems: "center",
          padding: "6px 10px", borderRadius: 8,
          background: hovered ? "var(--lib-track-hover-bg)" : "transparent",
          transition: "background 0.1s", cursor: "pointer",
        }}
      >
        <div className="library-track-num">
          {hovered ? <span className="library-track-play flex items-center justify-center"><MonoIcon.PlayMini size={10} /></span> : idx + 1}
        </div>
        {/* Title — clicking navigates to album; row click plays */}
        <div
          className="library-track-title hover:underline cursor-pointer"
          style={{ fontWeight: sortField === "title" ? 500 : 400 }}
          onClick={(e) => { e.stopPropagation(); onSelectAlbum?.({ title: t.album, artist: t.artist, art: t.art, coverUrl: t.art }) }}
          title={`Go to album: ${t.album}`}
        >
          {t.title}
        </div>
        {/* Artist — clicking navigates to artist view */}
        <div
          className="library-track-artist hover:underline cursor-pointer"
          style={{ fontWeight: sortField === "artist" ? 500 : 400 }}
          onClick={(e) => { e.stopPropagation(); onSelectArtist?.({ name: t.artist }) }}
          title={`Go to artist: ${t.artist}`}
        >
          {t.artist}
        </div>
        {/* Album — clicking navigates to album view */}
        <div
          className="library-track-album hover:underline cursor-pointer"
          onClick={(e) => { e.stopPropagation(); onSelectAlbum?.({ title: t.album, artist: t.artist, art: t.art, coverUrl: t.art }) }}
          title={`Go to album: ${t.album}`}
        >
          {t.album}
        </div>
        <div>
          <span style={{
            fontFamily: "'DM Mono', monospace", fontSize: 11,
            color: "var(--lib-track-artist)",
            background: sortField === "format" ? "var(--lib-format-badge-bg)" : "transparent",
            border: sortField === "format" ? "1px solid var(--lib-format-badge-border)" : "none",
            padding: sortField === "format" ? "2px 6px" : "0",
            borderRadius: 4,
          }}>
            {t.format}
          </span>
        </div>
        <div style={{ textAlign: "center" }}>
          <span style={{
            fontFamily: "'DM Mono', monospace", fontSize: 12,
            fontWeight: sortField === "dr" ? 700 : 400,
            color: sortField === "dr" ? "var(--lib-dr-badge-text)" : "var(--lib-track-album)",
            background: sortField === "dr" ? "var(--lib-dr-badge-bg)" : "transparent",
            padding: sortField === "dr" ? "2px 5px" : "0",
            borderRadius: 4,
          }}>
            DR{t.dr}
          </span>
        </div>
        <div className="library-track-time">
          {t.time}
        </div>
        <div
          style={{ display: "flex", alignItems: "center", justifyContent: "center" }}
          onClick={(e) => e.stopPropagation()}
        >
          {(onPlayNow || onPlayNext || onAddToQueue) && (
            <TrackActionMenu
              track={trackPayload}
              onPlayNow={onPlayNow ?? (() => {})}
              onPlayNext={onPlayNext ?? (() => {})}
              onAddToQueue={onAddToQueue ?? (() => {})}
            />
          )}
        </div>
      </div>
    )
  }

  return (
    <div style={{ maxWidth: 1260, margin: "0 auto", padding: "40px 64px" }}>
      {/* Header row */}
      <div className="flex items-start justify-between mb-6">
        <h1 className="library-title">
          My Library
        </h1>
        <span className="library-stat-label">
          {LIB_ALBUMS.length.toLocaleString()} Albums · {LIB_TRACKS.length.toLocaleString()} Tracks
          <span className="library-stat-dot" />
        </span>
      </div>

      {/* Category tabs */}
      <div style={{ borderBottom: "1px solid var(--lib-tab-divider)", marginBottom: 20 }}>
        <div className="flex items-center" style={{ gap: 0 }}>
          {CATEGORY_TABS.map((tab) => {
            const isActive = libraryTab === tab.id
            return (
              <button
                key={tab.id}
                onClick={() => setLibraryTab(tab.id)}
                className={["library-tab-btn", isActive ? "library-tab-btn-active" : ""].join(" ")}
              >
                {tab.label}
              </button>
            )
          })}
        </div>
      </div>

      {/* Format pills — show only for albums */}
      {libraryTab === "albums" && (
        <div className="flex items-center gap-2 mb-8 flex-wrap">
          {FORMAT_PILLS.map((pill) => {
            const isActive = formatFilter === pill.id
            return (
              <button
                key={pill.id}
                onClick={() => setFormatFilter(pill.id)}
                className={["library-format-pill", isActive ? "library-format-pill-active" : ""].join(" ")}
              >
                {pill.label}
              </button>
            )
          })}
        </div>
      )}

      {/* Albums tab */}
      {libraryTab === "albums" && (
        <div className="grid grid-cols-5 gap-6">
          {(filteredAlbums.length > 0 ? filteredAlbums : LIB_ALBUMS).map((album, i) => (
            <div key={i} style={{ cursor: "pointer" }} onClick={() => onSelectAlbum?.({ ...album, coverUrl: album.art })}>
              <div
                style={{
                  borderRadius: 16, overflow: "hidden", aspectRatio: "1 / 1",
                  boxShadow: "0 4px 16px rgba(0,0,0,0.1), 0 1px 4px rgba(0,0,0,0.06)",
                  border: "1px solid var(--lib-art-border)", marginBottom: 10, position: "relative",
                }}
                onMouseEnter={(e) => { (e.currentTarget as HTMLDivElement).style.boxShadow = "0 8px 28px rgba(0,0,0,0.16), 0 2px 8px rgba(0,0,0,0.08)" }}
                onMouseLeave={(e) => { (e.currentTarget as HTMLDivElement).style.boxShadow = "0 4px 16px rgba(0,0,0,0.1), 0 1px 4px rgba(0,0,0,0.06)" }}
              >
                <img src={album.art} alt={album.title} style={{ width: "100%", height: "100%", objectFit: "cover", display: "block", transition: "transform 0.25s ease" }}
                  onMouseEnter={(e) => ((e.target as HTMLImageElement).style.transform = "scale(1.04)")}
                  onMouseLeave={(e) => ((e.target as HTMLImageElement).style.transform = "scale(1)")}
                />
              </div>
              <div className="library-album-title">{album.title}</div>
              <div className="library-artist-name">{album.artist}</div>
              <div className="library-audio-spec">{album.spec}</div>
            </div>
          ))}
        </div>
      )}

      {/* Artists tab */}
      {libraryTab === "artists" && (
        <div className="grid grid-cols-6 gap-6">
          {LIB_ARTISTS.map((artist, i) => (
            <div key={i} style={{ cursor: "pointer", textAlign: "center" }} onClick={() => onSelectArtist?.(artist)}>
              <div
                style={{
                  width: 128, height: 128, borderRadius: "50%", overflow: "hidden",
                  margin: "0 auto", border: "2px solid var(--lib-artist-circle-border)",
                  transition: "border-color 0.2s, box-shadow 0.2s",
                  boxShadow: "0 2px 8px rgba(0,0,0,0.06)",
                }}
                onMouseEnter={(e) => {
                  ;(e.currentTarget as HTMLDivElement).style.borderColor = "var(--lib-artist-circle-border-hover)"
                  ;(e.currentTarget as HTMLDivElement).style.boxShadow = "0 4px 16px rgba(0,0,0,0.12)"
                }}
                onMouseLeave={(e) => {
                  ;(e.currentTarget as HTMLDivElement).style.borderColor = "var(--lib-artist-circle-border)"
                  ;(e.currentTarget as HTMLDivElement).style.boxShadow = "0 2px 8px rgba(0,0,0,0.06)"
                }}
              >
                <img src={artist.art} alt={artist.name} style={{ width: "100%", height: "100%", objectFit: "cover", display: "block", transition: "transform 0.25s ease" }}
                  onMouseEnter={(e) => ((e.target as HTMLImageElement).style.transform = "scale(1.08)")}
                  onMouseLeave={(e) => ((e.target as HTMLImageElement).style.transform = "scale(1)")}
                />
              </div>
              <div style={{ fontSize: 13, fontWeight: 600, color: "var(--text-primary)", marginTop: 12, marginBottom: 3 }}>
                {artist.name}
              </div>
              <div style={{ fontSize: 11, color: "var(--text-muted)", fontFamily: "'DM Mono', monospace" }}>
                {artist.count}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Composers tab */}
      {libraryTab === "composers" && (
        <div className="grid grid-cols-4 gap-6">
          {LIB_COMPOSERS.map((composer, i) => (
            <div
              key={i}
              style={{
                borderRadius: 16, overflow: "hidden", border: "1px solid var(--lib-composer-card-border)",
                background: "var(--lib-composer-card-bg)", cursor: "pointer",
                boxShadow: "0 2px 8px rgba(0,0,0,0.05)",
                transition: "box-shadow 0.2s, transform 0.2s",
              }}
              onMouseEnter={(e) => {
                ;(e.currentTarget as HTMLDivElement).style.boxShadow = "0 8px 28px rgba(0,0,0,0.1)"
                ;(e.currentTarget as HTMLDivElement).style.transform = "translateY(-2px)"
              }}
              onMouseLeave={(e) => {
                ;(e.currentTarget as HTMLDivElement).style.boxShadow = "0 2px 8px rgba(0,0,0,0.05)"
                ;(e.currentTarget as HTMLDivElement).style.transform = "translateY(0)"
              }}
            >
              <div style={{ position: "relative", paddingTop: "133.33%", overflow: "hidden", background: "#F1F5F9" }}>
                <img
                  src={composer.art}
                  alt={composer.name}
                  style={{ position: "absolute", inset: 0, width: "100%", height: "100%", objectFit: "cover", display: "block", transition: "transform 0.3s ease" }}
                  onMouseEnter={(e) => ((e.target as HTMLImageElement).style.transform = "scale(1.05)")}
                  onMouseLeave={(e) => ((e.target as HTMLImageElement).style.transform = "scale(1)")}
                />
                <div style={{ position: "absolute", top: 10, left: 10 }}>
                  <span style={{
                    fontFamily: "'DM Mono', monospace", fontSize: 10, fontWeight: 600,
                    color: "#475569", background: "rgba(255,255,255,0.9)", backdropFilter: "blur(4px)",
                    borderRadius: 4, padding: "3px 8px", letterSpacing: "0.06em", textTransform: "uppercase",
                    border: "1px solid rgba(0,0,0,0.06)",
                  }}>
                    {composer.era}
                  </span>
                </div>
              </div>
              <div style={{ padding: "14px 16px 16px" }}>
                <div style={{ fontSize: 15, fontWeight: 700, color: "var(--text-primary)", fontFamily: "Georgia, 'Times New Roman', serif", marginBottom: 4, lineHeight: 1.25 }}>
                  {composer.name}
                </div>
                <div style={{ fontSize: 12, color: "var(--text-secondary)" }}>
                  {composer.works}
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Playlists tab */}
      {libraryTab === "playlists" && (
        <>
          <div className="library-playlist-count">
            <span className="library-playlist-count-num">{playlists.length}</span> Curated Playlist{playlists.length !== 1 ? "s" : ""} in Library
          </div>
          <div className="grid grid-cols-3 gap-5" style={{ gridAutoRows: "160px" }}>
            {/* New Playlist creation card */}
            <button
              onClick={() => setIsNewPlaylistModalOpen(true)}
              className="playlist-create-card"
            >
              <div className="playlist-create-icon">+</div>
              <div className="playlist-create-label">New Playlist</div>
              <div className="playlist-create-hint">Create collection</div>
            </button>

            {playlists.map((pl) => (
              <div key={pl.id} className="playlist-card-surface">
                {/* Art mosaic or single cover */}
                {pl.arts && pl.arts.length >= 4 ? (
                  <div style={{ width: 104, height: 104, borderRadius: 10, overflow: "hidden", display: "grid", gridTemplateColumns: "1fr 1fr", flexShrink: 0 }}>
                    {pl.arts.slice(0, 4).map((art, j) => (
                      <img key={j} src={art} alt="" style={{ width: "100%", height: "100%", objectFit: "cover", display: "block" }} />
                    ))}
                  </div>
                ) : (
                  <div style={{ width: 104, height: 104, borderRadius: 10, overflow: "hidden", flexShrink: 0 }}>
                    <img src={pl.art} alt={pl.name} style={{ width: "100%", height: "100%", objectFit: "cover", display: "block" }} />
                  </div>
                )}

                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: 8, marginBottom: 5 }}>
                    <div className="playlist-card-title">{pl.name}</div>
                    {onOpenShare && (
                      <button
                        onClick={(e) => {
                          e.stopPropagation()
                          onOpenShare({
                            type: "playlist",
                            id: pl.id,
                            title: pl.name,
                            subtitle: `${pl.trackCount || 0} tracks · Curated Crate`,
                            coverUrl: pl.art,
                            audioSpec: pl.spec || "Lossless Master Stream",
                          })
                        }}
                        className="playlist-share-btn"
                        title="Share Playlist"
                      >
                        {SHARE_ICON}
                      </button>
                    )}
                  </div>
                  <div className="playlist-card-meta">{pl.description}</div>
                  {pl.spec && (
                    <div className="playlist-card-spec">{pl.spec}</div>
                  )}
                  {pl.dr && (
                    <span className="library-dr-badge" style={{ borderRadius: 9999, padding: "2px 8px", display: "inline-block", fontWeight: 500 }}>
                      {pl.dr}
                    </span>
                  )}
                </div>
              </div>
            ))}
          </div>

          {/* New Playlist Modal */}
          {isNewPlaylistModalOpen && (
            <div
              style={{ position: "fixed", inset: 0, zIndex: 500, background: "rgba(0,0,0,0.45)", backdropFilter: "blur(4px)", display: "flex", alignItems: "center", justifyContent: "center" }}
              onClick={(e) => { if (e.target === e.currentTarget) { setIsNewPlaylistModalOpen(false); setNewTitle(""); setNewDesc("") } }}
            >
              <div style={{ width: 420, background: "#FFFFFF", borderRadius: 18, boxShadow: "0 24px 60px rgba(0,0,0,0.2)", border: "1px solid #E2E8F0", overflow: "hidden" }} onClick={(e) => e.stopPropagation()}>
                <div style={{ padding: "20px 24px 16px", borderBottom: "1px solid #F1F5F9", display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                  <div>
                    <div style={{ fontSize: 15, fontWeight: 700, color: "#0F172A", marginBottom: 2 }}>New Playlist</div>
                    <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10.5, color: "#94A3B8" }}>Audiophile Master Curation</div>
                  </div>
                  <button onClick={() => { setIsNewPlaylistModalOpen(false); setNewTitle(""); setNewDesc("") }} style={{ background: "none", border: "none", cursor: "pointer", color: "#94A3B8", padding: 4, display: "flex", alignItems: "center" }} aria-label="Close">
                    <MonoIcon.Close size={16} />
                  </button>
                </div>
                <div style={{ padding: "20px 24px 24px", display: "flex", flexDirection: "column", gap: 14 }}>
                  <div>
                    <label style={{ display: "block", fontSize: 11, fontWeight: 600, color: "#64748B", fontFamily: "'DM Mono', monospace", letterSpacing: "0.05em", textTransform: "uppercase", marginBottom: 6 }}>Playlist Title</label>
                    <input
                      type="text"
                      value={newTitle}
                      onChange={(e) => setNewTitle(e.target.value)}
                      placeholder="e.g. Late Night Reference Jazz"
                      autoFocus
                      style={{ width: "100%", boxSizing: "border-box", padding: "10px 12px", borderRadius: 9, border: "1px solid #E2E8F0", fontSize: 13, color: "#0F172A", outline: "none", fontFamily: "inherit", transition: "border-color 0.15s" }}
                      onFocus={(e) => (e.currentTarget.style.borderColor = "#7C3AED")}
                      onBlur={(e) => (e.currentTarget.style.borderColor = "#E2E8F0")}
                    />
                  </div>
                  <div>
                    <label style={{ display: "block", fontSize: 11, fontWeight: 600, color: "#64748B", fontFamily: "'DM Mono', monospace", letterSpacing: "0.05em", textTransform: "uppercase", marginBottom: 6 }}>Curator Note <span style={{ fontWeight: 400, color: "#94A3B8" }}>(optional)</span></label>
                    <input
                      type="text"
                      value={newDesc}
                      onChange={(e) => setNewDesc(e.target.value)}
                      placeholder="e.g. DR13+ only, no limiting"
                      style={{ width: "100%", boxSizing: "border-box", padding: "10px 12px", borderRadius: 9, border: "1px solid #E2E8F0", fontSize: 13, color: "#0F172A", outline: "none", fontFamily: "inherit", transition: "border-color 0.15s" }}
                      onFocus={(e) => (e.currentTarget.style.borderColor = "#7C3AED")}
                      onBlur={(e) => (e.currentTarget.style.borderColor = "#E2E8F0")}
                      onKeyDown={(e) => { if (e.key === "Enter" && newTitle.trim()) { /* submit below */ } }}
                    />
                  </div>
                  <div style={{ display: "flex", gap: 10, marginTop: 4 }}>
                    <button
                      onClick={() => { setIsNewPlaylistModalOpen(false); setNewTitle(""); setNewDesc("") }}
                      style={{ flex: 1, padding: "10px 14px", borderRadius: 9, border: "1px solid #E2E8F0", background: "#F8FAFC", cursor: "pointer", fontSize: 13, fontWeight: 500, color: "#475569", fontFamily: "inherit" }}
                    >
                      Cancel
                    </button>
                    <button
                      disabled={!newTitle.trim()}
                      onClick={() => {
                        if (!newTitle.trim()) return
                        // Core 가 만들고 목록을 다시 밀어 준다. 로컬 상태를 따로 두지 않는다.
                        void cmd.createPlaylist(newTitle.trim(), [])
                        setIsNewPlaylistModalOpen(false)
                        setNewTitle("")
                        setNewDesc("")
                      }}
                      style={{ flex: 1, padding: "10px 14px", borderRadius: 9, border: "none", background: newTitle.trim() ? "#7C3AED" : "#E2E8F0", cursor: newTitle.trim() ? "pointer" : "not-allowed", fontSize: 13, fontWeight: 700, color: newTitle.trim() ? "#FFFFFF" : "#94A3B8", fontFamily: "inherit", transition: "background 0.15s" }}
                    >
                      Create Playlist
                    </button>
                  </div>
                </div>
              </div>
            </div>
          )}
        </>
      )}

      {/* Tracks tab */}
      {libraryTab === "tracks" && (
        <div>
          <div className="library-track-count" style={{ marginBottom: 16 }}>
            <span className="library-track-count-num">420</span> Tracks in Library
          </div>

          {/* Table header */}
          <div style={{ display: "grid", gridTemplateColumns: TRACK_COL, padding: "4px 10px 10px", borderBottom: "1px solid var(--lib-tab-divider)", marginBottom: 4, userSelect: "none" }}>
            <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, fontWeight: 600, color: "var(--lib-track-num)", letterSpacing: "0.06em", textAlign: "center" }}>#</div>
            {(["title", "artist", "album", "format"] as const).map((field) => (
              <button
                key={field}
                onClick={() => handleHeaderClick(field)}
                title={field === "format" ? formatHeaderTooltip : undefined}
                style={{ all: "unset", fontFamily: "'DM Mono', monospace", fontSize: 10, fontWeight: sortField === field ? 700 : 600, color: sortField === field ? "var(--lib-track-header-active)" : "var(--lib-track-album)", letterSpacing: "0.06em", cursor: "pointer", transition: "color 0.15s", paddingRight: 14 }}
                onMouseEnter={(e) => { if (sortField !== field) (e.currentTarget as HTMLButtonElement).style.color = "var(--lib-track-artist)" }}
                onMouseLeave={(e) => { if (sortField !== field) (e.currentTarget as HTMLButtonElement).style.color = "var(--lib-track-album)" }}
              >
                {field.toUpperCase()}{sortArrow(field)}
              </button>
            ))}
            <button
              onClick={() => handleHeaderClick("dr")}
              style={{ all: "unset", fontFamily: "'DM Mono', monospace", fontSize: 10, fontWeight: sortField === "dr" ? 700 : 600, color: sortField === "dr" ? "var(--lib-track-header-active)" : "var(--lib-track-album)", letterSpacing: "0.06em", cursor: "pointer", transition: "color 0.15s", textAlign: "center" }}
              onMouseEnter={(e) => { if (sortField !== "dr") (e.currentTarget as HTMLButtonElement).style.color = "var(--lib-track-artist)" }}
              onMouseLeave={(e) => { if (sortField !== "dr") (e.currentTarget as HTMLButtonElement).style.color = "var(--lib-track-album)" }}
            >
              DR{sortArrow("dr")}
            </button>
            <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, fontWeight: 600, color: "var(--lib-track-num)", letterSpacing: "0.06em", textAlign: "right" }}>TIME</div>
          </div>

          {/* Album-grouped view */}
          {sortField === "album" && (() => {
            let seq = 0
            return albumGroups.map((group, gi) => (
              <div key={gi} style={{ marginBottom: 20 }}>
                <div className="flex items-center gap-3" style={{ padding: "12px 10px 8px", borderBottom: "1px solid var(--lib-album-group-divider)", marginBottom: 2 }}>
                  <img src={group.art} alt={group.album} style={{ width: 40, height: 40, borderRadius: 6, objectFit: "cover", flexShrink: 0, boxShadow: "0 2px 8px rgba(0,0,0,0.12)" }} />
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ fontSize: 13, fontWeight: 600, color: "var(--text-primary)", marginBottom: 1, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{group.album}</div>
                    <div style={{ fontSize: 11, color: "var(--lib-album-group-artist)" }}>{group.artist} · {group.year}</div>
                  </div>
                  <div className="flex items-center gap-2" style={{ flexShrink: 0 }}>
                    <span className="library-format-badge">{group.format}</span>
                    <span className="library-dr-badge">DR{group.dr}</span>
                  </div>
                </div>
                {group.tracks.map((t) => <TrackRow key={seq} t={t} idx={seq++} />)}
              </div>
            ))
          })()}

          {/* Flat sorted view */}
          {sortField !== "album" && sortedTracks.map((t, i) => <TrackRow key={i} t={t} idx={i} />)}
        </div>
      )}

      <div style={{ height: 40 }} />
    </div>
  )
}
