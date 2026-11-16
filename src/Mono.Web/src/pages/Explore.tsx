import { useMemo, useState } from "react"
import { Check, Compass, Plus } from "lucide-react"
import { useApp } from "../state/app"
import { useMono } from "../state/MonoProvider"
import { StreamingProvider } from "../lib/protocol"
import { Empty } from "../ui"
import { AlbumCard, TrackHead, TrackRow } from "../ui/tracks"

export const SERVICES = [
  { id: "tidal" as const, provider: StreamingProvider.Tidal, name: "TIDAL", blurb: "HiRes FLAC 최대 24-bit / 192 kHz. 브라우저 로그인으로 연결합니다." },
  { id: "qobuz" as const, provider: StreamingProvider.Qobuz, name: "Qobuz", blurb: "스튜디오 마스터 최대 24-bit / 192 kHz. 앱 토큰으로 연결합니다." },
]

/** 서비스 로고 자리. 남의 로고를 흉내 내지 않고 머리글자 한 자만 쓴다. */
export function ServiceGlyph({ id, size = 40 }: { id: "tidal" | "qobuz"; size?: number }) {
  return (
    <span className={`svc-glyph svc-glyph--${id}`} style={{ width: size, height: size, fontSize: size * 0.42 }} aria-hidden>
      {id === "tidal" ? "T" : "Q"}
    </span>
  )
}

/**
 * 스트리밍에서 온 음악만 모아 본다.
 *
 * Core 는 스트리밍 카탈로그를 통째로 주지 않는다 — 연결할 때 가져온 것과, 검색할 때마다
 * 더 끌어온 것이 라이브러리에 섞여 들어올 뿐이다. 그래서 이 화면은 "추천"을 지어내지 않고,
 * 실제로 들어와 있는 스트리밍 음원을 서비스별로 보여 준다.
 */
export default function Explore() {
  const app = useApp()
  const { streamingAccounts, catalog } = useMono()
  const [service, setService] = useState<"all" | "tidal" | "qobuz">("all")

  const connected = SERVICES.filter((s) => streamingAccounts.some((a) => a.provider === s.provider && a.connected))
  const albums = useMemo(
    () => app.albums
      .filter((a) => a.source !== StreamingProvider.Local)
      .filter((a) => service === "all" || a.source === (service === "tidal" ? StreamingProvider.Tidal : StreamingProvider.Qobuz))
      .sort((a, b) => (b.addedAt ?? "").localeCompare(a.addedAt ?? "")),
    [app.albums, service],
  )
  const hires = albums.filter((a) => a.quality.tier === "hires" || a.quality.tier === "dsd")
  const tracks = useMemo(() => albums.flatMap((a) => a.tracks).slice(0, 8), [albums])
  const streamCount = catalog.filter((t) => t.source !== StreamingProvider.Local).length

  return (
    <div className="page">
      <section>
        <h1 className="t-h1">스트리밍</h1>
        <p className="t-body dim" style={{ marginTop: 8, maxWidth: 620, lineHeight: 1.6 }}>
          연결한 서비스의 음원이 로컬 라이브러리와 한 목록에서 같은 신호 경로로 재생됩니다. 검색하면 Core 가 서비스 카탈로그에서 더 찾아옵니다.
        </p>
      </section>

      <section className="svc-cards" style={{ marginTop: 28 }}>
        {SERVICES.map((s) => {
          const account = streamingAccounts.find((a) => a.provider === s.provider)
          const on = !!account?.connected
          return (
            <div key={s.id} className={`panel svc-card ${on ? "is-on" : ""}`}>
              <ServiceGlyph id={s.id} size={44} />
              <div className="grow">
                <div className="row gap-8">
                  <span className="t-h3" style={{ fontSize: 16 }}>{s.name}</span>
                  {on && <span className="badge badge--ok"><Check size={11} />연결됨</span>}
                </div>
                <p className="t-small dimmer" style={{ marginTop: 4, lineHeight: 1.5 }}>
                  {on ? `${account?.displayName ?? "계정"} · 가져온 곡 ${account?.imported ?? 0}개` : s.blurb}
                </p>
              </div>
              {on
                ? <button className="btn btn--sm btn--line" onClick={() => app.go({ page: "settings", tab: "accounts" })}>관리</button>
                : <button className="btn btn--sm btn--primary" onClick={() => app.openModal({ kind: "login", service: s.id })}><Plus size={14} />연결</button>}
            </div>
          )
        })}
      </section>

      {connected.length === 0 ? (
        <section>
          <Empty icon={<Compass size={40} strokeWidth={1.2} />} title="연결된 스트리밍 서비스가 없습니다">
            위에서 TIDAL 이나 Qobuz 를 연결하면, 스트리밍 음원도 비트 퍼펙트 경로로 재생하고 라운지에서 함께 들을 수 있습니다.
          </Empty>
        </section>
      ) : streamCount === 0 ? (
        <section>
          <Empty icon={<Compass size={40} strokeWidth={1.2} />} title="아직 가져온 스트리밍 음원이 없습니다">
            위 검색창에서 아티스트나 앨범을 찾아보세요. 찾은 음원이 여기에 모입니다.
          </Empty>
        </section>
      ) : (
        <>
          <section>
            <div className="sec-head">
              <h2 className="t-h2">스트리밍 앨범</h2>
              <div className="row gap-8">
                <button className={`chip ${service === "all" ? "is-on" : ""}`} onClick={() => setService("all")}>전체</button>
                {connected.map((s) => (
                  <button key={s.id} className={`chip ${service === s.id ? "is-on" : ""}`} onClick={() => setService(s.id)}>{s.name}</button>
                ))}
              </div>
            </div>
            {albums.length === 0
              ? <p className="t-small dimmer">이 서비스에서 가져온 앨범이 없습니다.</p>
              : <div className="grid">{albums.map((a) => <AlbumCard key={a.id} album={a} />)}</div>}
          </section>

          {hires.length > 0 && hires.length < albums.length && (
            <section>
              <div className="sec-head"><h2 className="t-h2">Hi-Res 로 들을 수 있는 앨범</h2></div>
              <div className="shelf">{hires.slice(0, 6).map((a) => <AlbumCard key={a.id} album={a} />)}</div>
            </section>
          )}

          {tracks.length > 0 && (
            <section>
              <div className="sec-head"><h2 className="t-h2">최근 가져온 곡</h2></div>
              <div className="tlist">
                <TrackHead cols={["#", "제목", "앨범", "음질", "길이", null]} />
                {tracks.map((t, i) => (
                  <TrackRow key={t.id} track={t} index={i + 1} showArt showAlbum onPlay={() => app.playTracks(tracks.map((x) => x.id), i)} />
                ))}
              </div>
            </section>
          )}
        </>
      )}
    </div>
  )
}
