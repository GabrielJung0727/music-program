import { useEffect, useState, type ReactNode } from "react"
import {
  Check, Cpu, Download, FolderOpen, FolderPlus, HardDrive, Headphones, Moon, RefreshCw, SlidersHorizontal, Speaker, Sun,
  TriangleAlert, UserRound, Waves,
} from "lucide-react"
import { useApp, type SettingsTab } from "../state/app"
import { useMono, useMonoCommands } from "../state/MonoProvider"
import { MSG } from "../lib/protocol"
import {
  applyUpdate, checkForUpdate, discordActivityStatus, hasShell, listAudioDevices, onUpdateProgress, openExternal, pickFolder,
  restartOutput, setDiscordActivityEnabled, setDiscordApplicationId, shell, useOutputStatus, type AudioDeviceListing, type UpdateStatus,
} from "../lib/shell"
import { clearSetup, saveSetup, startConfiguredOutput, type AudioSetup } from "../lib/setup"
import { selectDsp, toggleDsp } from "../lib/dsp"
import { Toggle } from "../ui"
import { DSP_PRESETS } from "../shell/PlayerBar"
import { SERVICES, ServiceGlyph } from "./Explore"

const TABS: { id: SettingsTab; label: string; icon: typeof Cpu; desc: string }[] = [
  { id: "audio", label: "오디오 엔진", icon: Waves, desc: "어느 장치로, 어떤 방식으로 소리를 내보낼지 정합니다." },
  { id: "dsp", label: "DSP", icon: SlidersHorizontal, desc: "신호를 가공하는 단계입니다. 끄면 원본이 그대로 나갑니다." },
  { id: "library", label: "라이브러리", icon: HardDrive, desc: "Mono 가 음악을 읽어 오는 폴더입니다." },
  { id: "accounts", label: "스트리밍 계정", icon: Download, desc: "연결한 서비스의 음원이 라이브러리에 합쳐집니다." },
  { id: "general", label: "일반", icon: UserRound, desc: "프로필, 화면, 업데이트." },
]

function Row({ title, desc, children }: { title: string; desc?: ReactNode; children?: ReactNode }) {
  return (
    <div className="setrow">
      <div className="setrow__text">
        <h4>{title}</h4>
        {desc && <p>{desc}</p>}
      </div>
      {children}
    </div>
  )
}

function Group({ title, children, note }: { title: string; children: ReactNode; note?: ReactNode }) {
  return (
    <div className="set-group">
      <div className="label">{title}</div>
      <div className="panel">{children}</div>
      {note && <p className="t-cap dimmer" style={{ marginTop: 9, lineHeight: 1.6 }}>{note}</p>}
    </div>
  )
}

export default function Settings({ tab }: { tab: SettingsTab }) {
  const app = useApp()
  const current = TABS.find((t) => t.id === tab) ?? TABS[0]
  return (
    <div className="page settings">
      <nav className="settings__nav">
        <h1 className="t-h1" style={{ marginBottom: 22, paddingLeft: 11 }}>설정</h1>
        {TABS.map(({ id, label, icon: Icon }) => (
          <button key={id} className={`side-item ${tab === id ? "is-on" : ""}`} onClick={() => app.go({ page: "settings", tab: id }, { replace: true })}>
            <Icon size={17} strokeWidth={1.9} /><span>{label}</span>
          </button>
        ))}
      </nav>
      <div className="settings__body">
        <header style={{ marginBottom: 30 }}>
          <h2 className="t-h1" style={{ fontSize: 24 }}>{current.label}</h2>
          <p className="t-body dim" style={{ marginTop: 6 }}>{current.desc}</p>
        </header>
        {tab === "audio" && <AudioTab />}
        {tab === "dsp" && <DspTab />}
        {tab === "library" && <LibraryTab />}
        {tab === "accounts" && <AccountsTab />}
        {tab === "general" && <GeneralTab />}
      </div>
    </div>
  )
}

// ── 오디오 엔진 ─────────────────────────────────────────────────────────────

const BUFFERS = [64, 128, 256, 512, 1024]

