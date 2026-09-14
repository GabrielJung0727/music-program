// 첫 실행 마법사가 고른 값들. 이 PC 한 대에 매인 설정이다.
//
// 진실은 Core 의 data/setup.json 에 있다. localStorage 에만 두면 WebView 프로필을
// 지우는 순간 마법사가 다시 뜨고, Core 쪽에서는 읽을 수도 없다.
// localStorage 는 Core 가 아직 안 떴을 때를 위한 사본일 뿐이다 — 화면이 한 프레임
// 깜빡이며 마법사를 보였다 감추는 일을 막는다.

const MIRROR_KEY = "mono_setup"

/** 마법사가 고른 출력. deviceName 은 Output 워커가 부분 일치로 찾는 힌트다. */
export interface AudioSetup {
  driverType: "ASIO" | "WASAPI_EXCLUSIVE"
  deviceId: string
  deviceName: string
  bufferSize: number
  exclusiveMode: boolean
}

export interface ProfileSetup {
  displayName: string
  avatarColor: string
  gear: { headphonesOrSpeakers: string; amplifier: string; dac: string }
}

export interface SetupState {
  completed: boolean
  theme?: "light" | "dark"
  folders?: string[]
  autoWatch?: boolean
  audio?: AudioSetup
  profile?: ProfileSetup
  savedAt?: string
}

const EMPTY: SetupState = { completed: false }

function readMirror(): SetupState | null {
  try {
    const raw = localStorage.getItem(MIRROR_KEY)
    return raw ? (JSON.parse(raw) as SetupState) : null
  } catch {
    return null
  }
}

function writeMirror(state: SetupState) {
  try { localStorage.setItem(MIRROR_KEY, JSON.stringify(state)) } catch { /* 사본일 뿐이다 */ }
}

/**
 * Core 에 저장된 설정. Core 가 응답하지 않으면 사본으로 물러난다.
 * 사본조차 없으면 "아직 설정 안 함" — 마법사를 띄우는 게 맞다.
 */
export async function loadSetup(signal?: AbortSignal): Promise<SetupState> {
  try {
    const res = await fetch("/api/setup", { signal, headers: { accept: "application/json" } })
    if (res.ok) {
      const state = (await res.json()) as SetupState
      writeMirror(state)
      return state
    }
  } catch { /* 아래 사본으로 */ }
  return readMirror() ?? EMPTY
}

/** 보낸 필드만 덮어쓴다. Core 가 죽어 있어도 사본에는 남겨 둔다. */
export async function saveSetup(patch: Partial<SetupState>): Promise<SetupState> {
  const merged: SetupState = { ...(readMirror() ?? EMPTY), ...patch }
  writeMirror(merged)
  try {
    const res = await fetch("/api/setup", {
      method: "PUT",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(patch),
    })
    if (res.ok) {
      const state = (await res.json()) as SetupState
      writeMirror(state)
      return state
    }
  } catch { /* 사본은 이미 갱신했다 */ }
  return merged
}

/** 마법사를 처음부터 다시 보고 싶을 때. Settings 의 워크스페이스 초기화가 부른다. */
export async function clearSetup(): Promise<void> {
  try { localStorage.removeItem(MIRROR_KEY) } catch { /* ignore */ }
  try { await fetch("/api/setup", { method: "DELETE" }) } catch { /* ignore */ }
}

/** 마법사가 고른 드라이버를 Output 워커의 백엔드 이름으로 옮긴다. */
export function backendFor(audio: AudioSetup | undefined): string | undefined {
  if (!audio) return undefined
  if (audio.driverType === "ASIO") return "asio"
  return audio.exclusiveMode === false ? "shared" : "exclusive"
}
