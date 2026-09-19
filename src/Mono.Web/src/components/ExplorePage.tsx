import { useMemo, useState, useRef } from "react"
import { useMono } from "../state/MonoProvider"
import { useLiveSession } from "../state/useLiveSession"
import { libPlaylists } from "../lib/libraryData"
import { formatDuration } from "../lib/adapters"
import TrackActionMenu, { type TrackActionTarget, type TrackActionHandle } from "./TrackActionMenu"
import { MonoIcon } from "./icons/MonoIcons"
import { albumArt } from "../lib/artwork"

function IconChevron({ size = 12 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <path d="m6 9 6 6 6-6" />
    </svg>
  )
}

interface ExplorePageProps {
  onPlayNow?: (track: TrackActionTarget) => void
  onPlayNext?: (track: TrackActionTarget) => void
  onAddToQueue?: (track: TrackActionTarget) => void
  onSelectAlbum?: (album: any) => void
  connectedServices?: { qobuz: boolean; tidal: boolean }
  onConnectService?: (service: "qobuz" | "tidal") => void
}

export default function ExplorePage({ onPlayNow, onPlayNext, onAddToQueue, onSelectAlbum, connectedServices, onConnectService }: ExplorePageProps = {}) {
  const { catalog, playlists: corePlaylists } = useMono()
  const { albums, currentTrack, playing } = useLiveSession()

  // Core 에는 편집자 추천 API 가 없다. 화면의 세 구획을 카탈로그에서 정직하게 채운다:
  // 최근 추가된 앨범 · DR 이 높은(다이내믹) 앨범 · 내 플레이리스트.
  const selections = useMemo(() => {
    const addedOf = new Map(catalog.map((t) => [t.id, t.addedAt ?? ""]))
    return albums
      .map((a) => ({
        album: a,
        added: a.trackIds.map((id) => addedOf.get(id) ?? "").sort().pop() ?? "",
      }))
      .sort((x, y) => y.added.localeCompare(x.added))
      .slice(0, 5)
      .map(({ album, added }) => ({
        art: album.coverUrl,
        date: added ? new Date(added).toLocaleDateString([], { year: "numeric", month: "long", day: "numeric" }) : "",
        title: album.title,
        artist: album.artist,
        trackIds: album.trackIds,
        year: album.year,
      }))
  }, [albums, catalog])

  const awards = useMemo(() => {
    const drOf = (badge: string) => Number.parseInt(badge.replace(/[^0-9]/g, ""), 10) || 0
    return albums
      .filter((a) => drOf(a.dr) > 0)
      .sort((x, y) => drOf(y.dr) - drOf(x.dr))
      .slice(0, 5)
      .map((a) => ({
        art: a.coverUrl,
        badge: a.dr,
        title: a.title,
        artist: a.artist,
        trackIds: a.trackIds,
      }))
  }, [albums])

  const curatedPlaylists = useMemo(
    () => libPlaylists(corePlaylists, catalog).map((p) => ({ arts: p.arts, title: p.name, meta: p.description })),
    [corePlaylists, catalog],
  )

  // TIDAL 탭: 스트리밍으로 들어온 곡을 근거로 채운다. Core 에 TIDAL 편집자 피드 API 는 없다.
  const streamingAlbums = useMemo(
    () => albums.filter((a) => a.trackIds.some((id) => catalog.find((t) => t.id === id && !t.hasLocal))),
    [albums, catalog],
  )

  const dailyMixes = useMemo(() => {
    // 장르별로 묶어 "믹스" 네 개를 만든다. 같은 장르의 아티스트들이 한 카드에 모인다.
    const byGenre = new Map<string, { artists: Set<string>; art: string; trackIds: string[] }>()
    for (const t of catalog) {
      for (const g of t.genres) {
        const entry = byGenre.get(g) ?? { artists: new Set<string>(), art: "", trackIds: [] }
        if (t.artist) entry.artists.add(t.artist)
        if (!entry.art && t.artUrl) entry.art = t.artUrl
        entry.trackIds.push(t.id)
        byGenre.set(g, entry)
      }
    }
    return [...byGenre.entries()]
      .sort((a, b) => b[1].trackIds.length - a[1].trackIds.length)
      .slice(0, 4)
      .map(([genre, v]) => ({
        title: genre,
        artist: [...v.artists].slice(0, 2).join(", ") + (v.artists.size > 2 ? " +" : ""),
        art: v.art,
        trackIds: v.trackIds,
      }))
  }, [catalog])

  const recommendedColumns = useMemo(() => {
    const rows = catalog.slice(0, 9).map((t) => ({
      id: t.id,
      title: t.title,
      artist: [t.artist, t.album].filter(Boolean).join(" · "),
      duration: formatDuration(t.durationMs),
      art: t.artUrl ?? "",
    }))
    return [rows.slice(0, 3), rows.slice(3, 6), rows.slice(6, 9)].filter((c) => c.length > 0)
  }, [catalog])

  const suggestedAlbums = useMemo(
    () => (streamingAlbums.length > 0 ? streamingAlbums : albums).slice(0, 5).map((a) => ({
      art: a.coverUrl,
      title: a.title,
      artist: a.artist,
      trackIds: a.trackIds,
    })),
    [streamingAlbums, albums],
  )

  const qobuzConnected = connectedServices?.qobuz ?? false
  const tidalConnected = connectedServices?.tidal ?? false
  const isAnyConnected = qobuzConnected || tidalConnected

  // Default active tab to whichever service is connected; prefer qobuz
  const defaultTab: "qobuz" | "tidal" = tidalConnected && !qobuzConnected ? "tidal" : "qobuz"
  const [partnerTab, setPartnerTab] = useState<"qobuz" | "tidal">(defaultTab)
  const [qobuzSubTab, setQobuzSubTab] = useState<"releases" | "playlists" | "awards" | "charts">("releases")
  const [tidalSubTab, setTidalSubTab] = useState<"whatsnew" | "explore" | "playlists" | "collection">("whatsnew")

  const QOBUZ_SUB: Array<{ id: typeof qobuzSubTab; label: string }> = [
    { id: "releases", label: "NEW RELEASES" },
    { id: "playlists", label: "PLAYLISTS" },
    { id: "awards", label: "PRESS AWARDS" },
    { id: "charts", label: "TOP CHARTS" },
  ]

  const TIDAL_SUB: Array<{ id: typeof tidalSubTab; label: string }> = [
    { id: "whatsnew", label: "WHAT'S NEW" },
    { id: "explore", label: "EXPLORE" },
    { id: "playlists", label: "PLAYLISTS" },
    { id: "collection", label: "MY TIDAL COLLECTION" },
  ]

  const CarouselControls = ({ moreColor }: { moreColor?: string }) => (
    <div className="flex items-center gap-2">
      {(["‹", "›"] as const).map((ch) => (
        <button key={ch} className="explore-carousel-btn">{ch}</button>
      ))}
      <button className="explore-more-btn" style={moreColor ? { color: moreColor } : {}}>MORE ›</button>
    </div>
  )

  const SectionHeader = ({ title, moreColor }: { title: string; moreColor?: string }) => (
    <div className="flex items-center justify-between mb-5">
      <span className="explore-section-label">{title}</span>
      <CarouselControls moreColor={moreColor} />
    </div>
  )

  const AlbumArtFrame = ({ children, onClick }: { children: React.ReactNode; onClick?: () => void }) => (
    <div
      style={{
        borderRadius: 12, overflow: "hidden", aspectRatio: "1 / 1", marginBottom: 10,
        boxShadow: "0 3px 12px rgba(0,0,0,0.10)", border: "1px solid var(--explore-art-border)",
        transition: "box-shadow 0.2s ease, transform 0.2s ease", position: "relative",
      }}
      onClick={onClick}
      onMouseEnter={(e) => {
        (e.currentTarget as HTMLDivElement).style.boxShadow = "0 8px 24px rgba(0,0,0,0.18)"
        ;(e.currentTarget as HTMLDivElement).style.transform = "translateY(-3px)"
      }}
      onMouseLeave={(e) => {
        (e.currentTarget as HTMLDivElement).style.boxShadow = "0 3px 12px rgba(0,0,0,0.10)"
        ;(e.currentTarget as HTMLDivElement).style.transform = "translateY(0)"
      }}
    >
      {children}
    </div>
  )

  return (
    <div style={{ maxWidth: 1260, margin: "0 auto", padding: "40px 64px" }}>

      {/* ── Unconnected gate ─────────────────────────────────────────────────── */}
      {!isAnyConnected && (
        <div style={{ display: "flex", alignItems: "center", justifyContent: "center", minHeight: 480 }}>
          <div className="explore-unconnected-state">
            {/* Icon */}
            <div style={{
              width: 56, height: 56, borderRadius: 16, marginBottom: 24,
              background: "var(--accent-solo-surface)", border: "1px solid var(--accent-solo-border)",
              display: "flex", alignItems: "center", justifyContent: "center",
            }}>
              <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="var(--accent-solo)" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
                <path d="M9 18V5l12-2v13" />
                <circle cx="6" cy="18" r="3" />
                <circle cx="18" cy="16" r="3" />
              </svg>
            </div>

            <h2 style={{
              fontSize: 22, fontWeight: 700, letterSpacing: "-0.4px", marginBottom: 10,
              color: "var(--text-primary)", fontFamily: "Georgia, 'Times New Roman', serif",
              lineHeight: 1.25,
            }}>
              Connect Your Hi-Res Streaming Account
            </h2>
            <p style={{
              fontSize: 14, lineHeight: 1.65, color: "var(--text-secondary)",
              fontFamily: "inherit", marginBottom: 32, maxWidth: 460,
            }}>
              Link Qobuz or TIDAL to stream 24-bit Hi-Res masters, explore curated editorial crates, and sync bit-perfect playback across live lounges.
            </p>

            {/* CTA buttons */}
            <div style={{ display: "flex", gap: 14, flexWrap: "wrap", justifyContent: "center" }}>
              <button
                onClick={() => onConnectService?.("qobuz")}
                className="explore-connect-btn explore-connect-btn--qobuz"
                style={{ minWidth: 158 }}
              >
                <span style={{
                  display: "inline-flex", alignItems: "center", justifyContent: "center",
                  width: 20, height: 20, borderRadius: 6, flexShrink: 0,
                  background: "linear-gradient(135deg, #C9A227 0%, #F5D06B 50%, #B8891A 100%)",
                  fontFamily: "Georgia, serif", fontSize: 12, fontWeight: 900, color: "#0B1120",
                }}>Q</span>
                Connect Qobuz
              </button>
              <button
                onClick={() => onConnectService?.("tidal")}
                className="explore-connect-btn explore-connect-btn--tidal"
                style={{ minWidth: 158 }}
              >
                <span style={{
                  display: "inline-flex", alignItems: "center", justifyContent: "center",
                  width: 20, height: 20, borderRadius: 6, flexShrink: 0,
                  background: "rgba(0, 200, 220, 0.10)",
                  border: "1px solid rgba(0,200,220,0.30)",
                }}>
                  <svg width="12" height="10" viewBox="0 0 18 14" fill="none">
                    <path d="M9 0 L12 4 L15 0 L18 4 L15 8 L12 4 L9 8 L6 4 L3 8 L0 4 L3 0 L6 4 Z" fill="#00C8DC" />
                  </svg>
                </span>
                Connect TIDAL
              </button>
            </div>

            {/* Footnote */}
            <p style={{
              marginTop: 24, fontSize: 11, color: "var(--text-muted)",
              fontFamily: "'DM Mono', monospace", letterSpacing: "0.02em",
            }}>
              Zero transcoding · Direct CDN · Bit-perfect stream
            </p>
          </div>
        </div>
      )}

      {/* ── Connected catalog ────────────────────────────────────────────────── */}
      {isAnyConnected && (<>

      {/* Top bar: service switcher + genre filter */}
      <div className="flex items-center justify-between mb-7">
        <div className="flex items-center gap-6">
          {/* Only show tabs for connected services */}
          {(["qobuz", "tidal"] as const).filter(p => p === "qobuz" ? qobuzConnected : tidalConnected).map((p) => {
            const isActive = partnerTab === p
            return (
              <button
                key={p}
                onClick={() => setPartnerTab(p)}
                style={{
                  background: "none", border: "none", padding: "0 0 4px", cursor: "pointer",
                  fontFamily: "Georgia, 'Times New Roman', serif", fontSize: 20, letterSpacing: "-0.3px",
                  fontWeight: isActive ? 700 : 400,
                  color: isActive ? "var(--explore-service-active)" : "var(--explore-service-idle)",
                  borderBottom: isActive ? "2px solid var(--explore-service-active-border)" : "2px solid transparent",
                  transition: "color 0.15s",
                }}
                onMouseEnter={(e) => { if (!isActive) (e.currentTarget as HTMLButtonElement).style.color = "var(--explore-service-idle-hover)" }}
                onMouseLeave={(e) => { if (!isActive) (e.currentTarget as HTMLButtonElement).style.color = "var(--explore-service-idle)" }}
              >
                {p === "qobuz" ? "Qobuz" : "TIDAL"}
              </button>
            )
          })}
          {/* Add Service button when one provider is missing */}
          {(!qobuzConnected || !tidalConnected) && (
            <button
              onClick={() => onConnectService?.(qobuzConnected ? "tidal" : "qobuz")}
              style={{
                display: "inline-flex", alignItems: "center", gap: 5,
                background: "none", border: "1px dashed var(--border-subtle)",
                borderRadius: 9999, padding: "3px 11px", cursor: "pointer",
                fontSize: 11, fontFamily: "'DM Mono', monospace", letterSpacing: "0.03em",
                color: "var(--text-muted)", transition: "border-color 0.15s, color 0.15s",
              }}
              onMouseEnter={(e) => { const b = e.currentTarget as HTMLButtonElement; b.style.borderColor = "var(--accent-solo-border)"; b.style.color = "var(--accent-solo-text)" }}
              onMouseLeave={(e) => { const b = e.currentTarget as HTMLButtonElement; b.style.borderColor = "var(--border-subtle)"; b.style.color = "var(--text-muted)" }}
            >
              + Add {qobuzConnected ? "TIDAL" : "Qobuz"}
            </button>
          )}
        </div>

        <button
          style={{
            fontSize: 12, color: "var(--explore-genre-text)", border: "1px solid var(--explore-genre-border)",
            borderRadius: 9999, padding: "4px 12px", background: "var(--explore-genre-bg)",
            cursor: "pointer", fontFamily: "inherit", display: "flex", alignItems: "center", gap: 4,
            transition: "border-color 0.15s",
          }}
          onMouseEnter={(e) => ((e.currentTarget as HTMLButtonElement).style.borderColor = "var(--explore-genre-border-hover)")}
          onMouseLeave={(e) => ((e.currentTarget as HTMLButtonElement).style.borderColor = "var(--explore-genre-border)")}
        >
          All Genres <IconChevron size={10} />
        </button>
      </div>

      {/* Sub-nav */}
      <div style={{ borderBottom: "1px solid var(--explore-subtab-divider)", marginBottom: 32 }}>
        <div className="flex items-center gap-6">
          {(partnerTab === "qobuz" ? QOBUZ_SUB : TIDAL_SUB).map((tab) => {
            const active = partnerTab === "qobuz" ? qobuzSubTab === tab.id : tidalSubTab === tab.id
            return (
              <button
                key={tab.id}
                onClick={() => partnerTab === "qobuz"
                  ? setQobuzSubTab(tab.id as typeof qobuzSubTab)
                  : setTidalSubTab(tab.id as typeof tidalSubTab)}
                style={{
                  background: "none", border: "none", padding: "0 0 10px", cursor: "pointer",
                  fontFamily: "'DM Mono', monospace", fontSize: 11, fontWeight: active ? 700 : 400,
                  color: active ? "var(--explore-subtab-active)" : "var(--explore-subtab-idle)",
                  borderBottom: active ? "2px solid var(--explore-subtab-active-border)" : "2px solid transparent",
                  letterSpacing: "0.04em", whiteSpace: "nowrap", transition: "color 0.15s",
                }}
                onMouseEnter={(e) => { if (!active) (e.currentTarget as HTMLButtonElement).style.color = "var(--explore-subtab-idle-hover)" }}
                onMouseLeave={(e) => { if (!active) (e.currentTarget as HTMLButtonElement).style.color = "var(--explore-subtab-idle)" }}
              >
                {tab.label}
              </button>
            )
          })}
        </div>
      </div>

      {/* ── TIDAL layout ─────────────────────────────────────────────────────── */}
      {partnerTab === "tidal" && (
        <div style={{ display: "flex", flexDirection: "column", gap: 48 }}>

          {/* Mixes for you */}
          <section>
            <div className="flex items-center justify-between mb-5">
              <span className="explore-section-label">Mixes for you</span>
              <CarouselControls />
            </div>
            <div className="grid grid-cols-5 gap-5">
              {/* Daily Discovery card */}
              <div className="aspect-square rounded-xl p-4 flex flex-col justify-between cursor-pointer" style={{ background: "linear-gradient(135deg, #020617, #1e1b4b)" }}>
                <div>
                  <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, fontWeight: 700, color: "#67E8F9", letterSpacing: "0.05em", background: "rgba(103,232,249,0.12)", border: "1px solid rgba(103,232,249,0.25)", borderRadius: 4, padding: "2px 7px", display: "inline-flex", alignItems: "center", gap: 4 }}>
                    <MonoIcon.Sparkle size={11} />
                    <span>Daily Discovery</span>
                  </span>
                </div>
                <div className="flex justify-center">
                  <div className="w-10 h-10 bg-cyan-500 text-white rounded-full flex items-center justify-content-center shadow-lg flex items-center justify-center" style={{ transition: "transform 0.15s" }}
                    onMouseEnter={(e) => ((e.currentTarget as HTMLDivElement).style.transform = "scale(1.1)")}
                    onMouseLeave={(e) => ((e.currentTarget as HTMLDivElement).style.transform = "scale(1)")}
                  >
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z"/></svg>
                  </div>
                </div>
                <div style={{ fontSize: 13, fontWeight: 600, color: "#F1F5F9", lineHeight: 1.3 }}>My Daily Discovery</div>
              </div>

              {dailyMixes.map((mix, i) => (
                <div key={i}>
                  <div className="aspect-square rounded-xl overflow-hidden relative cursor-pointer" style={{ background: "var(--surface-elevated)", marginBottom: 8 }}>
                    <img src={albumArt(mix.art)} alt={mix.title} style={{ width: "100%", height: "100%", objectFit: "cover", display: "block", transition: "transform 0.25s" }}
                      onMouseEnter={(e) => ((e.target as HTMLImageElement).style.transform = "scale(1.06)")}
                      onMouseLeave={(e) => ((e.target as HTMLImageElement).style.transform = "scale(1)")}
                    />
                    <div style={{ position: "absolute", inset: 0, background: "linear-gradient(to top, rgba(0,0,0,0.65) 0%, transparent 55%)" }} />
                    <div style={{ position: "absolute", inset: 0, display: "flex", alignItems: "center", justifyContent: "center", opacity: 0, transition: "opacity 0.15s" }}
                      onMouseEnter={(e) => ((e.currentTarget as HTMLDivElement).style.opacity = "1")}
                      onMouseLeave={(e) => ((e.currentTarget as HTMLDivElement).style.opacity = "0")}
                    >
                      <div style={{ width: 38, height: 38, borderRadius: "50%", background: "rgba(255,255,255,0.9)", display: "flex", alignItems: "center", justifyContent: "center", boxShadow: "0 4px 16px rgba(0,0,0,0.3)" }}>
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="#0F172A"><path d="M8 5v14l11-7z"/></svg>
                      </div>
                    </div>
                    <div style={{ position: "absolute", bottom: 0, left: 0, right: 0, padding: "10px 12px" }}>
                      <div style={{ fontSize: 12, fontWeight: 600, color: "#FFFFFF", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{mix.title}</div>
                    </div>
                  </div>
                  <div className="explore-artist-name" style={{ fontSize: 11 }}>{mix.artist}</div>
                </div>
              ))}
            </div>
          </section>

          {/* Recommended new tracks */}
          <section>
            <div className="flex items-center justify-between mb-5">
              <span className="explore-section-label">Recommended new tracks</span>
              <button className="explore-more-btn">MORE ›</button>
            </div>
            <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
              {recommendedColumns.map((col, ci) => (
                <div key={ci} style={{ display: "flex", flexDirection: "column" }}>
                  {col.map((track, ti) => (
                    <ExploreTrackRow
                      key={ti}
                      track={track}
                      opensMenu={playing && currentTrack != null && currentTrack.id !== track.id && currentTrack.title !== track.title}
                      onPlayNow={onPlayNow}
                      onPlayNext={onPlayNext}
                      onAddToQueue={onAddToQueue}
                    />
                  ))}
                </div>
              ))}
            </div>
          </section>

          {/* Suggested new albums */}
          <section>
            <div className="flex items-center justify-between mb-5">
              <span className="explore-section-label">Suggested new albums</span>
              <CarouselControls />
            </div>
            <div className="grid grid-cols-5 gap-5">
              {suggestedAlbums.map((album, i) => (
                <div key={i} style={{ cursor: "pointer" }} onClick={() => onSelectAlbum?.({ ...album, coverUrl: album.art })}>
                  <AlbumArtFrame>
                    <img src={albumArt(album.art)} alt={album.title} style={{ width: "100%", height: "100%", objectFit: "cover", display: "block", transition: "transform 0.25s" }}
                      onMouseEnter={(e) => ((e.target as HTMLImageElement).style.transform = "scale(1.04)")}
                      onMouseLeave={(e) => ((e.target as HTMLImageElement).style.transform = "scale(1)")}
                    />
                  </AlbumArtFrame>
                  <div className="explore-album-title">{album.title}</div>
                  <div className="explore-artist-name">{album.artist}</div>
                </div>
              ))}
            </div>
          </section>

        </div>
      )}

      {/* ── Qobuz content ────────────────────────────────────────────────────── */}
      {partnerTab === "qobuz" && (
        <div style={{ display: "flex", flexDirection: "column", gap: 48 }}>

          {/* Grand Selection */}
          <section>
            <SectionHeader title="Qobuz Grand Selection" />
            <div className="grid grid-cols-5 gap-5">
              {selections.map((item, i) => (
                <div key={i} style={{ cursor: "pointer" }} onClick={() => onSelectAlbum?.({ art: item.art, coverUrl: item.art, title: item.title, artist: item.artist, year: item.year, trackIds: item.trackIds })}>
                  <AlbumArtFrame>
                    <img src={albumArt(item.art)} alt={item.title} style={{ width: "100%", height: "100%", objectFit: "cover", display: "block", transition: "transform 0.25s" }}
                      onMouseEnter={(e) => ((e.target as HTMLImageElement).style.transform = "scale(1.04)")}
                      onMouseLeave={(e) => ((e.target as HTMLImageElement).style.transform = "scale(1)")}
                    />
                  </AlbumArtFrame>
                  <div className="explore-audio-spec">{item.date}</div>
                  <div className="explore-album-title">{item.title}</div>
                  <div className="explore-artist-name">{item.artist}</div>
                </div>
              ))}
            </div>
          </section>

          {/* Press Awards */}
          <section>
            <SectionHeader title="Press Awards & Accolades" />
            <div className="grid grid-cols-5 gap-5">
              {awards.map((item, i) => (
                <div key={i} style={{ cursor: "pointer" }} onClick={() => onSelectAlbum?.({ art: item.art, coverUrl: item.art, title: item.title, artist: item.artist, trackIds: item.trackIds })}>
                  <AlbumArtFrame>
                    <img src={albumArt(item.art)} alt={item.title} style={{ width: "100%", height: "100%", objectFit: "cover", display: "block", transition: "transform 0.25s" }}
                      onMouseEnter={(e) => ((e.target as HTMLImageElement).style.transform = "scale(1.04)")}
                      onMouseLeave={(e) => ((e.target as HTMLImageElement).style.transform = "scale(1)")}
                    />
                    <div style={{ position: "absolute", bottom: 8, left: 8 }}>
                      <span className="badge-award-hallmark flex items-center gap-1">
                        <MonoIcon.Star size={10} />
                        <span>{item.badge}</span>
                      </span>
                    </div>
                  </AlbumArtFrame>
                  <div className="explore-album-title">{item.title}</div>
                  <div className="explore-artist-name">{item.artist}</div>
                </div>
              ))}
            </div>
          </section>

          {/* Curated Playlists */}
          <section>
            <SectionHeader title="Curated Playlists" />
            <div className="grid grid-cols-4 gap-5">
              {curatedPlaylists.map((pl, i) => (
                <div key={i} className="explore-playlist-card">
                  <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", aspectRatio: "1 / 1" }}>
                    {pl.arts.map((art, j) => (
                      <img key={j} src={albumArt(art)} alt="" style={{ width: "100%", height: "100%", objectFit: "cover", display: "block" }} />
                    ))}
                  </div>
                  <div style={{ padding: "12px 14px 14px" }}>
                    <div className="explore-album-title" style={{ marginBottom: 3 }}>{pl.title}</div>
                    <div className="explore-audio-spec" style={{ marginBottom: 0, color: "var(--explore-playlist-meta)" }}>{pl.meta}</div>
                  </div>
                </div>
              ))}
            </div>
          </section>

        </div>
      )}

      <div style={{ height: 40 }} />
      </>)}
    </div>
  )
}

