import type { QueueTrack, LoungeRoom } from "../../data/types"
import type { ShareData } from "../ShareModal"
import type { PlayerMode } from "../PlayerBar"
import { useMono, useMediaClock, useMonoCommands } from "../../state/MonoProvider"
import { useLiveSession } from "../../state/useLiveSession"
import { initials as initialsOf } from "../../lib/adapters"
import { MonoIcon } from "../icons/MonoIcons"

/**
 * 리액션 한 벌. key 는 Core 로 나가는 값이라 이모지 문자 그대로 두고(룸 전체가 이 값으로
 * 합의한다), 화면에 그리는 건 아이콘 쪽이다. 팝오버와 떠오르는 잔상이 같은 목록을 봐야
 * 버튼과 실제로 날아가는 그림이 어긋나지 않는다.
 */
const REACTIONS = [
  { key: "🔥", title: "Fire",       Icon: MonoIcon.Flame,      tone: "text-orange-500" },
  { key: "🎧", title: "Headphones", Icon: MonoIcon.Headphones, tone: "text-sky-500" },
  { key: "🎷", title: "Jazz",       Icon: MonoIcon.Saxophone,  tone: "text-amber-500" },
  { key: "🍷", title: "Lounge",     Icon: MonoIcon.Wine,       tone: "text-rose-500" },
  { key: "👏", title: "Clap",       Icon: MonoIcon.Clap,       tone: "text-violet-400" },
] as const

interface FloatingReaction {
  id: number
  emoji: string
  left: number
  rotate: number
  scale: number
}

interface Props {
  currentTrack: QueueTrack | null
  playerMode: PlayerMode
  joinedLoungeId: string | null
  hostSessionPrivacy: "public" | "unlisted"
  hostSessionSecretKey: string | undefined
  cueSearchQuery: string
  setCueSearchQuery: (q: string) => void
  onCueTrack: (t: QueueTrack) => void
  chatInput: string
  setChatInput: (v: string) => void
  showReactions: boolean
  setShowReactions: (v: boolean) => void
  floatingReactions: FloatingReaction[]
  triggerReaction: (emoji: string) => void
  isGearWallExpanded: boolean
  setIsGearWallExpanded: (v: boolean) => void
  onExitAttempt: () => void
  onOpenShare: (data: ShareData) => void
  theme?: "light" | "dark"
  /** Core 의 실제 룸 목록. joinedLoungeId 로 지금 방을 찾는다. */
  rooms: LoungeRoom[]
}

