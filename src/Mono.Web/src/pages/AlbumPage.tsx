import { useEffect, useMemo, useState } from "react"
import { ListEnd, MoreHorizontal, Pause, Play, Plus, Shuffle, UserRound } from "lucide-react"
import { useApp } from "../state/app"
import { useMono, useMonoCommands } from "../state/MonoProvider"
import { fmtLong, qualityOf, sourceName, TIER_LABEL } from "../lib/model"
import { StreamingProvider, type AlbumDetail } from "../lib/protocol"
import { Art, Empty, QualityTag } from "../ui"
import { Credits, Tint } from "../ui/motion"
import { AlbumCard, TrackHead, TrackRow } from "../ui/tracks"

export default function AlbumPage({ id }: { id: string }) {
  const app = useApp()
  const { room, catalogLoaded } = useMono()
  const cmd = useMonoCommands()
  const album = app.albumById.get(id)
  const [detail, setDetail] = useState<AlbumDetail | null>(null)

  // 라이너 노트와 크레딧은 카탈로그에 실려 오지 않는다. 앨범을 열 때 따로 묻는다.
  useEffect(() => {
    let alive = true
    void cmd.albumDetail(id).then((d) => { if (alive) setDetail(d) })
    return () => { alive = false }
  }, [id, cmd])

  const more = useMemo(
    () => (album ? app.albums.filter((a) => a.artistId === album.artistId && a.id !== album.id) : []),
    [app.albums, album],
  )

  if (!album) {
    return (
      <div className="page">
        {catalogLoaded && <Empty title="앨범을 찾지 못했습니다">라이브러리에서 빠졌거나 폴더가 연결돼 있지 않습니다.</Empty>}
      </div>
    )
  }

  const isCurrent = room?.currentTrack?.albumId === album.id
  const various = new Set(album.tracks.map((t) => t.artistId)).size > 1
  const q = album.quality
  const cols = album.mixed ? "36px minmax(0,1fr) 110px 56px 34px" : "36px minmax(0,1fr) 56px 34px"

  return (
    <div className="page">
      <Tint src={album.art} />

      <section className="detail-hero">
        <Art src={album.art} className="detail-hero__art" lift eager />
        <div className="detail-hero__body">
          <span className="label">앨범{album.source !== StreamingProvider.Local ? ` · ${sourceName(album.source)}` : ""}</span>
          <h1 className="t-display clip-2 selectable">{album.title}</h1>
          <p className="detail-hero__by">
            <span className="link" onClick={() => app.go({ page: "artist", id: album.artistId })}>{album.artist}</span>
            {album.year ? <span className="dim"> · {album.year}</span> : null}
            {album.label ? <span className="dim"> · {album.label}</span> : null}
          </p>
          <div className="row gap-10" style={{ flexWrap: "wrap" }}>
            <QualityTag quality={q} pill />
            <span className="badge">{TIER_LABEL[q.tier]}</span>
            <span className="q">{album.tracks.length}곡 · {fmtLong(album.durationMs)}</span>
          </div>
          <div className="row gap-8" style={{ marginTop: 26 }}>
            {isCurrent ? (
              <button className="btn btn--primary btn--lg" onClick={() => (room?.playing ? cmd.pause() : cmd.play())} disabled={!app.canControl}>
                {room?.playing ? <><Pause size={17} fill="currentColor" />일시정지</> : <><Play size={17} fill="currentColor" />이어서 재생</>}
              </button>
            ) : (
              <button className="btn btn--primary btn--lg" onClick={() => app.playTracks(album.trackIds, 0)}>
                <Play size={17} fill="currentColor" />재생
              </button>
            )}
            <button className="btn btn--lg btn--line" onClick={() => app.playTracks([...album.trackIds].sort(() => Math.random() - 0.5), 0)}>
              <Shuffle size={16} />셔플
            </button>
            <button
              className="icon-btn icon-btn--solid"
              style={{ width: 44, height: 44 }}
              aria-label="더 보기"
              onClick={(e) => app.openMenu(e, [
                {
                  label: "대기열 끝에 추가", icon: <ListEnd size={15} />, disabled: !app.canControl,
                  onSelect: () => { void cmd.ensureSoloRoom().then(() => { cmd.enqueueMany(album.trackIds); app.toast(`대기열에 ${album.tracks.length}곡을 추가했습니다`) }) },
                },
                { label: "이 앨범으로 플레이리스트 만들기", icon: <Plus size={15} />, onSelect: () => app.openModal({ kind: "playlist", trackIds: album.trackIds }) },
                { sep: true },
                { label: "아티스트 보기", icon: <UserRound size={15} />, onSelect: () => app.go({ page: "artist", id: album.artistId }) },
              ])}
            >
              <MoreHorizontal size={19} />
            </button>
          </div>
        </div>
      </section>

      <section className="detail-cols">
        <div className="tlist" style={{ "--tcols": cols } as React.CSSProperties}>
          <TrackHead cols={album.mixed ? ["#", "제목", "음질", "길이", null] : ["#", "제목", "길이", null]} />
          {album.tracks.map((t, i) => (
            <TrackRow
              key={t.id}
              track={t}
              index={i + 1}
              showArtist={various}
              showQuality={album.mixed}
              onPlay={() => app.playTracks(album.trackIds, i)}
            />
          ))}
        </div>

        <aside className="col gap-24">
          <div>
            <div className="label" style={{ marginBottom: 10 }}>라이너 노트</div>
            <p className="prose">{detail?.linerNotes || "이 앨범에는 라이너 노트가 없습니다."}</p>
          </div>
          {detail?.credits && (
            <div>
              <div className="label" style={{ marginBottom: 10 }}>크레딧</div>
              <Credits text={detail.credits} />
            </div>
          )}
          <div>
            <div className="label" style={{ marginBottom: 12 }}>음원 정보</div>
            <dl className="kv">
              <dt>형식</dt><dd className="mono" style={{ fontSize: 12.5 }}>{album.mixed ? "곡마다 다름" : [q.container, q.res].filter(Boolean).join(" ")}</dd>
              <dt>출처</dt><dd>{album.source === StreamingProvider.Local ? "로컬 라이브러리" : sourceName(album.source)}</dd>
              {album.label && <><dt>레이블</dt><dd>{album.label}</dd></>}
              {album.genres.length > 0 && <><dt>장르</dt><dd>{album.genres.join(", ")}</dd></>}
              {album.tracks[0]?.composers.length > 0 && <><dt>작곡</dt><dd>{[...new Set(album.tracks.flatMap((t) => t.composers))].join(", ")}</dd></>}
              {!album.mixed && album.tracks[0] && q.tier !== "dsd" && (
                <><dt>해상도</dt><dd className="mono" style={{ fontSize: 12.5 }}>{qualityOf(album.tracks[0]).res.replace("/", "-bit / ")} kHz</dd></>
              )}
            </dl>
          </div>
        </aside>
      </section>

      {more.length > 0 && (
        <section>
          <div className="sec-head"><h2 className="t-h2">{album.artist}의 다른 앨범</h2></div>
          <div className="shelf">
            {more.slice(0, 6).map((a) => <AlbumCard key={a.id} album={a} sub={a.year ? String(a.year) : undefined} />)}
          </div>
        </section>
      )}
    </div>
  )
}
