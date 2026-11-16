import { useEffect, useState, type CSSProperties } from "react"
import { ChevronDown, Mic2 } from "lucide-react"
import { useApp } from "../state/app"
import { useMono, useMonoCommands } from "../state/MonoProvider"
import { fmtTime, qualityOf, sourceName } from "../lib/model"
import { Art, Empty, Eq, QualityTag } from "../ui"
import { Credits, useArtTint, usePresence } from "../ui/motion"
import { LyricLines } from "./Dock"
import { signalSummary } from "./PlayerBar"

type Tab = "lyrics" | "notes" | "album"

/**
 * 전체 화면 재생. 왼쪽은 커버 한 장, 오른쪽은 읽을거리.
 *
 * 테마와 상관없이 늘 어두운 캔버스다 — 커버와 가사가 가장 잘 보이는 바탕이기 때문이다.
 * 하단 바에서 위로 펼쳐지고(260ms), 커버에서 뽑은 색이 배경 위쪽에만 번진다(400ms).
 * 플레이어 바는 그대로 아래에 남는다. 조작부를 두 벌 만들지 않는다.
 */
export default function NowPlaying() {
  const app = useApp()
  const { room } = useMono()
  const cmd = useMonoCommands()
  const track = room?.currentTrack ?? null
  const hasLyrics = (room?.lyrics.length ?? 0) > 0
  const [tab, setTab] = useState<Tab>("lyrics")
  const { mounted, shown } = usePresence(app.nowPlayingOpen && !!track, 260)
  const tint = useArtTint(track?.artUrl, true)

  useEffect(() => {
    if (!app.nowPlayingOpen) return
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") app.setNowPlayingOpen(false) }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [app])

  // 곡이 사라지면(대기열이 끝나면) 화면도 닫는다.
  useEffect(() => { if (!track && app.nowPlayingOpen) app.setNowPlayingOpen(false) }, [track, app])

  if (!mounted || !room || !track) return null

  const signal = signalSummary(room)
  const q = qualityOf(track)
  const leave = (fn: () => void) => { app.setNowPlayingOpen(false); fn() }

  return (
    <div className={`np-full ${shown ? "is-open" : ""}`} data-theme="dark" style={{ "--tint": tint } as CSSProperties}>
      <div className="np-full__tint" aria-hidden />

      <div className="np-full__top">
        <button className="icon-btn icon-btn--solid" onClick={() => app.setNowPlayingOpen(false)} aria-label="닫기"><ChevronDown size={20} /></button>
        <span className="label" style={{ marginLeft: 6 }}>
          {app.role === "host" || app.role === "guest" ? `라운지 · ${room.name}` : "지금 재생"}
        </span>
      </div>

      <div className="np-full__stage">
        <Art src={track.artUrl} className="np-full__art" eager />
        <div style={{ marginTop: 34, maxWidth: 560 }}>
          <h1 className="t-display clip-2" style={{ fontSize: 40 }}>{track.title}</h1>
          <p className="np-full__by">
            <span className="link" onClick={() => leave(() => track.artistId && app.go({ page: "artist", id: track.artistId }))}>{track.artistName}</span>
            {track.albumTitle && (
              <>
                <span className="dimmer"> · </span>
                <span className="link" onClick={() => leave(() => track.albumId && app.go({ page: "album", id: track.albumId }))}>{track.albumTitle}</span>
              </>
            )}
          </p>
          <div className="row gap-10" style={{ marginTop: 20, flexWrap: "wrap" }}>
            <QualityTag quality={q} pill />
            <span className={`badge ${signal.lamp ? `badge--${signal.lamp}` : ""}`}>
              <i className={`lamp ${signal.lamp ? `lamp--${signal.lamp}` : ""}`} style={{ boxShadow: "none" }} />{signal.text}
            </span>
            <span className="badge">{track.hasLocal ? "로컬" : sourceName(track.source)}</span>
            {room.outputs[0]?.device && <span className="badge">→ {room.outputs[0].device}</span>}
          </div>
        </div>
      </div>

      <div className="np-full__side">
        <div className="tabs" style={{ borderBottomColor: "transparent" }}>
          <button className={`tab ${tab === "lyrics" ? "is-on" : ""}`} onClick={() => setTab("lyrics")}>가사</button>
          <button className={`tab ${tab === "notes" ? "is-on" : ""}`} onClick={() => setTab("notes")}>라이너 노트</button>
          <button className={`tab ${tab === "album" ? "is-on" : ""}`} onClick={() => setTab("album")}>
            수록곡<span className="count">{room.albumTracks.length}</span>
          </button>
        </div>

        <div className="np-full__scroll">
          {tab === "lyrics" && (
            hasLyrics
              ? <LyricLines lines={room.lyrics} big />
              : <Empty icon={<Mic2 size={36} strokeWidth={1.2} />} title="가사가 없는 곡입니다">같은 폴더의 .lrc 파일이나 태그에 든 가사를 읽어 옵니다.</Empty>
          )}

          {tab === "notes" && (
            <div className="col gap-24" style={{ maxWidth: 560 }}>
              {room.linerNotes
                ? <p className="prose" style={{ fontSize: 17, color: "var(--fg-0)" }}>{room.linerNotes}</p>
                : <p className="prose">이 앨범에는 라이너 노트가 없습니다.</p>}
              <dl className="kv">
                {room.album?.label && <><dt>레이블</dt><dd>{room.album.label}</dd></>}
                {room.album?.year && <><dt>발매</dt><dd>{room.album.year}</dd></>}
                <dt>규격</dt><dd className="mono" style={{ fontSize: 12.5 }}>{[q.container, q.res].filter(Boolean).join(" ")}</dd>
              </dl>
              {room.credits && (
                <div>
                  <div className="label" style={{ marginBottom: 10 }}>크레딧</div>
                  <Credits text={room.credits} />
                </div>
              )}
              {room.artist?.bio && (
                <div>
                  <div className="label" style={{ marginBottom: 10 }}>{room.artist.name}</div>
                  <p className="prose">{room.artist.bio}</p>
                </div>
              )}
            </div>
          )}

          {tab === "album" && (
            <div className="tlist" style={{ "--tcols": "32px minmax(0,1fr) 52px" } as CSSProperties}>
              {room.albumTracks.map((t, i) => {
                const isCurrent = t.id === track.id
                return (
                  <div
                    key={t.id}
                    className={`trow trow--tight ${isCurrent ? "is-current" : ""}`}
                    onDoubleClick={() => app.playTracks(room.albumTracks.map((x) => x.id), i)}
                  >
                    <div className="trow__idx">{isCurrent ? <Eq paused={!room.playing} /> : <span>{i + 1}</span>}</div>
                    <div className="trow__title clip">{t.title}</div>
                    <div className="trow__time num">{fmtTime(t.durationMs)}</div>
                  </div>
                )
              })}
              {app.canControl && room.albumTracks.length > 0 && (
                <button
                  className="btn btn--sm btn--line"
                  style={{ alignSelf: "flex-start", marginTop: 16 }}
                  onClick={() => { cmd.enqueueMany(room.albumTracks.filter((t) => t.id !== track.id).map((t) => t.id)); app.toast("앨범의 나머지 곡을 대기열에 넣었습니다") }}
                >
                  나머지 곡 대기열에 추가
                </button>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
