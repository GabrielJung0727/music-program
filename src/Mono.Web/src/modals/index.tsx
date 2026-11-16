// 메뉴 · 토스트 · 모달. 앱 어디서든 useApp() 으로 띄우고, 그리는 건 여기 한 곳이다.

import { useEffect, useLayoutEffect, useRef, useState } from "react"
import { Check, Copy, ExternalLink, Globe, Lock, Radio } from "lucide-react"
import { useApp, type ModalState } from "../state/app"
import { useMono, useMonoCommands } from "../state/MonoProvider"
import { RoomMode, StreamingProvider } from "../lib/protocol"
import { openExternal } from "../lib/shell"
import { Modal } from "../ui"

// ── 메뉴 ────────────────────────────────────────────────────────────────────

export function MenuHost() {
  const { menu, closeMenu } = useApp()
  const ref = useRef<HTMLDivElement>(null)
  const [pos, setPos] = useState<{ left: number; top: number } | null>(null)

  // 화면 밖으로 나가지 않게, 그린 뒤 크기를 재서 자리를 잡는다.
  useLayoutEffect(() => {
    if (!menu || !ref.current) { setPos(null); return }
    const box = ref.current.getBoundingClientRect()
    let left = menu.align === "right" ? menu.x - box.width : menu.x
    let top = menu.y
    left = Math.max(8, Math.min(left, window.innerWidth - box.width - 8))
    if (top + box.height > window.innerHeight - 8) top = Math.max(8, menu.y - box.height - 12)
    setPos({ left, top })
  }, [menu])

  useEffect(() => {
    if (!menu) return
    const close = () => closeMenu()
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") closeMenu() }
    const id = setTimeout(() => {
      window.addEventListener("mousedown", onDown)
      window.addEventListener("resize", close)
    }, 0)
    function onDown(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) closeMenu()
    }
    window.addEventListener("keydown", onKey)
    return () => {
      clearTimeout(id)
      window.removeEventListener("mousedown", onDown)
      window.removeEventListener("resize", close)
      window.removeEventListener("keydown", onKey)
    }
  }, [menu, closeMenu])

  if (!menu) return null
  return (
    <div
      ref={ref}
      className="pop"
      role="menu"
      style={{ left: pos?.left ?? menu.x, top: pos?.top ?? menu.y, visibility: pos ? "visible" : "hidden" }}
      onContextMenu={(e) => e.preventDefault()}
    >
      {menu.items.map((item, i) =>
        item.sep ? <div key={i} className="menu-sep" />
          : item.title ? <div key={i} className="menu-title label">{item.title}</div>
            : (
              <button
                key={i}
                role="menuitem"
                className={`menu-item ${item.danger ? "menu-item--danger" : ""}`}
                disabled={item.disabled}
                onClick={() => { closeMenu(); item.onSelect?.() }}
              >
                {item.icon}
                <span className="clip">{item.label}</span>
                {item.hint && <span className="hint">{item.hint}</span>}
              </button>
            ),
      )}
    </div>
  )
}

// ── 토스트 ──────────────────────────────────────────────────────────────────

export function Toasts() {
  const { toasts } = useApp()
  const last = toasts[toasts.length - 1]
  if (!last) return null
  return <div key={last.id} className={`toast ${last.error ? "toast--error" : ""}`} role="status">{last.text}</div>
}

// ── 모달 ────────────────────────────────────────────────────────────────────

export function ModalHost() {
  const { modal, openModal } = useApp()
  if (!modal) return null
  const close = () => openModal(null)
  switch (modal.kind) {
    case "host": return <HostModal onClose={close} />
    case "share": return <ShareModal onClose={close} />
    case "login": return <LoginModal service={modal.service} onClose={close} />
    case "playlist": return <PlaylistModal trackIds={modal.trackIds} onClose={close} />
    case "confirm": return <ConfirmModal modal={modal} onClose={close} />
  }
}

function ConfirmModal({ modal, onClose }: { modal: Extract<ModalState, { kind: "confirm" }>; onClose: () => void }) {
  return (
    <Modal
      title={modal.title}
      subtitle={modal.body}
      onClose={onClose}
      width={420}
      footer={
        <>
          <button className="btn btn--quiet" onClick={onClose}>취소</button>
          <button className={`btn ${modal.danger ? "btn--live" : "btn--primary"}`} onClick={() => { onClose(); modal.onConfirm() }} autoFocus>
            {modal.confirmLabel}
          </button>
        </>
      }
    />
  )
}

