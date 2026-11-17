import { useEffect, useMemo, useRef, useState, type CSSProperties } from "react"
import { Link2, LogOut, MapPin, Plus, Radio, Search, Send, Square, X } from "lucide-react"
import { useApp } from "../state/app"
import { useMediaClock, useMono, useMonoCommands } from "../state/MonoProvider"
import { fmtTime, qualityOf, ROOM_MODE_LABEL } from "../lib/model"
import { MemberRole, RoomMode } from "../lib/protocol"
import { Art, Avatar, Empty, Eq, QualityTag, Slider, fromButton } from "../ui"
import { Tint } from "../ui/motion"
import { signalSummary } from "../shell/PlayerBar"

type Tab = "setlist" | "people" | "pins"

export default function Room() {
  const app = useApp()
  const { room, peerId, catalog } = useMono()
  const cmd = useMonoCommands()
  const { positionMs, durationMs } = useMediaClock()
  const [tab, setTab] = useState<Tab>("setlist")
  const [cue, setCue] = useState("")
  const [pin, setPin] = useState("")
  const [text, setText] = useState("")
  const chatRef = useRef<HTMLDivElement>(null)
  const isHost = app.role === "host"
  const inLounge = app.role === "host" || app.role === "guest"

  // 새 말이 오면 맨 아래로. 위로 올려 읽고 있을 때는 끌어내리지 않는다.
  const chatCount = room?.chat.length ?? 0
  useEffect(() => {
    const el = chatRef.current
    if (!el) return
    if (el.scrollHeight - el.scrollTop - el.clientHeight < 160) el.scrollTop = el.scrollHeight
  }, [chatCount])
  useEffect(() => { if (chatRef.current) chatRef.current.scrollTop = chatRef.current.scrollHeight }, [room?.id])

  const hits = useMemo(() => {
    const q = cue.trim().toLowerCase()
    if (q.length < 2) return []
    return catalog.filter((t) => `${t.title} ${t.artist} ${t.album}`.toLowerCase().includes(q)).slice(0, 6)
  }, [cue, catalog])

  if (!room || !inLounge) {
    return (
      <div className="page">
        <Empty icon={<Radio size={40} strokeWidth={1.2} />} title="라운지에 들어와 있지 않습니다"
          action={<button className="btn btn--primary" onClick={() => app.go({ page: "lounges" })}>라운지 둘러보기</button>} />
      </div>
    )
  }

  const track = room.currentTrack
  const people = room.members.filter((m) => !m.isOutput)
  const hostName = (room.hostPeerId && room.peers[room.hostPeerId]) || "호스트"
  const signal = signalSummary(room)
  const canSeek = isHost && durationMs > 0
  const progress = durationMs > 0 ? positionMs / durationMs : 0

  const leave = () => {
    if (isHost) {
      app.openModal({
        kind: "confirm", title: "방송을 끝낼까요?",
        body: `함께 듣던 ${Math.max(0, people.length - 1)}명의 연결이 끊깁니다. 듣던 곡과 대기열은 내 플레이어에 그대로 남습니다.`,
        confirmLabel: "방송 종료", danger: true,
        onConfirm: () => { void cmd.closeRoom().then(() => { app.go({ page: "lounges" }); app.toast("방송을 끝냈습니다") }) },
      })
    } else {
      cmd.leaveRoom()
      app.go({ page: "lounges" })
    }
  }

  const send = () => {
    if (!text.trim()) return
    cmd.chat(text)
    setText("")
  }

  return (
    <div className="room">
      <div className="room__main">
        <Tint src={track?.artUrl} />

        {/* ── 머리 ── */}
        <header className="room__head">
          <div className="grow">
            <div className="row gap-8">
              {isHost
                ? <span className={`badge badge--onair ${app.onAirFresh ? "is-fresh" : ""}`}><i className="lamp" />On Air</span>
                : <span className="badge badge--live"><i className="lamp lamp--live" style={{ boxShadow: "none" }} />Live</span>}
              <span className="badge">{ROOM_MODE_LABEL[room.mode]}</span>
              <span className="badge">{people.length}명</span>
            </div>
            <h1 className="t-h1 clip" style={{ marginTop: 12 }}>{room.name}</h1>
            <p className="t-small dim" style={{ marginTop: 5 }}>
              호스트 {hostName}{isHost ? " (나)" : ""} · {new Date(room.startedAt).toLocaleTimeString("ko-KR", { hour: "numeric", minute: "2-digit" })}부터
            </p>
          </div>
          <button className="btn btn--line" onClick={() => app.openModal({ kind: "share" })}><Link2 size={15} />초대</button>
          <button className="btn btn--danger" onClick={leave}>
            {isHost ? <><Square size={13} fill="currentColor" />방송 종료</> : <><LogOut size={15} />나가기</>}
          </button>
        </header>

        {/* ── 무대 ── */}
        <section className="stage">
          <Art src={track?.artUrl} className="stage__art" lift eager />
          <div className="stage__body">
            {track ? (
              <>
                <span className="label">지금 함께 듣는 곡</span>
                <h2 className="stage__title clip-2">{track.title}</h2>
                <p className="t-h2 dim clip" style={{ fontWeight: 520, marginTop: 6 }}>
                  <span className="link" onClick={() => track.artistId && app.go({ page: "artist", id: track.artistId })}>{track.artistName}</span>
                  {track.albumTitle ? <span className="dimmer"> · {track.albumTitle}</span> : null}
                </p>
                <div className="row gap-10" style={{ marginTop: 16, flexWrap: "wrap" }}>
                  <QualityTag quality={qualityOf(track)} pill />
                  <span className={`badge ${signal.lamp ? `badge--${signal.lamp}` : ""}`}>
                    <i className={`lamp ${signal.lamp ? `lamp--${signal.lamp}` : ""}`} style={{ boxShadow: "none" }} />{signal.text}
                  </span>
                  <span className="badge badge--ok"><i className="lamp lamp--ok" style={{ boxShadow: "none" }} />동기화 잠김</span>
                </div>
                <div className="stage__scrub">
                  <span className="mono num">{fmtTime(positionMs)}</span>
                  <Slider value={progress} disabled={!canSeek} accent label="재생 위치" onCommit={(v) => cmd.seek(v * durationMs)} />
                  <span className="mono num">{fmtTime(durationMs)}</span>
                </div>
              </>
            ) : (
              <>
                <span className="label">대기 중</span>
                <h2 className="stage__title">{isHost ? "첫 곡을 골라 주세요" : "호스트가 곡을 고르는 중입니다"}</h2>
                <p className="t-body dim" style={{ marginTop: 8 }}>
                  {isHost ? "아래 셋리스트에 곡을 넣거나, 라이브러리에서 아무 앨범이나 재생하면 방송이 시작됩니다." : "곡이 시작되면 같은 시각에 맞춰 자동으로 재생됩니다."}
                </p>
              </>
            )}
            <div className="reactions">
              {room.allowedReactionEmoji.map((emoji) => (
                <button key={emoji} className="reaction" onClick={() => cmd.react(emoji)} disabled={!track} aria-label={`${emoji} 반응`}>
                  <span>{emoji}</span>
                  <b className="mono num">{room.reactionCounts[emoji] ?? 0}</b>
                </button>
              ))}
            </div>
          </div>
        </section>

        {/* ── 셋리스트 · 사람 · 핀 ── */}
        <section>
          <div className="tabs">
            <button className={`tab ${tab === "setlist" ? "is-on" : ""}`} onClick={() => setTab("setlist")}>셋리스트<span className="count">{room.queue.length}</span></button>
            <button className={`tab ${tab === "people" ? "is-on" : ""}`} onClick={() => setTab("people")}>함께 듣는 사람<span className="count">{people.length}</span></button>
            <button className={`tab ${tab === "pins" ? "is-on" : ""}`} onClick={() => setTab("pins")}>핀<span className="count">{room.pins.length}</span></button>
          </div>

          {tab === "setlist" && (
            <div style={{ marginTop: 16 }}>
              {isHost && (
                <div className="cue">
                  <label className="finder finder--wide">
                    <Search size={15} />
                    <input value={cue} onChange={(e) => setCue(e.target.value)} placeholder="셋리스트에 넣을 곡 찾기" spellCheck={false} />
                    {cue && <button className="icon-btn icon-btn--sm" onClick={() => setCue("")} aria-label="지우기"><X size={14} /></button>}
                  </label>
                  {hits.length > 0 && (
                    <div className="cue__hits">
                      {hits.map((t) => (
                        <button key={t.id} className="autoplay__pick" onClick={() => { cmd.enqueue(t.id); app.toast(`셋리스트에 추가: ${t.title}`) }}>
                          <Art src={t.artUrl} size={36} />
                          <span className="grow">
                            <span className="qrow__title clip" style={{ display: "block" }}>{t.title}</span>
                            <span className="qrow__sub clip" style={{ display: "block" }}>{t.artist} · {t.album}</span>
                          </span>
                          <QualityTag of={t} />
                          <Plus size={16} style={{ color: "var(--fg-2)" }} />
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              )}
              {room.queue.length === 0 ? (
                <p className="t-small dimmer" style={{ padding: "20px 4px" }}>셋리스트가 비어 있습니다.</p>
              ) : (
                <div className="tlist" style={{ "--tcols": "30px minmax(0,1fr) 120px 50px 30px" } as CSSProperties}>
                  {room.queue.map((q, i) => {
                    const isCurrent = i === room.queueIndex
                    return (
                      <div
                        key={q.id}
                        className={`trow ${isCurrent ? "is-current" : ""}`}
                        style={i < room.queueIndex ? { opacity: 0.5 } : undefined}
                        onDoubleClick={(e) => {
                          // X 를 연달아 눌러 곡을 빼는 것이 더블클릭으로 묶여 다음 곡을 틀지 않게 한다.
                          if (fromButton(e)) return
                          if (isHost && !isCurrent) cmd.jumpTo(i, q.id)
                        }}
                      >
                        <div className="trow__idx">{isCurrent ? <Eq paused={!room.playing} /> : <span>{i + 1}</span>}</div>
                        <div className="trow__main">
                          <Art src={q.artUrl} size={38} />
                          <div className="grow">
                            <div className="trow__title clip">{q.title}</div>
                            <div className="trow__sub clip">{q.artist}</div>
                          </div>
                        </div>
                        <div className="trow__cell clip dimmer" style={{ fontSize: 12.5 }}>{room.peers[q.addedByPeerId] ?? hostName} 추가</div>
                        <div className="trow__time num">{fmtTime(q.durationMs)}</div>
                        {isHost && !isCurrent ? (
                          <button className="icon-btn icon-btn--sm trow__more" aria-label="셋리스트에서 빼기" onClick={() => cmd.removeFromQueue(i, q.id)}><X size={15} /></button>
                        ) : <span />}
                      </div>
                    )
                  })}
                </div>
              )}
            </div>
          )}

          {tab === "people" && (
            <div className="people">
              {people.map((m) => (
                <div key={m.peerId} className="person">
                  <Avatar name={m.name ?? "?"} host={m.role === MemberRole.Host} />
                  <div className="grow">
                    <div className="t-small clip" style={{ fontWeight: 600 }}>{m.name}{m.peerId === peerId ? " (나)" : ""}</div>
                    <div className="t-cap dimmer">
                      {m.role === MemberRole.Host ? "호스트" : m.role === MemberRole.CoHost ? "공동 호스트" : "청취자"}
                      {m.stats?.rttMs != null ? <span className="mono"> · {Math.round(m.stats.rttMs)} ms</span> : null}
                    </div>
                  </div>
                  {m.stats?.locked && <i className="lamp lamp--ok" title="클럭 잠김" />}
                </div>
              ))}
            </div>
          )}

          {tab === "pins" && (
            <div style={{ marginTop: 16 }}>
              {room.commentsAllowed && track && (
                <div className="row gap-8" style={{ marginBottom: 14 }}>
                  <input
                    className="input" value={pin} onChange={(e) => setPin(e.target.value)} maxLength={120}
                    placeholder={`${fmtTime(positionMs)} 지점에 메모 남기기`}
                    onKeyDown={(e) => { if (e.key === "Enter" && pin.trim()) { cmd.pin(pin); setPin("") } }}
                  />
                  <button className="btn btn--line" style={{ flex: "none" }} disabled={!pin.trim()} onClick={() => { cmd.pin(pin); setPin("") }}><MapPin size={15} />핀 꽂기</button>
                </div>
              )}
              {room.pins.length === 0 ? (
                <p className="t-small dimmer" style={{ padding: "8px 4px" }}>곡의 특정 순간에 메모를 남기면, 함께 듣는 모두가 그 자리에서 봅니다.</p>
              ) : (
                <div className="rows">
                  {room.pins.map((p) => (
                    <div key={p.id} className="row-card" style={{ padding: "10px 14px" }}>
                      <button className="badge badge--accent mono" disabled={!canSeek || !p.onCurrentTrack} onClick={() => cmd.seek(p.mediaTimeMs)}>{fmtTime(p.mediaTimeMs)}</button>
                      <div className="grow">
                        <div className="t-small selectable">{p.text}</div>
                        <div className="t-cap dimmer">{p.peerName}{p.onCurrentTrack ? "" : " · 다른 곡"}</div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}
        </section>
      </div>

      {/* ── 채팅 ── */}
      <aside className="room__chat">
        <div className="chat__head">
          <span className="t-h3">채팅</span>
          <span className="stack">
            {people.slice(0, 5).map((m) => <Avatar key={m.peerId} name={m.name ?? "?"} size="sm" />)}
            {people.length > 5 && <span className="avatar avatar--sm">+{people.length - 5}</span>}
          </span>
        </div>
        <div className="chat__list" ref={chatRef}>
          {room.chat.length === 0 && <p className="t-small dimmer" style={{ textAlign: "center", padding: "40px 20px", lineHeight: 1.6 }}>아직 조용합니다.<br />첫 마디를 남겨 보세요.</p>}
          {room.chat.map((line, i) => {
            const mine = line.peerId === peerId
            const fromHost = line.peerId === room.hostPeerId
            return (
              <div key={i} className={`msg ${mine ? "msg--mine" : ""}`}>
                {!mine && <Avatar name={line.peerName ?? "?"} size="sm" host={fromHost} />}
                <div className="msg__body">
                  {!mine && (
                    <div className="msg__who">
                      {line.peerName}{fromHost && <span className="accent"> · 호스트</span>}
                      <span className="mono">{new Date(line.at).toLocaleTimeString("ko-KR", { hour: "2-digit", minute: "2-digit", hour12: false })}</span>
                    </div>
                  )}
                  <div className="msg__text selectable">{line.text}</div>
                </div>
              </div>
            )
          })}
        </div>
        <div className="chat__input">
          <input
            className="input" value={text} onChange={(e) => setText(e.target.value)} maxLength={300}
            disabled={!room.commentsAllowed}
            placeholder={room.commentsAllowed ? "메시지 보내기" : "호스트가 채팅을 꺼 두었습니다"}
            onKeyDown={(e) => { if (e.key === "Enter") send() }}
          />
          <button className="play-btn" style={{ width: 38, height: 38 }} onClick={send} disabled={!text.trim()} aria-label="보내기"><Send size={16} /></button>
        </div>
      </aside>
    </div>
  )
}