function AudioTab() {
  const app = useApp()
  const { room } = useMono()
  const cmd = useMonoCommands()
  const worker = useOutputStatus()
  const [devices, setDevices] = useState<AudioDeviceListing[]>([])
  const [scanning, setScanning] = useState(false)
  const audio = app.setup?.audio
  const connected = room?.outputs[0] ?? null

  // 목록은 NAudio 를 든 Output 워커만 열거할 수 있다. 브라우저에서는 빈 목록이 맞다.
  const rescan = async () => {
    setScanning(true)
    try { setDevices(await listAudioDevices()) } finally { setScanning(false) }
  }
  useEffect(() => { void rescan() }, [])

  /**
   * 고른 값을 저장하고 워커를 그 장치로 다시 세운다. 저장만 하면 다음 실행부터 적용돼서,
   * 방금 바꿨다고 믿는데 소리는 이전 장치에서 계속 난다. 눌렀으면 지금 바뀌어야 한다.
   */
  const apply = async (patch: Partial<AudioSetup>) => {
    const next: AudioSetup = {
      driverType: patch.driverType ?? audio?.driverType ?? "WASAPI_EXCLUSIVE",
      deviceId: patch.deviceId ?? audio?.deviceId ?? "",
      deviceName: patch.deviceName ?? audio?.deviceName ?? "",
      bufferSize: patch.bufferSize ?? audio?.bufferSize ?? 256,
      exclusiveMode: patch.exclusiveMode ?? audio?.exclusiveMode ?? true,
    }
    app.setSetup({ ...(app.setup ?? { completed: true }), audio: next })
    await saveSetup({ audio: next })
    if (!hasShell()) { app.toast("저장했습니다. 적용은 Mono 데스크톱 앱에서 됩니다."); return }
    const roomId = room?.id ?? await cmd.ensureSoloRoom()
    const res = await startConfiguredOutput(roomId)
    app.toast(res.ok ? `출력을 ${next.deviceName || "기본 장치"} 로 바꿨습니다` : (res.error ?? "출력을 바꾸지 못했습니다"), !res.ok)
  }

  const buffer = audio?.bufferSize ?? 256
  const selected = devices.find((d) => d.id === audio?.deviceId) ?? devices.find((d) => d.name === audio?.deviceName)

  return (
    <>
      <Group title="지금 연결된 출력">
        <div className="out-now">
          <span className={`chain__dot ${connected ? "is-lit-dot" : ""}`} style={{ width: 46, height: 46 }}>
            {connected?.supportsDsd ? <Headphones size={21} /> : <Speaker size={21} />}
          </span>
          <div className="grow">
            <div className="t-h3" style={{ fontSize: 16 }}>{connected?.device ?? audio?.deviceName ?? "시스템 기본 출력"}</div>
            <div className="chain__spec">
              {connected
                ? [
                    connected.exclusiveMode ? "배타 모드" : "공유 모드",
                    `최대 ${connected.maxBitDepth}-bit / ${Math.round(connected.maxSampleRate / 1000)} kHz`,
                    connected.supportsDsd ? "DSD" : null,
                    `지연 ${connected.reportedLatencyMs} ms`,
                  ].filter(Boolean).join(" · ")
                : worker?.running ? `출력 워커 실행 중 (${worker.backend}). 재생하면 이 장치에 붙습니다` : "아직 연결되지 않았습니다"}
            </div>
            {connected?.stats && (
              <div className="chain__spec">
                오프셋 {(connected.stats.offsetMs ?? 0).toFixed(2)} ms · 지터 {(connected.stats.jitterMs ?? 0).toFixed(2)} ms · 버퍼 {connected.stats.bufferMs ?? 0} ms · {connected.stats.locked ? "클럭 잠김" : "클럭 맞추는 중"}
              </div>
            )}
          </div>
          <span className={`badge ${connected ? "badge--ok" : ""}`}>
            <i className={`lamp ${connected ? "lamp--ok" : ""}`} style={{ boxShadow: "none" }} />{connected ? "연결됨" : "대기"}
          </span>
        </div>
        {connected?.stats?.deviceError && (
          <div className="setrow" style={{ color: "var(--warn-text)", gap: 12, minHeight: 0 }}>
            <TriangleAlert size={16} /><span className="t-small">{connected.stats.deviceError}</span>
          </div>
        )}
        {hasShell() && (
          <div className="setrow" style={{ gap: 8, minHeight: 0 }}>
            <button className="btn btn--sm btn--line" onClick={() => { void apply({}) }}>이 장치로 다시 연결</button>
            <button className="btn btn--sm btn--line" onClick={() => { void restartOutput().then((r) => app.toast(r.ok ? "출력 워커를 다시 시작했습니다" : (r.error ?? "다시 시작하지 못했습니다"), !r.ok)) }}>
              워커 다시 시작
            </button>
          </div>
        )}
      </Group>

      <Group
        title="출력 장치"
        note={!hasShell() ? "장치 목록은 Mono 데스크톱 앱에서만 보입니다. 브라우저에서는 저장된 장치 이름만 확인할 수 있습니다." : undefined}
      >
        {devices.length === 0 ? (
          <Row
            title={scanning ? "장치를 찾는 중…" : audio?.deviceName ? audio.deviceName : "찾은 장치가 없습니다"}
            desc={audio?.deviceName ? `저장된 장치 · ${audio.driverType === "ASIO" ? "ASIO" : "WASAPI"}` : "DAC 이나 오디오 인터페이스를 연결한 뒤 다시 찾아보세요."}
          >
            <button className="btn btn--sm btn--line" onClick={rescan} disabled={scanning}><RefreshCw size={14} />다시 찾기</button>
          </Row>
        ) : (
          <>
            {devices.map((d) => {
              const on = selected?.id === d.id
              return (
                <button key={d.id} className={`device ${on ? "is-on" : ""}`} onClick={() => { void apply({ deviceId: d.id, deviceName: d.name, driverType: d.driverType }) }}>
                  <span className="chain__dot">{d.dsdSupport ? <Headphones size={16} /> : <Speaker size={16} />}</span>
                  <span className="grow">
                    <span className="row gap-8">
                      <span className="t-small" style={{ fontWeight: 620 }}>{d.name}</span>
                      {d.isDefault && <span className="badge">시스템 기본</span>}
                      {d.bitPerfectVerified && <span className="badge badge--ok">Bit-Perfect 확인</span>}
                    </span>
                    <span className="chain__spec" style={{ display: "block" }}>
                      {[d.driverType === "ASIO" ? "ASIO" : "WASAPI", d.architecture, ...d.specs, d.dsdSupport].filter(Boolean).join(" · ")}
                    </span>
                  </span>
                  <span className="choice__radio" />
                </button>
              )
            })}
            <div className="setrow" style={{ minHeight: 0 }}>
              <button className="btn btn--sm btn--quiet" style={{ marginLeft: -12 }} onClick={rescan} disabled={scanning}><RefreshCw size={14} />{scanning ? "찾는 중…" : "장치 다시 찾기"}</button>
            </div>
          </>
        )}
      </Group>

      <Group title="전송 방식">
        <Row title="배타 모드" desc="장치를 Mono 가 독점합니다. 윈도우 믹서를 거치지 않아 샘플레이트가 곡마다 그대로 맞춰집니다. 다른 앱의 소리는 나지 않습니다.">
          <Toggle label="배타 모드" value={audio?.exclusiveMode ?? true} onChange={(v) => { void apply({ exclusiveMode: v }) }} />
        </Row>
        <Row title="다른 PC 의 출력 허용" desc="켜면 다른 PC 의 출력 워커와 Mono.Cli 가 이 Core 에 붙을 수 있습니다. 끄면 이 PC 에서만 받습니다. Mono 를 다시 시작하면 적용됩니다.">
          <Toggle
            label="다른 PC 의 출력 허용"
            value={app.setup?.allowRemoteOutputs === true}
            onChange={(v) => {
              app.setSetup({ ...(app.setup ?? { completed: true }), allowRemoteOutputs: v })
              void saveSetup({ allowRemoteOutputs: v })
              app.toast("저장했습니다. Mono 를 다시 시작하면 적용됩니다")
            }}
          />
        </Row>
        <Row
          title="버퍼 크기"
          desc={<>작을수록 반응이 빠르고, 클수록 끊김에 강합니다. 지금 <span className="mono">{buffer}</span> 샘플 ≈ <span className="mono">{((buffer / 44100) * 1000).toFixed(1)} ms</span></>}
        >
          <div className="seg">
            {BUFFERS.map((b) => (
              <button key={b} className={`mono ${buffer === b ? "is-on" : ""}`} onClick={() => { void apply({ bufferSize: b }) }}>{b}</button>
            ))}
          </div>
        </Row>
      </Group>
    </>
  )
}