/** 듣던 방을 그대로 라운지로 연다. 대기열과 재생 위치는 끊기지 않는다. */
function HostModal({ onClose }: { onClose: () => void }) {
  const app = useApp()
  const { displayName, room } = useMono()
  const cmd = useMonoCommands()
  const [name, setName] = useState(`${displayName}의 라운지`)
  const [mode, setMode] = useState<RoomMode>(RoomMode.Open)
  const [busy, setBusy] = useState(false)

  const open = async () => {
    setBusy(true)
    const id = await cmd.publishRoom(name.trim() || `${displayName}의 라운지`, mode)
    setBusy(false)
    if (!id) { app.toast("라운지를 열지 못했습니다", true); return }
    onClose()
    app.setNowPlayingOpen(false)
    app.go({ page: "room" })
  }

  const options = [
    { mode: RoomMode.Open, icon: <Globe size={18} />, title: "공개", desc: "라운지 목록에 올라가고 누구나 들어올 수 있습니다." },
    { mode: RoomMode.Invite, icon: <Lock size={18} />, title: "초대 전용", desc: "목록에는 보이지만 초대 코드가 있어야 들어옵니다." },
  ]

  return (
    <Modal
      title="라운지 열기"
      subtitle={room?.currentTrack
        ? "지금 듣는 곡과 대기열 그대로 방송을 시작합니다. 들어온 사람은 같은 시각에 맞춰 듣습니다."
        : "방을 열어 두고 곡을 고르면, 들어온 사람이 같은 시각에 맞춰 함께 듣습니다."}
      onClose={onClose}
      icon={<span className="chain__dot" style={{ background: "var(--accent-soft)", color: "var(--accent-text)", width: 38, height: 38 }}><Radio size={18} /></span>}
      footer={
        <>
          <button className="btn btn--quiet" onClick={onClose}>취소</button>
          <button className="btn btn--primary" onClick={open} disabled={busy}><Radio size={15} />{busy ? "여는 중…" : "방송 시작"}</button>
        </>
      }
    >
      <label className="field">
        <span>라운지 이름</span>
        <input className="input" value={name} onChange={(e) => setName(e.target.value)} maxLength={48} autoFocus onKeyDown={(e) => { if (e.key === "Enter") void open() }} />
      </label>
      <div className="field">
        <span>공개 범위</span>
        <div className="col gap-8">
          {options.map((o) => (
            <button key={o.mode} className={`choice ${mode === o.mode ? "is-on" : ""}`} onClick={() => setMode(o.mode)}>
              <span className="choice__icon">{o.icon}</span>
              <span className="grow">
                <span className="t-small" style={{ fontWeight: 620, display: "block" }}>{o.title}</span>
                <span className="t-cap dimmer">{o.desc}</span>
              </span>
              <span className="choice__radio" />
            </button>
          ))}
        </div>
      </div>
    </Modal>
  )
}

function CopyField({ label, value }: { label: string; value: string }) {
  const [copied, setCopied] = useState(false)
  const copy = () => {
    void navigator.clipboard?.writeText(value).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 1600)
    })
  }
  return (
    <div className="field">
      <span>{label}</span>
      <div className="row gap-8">
        <input className="input mono" readOnly value={value} style={{ fontSize: 12.5 }} onFocus={(e) => e.currentTarget.select()} />
        <button className="btn btn--line" onClick={copy} style={{ flex: "none" }}>
          {copied ? <><Check size={15} />복사됨</> : <><Copy size={15} />복사</>}
        </button>
      </div>
    </div>
  )
}

function ShareModal({ onClose }: { onClose: () => void }) {
  const { room } = useMono()
  if (!room) return null
  const key = room.inviteCode ? `&key=${encodeURIComponent(room.inviteCode)}` : ""
  const link = `${location.origin}${location.pathname}?lounge=${encodeURIComponent(room.id)}${key}`
  return (
    <Modal
      title="라운지 초대"
      subtitle={<>「{room.name}」에 들어올 수 있는 링크입니다. 받는 사람도 Mono 가 설치돼 있어야 같은 음질로 들을 수 있습니다.</>}
      onClose={onClose}
      footer={<button className="btn btn--primary" onClick={onClose}>완료</button>}
    >
      <CopyField label="초대 링크" value={link} />
      {room.inviteCode && <CopyField label="초대 코드" value={room.inviteCode} />}
    </Modal>
  )
}

function PlaylistModal({ trackIds, onClose }: { trackIds: string[]; onClose: () => void }) {
  const app = useApp()
  const cmd = useMonoCommands()
  const [title, setTitle] = useState("")
  const [busy, setBusy] = useState(false)

  const create = async () => {
    if (!title.trim()) return
    setBusy(true)
    try {
      await cmd.createPlaylist(title.trim(), trackIds)
      app.toast(`「${title.trim()}」 플레이리스트를 만들었습니다`)
      onClose()
    } catch {
      setBusy(false)
    }
  }

  return (
    <Modal
      title="새 플레이리스트"
      subtitle={trackIds.length > 0 ? `고른 ${trackIds.length}곡을 담아 만듭니다.` : "빈 플레이리스트를 만들고 곡의 ⋯ 메뉴에서 채워 넣습니다."}
      onClose={onClose}
      width={420}
      footer={
        <>
          <button className="btn btn--quiet" onClick={onClose}>취소</button>
          <button className="btn btn--primary" onClick={create} disabled={!title.trim() || busy}>만들기</button>
        </>
      }
    >
      <label className="field">
        <span>이름</span>
        <input className="input" value={title} onChange={(e) => setTitle(e.target.value)} placeholder="예: 늦은 밤 레퍼런스" maxLength={60} autoFocus onKeyDown={(e) => { if (e.key === "Enter") void create() }} />
      </label>
    </Modal>
  )
}

