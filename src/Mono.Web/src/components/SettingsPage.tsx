import { useEffect, useRef, useState } from "react"
import { useMono, useMonoCommands } from "../state/MonoProvider"
import { pickFolder, listAudioDevices, restartOutput, outputStatus, hasShell, useOutputStatus, type AudioDeviceListing } from "../lib/shell"
import { loadSetup, saveSetup, startConfiguredOutput, type AudioSetup } from "../lib/setup"
import StreamingLoginModal from "./StreamingLoginModal"
import GeneralSystemSettings from "./settings/GeneralSystemSettings"
import { MonoIcon } from "./icons/MonoIcons"

export default function SettingsPage({
  initialTab = "audio",
  connectedServices = { qobuz: true, tidal: true },
  onToggleService = () => {},
  theme,
  onToggleTheme,
}: {
  initialTab?: "audio" | "lens" | "storage" | "accounts" | "general"
  connectedServices?: { qobuz: boolean; tidal: boolean }
  onToggleService?: (service: "qobuz" | "tidal") => void
  theme?: "light" | "dark"
  onToggleTheme?: () => void
}) {
  const { room, folders, catalog } = useMono()
  const cmd = useMonoCommands()
  const worker = useOutputStatus()
  const [settingsTab, setSettingsTab] = useState<"audio" | "lens" | "storage" | "accounts" | "general">(initialTab)
  const [scanning, setScanning] = useState(false)
  const [engineNote, setEngineNote] = useState<string | null>(null)
  const [authService, setAuthService] = useState<"qobuz" | "tidal" | null>(null)
  const [exclusiveMode, setExclusiveMode] = useState<boolean>(true)
  const [memoryPlayback, setMemoryPlayback] = useState<boolean>(true)
  const [dsdStrategy, setDsdStrategy] = useState<"native" | "dop" | "pcm">("dop")
  const [bufferSize, setBufferSize] = useState<number>(256)
  // ── 이 PC 에 달린 출력 하드웨어 ────────────────────────────────────────────
  //
  // 마법사에서만 고를 수 있으면 한 번 고른 장치를 바꾸려고 온보딩을 다시 돌려야 한다.
  // 목록의 출처는 마법사와 같다 — 열거는 NAudio 를 든 Output 워커만 한다.
  const [hwDevices, setHwDevices] = useState<AudioDeviceListing[]>([])
  const [hwScanning, setHwScanning] = useState(false)
  const [audio, setAudio] = useState<AudioSetup | undefined>(undefined)
  const [isDeviceMenuOpen, setIsDeviceMenuOpen] = useState(false)

  useEffect(() => {
    let alive = true
    void (async () => {
      const state = await loadSetup()
      if (!alive) return
      setAudio(state.audio)
      setExclusiveMode(state.audio?.exclusiveMode ?? true)
      if (state.audio?.bufferSize) setBufferSize(state.audio.bufferSize)
    })()
    void rescanDevices()
    return () => { alive = false }
  }, [])

  async function rescanDevices() {
    setHwScanning(true)
    try { setHwDevices(await listAudioDevices()) } finally { setHwScanning(false) }
  }

  /**
   * 고른 장치를 저장하고 워커를 그 장치로 다시 세운다.
   *
   * 저장만 하고 워커를 두면 다음 실행부터 적용된다 — 사용자는 방금 바꿨다고 믿는데
   * 소리는 이전 장치에서 계속 난다. 눌렀으면 지금 바뀌어야 한다.
   */
  async function applyAudio(patch: Partial<AudioSetup>) {
    const next: AudioSetup = {
      driverType: patch.driverType ?? audio?.driverType ?? "WASAPI_EXCLUSIVE",
      deviceId: patch.deviceId ?? audio?.deviceId ?? "",
      deviceName: patch.deviceName ?? audio?.deviceName ?? "",
      bufferSize: patch.bufferSize ?? audio?.bufferSize ?? 256,
      exclusiveMode: patch.exclusiveMode ?? audio?.exclusiveMode ?? true,
    }
    setAudio(next)
    await saveSetup({ audio: next })
    if (!hasShell()) {
      setEngineNote("출력 장치는 저장했습니다. 적용은 Mono 데스크톱 앱에서만 됩니다.")
      return
    }
    const roomId = room?.id ?? await cmd.ensureSoloRoom()
    const res = await startConfiguredOutput(roomId)
    setEngineNote(res.ok
      ? `출력을 ${next.deviceName || "기본 장치"} 로 전환했습니다.`
      : (res.error ?? "출력을 전환하지 못했습니다."))
  }

  // 기다리는 동안 room 은 갱신된다. 클로저에 잡힌 값은 낡으므로 ref 로 본다.
  const roomRef = useRef(room)
  useEffect(() => { roomRef.current = room }, [room])

  /**
   * 엔드포인트가 룸에 실제로 붙을 때까지 잠깐 기다린다.
   *
   * 워커는 프로세스가 뜨고 나서 Core 에 접속해 룸에 들어간다. 그 사이를 안 기다리면
   * "연결했습니다" 를 띄운 직후 화면에는 여전히 출력이 없다고 나온다.
   */
  async function waitForOutput(timeoutMs = 6000): Promise<boolean> {
    const deadline = Date.now() + timeoutMs
    while (Date.now() < deadline) {
      await new Promise((r) => setTimeout(r, 400))
      if ((roomRef.current?.outputs?.length ?? 0) > 0) return true
      const st = await outputStatus()
      if (st?.running && st.roomId) return true
    }
    return false
  }

  const selectedHw = hwDevices.find((d) => d.id === audio?.deviceId)
    ?? hwDevices.find((d) => d.name === audio?.deviceName)
    ?? null

  // 고른 장치가 룸에 붙어 실제로 보고하는 수치. 없으면 아직 연결 전이다.
  const connected = (room?.outputs ?? [])[0] ?? null
  const telemetry = connected?.stats
    ? `Offset ${(connected.stats.offsetMs ?? 0).toFixed(2)}ms • Jitter ${(connected.stats.jitterMs ?? 0).toFixed(2)}ms • Buffer ${connected.stats.bufferMs ?? 0}ms • ${connected.stats.locked ? "Locked" : "Unlocked"}`
    : connected?.note
      ?? (worker?.running
        ? `출력 워커 실행 중 (${worker.backend}) — 재생하면 이 장치에 붙습니다.`
        : "출력이 아직 연결되지 않았습니다.")

  // 워커가 배타를 거절당해 공유로 내려갔으면 그 사실이 여기로 올라온다.
  const deviceNotice = connected?.stats?.deviceError ?? null

  // 고른 장치가 목록에 없을 때 "장치가 사라졌다"고 말하기 전에, 목록 자체가 비어 있는
  // 이유부터 본다 — 브라우저에서는 애초에 열거할 방법이 없고, 스캔은 아직 끝나지 않았을 수 있다.
  function deviceSubtitle(): string {
    if (selectedHw) return [selectedHw.architecture, ...selectedHw.specs].join(" • ")
    if (!hasShell()) return "저장된 장치 — 목록은 Mono 데스크톱 앱에서만 보입니다"
    if (hwScanning) return "장치를 찾는 중…"
    if (audio?.deviceName) return "목록에 없는 장치 — Rescan 을 눌러 보세요"
    return "장치를 고르면 그 장치로 재생합니다"
  }

  const activeDevice = {
    icon: selectedHw?.dsdSupport ? <MonoIcon.Headphones size={15} /> : <MonoIcon.Speaker size={15} />,
    name: selectedHw?.name ?? audio?.deviceName ?? "시스템 기본 출력",
    driver: deviceSubtitle(),
  }

  const [lensMasterBypass, setLensMasterBypass] = useState<boolean>(true)
  const [peqActive, setPeqActive] = useState<boolean>(true)
  const [peqPreset, setPeqPreset] = useState<string>("Harman Reference Target")
  const [crossfeedActive, setCrossfeedActive] = useState<boolean>(true)
  const [crossfeedPreset, setCrossfeedPreset] = useState<"subtle" | "bauer" | "wide">("bauer")

  const navItems: Array<{ id: typeof settingsTab; label: string }> = [
    { id: "audio", label: "Audio Engine" },
    { id: "lens", label: "LENS (DSP Suite)" },
    { id: "storage", label: "Storage & Folders" },
    { id: "accounts", label: "Streaming Accounts" },
    { id: "general", label: "General & System" },
  ]

  const latencyMs = ((bufferSize / 44100) * 1000).toFixed(1)

  const Toggle = ({ value, onChange }: { value: boolean; onChange: (v: boolean) => void }) => (
    <button
      onClick={() => onChange(!value)}
      style={{
        width: 36, height: 20, borderRadius: 10, flexShrink: 0,
        background: value ? "var(--settings-toggle-on)" : "var(--settings-toggle-off)",
        border: "none", cursor: "pointer", padding: "0 3px",
        display: "inline-flex", alignItems: "center",
        transition: "background 0.2s",
      }}
    >
      <span style={{
        width: 14, height: 14, borderRadius: "50%", background: "#FFFFFF",
        transform: value ? "translateX(16px)" : "translateX(0)",
        transition: "transform 0.2s", display: "block", flexShrink: 0,
        boxShadow: "0 1px 3px rgba(0,0,0,0.2)",
      }} />
    </button>
  )

  const SectionLabel = ({ children }: { children: React.ReactNode }) => (
    <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, fontWeight: 700, letterSpacing: "0.12em", color: "var(--settings-section-label)", textTransform: "uppercase", marginBottom: 24 }}>
      {children}
    </div>
  )

  const Divider = () => (
    <div style={{ borderTop: "1px solid var(--settings-divider)", marginBottom: 24 }} />
  )

  return (
    <div style={{ maxWidth: 960, margin: "0 auto", padding: "40px 32px" }}>
      <div style={{ display: "flex", flexDirection: "row", gap: 40, minHeight: 600 }}>

        {/* ── Left sidebar ─────────────────────────────────────────────────── */}
        <div style={{ width: 224, flexShrink: 0, borderRight: "1px solid var(--settings-sidebar-border)", paddingRight: 24 }}>
          <div style={{ fontSize: 22, fontWeight: 700, color: "var(--settings-title)", fontFamily: "Georgia, 'Times New Roman', serif", marginBottom: 24, letterSpacing: "-0.3px" }}>
            Settings
          </div>
          <nav style={{ display: "flex", flexDirection: "column", gap: 2 }}>
            {navItems.map((item) => {
              const active = settingsTab === item.id
              return (
                <button
                  key={item.id}
                  onClick={() => setSettingsTab(item.id)}
                  style={{
                    background: "none", border: "none", textAlign: "left", cursor: "pointer",
                    fontFamily: "inherit",
                    fontSize: 12, letterSpacing: "0.03em",
                    fontWeight: active ? 600 : 400,
                    color: active ? "var(--settings-nav-active)" : "var(--settings-nav-idle)",
                    borderLeft: `2px solid ${active ? "var(--settings-nav-active-border)" : "transparent"}`,
                    paddingLeft: 12, paddingTop: 6, paddingBottom: 6,
                    transition: "color 0.12s, border-color 0.12s",
                  }}
                  onMouseEnter={(e) => { if (!active) (e.currentTarget as HTMLButtonElement).style.color = "var(--settings-nav-idle-hover)" }}
                  onMouseLeave={(e) => { if (!active) (e.currentTarget as HTMLButtonElement).style.color = "var(--settings-nav-idle)" }}
                >
                  {item.label}
                </button>
              )
            })}
          </nav>
        </div>

        {/* ── Right content ────────────────────────────────────────────────── */}
        <div style={{ flex: 1 }}>

          {/* ── Audio Engine ── */}
          {settingsTab === "audio" && (
            <div>
              <SectionLabel>Audio Engine & Hardware</SectionLabel>

              {/* Output device */}
              <div style={{ marginBottom: 28, position: "relative" }}>
                <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 8 }}>
                  <span style={{ fontSize: 12, fontWeight: 600, color: "var(--settings-row-label)" }}>Output Device</span>
                  <button
                    onClick={() => { if (!hwScanning) void rescanDevices() }}
                    disabled={hwScanning}
                    style={{
                      background: "none", border: "none", cursor: hwScanning ? "default" : "pointer", fontFamily: "'DM Mono', monospace",
                      fontSize: 11, color: "var(--settings-input-meta)", padding: 0,
                    }}
                  >
                    {hwScanning ? "Scanning…" : "↻ Rescan"}
                  </button>
                </div>

                {/* Trigger */}
                <button
                  onClick={() => setIsDeviceMenuOpen((v) => !v)}
                  style={{
                    width: "100%", background: "var(--settings-input-bg)", border: `1px solid ${isDeviceMenuOpen ? "var(--settings-input-border-hover)" : "var(--settings-input-border)"}`, borderRadius: 10,
                    padding: "10px 14px", cursor: "pointer", fontFamily: "inherit",
                    display: "flex", alignItems: "center", gap: 10, justifyContent: "space-between",
                    transition: "border-color 0.15s",
                  }}
                >
                  <span style={{ display: "flex", alignItems: "center", gap: 8, minWidth: 0 }}>
                    <span style={{ fontSize: 16, flexShrink: 0 }}>{activeDevice.icon}</span>
                    <span style={{ display: "flex", flexDirection: "column", alignItems: "flex-start", minWidth: 0 }}>
                      <span style={{ fontSize: 13, fontWeight: 500, color: "var(--settings-input-text)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{activeDevice.name}</span>
                      <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, color: "var(--settings-input-meta)", marginTop: 1 }}>{activeDevice.driver}</span>
                    </span>
                  </span>
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"
                    style={{ color: "var(--settings-section-label)", flexShrink: 0, transform: isDeviceMenuOpen ? "rotate(180deg)" : "none", transition: "transform 0.2s" }}>
                    <path d="m6 9 6 6 6-6"/>
                  </svg>
                </button>

                {/* Telemetry — 지금 붙어 있는 엔드포인트가 보고하는 값 */}
                <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--settings-input-meta)", marginTop: 6 }}>
                  {telemetry}
                </div>

                {/* 배타를 거절당해 공유로 내려갔으면 여기서 말해 준다 — 비트퍼펙트라고 믿게 두지 않는다. */}
                {deviceNotice && (
                  <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10.5, color: "#d97706", marginTop: 6, lineHeight: 1.5 }}>
                    {deviceNotice}
                  </div>
                )}

                {/* 출력 워커 제어 — Output 프로세스를 띄우고 내리는 건 데스크톱 셸만 할 수 있다. */}
                <div style={{ display: "flex", gap: 8, marginTop: 10 }}>
                  <button
                    onClick={async () => {
                      const roomId = room?.id ?? await cmd.ensureSoloRoom()
                      const res = await startConfiguredOutput(roomId)
                      if (!res.ok) {
                        setEngineNote(res.error ?? "출력을 시작하지 못했습니다.")
                        return
                      }

                      setEngineNote("출력 워커를 띄웠습니다 — 장치가 붙기를 기다리는 중…")
                      const attached = await waitForOutput()
                      setEngineNote(attached
                        ? "출력을 연결했습니다."
                        : worker?.running
                          ? "워커는 실행 중입니다. 재생을 시작하면 이 장치로 나갑니다."
                          : "워커는 떴지만 아직 장치가 붙지 않았습니다. 상태를 눌러 확인해 보세요.")
                    }}
                    style={{ flex: 1, padding: "9px 12px", borderRadius: 9, border: "1px solid var(--settings-input-border)", background: "var(--settings-input-bg)", color: "var(--settings-input-text)", fontSize: 12, fontWeight: 600, cursor: "pointer", fontFamily: "inherit" }}
                  >
                    출력 연결
                  </button>
                  <button
                    onClick={async () => {
                      const res = await restartOutput()
                      setEngineNote(res.ok ? "출력 워커를 다시 시작했습니다." : (res.error ?? "재시작하지 못했습니다."))
                    }}
                    style={{ flex: 1, padding: "9px 12px", borderRadius: 9, border: "1px solid var(--settings-input-border)", background: "transparent", color: "var(--settings-input-text)", fontSize: 12, fontWeight: 600, cursor: "pointer", fontFamily: "inherit" }}
                  >
                    워커 재시작
                  </button>
                  <button
                    onClick={async () => {
                      const st = await outputStatus()
                      setEngineNote(st
                        ? `Core ${st.coreRunning ? "실행 중" : "중지"} · 출력 ${st.running ? `실행 중 (${st.backend})` : "중지"}`
                        : "데스크톱 앱에서만 확인할 수 있습니다.")
                    }}
                    style={{ padding: "9px 12px", borderRadius: 9, border: "1px solid var(--settings-input-border)", background: "transparent", color: "var(--settings-input-meta)", fontSize: 12, cursor: "pointer", fontFamily: "inherit" }}
                  >
                    상태
                  </button>
                </div>
                {engineNote && (
                  <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--settings-input-meta)", marginTop: 8 }}>{engineNote}</div>
                )}

                {/* Dropdown menu */}
                {isDeviceMenuOpen && (
                  <>
                    {/* Click-outside backdrop */}
                    <div
                      style={{ position: "fixed", inset: 0, zIndex: 49 }}
                      onClick={() => setIsDeviceMenuOpen(false)}
                    />
                    <div className="device-select-menu">
                      {hwDevices.length === 0 ? (
                        <div className="device-select-item device-select-item--hidden" style={{ cursor: "default" }}>
                          <span className="device-select-item__driver" style={{ lineHeight: 1.6 }}>
                            {!hasShell()
                              ? "출력 장치는 Mono 데스크톱 앱에서만 찾을 수 있습니다."
                              : hwScanning ? "장치를 찾는 중…" : "장치를 찾지 못했습니다. Rescan 을 눌러 보세요."}
                          </span>
                        </div>
                      ) : hwDevices.map((dev) => {
                        const isActive = dev.id === audio?.deviceId
                        return (
                          <button
                            key={dev.id}
                            className={`device-select-item${isActive ? " is-active" : ""}`}
                            onClick={() => {
                              setIsDeviceMenuOpen(false)
                              void applyAudio({
                                driverType: dev.driverType,
                                deviceId: dev.id,
                                deviceName: dev.name,
                                // ASIO 는 본질적으로 배타다. 그쪽을 고르면 토글 값과 무관하게 배타로 간다.
                                exclusiveMode: dev.driverType === "ASIO" ? true : exclusiveMode,
                              })
                            }}
                          >
                            <span style={{ display: "flex", alignItems: "center", gap: 10, minWidth: 0 }}>
                              <span style={{ fontSize: 18, flexShrink: 0 }}>
                                {dev.dsdSupport ? <MonoIcon.Headphones size={15} /> : <MonoIcon.Speaker size={15} />}
                              </span>
                              <span style={{ display: "flex", flexDirection: "column", alignItems: "flex-start", minWidth: 0 }}>
                                <span className="device-select-item__name">{dev.name}{dev.isDefault ? " · 시스템 기본" : ""}</span>
                                <span className="device-select-item__driver">{[dev.architecture, ...dev.specs].join(" • ")}</span>
                              </span>
                            </span>
                            {isActive && (
                              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#38bdf8" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" style={{ flexShrink: 0 }}>
                                <polyline points="20 6 9 17 4 12"/>
                              </svg>
                            )}
                          </button>
                        )
                      })}
                    </div>
                  </>
                )}
              </div>

              <Divider />

              {/* Toggle rows */}
              <div style={{ display: "flex", flexDirection: "column", gap: 18, marginBottom: 28 }}>
                {[
                  {
                    label: "Exclusive Mode",
                    sub: audio?.driverType === "ASIO"
                      ? "ASIO 는 언제나 배타 점유입니다"
                      : "장치가 거절하면 공유 모드로 내려가고, 그 사실을 위에 표시합니다",
                    value: audio?.driverType === "ASIO" ? true : exclusiveMode,
                    // 화면에만 두면 마법사에서 고른 값과 갈라진다 — 저장하고 워커까지 다시 세운다.
                    set: (v: boolean) => { setExclusiveMode(v); void applyAudio({ exclusiveMode: v }) },
                  },
                  { label: "Memory Playback (RAM Load)", sub: "Preloads tracks into RAM before streaming to DAC", value: memoryPlayback, set: setMemoryPlayback },
                ].map((row) => (
                  <div key={row.label} style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 16 }}>
                    <div>
                      <div style={{ fontSize: 12, fontWeight: 600, color: "var(--settings-row-label)", marginBottom: 2 }}>{row.label}</div>
                      <div style={{ fontSize: 11, color: "var(--settings-row-sub)" }}>{row.sub}</div>
                    </div>
                    <Toggle value={row.value} onChange={row.set} />
                  </div>
                ))}
              </div>

              <Divider />

              {/* DSD strategy */}
              <div style={{ marginBottom: 28 }}>
                <div style={{ fontSize: 12, fontWeight: 600, color: "var(--settings-row-label)", marginBottom: 12 }}>DSD Playback Strategy</div>
                <div style={{ display: "flex", gap: 8 }}>
                  {([
                    { val: "native", label: "Native DSD" },
                    { val: "dop", label: "DoP — DSD over PCM" },
                    { val: "pcm", label: "DSD to PCM" },
                  ] as const).map(({ val, label }) => {
                    const active = dsdStrategy === val
                    return (
                      <button
                        key={val}
                        onClick={() => setDsdStrategy(val)}
                        style={{
                          fontFamily: "inherit", cursor: "pointer",
                          fontSize: 12, fontWeight: active ? 600 : 400,
                          color: active ? "var(--settings-dsd-active-text)" : "var(--settings-dsd-idle-text)",
                          background: active ? "var(--settings-dsd-active-bg)" : "transparent",
                          border: `1px solid ${active ? "var(--settings-dsd-active-border)" : "transparent"}`,
                          borderRadius: 8, padding: "6px 14px",
                          transition: "all 0.15s",
                        }}
                        onMouseEnter={(e) => { if (!active) (e.currentTarget as HTMLButtonElement).style.color = "var(--settings-nav-idle-hover)" }}
                        onMouseLeave={(e) => { if (!active) (e.currentTarget as HTMLButtonElement).style.color = "var(--settings-dsd-idle-text)" }}
                      >
                        ( {label} )
                      </button>
                    )
                  })}
                </div>
              </div>

              <Divider />

              {/* Buffer size */}
              <div>
                <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 8 }}>
                  <span style={{ fontSize: 12, fontWeight: 600, color: "var(--settings-row-label)" }}>Buffer Size</span>
                  <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--settings-section-label)" }}>
                    {bufferSize} Samples ({latencyMs} ms latency)
                  </span>
                </div>
                <input
                  type="range" min={64} max={2048} step={64} value={bufferSize}
                  onChange={(e) => setBufferSize(Number(e.target.value))}
                  // 놓았을 때만 저장한다 — 끄는 동안 매번 쓰면 Core 에 수십 번 PUT 이 날아간다.
                  // 워커를 다시 세우지는 않는다: 이 값은 아직 워커에 전달되는 경로가 없다.
                  onPointerUp={() => {
                    if (audio && audio.bufferSize === bufferSize) return
                    const next = { ...(audio ?? { driverType: "WASAPI_EXCLUSIVE" as const, deviceId: "", deviceName: "", exclusiveMode: true }), bufferSize }
                    setAudio(next)
                    void saveSetup({ audio: next })
                  }}
                  style={{ width: "100%", height: 4, borderRadius: 2, accentColor: "var(--accent-solo)", cursor: "pointer", background: "var(--settings-input-border)", display: "block" }}
                />
                <div style={{ display: "flex", justifyContent: "space-between", marginTop: 4 }}>
                  <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, color: "var(--settings-row-sub)" }}>64</span>
                  <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, color: "var(--settings-row-sub)" }}>2048</span>
                </div>
              </div>
            </div>
          )}

          {/* ── LENS ── */}
          {settingsTab === "lens" && (
            <div>
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", borderBottom: "1px solid var(--settings-divider)", paddingBottom: 16, marginBottom: 24 }}>
                <div style={{ display: "flex", alignItems: "center" }}>
                  <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, fontWeight: 700, letterSpacing: "0.12em", color: "var(--settings-section-label)", textTransform: "uppercase" }}>
                    LENS Acoustic Precision
                  </span>
                  <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "#22c55e", marginLeft: 12, display: "flex", alignItems: "center", gap: 4 }}>
                    <span style={{ fontSize: 8 }}>●</span> 64-Bit Float Core Engine
                  </span>
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                  <span style={{ fontSize: 12, fontWeight: 600, color: "var(--settings-row-label)" }}>LENS Master Engine</span>
                  <button
                    onClick={() => setLensMasterBypass(!lensMasterBypass)}
                    style={{
                      width: 36, height: 20, borderRadius: 10, border: "none", cursor: "pointer",
                      background: lensMasterBypass ? "var(--settings-toggle-on)" : "var(--settings-toggle-off)",
                      padding: "0 3px", display: "inline-flex", alignItems: "center", transition: "background 0.2s",
                    }}
                  >
                    <span style={{
                      width: 14, height: 14, borderRadius: "50%", background: "#fff",
                      transform: lensMasterBypass ? "translateX(16px)" : "translateX(0)",
                      transition: "transform 0.2s", display: "block", boxShadow: "0 1px 3px rgba(0,0,0,0.2)",
                    }} />
                  </button>
                </div>
              </div>

              {/* Module 1 — PEQ */}
              <div style={{ border: "1px solid var(--settings-card-border)", borderRadius: 16, padding: 20, marginBottom: 20, background: "var(--settings-card-bg)", boxShadow: "0 1px 3px rgba(0,0,0,0.06)" }}>
                <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 4 }}>
                  <span style={{ fontSize: 14, fontWeight: 600, color: "var(--settings-row-label)" }}>1. Parametric Equalizer (PEQ)</span>
                  <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                    <button
                      style={{
                        fontFamily: "inherit", fontSize: 12, color: "var(--settings-preset-text)", background: "var(--settings-preset-bg)",
                        border: "1px solid var(--settings-preset-border)", borderRadius: 8, padding: "4px 10px", cursor: "pointer", display: "flex", alignItems: "center", gap: 4,
                      }}
                    >
                      {peqPreset} <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><path d="m6 9 6 6 6-6"/></svg>
                    </button>
                    <button
                      onClick={() => setPeqActive(!peqActive)}
                      style={{
                        width: 36, height: 20, borderRadius: 10, border: "none", cursor: "pointer",
                        background: peqActive ? "var(--settings-toggle-on)" : "var(--settings-toggle-off)",
                        padding: "0 3px", display: "inline-flex", alignItems: "center", transition: "background 0.2s",
                      }}
                    >
                      <span style={{
                        width: 14, height: 14, borderRadius: "50%", background: "#fff",
                        transform: peqActive ? "translateX(16px)" : "translateX(0)",
                        transition: "transform 0.2s", display: "block", boxShadow: "0 1px 3px rgba(0,0,0,0.2)",
                      }} />
                    </button>
                  </div>
                </div>

                {/* Frequency visualizer — always dark, intentional */}
                <div style={{ height: 128, width: "100%", background: "#020617", borderRadius: 12, padding: 12, position: "relative", overflow: "hidden", marginTop: 16, marginBottom: 16, border: "1px solid rgba(15,23,42,0.8)", boxSizing: "border-box" }}>
                  {[{ y: "20%", label: "+12dB" }, { y: "50%", label: "0dB" }, { y: "80%", label: "-12dB" }].map(({ y, label }) => (
                    <div key={label} style={{ position: "absolute", left: 0, right: 0, top: y, borderTop: "1px solid rgba(100,116,139,0.25)", display: "flex", alignItems: "center" }}>
                      <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 9, color: "#475569", paddingLeft: 4, lineHeight: 1 }}>{label}</span>
                    </div>
                  ))}
                  <div style={{ position: "absolute", bottom: 6, left: 0, right: 0, display: "flex", justifyContent: "space-between", padding: "0 8px" }}>
                    {["20Hz", "100Hz", "1kHz", "10kHz", "20kHz"].map((f) => (
                      <span key={f} style={{ fontFamily: "'DM Mono', monospace", fontSize: 9, color: "#475569" }}>{f}</span>
                    ))}
                  </div>
                  <svg style={{ position: "absolute", inset: 0, width: "100%", height: "100%" }} viewBox="0 0 600 128" preserveAspectRatio="none">
                    <defs>
                      <filter id="glow"><feGaussianBlur stdDeviation="2" result="blur"/><feMerge><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
                    </defs>
                    <path d="M0,64 C30,64 60,58 90,54 C130,48 160,52 200,50 C250,48 280,50 320,47 C360,44 390,46 430,50 C470,54 510,60 550,62 L600,64 L600,128 L0,128 Z" fill="rgba(34,211,238,0.06)" />
                    <path d="M0,64 C30,64 60,58 90,54 C130,48 160,52 200,50 C250,48 280,50 320,47 C360,44 390,46 430,50 C470,54 510,60 550,62 L600,64" stroke="#22D3EE" strokeWidth="1.5" fill="none" filter="url(#glow)" opacity={peqActive ? 1 : 0.3} />
                    {peqActive && [{ cx: 90, cy: 54 }, { cx: 200, cy: 50 }, { cx: 360, cy: 47 }, { cx: 490, cy: 52 }].map((pt, i) => (
                      <circle key={i} cx={pt.cx} cy={pt.cy} r="4" fill="#22D3EE" stroke="#020617" strokeWidth="1.5" filter="url(#glow)" />
                    ))}
                  </svg>
                </div>

                {/* Band table */}
                <div>
                  <div style={{ display: "grid", gridTemplateColumns: "28px 90px 90px 70px 80px 60px", gap: 4, paddingBottom: 8, borderBottom: "1px solid var(--settings-peq-header-border)", marginBottom: 4 }}>
                    {["#", "TYPE", "FREQUENCY", "GAIN", "Q-FACTOR", "STATE"].map((h) => (
                      <span key={h} style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, color: "var(--settings-peq-col-meta)", textTransform: "uppercase", letterSpacing: "0.05em" }}>{h}</span>
                    ))}
                  </div>
                  {[
                    { n: 1, type: "Low Shelf", freq: "80 Hz", gain: "+2.5 dB", q: "0.71" },
                    { n: 2, type: "Peaking", freq: "240 Hz", gain: "−1.8 dB", q: "1.41" },
                    { n: 3, type: "Peaking", freq: "3,200 Hz", gain: "+1.0 dB", q: "2.00" },
                    { n: 4, type: "High Shelf", freq: "10,000 Hz", gain: "−0.5 dB", q: "0.71" },
                  ].map((row) => (
                    <div key={row.n} style={{ display: "grid", gridTemplateColumns: "28px 90px 90px 70px 80px 60px", gap: 4, padding: "6px 0", borderBottom: "1px solid var(--settings-peq-row-border)" }}>
                      <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--settings-peq-col-meta)" }}>{row.n}</span>
                      <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--settings-peq-col-type)" }}>{row.type}</span>
                      <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--settings-peq-col-freq)" }}>{row.freq}</span>
                      <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: row.gain.startsWith("+") ? "#059669" : "#DC2626" }}>{row.gain}</span>
                      <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--settings-peq-col-freq)" }}>{row.q}</span>
                      <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, color: "var(--settings-peq-state-text)", background: "var(--settings-peq-state-bg)", border: "1px solid var(--settings-peq-state-border)", borderRadius: 4, padding: "1px 6px", display: "inline-block", height: "fit-content" }}>Active</span>
                    </div>
                  ))}
                </div>
              </div>

              {/* Module 2 — Crossfeed */}
              <div style={{ border: "1px solid var(--settings-card-border)", borderRadius: 16, padding: 20, background: "var(--settings-card-bg)", boxShadow: "0 1px 3px rgba(0,0,0,0.06)" }}>
                <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                  <span style={{ fontSize: 14, fontWeight: 600, color: "var(--settings-row-label)" }}>2. Headphone Crossfeed (Binaural Space)</span>
                  <button
                    onClick={() => setCrossfeedActive(!crossfeedActive)}
                    style={{
                      width: 36, height: 20, borderRadius: 10, border: "none", cursor: "pointer",
                      background: crossfeedActive ? "var(--settings-toggle-on)" : "var(--settings-toggle-off)",
                      padding: "0 3px", display: "inline-flex", alignItems: "center", transition: "background 0.2s",
                    }}
                  >
                    <span style={{
                      width: 14, height: 14, borderRadius: "50%", background: "#fff",
                      transform: crossfeedActive ? "translateX(16px)" : "translateX(0)",
                      transition: "transform 0.2s", display: "block", boxShadow: "0 1px 3px rgba(0,0,0,0.2)",
                    }} />
                  </button>
                </div>
                <p style={{ fontSize: 12, color: "var(--settings-row-sub)", marginTop: 6, marginBottom: 16, lineHeight: 1.5 }}>
                  Simulates natural 60° speaker acoustic angles to eliminate headphone channel isolation and ear fatigue.
                </p>
                <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
                  {([
                    { val: "subtle", label: "Subtle (Acoustic / Jazz)" },
                    { val: "bauer", label: "Bauer Standard (650Hz / 4.5dB)" },
                    { val: "wide", label: "Wide Soundstage (Orchestral)" },
                  ] as const).map(({ val, label }) => {
                    const active = crossfeedPreset === val
                    return (
                      <button
                        key={val}
                        onClick={() => setCrossfeedPreset(val)}
                        style={{
                          fontFamily: "inherit", fontSize: 12, fontWeight: active ? 500 : 400,
                          color: active ? "var(--settings-crossfeed-active-text)" : "var(--settings-crossfeed-idle-text)",
                          background: active ? "var(--settings-crossfeed-active-bg)" : "var(--settings-crossfeed-idle-bg)",
                          border: "none", borderRadius: 8, padding: "8px 14px",
                          cursor: "pointer", transition: "all 0.15s",
                        }}
                        onMouseEnter={(e) => { if (!active) (e.currentTarget as HTMLButtonElement).style.background = "var(--settings-crossfeed-idle-hover-bg)" }}
                        onMouseLeave={(e) => { if (!active) (e.currentTarget as HTMLButtonElement).style.background = "var(--settings-crossfeed-idle-bg)" }}
                      >
                        {label}
                      </button>
                    )
                  })}
                </div>
              </div>
            </div>
          )}

          {/* ── Storage ── */}
          {settingsTab === "storage" && (
            <div>
              <SectionLabel>Storage & Folders</SectionLabel>
              {folders.map((f) => (
                <div key={f.path} style={{ background: "var(--settings-storage-folder-bg)", border: "1px solid var(--settings-storage-folder-border)", borderRadius: 10, padding: "14px 16px", marginBottom: 12 }}>
                  <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 12, color: "var(--settings-storage-folder-text)", fontWeight: 500, wordBreak: "break-all" }}>
                    {f.path}
                  </div>
                  <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--settings-storage-folder-meta)", marginTop: 4 }}>
                    {f.exists ? `${f.trackCount.toLocaleString()} tracks` : "폴더를 찾을 수 없습니다"}
                  </div>
                </div>
              ))}
              {folders.length === 0 && (
                <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11.5, color: "var(--settings-storage-folder-meta)", marginBottom: 12 }}>
                  아직 감시 중인 폴더가 없습니다.
                </div>
              )}

              <button
                disabled={scanning}
                onClick={async () => {
                  // 셸이 없으면 브라우저에서 경로를 고를 방법이 없다 — 직접 입력받는다.
                  const picked = hasShell() ? await pickFolder("음악 폴더 선택") : window.prompt("음악 폴더 경로")
                  if (!picked) return
                  setScanning(true)
                  setEngineNote(`${picked} 스캔 중…`)
                  await cmd.scanLibrary(picked)
                  setScanning(false)
                  setEngineNote(`스캔 완료 — 총 ${catalog.length.toLocaleString()}곡`)
                }}
                style={{
                  width: "100%", border: "1.5px dashed var(--settings-storage-add-border)", borderRadius: 10, padding: "12px 16px",
                  background: "transparent", cursor: scanning ? "wait" : "pointer", fontFamily: "inherit",
                  fontSize: 12, fontWeight: 500, color: "var(--settings-storage-add-text)", transition: "border-color 0.15s, color 0.15s",
                  opacity: scanning ? 0.6 : 1,
                }}
                onMouseEnter={(e) => { (e.currentTarget as HTMLButtonElement).style.borderColor = "var(--settings-storage-add-hover-border)"; (e.currentTarget as HTMLButtonElement).style.color = "var(--settings-storage-add-hover-text)" }}
                onMouseLeave={(e) => { (e.currentTarget as HTMLButtonElement).style.borderColor = "var(--settings-storage-add-border)"; (e.currentTarget as HTMLButtonElement).style.color = "var(--settings-storage-add-text)" }}
              >
                {scanning ? "스캔 중…" : "+ Add Watch Folder"}
              </button>

              <button
                disabled={scanning || folders.length === 0}
                onClick={async () => {
                  setScanning(true)
                  setEngineNote("라이브러리 다시 스캔 중…")
                  await cmd.scanLibrary()
                  setScanning(false)
                  setEngineNote(`스캔 완료 — 총 ${catalog.length.toLocaleString()}곡`)
                }}
                style={{
                  width: "100%", marginTop: 10, border: "1px solid var(--settings-storage-folder-border)", borderRadius: 10, padding: "10px 16px",
                  background: "transparent", cursor: scanning ? "wait" : "pointer", fontFamily: "inherit",
                  fontSize: 12, fontWeight: 500, color: "var(--settings-storage-add-text)",
                  opacity: scanning || folders.length === 0 ? 0.5 : 1,
                }}
              >
                라이브러리 다시 스캔
              </button>

              {engineNote && (
                <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--settings-storage-folder-meta)", marginTop: 12 }}>{engineNote}</div>
              )}
            </div>
          )}

          {/* ── Accounts ── */}
          {settingsTab === "accounts" && (
            <div>
              <SectionLabel>Streaming Accounts</SectionLabel>
              <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
                {(
                  [
                    { key: "qobuz" as const, name: "Qobuz Studio", spec: "Max 24-Bit / 192kHz Studio Master" },
                    { key: "tidal" as const, name: "TIDAL Max", spec: "HiRes FLAC Direct" },
                  ] as const
                ).map((svc) => {
                  const connected = connectedServices[svc.key]
                  return (
                    <div
                      key={svc.key}
                      style={{
                        background: "var(--settings-svc-card-bg)",
                        border: "1px solid var(--settings-svc-card-border)",
                        borderRadius: 12, padding: "16px 20px",
                        display: "flex", alignItems: "center", justifyContent: "space-between",
                      }}
                    >
                      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                        {/* Service mark */}
                        {svc.key === "qobuz" ? (
                          <div style={{
                            width: 28, height: 28, borderRadius: 7, flexShrink: 0,
                            background: "linear-gradient(135deg, #C9A227 0%, #F5D06B 50%, #B8891A 100%)",
                            display: "flex", alignItems: "center", justifyContent: "center",
                          }}>
                            <span style={{ fontSize: 11, fontWeight: 900, color: "#0B1120", fontFamily: "Georgia, serif", letterSpacing: "-0.5px" }}>Q</span>
                          </div>
                        ) : (
                          <div style={{
                            width: 28, height: 28, borderRadius: 7, flexShrink: 0, background: "#000",
                            border: "1px solid rgba(0,200,220,0.4)",
                            display: "flex", alignItems: "center", justifyContent: "center",
                          }}>
                            <svg width="14" height="11" viewBox="0 0 18 14" fill="none">
                              <path d="M9 0 L12 4 L15 0 L18 4 L15 8 L12 4 L9 8 L6 4 L3 8 L0 4 L3 0 L6 4 Z" fill="#00C8DC" opacity="0.9" />
                            </svg>
                          </div>
                        )}
                        <div>
                          <div style={{ fontSize: 13, fontWeight: 600, color: "var(--settings-svc-name)" }}>{svc.name}</div>
                          <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: connected ? "var(--settings-svc-spec)" : "var(--settings-svc-spec-idle)", marginTop: 2 }}>
                            {connected ? svc.spec : "Not connected"}
                          </div>
                        </div>
                      </div>

                      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                        {/* Status badge */}
                        <span style={{
                          fontFamily: "'DM Mono', monospace", fontSize: 11, fontWeight: 600,
                          display: "inline-flex", alignItems: "center", gap: 5,
                          color: connected ? "var(--settings-badge-on-text)" : "var(--settings-badge-off-text)",
                          background: connected ? "var(--settings-badge-on-bg)" : "var(--settings-badge-off-bg)",
                          border: `1px solid ${connected ? "var(--settings-badge-on-border)" : "var(--settings-badge-off-border)"}`,
                          borderRadius: 6, padding: "3px 9px", transition: "all 0.2s",
                        }}>
                          {connected && (
                            <span style={{ width: 6, height: 6, borderRadius: "50%", background: "#10b981", boxShadow: "0 0 5px rgba(16,185,129,0.5)", flexShrink: 0, display: "inline-block" }} />
                          )}
                          {connected ? "Active" : "Disconnected"}
                        </span>

                        {/* Action button */}
                        <button
                          onClick={() => connected ? onToggleService(svc.key) : setAuthService(svc.key)}
                          style={{
                            fontSize: 12, fontWeight: 600, fontFamily: "inherit",
                            color: connected ? "var(--settings-btn-disconnect-text)" : "var(--settings-btn-connect-text)",
                            background: connected ? "var(--settings-btn-disconnect-bg)" : "var(--settings-btn-connect-bg)",
                            border: `1px solid ${connected ? "var(--settings-btn-disconnect-border)" : "var(--settings-btn-connect-border)"}`,
                            borderRadius: 7, padding: "6px 14px", cursor: "pointer",
                            transition: "all 0.15s",
                          }}
                          onMouseEnter={(e) => {
                            const btn = e.currentTarget as HTMLButtonElement
                            if (connected) {
                              btn.style.color = "var(--settings-btn-disconnect-hover-text)"
                              btn.style.borderColor = "var(--settings-btn-disconnect-hover-border)"
                              btn.style.background = "var(--settings-btn-disconnect-hover-bg)"
                            } else {
                              btn.style.background = "var(--settings-btn-connect-hover-bg)"
                            }
                          }}
                          onMouseLeave={(e) => {
                            const btn = e.currentTarget as HTMLButtonElement
                            if (connected) {
                              btn.style.color = "var(--settings-btn-disconnect-text)"
                              btn.style.borderColor = "var(--settings-btn-disconnect-border)"
                              btn.style.background = "var(--settings-btn-disconnect-bg)"
                            } else {
                              btn.style.background = "var(--settings-btn-connect-bg)"
                            }
                          }}
                        >
                          {connected ? "Disconnect" : "Connect Account"}
                        </button>
                      </div>
                    </div>
                  )
                })}
              </div>
            </div>
          )}

          {/* ── General ── */}
          {settingsTab === "general" && (
            <GeneralSystemSettings theme={theme} onToggleTheme={onToggleTheme} />
          )}

        </div>
      </div>

      <StreamingLoginModal
        service={authService}
        isOpen={authService !== null}
        onClose={() => setAuthService(null)}
        onSuccessfulLogin={(svc) => {
          onToggleService(svc)
          setAuthService(null)
        }}
      />
    </div>
  )
}
