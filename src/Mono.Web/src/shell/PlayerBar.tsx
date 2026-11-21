import { useEffect, useRef, useState } from "react"
import {
  Cpu, HardDrive, Headphones, ListMusic, Maximize2, Mic2, Minimize2, Radio, Repeat, Repeat1,
  Shuffle, SkipBack, SkipForward, SlidersHorizontal, Speaker, Volume1, Volume2, VolumeX,
} from "lucide-react"
import { useApp } from "../state/app"
import { useMediaClock, useMono, useMonoCommands } from "../state/MonoProvider"
import { QualityPolicy, RepeatMode, StreamingProvider, type RoomSnapshot } from "../lib/protocol"
import { fmtTime, outputLatency, outputMode, qualityOf, sourceName } from "../lib/model"
import { hasShell, restartOutput, stopOutput, useOutputStatus } from "../lib/shell"
import { cachedSetup, startConfiguredOutput } from "../lib/setup"
import { toggleDsp } from "../lib/dsp"
import { rememberVolume, seedVolume, unmuteVolume } from "../lib/volume"
import { Art, Slider, Toggle, useDismiss } from "../ui"
import { PlayPause } from "../ui/motion"

export const DSP_PRESETS = ["끔", "헤드폰 EQ", "스피커 보정", "룸 보정 (컨볼루션)", "크로스피드", "파라메트릭 EQ"]

const POLICY_LABEL: Record<number, string> = {
  [QualityPolicy.Original]: "원본 그대로 전송",
  [QualityPolicy.MatchAll]: "모든 출력에 맞춤",
  [QualityPolicy.HostPriority]: "호스트 출력 우선",
}

/** 신호 경로를 한 줄로 요약한다. 플레이어 바의 칩과 팝오버 머리가 같이 쓴다. */
export function signalSummary(room: RoomSnapshot | null): { lamp: "ok" | "warn" | ""; text: string; res: string } {
  const t = room?.currentTrack
  if (!room || !t) return { lamp: "", text: "대기 중", res: "" }
  const res = qualityOf(t).res
  if (room.outputs.length === 0) return { lamp: "", text: "출력 없음", res }
  if (room.dspEnabled) return { lamp: "warn", text: "DSP", res }
  if (room.srcApplied) return { lamp: "warn", text: "SRC", res }
  if (room.bitPerfect) return { lamp: "ok", text: "Bit-Perfect", res }
  return { lamp: "", text: "재생 중", res }
}

