import { useEffect, useMemo, useState } from "react"
import { Play, Shuffle } from "lucide-react"
import { useApp } from "../state/app"
import { useMono, useMonoCommands } from "../state/MonoProvider"
import { mostPlayed, type Artist } from "../lib/model"
import type { ArtistGraph } from "../lib/protocol"
import { api } from "../lib/rest"
import { openExternal } from "../lib/shell"
import { Art, Empty } from "../ui"
import { Tint } from "../ui/motion"
import { AlbumCard, TrackRow } from "../ui/tracks"

export default function ArtistPage({ id }: { id: string }) {
  const app = useApp()
  const { catalog, history, catalogLoaded } = useMono()
  const cmd = useMonoCommands()
  const artist = app.artists.find((a) => a.id === id)
  const [graph, setGraph] = useState<ArtistGraph | null>(null)
  const [wiki, setWiki] = useState<{ text: string; url?: string } | null>(null)
  const [expanded, setExpanded] = useState(false)

  // 소개글과 연관 아티스트는 Core 의 graph 질의에서 온다. 소개가 비면 위키 요약을 쓴다.
  useEffect(() => {
    let alive = true
    const ac = new AbortController()
    void cmd.artistGraph(id).then((g) => { if (alive) setGraph(g) })
    api.wiki(id, ac.signal).then((w) => { if (alive && w.extract) setWiki({ text: w.extract, url: w.sourceUrl }) }).catch(() => {})
    return () => { alive = false; ac.abort() }
  }, [id, cmd])

  const albums = useMemo(
    () => app.albums.filter((a) => a.artistId === id).sort((a, b) => (b.year ?? 0) - (a.year ?? 0)),
    [app.albums, id],
  )
  const tracks = useMemo(() => catalog.filter((t) => t.artistId === id), [catalog, id])
  const top = useMemo(() => {
    const mine = history.filter((h) => app.trackById.get(h.trackId)?.artistId === id)
    const played = mostPlayed(mine, app.trackById, 5).map((r) => r.track)
    const rest = tracks.filter((t) => !played.includes(t))
    return [...played, ...rest].slice(0, 5)
  }, [history, tracks, app.trackById, id])

  if (!artist) {
    return <div className="page">{catalogLoaded && <Empty title="아티스트를 찾지 못했습니다" />}</div>
  }

  const bio = graph?.artist?.bio || wiki?.text
  const wikiUrl = !graph?.artist?.bio ? wiki?.url : undefined
  const related = (graph?.related ?? [])
    .map((r) => app.artists.find((a) => a.id === r.id))
    .filter((a): a is Artist => !!a)
  const allIds = albums.flatMap((a) => a.trackIds)

  return (
    <div className="page">
      <Tint src={artist.art} />

      <section className="detail-hero detail-hero--artist">
        <Art src={artist.art} className="detail-hero__art" round lift eager />
        <div className="detail-hero__body">
          <span className="label">아티스트{artist.genres[0] ? ` · ${artist.genres.slice(0, 2).join(", ")}` : ""}</span>
          <h1 className="t-display clip-2 selectable" style={{ fontSize: 56 }}>{artist.name}</h1>
          <p className="detail-hero__by dim">앨범 {artist.albumCount}장 · {artist.trackCount}곡</p>
          <div className="row gap-8" style={{ marginTop: 22 }}>
            <button className="btn btn--primary btn--lg" onClick={() => app.playTracks(allIds, 0)}><Play size={17} fill="currentColor" />재생</button>
            <button className="btn btn--lg btn--line" onClick={() => app.playTracks([...allIds].sort(() => Math.random() - 0.5), 0)}><Shuffle size={16} />셔플</button>
          </div>
        </div>
      </section>

      <section className="detail-cols">
        <div>
          <div className="sec-head"><h2 className="t-h2">대표곡</h2></div>
          <div className="tlist" style={{ "--tcols": "30px minmax(0,1.4fr) minmax(0,1fr) 96px 50px 30px" } as React.CSSProperties}>
            {top.map((t, i) => (
              <TrackRow key={t.id} track={t} index={i + 1} showArt showArtist={false} showAlbum onPlay={() => app.playTracks(top.map((x) => x.id), i)} />
            ))}
          </div>
        </div>
        <aside>
          <div className="label" style={{ marginBottom: 10 }}>소개</div>
          {bio ? (
            <>
              <p className={`prose ${expanded ? "" : "clip-6"}`}>{bio}</p>
              <div className="row gap-4" style={{ margin: "8px 0 0 -12px" }}>
                {bio.length > 180 && (
                  <button className="btn btn--quiet btn--sm" onClick={() => setExpanded(!expanded)}>{expanded ? "접기" : "더 읽기"}</button>
                )}
                {wikiUrl && <button className="btn btn--quiet btn--sm" onClick={() => openExternal(wikiUrl)}>위키백과에서 보기</button>}
              </div>
            </>
          ) : <p className="prose">아직 소개글이 없습니다.</p>}
        </aside>
      </section>

      <section>
        <div className="sec-head"><h2 className="t-h2">앨범</h2></div>
        <div className="grid">
          {albums.map((a) => <AlbumCard key={a.id} album={a} sub={[a.year, a.label].filter(Boolean).join(" · ")} />)}
        </div>
      </section>

      {related.length > 0 && (
        <section>
          <div className="sec-head"><h2 className="t-h2">함께 들으면 좋은 아티스트</h2></div>
          <div className="shelf" style={{ "--cols": 7 } as React.CSSProperties}>
            {related.slice(0, 7).map((a) => (
              <div key={a.id} className="card card--artist" role="link" tabIndex={0} onClick={() => app.go({ page: "artist", id: a.id })}>
                <Art src={a.art} round />
                <div style={{ textAlign: "center" }}>
                  <div className="card__title clip">{a.name}</div>
                  <div className="card__sub">{a.genres[0] ?? ""}</div>
                </div>
              </div>
            ))}
          </div>
        </section>
      )}
    </div>
  )
}