// ── DSP ─────────────────────────────────────────────────────────────────────

const DSP_DESC = [
  "",
  "헤드폰의 주파수 응답을 목표 곡선에 맞춥니다.",
  "좌우 스피커의 거리·음량 차이를 보정합니다.",
  "측정한 임펄스 응답(IR)으로 방의 울림을 상쇄합니다.",
  "헤드폰에서 좌우 채널을 살짝 섞어 스피커처럼 들리게 합니다.",
  "직접 만든 파라메트릭 EQ 대역을 적용합니다.",
]

function DspTab() {
  const app = useApp()
  const { room } = useMono()
  const cmd = useMonoCommands()

  const enabled = room?.dspEnabled ?? false
  const preset = room?.dspPreset ?? 0
  const locked = !!room?.dspLocked && app.role !== "host"

  // DSP 는 방에 걸린다. 아직 방이 없으면 솔로 방부터 만든다.
  const withRoom = async (fn: () => void) => {
    if (!room) await cmd.ensureSoloRoom()
    fn()
  }

  return (
    <>
      <Group
        title="신호 가공"
        note={locked ? "이 라운지의 호스트가 DSP 를 잠가 두었습니다." : "DSP 는 방(룸)마다 따로 기억됩니다. 라운지에서는 호스트의 설정이 기준이 됩니다."}
      >
        <Row
          title={enabled ? "DSP 켜짐" : "DSP 우회 (원본 그대로)"}
          desc={enabled ? "신호가 가공되어 나갑니다. 플레이어 바의 램프가 초록에서 앰버로 바뀝니다." : "어떤 계산도 거치지 않습니다. 비트 퍼펙트 재생의 조건입니다."}
        >
          <span className={`badge ${enabled ? "badge--warn" : "badge--ok"}`}>
            <i className={`lamp ${enabled ? "lamp--warn" : "lamp--ok"}`} style={{ boxShadow: "none" }} />{enabled ? "Processed" : "Bit-Perfect"}
          </span>
          <Toggle label="DSP" value={enabled} disabled={locked} onChange={(on) => { void withRoom(() => toggleDsp(cmd.setDsp, preset, on)) }} />
        </Row>
      </Group>

      <Group title="프리셋">
        {DSP_PRESETS.slice(1).map((name, i) => {
          const id = i + 1
          const on = enabled && preset === id
          return (
            <button key={id} className={`device ${on ? "is-on" : ""}`} disabled={locked} onClick={() => { void withRoom(() => selectDsp(cmd.setDsp, id)) }}>
              <span className="chain__dot"><SlidersHorizontal size={16} /></span>
              <span className="grow">
                <span className="t-small" style={{ fontWeight: 620, display: "block" }}>{name}</span>
                <span className="t-cap dimmer">{DSP_DESC[id]}</span>
              </span>
              <span className="choice__radio" />
            </button>
          )
        })}
      </Group>

      <Group title="지금 적용된 값">
        <Row title="헤드룸" desc="EQ 로 올린 대역이 클리핑되지 않도록 미리 낮춰 두는 양입니다.">
          <span className="mono t-small">{room ? `${room.headroomDb} dB` : "없음"}</span>
        </Row>
        <Row title="컨볼루션 IR" desc="룸 보정 프리셋이 읽는 임펄스 응답 파일입니다.">
          <span className="mono t-cap dim clip" style={{ maxWidth: 280 }}>{room?.convolutionIrPath ?? "지정 안 됨"}</span>
        </Row>
        <Row title="장치 EQ 프로필" desc="출력 장치에 맞춘 보정 곡선입니다.">
          <span className="mono t-cap dim">{room?.deviceEqProfile ?? "없음"}</span>
        </Row>
      </Group>
    </>
  )
}

