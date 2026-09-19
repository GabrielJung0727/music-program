import { useState, useEffect } from "react"
import { Lock } from "lucide-react"
import { type QueueTrack } from "../data/types"
import { useMono, useMediaClock, useMonoCommands } from "../state/MonoProvider"
import { stopOutput, useOutputStatus } from "../lib/shell"
import { startConfiguredOutput, cachedSetup } from "../lib/setup"
import { MonoIcon } from "./icons/MonoIcons"
import { RepeatMode } from "../lib/protocol"
import { FALLBACK_ART } from "../lib/artwork"

export type PlayerMode = "solo" | "host" | "guest"

/** 0~100% ↔ dBFS. 100% 가 0 dB, 0% 는 하한(-60 dB)으로 잘라 슬라이더 끝과 맞춘다. */
const MIN_DB = -60
function percentToDb(percent: number): number {
  if (percent <= 0) return MIN_DB
  return Math.max(MIN_DB, Math.round(20 * Math.log10(percent / 100) * 10) / 10)
}
function dbToPercent(db: number): number {
  if (db <= MIN_DB) return 0
  return Math.max(0, Math.min(100, Math.round(10 ** (db / 20) * 100)))
}

function IconPrev() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor">
      <path d="M6 6h2v12H6zm3.5 6 8.5 6V6z" />
    </svg>
  )
}

function IconNext() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor">
      <path d="M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z" />
    </svg>
  )
}

function IconPlay() {
  return (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
      <path d="M8 5v14l11-7z" />
    </svg>
  )
}

function IconPause() {
  return (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
      <path d="M6 19h4V5H6v14zm8-14v14h4V5h-4z" />
    </svg>
  )
}

function IconShuffle() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="16 3 21 3 21 8" />
      <line x1="4" y1="20" x2="21" y2="3" />
      <polyline points="21 16 21 21 16 21" />
      <line x1="15" y1="15" x2="21" y2="21" />
    </svg>
  )
}

function IconLoopOne() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="17 1 21 5 17 9" />
      <path d="M3 11V9a4 4 0 0 1 4-4h14" />
      <polyline points="7 23 3 19 7 15" />
      <path d="M21 13v2a4 4 0 0 1-4 4H3" />
      <text x="12" y="15.5" textAnchor="middle" fontSize="9" fontWeight="700" fill="currentColor" stroke="none">1</text>
    </svg>
  )
}

function IconLoop() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="17 1 21 5 17 9" />
      <path d="M3 11V9a4 4 0 0 1 4-4h14" />
      <polyline points="7 23 3 19 7 15" />
      <path d="M21 13v2a4 4 0 0 1-4 4H3" />
    </svg>
  )
}

function IconQueue() {
  return (
    <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" style={{ fontWeight: 400 }}>
      <line x1="8" y1="6" x2="21" y2="6" />
      <line x1="8" y1="12" x2="21" y2="12" />
      <line x1="8" y1="18" x2="21" y2="18" />
      <line x1="3" y1="6" x2="3.01" y2="6" />
      <line x1="3" y1="12" x2="3.01" y2="12" />
      <line x1="3" y1="18" x2="3.01" y2="18" />
    </svg>
  )
}

function IconChevron({ size = 12 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <path d="m6 9 6 6 6-6" />
    </svg>
  )
}


