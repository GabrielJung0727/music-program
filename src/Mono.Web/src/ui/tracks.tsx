// 곡·앨범을 그리는 공용 부품. 어느 화면에서든 같은 행, 같은 카드, 같은 메뉴가 나온다.

import { type CSSProperties, type MouseEvent, type ReactNode } from "react"
import { Disc3, ListEnd, ListPlus, ListStart, MoreHorizontal, Play, Plus, UserRound } from "lucide-react"
import { useApp, type MenuItem } from "../state/app"
import { useMono, useMonoCommands } from "../state/MonoProvider"
import { fmtTime, type Album } from "../lib/model"
import type { CatalogTrack } from "../lib/protocol"
import { Art, Eq, QualityTag } from "./index"

/** 곡 하나에 대한 메뉴. 행의 ⋯ 버튼과 우클릭이 같은 것을 연다. */
export function useTrackMenu() {
  const app = useApp()
  const { playlists } = useMono()
  const cmd = useMonoCommands()

  return (e: MouseEvent, track: CatalogTrack, opts?: { playNow?: () => void; extra?: MenuItem[] }) => {
    const items: MenuItem[] = [
      { label: "지금 재생", icon: <Play size={15} />, disabled: !app.canControl, onSelect: opts?.playNow ?? (() => app.playTracks([track.id])) },
      { label: "다음에 재생", icon: <ListStart size={15} />, disabled: !app.canControl, onSelect: () => app.playNext(track) },
      { label: "대기열 끝에 추가", icon: <ListEnd size={15} />, disabled: !app.canControl, onSelect: () => app.addToQueue(track) },
      { sep: true },
      { title: "플레이리스트에 추가" },
      ...playlists.slice(0, 4).map<MenuItem>((p) => ({
        label: p.title,
        icon: <ListPlus size={15} />,
        onSelect: () => {
          void cmd.addPlaylistTrack(p.id, track.id).then(() => app.toast(`「${p.title}」에 추가했습니다`)).catch(() => {})
        },
      })),
      { label: "새 플레이리스트…", icon: <Plus size={15} />, onSelect: () => app.openModal({ kind: "playlist", trackIds: [track.id] }) },
      { sep: true },
      { label: "앨범 보기", icon: <Disc3 size={15} />, onSelect: () => { app.setNowPlayingOpen(false); app.go({ page: "album", id: track.albumId }) } },
      { label: "아티스트 보기", icon: <UserRound size={15} />, onSelect: () => { app.setNowPlayingOpen(false); app.go({ page: "artist", id: track.artistId }) } },
      ...(opts?.extra ?? []),
    ]
    app.openMenu(e, items, e.type === "contextmenu" ? "left" : "right")
  }
}

/**
 * 곡 한 줄.
 *
 * 열 구성은 부모가 --tcols 로 정한다. 여기서는 어떤 칸을 그릴지만 고른다.
 * 더블클릭으로 재생한다 — 한 번 클릭은 선택일 뿐이라, 스크롤하다 잘못 눌러 곡이 바뀌지 않는다.
 */