export default function LiveLoungeRoom({
  currentTrack,
  playerMode,
  joinedLoungeId,
  hostSessionPrivacy,
  hostSessionSecretKey,
  cueSearchQuery,
  setCueSearchQuery,
  onCueTrack,
  chatInput,
  setChatInput,
  showReactions,
  setShowReactions,
  floatingReactions,
  triggerReaction,
  isGearWallExpanded,
  setIsGearWallExpanded,
  onExitAttempt,
  onOpenShare,
  theme = "dark",
  rooms,
}: Props) {
  const isDark = theme === "dark"
  const isHost = playerMode === "host"
  const room: LoungeRoom | undefined = rooms.find(r => r.id === joinedLoungeId)
  const { room: snapshot } = useMono()
  const cmd = useMonoCommands()
  const { albums } = useLiveSession()
  // 출력 엔드포인트는 청취자가 아니다 — 사람만 센다.
  const listenerCount = snapshot?.members.filter((m) => !m.isOutput).length ?? 0

  // 진행률은 Core 의 미디어 시계가 진실이다. 호스트가 끌면 룸 전체가 같이 움직인다.
  const { positionMs, durationMs } = useMediaClock()
  const progress = durationMs > 0 ? (positionMs / durationMs) * 100 : 0
  const currentSecs = Math.round(positionMs / 1000)
  const fmt = (s: number) => {
    if (!s || isNaN(s) || s < 0) return "00:00"
    return `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(Math.floor(s % 60)).padStart(2, "0")}`
  }

  const isUnlistedSession = isHost
    ? hostSessionPrivacy === "unlisted"
    : room?.visibility === "unlisted"

  const shareData: ShareData | null = isHost
    ? {
        type: "lounge",
        id: "my-live-session",
        title: "Live Audiophile Session",
        subtitle: "Hosted by You · Live Stream",
        audioSpec: "Bit-Perfect ASIO Master Stream",
        hostGear: "Your Current Output DAC / Signal Chain",
        visibility: hostSessionPrivacy,
        secretKey: hostSessionSecretKey,
      }
    : room
      ? {
          type: "lounge",
          id: room.id,
          title: room.title,
          subtitle: `Host: ${room.hostName} · ${room.currentArtist}`,
          audioSpec: room.audioSpec,
          hostGear: room.hostGear,
          visibility: room.visibility,
          secretKey: room.secretKey,
        }
      : null

  return (
    <div className="flex-1 flex flex-col h-[calc(100vh-10rem)] overflow-hidden bg-[var(--chassis-bg)] text-[var(--text-primary)]">

      {/* Sub-Header Bar */}
      <div className="h-14 border-b border-[var(--border-subtle)] px-6 py-3 flex items-center justify-between bg-[var(--chassis-bg)] dark:bg-[#16171c]/80 dark:backdrop-blur-md shrink-0">
        <div className="flex items-center">
          <button
            onClick={onExitAttempt}
            className="text-[var(--text-secondary)] hover:text-[var(--text-primary)] flex items-center gap-1.5 text-xs transition-colors border border-[var(--border-subtle)] px-3.5 py-1.5 rounded-full cursor-pointer mr-4"
            style={{ background: "none" }}
          >
            <MonoIcon.ArrowLeft size={13} />
            <span>Leave Lounge</span>
          </button>
          <span className="text-xs font-semibold text-[var(--text-primary)] tracking-wide mr-3">
            {currentTrack
              ? currentTrack.title
              : isHost
              ? "Live Lounge · Standby"
              : room
              ? `${room.title} · Host: ${room.hostName}`
              : "Live Lounge · Standby"}
          </span>
          <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium bg-emerald-500/10 text-emerald-700 dark:text-emerald-400 border border-emerald-500/20 dark:bg-white/5 dark:border-white/10 transition-colors">
            <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse shrink-0" />
            <span>{listenerCount} Listening</span>
          </span>
          {(Boolean(joinedLoungeId) || isHost) && shareData && (
            <>
              {isUnlistedSession && (
                <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-mono font-medium bg-zinc-800 text-violet-300 border border-violet-500/30 ml-2">
                  <MonoIcon.Lock size={12} />
                  <span>Invite-Only Session</span>
                </span>
              )}
              <button
                onClick={() => onOpenShare(shareData)}
                className="btn-lounge-share ml-2"
                title={isHost ? "Invite listeners to your live session" : "Share this live session"}
              >
                <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><circle cx="18" cy="5" r="3"/><circle cx="6" cy="12" r="3"/><circle cx="18" cy="19" r="3"/><line x1="8.59" y1="13.51" x2="15.42" y2="17.49"/><line x1="15.41" y1="6.51" x2="8.59" y2="10.49"/></svg>
                <span>{isHost ? "Invite Listeners" : "Share"}</span>
              </button>
            </>
          )}
        </div>
        <span className="badge-stream-format">
          <span style={{ width: 6, height: 6, borderRadius: "50%", background: "currentColor", flexShrink: 0, opacity: 0.8 }} />
          {currentTrack?.format ?? "Direct Bitstream Ready"}
        </span>
      </div>

      {/* 2-Column Body */}
      <div className="flex-1 grid grid-cols-12 overflow-hidden">

        {/* Left Stage */}
        <div className="col-span-12 lg:col-span-8 flex flex-col items-center justify-center p-8 border-r border-[var(--border-subtle)] overflow-y-auto bg-[var(--chassis-bg)]">
          {currentTrack ? (
            <>
              <div className="w-[380px] md:w-[440px] aspect-square rounded-2xl border border-slate-100 shadow-2xl overflow-hidden mb-6">
                {(currentTrack.art || (currentTrack as { coverUrl?: string }).coverUrl)
                  ? <img src={currentTrack.art || (currentTrack as { coverUrl?: string }).coverUrl} alt={currentTrack.title} className="object-cover w-full h-full" />
                  : <div className="w-full h-full bg-zinc-100 flex items-center justify-center">
                      <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="#A1A1AA" strokeWidth="1" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="10"/><circle cx="12" cy="12" r="3"/></svg>
                    </div>
                }
              </div>
              <div className="lounge-stage-title mb-1">{currentTrack.title}</div>
              <div className="text-sm text-slate-600 dark:text-zinc-400 mb-4">{currentTrack.artist}{currentTrack.album ? ` — ${currentTrack.album}` : ""}</div>
              <div className="lounge-central-card w-full max-w-md backdrop-blur-md rounded-2xl p-4 text-center">
                <span className="text-xs font-mono tracking-wider mb-2 block text-zinc-400 dark:text-zinc-500"><span className="text-sky-600 dark:text-sky-400 animate-pulse">●</span> Bit-Perfect Stream Sync Locked (Host Controlled)</span>
                <div
                  className="h-1.5 w-full rounded-full overflow-hidden bg-zinc-200 dark:bg-zinc-800 relative"
                  style={isHost ? { cursor: "pointer" } : undefined}
                  onClick={isHost && durationMs > 0 ? (e) => {
                    const rect = (e.currentTarget as HTMLDivElement).getBoundingClientRect()
                    const pct = Math.min(1, Math.max(0, (e.clientX - rect.left) / rect.width))
                    cmd.seek(pct * durationMs)
                  } : undefined}
                >
                  <div className="h-full bg-sky-600 dark:bg-sky-400 rounded-full transition-all duration-300" style={{ width: `${progress}%` }} />
                </div>
                <div className="text-xs font-mono mt-2 text-zinc-400 dark:text-zinc-500">{fmt(currentSecs)} / {currentTrack.duration ?? "--:--"}</div>
              </div>
            </>
          ) : isHost ? (
            /* Host Standby — Studio Master Cue Console */
            (() => {
              const QUICK_CUE = albums.slice(0, 4)
              const cueQ = cueSearchQuery.toLowerCase().trim()
              const cueResults = cueQ.length > 0
                ? albums.filter(a =>
                    a.title.toLowerCase().includes(cueQ) ||
                    a.artist.toLowerCase().includes(cueQ)
                  ).slice(0, 6)
                : []
              // 앨범을 큐에 올린다 — Core 에는 앨범 전체가 큐로 들어간다.
              const cueTrack = (album: (typeof albums)[number]) => {
                void cmd.playAlbum(album.trackIds)
                onCueTrack({
                  id: album.trackIds[0] ?? album.id,
                  title: album.title,
                  artist: album.artist,
                  album: album.title,
                  duration: "--:--",
                  format: album.format,
                  dr: album.dr,
                  art: album.coverUrl,
                  source: "local",
                })
                setCueSearchQuery("")
              }
              return (
                <div className="w-full max-w-2xl mx-auto px-4 flex flex-col items-center">
                  <p className="text-blue-600 dark:text-blue-400 font-mono text-[10px] tracking-widest font-semibold uppercase text-center mb-1">Broadcast Active · Cue Opening Track</p>
                  <p className="text-[var(--text-secondary)] text-xs text-center mb-6">Search the catalogue or select a master record below to initiate room stream.</p>

                  <div className="w-full mx-auto relative mb-2">
                    <span className="absolute left-3 top-1/2 -translate-y-1/2 text-zinc-400 pointer-events-none">
                      <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/></svg>
                    </span>
                    <input
                      type="text"
                      placeholder="Search master records, artist, album..."
                      value={cueSearchQuery}
                      onChange={e => setCueSearchQuery(e.target.value)}
                      className="h-10 w-full pl-9 pr-4 rounded-xl bg-[var(--surface-card)] dark:bg-[#1a1b22] border border-[var(--border-subtle)] dark:border-white/10 text-sm text-[var(--text-primary)] dark:text-zinc-100 placeholder:text-[var(--text-muted)] dark:placeholder:text-zinc-500 focus:border-blue-500 focus:outline-none transition-all"
                    />
                    {cueResults.length > 0 && (
                      <div className="absolute left-0 right-0 top-full mt-1.5 bg-[var(--surface-card)] border border-[var(--border-subtle)] rounded-xl shadow-xl overflow-hidden z-20">
                        {cueResults.map(album => (
                          <button
                            key={album.title}
                            onClick={() => cueTrack(album)}
                            className="w-full flex items-center gap-3 px-3 py-2.5 hover:bg-[var(--surface-hover)] transition-colors text-left cursor-pointer border-b border-[var(--border-subtle)] last:border-0"
                            style={{ background: "none", fontFamily: "inherit" }}
                          >
                            <img src={album.art} alt={album.title} className="w-8 h-8 rounded object-cover border border-zinc-200 shrink-0" />
                            <div className="flex-1 min-w-0">
                              <div className="text-xs font-medium text-zinc-900 truncate">{album.title}</div>
                              <div className="text-[11px] text-zinc-500 truncate">{album.artist}</div>
                            </div>
                            <span className="text-[9px] font-mono w-auto whitespace-nowrap px-2 py-0.5 rounded bg-[var(--surface-elevated)] text-[var(--text-muted)] shrink-0 ml-2">{album.format || "24-Bit / 192kHz"}</span>
                          </button>
                        ))}
                      </div>
                    )}
                  </div>

                  <div className="w-full mx-auto mt-5">
                    <p className="text-[10px] font-mono text-zinc-400 tracking-wider mb-2 uppercase">Quick Cue Masters</p>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 w-full">
                      {QUICK_CUE.map(album => (
                        <button
                          key={album.title}
                          onClick={() => cueTrack(album)}
                          className="bg-[var(--surface-card)] hover:bg-[var(--surface-hover)] dark:bg-[#1a1b22]/90 dark:hover:bg-[#22242c] border border-[var(--border-subtle)] dark:border-white/10 hover:dark:border-white/20 rounded-xl p-3 flex items-center gap-3 transition-all cursor-pointer text-left"
                          style={{ fontFamily: "inherit" }}
                        >
                          <img src={album.art} alt={album.title} className="w-10 h-10 rounded-lg shrink-0 object-cover" />
                          <div className="min-w-0 flex-1 flex flex-col justify-center">
                            <span className="text-xs font-semibold text-[var(--text-primary)] truncate block">{album.title}</span>
                            <span className="text-[11px] text-[var(--text-secondary)] truncate block">{album.artist}</span>
                          </div>
                          <span className="shrink-0 px-2.5 py-1 rounded text-[10px] font-mono font-medium tracking-tight bg-[var(--spec-badge-bg)] dark:bg-white/5 text-[var(--spec-badge-text)] dark:text-zinc-300 border border-[var(--spec-badge-border)] dark:border-white/10 whitespace-nowrap">{album.format || "24-Bit / 192kHz"}</span>
                        </button>
                      ))}
                    </div>
                  </div>
                </div>
              )
            })()
          ) : (
            /* Listener Standby */
            <div className="flex flex-col items-center gap-6 max-w-sm text-center">
              <div className="w-48 h-48 rounded-2xl bg-zinc-100 border border-zinc-200 flex items-center justify-center">
                <svg width="56" height="56" viewBox="0 0 24 24" fill="none" stroke="#D4D4D8" strokeWidth="1" strokeLinecap="round" strokeLinejoin="round">
                  <circle cx="12" cy="12" r="10"/>
                  <circle cx="12" cy="12" r="3"/>
                  <line x1="12" y1="2" x2="12" y2="5"/>
                  <line x1="12" y1="19" x2="12" y2="22"/>
                  <line x1="2" y1="12" x2="5" y2="12"/>
                  <line x1="19" y1="12" x2="22" y2="12"/>
                </svg>
              </div>
              <div>
                <div className="text-lg font-semibold text-zinc-700 mb-1">Live Lounge · Standby</div>
                <div className="text-sm text-zinc-400 font-mono">Host is queuing master records</div>
              </div>
              <div className="lounge-central-card rounded-xl p-4 w-full">
                <div className="text-[11px] font-mono text-zinc-400 dark:text-zinc-500 mb-2">
                  <span className="inline-block w-1.5 h-1.5 rounded-full bg-sky-600 dark:bg-sky-400 mr-1.5 align-middle" />
                  Direct Bitstream Lock Active · Waiting for host playback
                </div>
                <div className="h-1.5 w-full bg-zinc-200 dark:bg-zinc-800 rounded-full" />
                <div className="text-[10px] font-mono text-zinc-400 dark:text-zinc-500 mt-2">00:00 / --:--</div>
              </div>
            </div>
          )}
        </div>

        {/* Right Panel — Live Chat */}
        <div className="col-span-12 lg:col-span-4 border-l border-[var(--border-subtle)] dark:border-white/10 bg-[var(--chassis-bg)] dark:bg-[#18191f] flex flex-col h-full overflow-hidden relative">
          <style>{`
            @keyframes floatAndFade {
              0% { transform: translateY(0) scale(var(--start-scale, 0.9)); opacity: 0.95; }
              50% { transform: translateY(-35px) scale(calc(var(--start-scale, 0.9) * 1.15)); opacity: 0.7; }
              100% { transform: translateY(-65px) scale(var(--start-scale, 0.9)); opacity: 0; }
            }
            .animate-float-fade {
              animation: floatAndFade 0.85s cubic-bezier(0.22, 1, 0.36, 1) forwards;
              pointer-events: none;
            }
          `}</style>

          {/* Header */}
          <div className="px-4 py-3 border-b border-[var(--border-subtle)] bg-transparent text-[var(--text-secondary)] text-xs flex items-center justify-between shrink-0">
            <span className="text-xs font-bold text-[var(--text-secondary)] flex items-center gap-2">
              <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse inline-block" />
              Live Chat
            </span>
            <button
              className="text-[11px] font-mono text-[var(--text-muted)] hover:text-[var(--text-primary)] cursor-pointer transition flex items-center gap-1.5"
              style={{ background: "none", border: "none" }}
              onClick={() => setIsGearWallExpanded(!isGearWallExpanded)}
            >
              <MonoIcon.Headphones size={13} />
              <span>Gear List ({snapshot?.outputs.length ?? 0})</span>
            </button>
          </div>

          {/* Gear drawer */}
          {isGearWallExpanded && (
            <div className="max-h-40 overflow-y-auto p-4 space-y-2.5 bg-[var(--surface-card)] border-b border-[var(--border-subtle)] text-xs font-mono shrink-0">
              {(snapshot?.outputs ?? []).map((o) => (
                <div key={o.peerId}>
                  <span className="font-semibold text-[var(--text-primary)]">
                    {o.displayName ?? o.peerId}
                    {o.peerId === snapshot?.hostPeerId ? " [Host]" : ""}
                  </span>
                  <span className="text-[var(--text-muted)] block text-[10px]">
                    {o.device ?? "Unknown device"} → {o.exclusiveMode ? "Exclusive" : "Shared"}
                    {o.maxSampleRate ? ` · ${Math.round(o.maxSampleRate / 1000)}kHz/${o.maxBitDepth}bit` : ""}
                  </span>
                </div>
              ))}
              {(snapshot?.outputs.length ?? 0) === 0 && (
                <div className="text-[var(--text-muted)] text-[10px]">연결된 출력 장치가 없습니다.</div>
              )}
            </div>
          )}

          {/* Message stream */}
          <div className="flex-1 p-4 overflow-y-auto space-y-4 flex flex-col justify-end text-xs bg-transparent">
            {(snapshot?.chat ?? []).map((m, i) => {
              const isHostLine = m.peerId === snapshot?.hostPeerId
              const name = m.peerName ?? m.peerId
              return (
                <div key={`${m.peerId}-${m.at}-${i}`} className="flex items-start gap-2 border-b border-white/5 last:border-0 pb-3 last:pb-0">
                  <div className={`w-6 h-6 rounded-full ${isHostLine ? "bg-amber-500 text-white" : "bg-slate-200 text-slate-700 dark:bg-slate-500/20 dark:text-slate-300"} text-[10px] font-bold flex items-center justify-center shrink-0`}>
                    {isHostLine ? <MonoIcon.Crown size={12} /> : initialsOf(name)}
                  </div>
                  <div>
                    <span className={`text-xs font-bold ${isHostLine ? "text-amber-400 flex items-center gap-1" : "text-[var(--text-primary)] dark:text-zinc-200"}`}>
                      <span>{name}</span>
                      {isHostLine && <MonoIcon.Crown size={11} className="inline text-amber-400" />}
                    </span>
                    <span className="text-[10px] text-[var(--text-muted)] ml-1.5">
                      {new Date(m.at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}
                    </span>
                    <p className="text-[var(--text-primary)] dark:text-zinc-200 text-xs leading-relaxed mt-0.5 font-normal break-words">{m.text}</p>
                  </div>
                </div>
              )
            })}
            {(snapshot?.chat.length ?? 0) === 0 && (
              <div className="text-[var(--text-muted)] text-[11px] font-mono text-center py-6">
                아직 대화가 없습니다. 첫 마디를 남겨 보세요.
              </div>
            )}
          </div>

          {/* Floating reaction canvas */}
          <div className="absolute left-0 right-0 bottom-14 h-0 overflow-visible pointer-events-none z-30">
            {floatingReactions.map(r => (
              <span
                key={r.id}
                className="absolute animate-float-fade text-2xl select-none"
                style={{ left: `${r.left}%`, bottom: "0px", transform: `rotate(${r.rotate}deg)`, ["--start-scale" as string]: r.scale } as Record<string, unknown>}
              >
                {(() => {
                  const hit = REACTIONS.find((x) => x.key === r.emoji)
                  return hit ? <hit.Icon size={26} className={hit.tone} /> : r.emoji
                })()}
              </span>
            ))}
          </div>

          {/* Chat input row */}
          <div className="p-3 border-t border-[var(--border-subtle)] dark:border-white/10 bg-[var(--chassis-bg)] dark:bg-[#18191f] flex items-center gap-2 shrink-0 relative">
            {showReactions && (
              <div className="absolute right-3 bottom-14 bg-[var(--surface-card)] border border-[var(--border-subtle)] shadow-xl rounded-full px-2.5 py-1.5 flex items-center gap-1 z-20">
                {REACTIONS.map((item) => (
                  <button
                    key={item.key}
                    onClick={() => { triggerReaction(item.key); cmd.react(item.key) }}
                    className="w-7 h-7 rounded-full flex items-center justify-center active:scale-125 transition-all duration-75 cursor-pointer select-none hover:bg-black/5 dark:hover:bg-white/10"
                    style={{ background: "none", border: "none" }}
                    title={item.title}
                  >
                    <item.Icon size={16} className={item.tone} />
                  </button>
                ))}
                <button
                  onClick={() => setShowReactions(false)}
                  className="w-5 h-5 rounded-full flex items-center justify-center text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-black/5 dark:hover:bg-white/10 transition cursor-pointer ml-1"
                  style={{ background: "none", border: "none" }}
                  aria-label="Close reactions"
                >
                  <MonoIcon.Close size={11} />
                </button>
              </div>
            )}
            <input
              type="text"
              value={chatInput}
              onChange={(e) => setChatInput(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") { cmd.chat(chatInput); setChatInput("") } }}
              placeholder="React or comment with room..."
              className="w-full bg-[var(--surface-elevated)] dark:bg-[#1f2028] border border-[var(--border-subtle)] dark:border-white/10 text-[var(--text-primary)] dark:text-zinc-100 placeholder:text-[var(--text-muted)] dark:placeholder:text-zinc-500 text-xs rounded-xl px-3 py-2.5 outline-none focus:border-blue-500 transition-colors flex-1"
            />
            <button
              onClick={() => setShowReactions(!showReactions)}
              className="w-8 h-8 rounded-full flex items-center justify-center text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-black/5 dark:hover:bg-white/10 transition cursor-pointer shrink-0"
              style={{ background: "none", border: "none" }}
              title="Reactions"
              aria-label="Reactions"
            >
              <MonoIcon.Smile size={18} />
            </button>
            <button
              onClick={() => { cmd.chat(chatInput); setChatInput("") }}
              className="bg-sky-900/60 hover:bg-sky-800/70 text-sky-300/80 hover:text-sky-200 rounded-lg p-2 text-xs font-mono transition-colors cursor-pointer border border-sky-700/30"
              style={{ border: "none" }}
            >
              Send
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
