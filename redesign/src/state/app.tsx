// 화면 전체가 나눠 쓰는 것들: 어디에 있는가(route), 무엇을 띄웠는가(패널·모달·메뉴),
// 그리고 카탈로그에서 접어 만든 앨범·아티스트 목록.
//
// Core 의 상태(MonoProvider)와는 층이 다르다. 여기 있는 것은 이 창에서만 의미가 있다.

import {
  createContext, useCallback, useContext, useEffect, useLayoutEffect, useMemo, useRef, useState,
  type MouseEvent as ReactMouseEvent, type ReactNode,
} from "react"
import { useMono, useMonoCommands } from "./MonoProvider"
import { buildAlbums, buildArtists, sessionRole, type Album, type Artist, type SessionRole } from "../lib/model"
import type { CatalogTrack } from "../lib/protocol"
import { loadSetup, saveSetup, type SetupState } from "../lib/setup"

// ── 경로 ────────────────────────────────────────────────────────────────────

export type LibTab = "albums" | "artists" | "tracks" | "composers" | "playlists"
export type SettingsTab = "audio" | "dsp" | "library" | "accounts" | "general"

export type Route =
  | { page: "home" }
  | { page: "library"; tab?: LibTab }
  | { page: "album"; id: string }
  | { page: "artist"; id: string }
  | { page: "playlist"; id: string }
  | { page: "lounges" }
  | { page: "room" }
  | { page: "explore" }
  | { page: "search"; q: string }
  | { page: "settings"; tab?: SettingsTab }

function toHash(r: Route): string {
  switch (r.page) {
    case "library": return `#/library${r.tab ? `/${r.tab}` : ""}`
    case "album": case "artist": case "playlist": return `#/${r.page}/${encodeURIComponent(r.id)}`
    case "search": return `#/search/${encodeURIComponent(r.q)}`
    case "settings": return `#/settings${r.tab ? `/${r.tab}` : ""}`
    default: return `#/${r.page}`
  }
}

