import { useEffect, useMemo, useState } from "react"
import { ChevronRight, Lock, Search, Users } from "lucide-react"
import { useApp } from "../state/app"
import { useMono, useMonoCommands } from "../state/MonoProvider"
import { useDebounced } from "../state/useDebounced"
import { Art, Avatar, Empty } from "../ui"
import { AlbumCard, TrackRow } from "../ui/tracks"
import { badgeRes } from "./Lounges"

export default function SearchPage({ q }: { q: string }) {
  const app = useApp()
  const { catalog, rooms } = useMono()
  const cmd = useMonoCommands()
  const [busy, setBusy] = useState(false)
  const needle = q.trim().toLowerCase()

  // 타이핑이 멎은 뒤에야 Core 로 보낸다. Core 는 그 질의로 TIDAL 카탈로그를 보강하므로,
  // 글자마다 보내면 곧장 요청 한도에 걸린다.
  const settled = useDebounced(q, 500)
  useEffect(() => {
    const text = settled.trim()
    if (text.length < 3) return
    let alive = true
    setBusy(true)
    void cmd.search(text).finally(() => { if (alive) setBusy(false) })
    return () => { alive = false }
  }, [settled, cmd])

  const tracks = useMemo(
    () => (needle ? catalog.filter((t) => `${t.title} ${t.artist} ${t.album}`.toLowerCase().includes(needle)) : []),
    [catalog, needle],
  )
  const albums = useMemo(
    () => (needle ? app.albums.filter((a) => `${a.title} ${a.artist} ${a.label}`.toLowerCase().includes(needle)) : []),
    [app.albums, needle],
  )
  const artists = useMemo(() => (needle ? app.artists.filter((a) => a.name.toLowerCase().includes(needle)) : []), [app.artists, needle])
  const lounges = useMemo(
    () => (needle ? rooms.filter((r) => `${r.name} ${r.host}`.toLowerCase().includes(needle)) : []),
    [rooms, needle],
  )

  const nothing = tracks.length + albums.length + artists.length + lounges.length === 0
  const topArtist = artists[0]

  return (
    <div className="page">
      <section>
        <div className="label">검색 결과{busy ? " · 스트리밍에서 더 찾는 중…" : ""}</div>
        <h1 className="t-h1 selectable" style={{ marginTop: 8 }}>“{q}”</h1>
      </section>

      {nothing ? (
        <section>
          <Empty icon={<Search size={40} strokeWidth={1.2} />} title="찾은 것이 없습니다">
            앨범·아티스트·곡 제목으로 찾습니다. 세 글자 이상이면 연결된 스트리밍 서비스에서도 함께 찾습니다.
          </Empty>
        </section>
      ) : (
        <>
          <section className="search-top" style={{ marginTop: 28 }}>
            {topArtist && (
              <button className="panel top-hit" onClick={() => app.go({ page: "artist", id: topArtist.id })}>
                <Art src={topArtist.art} size={112} round lift />
                <span className="label" style={{ marginTop: 20 }}>아티스트</span>
                <span className="t-h1 clip" style={{ marginTop: 6, maxWidth: "100%" }}>{topArtist.name}</span>
                <span className="t-small dim" style={{ marginTop: 6 }}>앨범 {topArtist.albumCount} · {topArtist.trackCount}곡</span>
                <ChevronRight size={18} className="top-hit__go" />
              </button>
            )}
            {tracks.length > 0 && (
              <div className="grow">
                <div className="sec-head"><h2 className="t-h2">곡</h2><span className="t-small dimmer">{tracks.length.toLocaleString()}개</span></div>
                <div className="tlist" style={{ "--tcols": "30px minmax(0,1.3fr) minmax(0,1fr) 96px 50px 30px" } as React.CSSProperties}>
                  {tracks.slice(0, topArtist ? 5 : 8).map((t, i) => (
                    <TrackRow key={t.id} track={t} index={i + 1} showArt showAlbum onPlay={() => app.playTracks(tracks.slice(0, 50).map((x) => x.id), i)} />
                  ))}
                </div>
              </div>
            )}
          </section>

          {albums.length > 0 && (
            <section>
              <div className="sec-head"><h2 className="t-h2">앨범</h2><span className="t-small dimmer">{albums.length}개</span></div>
              <div className="grid">{albums.slice(0, 12).map((a) => <AlbumCard key={a.id} album={a} />)}</div>
            </section>
          )}

          {artists.length > 1 && (
            <section>
              <div className="sec-head"><h2 className="t-h2">아티스트</h2></div>
              <div className="shelf" style={{ "--cols": 7 } as React.CSSProperties}>
                {artists.slice(0, 7).map((a) => (
                  <div key={a.id} className="card card--artist" role="link" tabIndex={0} onClick={() => app.go({ page: "artist", id: a.id })}>
                    <Art src={a.art} round />
                    <div style={{ textAlign: "center" }}><div className="card__title clip">{a.name}</div></div>
                  </div>
                ))}
              </div>
            </section>
          )}

          {lounges.length > 0 && (
            <section>
              <div className="sec-head"><h2 className="t-h2">라운지</h2></div>
              <div className="rows">
                {lounges.map((r) => (
                  <div key={r.id} className="row-card">
                    <Avatar name={r.host} size="lg" />
                    <div className="grow">
                      <div className="row gap-8">
                        <span className="t-h3 clip">{r.name}</span>
                        {r.playing && <span className="badge badge--live"><i className="lamp lamp--live" style={{ boxShadow: "none" }} />Live</span>}
                      </div>
                      <div className="t-small dimmer row gap-6" style={{ marginTop: 2 }}>
                        {r.host} · {r.needsInvite ? <Lock size={11} /> : <Users size={11} />}{r.members}명{badgeRes(r.badge) ? ` · ${badgeRes(r.badge)}` : ""}
                      </div>
                    </div>
                    <button className="btn btn--sm btn--line" onClick={() => app.go({ page: "lounges" })}>라운지에서 보기</button>
                  </div>
                ))}
              </div>
            </section>
          )}
        </>
      )}
    </div>
  )
}
