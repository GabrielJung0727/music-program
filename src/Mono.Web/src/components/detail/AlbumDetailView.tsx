import { useRef } from "react"
import TrackActionMenu, { type TrackActionHandle } from "../TrackActionMenu"
import type { TrackActionTarget } from "../TrackActionMenu"
import type { PlayerMode } from "../PlayerBar"
import type { QueueTrack } from "../../data/types"
import { useLiveSession } from "../../state/useLiveSession"
import { MonoIcon } from "../icons/MonoIcons"
import { albumArt } from "../../lib/artwork"

interface Props {
  selectedAlbum: any
  currentTrack: QueueTrack | null
  activeLoungeRoom: boolean
  playerMode: PlayerMode
  onReturnToLounge: () => void
  onBack: () => void
  onPlayNow: (t: TrackActionTarget) => void
  onPlayNext: (t: TrackActionTarget) => void
  onAddToQueue: (t: TrackActionTarget) => void
  /** 앨범 전체를 큐에 걸고 고른 칸부터 재생한다. */
  onPlayAlbumFrom: (tracks: TrackActionTarget[], startIndex: number) => void
}

export default function AlbumDetailView({ selectedAlbum, currentTrack, activeLoungeRoom, playerMode, onReturnToLounge, onBack, onPlayNow, onPlayNext, onAddToQueue, onPlayAlbumFrom }: Props) {
  const { findAlbum } = useLiveSession()
  const artSrc: string = selectedAlbum?.art || selectedAlbum?.coverUrl || ""

  // 트랙 목록: 넘어온 게 있으면 그대로, 없으면 카탈로그에서 제목으로 찾는다.
  const resolvedTracks: any[] = (() => {
    if (selectedAlbum?.tracks?.length) return selectedAlbum.tracks
    return findAlbum(selectedAlbum?.title ?? "")?.tracks ?? []
  })()

  // Liner notes: prefer wikiSummary, fall back to generic
  const linerNotes: string =
    selectedAlbum?.wikiSummary ||
    selectedAlbum?.description ||
    selectedAlbum?.notes ||
    "이 앨범에는 라이너 노트가 없습니다."

  const isTrackActive = (track: any): boolean =>
    currentTrack !== null && (
      (currentTrack.title === track.title && currentTrack.album === selectedAlbum?.title) ||
      (track.id != null && currentTrack.id === track.id)
    )

  const asTarget = (track: any): TrackActionTarget => ({
    id: track.id,
    title: track.title,
    artist: track.artist ?? selectedAlbum?.artist,
    album: selectedAlbum?.title,
    duration: track.duration,
    format: selectedAlbum?.format,
    dr: track.dr,
    art: artSrc,
  })

  /**
   * 앨범 안에서 재생을 시작하면 앨범 전체가 큐가 되고, 고른 곡이 그 안의 시작점이 된다.
   * 예전에는 고른 한 곡만 큐 끝에 붙어서, 가운데 곡을 누르면 순서가 뒤엉킨 것처럼 보였다.
   */
  const playFromIndex = (index: number) => {
    if (resolvedTracks.length === 0) return
    onPlayAlbumFrom(resolvedTracks.map(asTarget), index)
  }

  return (
    <div className="max-w-5xl mx-auto px-8 py-10 space-y-8">

      {/* Breadcrumb */}
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
          <button
            onClick={onBack}
            className="album-back-btn"
            style={{ background: "none", border: "none", padding: 0 }}
          >
            <svg width="14" height="14" viewBox="0 0 16 16" fill="none" aria-hidden="true" style={{ flexShrink: 0 }}>
              <path d="M10 3L5 8l5 5" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round"/>
            </svg>
            Repertoire
          </button>
        )}
      </div>

      {/* Hero */}
      <div className="flex items-start gap-10">
        <div className="relative shrink-0">
          {artSrc && (
            <img src={albumArt(artSrc)} aria-hidden="true" className="absolute -inset-4 w-[calc(100%+32px)] h-[calc(100%+32px)] object-cover rounded-3xl pointer-events-none" style={{ opacity: 0.12, filter: "blur(40px)", zIndex: 0 }} />
          )}
          <img
            src={albumArt(artSrc)}
            alt={selectedAlbum?.title}
            className="relative w-56 h-56 rounded-2xl object-cover shadow-xl"
            style={{ border: "1px solid var(--album-detail-border)", zIndex: 1, background: artSrc ? undefined : "#E4E4E7" }}
          />
        </div>
        <div className="flex flex-col min-w-0 justify-center pt-1">
          <p className="text-[10px] font-mono uppercase tracking-widest mb-2" style={{ color: "var(--text-muted)" }}>
            {selectedAlbum?.year || "1958"} · {selectedAlbum?.label || "Master Recording"}
          </p>
          <h1 className="album-title-primary">
            {selectedAlbum?.title || "Album Title"}
          </h1>
          <p className="text-sm mb-1" style={{ color: "var(--text-secondary)" }}>{selectedAlbum?.artist || "Artist"}</p>
          {selectedAlbum?.studio && (
            <p className="text-xs font-mono mb-5" style={{ color: "var(--text-muted)" }}>{selectedAlbum.studio}</p>
          )}
          <div className="flex flex-wrap gap-2 mb-6">
            {selectedAlbum?.format && (
              <span className="album-detail-pill">{selectedAlbum.format}</span>
            )}
            {selectedAlbum?.dr && (
              <span className="album-detail-pill">{selectedAlbum.dr} Uncompressed</span>
            )}
            <span className="album-detail-pill">¼″ Analog Master Transfer</span>
          </div>
          <div className="flex items-center gap-3">
            <button
              onClick={() => playFromIndex(0)}
              disabled={resolvedTracks.length === 0}
              className="btn-album-play"
            >
              <svg viewBox="0 0 16 16" width="13" height="13" fill="currentColor" aria-hidden="true"><path d="M3 2.5l10 5.5-10 5.5V2.5z" /></svg>
              Play Album
            </button>
            {selectedAlbum?.wikiUrl && (
              <a href={selectedAlbum.wikiUrl} target="_blank" rel="noopener noreferrer" className="btn-album-secondary">
                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                  <circle cx="12" cy="12" r="10"/><line x1="2" y1="12" x2="22" y2="12"/><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/>
                </svg>
                <span>Wikipedia</span>
                <MonoIcon.ExternalLink size={11} />
              </a>
            )}
          </div>
        </div>
      </div>

      {/* Liner Notes Card */}
      <div className="album-liner-notes-surface">
        <div className="flex items-center justify-between mb-3">
          <span className="text-[10px] font-mono uppercase tracking-widest" style={{ color: "var(--text-muted)" }}>Studio Liner Notes</span>
          {selectedAlbum?.wikiUrl && (
            <a href={selectedAlbum.wikiUrl} target="_blank" rel="noopener noreferrer" className="text-[10px] font-mono transition-colors no-underline hover:opacity-80" style={{ color: "var(--text-muted)" }}>Open article</a>
          )}
        </div>
        <p className="text-xs leading-relaxed" style={{ color: "var(--text-secondary)" }}>{linerNotes}</p>
      </div>

      {/* Master Tape Tracklist */}
      {resolvedTracks.length === 0 && (
        <div className="album-liner-notes-surface px-5 py-6 text-center">
          <p className="text-[10px] font-mono uppercase tracking-widest mb-1.5" style={{ color: "var(--text-muted)" }}>Master Tracklist</p>
          <p className="text-xs font-mono" style={{ color: "var(--text-muted)" }}>Master tracklist pending verification.</p>
        </div>
      )}
      {resolvedTracks.length > 0 && (
        <div>
          <p className="text-[11px] font-mono uppercase tracking-wider mb-3" style={{ color: "var(--text-muted)" }}>Master Tape Tracklist</p>
          <div className="album-detail-table-wrap">
            <table className="w-full text-sm">
              <thead>
                <tr className="album-detail-table-header">
                  <th className="text-left text-[10px] font-mono uppercase tracking-wider px-5 py-3 w-12" style={{ color: "var(--text-muted)" }}>#</th>
                  <th className="text-left text-[10px] font-mono uppercase tracking-wider px-3 py-3" style={{ color: "var(--text-muted)" }}>Title</th>
                  <th className="text-left text-[10px] font-mono uppercase tracking-wider px-3 py-3" style={{ color: "var(--text-muted)" }}>Artist</th>
                  <th className="text-center text-[10px] font-mono uppercase tracking-wider px-3 py-3" style={{ color: "var(--text-muted)" }}>DR</th>
                  <th className="text-right text-[10px] font-mono uppercase tracking-wider px-5 py-3" style={{ color: "var(--text-muted)" }}>Time</th>
                  <th className="w-8" />
                </tr>
              </thead>
              <tbody>
                {resolvedTracks.map((track: any, tIdx: number) => (
                  <AlbumTrackRow
                    key={track.num ?? track.title ?? tIdx}
                    track={track}
                    index={tIdx}
                    artSrc={artSrc}
                    albumTitle={selectedAlbum?.title}
                    albumArtist={selectedAlbum?.artist}
                    albumFormat={selectedAlbum?.format}
                    active={isTrackActive(track)}
                    opensMenu={currentTrack != null && !isTrackActive(track)}
                    isGuest={playerMode === "guest"}
                    onPlayNow={() => playFromIndex(tIdx)}
                    onPlayNext={onPlayNext}
                    onAddToQueue={onAddToQueue}
                  />
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

    </div>
  )
}

function AlbumTrackRow({
  track,
  index,
  artSrc,
  albumTitle,
  albumArtist,
  albumFormat,
  active,
  opensMenu,
  isGuest,
  onPlayNow,
  onPlayNext,
  onAddToQueue,
}: {
  track: any
  index: number
  artSrc: string
  albumTitle?: string
  albumArtist?: string
  albumFormat?: string
  active: boolean
  opensMenu: boolean
  isGuest: boolean
  onPlayNow: (t: TrackActionTarget) => void
  onPlayNext: (t: TrackActionTarget) => void
  onAddToQueue: (t: TrackActionTarget) => void
}) {
  const menuRef = useRef<TrackActionHandle>(null)
  const payload: TrackActionTarget = {
    id: track.id,
    title: track.title,
    artist: track.artist ?? albumArtist,
    album: albumTitle,
    duration: track.duration,
    format: albumFormat,
    dr: track.dr,
    art: artSrc,
  }

  return (
    <tr
      className={`group album-track-row ${active ? "album-track-row-active" : ""}`}
      onClick={(e) => {
        if ((e.target as HTMLElement).closest(".track-more-btn, .track-dropdown-menu")) return
        menuRef.current?.activate(e.currentTarget.getBoundingClientRect())
      }}
    >
      <td className="px-5 py-3.5 w-12 text-center">
        {active ? (
          <MonoIcon.PlayMini size={11} color="var(--album-detail-active-accent)" />
        ) : (
          <>
            <span className="text-xs font-mono group-hover:hidden" style={{ color: "var(--text-muted)" }}>{index + 1}</span>
            <span className="hidden group-hover:inline"><MonoIcon.PlayMini size={11} color="var(--text-muted)" /></span>
          </>
        )}
      </td>
      <td className="px-3 py-3.5">
        <span className="album-track-title-btn">{track.title}</span>
      </td>
      <td className="px-3 py-3.5"><span className="text-xs font-mono" style={{ color: "var(--text-secondary)" }}>{track.artist ?? albumArtist}</span></td>
      <td className="px-3 py-3.5 text-center"><span className="badge-dr-meter">{track.dr}</span></td>
      <td className="px-5 py-3.5 text-right"><span className="text-xs font-mono" style={{ color: "var(--text-muted)" }}>{track.duration}</span></td>
      <td className="pr-2 py-2 w-8">
        <TrackActionMenu
          ref={menuRef}
          track={payload}
          onPlayNow={onPlayNow}
          onPlayNext={onPlayNext}
          onAddToQueue={onAddToQueue}
          isGuest={isGuest}
          titleOpensMenu={opensMenu}
        />
      </td>
    </tr>
  )
}