function fromHash(hash: string): Route {
  const [page, arg] = hash.replace(/^#\/?/, "").split("/")
  const value = arg ? decodeURIComponent(arg) : ""
  switch (page) {
    case "library": return { page, tab: (value || undefined) as LibTab | undefined }
    case "album": case "artist": case "playlist": return value ? { page, id: value } : { page: "home" }
    case "lounges": case "room": case "explore": return { page }
    case "search": return { page, q: value }
    case "settings": return { page, tab: (value || undefined) as SettingsTab | undefined }
    default: return { page: "home" }
  }
}

// ── 메뉴 · 모달 ─────────────────────────────────────────────────────────────

export interface MenuItem {
  label?: string
  icon?: ReactNode
  hint?: string
  danger?: boolean
  disabled?: boolean
  sep?: boolean
  title?: string
  onSelect?: () => void
}

interface MenuState { x: number; y: number; items: MenuItem[]; align: "left" | "right" }

export type ModalState =
  | { kind: "host" }
  | { kind: "share" }
  | { kind: "login"; service: "tidal" | "qobuz" }
  | { kind: "playlist"; trackIds: string[] }
  | { kind: "confirm"; title: string; body: string; confirmLabel: string; danger?: boolean; onConfirm: () => void }

export type Panel = "queue" | "lyrics" | null

interface Toast { id: number; text: string; error?: boolean }

interface AppContextValue {
  route: Route
  go(route: Route, opts?: { replace?: boolean }): void
  back(): void
  forward(): void
  canBack: boolean
  canForward: boolean

  albums: Album[]
  albumById: Map<string, Album>
  artists: Artist[]
  trackById: Map<string, CatalogTrack>
  role: SessionRole
  /** 게스트는 호스트의 재생을 따라갈 뿐이다. */
  canControl: boolean
  /** 방금 방송을 시작했다(0.7초 동안만 참). On Air 링이 한 번 퍼지는 데 쓴다. */
  onAirFresh: boolean

  playTracks(trackIds: string[], index?: number): void
  playNext(track: CatalogTrack): void
  addToQueue(track: CatalogTrack): void

  panel: Panel
  setPanel(panel: Panel): void
  nowPlayingOpen: boolean
  setNowPlayingOpen(open: boolean): void

  modal: ModalState | null
  openModal(modal: ModalState | null): void

  menu: MenuState | null
  openMenu(e: ReactMouseEvent | { x: number; y: number }, items: MenuItem[], align?: "left" | "right"): void
  closeMenu(): void

  toasts: Toast[]
  toast(text: string, error?: boolean): void

  theme: "dark" | "light"
  setTheme(theme: "dark" | "light"): void

  setup: SetupState | null
  setSetup(next: SetupState): void
}

const Ctx = createContext<AppContextValue | null>(null)

export function useApp(): AppContextValue {
  const ctx = useContext(Ctx)
  if (!ctx) throw new Error("useApp must be used inside <AppProvider>")
  return ctx
}

export function AppProvider({ children }: { children: ReactNode }) {
  const { catalog, room, peerId, lastError } = useMono()
  const cmd = useMonoCommands()

  // ── 경로: 자체 스택을 들고, 주소창의 해시는 거울로만 쓴다 ──────────────────
  const [stack, setStack] = useState<Route[]>(() => [fromHash(location.hash)])
  const [index, setIndex] = useState(0)
  const route = stack[index]

  const go = useCallback((next: Route, opts?: { replace?: boolean }) => {
    setStack((prev) => {
      const head = prev.slice(0, index + 1)
      if (opts?.replace) head[head.length - 1] = next
      else head.push(next)
      return head
    })
    if (!opts?.replace) setIndex((i) => i + 1)
  }, [index])

  const back = useCallback(() => setIndex((i) => Math.max(0, i - 1)), [])
  const forward = useCallback(() => setIndex((i) => Math.min(stack.length - 1, i + 1)), [stack.length])

  useEffect(() => {
    const hash = toHash(route)
    if (location.hash !== hash) history.replaceState(null, "", hash)
  }, [route])

  // 주소창에서 해시를 직접 바꾼 경우(붙여 넣은 링크 등)에도 따라간다.
  useEffect(() => {
    const onHash = () => {
      if (location.hash && location.hash !== toHash(route)) go(fromHash(location.hash))
    }
    window.addEventListener("hashchange", onHash)
    return () => window.removeEventListener("hashchange", onHash)
  }, [route, go])

  // ── 카탈로그에서 접어 만든 목록 ──────────────────────────────────────────
  const albums = useMemo(() => buildAlbums(catalog), [catalog])
  const albumById = useMemo(() => new Map(albums.map((a) => [a.id, a])), [albums])
  const artists = useMemo(() => buildArtists(catalog), [catalog])
  const trackById = useMemo(() => new Map(catalog.map((t) => [t.id, t])), [catalog])

  const role = sessionRole(room, peerId)
  const canControl = role !== "guest"

  const prevRole = useRef(role)
  const [onAirFresh, setOnAirFresh] = useState(false)
  useEffect(() => {
    const was = prevRole.current
    prevRole.current = role
    if (role !== "host" || was === "host") return
    setOnAirFresh(true)
    const id = setTimeout(() => setOnAirFresh(false), 700)
    return () => clearTimeout(id)
  }, [role])

  // ── 토스트 ──────────────────────────────────────────────────────────────
  const [toasts, setToasts] = useState<Toast[]>([])
  const seq = useRef(0)
  const toast = useCallback((text: string, error = false) => {
    const id = ++seq.current
    setToasts((prev) => [...prev.slice(-1), { id, text, error }])
    setTimeout(() => setToasts((prev) => prev.filter((t) => t.id !== id)), 3200)
  }, [])

  // Core 가 명령을 거절하면 사유를 그대로 띄운다.
  useEffect(() => {
    if (!lastError) return
    toast(lastError, true)
    cmd.clearError()
  }, [lastError, toast, cmd])

  // ── 재생 ────────────────────────────────────────────────────────────────
  const roomRef = useRef(room)
  roomRef.current = room

  const playTracks = useCallback((trackIds: string[], startIndex = 0) => {
    if (!canControl) { toast("라운지에서는 호스트가 재생을 고릅니다"); return }
    void cmd.playFrom(trackIds, startIndex)
  }, [cmd, canControl, toast])

  const addToQueue = useCallback((track: CatalogTrack) => {
    if (!canControl) { toast("라운지에서는 호스트가 재생을 고릅니다"); return }
    void (async () => {
      await cmd.ensureSoloRoom()
      cmd.enqueue(track.id)
      toast(`대기열에 추가: ${track.title}`)
    })()
  }, [cmd, canControl, toast])

  const playNext = useCallback((track: CatalogTrack) => {
    if (!canControl) { toast("라운지에서는 호스트가 재생을 고릅니다"); return }
    void (async () => {
      await cmd.ensureSoloRoom()
      // Core 에는 "다음에 재생"이 따로 없다. 큐 끝에 넣고 현재 곡 바로 뒤로 끌어올린다.
      const r = roomRef.current
      const from = r?.queue.length ?? 0
      const target = (r?.queueIndex ?? -1) + 1
      cmd.enqueue(track.id)
      if (from > target) cmd.moveInQueue(from, target - from)
      toast(`다음에 재생: ${track.title}`)
    })()
  }, [cmd, canControl, toast])

  // ── 패널 · 모달 · 메뉴 ──────────────────────────────────────────────────
  const [panel, setPanel] = useState<Panel>(null)
  const [nowPlayingOpen, setNowPlayingOpen] = useState(false)
  const [modal, openModal] = useState<ModalState | null>(null)
  const [menu, setMenu] = useState<MenuState | null>(null)

  const openMenu = useCallback<AppContextValue["openMenu"]>((e, items, align = "left") => {
    if ("currentTarget" in e) {
      e.preventDefault()
      e.stopPropagation()
      if (e.type === "contextmenu") {
        setMenu({ x: e.clientX, y: e.clientY, items, align: "left" })
        return
      }
      const box = (e.currentTarget as HTMLElement).getBoundingClientRect()
      setMenu({ x: align === "right" ? box.right : box.left, y: box.bottom + 6, items, align })
      return
    }
    setMenu({ x: e.x, y: e.y, items, align })
  }, [])
  const closeMenu = useCallback(() => setMenu(null), [])

  // ── 테마 · 이 PC 의 설정 ────────────────────────────────────────────────
  const [theme, setThemeState] = useState<"dark" | "light">(() => {
    try { return (localStorage.getItem("mono_theme") as "dark" | "light") || "dark" } catch { return "dark" }
  })
  const [setup, setSetup] = useState<SetupState | null>(null)

  useLayoutEffect(() => {
    document.documentElement.dataset.theme = theme
    try { localStorage.setItem("mono_theme", theme) } catch { /* 저장이 막힌 환경 */ }
  }, [theme])

  useEffect(() => {
    const ac = new AbortController()
    void loadSetup(ac.signal).then((state) => {
      setSetup(state)
      if (state.theme) setThemeState(state.theme)
    })
    return () => ac.abort()
  }, [])

  const setTheme = useCallback((next: "dark" | "light") => {
    setThemeState(next)
    // 마법사를 마치기 전에는 저장하지 않는다 — 아직 모르는 값으로 덮어쓰게 된다.
    if (setup?.completed) void saveSetup({ theme: next })
  }, [setup?.completed])

  const value = useMemo<AppContextValue>(() => ({
    route, go, back, forward, canBack: index > 0, canForward: index < stack.length - 1,
    albums, albumById, artists, trackById, role, canControl, onAirFresh,
    playTracks, playNext, addToQueue,
    panel, setPanel, nowPlayingOpen, setNowPlayingOpen,
    modal, openModal, menu, openMenu, closeMenu,
    toasts, toast, theme, setTheme, setup, setSetup,
  }), [
    route, go, back, forward, index, stack.length, albums, albumById, artists, trackById, role, canControl, onAirFresh,
    playTracks, playNext, addToQueue, panel, nowPlayingOpen, modal, menu, openMenu, closeMenu,
    toasts, toast, theme, setTheme, setup,
  ])

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}