export default function PlayerBar() {
  const app = useApp()
  const { room } = useMono()
  const cmd = useMonoCommands()
  const { positionMs, durationMs } = useMediaClock()
  const { role, canControl } = app

  const track = room?.currentTrack ?? null
  const playing = room?.playing ?? false

  /*
   * 진행 바의 진실은 Core 의 미디어 시계다. 손으로 옮긴 값(pending)은 "아직 확정되지 않은
   * 요청"일 뿐이라, Core 가 탐색을 받아 세대(resyncEpoch)를 올리거나 1.5초가 지나면 거둔다.
   */
  const [pending, setPending] = useState<number | null>(null)
  const epoch = room?.resyncEpoch ?? 0
  useEffect(() => setPending(null), [epoch])
  useEffect(() => {
    if (pending === null) return
    const id = setTimeout(() => setPending(null), 1500)
    return () => clearTimeout(id)
  }, [pending])

  const hasTimeline = durationMs > 0
  const live = hasTimeline ? positionMs / durationMs : 0
  const progress = pending ?? live

  // 볼륨은 출력 엔드포인트가 진실이다.
  const output = room?.outputs[0]
  const volume = (output?.volumePercent ?? 100) / 100
  const lastVolumeSent = useRef(0)
  const sendVolume = (v: number, force = false) => {
    const now = Date.now()
    if (!force && now - lastVolumeSent.current < 70) return
    lastVolumeSent.current = now
    cmd.setVolume(v * 100, output?.peerId)
  }
  useEffect(() => { seedVolume(output?.peerId, volume) }, [output?.peerId, volume])
  const toggleMute = () => {
    if (volume > 0) {
      rememberVolume(output?.peerId, volume)
      sendVolume(0, true)
    } else {
      sendVolume(unmuteVolume(output?.peerId), true)
    }
  }

  const [flyout, setFlyout] = useState<"signal" | "output" | null>(null)
  // 신호 경로 팝오버는 칩 바로 위에 뜬다. 칩의 위치는 곡 제목 길이에 따라 달라진다.
  const [signalLeft, setSignalLeft] = useState(16)
  const flyoutRef = useDismiss(flyout !== null, () => setFlyout(null))

  // 스페이스 = 재생/일시정지. 글자를 치는 중에는 가로채지 않는다.
  const stateRef = useRef({ playing, canControl, hasTrack: !!track })
  stateRef.current = { playing, canControl, hasTrack: !!track }
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const el = e.target as HTMLElement
      if (e.code !== "Space" || el.tagName === "INPUT" || el.tagName === "TEXTAREA" || el.tagName === "BUTTON") return
      const s = stateRef.current
      if (!s.canControl || !s.hasTrack) return
      e.preventDefault()
      if (s.playing) cmd.pause()
      else cmd.play()
    }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [cmd])

  const repeat = room?.repeat ?? RepeatMode.Off
  const nextRepeat = repeat === RepeatMode.Off ? RepeatMode.All : repeat === RepeatMode.All ? RepeatMode.One : RepeatMode.Off
  const upcoming = room ? Math.max(0, room.queue.length - room.queueIndex - 1) : 0
  const signal = signalSummary(room)
  const VolIcon = volume === 0 ? VolumeX : volume < 0.5 ? Volume1 : Volume2
  const listeners = room ? room.members.filter((m) => !m.isOutput).length : 0

  return (
    <footer className={`player ${role === "host" ? "is-live" : ""}`}>
      {/* ── 지금 재생 ── */}
      <div className="np">
        {track ? (
          <>
            <button className="np__art" onClick={() => app.setNowPlayingOpen(!app.nowPlayingOpen)} aria-label="지금 재생 화면">
              <Art src={track.artUrl} size={54} eager />
              <span className="expand">{app.nowPlayingOpen ? <Minimize2 size={18} /> : <Maximize2 size={18} />}</span>
            </button>
            <div className="grow">
              <div className="np__title clip">
                <span className="link" onClick={() => { app.setNowPlayingOpen(false); if (track.albumId) app.go({ page: "album", id: track.albumId }) }}>{track.title}</span>
              </div>
              <div className="np__artist clip">
                <span className="link" onClick={() => { app.setNowPlayingOpen(false); if (track.artistId) app.go({ page: "artist", id: track.artistId }) }}>{track.artistName}</span>
              </div>
            </div>
            <button
              className={`signal ${flyout === "signal" ? "is-open" : ""}`}
              onClick={(e) => {
                const left = e.currentTarget.getBoundingClientRect().left
                setSignalLeft(Math.max(16, Math.min(left, window.innerWidth - 396)))
                setFlyout(flyout === "signal" ? null : "signal")
              }}
              title="신호 경로"
            >
              <i className={`lamp ${signal.lamp ? `lamp--${signal.lamp}` : ""}`} />
              <span className="signal__text">
                {signal.res && <><b>{signal.res}</b> · </>}{signal.text.toUpperCase()}
              </span>
            </button>
          </>
        ) : (
          <>
            <Art size={54} />
            <div className="np__idle">재생할 음악을 골라 보세요</div>
          </>
        )}
      </div>

      {/* ── 트랜스포트 ── */}
      <div className="transport">
        {canControl ? (
          <div className="transport__btns">
            <button
              className={`icon-btn ${room?.shuffle ? "is-on" : ""}`}
              onClick={() => cmd.setShuffle(!room?.shuffle)}
              disabled={!room}
              aria-label="셔플" aria-pressed={room?.shuffle ?? false}
            >
              <Shuffle size={17} />
            </button>
            <button className="icon-btn" onClick={cmd.previous} disabled={!track} aria-label="이전 곡">
              <SkipBack size={19} fill="currentColor" />
            </button>
            <button
              className="play-btn play-btn--fg"
              onClick={() => (playing ? cmd.pause() : cmd.play())}
              disabled={!track}
              aria-label={playing ? "일시정지" : "재생"}
              style={!track ? { opacity: 0.35 } : undefined}
            >
              <PlayPause playing={playing} size={18} />
            </button>
            <button className="icon-btn" onClick={cmd.next} disabled={!track} aria-label="다음 곡">
              <SkipForward size={19} fill="currentColor" />
            </button>
            <button
              className={`icon-btn ${repeat !== RepeatMode.Off ? "is-on" : ""}`}
              onClick={() => cmd.setRepeat(nextRepeat)}
              disabled={!room}
              aria-label={repeat === RepeatMode.One ? "한 곡 반복" : repeat === RepeatMode.All ? "전체 반복" : "반복 끔"}
            >
              {repeat === RepeatMode.One ? <Repeat1 size={17} /> : <Repeat size={17} />}
            </button>
          </div>
        ) : (
          <div className="guest-note">
            <i className="lamp lamp--accent" />
            호스트와 같은 시각에 맞춰 듣는 중입니다. 재생은 호스트가 고릅니다
          </div>
        )}
        <div className="scrub">
          <span className="time num">{fmtTime(progress * durationMs)}</span>
          <Slider
            value={progress}
            disabled={!hasTimeline || !canControl || room?.seekingAllowed === false}
            accent={playing}
            label="재생 위치"
            onInput={setPending}
            onCommit={(v) => { setPending(v); cmd.seek(v * durationMs) }}
          />
          <span className="time num">{fmtTime(durationMs)}</span>
        </div>
      </div>

      {/* ── 도구 ── */}
      <div className="tools">
        {role === "host" ? (
          <button className={`onair ${app.onAirFresh ? "is-fresh" : ""}`} onClick={() => app.go({ page: "room" })} title="내 라운지로">
            <i />ON AIR · {listeners}
          </button>
        ) : role === "guest" ? (
          <button className="onair onair--guest" onClick={() => app.go({ page: "room" })} title="라운지로 돌아가기">
            <i />LOUNGE
          </button>
        ) : (
          <button className="btn btn--sm btn--line" onClick={() => app.openModal({ kind: "host" })}>
            <Radio size={15} /><span className="hide-narrow">라운지 열기</span>
          </button>
        )}

        <span className="sep" />

        <button
          className={`icon-btn ${flyout === "output" ? "is-on" : ""}`}
          onClick={() => setFlyout(flyout === "output" ? null : "output")}
          aria-label="출력 장치" title={output?.device ?? "출력 장치"}
        >
          <Speaker size={18} />
        </button>

        <div className="vol">
          <button className="icon-btn" onClick={toggleMute} disabled={!output} aria-label="음소거">
            <VolIcon size={18} />
          </button>
          <Slider
            value={volume}
            disabled={!output}
            label="볼륨"
            onInput={(v) => sendVolume(v)}
            onCommit={(v) => { rememberVolume(output?.peerId, v); sendVolume(v, true) }}
          />
        </div>

        <span className="sep" />

        <button
          className={`icon-btn ${app.panel === "lyrics" ? "is-on" : ""}`}
          onClick={() => app.setPanel(app.panel === "lyrics" ? null : "lyrics")}
          aria-label="가사" title="가사"
        >
          <Mic2 size={18} />
        </button>
        <button
          className={`icon-btn ${app.panel === "queue" ? "is-on" : ""}`}
          onClick={() => app.setPanel(app.panel === "queue" ? null : "queue")}
          aria-label="대기열" title="대기열"
          style={{ position: "relative" }}
        >
          <ListMusic size={18} />
          {upcoming > 0 && app.panel !== "queue" && <span className="count-dot">{upcoming > 99 ? "99+" : upcoming}</span>}
        </button>
        <button
          className={`icon-btn ${app.nowPlayingOpen ? "is-on" : ""}`}
          onClick={() => app.setNowPlayingOpen(!app.nowPlayingOpen)}
          disabled={!track}
          aria-label="지금 재생 화면" title="지금 재생 화면"
        >
          {app.nowPlayingOpen ? <Minimize2 size={17} /> : <Maximize2 size={17} />}
        </button>
      </div>

      {flyout && (
        <div className={`flyout ${flyout === "signal" ? "flyout--left" : ""}`} style={flyout === "signal" ? { left: signalLeft } : undefined} ref={flyoutRef}>
          {flyout === "signal" ? <SignalFlyout onClose={() => setFlyout(null)} /> : <OutputFlyout onClose={() => setFlyout(null)} />}
        </div>
      )}
    </footer>
  )
}

