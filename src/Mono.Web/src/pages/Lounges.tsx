import { useEffect, useMemo, useState, type CSSProperties } from "react"
import { ListPlus, Lock, Radio } from "lucide-react"
import { useApp } from "../state/app"
import { useMono, useMonoCommands } from "../state/MonoProvider"
import { hueOf, mostPlayed, ROOM_MODE_LABEL } from "../lib/model"
import { RoomMode, type RoomListing } from "../lib/protocol"
import { Art, Avatar, Empty, Modal } from "../ui"
import { TrackRow } from "../ui/tracks"

/**
 * Core 의 경로 배지("24/96000 · Bit-perfect Local")에서 해상도만 뽑아 "24/96" 으로 줄인다.
 * 목록에는 이 한 줄로 충분하고, 나머지는 방에 들어가면 신호 경로에서 보인다.
 */
export function badgeRes(badge: string | null | undefined): string {
  if (!badge) return ""
  const dsd = badge.match(/^DSD\s?(\d+)/i)
  if (dsd) return `DSD${dsd[1]}`
  const pcm = badge.match(/^(\d+)\s*\/\s*(\d+)/)
  if (!pcm) return ""
  const rate = Number(pcm[2])
  return `${pcm[1]}/${rate >= 1000 ? Math.round(rate / 100) / 10 : rate}`
}

// ── 표지 ────────────────────────────────────────────────────────────────────

export interface LoungePeek {
  /** 지금 곡의 커버부터, 그 뒤로 이어지는 서로 다른 앨범의 커버까지 최대 셋. */
  covers: string[]
  /** 지금 곡 뒤에 남은 곡 수. */
  upcoming: number
  title: string | null
  artist: string | null
}

/**
 * 목록 카드에 쓸 요약. Core 가 list_rooms 에 실어 준다(covers · nowTitle · nowArtist · upcoming).
 * 같은 앨범이 이어질 때 커버를 걸러 내는 일도 Core 가 한다.
 * 이 필드를 모르는 예전 Core 에서는 비어 있고, 그때는 호스트 패턴 표지로 그린다.
 */
export function peekOf(room: RoomListing): LoungePeek {
  return {
    covers: room.covers ?? [],
    upcoming: room.upcoming ?? 0,
    title: room.nowTitle ?? null,
    artist: room.nowArtist ?? null,
  }
}

/**
 * 라운지 표지.
 *
 * 곡이 걸려 있으면 지금 곡과 다음 두 곡의 커버를 벤토로 묶는다. 표지는 3:2 이고
 * 지금 곡이 왼쪽에 정사각형으로 크게, 다음 두 곡이 오른쪽에 위아래로 놓인다 — 세 칸 모두
 * 정사각형이라 커버가 잘리지 않는다. 서로 다른 커버가 셋이 안 되면 남은 칸에 남은 곡 수를 적는다.
 * 대기 중이면 호스트 이름에서 뽑은 두 색의 패턴 위에 호스트 아바타를 얹는다.
 * 오른쪽 아래에는 방송 상태 · 인원 · 음질이 늘 같은 자리에 붙는다.
 */
export function LoungeCover({ room, peek, className = "" }: { room: RoomListing; peek?: LoungePeek; className?: string }) {
  const covers = peek?.covers ?? []
  const res = badgeRes(room.badge)
  const hue = hueOf(room.host)
  return (
    <div className={`lcover ${className}`}>
      {covers.length > 0 ? (
        <div className={`bento bento--${covers.length}`}>
          {covers.map((src) => <img key={src} src={src} alt="" loading="lazy" draggable={false} />)}
          {covers.length < 3 && (
            <div className="bento__more">{peek && peek.upcoming > 0 ? <>다음<b className="mono">{peek.upcoming}</b>곡</> : "마지막 곡"}</div>
          )}
        </div>
      ) : (
        <div className={`duotone duotone--${hue % 3}`} style={{ "--h": hue } as CSSProperties}>
          <Avatar name={room.host} size="lg" />
        </div>
      )}
      <div className="lcover__meta">
        {room.playing
          ? <span className="badge badge--onair"><i className="lamp" />On Air</span>
          : <span className="badge badge--dark">대기 중</span>}
        <span className="badge badge--dark">{room.needsInvite && <Lock size={10} />}{room.members}명</span>
        {res && <span className="badge badge--dark mono">{res}</span>}
      </div>
    </div>
  )
}

// ── 화면 ────────────────────────────────────────────────────────────────────

type Filter = "all" | "playing" | "open" | "invite"