// ── 라이브러리 ──────────────────────────────────────────────────────────────

function LibraryTab() {
  const app = useApp()
  const { folders, catalog } = useMono()
  const cmd = useMonoCommands()
  const [scanning, setScanning] = useState<string | null>(null)
  const [manual, setManual] = useState("")

  useEffect(() => { void cmd.refreshFolders() }, [cmd])

  const scan = async (path?: string) => {
    setScanning(path ?? "전체 라이브러리")
    await cmd.scanLibrary(path)
    setScanning(null)
    app.toast("스캔을 마쳤습니다")
  }

  const add = async () => {
    // 브라우저에서는 폴더 선택창을 띄울 수 없다. 아래 입력란에 경로를 직접 적는다.
    const picked = await pickFolder("음악 폴더 선택")
    if (!picked) return
    const next = [...new Set([...(app.setup?.folders ?? []), picked])]
    app.setSetup({ ...(app.setup ?? { completed: true }), folders: next })
    void saveSetup({ folders: next })
    await scan(picked)
  }

  return (
    <>
      <Group
        title="음악 폴더"
        note="FLAC · WAV · AIFF · ALAC · M4A · MP3 · AAC · DSF · DFF 를 읽습니다. 파일은 옮기거나 고치지 않고, 태그와 커버만 읽어 옵니다."
      >
        {folders.length === 0 && <Row title="등록된 폴더가 없습니다" desc="음악이 들어 있는 폴더를 추가하면 바로 스캔을 시작합니다." />}
        {folders.map((f) => (
          <div className="setrow" key={f.path}>
            <span className="chain__dot"><FolderOpen size={16} /></span>
            <div className="setrow__text">
              <h4 className="mono clip selectable" style={{ fontSize: 13, fontWeight: 500 }}>{f.path}</h4>
              <p>{f.exists ? `${f.trackCount.toLocaleString()}곡` : <span style={{ color: "var(--warn-text)" }}>폴더를 찾을 수 없습니다. 드라이브가 연결돼 있는지 확인하세요</span>}</p>
            </div>
            <button className="btn btn--sm btn--quiet" onClick={() => { void scan(f.path) }} disabled={!!scanning || !f.exists}>
              <RefreshCw size={14} />다시 스캔
            </button>
          </div>
        ))}
        <div className="setrow" style={{ gap: 8 }}>
          {hasShell() ? (
            <button className="btn btn--sm btn--primary" onClick={add} disabled={!!scanning}><FolderPlus size={14} />폴더 추가</button>
          ) : (
            <>
              <input className="input mono" style={{ fontSize: 12.5 }} value={manual} onChange={(e) => setManual(e.target.value)} placeholder="D:\Music 처럼 Core 가 있는 PC 의 경로" />
              <button className="btn btn--primary" style={{ flex: "none" }} disabled={!manual.trim() || !!scanning} onClick={() => { void scan(manual.trim()).then(() => setManual("")) }}>
                <FolderPlus size={14} />추가
              </button>
            </>
          )}
          {hasShell() && <div className="grow" />}
          {hasShell() && (
            <button className="btn btn--sm btn--line" onClick={() => { void scan() }} disabled={!!scanning || folders.length === 0}>
              <RefreshCw size={14} />전체 다시 스캔
            </button>
          )}
        </div>
        {scanning && (
          <div className="setrow" style={{ minHeight: 0, gap: 10 }}>
            <i className="lamp lamp--accent" /><span className="t-small dim clip">{scanning} 스캔 중…</span>
          </div>
        )}
      </Group>

      <Group title="관리">
        <Row title="새 파일 자동 감지" desc="폴더에 음악을 넣으면 스캔을 누르지 않아도 라이브러리에 나타납니다.">
          <Toggle
            label="자동 감지"
            value={app.setup?.autoWatch ?? true}
            onChange={(v) => { app.setSetup({ ...(app.setup ?? { completed: true }), autoWatch: v }); void saveSetup({ autoWatch: v }) }}
          />
        </Row>
        <Row title="라이브러리 백업" desc={`재생 기록, 플레이리스트, 라운지 기록을 한 파일로 남깁니다. 지금 ${catalog.length.toLocaleString()}곡이 등록돼 있습니다.`}>
          <button
            className="btn btn--sm btn--line"
            onClick={() => {
              void cmd.request({ type: MSG.backup }, MSG.backup)
                .then((res) => app.toast(res.ok ? `백업을 만들었습니다: ${res.body ?? ""}` : (res.error ?? "백업에 실패했습니다"), !res.ok))
                .catch((err) => app.toast(err instanceof Error ? err.message : "백업에 실패했습니다", true))
            }}
          >
            지금 백업
          </button>
        </Row>
      </Group>
    </>
  )
}

