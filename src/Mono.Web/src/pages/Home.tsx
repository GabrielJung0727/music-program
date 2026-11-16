import { useMemo } from "react"
import { ArrowRight, ChevronRight, Lock, Play, Radio, Shuffle, Users } from "lucide-react"
import { useApp } from "../state/app"
import { useMono, useMonoCommands } from "../state/MonoProvider"
import { fmtLong, libraryStats, mostPlayed, recentAlbumIds, relativeDay, TIER_LABEL, type Album, type Tier } from "../lib/model"
import { Art, Avatar, Empty, QualityTag } from "../ui"
import { Tint } from "../ui/motion"
import { AlbumCard, TrackRow } from "../ui/tracks"
import { badgeRes } from "./Lounges"

function greeting(): string {
  const h = new Date().getHours()
  if (h < 5) return "늦은 밤이네요"
  if (h < 11) return "좋은 아침이에요"
  if (h < 17) return "좋은 오후예요"
  if (h < 21) return "좋은 저녁이에요"
  return "편안한 밤 되세요"
}

export default function Home() {
  const app = useApp()
  const { displayName, room, rooms, history, catalog, catalogLoaded } = useMono()
  const cmd = useMonoCommands()
  const { albums, albumById, trackById } = app

  const recentIds = useMemo(() => recentAlbumIds(history, trackById), [history, trackById])
  const recent = useMemo(() => recentIds.map((id) => albumById.get(id)).filter((a): a is Album => !!a), [recentIds, albumById])
  const added = useMemo(() => [...albums].sort((a, b) => (b.addedAt ?? "").localeCompare(a.addedAt ?? "")), [albums])
  const top = useMemo(() => mostPlayed(history, trackById, 5), [history, trackById])
  const stats = useMemo(() => libraryStats(catalog, albums, history), [catalog, albums, history])

  // 맨 위 한 장: 지금 듣는 앨범 → 마지막으로 들은 앨범 → 가장 최근에 넣은 앨범.
  const nowAlbum = room?.currentTrack?.albumId ? albumById.get(room.currentTrack.albumId) : undefined
  const hero = nowAlbum ?? recent[0] ?? added[0]
  const heroKind = nowAlbum ? "지금 재생 중" : recent[0] ? "이어서 듣기" : "새로 들어온 앨범"
  const lastHeard = !nowAlbum && recent[0] ? history.find((h) => trackById.get(h.trackId)?.albumId === recent[0].id)?.heardAt : null

  const live = useMemo(() => [...rooms].sort((a, b) => b.members - a.members).slice(0, 4), [rooms])
  const today = new Date().toLocaleDateString("ko-KR", { month: "long", day: "numeric", weekday: "long" })

  if (catalogLoaded && albums.length === 0) {
    return (
      <div className="page">
        <Empty
          title="라이브러리가 비어 있습니다"
          action={<button className="btn btn--primary" onClick={() => app.go({ page: "settings", tab: "library" })}>음악 폴더 추가</button>}
        >
          음악이 든 폴더를 알려 주면 Mono 가 태그와 커버를 읽어 라이브러리를 만듭니다.
        </Empty>
      </div>
    )
  }

  const tiers = (["dsd", "hires", "cd", "lossy"] as Tier[]).filter((t) => stats.tiers[t] > 0)

  return (
    <div className="page">
      <Tint src={hero?.art} />

      <section>
        <div className="label">{today}</div>
        <h1 className="t-h1" style={{ marginTop: 8 }}>{greeting()}, {displayName}</h1>
      </section>

      {/* ── 맨 위: 한 장의 앨범 + 지금 열린 라운지 ── */}
      <section className="home-top" style={{ marginTop: 28 }}>
        {hero && (
          <div className="hero">
            <button className="hero__art" onClick={() => app.go({ page: "album", id: hero.id })} aria-label={`${hero.title} 열기`}>
              <Art src={hero.art} lift eager />
            </button>
            <div className="hero__body">
              <div className="row gap-10">
                <span className={`badge ${nowAlbum ? "badge--accent" : ""}`}>
                  {nowAlbum && <i className="lamp lamp--accent" style={{ boxShadow: "none" }} />}{heroKind}
                </span>
                {lastHeard && <span className="t-cap dimmer">{relativeDay(lastHeard)}</span>}
              </div>
              <h2 className="hero__title clip-2">{hero.title}</h2>
              <p className="hero__artist clip">
                <span className="link" onClick={() => app.go({ page: "artist", id: hero.artistId })}>{hero.artist}</span>
                {hero.year ? <span className="dimmer"> · {hero.year}</span> : null}
              </p>
              <div className="row gap-12" style={{ marginTop: 14 }}>
                <QualityTag quality={hero.quality} />
                <span className="q">{hero.tracks.length}곡 · {fmtLong(hero.durationMs)}</span>
              </div>
              <div className="row gap-8" style={{ marginTop: "auto", paddingTop: 22 }}>
                {nowAlbum ? (
                  <button className="btn btn--primary btn--lg" onClick={() => app.setNowPlayingOpen(true)}>지금 재생 화면 열기</button>
                ) : (
                  <button className="btn btn--primary btn--lg" onClick={() => app.playTracks(hero.trackIds, 0)}>
                    <Play size={17} fill="currentColor" />재생
                  </button>
                )}
                <button
                  className="btn btn--lg btn--line"
                  onClick={() => { const ids = [...hero.trackIds].sort(() => Math.random() - 0.5); app.playTracks(ids, 0) }}
                >
                  <Shuffle size={16} />셔플
                </button>
              </div>
            </div>
          </div>
        )}

        <div className="home-live">
          <div className="row" style={{ justifyContent: "space-between", marginBottom: 6 }}>
            <span className="row gap-8"><Radio size={15} style={{ color: "var(--live)" }} /><span className="t-h3">지금 열린 라운지</span></span>
            <button className="icon-btn icon-btn--sm" onClick={() => app.go({ page: "lounges" })} aria-label="라운지 모두 보기"><ArrowRight size={16} /></button>
          </div>
          {live.length === 0 && <p className="t-small dimmer" style={{ padding: "18px 0", lineHeight: 1.6 }}>열려 있는 라운지가 없습니다. 플레이어 바에서 직접 열어 보세요.</p>}
          {live.map((r) => (
            <button
              key={r.id}
              className="live-row"
              onClick={() => { void cmd.joinRoom(r.id).then(() => app.go({ page: "room" })) }}
              disabled={r.needsInvite}
              title={r.needsInvite ? "초대 코드가 필요한 라운지입니다" : `${r.name} 입장`}
            >
              <Avatar name={r.host} />
              <span className="grow">
                <span className="t-small clip" style={{ fontWeight: 620, display: "block" }}>{r.name}</span>
                <span className="t-cap dimmer clip" style={{ display: "block" }}>{r.host} · {badgeRes(r.badge) || (r.playing ? "재생 중" : "대기 중")}</span>
              </span>
              <span className="live-row__n">
                {r.needsInvite ? <Lock size={12} /> : <Users size={12} />}{r.members}
              </span>
            </button>
          ))}
        </div>
      </section>

      {/* ── 최근 추가 ── */}
      <section>
        <div className="sec-head">
          <h2 className="t-h2">새로 들어온 앨범</h2>
          <button className="more" onClick={() => app.go({ page: "library", tab: "albums" })}>모두 보기<ChevronRight size={15} /></button>
        </div>
        <div className="shelf">
          {added.slice(0, 6).map((a) => <AlbumCard key={a.id} album={a} />)}
        </div>
      </section>

      {/* ── 자주 듣는 곡 + 라이브러리 구성 ── */}
      <section className="home-split">
        <div>
          <div className="sec-head"><h2 className="t-h2">요즘 자주 듣는 곡</h2></div>
          {top.length === 0 ? (
            <p className="t-small dimmer">아직 재생 기록이 없습니다.</p>
          ) : (
            <div className="tlist" style={{ "--tcols": "30px minmax(0,1fr) 96px 62px 50px 30px" } as React.CSSProperties}>
              {top.map(({ track, plays }, i) => (
                <TrackRow
                  key={track.id}
                  track={track}
                  index={i + 1}
                  showArt
                  onPlay={() => app.playTracks(top.map((t) => t.track.id), i)}
                  tail={<span className="q" style={{ justifyContent: "flex-end" }}>{plays}회</span>}
                />
              ))}
            </div>
          )}
        </div>

        <div>
          <div className="sec-head"><h2 className="t-h2">내 라이브러리</h2></div>
          <div className="panel statcard">
            <div className="statcard__nums">
              <div><b className="num">{stats.albums.toLocaleString()}</b><span>앨범</span></div>
              <div><b className="num">{stats.tracks.toLocaleString()}</b><span>곡</span></div>
              <div><b className="num">{Math.round(stats.durationMs / 3600000).toLocaleString()}</b><span>시간</span></div>
              <div><b className="num">{stats.weekPlays.toLocaleString()}</b><span>이번 주 재생</span></div>
            </div>
            <div className="tierbar" role="img" aria-label="음질 구성">
              {tiers.map((t) => <i key={t} className={`tierbar__seg tierbar__seg--${t}`} style={{ flexGrow: stats.tiers[t] }} />)}
            </div>
            <div className="tierbar__legend">
              {tiers.map((t) => (
                <span key={t}>
                  <i className={`tierbar__seg--${t}`} />{TIER_LABEL[t]}
                  <b className="mono num">{Math.round((stats.tiers[t] / stats.tracks) * 100)}%</b>
                </span>
              ))}
            </div>
          </div>
        </div>
      </section>

      {/* ── 다시 듣기 ── */}
      {recent.length > 1 && (
        <section>
          <div className="sec-head"><h2 className="t-h2">최근에 들은 앨범</h2></div>
          <div className="shelf">
            {recent.slice(nowAlbum ? 0 : 1, nowAlbum ? 6 : 7).map((a) => <AlbumCard key={a.id} album={a} />)}
          </div>
        </section>
      )}
    </div>
  )
}
