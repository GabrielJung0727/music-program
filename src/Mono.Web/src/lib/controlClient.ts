import { MSG, type MonoMessage } from "./protocol"

export type ConnectionState = "connecting" | "open" | "reconnecting" | "closed"

type Listener = (msg: MonoMessage) => void
type StateListener = (state: ConnectionState) => void

/** 응답을 기다리는 명령. Core 는 요청 id 를 돌려주지 않으므로 타입으로 짝을 맞춘다. */
interface Waiter {
  type: string
  resolve: (msg: MonoMessage) => void
  reject: (err: Error) => void
  timer: ReturnType<typeof setTimeout>
}

function wsUrl(): string {
  const proto = location.protocol === "https:" ? "wss:" : "ws:"
  return `${proto}//${location.host}/ws/control`
}

/**
 * Core 컨트롤 플레인 클라이언트. TCP 7700 과 같은 MonoMessage 규약을
 * WebSocket 으로 말한다. 끊기면 지수 백오프로 다시 붙고, 붙을 때마다 hello 를 다시 보낸다.
 */
export class ControlClient {
  private socket: WebSocket | null = null
  private listeners = new Set<Listener>()
  private stateListeners = new Set<StateListener>()
  private waiters: Waiter[] = []
  private backoff = 500
  private closedByUs = false
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null
  /** 연결 전에 쌓인 명령. hello·welcome 이후 순서대로 흘려보낸다. */
  private outbox: MonoMessage[] = []

  state: ConnectionState = "closed"
  peerId: string | null = null
  displayName: string

  constructor(displayName: string) {
    this.displayName = displayName
  }

  // ── 수명 ──────────────────────────────────────────────────────────────────

  connect() {
    if (this.socket && (this.socket.readyState === WebSocket.OPEN || this.socket.readyState === WebSocket.CONNECTING))
      return

    this.closedByUs = false
    this.setState(this.peerId ? "reconnecting" : "connecting")

    let socket: WebSocket
    try {
      socket = new WebSocket(wsUrl())
    } catch {
      this.scheduleReconnect()
      return
    }
    this.socket = socket

    socket.onopen = () => {
      this.backoff = 500
      // peerId 를 유지하면 재연결 뒤에도 Core 가 같은 사람으로 인식한다.
      this.raw({ type: MSG.hello, peerId: this.peerId ?? undefined, displayName: this.displayName })
    }

    socket.onmessage = (e) => {
      let msg: MonoMessage
      try {
        msg = JSON.parse(e.data as string) as MonoMessage
      } catch {
        return
      }

      if (msg.type === MSG.welcome) {
        this.peerId = msg.peerId ?? this.peerId
        this.setState("open")
        const queued = this.outbox
        this.outbox = []
        for (const m of queued) this.raw(m)
      }

      this.settle(msg)
      for (const fn of this.listeners) {
        try {
          fn(msg)
        } catch (err) {
          console.error("control listener threw", err)
        }
      }
    }

    socket.onclose = () => {
      this.socket = null
      this.failAllWaiters(new Error("연결이 끊어졌습니다"))
      if (this.closedByUs) {
        this.setState("closed")
        return
      }
      this.scheduleReconnect()
    }

    socket.onerror = () => {
      // onclose 가 뒤따라 오므로 여기서는 아무것도 하지 않는다.
    }
  }

  close() {
    this.closedByUs = true
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer)
      this.reconnectTimer = null
    }
    this.failAllWaiters(new Error("closed"))
    this.socket?.close()
    this.socket = null
    this.setState("closed")
  }

  private scheduleReconnect() {
    this.setState("reconnecting")
    if (this.reconnectTimer) return
    const delay = this.backoff
    this.backoff = Math.min(this.backoff * 2, 10_000)
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null
      this.connect()
    }, delay)
  }

  private setState(next: ConnectionState) {
    if (this.state === next) return
    this.state = next
    for (const fn of this.stateListeners) fn(next)
  }

  // ── 송수신 ────────────────────────────────────────────────────────────────

  /** 응답을 기다리지 않는 명령. 연결 전이면 큐에 쌓았다가 welcome 뒤에 보낸다. */
  send(msg: MonoMessage) {
    if (this.state !== "open" && msg.type !== MSG.hello) {
      // 큐가 무한히 자라지 않게 막는다 — Core 가 오래 죽어 있어도 메모리를 먹으면 안 된다.
      if (this.outbox.length < 200) this.outbox.push(msg)
      this.connect()
      return
    }
    this.raw(msg)
  }

  private raw(msg: MonoMessage) {
    if (this.socket?.readyState !== WebSocket.OPEN) return
    this.socket.send(JSON.stringify(msg))
  }

  /**
   * 응답 한 건을 기다린다. Core 는 요청과 응답을 id 로 묶지 않으므로
   * "같은 타입의 다음 메시지"를 응답으로 본다. 같은 타입을 동시에 두 번
   * 보내지 않는 한 안전하고, 실제로 그럴 일은 없다.
   */
  request(msg: MonoMessage, expect: string = msg.type, timeoutMs = 15_000): Promise<MonoMessage> {
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.waiters = this.waiters.filter((w) => w.timer !== timer)
        reject(new Error(`${msg.type} 응답이 없습니다`))
      }, timeoutMs)

      this.waiters.push({ type: expect, resolve, reject, timer })
      this.send(msg)
    })
  }

  private settle(msg: MonoMessage) {
    const index = this.waiters.findIndex((w) => w.type === msg.type)
    if (index >= 0) {
      const [waiter] = this.waiters.splice(index, 1)
      clearTimeout(waiter.timer)
      waiter.resolve(msg)
      return
    }

    // 명령이 거절되면 error 로 온다. 가장 오래 기다린 쪽에 실패를 돌려준다.
    if (msg.type === MSG.error && this.waiters.length > 0) {
      const [waiter] = this.waiters.splice(0, 1)
      clearTimeout(waiter.timer)
      waiter.reject(new Error(msg.error ?? "명령이 거절됐습니다"))
    }
  }

  private failAllWaiters(err: Error) {
    const pending = this.waiters
    this.waiters = []
    for (const w of pending) {
      clearTimeout(w.timer)
      w.reject(err)
    }
  }

  // ── 구독 ──────────────────────────────────────────────────────────────────

  on(fn: Listener): () => void {
    this.listeners.add(fn)
    return () => this.listeners.delete(fn)
  }

  onState(fn: StateListener): () => void {
    this.stateListeners.add(fn)
    return () => this.stateListeners.delete(fn)
  }
}