// ── 스트리밍 계정 ───────────────────────────────────────────────────────────

function AccountsTab() {
  const app = useApp()
  const { streamingAccounts } = useMono()
  const cmd = useMonoCommands()

  useEffect(() => { cmd.refreshStreamingAccounts() }, [cmd])

  return (
    <Group title="서비스" note="계정 정보는 이 PC 의 Mono Core 에만 저장됩니다. 연결을 끊으면 가져온 스트리밍 음원도 라이브러리에서 빠집니다.">
      {SERVICES.map((s) => {
        const account = streamingAccounts.find((a) => a.provider === s.provider)
        const on = !!account?.connected
        return (
          <div className="setrow" key={s.id}>
            <ServiceGlyph id={s.id} size={40} />
            <div className="setrow__text">
              <h4 className="row gap-8">{s.name}{on && <span className="badge badge--ok"><Check size={11} />연결됨</span>}</h4>
              <p>{on ? `${account?.displayName ?? "계정"} · 가져온 곡 ${account?.imported ?? 0}개${account?.note ? ` · ${account.note}` : ""}` : s.blurb}</p>
            </div>
            {on ? (
              <button
                className="btn btn--sm btn--danger"
                onClick={() => app.openModal({
                  kind: "confirm", title: `${s.name} 연결을 끊을까요?`,
                  body: "이 서비스에서 가져온 음원이 라이브러리와 플레이리스트에서 재생되지 않게 됩니다.",
                  confirmLabel: "연결 끊기", danger: true,
                  onConfirm: () => { void cmd.unlinkStreaming(s.provider); app.toast(`${s.name} 연결을 끊었습니다`) },
                })}
              >
                연결 끊기
              </button>
            ) : (
              <button className="btn btn--sm btn--primary" onClick={() => app.openModal({ kind: "login", service: s.id })}>연결</button>
            )}
          </div>
        )
      })}
    </Group>
  )
}

