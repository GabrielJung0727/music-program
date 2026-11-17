import { useEffect, useRef, useState } from "react"
import { ListMusic, Mic2, Sparkles, Trash2, X } from "lucide-react"
import { useApp, type Panel } from "../state/app"
import { useMediaClock, useMono, useMonoCommands } from "../state/MonoProvider"
import { fmtTime } from "../lib/model"
import type { SnapshotLyricLine } from "../lib/protocol"
import { Art, Empty, Eq, Toggle, fromButton } from "../ui"
import { centerInScroll, usePresence } from "../ui/motion"

/**
 * 오른쪽에 붙는 패널. 본문을 덮지 않고 옆으로 밀어낸다 — 보면서 고를 수 있어야 한다.
 * 열리고 닫힐 때 너비가 220ms 동안 늘고 준다(DESIGN.md 모션 5).
 */
export default function Dock() {
  const app = useApp()
  const { mounted, shown } = usePresence(app.panel !== null, 220)

  // 닫히는 동안에는 마지막으로 보던 내용을 그대로 둔다.
  const last = useRef<Panel>(app.panel)
  if (app.panel) last.current = app.panel
  const panel = app.panel ?? last.current

  return (
    <aside className={`dock ${shown ? "is-open" : ""}`} aria-hidden={!app.panel}>
      {mounted && (
        <div className="dock__inner">
          <div className="dock__head">
            <div className="seg">
              <button className={panel === "queue" ? "is-on" : ""} onClick={() => app.setPanel("queue")}><ListMusic size={14} />대기열</button>
              <button className={panel === "lyrics" ? "is-on" : ""} onClick={() => app.setPanel("lyrics")}><Mic2 size={14} />가사</button>
            </div>
            <div className="grow" />
            <button className="icon-btn" onClick={() => app.setPanel(null)} aria-label="패널 닫기"><X size={18} /></button>
          </div>
          <div className="dock__body">
            {panel === "queue" ? <Queue /> : <Lyrics />}
          </div>
        </div>
      )}
    </aside>
  )
}

// ── 대기열 ──────────────────────────────────────────────────────────────────

function Queue() {
  const app = useApp()
  const { room } = useMono()
  const cmd = useMonoCommands()
  const [dragFrom, setDragFrom] = useState<number | null>(null)
  const [dragOver, setDragOver] = useState<number | null>(null)
  const [now, setNow] = useState(Date.now())
  const autoplay = room?.autoplay

  useEffect(() => {
    if (!autoplay) return
    const id = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(id)
  }, [autoplay])

  if (!room || room.queue.length === 0) {
    return (
      <Empty icon={<ListMusic size={36} strokeWidth={1.2} />} title="대기열이 비어 있습니다">
        앨범이나 곡을 재생하면 여기에 순서대로 쌓입니다.
      </Empty>
    )
  }

  const { queue, queueIndex } = room
  const canControl = app.canControl && !(room.queueLocked && app.role !== "host")
  const upcomingMs = queue.slice(queueIndex + 1).reduce((sum, q) => sum + q.durationMs, 0)

  const row = (i: number) => {
    const q = queue[i]
    const isCurrent = i === queueIndex
    return (
      <div
        key={q.id}
        className={`qrow ${isCurrent ? "is-current" : ""} ${i < queueIndex ? "is-past" : ""} ${dragFrom === i ? "is-dragging" : ""} ${dragOver === i && dragFrom !== i ? "is-over" : ""}`}
        draggable={canControl && !isCurrent}
        onDragStart={(e) => { setDragFrom(i); e.dataTransfer.effectAllowed = "move" }}
        onDragOver={(e) => { if (dragFrom !== null) { e.preventDefault(); setDragOver(i) } }}
        onDragEnd={() => { setDragFrom(null); setDragOver(null) }}
        onDrop={() => {
          if (dragFrom !== null && dragFrom !== i) cmd.moveInQueue(dragFrom, i - dragFrom)
          setDragFrom(null)
          setDragOver(null)
        }}
        onDoubleClick={(e) => {
          // 행 안의 버튼에서 올라온 더블클릭은 "이 곡으로 이동"이 아니다. X 를 눌러 곡을 빼면
          // 아래 곡이 그 자리로 올라오는데, 이어서 한 번 더 누르면 브라우저는 그 두 번을
          // 더블클릭으로 묶는다 — 걸러 내지 않으면 곡을 빼다 말고 다음 곡이 재생된다.
          if (fromButton(e)) return
          if (canControl && !isCurrent) cmd.jumpTo(i, q.id)
        }}
      >
        <Art src={q.artUrl} size={40} />
        <div className="grow">
          <div className="qrow__title clip">{q.title}</div>
          <div className="qrow__sub clip">{q.artist}</div>
        </div>
        <div className="qrow__tail">
          {isCurrent ? <Eq paused={!room.playing} /> : (
            <>
              <span className="time num">{fmtTime(q.durationMs)}</span>
              {canControl && (
                <button className="icon-btn icon-btn--sm" aria-label="대기열에서 빼기" onClick={() => cmd.removeFromQueue(i, q.id)}>
                  <X size={15} />
                </button>
              )}
            </>
          )}
        </div>
      </div>
    )
  }

  return (
    <>
      <div className="dock__sec"><span className="label">지금 재생</span></div>
      {queueIndex >= 0 && row(queueIndex)}

      <div className="dock__sec">
        <span className="label">다음 {queue.length - queueIndex - 1}곡 · {fmtTime(upcomingMs)}</span>
        {canControl && queue.length - queueIndex - 1 > 0 && (
          <button className="btn btn--sm btn--quiet" style={{ margin: "-8px -6px", height: 26 }} onClick={cmd.clearQueue}>
            <Trash2 size={13} />비우기
          </button>
        )}
      </div>
      {queue.map((_, i) => (i > queueIndex ? row(i) : null))}
      {queue.length - queueIndex - 1 === 0 && (
        <p className="t-small dimmer" style={{ padding: "4px 10px 0" }}>이 곡이 마지막입니다.</p>
      )}

      <div className="autoplay">
        <div className="row gap-12">
          <Sparkles size={16} style={{ color: "var(--accent-text)", flex: "none" }} />
          <div className="grow">
            <div className="t-small" style={{ fontWeight: 620 }}>이어서 자동 재생</div>
            <div className="t-cap dimmer">대기열이 끝나면 비슷한 곡을 이어서 틉니다</div>
          </div>
          <Toggle
            label="자동 재생"
            value={room.smartAutoplay}
            disabled={!app.canControl}
            onChange={(v) => cmd.setRoomFlag("smart_autoplay", v)}
          />
        </div>
        {room.smartAutoplay && autoplay && autoplay.candidates.length > 0 && (
          <div style={{ marginTop: 12, paddingTop: 12, borderTop: "1px solid var(--line)" }}>
            <div className="label" style={{ padding: "0 7px 6px" }}>
              다음 후보 · {Math.max(0, Math.ceil((autoplay.deadlineUnixMs - now) / 1000))}초 안에 고르기
            </div>
            {autoplay.candidates.map((c) => (
              <button
                key={c.id}
                className={`autoplay__pick ${autoplay.chosenId === c.id ? "is-on" : ""}`}
                disabled={app.role === "guest"}
                onClick={() => cmd.chooseAutoplay(c.id)}
              >
                <Art src={c.artUrl} size={34} />
                <span className="grow">
                  <span className="qrow__title clip" style={{ display: "block" }}>{c.title}</span>
                  <span className="qrow__sub clip" style={{ display: "block" }}>{c.artistName}</span>
                </span>
              </button>
            ))}
          </div>
        )}
      </div>
    </>
  )
}

