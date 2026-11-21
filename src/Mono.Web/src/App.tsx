import { useEffect, useRef, useState } from "react"
import { AppProvider, useApp } from "./state/app"
import { useMono, useMonoCommands } from "./state/MonoProvider"
import { hasShell, outputStatus } from "./lib/shell"
import { startConfiguredOutput } from "./lib/setup"
import { APP_VERSION, hasUnseenReleaseNotes, markReleaseNotesSeen } from "./lib/changelog"
import Sidebar from "./shell/Sidebar"
import TopBar from "./shell/TopBar"
import PlayerBar from "./shell/PlayerBar"
import Dock from "./shell/Dock"
import NowPlaying from "./shell/NowPlaying"
import { MenuHost, ModalHost, Toasts } from "./modals"
import Home from "./pages/Home"
import Library from "./pages/Library"
import AlbumPage from "./pages/AlbumPage"
import ArtistPage from "./pages/ArtistPage"
import PlaylistPage from "./pages/PlaylistPage"
import Lounges from "./pages/Lounges"
import Room from "./pages/Room"
import Explore from "./pages/Explore"
import SearchPage from "./pages/SearchPage"
import Settings from "./pages/Settings"
import Onboarding from "./pages/Onboarding"

export default function App() {
  return (
    <AppProvider>
      <Shell />
    </AppProvider>
  )
}

function Shell() {
  const app = useApp()
  const { connection, room } = useMono()
  const cmd = useMonoCommands()
  const { route, setup } = app

  // ── 마법사에서 고른 값을 이번 실행에 되살린다 ──────────────────────────────
  // 이름과 출력 장치는 아무도 기억해 주지 않는다. 여기서 돌려놓지 않으면 두 번째 실행부터
  // "Listener" 와 OS 기본 장치로 돌아간다.
  const restored = useRef(false)
  useEffect(() => {
    if (!setup?.completed || restored.current) return
    restored.current = true
    const name = setup.profile?.displayName?.trim()
    if (name) cmd.setDisplayName(name)
    if (setup.audio?.deviceName && hasShell()) {
      void (async () => {
        const roomId = await cmd.ensureSoloRoom()
        await startConfiguredOutput(roomId)
      })()
    }
  }, [setup, cmd])

  // ── 업데이트한 뒤 처음 켰으면 이번 버전에서 바뀐 것을 한 번 보여 준다 ──────────
  // 막 설치해서 마법사를 거친 사람에게는 띄우지 않는다. 그 사람에게는 전부 새것이다.
  const setupAtStart = useRef<boolean | null>(null)
  useEffect(() => {
    if (!setup) return
    if (setupAtStart.current === null) setupAtStart.current = setup.completed
    if (!setup.completed) return
    if (!setupAtStart.current) { markReleaseNotesSeen(); return }
    if (!hasUnseenReleaseNotes()) return
    markReleaseNotesSeen()
    app.openModal({ kind: "releaseNotes", version: APP_VERSION, afterUpdate: true })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [setup?.completed])

  // 출력 워커는 룸 하나에 붙는다. 곡을 틀어 룸이 생기면 그 룸으로 다시 붙여 준다.
  useEffect(() => {
    const roomId = room?.id
    if (!roomId || !hasShell()) return
    let cancelled = false
    void (async () => {
      const status = await outputStatus()
      if (cancelled || !status?.running || status.roomId === roomId) return
      await startConfiguredOutput(roomId)
    })()
    return () => { cancelled = true }
  }, [room?.id])

  // 초대 링크(?lounge=<id>&key=<code>)로 열렸으면 그 라운지로 들어간다.
  useEffect(() => {
    const params = new URLSearchParams(location.search)
    const lounge = params.get("lounge")
    if (!lounge) return
    history.replaceState(null, "", location.pathname + location.hash)
    void cmd.joinRoom(lounge, params.get("key") ?? undefined).then(() => app.go({ page: "room" }))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // 라운지에서 나왔는데 룸 화면에 남아 있으면 목록으로 돌려보낸다.
  useEffect(() => {
    if (route.page === "room" && connection === "open" && (app.role === "idle" || app.role === "solo")) {
      const id = setTimeout(() => app.go({ page: "lounges" }, { replace: true }), 600)
      return () => clearTimeout(id)
    }
  }, [route.page, app.role, connection, app])

  // 화면을 옮기면 맨 위에서 시작한다. 검색어를 고치는 동안에는 그대로 둔다.
  const viewRef = useRef<HTMLDivElement>(null)
  const [scrolled, setScrolled] = useState(false)
  const routeKey = route.page === "search" ? "search" : JSON.stringify(route)
  useEffect(() => {
    viewRef.current?.scrollTo(0, 0)
    setScrolled(false)
  }, [routeKey])

  // 처음 켠 PC 에서는 마법사가 먼저다. 답이 오기 전에는 아무것도 그리지 않는다 —
  // 띄웠다 감추면 첫 프레임이 깜빡인다.
  if (!setup) return <div className="app" />
  if (!setup.completed) return <><Onboarding /><Toasts /></>

  return (
    <div className="app">
      <Sidebar />
      <main className="main">
        <TopBar solid={scrolled} />
        {connection !== "open" && (
          <div className="conn-banner">
            <i className="lamp lamp--warn" />
            {connection === "connecting" ? "Mono Core 에 연결하는 중입니다…" : "Mono Core 와 연결이 끊겼습니다. 다시 연결하는 중…"}
          </div>
        )}
        <div className="view" ref={viewRef} onScroll={(e) => setScrolled(e.currentTarget.scrollTop > 12)}>
          {route.page === "home" && <Home />}
          {route.page === "library" && <Library tab={route.tab ?? "albums"} />}
          {route.page === "album" && <AlbumPage key={route.id} id={route.id} />}
          {route.page === "artist" && <ArtistPage key={route.id} id={route.id} />}
          {route.page === "playlist" && <PlaylistPage key={route.id} id={route.id} />}
          {route.page === "lounges" && <Lounges />}
          {route.page === "room" && <Room />}
          {route.page === "explore" && <Explore />}
          {route.page === "search" && <SearchPage q={route.q} />}
          {route.page === "settings" && <Settings tab={route.tab ?? "audio"} />}
        </div>
      </main>
      <Dock />
      <PlayerBar />
      <NowPlaying />
      <MenuHost />
      <ModalHost />
      <Toasts />
    </div>
  )
}