function ExploreTrackRow({
  track,
  opensMenu,
  onPlayNow,
  onPlayNext,
  onAddToQueue,
}: {
  track: { id: string; title: string; artist: string; duration: string; art: string }
  opensMenu: boolean
  onPlayNow?: (track: TrackActionTarget) => void
  onPlayNext?: (track: TrackActionTarget) => void
  onAddToQueue?: (track: TrackActionTarget) => void
}) {
  const menuRef = useRef<TrackActionHandle>(null)
  const payload: TrackActionTarget = { id: track.id, title: track.title, artist: track.artist, duration: track.duration, art: track.art }
  return (
    <div
      className="group relative flex items-center gap-3 p-2 rounded-lg cursor-pointer"
      style={{ borderBottom: "1px solid var(--explore-track-divider)", transition: "background 0.12s" }}
      onMouseEnter={(e) => ((e.currentTarget as HTMLDivElement).style.background = "var(--explore-track-hover-bg)")}
      onMouseLeave={(e) => ((e.currentTarget as HTMLDivElement).style.background = "transparent")}
      onClick={(e) => {
        if ((e.target as HTMLElement).closest(".track-more-btn, .track-dropdown-menu")) return
        menuRef.current?.activate(e.currentTarget.getBoundingClientRect())
      }}
    >
      <img src={albumArt(track.art)} alt={track.title} className="w-10 h-10 rounded-md object-cover shrink-0" />
      <div style={{ flex: 1, minWidth: 0 }}>
        <div className="explore-album-title" style={{ marginBottom: 1 }}>{track.title}</div>
        <div className="explore-artist-name" style={{ fontSize: 11 }}>{track.artist}</div>
      </div>
      <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--text-muted)", flexShrink: 0 }}>{track.duration}</span>
      {(onPlayNow || onPlayNext || onAddToQueue) && (
        <TrackActionMenu
          ref={menuRef}
          track={payload}
          onPlayNow={onPlayNow ?? (() => {})}
          onPlayNext={onPlayNext ?? (() => {})}
          onAddToQueue={onAddToQueue ?? (() => {})}
          titleOpensMenu={opensMenu}
        />
      )}
    </div>
  )
}
