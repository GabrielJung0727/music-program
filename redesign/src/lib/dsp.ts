// DSP 켜고 끄기.
//
// "끄기"는 프리셋 Off(0)를 보내는 것이다. Core 는 방마다 프리셋 하나만 기억하므로, 끄고 나면
// 무엇을 쓰고 있었는지 잊는다. 다시 켤 때 되돌리려고 마지막 프리셋을 이 PC 에 적어 둔다.
//
// 0.5.9.3 까지의 Core 는 set_dsp 의 flag 를 보지 않았다(프리셋이 Off 가 아니면 켜짐). 그 뒤로는
// flag=false 도 끄기로 받는다. 프리셋 0 을 보내는 이 방식은 두 쪽 모두에서 통한다.

const KEY = "mono_dsp_preset"

function lastPreset(): number {
  try {
    const n = Number(localStorage.getItem(KEY))
    return Number.isInteger(n) && n > 0 ? n : 1
  } catch {
    return 1
  }
}

function remember(preset: number) {
  if (preset <= 0) return
  try { localStorage.setItem(KEY, String(preset)) } catch { /* 저장이 막힌 환경 */ }
}

/** 고른 프리셋으로 켠다. */
export function selectDsp(setDsp: (preset: number, enabled: boolean) => void, preset: number) {
  remember(preset)
  setDsp(preset, true)
}

/** 켜면 마지막에 쓰던 프리셋으로, 끄면 Off 로. */
export function toggleDsp(setDsp: (preset: number, enabled: boolean) => void, currentPreset: number, on: boolean) {
  if (on) {
    setDsp(lastPreset(), true)
    return
  }
  remember(currentPreset)
  setDsp(0, false)
}