// ── 가사 ────────────────────────────────────────────────────────────────────

/**
 * 시각이 붙은 가사. 지금 소절만 또렷하고(opacity 1, scale 1.02) 나머지는 물러난다(0.35).
 * 소절이 바뀌면 320ms 동안 가운데로 끌어온다(DESIGN.md 모션 4). 줄을 누르면 그 자리로 옮긴다.
 */
export function LyricLines({ lines, big }: { lines: SnapshotLyricLine[]; big?: boolean }) {
  const app = useApp()
  const { room } = useMono()
  const cmd = useMonoCommands()
  const { positionMs } = useMediaClock()
  const activeRef = useRef<HTMLButtonElement>(null)

  // 지금 줄 = 이미 지난 줄 가운데 마지막. 연 사이의 빈 줄은 건너뛴다 — 빈 줄이 "지금"이 되면
  // 간주 동안 아무 줄도 밝혀지지 않는다.
  let active = -1
  for (let i = 0; i < lines.length; i++) {
    if (lines[i].timeMs > positionMs) break
    if (lines[i].text.trim()) active = i
  }

  useEffect(() => {
    if (activeRef.current) centerInScroll(activeRef.current)
  }, [active])

  return (
    <div className="lyrics" style={big ? undefined : { paddingTop: 20 }}>
      {lines.map((line, i) =>
        line.text.trim() ? (
          <button
            key={i}
            ref={i === active ? activeRef : undefined}
            className={`lyric ${i === active ? "is-on" : ""}`}
            disabled={!app.canControl || room?.seekingAllowed === false}
            onClick={() => cmd.seek(line.timeMs)}
          >
            {line.text}
          </button>
        ) : <span key={i} className="lyric--gap" />,
      )}
    </div>
  )
}

function Lyrics() {
  const { room } = useMono()
  const track = room?.currentTrack
  if (!track) {
    return <Empty icon={<Mic2 size={36} strokeWidth={1.2} />} title="재생 중인 곡이 없습니다" />
  }
  if (!room || room.lyrics.length === 0) {
    return (
      <Empty icon={<Mic2 size={36} strokeWidth={1.2} />} title="가사가 없는 곡입니다">
        음원과 같은 폴더에 .lrc 파일을 두거나 태그에 가사를 넣으면 여기에 나옵니다.
      </Empty>
    )
  }
  return <LyricLines lines={room.lyrics} />
}
