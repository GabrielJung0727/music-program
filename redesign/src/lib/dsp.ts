// DSP 켜고 끄기.
//
// Core 의 set_dsp 는 flag 를 보지 않는다. 프리셋이 Off(0)면 꺼지고, 그 밖이면 켜진다
// (CommandProcessor.cs: DspEnabled = DspPreset != Off). 그래서 "끄기"는 프리셋 0 을 보내는 것이고,
// 그러면 방은 어떤 프리셋을 쓰고 있었는지 잊는다. 다시 켤 때 되돌리려고 마지막 프리셋을 이 PC 에 적어 둔다.

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