export default function Lounges() {
  const app = useApp()
  const { rooms, room, archives, history } = useMono()
  const cmd = useMonoCommands()
  const [filter, setFilter] = useState<Filter>("all")
  const [invite, setInvite] = useState<RoomListing | null>(null)
  const [code, setCode] = useState("")

  // 목록은 Core 가 밀어 주지 않는다. 화면에 있는 동안 가끔 다시 묻는다.
  useEffect(() => {
    void cmd.refreshRooms()
    const id = setInterval(() => { void cmd.refreshRooms() }, 8000)
    return () => clearInterval(id)
  }, [cmd])

  const sorted = useMemo(() => [...rooms].sort((a, b) => b.members - a.members), [rooms])
  const featured = sorted.find((r) => r.playing && !r.needsInvite) ?? sorted[0]
  const rest = sorted.filter((r) => r !== featured).filter((r) =>
    filter === "all" ? true : filter === "playing" ? r.playing : filter === "invite" ? r.needsInvite : !r.needsInvite,
  )
  const top = useMemo(() => mostPlayed(history.filter((h) => h.roomId), app.trackById, 5), [history, app.trackById])
  const topAny = top.length > 0 ? top : mostPlayed(history, app.trackById, 5)
  const inLounge = app.role === "host" || app.role === "guest"

  const join = (r: RoomListing, inviteCode?: string) => {
    const enter = () => { void cmd.joinRoom(r.id, inviteCode).then(() => app.go({ page: "room" })) }
    if (room?.id === r.id) { app.go({ page: "room" }); return }
    if (app.role === "host") {
      app.openModal({
        kind: "confirm", title: "방송 중인 라운지를 닫을까요?",
        body: "다른 라운지에 들어가면 지금 열어 둔 내 라운지는 끝납니다.",
        confirmLabel: "닫고 입장", danger: true, onConfirm: () => { void cmd.closeRoom().then(enter) },
      })
      return
    }
    enter()
  }
  const tryJoin = (r: RoomListing) => {
    if (r.needsInvite && room?.id !== r.id) { setCode(""); setInvite(r); return }
    join(r)
  }
  const nowLine = (r: RoomListing) => {
    const p = peekOf(r)
    return p?.title ? [p.title, p.artist].filter(Boolean).join(" · ") : r.playing ? "재생 중" : "호스트가 곡을 고르는 중"
  }

  return (
    <div className="page">
      <section className="row" style={{ alignItems: "flex-end", gap: 24 }}>
        <div className="grow">
          <h1 className="t-h1">라운지</h1>
          <p className="t-body dim" style={{ marginTop: 8 }}>같은 곡을, 같은 순간에, 같은 음질로. 지금 {rooms.length}개의 방이 열려 있습니다.</p>
        </div>
        {inLounge ? (
          <button className="btn btn--lg btn--line" onClick={() => app.go({ page: "room" })}><Radio size={16} />내가 있는 라운지로</button>
        ) : (
          <button className="btn btn--lg btn--primary" onClick={() => app.openModal({ kind: "host" })}><Radio size={16} />라운지 열기</button>
        )}
      </section>

      {rooms.length === 0 ? (
        <section>
          <Empty
            icon={<Radio size={40} strokeWidth={1.2} />}
            title="열려 있는 라운지가 없습니다"
            action={!inLounge && <button className="btn btn--primary" onClick={() => app.openModal({ kind: "host" })}>첫 라운지 열기</button>}
          >
            듣던 곡 그대로 방을 열면, 들어온 사람이 같은 시각에 맞춰 함께 듣습니다.
          </Empty>
        </section>
      ) : (
        <>
          {featured && (
            <section style={{ marginTop: 28 }}>
              <div className="lounge-feature">
                <LoungeCover room={featured} peek={peekOf(featured)} className="lounge-feature__art" />
                <div className="lounge-feature__body">
                  <span className="label">가장 많이 모인 방{featured.mode !== RoomMode.Open ? ` · ${ROOM_MODE_LABEL[featured.mode]}` : ""}</span>
                  <h2 className="t-display clip-2" style={{ fontSize: 40, marginTop: 12 }}>{featured.name}</h2>
                  <div className="row gap-10" style={{ marginTop: 16 }}>
                    <Avatar name={featured.host} host />
                    <span className="t-body"><span className="dim">호스트</span> {featured.host}</span>
                  </div>
                  <dl className="kv" style={{ marginTop: 22, gridTemplateColumns: "72px minmax(0,1fr)" }}>
                    <dt>지금 곡</dt><dd className="clip">{nowLine(featured)}</dd>
                    <dt>셋리스트</dt><dd>{featured.queued}곡</dd>
                  </dl>
                  <div className="row gap-8" style={{ marginTop: "auto", paddingTop: 22 }}>
                    <button className="btn btn--primary btn--lg" onClick={() => tryJoin(featured)}>
                      {room?.id === featured.id ? "라운지로 돌아가기" : "입장하기"}
                    </button>
                  </div>
                </div>
              </div>
            </section>
          )}

          <section>
            <div className="sec-head">
              <h2 className="t-h2">열려 있는 방</h2>
              <div className="row gap-8">
                {([["all", "전체"], ["playing", "재생 중"], ["open", "공개"], ["invite", "초대 전용"]] as [Filter, string][]).map(([id, label]) => (
                  <button key={id} className={`chip ${filter === id ? "is-on" : ""}`} onClick={() => setFilter(id)}>{label}</button>
                ))}
              </div>
            </div>
            {rest.length === 0 ? (
              <p className="t-small dimmer">이 조건에 맞는 방이 없습니다.</p>
            ) : (
              <div className="lounge-grid">
                {rest.map((r) => (
                  <button key={r.id} className="lounge-card" onClick={() => tryJoin(r)}>
                    <LoungeCover room={r} peek={peekOf(r)} className="lounge-card__art" />
                    <div className="lounge-card__body">
                      <div className="t-h3 clip" style={{ fontSize: 15.5 }}>{r.name}</div>
                      <div className="t-small dim clip" style={{ marginTop: 3 }}>
                        {r.host}{r.mode !== RoomMode.Open ? ` · ${ROOM_MODE_LABEL[r.mode]}` : ""}
                      </div>
                      <div className="t-cap dimmer clip" style={{ marginTop: 9 }}>{nowLine(r)}</div>
                    </div>
                  </button>
                ))}
              </div>
            )}
          </section>
        </>
      )}

      <section className="home-split">
        <div>
          <div className="sec-head"><h2 className="t-h2">지난 세션</h2></div>
          {archives.length === 0 ? (
            <p className="t-small dimmer">끝난 라운지 세션이 여기 기록으로 남습니다.</p>
          ) : (
            <div className="rows">
              {archives.map((a) => {
                const first = a.tracks[0] && app.trackById.get(a.tracks[0].id)
                return (
                  <div key={a.id} className="row-card">
                    <Art src={first?.artUrl} size={56} />
                    <div className="grow">
                      <div className="t-h3 clip">{a.roomName}</div>
                      <div className="t-small dimmer clip">
                        {new Date(a.endedAt ?? a.startedAt).toLocaleDateString("ko-KR", { month: "long", day: "numeric" })} · {a.tracks.length}곡 · {a.participants.length}명 참여{a.pins > 0 ? ` · 핀 ${a.pins}` : ""}
                      </div>
                    </div>
                    <button
                      className="btn btn--sm btn--line"
                      onClick={() => {
                        void cmd.request({ type: "create_playlist", archiveId: a.id }, "create_playlist")
                          .then(() => { void cmd.refreshPlaylists(); app.toast("세션을 플레이리스트로 저장했습니다") })
                          .catch(() => app.toast("세션을 불러오지 못했습니다", true))
                      }}
                    >
                      <ListPlus size={14} />플레이리스트로
                    </button>
                  </div>
                )
              })}
            </div>
          )}
        </div>
        <div>
          <div className="sec-head"><h2 className="t-h2">라운지에서 많이 들은 곡</h2></div>
          <div className="tlist" style={{ "--tcols": "30px minmax(0,1fr) 62px 50px 30px" } as CSSProperties}>
            {topAny.map(({ track, plays }, i) => (
              <TrackRow
                key={track.id} track={track} index={i + 1} showArt showQuality={false}
                onPlay={() => app.playTracks(topAny.map((t) => t.track.id), i)}
                tail={<span className="q" style={{ justifyContent: "flex-end" }}>{plays}회</span>}
              />
            ))}
          </div>
        </div>
      </section>

      {invite && (
        <Modal
          title="초대 코드 입력"
          subtitle={<>「{invite.name}」은 초대 전용 라운지입니다. 호스트에게 받은 코드를 넣어 주세요.</>}
          onClose={() => setInvite(null)}
          width={420}
          footer={
            <>
              <button className="btn btn--quiet" onClick={() => setInvite(null)}>취소</button>
              <button className="btn btn--primary" disabled={!code.trim()} onClick={() => { const r = invite; setInvite(null); join(r, code.trim()) }}>입장</button>
            </>
          }
        >
          <input
            className="input mono" value={code} onChange={(e) => setCode(e.target.value.toUpperCase())} placeholder="MONO-XXXX" autoFocus
            onKeyDown={(e) => { if (e.key === "Enter" && code.trim()) { const r = invite; setInvite(null); join(r, code.trim()) } }}
          />
        </Modal>
      )}
    </div>
  )
}