// ── 신호 경로 ───────────────────────────────────────────────────────────────

/**
 * 소스에서 출력 장치까지, 소리가 지나는 단계를 위에서 아래로 그린다.
 *
 * 왼쪽 레일 위에 단계마다 고리가 하나씩 놓인다. 큰 단계(소스·Core·출력)는 고리,
 * 그 사이에 끼어드는 변환(SRC, 꺼진 DSP)은 레일 위의 작은 점이다.
 * 이름 옆의 점이 그 구간의 상태다 — 초록은 원본 그대로, 앰버는 가공이 들어갔다는 뜻.
 */
function SignalFlyout({ onClose }: { onClose: () => void }) {
  const app = useApp()
  const { room } = useMono()
  const cmd = useMonoCommands()
  const t = room?.currentTrack
  const out = room?.outputs[0]
  const q = t ? qualityOf(t) : null
  const processed = !!room?.dspEnabled || !!room?.srcApplied
  const state = !t ? "대기 중" : !out ? "출력 없음" : processed ? "가공됨" : room?.bitPerfect ? "무손실" : "재생 중"
  const tone = !t || !out ? "" : processed ? "warn" : room?.bitPerfect ? "ok" : ""
  const dsp = !!room?.dspEnabled
  // 헤드폰 EQ(1)와 스피커 보정(2)은 2배 업샘플링을 함께 한다. 다만 장치가 2배 레이트를 받을 때만이다.
  // 실제로 올렸는지는 Core 가 srcApplied 로 알려 준다.
  const upsampling = dsp && (room?.dspPreset === 1 || room?.dspPreset === 2)
  const doubledKhz = t ? (t.sampleRate * 2) / 1000 : 0

  // "로컬 FLAC 96kHz 24bit 2ch" — 한 줄로 읽히는 원본 규격.
  const sourceSpec = t && q
    ? [
        t.hasLocal ? "로컬" : sourceName(t.source),
        q.container,
        t.isDsd ? `${q.res} ${(t.sampleRate / 1_000_000).toFixed(1)}MHz 1bit` : `${t.sampleRate / 1000}kHz ${t.bitDepth}bit`,
        `${t.channels}ch`,
      ].filter(Boolean).join(" ")
    : "곡을 재생하면 원본 규격이 여기 나옵니다"

  return (
    <>
      <div className="flyout__head">
        <div>
          <div className="sp__title">신호 경로: <b className={tone ? `is-${tone}` : ""}>{state}</b></div>
          <div className="t-cap dimmer" style={{ marginTop: 3 }}>{out?.device ?? out?.displayName ?? "연결된 출력 장치가 없습니다"}</div>
        </div>
      </div>
      <div className="flyout__body">
        <ol className="sp">
          <li className="sp__stage">
            <span className="sp__node">{q && q.container.length <= 4 && q.container ? q.container : <HardDrive size={14} />}</span>
            <div>
              <div className="sp__name">소스{t && <i className="lamp lamp--ok" />}</div>
              <div className="sp__spec">{sourceSpec}</div>
            </div>
          </li>

          {room?.srcApplied && (
            <li className="sp__stage sp__stage--minor">
              <span className="sp__node" />
              <div>
                <div className="sp__name">샘플레이트 변환<i className="lamp lamp--warn" /></div>
                <div className="sp__spec">
                  {t ? `DSP 프리셋의 2배 업샘플링 · ${t.sampleRate / 1000} → ${doubledKhz} kHz` : "DSP 프리셋의 2배 업샘플링"}
                </div>
              </div>
            </li>
          )}

          <li className="sp__stage">
            <span className="sp__node"><Cpu size={14} /></span>
            <div>
              <div className="sp__name">Mono Core{t && <i className={`lamp ${room?.srcApplied ? "lamp--warn" : "lamp--ok"}`} />}</div>
              <div className="sp__spec">{POLICY_LABEL[room?.qualityPolicy ?? 0]}</div>
            </div>
          </li>

          <li className={`sp__stage ${dsp ? "" : "sp__stage--minor"}`}>
            <span className="sp__node">{dsp && <SlidersHorizontal size={14} />}</span>
            <div>
              <div className="sp__name">{dsp ? DSP_PRESETS[room!.dspPreset] ?? "DSP" : "DSP 우회"}{dsp && <i className="lamp lamp--warn" />}</div>
              <div className="sp__spec">
                {dsp ? `헤드룸 ${room!.headroomDb} dB` : "신호에 손대지 않습니다"}
                {upsampling && t && out && !room?.srcApplied ? ` · 장치가 ${doubledKhz} kHz 를 받지 않아 업샘플링은 하지 않습니다` : ""}
                {room?.dspLocked ? " · 호스트가 잠금" : ""}
              </div>
            </div>
            <Toggle
              label="DSP"
              value={dsp}
              disabled={!room || !app.canControl || room.dspLocked}
              onChange={(on) => toggleDsp(cmd.setDsp, room?.dspPreset ?? 0, on)}
            />
          </li>

          <li className="sp__stage">
            <span className="sp__node">{out?.supportsDsd ? <Headphones size={14} /> : <Speaker size={14} />}</span>
            <div>
              <div className="sp__name">{out?.device ?? out?.displayName ?? "출력 장치 없음"}{out && tone && <i className={`lamp lamp--${tone}`} />}</div>
              <div className="sp__spec">
                {out ? (
                  <>
                    {[outputMode(out).label, outputLatency(out), out.stats?.bufferMs != null ? `버퍼 ${out.stats.bufferMs} ms` : null].filter(Boolean).join(" · ")}
                    <br />
                    {out.stats?.locked ? "클럭 잠김" : "클럭 맞추는 중"}
                    {out.stats?.jitterMs != null ? ` · 지터 ${out.stats.jitterMs.toFixed(2)} ms` : ""}
                    {out.hardwareVolume ? " · 하드웨어 볼륨" : " · 소프트웨어 볼륨"}
                    {/* 장치가 이 곡을 그대로 받지 못하거나 열리지 않았으면, 변환했다고 하지 않고 그 사실을 적는다. */}
                    {(out.stats?.deviceError || out.note) && (
                      <span style={{ display: "block", marginTop: 4, color: "var(--warn-text)" }}>{out.stats?.deviceError ?? out.note}</span>
                    )}
                  </>
                ) : "플레이어 바의 스피커 버튼에서 출력을 연결하세요"}
              </div>
            </div>
          </li>
        </ol>

        <button className="btn btn--sm btn--line btn--block" style={{ marginTop: 20 }} onClick={() => { onClose(); app.go({ page: "settings", tab: "dsp" }) }}>
          <SlidersHorizontal size={14} />DSP 설정 열기
        </button>
      </div>
    </>
  )
}

