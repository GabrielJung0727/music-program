import type { PlayerMode } from "../PlayerBar"
import { MonoIcon } from "../icons/MonoIcons"

interface Props {
  selectedArtist: any
  activeLoungeRoom: boolean
  playerMode: PlayerMode
  onReturnToLounge: () => void
  onBack: () => void
  onSelectSomethinElse: () => void
  onSelectAlbum?: (title: string) => void
}

const DISCOGRAPHY = [
  { art: "https://images.unsplash.com/photo-1618172842918-3eabce30c912?w=400&h=400&fit=crop&auto=format", title: "Kind of Blue", year: "1959", format: "FLAC 192kHz", dr: "DR 14" },
  { art: "https://images.unsplash.com/photo-1619983081593-e2ba5b543168?w=400&h=400&fit=crop&auto=format", title: "Somethin' Else", year: "1958", format: "FLAC 192kHz", dr: "DR 14" },
  { art: "https://images.unsplash.com/photo-1669801158950-f663cf15298c?w=400&h=400&fit=crop&auto=format", title: "Bitches Brew", year: "1970", format: "DSD 2.8MHz", dr: "DR 12" },
  { art: "https://images.unsplash.com/photo-1552847340-1e26a6af19d4?w=400&h=400&fit=crop&auto=format", title: "'Round About Midnight", year: "1957", format: "FLAC 96kHz", dr: "DR 13" },
]

const COLLABORATIONS = [
  { art: "https://images.unsplash.com/photo-1618172842918-3eabce30c912?w=80&h=80&fit=crop&auto=format", albumTitle: "Somethin' Else", albumArtist: "Cannonball Adderley", year: "1958", role: "Featured Trumpet" },
  { art: "https://images.unsplash.com/photo-1736882178500-f99bbe22d77d?w=80&h=80&fit=crop&auto=format", albumTitle: "The Complete Savoy Sessions", albumArtist: "Charlie Parker", year: "1945", role: "Trumpet" },
]

export default function ArtistDetailView({ selectedArtist, activeLoungeRoom, playerMode, onReturnToLounge, onBack, onSelectSomethinElse, onSelectAlbum }: Props) {
  const handleAlbumClick = (title: string) => {
    if (onSelectAlbum) {
      onSelectAlbum(title)
    } else if (title === "Somethin' Else") {
      onSelectSomethinElse()
    }
  }

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
      <div className="flex items-start gap-10">
        <img
          src={selectedArtist.avatarUrl}
          alt={selectedArtist.name}
          className="w-48 h-48 rounded-2xl shadow-xl object-cover shrink-0"
          style={{ border: "1px solid var(--artist-art-border)" }}
        />
        <div className="flex flex-col min-w-0">
          <h1 className="artist-hero-name">{selectedArtist.name}</h1>
          <p className="text-xs font-mono uppercase tracking-wider mt-1.5" style={{ color: "var(--text-secondary)" }}>
            {selectedArtist.role} · {selectedArtist.era}
          </p>
          <p className="text-xs font-mono mt-0.5" style={{ color: "var(--text-muted)" }}>
            b. {selectedArtist.born} · {selectedArtist.labels}
          </p>
          <div className="flex flex-wrap gap-2 pt-3">
            {(selectedArtist.genres ?? []).map((g: string) => (
              <span key={g} className="artist-genre-pill">{g}</span>
            ))}
          </div>
          <div className="flex items-center gap-3 mt-5">
            <button className="artist-play-btn">
              <svg viewBox="0 0 16 16" width="13" height="13" fill="currentColor" aria-hidden="true"><path d="M3 2.5l10 5.5-10 5.5V2.5z"/></svg>
              Play Artist Radio
            </button>
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
      <div className="artist-bio-card">
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
      </div>

      {/* Discography Grid */}
      <div>
        <p className="text-[11px] font-mono font-bold uppercase tracking-wider mb-4" style={{ color: "var(--text-muted)" }}>
          Studio Recordings & Master Tape Editions
        </p>
        <div className="grid grid-cols-4 gap-6">
          {DISCOGRAPHY.map((album) => (
            <div
              key={album.title}
              className="cursor-pointer group"
              onClick={() => handleAlbumClick(album.title)}
            >
              <div
                className="relative rounded-xl overflow-hidden mb-3 shadow-md aspect-square"
                style={{ border: "1px solid var(--artist-art-border)" }}
              >
                <img
                  src={album.art}
                  alt={album.title}
                  className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                />
              </div>
              <p className="text-sm font-semibold leading-snug mb-0.5" style={{ color: "var(--text-primary)" }}>{album.title}</p>
              <p className="text-[11px] font-mono mb-2" style={{ color: "var(--text-muted)" }}>{album.year}</p>
              <div className="flex items-center gap-1.5 flex-wrap">
                <span className="artist-disco-badge">{album.format}</span>
                <span className="artist-disco-badge">{album.dr}</span>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Appears On & Session Work */}
      <div>
        <p className="text-[11px] font-mono font-bold uppercase tracking-wider mb-4" style={{ color: "var(--text-muted)" }}>
          Appears On & Session Work
        </p>
        <div className="flex flex-col gap-2.5">
          {COLLABORATIONS.map((entry) => (
            <div
              key={entry.albumTitle}
              className="artist-collab-row group"
              onClick={() => handleAlbumClick(entry.albumTitle)}
            >
              <img
                src={entry.art}
                alt={entry.albumTitle}
                className="w-12 h-12 rounded-lg object-cover shrink-0"
                style={{ border: "1px solid var(--artist-art-border)" }}
              />
              <div className="flex-1 min-w-0">
                <p className="text-sm font-semibold truncate transition-colors" style={{ color: "var(--text-primary)" }}>
                  {entry.albumTitle}
                </p>
                <p className="text-xs font-mono truncate mt-0.5" style={{ color: "var(--text-secondary)" }}>
                  {entry.albumArtist} · {entry.year}
                </p>
              </div>
              <span className="artist-collab-role-pill">{entry.role}</span>
            </div>
          ))}
        </div>
      </div>

    </div>
  )
}