export default function PlayerBar({
  playerMode, setPlayerMode, onNavigateToLens, isFullscreenPlayer, setIsFullscreenPlayer,
  playing, setPlaying, activeLoungeRoom, setActiveLoungeRoom, onLeaveLounge,
  onOpenPlayer, onStartLounge, onStopHosting, onRequestStopHosting,
  onOpenArtist, onOpenAlbum, onReturnToLounge, isViewingArtistOrAlbum,
  joinedLoungeId, onShareLounge, onToggleQueue, queueCount, currentTrack,
}: {
  playerMode: PlayerMode
  setPlayerMode: (m: PlayerMode) => void
  onNavigateToLens: () => void
  isFullscreenPlayer: boolean
  setIsFullscreenPlayer: (v: boolean) => void
  playing: boolean
  setPlaying: (v: boolean) => void
  activeLoungeRoom: boolean
  setActiveLoungeRoom: (v: boolean) => void
  onLeaveLounge?: () => void
  onOpenPlayer: () => void
  onStartLounge: () => void
  onStopHosting: () => void
  onRequestStopHosting: () => void
  onOpenArtist: (e?: React.MouseEvent) => void
  onOpenAlbum: () => void
  onReturnToLounge: () => void
  isViewingArtistOrAlbum: boolean
  joinedLoungeId: string | null
  onShareLounge: () => void
  onToggleQueue: () => void
  queueCount: number
  currentTrack?: QueueTrack | null
}) {
  const { room } = useMono()
  const cmd = useMonoCommands()
  const { positionMs, durationMs } = useMediaClock()

  // 진행률은 Core 의 미디어 시계에서 온다. 드래그 중에만 로컬 값이 이긴다.
  const [scrubbing, setScrubbing] = useState<number | null>(null)
  const livePercent = durationMs > 0 ? (positionMs / durationMs) * 100 : 0
  const progress = scrubbing ?? livePercent
  const setProgress = (percent: number) => setScrubbing(percent)

  // 볼륨은 출력 엔드포인트가 진실이다. 없으면 100 으로 보인다.
  const activeOutput = room?.outputs[0]
  const volume = activeOutput?.volumePercent ?? 100
  const setVolume = (v: number) => cmd.setVolume(v, activeOutput?.peerId)
  const [isSignalPathOpen, setIsSignalPathOpen] = useState(false)
  const [isDspBypassed, setIsDspBypassed] = useState(false)
  useEffect(() => {
    if (room) setIsDspBypassed(!room.dspEnabled)
  }, [room?.dspEnabled])
  const [isDevicePopoverOpen, setIsDevicePopoverOpen] = useState(false)
  const [showHiddenDevices, setShowHiddenDevices] = useState(false)
  /** 숨김은 순수 UI 취향이라 Core 에 보내지 않는다. */
  const [hiddenDeviceIds, setHiddenDeviceIds] = useState<string[]>([])
  const [outputError, setOutputError] = useState<string | null>(null)
  const worker = useOutputStatus()
  const setupDeviceName = cachedSetup().audio?.deviceName ?? ""

  async function attachOutput() {
    const roomId = room?.id ?? await cmd.ensureSoloRoom()
    const res = await startConfiguredOutput(roomId)
    if (!res.ok) setOutputError(res.error ?? "출력을 시작하지 못했습니다")
    else setOutputError(null)
  }

  // 신호 경로 각 단계의 실제 값. 룸 스냅샷과 출력 엔드포인트가 진실이다.
  const snapTrack = room?.currentTrack
  const sourceLabel = snapTrack
    ? `${snapTrack.hasLocal ? "Local Library" : snapTrack.source === 1 ? "TIDAL" : snapTrack.source === 2 ? "Qobuz" : "Streaming"}` +
      (snapTrack.badge ? ` (${snapTrack.badge})` : "")
    : "소스 없음"
  const driverLabel = activeOutput
    ? `${activeOutput.exclusiveMode ? "Exclusive Lock" : "Shared Mixer"}${room?.bitPerfect ? " · Bit-Perfect" : room?.srcApplied ? " · SRC 적용됨" : ""}`
    : worker?.running
      ? `워커 실행 중 (${worker.backend || "local"})`
      : "출력 연결 안 됨"
  const bufferLabel = activeOutput?.stats?.bufferMs != null
    ? `Buffer: ${activeOutput.stats.bufferMs}ms · Latency: ${activeOutput.reportedLatencyMs}ms`
    : activeOutput ? `Latency: ${activeOutput.reportedLatencyMs}ms` : "—"
  const dacLabel = activeOutput?.device ?? activeOutput?.displayName ?? (worker?.running ? (setupDeviceName || "이 PC 출력") : "출력 장치 없음")
  const dacDetail = activeOutput
    ? [
        activeOutput.maxSampleRate ? `${Math.round(activeOutput.maxSampleRate / 1000)}kHz / ${activeOutput.maxBitDepth}-Bit` : null,
        activeOutput.supportsDsd ? "DSD" : null,
        activeOutput.hardwareVolume ? "Hardware volume" : "Software volume",
      ].filter(Boolean).join(" · ")
    : worker?.running
      ? "출력 워커가 이 장치로 재생 중입니다"
      : "Mono Output 을 연결하세요"
  // 룸에 붙은 실제 Output 엔드포인트. 워커는 떠 있는데 룸 목록이 비면
  // (시작 때 --room 없이 띄운 경우) 설정에 고른 장치 이름을 보여 준다.
  const roomDevices = (room?.outputs ?? []).map((o, i) => ({
    id: o.peerId,
    name: o.displayName ?? o.device ?? `출력 ${i + 1}`,
    spec: [
      o.exclusiveMode ? "Exclusive" : "Shared",
      o.maxSampleRate ? `${Math.round(o.maxSampleRate / 1000)}kHz / ${o.maxBitDepth}-Bit` : null,
      o.supportsDsd ? "DSD" : null,
      o.badge,
    ].filter(Boolean).join(" • "),
    supportsDsd: o.supportsDsd,
    icon: o.supportsDsd ? <MonoIcon.Headphones size={13} className="inline mr-1" /> : <MonoIcon.Speaker size={13} className="inline mr-1" />,
    active: !o.spectator && o.volumePercent > 0,
    // 디자인은 dB 로 보여 준다. Core 는 0~100% 를 안다.
    volume: percentToDb(o.volumePercent),
    isHidden: hiddenDeviceIds.includes(o.peerId),
    isLocalWorker: false,
  }))
  const devices = roomDevices.length > 0
    ? roomDevices
    : worker?.running
      ? [{
          id: "local-worker",
          name: setupDeviceName || "이 PC 출력",
          spec: [worker.backend || null, "워커 실행 중"].filter(Boolean).join(" • "),
          supportsDsd: false,
          icon: <MonoIcon.Speaker size={13} className="inline mr-1" />,
          active: true,
          volume: 0,
          isHidden: false,
          isLocalWorker: true,
        }]
      : []

  const isGuest = playerMode === "guest"
  // 셔플·반복은 룸이 진실이다. 로컬 state 로 두면 게스트 화면과 호스트 화면이 갈라진다.
  const shuffleOn = room?.shuffle ?? false
  const repeatMode: RepeatMode = room?.repeat ?? RepeatMode.Off
  const nextRepeat: RepeatMode =
    repeatMode === RepeatMode.Off ? RepeatMode.All : repeatMode === RepeatMode.All ? RepeatMode.One : RepeatMode.Off
  const repeatLabel =
    repeatMode === RepeatMode.All ? "Repeat all" : repeatMode === RepeatMode.One ? "Repeat one" : "Repeat off"
  const isHost = playerMode === "host"

  type PlaybackMode = "hosting" | "listener" | "solo"
  const [mode, setMode] = useState<PlaybackMode>("solo")

  // Sync local mode when playerMode prop changes via external actions (join/host from lounge cards)
  useEffect(() => {
    if (playerMode === "host") setMode("hosting")
    else if (playerMode === "guest") setMode("listener")
    else if (playerMode === "solo") setMode("solo")
  }, [playerMode])

  const isHosting = mode === "hosting"
  const isListener = mode === "listener"

  const accentColor = isHosting ? "#2563EB" : isListener ? "#38bdf8" : "#8b5cf6"
  const accentShadow = isHosting ? "rgba(37,99,235,0.35)" : isListener ? "rgba(56,189,248,0.3)" : "rgba(139,92,246,0.35)"
  const accentHover = isHosting ? "#1D4ED8" : isListener ? "#0ea5e9" : "#7c3aed"
  const seekbarColor = isHosting
    ? "bg-blue-600 dark:bg-blue-500"
    : isListener
    ? "bg-blue-600 dark:bg-blue-600"
    : "bg-violet-600 dark:bg-violet-500"

  const totalSecs = Math.round(durationMs / 1000)
  const currentSecs = Math.round((progress / 100) * totalSecs)
  const fmt = (s: number) => {
    if (!s || isNaN(s) || s < 0) return "00:00"
    return `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(Math.floor(s % 60)).padStart(2, "0")}`
  }

  // Unified lounge action — driven by local `mode` state, not playerMode prop
  const LoungeActionButton = () => {
    if (isHosting) {
      return (
        <div className="flex items-center shrink-0 ml-2" style={{ gap: 6 }}>
          <button
            onClick={(e) => { e.stopPropagation(); onReturnToLounge() }}
            className="h-7 px-2.5 text-[11px] font-mono font-medium rounded-md border border-blue-500/40 bg-blue-500/15 text-blue-400 transition-colors shrink-0 whitespace-nowrap flex items-center gap-1.5 cursor-pointer"
            style={{ fontFamily: "inherit", boxShadow: "0 1px 6px rgba(59,130,246,0.2)" }}
          >
            <span style={{ width: 6, height: 6, borderRadius: "50%", background: "#60a5fa", display: "inline-block", animation: "pulse 1.5s ease-in-out infinite", flexShrink: 0 }} />
            ON AIR
          </button>
          <button
            onClick={(e) => { e.stopPropagation(); onRequestStopHosting() }}
            className="lounge-btn-end shrink-0 relative z-30 pointer-events-auto"
          >
            End Lounge
          </button>
        </div>
      )
    }
    if (isListener) {
      return (
        <div className="flex items-center shrink-0 ml-2" style={{ gap: 6 }}>
          <span className="player-lounge-status">
            <span className="player-lounge-status-dot" />
            Listening
          </span>
          <button
            type="button"
            onClick={(e) => { e.stopPropagation(); onLeaveLounge?.() }}
            className="lounge-btn-end relative z-30 pointer-events-auto shrink-0"
          >
            Leave Lounge
          </button>
        </div>
      )
    }
    // Solo idle — discreet ghost button
    return (
      <button
        onClick={(e) => { e.stopPropagation(); onStartLounge() }}
        className="btn-host-lounge lounge-active-trigger inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-medium transition-colors shrink-0 ml-2 whitespace-nowrap cursor-pointer"
      >
        <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" style={{ flexShrink: 0 }}>
          <circle cx="12" cy="12" r="2"/><path d="M16.24 7.76a6 6 0 0 1 0 8.49"/><path d="M7.76 7.76a6 6 0 0 0 0 8.49"/><path d="M20.49 3.51a12 12 0 0 1 0 16.97"/><path d="M3.51 3.51a12 12 0 0 0 0 16.97"/>
        </svg>
        Host Lounge
      </button>
    )
  }

  return (
    <>
      <footer
        className="fixed bottom-0 left-0 right-0 z-50 h-20 grid items-center px-6"
        style={{
          gridTemplateColumns: "1fr auto 1fr",
          background: "color-mix(in srgb, var(--surface-card) 95%, transparent)",
          backdropFilter: "blur(12px)",
          WebkitBackdropFilter: "blur(12px)",
          borderTop: isHosting
            ? "2px solid rgba(59,130,246,0.4)"
            : isListener
            ? "2px solid rgba(56,189,248,0.3)"
            : "1px solid var(--border-subtle)",
          transition: "background 0.2s ease, border-color 0.2s ease",
        }}
      >
        {/* Left — Track + mode button */}
        <div className="justify-self-start flex items-center gap-3 min-w-0 relative z-20 pointer-events-auto">
          {currentTrack ? (
            /* Active playback */
            <>
              {/* Unified clickable zone: artwork + track info */}
              <div
                className="flex items-center gap-2 select-none"
                style={{ flex: "0 0 auto" }}
              >
                {/* Artwork — toggles expanded player (or returns to lounge) */}
                <div
                  className="relative shrink-0 cursor-pointer transition-all duration-200 hover:scale-[1.04]"
                  style={{ width: 44, height: 44, minWidth: 44, borderRadius: 6, overflow: "hidden", border: "1px solid rgba(228,228,231,0.8)", background: "#F4F4F5" }}
                  title={isFullscreenPlayer ? "Close Now Playing" : "Open Now Playing"}
                  onClick={(e) => {
                    e.stopPropagation()
                    if (activeLoungeRoom || playerMode === "host" || playerMode === "guest") {
                      onReturnToLounge()
                    } else {
                      setIsFullscreenPlayer(!isFullscreenPlayer)
                    }
                  }}
                >
                  <img
                    src={currentTrack.art || (currentTrack as any).coverUrl || FALLBACK_ART}
                    alt={currentTrack.title}
                    style={{ width: "100%", height: "100%", objectFit: "cover", display: "block" }}
                  />
                </div>
                {/* Text — title navigates to album, artist navigates to artist view */}
                <div className="flex flex-col justify-center overflow-hidden" style={{ width: 140 }}>
                  <button
                    onClick={(e) => { e.stopPropagation(); setIsFullscreenPlayer(false); onOpenAlbum() }}
                    className="cursor-pointer hover:underline transition-colors truncate whitespace-nowrap text-left"
                    style={{ fontSize: 12, fontWeight: 600, color: "var(--text-primary)", lineHeight: 1.3, overflow: "hidden", textOverflow: "ellipsis", background: "none", border: "none", padding: 0, fontFamily: "inherit", display: "block", minWidth: 0 }}
                  >
                    {currentTrack.title}
                  </button>
                  <button
                    onClick={(e) => { e.stopPropagation(); setIsFullscreenPlayer(false); onOpenArtist(e) }}
                    className="cursor-pointer hover:underline transition-colors truncate whitespace-nowrap text-left"
                    style={{ fontSize: 11, color: "var(--text-secondary)", background: "none", border: "none", padding: 0, fontFamily: "inherit", lineHeight: 1.3, marginTop: 2, overflow: "hidden", textOverflow: "ellipsis", display: "block", minWidth: 0 }}
                  >
                    {currentTrack.artist}
                  </button>
                </div>
              </div>
              <div className="shrink-0">
                <LoungeActionButton />
              </div>
            </>
          ) : (
            /* Standby state */
            <div className="flex items-center h-full" style={{ gap: 12 }}>
              <button
                type="button"
                onClick={(e) => { e.stopPropagation(); onOpenPlayer() }}
                title={isHosting || isListener ? "Return to Live Lounge" : "Open Full Player"}
                className="w-10 h-10 rounded-lg flex items-center justify-center border border-[var(--border-strong)] bg-[var(--surface-elevated)] text-[var(--text-secondary)] shrink-0 cursor-pointer transition-all duration-200 hover:scale-[1.04] hover:border-violet-500/50 hover:ring-2 hover:ring-violet-500/20"
                style={{ padding: 0 }}
              >
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                  <circle cx="12" cy="12" r="10" />
                  <circle cx="12" cy="12" r="3" />
                </svg>
              </button>
              <div className="flex flex-col justify-center overflow-hidden" style={{ width: 140 }}>
                <span className="text-xs font-medium block truncate" style={{ fontFamily: "'DM Mono', monospace", color: "var(--player-idle-title)" }}>Mono Engine Standby</span>
                <span className="text-[11px] font-mono text-[var(--text-muted)] block mt-0.5 truncate">-- / -- kHz · Awaiting Source</span>
              </div>
              <div className="flex items-center gap-2 shrink-0">
                {isHosting && (
                  <>
                    <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md text-[11px] font-mono font-bold tracking-wider bg-blue-500/10 text-blue-600 border border-blue-500/25 dark:bg-sky-400/12 dark:border-sky-400/40 dark:text-sky-300 select-none">
                      <span className="w-1.5 h-1.5 rounded-full bg-blue-600 dark:bg-sky-400 animate-pulse shrink-0" style={{ boxShadow: "0 0 6px rgba(56,189,248,0.6)" }} />
                      ON AIR
                    </span>
                    <button
                      type="button"
                      onClick={(e) => { e.stopPropagation(); onRequestStopHosting() }}
                      className="lounge-btn-end relative z-30 pointer-events-auto"
                    >
                      End Lounge
                    </button>
                  </>
                )}
                {isListener && (
                  <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md text-[11px] font-mono font-medium bg-blue-500/10 text-blue-400 border border-blue-500/30 select-none">
                    <span className="w-1.5 h-1.5 rounded-full bg-blue-500 shrink-0" />
                    SYNC LISTENER
                  </span>
                )}
                {!isHosting && !isListener && (
                  <button
                    type="button"
                    onClick={() => onStartLounge()}
                    className="btn-host-lounge"
                  >
                    <span className="btn-host-lounge-dot" />
                    Host Lounge
                  </button>
                )}
              </div>
            </div>
          )}
        </div>

        {/* Center — Controls + Progress */}
        <div className="player-center-section justify-self-center">
          <div className="player-controls-row">
            {/* Shuffle — 켜져 있으면 눈에 보여야 한다. 게스트는 호스트가 정한 상태를 보기만 한다. */}
            <button
              onClick={() => cmd.setShuffle(!shuffleOn)}
              className={`player-btn-icon player-btn-toggle ${shuffleOn ? "is-active" : ""} ${shuffleOn && (isHosting || isListener) ? "is-live" : ""}`}
              style={{ cursor: (isGuest || !currentTrack) ? "not-allowed" : "pointer", opacity: !currentTrack ? 0.3 : isGuest ? 0.55 : 1 }}
              disabled={isGuest || !currentTrack}
              title={isGuest
                ? `Shuffle ${shuffleOn ? "on" : "off"} — 호스트가 정합니다`
                : shuffleOn ? "Shuffle on" : "Shuffle off"}
              aria-pressed={shuffleOn}
            >
              <IconShuffle />
            </button>

            {/* Previous */}
            <button
              onClick={() => cmd.previous()}
              className="player-btn-icon"
              style={{ cursor: (isGuest || !currentTrack) ? "not-allowed" : "pointer", opacity: (isGuest || !currentTrack) ? 0.3 : 1 }}
              disabled={isGuest || !currentTrack}
              title="Previous track"
            >
              <IconPrev />
            </button>

            {/* Play / Pause */}
            <button
              onClick={() => currentTrack && !isListener && setPlaying(!playing)}
              title={isListener ? "Sync locked — controlled by host" : !currentTrack ? "No track loaded" : undefined}
              className={`player-btn-play ${(isHosting || isListener) ? "player-btn-blue" : "player-btn-violet"}`}
              style={{
                cursor: isListener ? "not-allowed" : !currentTrack ? "default" : "pointer",
                opacity: !currentTrack ? 0.4 : isListener ? 0.6 : 1,
                margin: "0 4px",
              }}
            >
              {isListener
                ? <Lock size={18} strokeWidth={2.2} />
                : playing && currentTrack
                ? <IconPause />
                : <IconPlay />}
            </button>

            {/* Next */}
            <button
              onClick={() => cmd.next()}
              className="player-btn-icon"
              style={{ cursor: (isGuest || !currentTrack) ? "not-allowed" : "pointer", opacity: (isGuest || !currentTrack) ? 0.3 : 1 }}
              disabled={isGuest || !currentTrack}
              title="Next track"
            >
              <IconNext />
            </button>

            {/* Loop / Repeat — off → all → one 을 돌고, 지금 어느 상태인지 아이콘에 남는다. */}
            <button
              onClick={() => cmd.setRepeat(nextRepeat)}
              className={`player-btn-icon player-btn-toggle ${repeatMode !== RepeatMode.Off ? "is-active" : ""} ${repeatMode !== RepeatMode.Off && (isHosting || isListener) ? "is-live" : ""}`}
              style={{ cursor: (isGuest || !currentTrack) ? "not-allowed" : "pointer", opacity: !currentTrack ? 0.3 : isGuest ? 0.55 : 1 }}
              disabled={isGuest || !currentTrack}
              title={isGuest ? `${repeatLabel} — 호스트가 정합니다` : repeatLabel}
              aria-pressed={repeatMode !== RepeatMode.Off}
            >
              {repeatMode === RepeatMode.One ? <IconLoopOne /> : <IconLoop />}
            </button>

            {/* Queue toggle */}
            <button
              onClick={(e) => { e.stopPropagation(); onToggleQueue() }}
              className={`player-btn-icon ml-3 gap-1.5 ${queueCount > 0 ? "is-active" : ""} ${(isHosting || isListener) ? "text-sky-600 dark:text-sky-400 dark:drop-shadow-[0_0_8px_rgba(56,189,248,0.5)]" : ""}`}
              style={{ cursor: "pointer", display: "flex", alignItems: "center" }}
              title={`Queue (${queueCount})`}
            >
              <IconQueue />
              {queueCount > 0 && (
                <span className="transport-queue-count font-normal">{queueCount}</span>
              )}
            </button>
          </div>

          {/* Progress row — strictly: elapsed | scrubber | total */}
          <div className="player-timeline-container">
            <span className="player-time-display player-time-display--left">
              {currentTrack ? fmt(currentSecs) : "00:00"}
            </span>

            <div
              className="bg-zinc-200 dark:bg-zinc-800"
              style={{ flex: 1, position: "relative", height: 3, borderRadius: 2, cursor: (isListener || !currentTrack) ? "not-allowed" : "pointer" }}
            >
              {currentTrack && (
                <div className={`h-full rounded-full transition-all duration-300 ${seekbarColor}`} style={{ position: "absolute", left: 0, top: 0, width: `${progress || 35}%`, pointerEvents: "none" }} />
              )}
              <input
                type="range" min={0} max={100} step={0.1} value={currentTrack ? progress : 0}
                onChange={(e) => { if (!isListener && currentTrack) setProgress(Number(e.target.value)) }}
                onPointerUp={() => {
                  if (scrubbing === null || durationMs <= 0) return
                  cmd.seek((scrubbing / 100) * durationMs)
                  setScrubbing(null)
                }}
                onKeyUp={() => {
                  if (scrubbing === null || durationMs <= 0) return
                  cmd.seek((scrubbing / 100) * durationMs)
                  setScrubbing(null)
                }}
                disabled={!currentTrack || isListener}
                style={{ position: "absolute", inset: 0, width: "100%", height: "100%", opacity: 0, cursor: (isListener || !currentTrack) ? "not-allowed" : "pointer", margin: 0 }}
              />
            </div>

            <span className="player-time-display player-time-display--right">
              {currentTrack ? fmt(totalSecs) : "--:--"}
            </span>
          </div>
        </div>

        {/* Right — Signal pip + device pill + volume */}
        <div className="justify-self-end flex items-center justify-end gap-3">
          {/* Signal Path pip */}
          <button
            onClick={() => setIsSignalPathOpen(!isSignalPathOpen)}
            title={isDspBypassed ? "Bit-Perfect Mode" : "Active DSP/EQ"}
            style={{
              width: 8, height: 8, borderRadius: "50%", flexShrink: 0,
              background: isDspBypassed ? "#9333EA" : "#2563EB",
              boxShadow: isDspBypassed ? "0 0 0 4px rgba(147,51,234,0.18)" : "0 0 0 4px rgba(37,99,235,0.15)",
              border: "none", cursor: "pointer", padding: 0, transition: "background 0.2s, box-shadow 0.2s",
            }}
          />

          {/* Device Zone Pill */}
          {(() => {
            const activeDevs = devices.filter((d) => d.active)
            const first = activeDevs[0]
            // 아이콘은 JSX 다. 문자열에 넣으면 "[object Object] 장치이름" 이 그대로 찍힌다.
            const label = first
              ? activeDevs.length > 1
                ? `${first.name} +${activeDevs.length - 1}`
                : first.name
              : "No Output Selected"
            return (
              <button
                onClick={() => setIsDevicePopoverOpen(!isDevicePopoverOpen)}
                className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full text-xs font-medium bg-[var(--surface-elevated)] border border-[var(--border-subtle)] text-[var(--text-primary)] hover:border-[var(--border-strong)] transition-colors"
                style={{ cursor: "pointer", whiteSpace: "nowrap", maxWidth: 200, overflow: "hidden", textOverflow: "ellipsis", fontFamily: "inherit" }}
              >
                {first?.icon}
                <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{label}</span>
                <IconChevron size={10} />
              </button>
            )
          })()}

          {/* Volume */}
          <div className="flex items-center gap-2">
            <span className="text-[11px] font-mono text-[var(--text-secondary)] font-medium select-none" style={{ whiteSpace: "nowrap" }}>
              {(() => { const db = -60 + (volume / 100) * 60; return db === 0 ? "0.0 dB" : `${db.toFixed(1)} dB` })()}
            </span>
            <div style={{ position: "relative", width: 80, height: 3, background: "var(--surface-elevated)", borderRadius: 2 }}>
              <div style={{ position: "absolute", left: 0, top: 0, height: "100%", width: `${volume}%`, background: "var(--accent-violet)", borderRadius: 2, pointerEvents: "none" }} />
              <input
                type="range" min={0} max={100} value={volume}
                onChange={(e) => setVolume(Number(e.target.value))}
                style={{ position: "absolute", inset: 0, width: "100%", height: "100%", opacity: 0, cursor: "pointer", margin: 0 }}
              />
            </div>
          </div>
        </div>
      </footer>

      {/* Signal Path Inspector Popover */}
      {isSignalPathOpen && (
        <div
          className="signal-path-popover"
          data-component="signal-path-modal"
          style={{
            position: "fixed", bottom: 96, right: 32, width: 340,
            borderRadius: 16, padding: 20, zIndex: 60,
          }}
        >
          <div className="flex items-start justify-between mb-4">
            <div>
              <div className="sp-title" style={{ fontSize: 13, fontWeight: 700 }}>Audio Signal Path</div>
              <div className="sp-subtitle" style={{ fontSize: 11, marginTop: 2 }}>Living Room · May L3</div>
            </div>
            <button
              onClick={() => setIsSignalPathOpen(false)}
              className="sp-close"
              style={{ background: "none", border: "none", cursor: "pointer", padding: "2px 4px", fontFamily: "inherit", transition: "color 0.15s", display: "flex", alignItems: "center" }}
              aria-label="Close"
            >
              <MonoIcon.Close size={14} />
            </button>
          </div>

          <div style={{ display: "flex", flexDirection: "column", gap: 0 }}>
            <div style={{ paddingLeft: 0, paddingBottom: 0 }}>
              <div style={{ display: "flex", alignItems: "flex-start", gap: 10 }}>
                <div style={{ display: "flex", flexDirection: "column", alignItems: "center", flexShrink: 0 }}>
                  <div className="sp-dot-source" style={{ width: 8, height: 8, borderRadius: "50%", marginTop: 3, flexShrink: 0 }} />
                  <div className="sp-rail" style={{ width: 1, height: 24, borderLeft: "1px dashed var(--border-subtle)", marginTop: 3 }} />
                </div>
                <div style={{ paddingBottom: 16 }}>
                  <div className="sp-node-title" style={{ fontSize: 12, fontWeight: 600 }}>{sourceLabel}</div>
                  <div className="sp-node-meta" style={{ fontSize: 11, marginTop: 1 }}>
                    {currentTrack ? `${currentTrack.title} — ${currentTrack.artist}` : "재생 중인 곡이 없습니다"}
                  </div>
                </div>
              </div>
            </div>

            <div style={{ paddingBottom: 0 }}>
              <div style={{ display: "flex", alignItems: "flex-start", gap: 10 }}>
                <div style={{ display: "flex", flexDirection: "column", alignItems: "center", flexShrink: 0 }}>
                  <div className={isDspBypassed ? "sp-dot-dsp" : "sp-dot-dsp"} style={{ width: 8, height: 8, borderRadius: "50%", marginTop: 3, flexShrink: 0, opacity: isDspBypassed ? 0.35 : 1, transition: "opacity 0.2s" }} />
                  <div className="sp-rail" style={{ width: 1, height: 24, borderLeft: "1px dashed var(--border-subtle)", marginTop: 3 }} />
                </div>
                <div style={{ paddingBottom: 16, flex: 1 }}>
                  <div style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 4 }}>
                    <span className="sp-node-title" style={{ fontSize: 12, fontWeight: 600, opacity: isDspBypassed ? 0.55 : 1, transition: "opacity 0.2s" }}>Parametric EQ</span>
                    {isDspBypassed && (
                      <span className="sp-bypassed-badge" style={{ fontFamily: "'DM Mono', monospace", fontSize: 9, fontWeight: 600, borderRadius: 4, padding: "1px 6px", border: "1px solid" }}>
                        Bypassed
                      </span>
                    )}
                  </div>
                  <button
                    onClick={() => {
                      const next = !isDspBypassed
                      setIsDspBypassed(next)
                      cmd.setDsp(room?.dspPreset ?? 0, !next)
                    }}
                    className="sp-bypass-btn"
                    style={{
                      fontSize: 11, fontWeight: 500,
                      borderRadius: 6, padding: "4px 10px", cursor: "pointer",
                      fontFamily: "inherit", transition: "all 0.2s", display: "flex", alignItems: "center", gap: 6,
                      border: "1px solid",
                    }}
                  >
                    <span
                      className="sp-toggle-track"
                      style={{
                        width: 24, height: 14, borderRadius: 7,
                        display: "inline-flex", alignItems: "center", padding: "0 2px",
                        transition: "background 0.2s", flexShrink: 0,
                        background: isDspBypassed ? "var(--accent-violet)" : "var(--border-strong)",
                      }}
                    >
                      <span style={{
                        width: 10, height: 10, borderRadius: "50%", background: "#FFFFFF",
                        transform: isDspBypassed ? "translateX(10px)" : "translateX(0)",
                        transition: "transform 0.2s", display: "block",
                      }} />
                    </span>
                    Bypass DSP (Bit-Perfect Mode)
                  </button>
                </div>
              </div>
            </div>

            <div style={{ paddingBottom: 0 }}>
              <div style={{ display: "flex", alignItems: "flex-start", gap: 10 }}>
                <div style={{ display: "flex", flexDirection: "column", alignItems: "center", flexShrink: 0 }}>
                  <div className="sp-dot-hw" style={{ width: 8, height: 8, borderRadius: "50%", marginTop: 3, flexShrink: 0 }} />
                  <div className="sp-rail" style={{ width: 1, height: 24, borderLeft: "1px dashed var(--border-subtle)", marginTop: 3 }} />
                </div>
                <div style={{ paddingBottom: 16 }}>
                  <div className="sp-node-title" style={{ fontSize: 12, fontWeight: 600 }}>{driverLabel}</div>
                  <div className="sp-node-meta" style={{ fontSize: 11, marginTop: 1 }}>{bufferLabel}</div>
                </div>
              </div>
            </div>

            <div>
              <div style={{ display: "flex", alignItems: "flex-start", gap: 10 }}>
                <div style={{ flexShrink: 0, marginTop: 3 }}>
                  <div className="sp-dot-dac" style={{ width: 8, height: 8, borderRadius: "50%" }} />
                </div>
                <div>
                  <div className="sp-node-title" style={{ fontSize: 12, fontWeight: 600 }}>{dacLabel}</div>
                  <div className="sp-node-meta" style={{ fontSize: 11, marginTop: 1 }}>{dacDetail}</div>
                </div>
              </div>
            </div>
          </div>

          <div className="sp-divider" style={{ borderTop: "1px solid var(--border-subtle)", margin: "16px 0 12px" }} />
          <div className="flex items-center justify-between">
            <span className="sp-status-badge" style={{ fontSize: 11, fontWeight: 500 }}>
              {isDspBypassed ? (
                <span className="flex items-center gap-1.5">
                  <MonoIcon.BitPerfect size={15} />
                  <span>Bit-Perfect Path</span>
                </span>
              ) : (
                <span className="flex items-center gap-1.5">
                  <MonoIcon.DspActive size={15} />
                  <span>Studio DSP Active</span>
                </span>
              )}
            </span>
            <button
              onClick={onNavigateToLens}
              className="sp-configure-link"
              style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, background: "none", border: "none", cursor: "pointer", padding: 0, transition: "color 0.15s", display: "flex", alignItems: "center", gap: 3 }}
            >
              <span>LENS</span>
              <MonoIcon.ExternalLink size={10} />
            </button>
          </div>
          <a
            href="#"
            className="sp-configure-link"
            style={{ fontSize: 11, display: "block", marginTop: 10, cursor: "pointer", textDecoration: "none" }}
            onClick={(e) => e.preventDefault()}
          >
            Configure Audio Engine & EQ in Settings →
          </a>
        </div>
      )}

      {/* Audio Outputs Popover */}
      {isDevicePopoverOpen && (() => {
        const visibleDevs = devices.filter((d) => !d.isHidden)
        const hiddenDevs = devices.filter((d) => d.isHidden)
        const activeDevs = devices.filter((d) => d.active)
        const dbFromVol = (v: number) => v === 0 ? "0.0 dB" : `${v.toFixed(1)} dB`

        // 장치 on/off 는 Core 에 볼륨 0 으로 나간다 — Control 에는 출력을 룸에서
        // 떼어내는 명령이 없고, 무음은 그 자리에서 되돌릴 수 있다.
        const toggleDevice = (id: string) => {
          const dev = devices.find((d) => d.id === id)
          cmd.setVolume(dev?.active ? 0 : 100, id)
        }
        const selectSolo = (id: string) => {
          for (const d of devices) cmd.setVolume(d.id === id ? 100 : 0, d.id)
        }
        const hideDevice = (id: string) => {
          setHiddenDeviceIds((prev) => prev.includes(id) ? prev : [...prev, id])
          cmd.setVolume(0, id)
          // 이 PC 에서 띄운 워커가 마지막 하나였다면 프로세스도 내린다.
          if (devices.length === 1) void stopOutput()
        }
        const showDevice = (id: string) => {
          setHiddenDeviceIds((prev) => prev.filter((x) => x !== id))
        }
        const DEFAULT_VOLUME_DB = -18.0
        const setDeviceVolume = (id: string, db: number) => {
          cmd.setVolume(dbToPercent(db), id)
        }
        const resetDeviceVolume = (e: React.MouseEvent, id: string) => {
          e.preventDefault()
          e.stopPropagation()
          setDeviceVolume(id, DEFAULT_VOLUME_DB)
        }

        return (
          <div
            className="signal-path-popover"
            style={{
              position: "fixed", bottom: 96, right: 32, width: 360,
              borderRadius: 18, padding: 20, zIndex: 60, userSelect: "none",
            }}
          >
            <div className="flex items-center justify-between" style={{ marginBottom: 14 }}>
              <span className="sp-title" style={{ fontSize: 13, fontWeight: 700 }}>Audio Outputs</span>
              <button
                onClick={() => setIsDevicePopoverOpen(false)}
                className="sp-close"
                style={{ background: "none", border: "none", cursor: "pointer", padding: "2px 4px", fontFamily: "inherit", transition: "color 0.15s", display: "flex", alignItems: "center" }}
                aria-label="Close"
              >
                <MonoIcon.Close size={14} />
              </button>
            </div>

            {activeDevs.length >= 2 && (
              <div style={{ background: "var(--surface-elevated)", border: "1px solid var(--border-subtle)", borderRadius: 10, padding: "10px 12px", marginBottom: 14 }}>
                <div className="sp-node-meta" style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, fontWeight: 600, letterSpacing: "0.08em", textTransform: "uppercase", marginBottom: 8 }}>
                  Group Master Volume
                </div>
                <div className="flex items-center gap-3">
                  <input
                    type="range" min={-60} max={0} step={0.5}
                    value={activeDevs[0]?.volume ?? -18}
                    onChange={(e) => {
                      const db = Number(e.target.value)
                      for (const d of activeDevs) cmd.setVolume(dbToPercent(db), d.id)
                    }}
                    style={{ flex: 1, accentColor: "#334155", cursor: "pointer" }}
                  />
                  <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "#475569", minWidth: 54, textAlign: "right" }}>
                    {dbFromVol(activeDevs[0]?.volume ?? -18)}
                  </span>
                </div>
              </div>
            )}

            {visibleDevs.length === 0 && (
              <div style={{ padding: "18px 4px 10px", textAlign: "center" }}>
                <div style={{ fontSize: 12.5, color: "#475569", marginBottom: 4 }}>연결된 출력 장치가 없습니다.</div>
                <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10.5, color: "#94A3B8", marginBottom: 14, lineHeight: 1.5 }}>
                  Mono Output 을 이 PC 에 붙이면 비트퍼펙트로 재생됩니다.
                </div>
                <button
                  onClick={() => { void attachOutput() }}
                  style={{ padding: "9px 18px", borderRadius: 9, border: "none", background: "#2563EB", color: "#fff", fontSize: 12.5, fontWeight: 600, cursor: "pointer", fontFamily: "inherit" }}
                >
                  출력 연결
                </button>
                {outputError && (
                  <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10.5, color: "#DC2626", marginTop: 10, lineHeight: 1.5 }}>{outputError}</div>
                )}
              </div>
            )}

            <div style={{ display: "flex", flexDirection: "column", gap: 2 }}>
              {visibleDevs.map((dev) => (
                <div
                  key={dev.id}
                  className="group"
                  style={{
                    display: "flex", alignItems: "flex-start", gap: 10, padding: "10px 10px",
                    borderRadius: 12, transition: "background 0.12s", cursor: "pointer", position: "relative",
                  }}
                  onMouseEnter={(e) => ((e.currentTarget as HTMLDivElement).style.background = "#F8FAFC")}
                  onMouseLeave={(e) => ((e.currentTarget as HTMLDivElement).style.background = "transparent")}
                >
                  <button
                    onClick={(e) => { e.stopPropagation(); if (!dev.isLocalWorker) toggleDevice(dev.id) }}
                    style={{
                      width: 18, height: 18, borderRadius: 5, flexShrink: 0, marginTop: 1,
                      border: dev.active ? "none" : "1.5px solid #CBD5E1",
                      background: dev.active ? "#2563EB" : "transparent",
                      display: "flex", alignItems: "center", justifyContent: "center",
                      cursor: "pointer", transition: "all 0.15s", color: "#fff", fontSize: 11, fontWeight: 700,
                    }}
                  >
                    {dev.active && <MonoIcon.Check size={12} strokeWidth={2.5} />}
                  </button>

                  <div style={{ flex: 1, minWidth: 0 }} onClick={() => { if (!dev.isLocalWorker) selectSolo(dev.id) }}>
                    <div style={{ fontSize: 12, fontWeight: 600, color: dev.active ? "#0F172A" : "#374151", marginBottom: 1, display: "flex", alignItems: "center" }}>
                      {dev.icon} <span>{dev.name}</span>
                    </div>
                    <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, color: "#94A3B8", marginBottom: dev.active && !dev.isLocalWorker ? 8 : 0 }}>
                      {dev.spec}
                    </div>
                    {dev.isLocalWorker && (
                      <button
                        onClick={(e) => { e.stopPropagation(); void attachOutput() }}
                        style={{ marginTop: 8, padding: "6px 12px", borderRadius: 8, border: "none", background: "#2563EB", color: "#fff", fontSize: 11.5, fontWeight: 600, cursor: "pointer", fontFamily: "inherit" }}
                      >
                        룸에 연결
                      </button>
                    )}
                    {dev.active && !dev.isLocalWorker && (
                      <div className="flex items-center gap-2" onClick={(e) => e.stopPropagation()}>
                        <input
                          type="range" min={-60} max={0} step={0.5}
                          value={dev.volume}
                          onChange={(e) => setDeviceVolume(dev.id, Number(e.target.value))}
                          onDoubleClick={(e) => resetDeviceVolume(e, dev.id)}
                          title={`Double-click to reset to default (${DEFAULT_VOLUME_DB.toFixed(1)} dB)`}
                          className="device-vol-slider"
                          style={{ width: 120, accentColor: "#334155", cursor: "pointer" }}
                        />
                        <span
                          onDoubleClick={(e) => resetDeviceVolume(e, dev.id)}
                          title={`Double-click to reset to default (${DEFAULT_VOLUME_DB.toFixed(1)} dB)`}
                          className="device-vol-db"
                          style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, color: "#475569", minWidth: 50 }}
                        >
                          {dbFromVol(dev.volume)}
                        </span>
                      </div>
                    )}
                    {outputError && dev.isLocalWorker && (
                      <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10.5, color: "#DC2626", marginTop: 8, lineHeight: 1.5 }}>{outputError}</div>
                    )}
                  </div>

                  <button
                    onClick={(e) => { e.stopPropagation(); if (!dev.isLocalWorker) hideDevice(dev.id) }}
                    style={{
                      background: "none", border: "none", cursor: "pointer", padding: "2px 4px",
                      color: "#CBD5E1", transition: "color 0.15s", flexShrink: 0, marginTop: 2, display: "flex", alignItems: "center",
                    }}
                    title="Hide device"
                    aria-label="Hide device"
                    onMouseEnter={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "#F43F5E")}
                    onMouseLeave={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "#CBD5E1")}
                  >
                    <MonoIcon.Close size={12} />
                  </button>
                </div>
              ))}
            </div>

            {hiddenDevs.length > 0 && (
              <>
                <button
                  onClick={() => setShowHiddenDevices(!showHiddenDevices)}
                  style={{
                    width: "100%", textAlign: "left", background: "none", border: "none",
                    borderTop: "1px solid #F1F5F9", marginTop: 8, paddingTop: 10, paddingBottom: 4,
                    fontSize: 12, color: "#94A3B8", cursor: "pointer", fontFamily: "inherit",
                    display: "flex", alignItems: "center", gap: 5, transition: "color 0.15s",
                  }}
                  onMouseEnter={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "#475569")}
                  onMouseLeave={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "#94A3B8")}
                >
                  <MonoIcon.ChevronDown size={12} style={{ transform: showHiddenDevices ? "rotate(0deg)" : "rotate(-90deg)", transition: "transform 0.15s" }} />
                  {hiddenDevs.length} Hidden & System Device{hiddenDevs.length !== 1 ? "s" : ""}
                </button>
                {showHiddenDevices && (
                  <div style={{ display: "flex", flexDirection: "column", gap: 4, marginTop: 4 }}>
                    {hiddenDevs.map((dev) => (
                      <div
                        key={dev.id}
                        className="hidden-device-card"
                        style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}
                      >
                        <div>
                          <span className="hidden-device-card__name">{dev.icon} {dev.name}</span>
                          <div className="hidden-device-card__spec">{dev.spec}</div>
                        </div>
                        <button
                          className="btn-device-show"
                          onClick={() => showDevice(dev.id)}
                        >
                          + Show
                        </button>
                      </div>
                    ))}
                  </div>
                )}
              </>
            )}
          </div>
        )
      })()}
    </>
  )
}
