import { useEffect, useState } from "react"
import { ArrowLeft, ArrowRight, Check, FolderOpen, FolderPlus, Headphones, Radio, RefreshCw, Speaker, Waves, X } from "lucide-react"
import { useApp } from "../state/app"
import { useMono, useMonoCommands } from "../state/MonoProvider"
import { hasShell, listAudioDevices, pickFolder, type AudioDeviceListing } from "../lib/shell"
import { saveSetup, startConfiguredOutput, type AudioSetup, type ProfileSetup } from "../lib/setup"
import { Avatar, Toggle } from "../ui"
import { BrandMark } from "../shell/Sidebar"
import { ModalHost } from "../modals"
import { SERVICES, ServiceGlyph } from "./Explore"
import { NOT_READY_BLURB, serviceReady } from "../lib/streaming"

const STEPS = ["시작", "음악 폴더", "출력 장치", "스트리밍", "프로필", "완료"]
const COLORS = ["#6D6DF6", "#22C55E", "#F59E0B", "#EF4444", "#38BDF8", "#E4E4E7"]

/**
 * 처음 켠 PC 에서 한 번 도는 마법사. 고른 값은 Core 의 setup.json 에 남는다 —
 * WebView 캐시를 지워도, 브라우저로 열어도 같은 답이 나와야 하기 때문이다.
 */
