// Mono.Control(WebView2 셸)이 심어 주는 네이티브 브리지.
// 브라우저에서 그냥 열면 window.mono 가 없다 — 호출부는 항상 hasShell() 을 먼저 본다.

export interface OutputStatus {
  running: boolean
  roomId: string | null
  backend: string
  coreRunning: boolean
}

export interface UpdateStatus {
  message: string
  available: boolean
  version?: string
  current: string
  installed: boolean
}

interface MonoBridge {
  isShell: true
  call(op: string, args?: Record<string, unknown>): Promise<unknown>
  on(event: string, fn: (data: unknown) => void): () => void
  pickFolder(title?: string): Promise<string | null>
  pickFile(title?: string, filter?: string): Promise<string | null>
  openExternal(url: string): Promise<boolean>
  output: {
    start(roomId?: string | null, backend?: string): Promise<{ ok: boolean; error?: string | null }>
    stop(): Promise<{ ok: boolean }>
    restart(): Promise<{ ok: boolean; error?: string | null }>
    status(): Promise<OutputStatus>
  }
  update: {
    check(): Promise<UpdateStatus>
    apply(): Promise<boolean>
  }
  app: {
    version(): Promise<{ version: string; installed: boolean }>
    logPath(): Promise<string>
    quit(): Promise<boolean>
    minimizeToTray(): Promise<boolean>
  }
}

declare global {
  interface Window {
    mono?: MonoBridge
  }
}

export function hasShell(): boolean {
  return typeof window !== "undefined" && window.mono?.isShell === true
}

export function shell(): MonoBridge | null {
  return hasShell() ? window.mono! : null
}

/** 셸이 있으면 네이티브 폴더 선택창, 없으면 null(호출부가 직접 입력받는다). */
export async function pickFolder(title = "음악 폴더 선택"): Promise<string | null> {
  const bridge = shell()
  if (!bridge) return null
  try {
    return await bridge.pickFolder(title)
  } catch {
    return null
  }
}

export async function pickFile(title: string, filter: string): Promise<string | null> {
  const bridge = shell()
  if (!bridge) return null
  try {
    return await bridge.pickFile(title, filter)
  } catch {
    return null
  }
}

/** 외부 링크. 셸에서는 기본 브라우저로, 브라우저에서는 새 탭으로. */
export function openExternal(url: string) {
  const bridge = shell()
  if (bridge) {
    void bridge.openExternal(url)
    return
  }
  window.open(url, "_blank", "noopener,noreferrer")
}

/** 출력 워커. 셸이 없으면 Output 프로세스를 띄울 방법이 없으므로 안내가 필요하다. */
export async function startOutput(roomId?: string | null, backend?: string) {
  const bridge = shell()
  if (!bridge) return { ok: false, error: "출력은 Mono 데스크톱 앱에서만 연결할 수 있습니다." }
  try {
    return await bridge.output.start(roomId ?? null, backend)
  } catch (err) {
    return { ok: false, error: err instanceof Error ? err.message : String(err) }
  }
}

export async function stopOutput() {
  await shell()?.output.stop()
}

export async function restartOutput() {
  const bridge = shell()
  if (!bridge) return { ok: false, error: "데스크톱 앱에서만 가능합니다." }
  return bridge.output.restart()
}

/** 업데이트 다운로드 진행률(0–100). 셸이 없으면 구독할 것도 없다. */
export function onUpdateProgress(fn: (percent: number) => void): () => void {
  const bridge = shell()
  if (!bridge) return () => {}
  return bridge.on("update.progress", (data) => {
    const percent = (data as { percent?: number } | null)?.percent
    if (typeof percent === "number") fn(percent)
  })
}

/** 업데이트 확인. 셸이 없으면(브라우저) 확인할 방법이 없다. */
export async function checkForUpdate(): Promise<UpdateStatus | null> {
  const bridge = shell()
  if (!bridge) return null
  return bridge.update.check()
}

/** 내려받아 적용하고 재시작한다. 성공하면 이 창은 곧 사라진다. */
export async function applyUpdate(): Promise<void> {
  const bridge = shell()
  if (!bridge) throw new Error("데스크톱 앱에서만 업데이트할 수 있습니다.")
  await bridge.update.apply()
}

export async function outputStatus(): Promise<OutputStatus | null> {
  const bridge = shell()
  if (!bridge) return null
  try {
    return await bridge.output.status()
  } catch {
    return null
  }
}
