import { useMemo } from "react"
import { Play, Radio, Shuffle } from "lucide-react"
import { useApp } from "../state/app"
import { useMono } from "../state/MonoProvider"
import { fmtLong, relativeDay } from "../lib/model"
import type { CatalogTrack } from "../lib/protocol"
import { Empty } from "../ui"
import { Tint } from "../ui/motion"
import { Mosaic, TrackHead, TrackRow } from "../ui/tracks"

export default function PlaylistPage({ id }: { id: string }) {
  const app = useApp()
  const { playlists } = useMono()
  const playlist = playlists.find((p) => p.id === id)

  // 플레이리스트는 곡 id 만 안다. 음질·앨범 같은 나머지는 카탈로그에서 되찾는다.
  const tracks = useMemo(
    () => (playlist?.tracks ?? []).map((t) => app.trackById.get(t.id)).filter((t): t is CatalogTrack => !!t),
    [playlist, app.trackById],
  )

  if (!playlist) return <div className="page"><Empty title="플레이리스트를 찾지 못했습니다" /></div>

  const ids = tracks.map((t) => t.id)
  const missing = playlist.tracks.length - tracks.length
  const total = tracks.reduce((sum, t) => sum + t.durationMs, 0)

  return (
    <div className="page">
      <Tint src={tracks[0]?.artUrl} />

      <section className="detail-hero">
        <div className="detail-hero__art art art--lift"><Mosaic arts={tracks.map((t) => t.artUrl)} /></div>
        <div className="detail-hero__body">
          <span className="label">
            {playlist.fromArchiveId ? <><Radio size={11} style={{ verticalAlign: -1, marginRight: 5 }} />라운지 세션에서 저장</> : "플레이리스트"}
          </span>
          <h1 className="t-display clip-2 selectable">{playlist.title}</h1>
          <p className="detail-hero__by dim">{tracks.length}곡 · {fmtLong(total)} · {relativeDay(playlist.createdAt)} 만듦</p>
          <div className="row gap-8" style={{ marginTop: 26 }}>
            <button className="btn btn--primary btn--lg" onClick={() => app.playTracks(ids, 0)} disabled={ids.length === 0}><Play size={17} fill="currentColor" />재생</button>
            <button className="btn btn--lg btn--line" onClick={() => app.playTracks([...ids].sort(() => Math.random() - 0.5), 0)} disabled={ids.length === 0}><Shuffle size={16} />셔플</button>
          </div>
        </div>
      </section>

      <section>
        {tracks.length === 0 ? (
          <Empty title="아직 담긴 곡이 없습니다">곡의 ⋯ 메뉴에서 「플레이리스트에 추가」를 골라 채워 보세요.</Empty>
        ) : (
          <div className="tlist">
            <TrackHead cols={["#", "제목", "앨범", "음질", "길이", null]} />
            {tracks.map((t, i) => (
              <TrackRow key={`${t.id}-${i}`} track={t} index={i + 1} showArt showAlbum onPlay={() => app.playTracks(ids, i)} />
            ))}
          </div>
        )}
        {missing > 0 && (
          <p className="t-small dimmer" style={{ marginTop: 16 }}>라이브러리에서 찾지 못한 곡 {missing}개는 숨겼습니다.</p>
        )}
      </section>
    </div>
  )
}