// ── 일반 ────────────────────────────────────────────────────────────────────

function GeneralTab() {
  const app = useApp()
  const { displayName } = useMono()
  const cmd = useMonoCommands()
  const [name, setName] = useState(displayName)
  const [version, setVersion] = useState<string | null>(null)
  const [update, setUpdate] = useState<UpdateStatus | null>(null)
  const [phase, setPhase] = useState<"idle" | "checking" | "applying">("idle")
  const [percent, setPercent] = useState(0)
  const [discord, setDiscord] = useState<{ enabled: boolean; hasAppId: boolean } | null>(null)
  const [appId, setAppId] = useState("")

  useEffect(() => {
    void shell()?.app.version().then((v) => setVersion(v.version)).catch(() => {})
    void discordActivityStatus().then((s) => { if (s) setDiscord({ enabled: s.enabled, hasAppId: s.hasAppId }) })
    return onUpdateProgress(setPercent)
  }, [])

  const saveName = () => {
    const next = name.trim()
    if (!next || next === displayName) return
    cmd.setDisplayName(next)
    const profile = { ...(app.setup?.profile ?? { avatarColor: "#6D6DF6", gear: { headphonesOrSpeakers: "", amplifier: "", dac: "" } }), displayName: next }
    app.setSetup({ ...(app.setup ?? { completed: true }), profile })
    void saveSetup({ profile })
    app.toast("이름을 바꿨습니다")
  }

  const check = async () => {
    setPhase("checking")
    try {
      const res = await checkForUpdate()
      if (!res) app.toast("업데이트는 Mono 데스크톱 앱에서만 확인할 수 있습니다")
      setUpdate(res)
    } catch (err) {
      app.toast(err instanceof Error ? err.message : "업데이트를 확인하지 못했습니다", true)
    }
    setPhase("idle")
  }

  const gear = app.setup?.profile?.gear

  return (
    <>
      <Group title="프로필" note="이 이름은 라운지의 멤버 목록과 채팅, 세션 기록에 그대로 쓰입니다.">
        <Row title="표시 이름">
          <input className="input" style={{ width: 220 }} value={name} onChange={(e) => setName(e.target.value)} maxLength={32} onKeyDown={(e) => { if (e.key === "Enter") saveName() }} />
          <button className="btn btn--sm btn--line" onClick={saveName} disabled={!name.trim() || name.trim() === displayName}>저장</button>
        </Row>
        {gear && (gear.headphonesOrSpeakers || gear.dac || gear.amplifier) && (
          <Row title="청취 장비" desc="라운지에서 내 프로필에 표시됩니다.">
            <span className="t-small dim" style={{ textAlign: "right", lineHeight: 1.6 }}>
              {[gear.headphonesOrSpeakers, gear.amplifier, gear.dac].filter(Boolean).join(" · ")}
            </span>
          </Row>
        )}
      </Group>

      <Group title="화면">
        <Row title="테마" desc="어두운 테마는 커버 아트의 색이 가장 잘 살아납니다.">
          <div className="seg">
            <button className={app.theme === "dark" ? "is-on" : ""} onClick={() => app.setTheme("dark")}><Moon size={14} />어둡게</button>
            <button className={app.theme === "light" ? "is-on" : ""} onClick={() => app.setTheme("light")}><Sun size={14} />밝게</button>
          </div>
        </Row>
      </Group>

      <Group title="Discord" note={!hasShell() ? "Discord 활동 표시는 Mono 데스크톱 앱에서만 쓸 수 있습니다." : undefined}>
        <Row title="듣는 곡을 Discord 에 표시" desc="프로필의 활동 카드에 지금 재생 중인 곡이 나옵니다.">
          <Toggle
            label="Discord 활동"
            value={discord?.enabled ?? false}
            disabled={!discord}
            onChange={(v) => { setDiscord((d) => (d ? { ...d, enabled: v } : d)); void setDiscordActivityEnabled(v) }}
          />
        </Row>
        {discord && (
          <Row title="Application ID" desc={discord.hasAppId ? "저장된 ID 가 있습니다. 바꾸려면 새 값을 넣으세요." : <>Discord 개발자 포털에서 만든 앱의 ID 입니다. <span className="link accent" onClick={() => openExternal("https://discord.com/developers/applications")}>포털 열기</span></>}>
            <input className="input mono" style={{ width: 220, fontSize: 12.5 }} value={appId} onChange={(e) => setAppId(e.target.value)} placeholder="123456789012345678" />
            <button
              className="btn btn--sm btn--line" disabled={!appId.trim()}
              onClick={() => { void setDiscordApplicationId(appId.trim()).then((ok) => { setDiscord((d) => (d ? { ...d, hasAppId: ok } : d)); app.toast(ok ? "Application ID 를 저장했습니다" : "ID 를 저장하지 못했습니다", !ok); if (ok) setAppId("") }) }}
            >
              저장
            </button>
          </Row>
        )}
      </Group>

      <Group title="Mono">
        <Row
          title={update?.available ? `새 버전 ${update.version ?? ""} 이 나왔습니다` : "업데이트"}
          desc={phase === "applying" ? `내려받는 중… ${percent}%` : update ? update.message : `현재 버전 ${version ?? "확인 안 됨"}`}
        >
          {update?.available ? (
            <button
              className="btn btn--sm btn--primary" disabled={phase !== "idle"}
              onClick={() => { setPhase("applying"); void applyUpdate().catch((err) => { setPhase("idle"); app.toast(err instanceof Error ? err.message : "업데이트를 적용하지 못했습니다", true) }) }}
            >
              <Download size={14} />설치하고 다시 시작
            </button>
          ) : (
            <button className="btn btn--sm btn--line" onClick={check} disabled={phase !== "idle"}>{phase === "checking" ? "확인하는 중…" : "업데이트 확인"}</button>
          )}
        </Row>
        <Row title="처음 설정 다시 하기" desc="폴더·출력 장치·프로필을 고르는 마법사를 처음부터 다시 엽니다. 라이브러리와 재생 기록은 지워지지 않습니다.">
          <button
            className="btn btn--sm btn--danger"
            onClick={() => app.openModal({
              kind: "confirm", title: "처음 설정을 다시 할까요?", body: "저장된 출력 장치와 프로필 설정이 초기화되고 마법사가 열립니다.",
              confirmLabel: "다시 설정", danger: true,
              onConfirm: () => { void clearSetup().then(() => app.setSetup({ completed: false })) },
            })}
          >
            다시 설정
          </button>
        </Row>
      </Group>
    </>
  )
}
