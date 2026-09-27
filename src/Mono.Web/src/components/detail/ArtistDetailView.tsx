import { useState } from "react"
import type { PlayerMode } from "../PlayerBar"
import { MonoIcon } from "../icons/MonoIcons"
import { albumArt } from "../../lib/artwork"

interface Props {
  selectedArtist: any
  activeLoungeRoom: boolean
  playerMode: PlayerMode
  onReturnToLounge: () => void
  onBack: () => void
  onSelectSomethinElse: () => void
  onSelectAlbum?: (title: string) => void
}

export default function ArtistDetailView({ selectedArtist, activeLoungeRoom, playerMode, onReturnToLounge, onBack, onSelectSomethinElse, onSelectAlbum }: Props) {
  const [avatarError, setAvatarError] = useState(false)

  const handleAlbumClick = (title: string) => {
    if (onSelectAlbum) {
      onSelectAlbum(title)
    }
  }

  const albums: Array<{ id: string; title: string; artUrl?: string | null; year?: number | null; trackCount: number }> = selectedArtist.albums ?? []
  const tracks: Array<{ id: string; title: string; album?: string | null; albumId: string; badge?: string | null }> = selectedArtist.tracks ?? []

  const initials = (selectedArtist.name ?? "")
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((w: string) => w[0])
    .join("")
    .toUpperCase() || "?"

  const metaLine = (() => {
    const parts: string[] = []
    if (selectedArtist.born && selectedArtist.died) parts.push(`${selectedArtist.born} – ${selectedArtist.died}`)
    else if (selectedArtist.born) parts.push(`b. ${selectedArtist.born}`)
    const genres: string[] = selectedArtist.genres ?? []
    if (genres.length) parts.push(genres.slice(0, 3).join(", "))
    if (selectedArtist.role) parts.push(selectedArtist.role)
    return parts.join(" • ") || null
  })()

  return (
    <div className="max-w-6xl mx-auto px-6 py-8 space-y-8">

      {/* Top bar: back */}
      <div>
        {activeLoungeRoom && playerMode === "host" ? (
          <button
            onClick={onReturnToLounge}
            className="text-xs font-mono font-medium px-3.5 py-1.5 rounded-full inline-flex items-center gap-2 cursor-pointer transition"
            style={{ background: "var(--surface-elevated)", border: "1px solid var(--border-subtle)", color: "var(--text-primary)" }}
          >
            <span style={{ width: 6, height: 6, borderRadius: "50%", background: "var(--accent-solo)", display: "inline-block", animation: "pulse 1.5s ease-in-out infinite", flexShrink: 0 }} />
            <MonoIcon.ArrowLeft size={12} />
            <span>Return to Live Lounge (ON AIR)</span>
          </button>
        ) : (
          <button onClick={onBack} className="album-back-btn" style={{ background: "none", border: "none", padding: 0 }}>
            <svg width="14" height="14" viewBox="0 0 16 16" fill="none" aria-hidden="true" style={{ flexShrink: 0 }}>
              <path d="M10 3L5 8l5 5" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round"/>
            </svg>
            Back
          </button>
        )}
      </div>

      {/* Hero header */}
      <div className="flex items-center gap-6 mb-2">
        {/* Avatar */}
        <div className="w-32 h-32 md:w-36 md:h-36 rounded-full overflow-hidden shrink-0 shadow-2xl" style={{ border: "2px solid rgba(255,255,255,0.1)", background: "var(--surface-elevated)" }}>
          {!avatarError && selectedArtist.avatarUrl ? (
            <img
              src={albumArt(selectedArtist.avatarUrl)}
              alt={selectedArtist.name}
              className="w-full h-full object-cover object-center"
              onError={() => setAvatarError(true)}
            />
          ) : (
            <div className="w-full h-full flex items-center justify-center text-2xl font-semibold" style={{ color: "var(--accent-violet)", background: "var(--surface-elevated)" }}>
              {initials}
            </div>
          )}
        </div>

        <div className="flex flex-col min-w-0">
          <h1 className="artist-hero-name">{selectedArtist.name}</h1>
          {metaLine && (
            <p className="text-sm font-medium mt-1.5" style={{ color: "var(--text-secondary)" }}>
              {metaLine}
            </p>
          )}
          <div className="flex flex-wrap gap-2 pt-3">
            {(selectedArtist.genres ?? []).map((g: string) => (
              <span key={g} className="artist-genre-pill">{g}</span>
            ))}
          </div>
          <div className="flex items-center gap-3 mt-5">
            {selectedArtist.wikiUrl && (
              <a href={selectedArtist.wikiUrl} target="_blank" rel="noopener noreferrer" className="artist-wiki-btn inline-flex items-center gap-1.5">
                <span style={{ fontFamily: "Georgia, serif", fontWeight: 700, fontSize: 13, lineHeight: 1 }}>W</span>
                <span>Wikipedia Biography</span>
                <MonoIcon.ExternalLink size={11} />
              </a>
            )}
          </div>
        </div>
      </div>

      {/* Biography Card */}
      {selectedArtist.wikiSummary && <div className="artist-bio-card">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-3">
            <span
              className="w-7 h-7 rounded-full text-xs font-bold flex items-center justify-center shrink-0"
              style={{ background: "var(--surface-elevated)", border: "1px solid var(--border-subtle)", color: "var(--text-secondary)", fontFamily: "Georgia, serif" }}
            >W</span>
            <span className="text-[10px] font-mono font-bold uppercase tracking-wider" style={{ color: "var(--text-muted)" }}>
              Wikipedia Archive & Historical Biography
            </span>
          </div>
          {selectedArtist.wikiUrl && (
            <a
              href={selectedArtist.wikiUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="text-[10px] font-mono no-underline shrink-0 transition-colors inline-flex items-center gap-1"
              style={{ color: "var(--text-muted)" }}
              onMouseEnter={e => (e.currentTarget.style.color = "var(--accent-solo)")}
              onMouseLeave={e => (e.currentTarget.style.color = "var(--text-muted)")}
            >
              <span>Open full Wikipedia entry</span>
              <MonoIcon.ExternalLink size={10} />
            </a>
          )}
        </div>
        <p className="text-sm leading-relaxed" style={{ color: "var(--text-secondary)", fontFamily: "Georgia, 'Times New Roman', serif" }}>
          {selectedArtist.wikiSummary}
        </p>
      </div>}

      {/* Discography Grid */}
      <div>
        <p className="text-[11px] font-mono font-bold uppercase tracking-wider mb-4" style={{ color: "var(--text-muted)" }}>
          Albums in Library
        </p>
        {albums.length === 0 ? <p style={{ color: "var(--text-muted)" }}>No albums in this library.</p> : <div className="grid grid-cols-4 gap-6">
          {albums.map((album) => (
            <div
              key={album.id}
              className="cursor-pointer group"
              onClick={() => handleAlbumClick(album.title)}
            >
              <div
                className="relative rounded-xl overflow-hidden mb-3 shadow-md aspect-square"
                style={{ border: "1px solid var(--artist-art-border)" }}
              >
                <img
                  src={albumArt(album.artUrl ?? "")}
                  alt={album.title}
                  className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                />
              </div>
              <p className="text-sm font-semibold leading-snug mb-0.5" style={{ color: "var(--text-primary)" }}>{album.title}</p>
              <p className="text-[11px] font-mono mb-2" style={{ color: "var(--text-muted)" }}>{album.year ?? ""} · {album.trackCount} tracks</p>
              <div className="flex items-center gap-1.5 flex-wrap">
              </div>
            </div>
          ))}
        </div>}
      </div>

      {/* Artist's actual catalog tracks, including compilation appearances. */}
      <div>
        <p className="text-[11px] font-mono font-bold uppercase tracking-wider mb-4" style={{ color: "var(--text-muted)" }}>
          Tracks in Library
        </p>
        {tracks.length === 0 ? <p style={{ color: "var(--text-muted)" }}>No tracks in this library.</p> :
          <div className="flex flex-col gap-2.5">{tracks.map(track => (
            <button key={track.id} className="artist-collab-row group" style={{ textAlign: "left", color: "var(--text-primary)" }} onClick={() => handleAlbumClick(track.album ?? albums.find(a => a.id === track.albumId)?.title ?? "") }>
              <span style={{ flex: 1 }}>{track.title}</span>
              <span style={{ color: "var(--text-secondary)" }}>{track.album ?? ""}</span>
              {track.badge && <span className="artist-collab-role-pill">{track.badge}</span>}
            </button>
          ))}</div>}
      </div>

    </div>
  )
}