export default function Onboarding() {
  const app = useApp()
  const { folders: coreFolders, streamingAccounts, displayName } = useMono()
  const cmd = useMonoCommands()
  const [step, setStep] = useState(0)

  const [folders, setFolders] = useState<string[]>([])
  const [manual, setManual] = useState("")
  const [autoWatch, setAutoWatch] = useState(true)

  const [devices, setDevices] = useState<AudioDeviceListing[]>([])
  const [scanning, setScanning] = useState(false)
  const [audio, setAudio] = useState<AudioSetup>({ driverType: "WASAPI_EXCLUSIVE", deviceId: "", deviceName: "", exclusiveMode: true })

  const [profile, setProfile] = useState<ProfileSetup>({
    displayName: displayName === "Listener" ? "" : displayName,
    avatarColor: COLORS[0],
    gear: { headphonesOrSpeakers: "", amplifier: "", dac: "" },
  })

  const rescan = async () => {
    setScanning(true)
    try {
      const list = await listAudioDevices()
      setDevices(list)
      // 아무것도 고르지 않았으면 비트 퍼펙트가 확인된 장치, 없으면 시스템 기본을 미리 골라 둔다.
      const preferred = list.find((d) => d.bitPerfectVerified) ?? list.find((d) => d.isDefault) ?? list[0]
      if (preferred) setAudio((a) => (a.deviceId ? a : { ...a, deviceId: preferred.id, deviceName: preferred.name, driverType: preferred.driverType }))
    } finally {
      setScanning(false)
    }
  }
  useEffect(() => { void rescan() }, [])

  const addFolder = async () => {
    const picked = hasShell() ? await pickFolder("음악 폴더 선택") : manual.trim()
    if (!picked) return
    setFolders((f) => [...new Set([...f, picked])])
    setManual("")
  }

  const finish = () => {
    const name = profile.displayName.trim() || "Listener"
    cmd.setDisplayName(name)

    // 고른 폴더를 실제로 읽어 들인다. 마법사를 끝냈는데 라이브러리가 비어 있으면 안 된다.
    const known = new Set(coreFolders.map((f) => f.path))
    for (const folder of folders) if (!known.has(folder)) void cmd.scanLibrary(folder)

    const chosen = audio.deviceId ? audio : undefined
    const next = { completed: true, theme: app.theme, folders, autoWatch, audio: chosen, profile: { ...profile, displayName: name } }
    void saveSetup(next).then(() => {
      // 다음 실행이 아니라 지금부터 그 장치로 들리게 한다.
      if (chosen && hasShell()) {
        void (async () => {
          const roomId = await cmd.ensureSoloRoom()
          await startConfiguredOutput(roomId)
        })()
      }
    })
    app.setSetup(next)
    app.toast(`환영합니다, ${name}`)
  }

  const last = step === STEPS.length - 1
  const connected = SERVICES.filter((s) => streamingAccounts.some((a) => a.provider === s.provider && a.connected))

  return (
    <div className="ob">
      <aside className="ob__rail">
        <div className="brand" style={{ padding: 0, height: "auto" }}><BrandMark size={30} /><span className="brand__word" style={{ fontSize: 22 }}>Mono</span></div>
        <ol className="ob__steps">
          {STEPS.map((label, i) => (
            <li key={label} className={i === step ? "is-on" : i < step ? "is-done" : ""}>
              <span className="ob__n">{i < step ? <Check size={13} strokeWidth={3} /> : String(i + 1).padStart(2, "0")}</span>
              <button onClick={() => i < step && setStep(i)} disabled={i >= step}>{label}</button>
            </li>
          ))}
        </ol>
        <p className="t-cap dimmer" style={{ position: "relative", lineHeight: 1.6 }}>여기서 고른 것은 모두<br />설정에서 다시 바꿀 수 있습니다.</p>
      </aside>

      <main className="ob__main">
        <div className="ob__content" key={step}>
          {step === 0 && (
            <>
              <span className="label">Hi-Fi Audio Lounge</span>
              <h1 className="t-display" style={{ fontSize: 60, marginTop: 14 }}>소리는 그대로,<br />듣는 건 함께.</h1>
              <p className="ob__lead">Mono 는 가진 음원을 한 비트도 건드리지 않고 DAC 까지 보내고, 같은 순간을 다른 사람과 나눠 듣게 해 주는 플레이어입니다. 세 가지만 정하면 준비가 끝납니다.</p>
              <div className="ob__points">
                <div><Waves size={20} /><b>Bit-Perfect</b><span>윈도우 믹서를 거치지 않고 원본 그대로 출력합니다.</span></div>
                <div><FolderOpen size={20} /><b>하나의 라이브러리</b><span>로컬 파일과 스트리밍을 한 목록에서 고릅니다.</span></div>
                <div><Radio size={20} /><b>라운지</b><span>같은 곡을 같은 시각, 같은 음질로 함께 듣습니다.</span></div>
              </div>
            </>
          )}

          {step === 1 && (
            <>
              <h1 className="t-h1" style={{ fontSize: 34 }}>음악은 어디에 있나요?</h1>
              <p className="ob__lead">폴더를 알려 주면 태그와 커버를 읽어 라이브러리를 만듭니다. 파일은 옮기거나 고치지 않습니다. NAS 나 외장 드라이브도 됩니다.</p>
              <div className="panel" style={{ marginTop: 28 }}>
                {folders.length === 0 && (
                  <div className="setrow"><div className="setrow__text"><h4>아직 고른 폴더가 없습니다</h4><p>나중에 설정 → 라이브러리에서 추가해도 됩니다.</p></div></div>
                )}
                {folders.map((f) => (
                  <div className="setrow" key={f}>
                    <span className="chain__dot"><FolderOpen size={16} /></span>
                    <div className="setrow__text"><h4 className="mono clip" style={{ fontSize: 13, fontWeight: 500 }}>{f}</h4></div>
                    <button className="icon-btn icon-btn--sm" aria-label="빼기" onClick={() => setFolders((x) => x.filter((p) => p !== f))}><X size={15} /></button>
                  </div>
                ))}
                <div className="setrow" style={{ gap: 8 }}>
                  {!hasShell() && <input className="input mono" style={{ fontSize: 12.5 }} value={manual} onChange={(e) => setManual(e.target.value)} placeholder="D:\Music" onKeyDown={(e) => { if (e.key === "Enter") void addFolder() }} />}
                  <button className="btn btn--primary" style={{ flex: "none" }} onClick={addFolder} disabled={!hasShell() && !manual.trim()}><FolderPlus size={15} />폴더 추가</button>
                </div>
              </div>
              <div className="panel" style={{ marginTop: 14 }}>
                <div className="setrow">
                  <div className="setrow__text"><h4>새 파일 자동 감지</h4><p>폴더에 음악을 넣으면 스캔을 누르지 않아도 나타납니다.</p></div>
                  <Toggle label="자동 감지" value={autoWatch} onChange={setAutoWatch} />
                </div>
              </div>
            </>
          )}

          {step === 2 && (
            <>
              <h1 className="t-h1" style={{ fontSize: 34 }}>어디로 소리를 보낼까요?</h1>
              <p className="ob__lead">DAC 이나 오디오 인터페이스를 고르세요. Mono 가 그 장치를 직접 열어 원본 샘플레이트 그대로 내보냅니다.</p>
              <div className="panel" style={{ marginTop: 28 }}>
                {devices.length === 0 ? (
                  <div className="setrow">
                    <div className="setrow__text">
                      <h4>{scanning ? "장치를 찾는 중…" : "찾은 장치가 없습니다"}</h4>
                      <p>{hasShell() ? "장치를 연결한 뒤 다시 찾아보세요. 건너뛰면 시스템 기본 출력을 씁니다." : "출력 장치는 Mono 데스크톱 앱에서 고를 수 있습니다. 지금은 건너뛰어도 됩니다."}</p>
                    </div>
                    <button className="btn btn--sm btn--line" onClick={rescan} disabled={scanning}><RefreshCw size={14} />다시 찾기</button>
                  </div>
                ) : devices.map((d) => (
                  <button key={d.id} className={`device ${audio.deviceId === d.id ? "is-on" : ""}`} onClick={() => setAudio((a) => ({ ...a, deviceId: d.id, deviceName: d.name, driverType: d.driverType }))}>
                    <span className="chain__dot">{d.dsdSupport ? <Headphones size={16} /> : <Speaker size={16} />}</span>
                    <span className="grow">
                      <span className="row gap-8">
                        <span className="t-small" style={{ fontWeight: 620 }}>{d.name}</span>
                        {d.bitPerfectVerified && <span className="badge badge--ok">Bit-Perfect 확인</span>}
                        {d.isDefault && <span className="badge">시스템 기본</span>}
                      </span>
                      <span className="chain__spec" style={{ display: "block" }}>{[d.driverType === "ASIO" ? "ASIO" : "WASAPI", d.architecture, ...d.specs, d.dsdSupport].filter(Boolean).join(" · ")}</span>
                    </span>
                    <span className="choice__radio" />
                  </button>
                ))}
              </div>
              <div className="panel" style={{ marginTop: 14 }}>
                <div className="setrow">
                  <div className="setrow__text"><h4>배타 모드</h4><p>장치를 Mono 가 독점해 윈도우 믹서를 건너뜁니다. 비트 퍼펙트의 조건입니다.</p></div>
                  <Toggle label="배타 모드" value={audio.exclusiveMode} onChange={(v) => setAudio((a) => ({ ...a, exclusiveMode: v }))} />
                </div>
              </div>
            </>
          )}

          {step === 3 && (
            <>
              <h1 className="t-h1" style={{ fontSize: 34 }}>스트리밍도 쓰시나요?</h1>
              <p className="ob__lead">연결하면 스트리밍 음원도 로컬 파일과 같은 경로로 재생되고, 라운지에서 함께 들을 수 있습니다. 없으면 건너뛰세요.</p>
              <div className="col gap-12" style={{ marginTop: 28 }}>
                {SERVICES.map((s) => {
                  const account = streamingAccounts.find((a) => a.provider === s.provider)
                  const on = !!account?.connected
                  const ready = serviceReady(account)
                  return (
                    <div key={s.id} className="panel svc-card">
                      <ServiceGlyph id={s.id} size={44} />
                      <div className="grow">
                        <div className="row gap-8">
                          <span className="t-h3" style={{ fontSize: 16 }}>{s.name}</span>
                          {on && <span className="badge badge--ok"><Check size={11} />연결됨</span>}
                          {!on && !ready && <span className="badge">준비 중</span>}
                        </div>
                        <p className="t-small dimmer" style={{ marginTop: 4 }}>{on ? account?.displayName ?? "계정 연결됨" : ready ? s.blurb : NOT_READY_BLURB}</p>
                      </div>
                      {on
                        ? <button className="btn btn--sm btn--line" onClick={() => { void cmd.unlinkStreaming(s.provider) }}>연결 끊기</button>
                        : ready
                          ? <button className="btn btn--sm btn--primary" onClick={() => app.openModal({ kind: "login", service: s.id })}>연결</button>
                          : <button className="btn btn--sm btn--line" disabled>준비 중</button>}
                    </div>
                  )
                })}
              </div>
            </>
          )}

          {step === 4 && (
            <>
              <h1 className="t-h1" style={{ fontSize: 34 }}>라운지에서 뭐라고 부를까요?</h1>
              <p className="ob__lead">이름은 라운지의 멤버 목록과 채팅에 쓰입니다. 장비는 적어 두면 프로필에 함께 보입니다.</p>
              <div className="ob__profile">
                <div className="col gap-16 grow">
                  <label className="field"><span>표시 이름</span>
                    <input className="input" value={profile.displayName} onChange={(e) => setProfile((p) => ({ ...p, displayName: e.target.value }))} placeholder="Listener" maxLength={32} autoFocus />
                  </label>
                  <div className="field"><span>색</span>
                    <div className="row gap-10">
                      {COLORS.map((c) => (
                        <button key={c} className={`swatch ${profile.avatarColor === c ? "is-on" : ""}`} style={{ background: c }} onClick={() => setProfile((p) => ({ ...p, avatarColor: c }))} aria-label={`색 ${c}`} />
                      ))}
                    </div>
                  </div>
                  <label className="field"><span>헤드폰 · 스피커</span>
                    <input className="input" value={profile.gear.headphonesOrSpeakers} onChange={(e) => setProfile((p) => ({ ...p, gear: { ...p.gear, headphonesOrSpeakers: e.target.value } }))} placeholder="예: Sennheiser HD 800 S" />
                  </label>
                  <div className="row gap-12">
                    <label className="field grow"><span>앰프</span>
                      <input className="input" value={profile.gear.amplifier} onChange={(e) => setProfile((p) => ({ ...p, gear: { ...p.gear, amplifier: e.target.value } }))} placeholder="선택" />
                    </label>
                    <label className="field grow"><span>DAC</span>
                      <input className="input" value={profile.gear.dac} onChange={(e) => setProfile((p) => ({ ...p, gear: { ...p.gear, dac: e.target.value } }))} placeholder={audio.deviceName || "선택"} />
                    </label>
                  </div>
                </div>
                <div className="panel ob__card">
                  <Avatar name={profile.displayName || "Listener"} size="lg" color={profile.avatarColor} />
                  <div className="t-h2" style={{ marginTop: 14 }}>{profile.displayName || "Listener"}</div>
                  <div className="t-small dimmer" style={{ marginTop: 6, lineHeight: 1.6 }}>
                    {[profile.gear.headphonesOrSpeakers, profile.gear.amplifier, profile.gear.dac || audio.deviceName].filter(Boolean).join(" · ") || "장비 정보 없음"}
                  </div>
                </div>
              </div>
            </>
          )}

          {step === 5 && (
            <>
              <span className="badge badge--ok"><Check size={11} />준비 끝</span>
              <h1 className="t-display" style={{ fontSize: 52, marginTop: 16 }}>이제 들을 차례입니다.</h1>
              <dl className="kv ob__summary">
                <dt>음악 폴더</dt><dd>{folders.length > 0 ? folders.join("\n") : "나중에 추가"}</dd>
                <dt>출력 장치</dt><dd>{audio.deviceName ? `${audio.deviceName} · ${audio.exclusiveMode ? "배타" : "공유"}` : "시스템 기본 출력"}</dd>
                <dt>스트리밍</dt><dd>{connected.length > 0 ? connected.map((s) => s.name).join(", ") : "연결 안 함"}</dd>
                <dt>이름</dt><dd>{profile.displayName.trim() || "Listener"}</dd>
              </dl>
            </>
          )}
        </div>

        <footer className="ob__foot">
          {step > 0 && <button className="btn btn--lg btn--quiet" onClick={() => setStep(step - 1)}><ArrowLeft size={16} />이전</button>}
          <div className="grow" />
          {step > 0 && !last && <button className="btn btn--lg btn--quiet" onClick={() => setStep(step + 1)}>건너뛰기</button>}
          {last
            ? <button className="btn btn--lg btn--primary" onClick={finish}>Mono 시작하기<ArrowRight size={16} /></button>
            : <button className="btn btn--lg btn--primary" onClick={() => setStep(step + 1)}>{step === 0 ? "시작하기" : "다음"}<ArrowRight size={16} /></button>}
        </footer>
      </main>
      <ModalHost />
    </div>
  )
}
