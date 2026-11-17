import { TrackTransition } from "./protocol"
import { startOutput } from "./shell"

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
  /** ASIO 에서 소리를 내보낼 첫 출력 채널(0부터). 헤드폰 단자가 3/4 에 있다면 2. */
  asioChannelOffset?: number
}

/**
 * 곡이 끝까지 재생되어 다음 곡으로 넘어갈 때의 방식. 이 PC 의 취향이라 여기 두고,
 * 방을 새로 만들 때마다 그 방에 적용한다(Core 는 방마다 기억한다).
 */
export interface PlaybackSetup {
  transition: TrackTransition
  crossfadeSec: number
}

export const DEFAULT_PLAYBACK: PlaybackSetup = { transition: TrackTransition.Gapless, crossfadeSec: 5 }

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
  /** Core 의 7700 · 7701 을 LAN 에 열지. Core 가 뜰 때 읽는다(NetworkExposure.cs). */
  allowRemoteOutputs?: boolean
  audio?: AudioSetup
  playback?: PlaybackSetup
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

/** 마지막으로 읽은 설정의 동기 사본. 장치 이름처럼 그 자리에서 필요한 값을 꺼낼 때 쓴다. */
export function cachedSetup(): SetupState {
  return readMirror() ?? EMPTY
}

/**
 * 저장된 출력 장치로 Output 워커를 띄운다.
 *
 * startOutput 을 직접 부르면 백엔드와 장치 힌트를 빠뜨리기 쉽다. 빠뜨리면 마법사에서
 * 무엇을 골랐든 OS 기본 장치로 열리는데, 화면에는 고른 장치 이름이 그대로 떠 있어서
 * 소리가 다른 데서 난다는 걸 알아챌 방법이 없다. 시작 지점을 한 곳으로 모아 둔다.
 */
export async function startConfiguredOutput(roomId?: string | null) {
  const audio = cachedSetup().audio
  // 채널은 ASIO 일 때 항상 보낸다. 보내지 않으면 셸이 앞서 쓰던 값을 그대로 둔다.
  const channel = audio?.driverType === "ASIO" ? audio.asioChannelOffset ?? 0 : undefined
  return startOutput(roomId ?? null, backendFor(audio), audio?.deviceName, channel)
}

/** 저장된 곡 전환 설정. 고른 적이 없으면 끊김 없이 잇기다. */
export function playbackPrefs(): PlaybackSetup {
  const saved = cachedSetup().playback
  if (!saved) return DEFAULT_PLAYBACK
  const sec = Number(saved.crossfadeSec)
  return {
    transition: saved.transition === TrackTransition.Off || saved.transition === TrackTransition.Crossfade
      ? saved.transition
      : TrackTransition.Gapless,
    crossfadeSec: Number.isFinite(sec) ? Math.max(1, Math.min(12, Math.round(sec))) : DEFAULT_PLAYBACK.crossfadeSec,
  }
}