export function TrackRow({
  track, index, onPlay, showArt, showArtist = true, showAlbum, showQuality = true, tail, extraMenu,
}: {
  track: CatalogTrack
  /** 표시할 번호. 없으면 번호 칸에 재생 아이콘만 둔다. */
  index?: number
  onPlay: () => void
  showArt?: boolean
  showArtist?: boolean
  showAlbum?: boolean
  showQuality?: boolean
  tail?: ReactNode
  extraMenu?: MenuItem[]
}) {
  const app = useApp()
  const { room } = useMono()
  const openMenu = useTrackMenu()
  const isCurrent = room?.currentTrack?.id === track.id

  return (
    <div
      className={`trow ${isCurrent ? "is-current" : ""}`}
      onDoubleClick={onPlay}
      onContextMenu={(e) => openMenu(e, track, { playNow: onPlay, extra: extraMenu })}
    >
      <div className="trow__idx">
        {isCurrent ? (
          <Eq paused={!room?.playing} />
        ) : (
          <>
            <span className="n">{index ?? ""}</span>
            <button className="play" onClick={onPlay} aria-label={`${track.title} 재생`}>
              <Play size={14} fill="currentColor" />
            </button>
          </>
        )}
      </div>

      <div className="trow__main">
        {showArt && <Art src={track.artUrl} size={38} />}
        <div className="grow">
          <div className="trow__title clip">{track.title}</div>
          {showArtist && (
            <div className="trow__sub clip">
              <span className="link" onClick={() => app.go({ page: "artist", id: track.artistId })}>{track.artist}</span>
            </div>
          )}
        </div>
      </div>

      {showAlbum && (
        <div className="trow__cell clip">
          <span className="link" onClick={() => app.go({ page: "album", id: track.albumId })}>{track.album}</span>
        </div>
      )}
      {showQuality && <div><QualityTag of={track} /></div>}
      {tail}
      <div className="trow__time num">{fmtTime(track.durationMs)}</div>
      <button
        className="icon-btn icon-btn--sm trow__more"
        aria-label="더 보기"
        onClick={(e) => openMenu(e, track, { playNow: onPlay, extra: extraMenu })}
      >
        <MoreHorizontal size={17} />
      </button>
    </div>
  )
}

export function TrackHead({ cols }: { cols: (string | null)[] }) {
  return (
    <div className="thead">
      {cols.map((c, i) => (
        <span key={i} className="label" style={i === cols.length - 2 ? { textAlign: "right" } : undefined}>{c ?? ""}</span>
      ))}
    </div>
  )
}

/** 앨범 카드. 커버가 주인공이고, 글자는 두 줄로 끝낸다. */
export function AlbumCard({ album, sub, style }: { album: Album; sub?: ReactNode; style?: CSSProperties }) {
  const app = useApp()
  const { room } = useMono()
  const isPlaying = !!room?.currentTrack && room.currentTrack.albumId === album.id

  return (
    <div
      className={`card ${isPlaying ? "is-playing" : ""}`}
      style={style}
      role="link"
      tabIndex={0}
      onClick={() => app.go({ page: "album", id: album.id })}
      onKeyDown={(e) => { if (e.key === "Enter") app.go({ page: "album", id: album.id }) }}
    >
      <Art src={album.art}>
        <button
          className="play-btn card__play"
          aria-label={`${album.title} 재생`}
          onClick={(e) => { e.stopPropagation(); app.playTracks(album.trackIds, 0) }}
        >
          <Play size={17} fill="currentColor" style={{ marginLeft: 2 }} />
        </button>
      </Art>
      <div>
        <div className="card__title clip-2">{album.title}</div>
        <div className="card__sub clip">{sub ?? album.artist}</div>
        <div className="card__meta"><QualityTag quality={album.quality} /></div>
      </div>
    </div>
  )
}

/** 플레이리스트 대표 이미지: 수록곡 커버 넷을 격자로. size 를 주지 않으면 부모 폭을 채운다. */
export function Mosaic({ arts, size }: { arts: (string | null | undefined)[]; size?: number }) {
  const unique = [...new Set(arts.filter((a): a is string => !!a))]
  const shown = unique.length >= 4 ? unique.slice(0, 4) : unique.slice(0, 1)
  const box: CSSProperties = size ? { width: size, height: size } : { width: "100%", aspectRatio: "1" }
  if (shown.length === 0) return <Art size={size} style={size ? undefined : { width: "100%" }} />
  return (
    <div className={`mosaic ${shown.length === 1 ? "mosaic--one" : ""}`} style={{ ...box, borderRadius: !size || size > 80 ? "var(--r-art)" : undefined }}>
      {shown.map((src) => <img key={src} src={src} alt="" loading="lazy" draggable={false} />)}
    </div>
  )
}