// ── 출력 장치 ───────────────────────────────────────────────────────────────

function OutputFlyout({ onClose }: { onClose: () => void }) {
  const app = useApp()
  const { room } = useMono()
  const cmd = useMonoCommands()
  const worker = useOutputStatus()
  const [busy, setBusy] = useState(false)
  const outputs = room?.outputs ?? []
  const configured = cachedSetup().audio?.deviceName

  // 출력 워커는 룸 하나에 붙는다. 룸이 없으면 솔로 룸부터 만든다.
  async function attach() {
    setBusy(true)
    try {
      const roomId = room?.id ?? await cmd.ensureSoloRoom()
      const res = await startConfiguredOutput(roomId)
      if (!res.ok) app.toast(res.error ?? "출력을 시작하지 못했습니다", true)
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <div className="flyout__head">
        <div>
          <div className="label">출력</div>
          <div className="t-h3" style={{ marginTop: 5 }}>
            {outputs.length > 0 ? `${outputs.length}개 장치로 재생` : "연결된 출력이 없습니다"}
          </div>
        </div>
      </div>
      <div className="flyout__body">
        <div style={{ marginTop: 10 }}>
          {outputs.map((o) => (
            <div className="out-row" key={o.peerId}>
              <span className="out-row__icon">{o.supportsDsd ? <Headphones size={16} /> : <Speaker size={16} />}</span>
              <div className="grow">
                <div className="t-small clip" style={{ fontWeight: 620 }}>{o.device ?? o.displayName ?? "출력"}</div>
                <div className="chain__spec clip" style={{ marginTop: 1 }}>
                  {[outputMode(o).short, o.maxSampleRate ? `최대 ${o.maxBitDepth}/${Math.round(o.maxSampleRate / 1000)}` : null, o.supportsDsd ? "DSD" : null].filter(Boolean).join(" · ")}
                </div>
              </div>
              <div className="out-row__vol">
                <Volume1 size={15} style={{ color: "var(--fg-2)" }} />
                <Slider
                  value={o.volumePercent / 100}
                  disabled={!app.canControl && o.peerId !== "out-local"}
                  label={`${o.device ?? "출력"} 볼륨`}
                  onCommit={(v) => { rememberVolume(o.peerId, v); cmd.setVolume(v * 100, o.peerId) }}
                />
                <span className="mono num">{Math.round(o.volumePercent)}%</span>
              </div>
              {(o.stats?.deviceError || o.note) && (
                <p className="t-cap" style={{ gridColumn: "1 / -1", color: "var(--warn-text)", lineHeight: 1.5 }}>{o.stats?.deviceError ?? o.note}</p>
              )}
            </div>
          ))}

          {outputs.length === 0 && (
            <p className="t-small dim" style={{ lineHeight: 1.65 }}>
              {!hasShell()
                ? "출력은 Mono 데스크톱 앱에서만 연결할 수 있습니다. 브라우저에서는 재생을 제어할 수만 있습니다."
                : worker?.running
                  ? `출력 워커가 실행 중입니다${configured ? ` (${configured})` : ""}. 곡을 재생하면 이 장치로 붙습니다.`
                  : `${configured ?? "시스템 기본 장치"} 로 소리를 내보냅니다.`}
            </p>
          )}
        </div>

        <div className="row gap-8" style={{ marginTop: 16 }}>
          {hasShell() && outputs.length === 0 && (
            <button className="btn btn--sm btn--primary grow" onClick={attach} disabled={busy}>{busy ? "연결하는 중…" : "출력 연결"}</button>
          )}
          {hasShell() && worker?.running && (
            <>
              <button className="btn btn--sm btn--line grow" onClick={() => { void restartOutput() }}>다시 시작</button>
              <button className="btn btn--sm btn--line grow" onClick={() => { void stopOutput() }}>연결 끊기</button>
            </>
          )}
          <button className="btn btn--sm btn--line grow" onClick={() => { onClose(); app.go({ page: "settings", tab: "audio" }) }}>오디오 설정</button>
        </div>
      </div>
    </>
  )
}