/**
 * 스트리밍 계정 연결.
 *
 * TIDAL 은 OAuth 다 — Core 가 인증 주소를 만들고, 브라우저에서 로그인하면 Core 의
 * /oauth/callback 이 토큰 교환까지 끝낸 뒤 계정 목록을 밀어 준다. 이 창은 그걸 기다린다.
 * Qobuz 는 Core 어댑터가 토큰을 그대로 받는다.
 */
function LoginModal({ service, onClose }: { service: "tidal" | "qobuz"; onClose: () => void }) {
  const app = useApp()
  const { streamingAccounts } = useMono()
  const cmd = useMonoCommands()
  const provider = service === "tidal" ? StreamingProvider.Tidal : StreamingProvider.Qobuz
  const name = service === "tidal" ? "TIDAL" : "Qobuz"
  const [waiting, setWaiting] = useState(false)
  const [busy, setBusy] = useState(false)
  const [note, setNote] = useState<string | null>(null)
  const [token, setToken] = useState("")
  const connected = streamingAccounts.some((a) => a.provider === provider && a.connected)

  // 브라우저에서 로그인을 마쳤는지 Core 에 주기적으로 묻는다.
  useEffect(() => {
    if (!waiting) return
    const id = setInterval(() => cmd.refreshStreamingAccounts(), 2000)
    return () => clearInterval(id)
  }, [waiting, cmd])

  useEffect(() => {
    if (!connected || (!waiting && !busy)) return
    app.toast(`${name} 계정을 연결했습니다`)
    onClose()
  }, [connected, waiting, busy, app, name, onClose])

  const startOAuth = async () => {
    setBusy(true)
    setNote(null)
    const begin = await cmd.beginOAuth(provider)
    setBusy(false)
    if (!begin) { setNote("Core 에 연결하지 못했습니다."); return }
    if (begin.already || begin.connected) {
      cmd.refreshStreamingAccounts()
      app.toast(`${name} 계정이 이미 연결돼 있습니다`)
      onClose()
      return
    }
    if (!begin.authUrl) { setNote(begin.note ?? "인증 주소를 받지 못했습니다."); return }
    openExternal(begin.authUrl)
    setWaiting(true)
  }

  const submitToken = async () => {
    if (!token.trim()) return
    setBusy(true)
    setNote(null)
    try {
      const res = await cmd.linkStreaming(provider, token.trim())
      if (res.ok === false) { setNote(res.error ?? "연결하지 못했습니다."); setBusy(false); return }
      cmd.refreshStreamingAccounts()
    } catch (err) {
      setNote(err instanceof Error ? err.message : String(err))
      setBusy(false)
    }
  }

  return (
    <Modal
      title={`${name} 연결`}
      subtitle={service === "tidal"
        ? "브라우저에서 TIDAL 에 로그인하면 Mono Core 가 계정을 넘겨받습니다. 비밀번호는 Mono 를 거치지 않습니다."
        : "Qobuz 계정의 앱 토큰을 넣으면 Core 가 스트리밍 카탈로그를 라이브러리에 합칩니다."}
      onClose={onClose}
      footer={
        service === "tidal" ? (
          <>
            <button className="btn btn--quiet" onClick={onClose}>닫기</button>
            <button className="btn btn--primary" onClick={startOAuth} disabled={busy || waiting}>
              <ExternalLink size={15} />{waiting ? "로그인을 기다리는 중…" : "브라우저에서 로그인"}
            </button>
          </>
        ) : (
          <>
            <button className="btn btn--quiet" onClick={onClose}>닫기</button>
            <button className="btn btn--primary" onClick={submitToken} disabled={busy || !token.trim()}>{busy ? "연결하는 중…" : "연결"}</button>
          </>
        )
      }
    >
      {service === "qobuz" && (
        <label className="field">
          <span>앱 토큰</span>
          <input className="input mono" type="password" value={token} onChange={(e) => setToken(e.target.value)} autoFocus onKeyDown={(e) => { if (e.key === "Enter") void submitToken() }} />
        </label>
      )}
      {waiting && (
        <div className="row gap-10 t-small dim">
          <i className="lamp lamp--accent" />
          열린 브라우저 창에서 로그인을 마치면 이 창은 저절로 닫힙니다.
        </div>
      )}
      {note && <p className="t-small" style={{ color: "var(--live)" }}>{note}</p>}
    </Modal>
  )
}
