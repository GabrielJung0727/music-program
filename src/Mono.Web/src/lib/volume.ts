// 음소거를 풀 때 돌아갈 볼륨.
//
// 0.7.0 까지는 음소거를 풀면 늘 60% 로 돌아갔다. 30% 로 듣다가 음소거를 풀면 갑자기 커지고,
// 100% 로 듣던 사람은 매번 다시 올려야 했다. 이제 출력마다 마지막으로 "정한" 볼륨을 기억한다.
//
// 기억하는 때는 사람이 값을 정한 순간뿐이다: 슬라이더를 놓았을 때, 음소거를 누르기 직전.
// 슬라이더를 끌어 0 까지 내리는 동안 지나간 값(3%, 1% …)을 기억하면 음소거를 풀었을 때
// 거의 들리지 않는 값으로 돌아가기 때문이다. Mono 를 다시 켜도 남도록 localStorage 에 둔다.

const KEY = "mono.audibleVolume"

/** 기억한 값이 없을 때 돌아갈 값. 갑자기 크게 나지 않도록 100% 가 아니라 예전과 같은 60% 다. */
export const DEFAULT_UNMUTE_VOLUME = 0.6

function load(): Record<string, number> {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(KEY) ?? "{}")
    return parsed && typeof parsed === "object" ? (parsed as Record<string, number>) : {}
  } catch {
    return {}
  }
}

function usable(v: unknown): v is number {
  return typeof v === "number" && v > 0 && v <= 1
}

/** 이 출력의 볼륨을 사람이 v(0–1)로 정했다. 0 은 음소거라 기억하지 않는다. */
export function rememberVolume(peerId: string | undefined, v: number) {
  if (!peerId || !usable(v)) return
  const all = load()
  if (all[peerId] === v) return
  all[peerId] = v
  try { localStorage.setItem(KEY, JSON.stringify(all)) } catch { /* 저장이 막힌 환경이면 이번 실행 동안만 60% 로 돌아간다 */ }
}

/** 아직 기억한 값이 없으면 지금 값을 기억해 둔다. 켜자마자 슬라이더를 0 으로 끌어내린 경우를 위해서다. */
export function seedVolume(peerId: string | undefined, v: number) {
  if (!peerId || usable(load()[peerId])) return
  rememberVolume(peerId, v)
}

/** 음소거를 풀 때 보낼 값(0–1). */
export function unmuteVolume(peerId: string | undefined): number {
  const v = peerId ? load()[peerId] : undefined
  return usable(v) ? v : DEFAULT_UNMUTE_VOLUME
}
