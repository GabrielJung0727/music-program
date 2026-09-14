import { useState, useRef, useEffect, useCallback, useMemo } from "react"
import LiveLoungePage, { type HostLaunchOpts } from "./components/lounge/LiveLoungePage"
import LiveLoungeRoom from "./components/lounge/LiveLoungeRoom"
import HomePage from "./components/home/HomePage"
import AlbumDetailView from "./components/detail/AlbumDetailView"
import ArtistDetailView from "./components/detail/ArtistDetailView"
import AccountRequiredGateModal from "./components/AccountRequiredGateModal"
import StreamingLoginModal from "./components/StreamingLoginModal"
import OnboardingWizard from "./components/onboarding/OnboardingWizard"
import type { ListenerProfile } from "./components/onboarding/Step3AudiophileRig"
import QueueDrawer from "./components/QueueDrawer"
import TrackActionMenu, { type TrackActionTarget } from "./components/TrackActionMenu"
import FullscreenPlayer from "./components/FullscreenPlayer"
import SettingsPage from "./components/SettingsPage"
import SettingsModal from "./components/SettingsModal"
import ShareModal, { type ShareData } from "./components/ShareModal"
import MyLibraryPage from "./components/MyLibraryPage"
import ExplorePage from "./components/ExplorePage"
import TopNav, { type CurrentTab } from "./components/TopNav"
import PlayerBar, { type PlayerMode } from "./components/PlayerBar"
import { MonoIcon } from "./components/icons/MonoIcons"
import {
  type Profile,
  type SearchTrack,
  type SearchAlbum,
  type QueueTrack,
  INITIAL_PROFILES,
  type LoungeRoom,
} from "./data/types"
import { useLiveSession, useToast } from "./state/useLiveSession"
import { useDebounced } from "./state/useDebounced"
import { libArtists } from "./lib/libraryData"
import {
  filterChips,
  hallOfFame,
  homeAlbums,
  insightCards,
  labelTiles,
  loungeCards as toLoungeCards,
  mostPlayed as toMostPlayed,
} from "./lib/homeData"
import { useMono, useMonoCommands } from "./state/MonoProvider"
import { RoomMode, StreamingProvider } from "./lib/protocol"

// ── (Components extracted to dedicated files) ─────────────────────────────────
// LoungeCard, SocialLoungesSection, AlbumCard, RecentlyAddedSection,
// ListeningInsightsSection, LabelBanner, LabelArchivesSection, HomePage
//   → src/components/home/HomePage.tsx
// HostModal, DiscoveryBadge, LoungeGridCard, LiveLoungePage
//   → src/components/lounge/LiveLoungePage.tsx
// LiveLoungeRoom → src/components/lounge/LiveLoungeRoom.tsx
// AlbumDetailView → src/components/detail/AlbumDetailView.tsx
// ArtistDetailView → src/components/detail/ArtistDetailView.tsx

// (inline component definitions removed — see dedicated files listed above)
// ── Queue helpers ─────────────────────────────────────────────────────────────

let _queueIdCounter = 0
function toQueueTrack(t: TrackActionTarget): import("./data/types").QueueTrack {
  return {
    id: `q-${Date.now()}-${_queueIdCounter++}`,
    title: t.title,
    artist: t.artist ?? "Unknown",
    album: t.album ?? "",
    duration: t.duration ?? "0:00",
    format: t.format ?? "Hi-Res FLAC",
    dr: t.dr,
    art: t.art || (t as any).coverUrl,
    source: t.source,
  }
}

// ── App Root ─────────────────────────────────────────────────────────────────
export default function App() {
  // Core 가 진실의 원본이다. 아래 상태들은 전부 room_state 스냅샷에서 파생된다.
  const live = useLiveSession()
  const cmd = useMonoCommands()
  const { connection, catalogLoaded, history, archives, displayName, streamingAccounts } = useMono()
  const { currentTrack, queue, playing, lounges } = live

  // 홈·라운지 화면의 섹션들은 카탈로그와 이력에서 집계해 쓴다.
  const homeAlbumList = useMemo(
    () => homeAlbums(live.albums, live.catalog, history),
    [live.albums, live.catalog, history],
  )
  const homeChips = useMemo(() => filterChips(live.catalog), [live.catalog])
  const homeInsights = useMemo(() => insightCards(live.catalog, history), [live.catalog, history])
  const homeLabels = useMemo(() => labelTiles(live.catalog), [live.catalog])
  const homeLounges = useMemo(() => toLoungeCards(lounges), [lounges])
  const hofSessions = useMemo(() => hallOfFame(archives, live.catalog), [archives, live.catalog])
  const mostPlayedRows = useMemo(() => toMostPlayed(history, live.catalog), [history, live.catalog])

  const [activeTab, setActiveTab] = useState("home")
  const [currentTab, setCurrentTab] = useState<CurrentTab>("home")
  const [libraryDefaultTab, setLibraryDefaultTab] = useState<"albums" | "artists" | "composers" | "playlists" | "tracks">("albums")
  const [playerMode, setPlayerMode] = useState<PlayerMode>("solo")
  const [settingsInitialTab, setSettingsInitialTab] = useState<"audio" | "lens" | "storage" | "accounts" | "general">("audio")
  const [isFullscreenPlayer, setIsFullscreenPlayer] = useState<boolean>(false)
  const [isCreditsExpanded, setIsCreditsExpanded] = useState<boolean>(false)
  const [isCreditsView, setIsCreditsView] = useState<boolean>(false)
  const [selectedArtist, setSelectedArtist] = useState<any | null>(null)
  const [selectedAlbum, setSelectedAlbum] = useState<any | null>(null)
  const [albumPrevView, setAlbumPrevView] = useState<string | null>(null)
  const [searchQuery, setSearchQuery] = useState<string>("")
  const [isSearchActive, setIsSearchActive] = useState<boolean>(false)

  const filteredAlbums: SearchAlbum[] = live.searchAlbums(searchQuery)
  const filteredTracks: SearchTrack[] = live.searchTracks(searchQuery)

  // 타이핑이 멎은 뒤에야 Core 로 검색을 보낸다. Core 는 그 질의로 Tidal 카탈로그를
  // 보강하므로, 글자마다 보내면 곧장 요청 한도(429)에 걸린다.
  // Core 쪽에도 같은 질의·짧은 질의·한도 상태를 거르는 방어가 따로 있다.
  const debouncedQuery = useDebounced(searchQuery, 500)
  useEffect(() => {
    const q = debouncedQuery.trim()
    if (q.length < 3) return
    void cmd.search(q)
  }, [debouncedQuery, cmd])

  // 검색어에 맞는 아티스트 한 명과 라운지들. 없으면 해당 섹션을 통째로 감춘다.
  const searchQ = searchQuery.toLowerCase().trim()
  const topArtist = useMemo(() => {
    if (!searchQ) return null
    const hit = libArtists(live.catalog).find(a => a.name.toLowerCase().includes(searchQ))
    if (!hit) return null
    const genres = [...new Set(
      live.catalog.filter(t => (t.artistId || t.artist) === hit.id).flatMap(t => t.genres),
    )].slice(0, 4)
    return { ...hit, genres }
  }, [searchQ, live.catalog])

  const matchedLounges = useMemo(
    () => (searchQ ? lounges.filter(r =>
      r.title.toLowerCase().includes(searchQ) || r.hostName.toLowerCase().includes(searchQ),
    ) : []),
    [searchQ, lounges],
  )

  /** 아티스트 상세로. Core 의 graph 질의로 앨범·연관 아티스트까지 가져온다. */
  const openArtist = (artistId: string) => {
    setIsFullscreenPlayer(false)
    void cmd.artistGraph(artistId).then(graph => {
      if (!graph?.artist) return
      setSelectedArtist({
        id: graph.artist.id,
        name: graph.artist.name,
        wikiSummary: graph.artist.bio ?? "",
        albums: graph.albums ?? [],
        related: graph.related ?? [],
      })
    })
  }

  // Dynamic album navigation — 카탈로그에서 제목으로 찾고, 없으면 현재 곡 메타로 채운다
  const navigateToAlbum = (albumTitle?: string, source?: CurrentTab | "artist") => {
    setIsFullscreenPlayer(false)
    setAlbumPrevView(source ?? (selectedArtist ? "artist" : currentTab))
    const title = albumTitle ?? currentTrack?.album ?? ""
    const found = live.findAlbum(title)
    if (found) {
      setSelectedAlbum(found)
      return
    }
    // Fallback: construct a minimal album object from currentTrack
    setSelectedAlbum({
      title,
      artist: currentTrack?.artist ?? "",
      year: "",
      label: "",
      studio: "",
      art: currentTrack?.art ?? (currentTrack as any)?.coverUrl ?? "",
      coverUrl: currentTrack?.art ?? (currentTrack as any)?.coverUrl ?? "",
      format: currentTrack?.format ?? "Hi-Res FLAC",
      dr: currentTrack?.dr ?? "",
      wikiUrl: "",
      wikiSummary: "",
      tracks: [],
    })
  }

  const handleSelectAlbum = (album: SearchAlbum) => navigateToAlbum(album.title, currentTab)

  const handleSelectAlbumAny = (album: any) => {
    const match = live.findAlbum(album.title)
    if (match) { navigateToAlbum(match.title, currentTab); return }
    navigateToAlbum(album.title, currentTab)
  }

  // Kept for backward-compat call sites
  const handleSelectSomethinElse = () => navigateToAlbum("Somethin' Else")

  // Navigation history (TIDAL-style back/forward)
  type NavSnapshot = { currentTab: CurrentTab; activeTab: string; isFullscreenPlayer: boolean }
  const [navHistory, setNavHistory] = useState<NavSnapshot[]>([{ currentTab: "home", activeTab: "home", isFullscreenPlayer: false }])
  const [historyIndex, setHistoryIndex] = useState<number>(0)
  const isNavigatingRef = useRef(false)

  const pushNav = useCallback((snapshot: NavSnapshot) => {
    if (isNavigatingRef.current) return
    setNavHistory(prev => {
      const truncated = prev.slice(0, historyIndex + 1)
      return [...truncated, snapshot]
    })
    setHistoryIndex(prev => prev + 1)
  }, [historyIndex])

  const handleGoBack = useCallback(() => {
    if (selectedAlbum) { setSelectedAlbum(null); return }
    if (selectedArtist) { setSelectedArtist(null); return }
    if (historyIndex === 0) return
    const target = navHistory[historyIndex - 1]
    isNavigatingRef.current = true
    setCurrentTab(target.currentTab)
    setActiveTab(target.activeTab)
    setIsFullscreenPlayer(target.isFullscreenPlayer)
    setHistoryIndex(prev => prev - 1)
    setTimeout(() => { isNavigatingRef.current = false }, 0)
  }, [historyIndex, navHistory, selectedArtist, selectedAlbum])

  const handleGoForward = useCallback(() => {
    if (historyIndex >= navHistory.length - 1) return
    const target = navHistory[historyIndex + 1]
    isNavigatingRef.current = true
    setCurrentTab(target.currentTab)
    setActiveTab(target.activeTab)
    setIsFullscreenPlayer(target.isFullscreenPlayer)
    setHistoryIndex(prev => prev + 1)
    setTimeout(() => { isNavigatingRef.current = false }, 0)
  }, [historyIndex, navHistory])
  const [isQueueOpen, setIsQueueOpen] = useState<boolean>(false)
  // playing · currentTrack · queue 는 Core 의 room_state 에서 온다(useLiveSession).
  const setPlaying = (next: boolean) => (next ? cmd.play() : cmd.pause())
  const [dspPreset, setDspPreset] = useState<"harman" | "flat" | "warm">("harman")
  const [crossfeedOn, setCrossfeedOn] = useState<boolean>(true)

  // ── Dark mode: persist + toggle ──────────────────────────────────────────
  const [theme, setTheme] = useState<"light" | "dark">(
    () => (localStorage.getItem("mono_theme") as "light" | "dark") || "dark"
  )
  useEffect(() => {
    const root = document.documentElement
    if (theme === "dark") {
      root.classList.add("dark")
    } else {
      root.classList.remove("dark")
    }
    localStorage.setItem("mono_theme", theme)
  }, [theme])
  const handleToggleTheme = () => setTheme(prev => (prev === "dark" ? "light" : "dark"))

  // ESC to close fullscreen
  useEffect(() => {
    const handler = (e: KeyboardEvent) => { if (e.key === "Escape") setIsFullscreenPlayer(false) }
    window.addEventListener("keydown", handler)
    return () => window.removeEventListener("keydown", handler)
  }, [])

  const [activeLoungeRoom, setActiveLoungeRoom] = useState<boolean>(false)
  const [joinedLoungeId, setJoinedLoungeId] = useState<string | null>(null)
  const [hostSessionPrivacy, setHostSessionPrivacy] = useState<"public" | "unlisted">("public")
  const [hostSessionSecretKey, setHostSessionSecretKey] = useState<string | undefined>(undefined)
  const [pendingLoungeJoin, setPendingLoungeJoin] = useState<{ id: string; key?: string } | null>(null)
  const [showAccountGateModal, setShowAccountGateModal] = useState<boolean>(false)
  const [authServiceForGate, setAuthServiceForGate] = useState<"qobuz" | "tidal" | null>(null)
  const { message: toastMessage, show: showToast } = useToast()

  // Deep-link handler: parse ?lounge=<id> on mount
  useEffect(() => {
    try {
      const params = new URLSearchParams(window.location.search)
      const targetLoungeId = params.get("lounge")
      const targetKey = params.get("key") ?? undefined

      if (targetLoungeId) {
        const targetRoom = lounges.find(r => r.id === targetLoungeId)
        window.history.replaceState({}, document.title, window.location.pathname)
        if (targetRoom) {
          // Unlisted rooms require the correct secret key in the URL
          if (targetRoom.visibility === "unlisted" && targetRoom.secretKey !== targetKey) return

          // Gate 1: must be running inside the installed Mono desktop client
          const isInstalledClient = typeof (window as Window & { __monoDesktopClient?: boolean }).__monoDesktopClient === "boolean"
            && (window as Window & { __monoDesktopClient?: boolean }).__monoDesktopClient === true
          if (!isInstalledClient) {
            setClientGateLoungeId(targetLoungeId)
            return
          }

          // Gate 2: must have at least one active streaming service
          const hasActiveService = connectedServices.qobuz || connectedServices.tidal
          if (!hasActiveService) {
            setPendingLoungeJoin({ id: targetLoungeId, key: targetKey })
            setShowAccountGateModal(true)
            return
          }

          executeJoinLounge(targetLoungeId, targetKey)
        }
      }
    } catch (err) {
      console.warn("Deep link parse error:", err)
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const [isGearWallExpanded, setIsGearWallExpanded] = useState<boolean>(false)
  const [chatInput, setChatInput] = useState<string>("")
  const [cueSearchQuery, setCueSearchQuery] = useState<string>("")
  const [floatingReactions, setFloatingReactions] = useState<{ id: number; emoji: string; left: number; rotate: number; scale: number }[]>([])
  const [showReactions, setShowReactions] = useState<boolean>(false)

  const triggerReaction = (emoji: string) => {
    const id = Date.now() + Math.random()
    const randomLeft = Math.floor(Math.random() * 76) + 12
    const randomRotate = Math.floor(Math.random() * 30) - 15
    const randomScale = Number((0.9 + Math.random() * 0.35).toFixed(2))
    setFloatingReactions(prev => [...prev, { id, emoji, left: randomLeft, rotate: randomRotate, scale: randomScale }])
    setTimeout(() => {
      setFloatingReactions(prev => prev.filter(r => r.id !== id))
    }, 850)
  }

  // Shared helper: commit a room join after all guards have passed.
  // 실제 입장은 Core 가 처리하고, 곡·큐·멤버는 room_state 로 따라온다.
  const commitJoinRoom = useCallback((room: LoungeRoom) => {
    void cmd.joinRoom(room.id, room.secretKey)
    setJoinedLoungeId(room.id)
    setPlayerMode("guest")
    setActiveLoungeRoom(true)
  }, [cmd])

  // Guard: if currently hosting, ask before tearing down the broadcast
  const guardHostConflict = useCallback((onConfirmed: () => void): boolean => {
    if (playerMode !== "host") { onConfirmed(); return true }
    const ok = window.confirm(
      "Active Broadcast in Progress\n\nYou are currently hosting a Live Lounge. Joining another lounge will terminate your broadcast.\n\nDo you want to proceed?"
    )
    if (!ok) return false
    // Inline stop-hosting (modal not open during join, so no need to reset it)
    setPlayerMode("solo")
    setActiveLoungeRoom(false)
    setIsFullscreenPlayer(false)
    onConfirmed()
    return true
  }, [playerMode])

  const joinLounge = (roomId?: string) => {
    const room = roomId ? lounges.find(r => r.id === roomId) : lounges[0]
    if (!room) { showToast("지금 열려 있는 라운지가 없습니다"); return }
    guardHostConflict(() => {
      commitJoinRoom(room)
      setCurrentTab("lounges")
      setActiveTab("live")
    })
  }

  const executeJoinLounge = useCallback((roomId: string, key?: string) => {
    const room = lounges.find(r => r.id === roomId)
    if (!room) return
    if (room.visibility === "unlisted" && room.secretKey !== key) return
    commitJoinRoom(room)
    setCurrentTab("lounges")
    setActiveTab("live")
    setPendingLoungeJoin(null)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [commitJoinRoom, lounges])

  // Play handlers: 전부 Core 명령으로 나간다. 결과는 room_state 로 돌아온다.
  // 디자인 컴포넌트들은 곡 id 없이 제목만 넘겨 주므로 카탈로그에서 되찾는다.
  const resolveTrackId = (track: TrackActionTarget): string | null =>
    (track as { id?: string }).id ?? live.findTrackId(track.title, track.artist)

  const handlePlayNow = (track: TrackActionTarget) => {
    const id = resolveTrackId(track)
    if (!id) { showToast(`"${track.title}" 을(를) 라이브러리에서 찾지 못했습니다`); return }
    void cmd.playNow(id)
  }

  const handlePlayNext = (track: TrackActionTarget) => {
    const id = resolveTrackId(track)
    if (!id) { showToast(`"${track.title}" 을(를) 라이브러리에서 찾지 못했습니다`); return }
    // Core 에는 "다음에 재생"이 따로 없다. 큐 끝에 넣고 현재 위치 바로 뒤로 끌어올린다.
    cmd.enqueue(id)
    const target = (live.room?.queueIndex ?? -1) + 1
    const from = live.room?.queue.length ?? 0
    if (from > target) cmd.moveInQueue(from, target - from)
    showToast(`"${track.title}" 을(를) 다음에 재생합니다`)
  }

  const handleAddToQueue = (track: TrackActionTarget) => {
    const id = resolveTrackId(track)
    if (!id) { showToast(`"${track.title}" 을(를) 라이브러리에서 찾지 못했습니다`); return }
    cmd.enqueue(id)
    showToast(`"${track.title}" 을(를) 큐에 추가했습니다`)
  }

  // 드로어는 현재 곡 이후만 보여 준다. Core 의 큐 인덱스로 되돌린다.
  const toQueueIndex = (drawerIndex: number) => queue[drawerIndex]?.queueIndex ?? drawerIndex

  const handleRemoveFromQueue = (index: number) => {
    cmd.removeFromQueue(toQueueIndex(index))
  }

  const handleReorderQueue = (fromIndex: number, toIndex: number) => {
    const from = toQueueIndex(fromIndex)
    const to = toQueueIndex(toIndex)
    if (from === to) return
    cmd.moveInQueue(from, to - from)
  }

  const handlePlayTrackNow = (_track: QueueTrack, index: number) => {
    cmd.jumpTo(toQueueIndex(index))
  }

  const handleLeaveLounge = () => {
    cmd.leaveRoom()
    setPlayerMode("solo")
    setActiveLoungeRoom(false)
    setJoinedLoungeId(null)
    setCurrentTab("lounges")
    setActiveTab("live")
  }

  const handleGoToLibraryTracks = () => {
    setLibraryDefaultTab("tracks")
    setCurrentTab("library")
  }

  const handleToggleJoinLounge = (roomId: string, e?: React.MouseEvent) => {
    if (e) e.stopPropagation()
    const resolvedId = roomId
    if (joinedLoungeId === resolvedId) {
      handleLeaveLounge()
    } else {
      const room = lounges.find(r => r.id === resolvedId)
      if (!room) return
      guardHostConflict(() => {
        commitJoinRoom(room)
        setCurrentTab("lounges")
        setActiveTab("live")
      })
    }
  }

  const handleStopHosting = () => {
    cmd.leaveRoom()
    setPlayerMode("solo")
    setActiveLoungeRoom(false)
    setCurrentTab("lounges")
    setActiveTab("live")
    setIsFullscreenPlayer(false)
    setShowEndSessionModal(false)
  }

  const [showEndSessionModal, setShowEndSessionModal] = useState<boolean>(false)
  const [clientGateLoungeId, setClientGateLoungeId] = useState<string | null>(null)

  const handleExitAttempt = () => {
    if (playerMode === "host") {
      setShowEndSessionModal(true)
    } else {
      handleLeaveLounge()
    }
  }

  const handleReturnToLounge = () => {
    setIsFullscreenPlayer(false)
    setSelectedAlbum(null)
    setSelectedArtist(null)
    setActiveLoungeRoom(true)
    setCurrentTab("lounges")
    setActiveTab("live")
  }

  const handleStartLounge = (opts?: HostLaunchOpts) => {
    const visibility = opts?.visibility ?? "public"
    setHostSessionPrivacy(visibility)
    setHostSessionSecretKey(opts?.secretKey)
    void cmd
      .createRoom(
        opts?.title?.trim() || `${activeProfile?.name ?? "Mono"} Live`,
        visibility === "unlisted" ? RoomMode.Invite : RoomMode.Open,
      )
      .then(id => {
        if (!id) { showToast("라운지를 열지 못했습니다"); return }
        setJoinedLoungeId(id)
        setPlayerMode("host")
        handleReturnToLounge()
      })
  }

  const handleOpenPlayerOrLounge = () => {
    // Never open the fullscreen player during any lounge session — always route back to the room.
    if (playerMode === "host" || playerMode === "guest" || activeLoungeRoom) {
      setIsFullscreenPlayer(false)
      setActiveLoungeRoom(true)
      setCurrentTab("lounges")
      setActiveTab("live")
    } else {
      setIsFullscreenPlayer(true)
    }
  }

  const navigate = useCallback((tab: CurrentTab, navActiveTab: string, fullscreen = false) => {
    setSelectedArtist(null)
    setSelectedAlbum(null)
    setIsSearchActive(false)
    setCurrentTab(tab)
    setActiveTab(navActiveTab)
    setIsFullscreenPlayer(fullscreen)
    pushNav({ currentTab: tab, activeTab: navActiveTab, isFullscreenPlayer: fullscreen })
  }, [pushNav])

  const navigateToLens = () => {
    setSettingsInitialTab("lens")
    navigate("settings", activeTab)
    setIsFullscreenPlayer(false)
  }

  /** 지금 곡의 아티스트 상세로. 곡이 없으면 아무 일도 하지 않는다. */
  const handleOpenCurrentArtist = (e?: React.MouseEvent) => {
    if (e) e.stopPropagation()
    const artistId = live.room?.currentTrack?.artistId
    if (!artistId) { showToast("재생 중인 곡이 없습니다"); return }
    openArtist(artistId)
  }


  const navigateToAccounts = () => {
    setSettingsInitialTab("accounts")
    navigate("settings", activeTab)
  }

  // ── Profile system state ───────────────────────────────────────────────────
  const [shareModalData, setShareModalData] = useState<ShareData | null>(null)
  const handleOpenShare = (data: ShareData) => setShareModalData(data)

  // 연동 여부는 Core 가 안다 — localStorage 사본을 두면 재설치·다른 기기에서 어긋난다.
  const connectedServices = useMemo(() => ({
    qobuz: streamingAccounts.some(a => a.provider === StreamingProvider.Qobuz && a.connected),
    tidal: streamingAccounts.some(a => a.provider === StreamingProvider.Tidal && a.connected),
  }), [streamingAccounts])

  function handleToggleService(service: "qobuz" | "tidal") {
    const provider = service === "qobuz" ? StreamingProvider.Qobuz : StreamingProvider.Tidal
    if (connectedServices[service]) {
      void cmd.unlinkStreaming(provider)
      showToast(`${service === "qobuz" ? "Qobuz" : "TIDAL"} 연결을 해제했습니다`)
      return
    }
    setAuthServiceForGate(service)
  }

  const [profiles, setProfiles] = useState<Profile[]>(INITIAL_PROFILES)
  const [activeProfileId, setActiveProfileId] = useState<string>("1")
  const [showProfileMenu, setShowProfileMenu] = useState<boolean>(false)
  const [showSettingsModal, setShowSettingsModal] = useState<boolean>(false)
  const [settingsActiveTab, setSettingsActiveTab] = useState<"profiles" | "engine" | "lens">("profiles")
  const handleOpenLensSettings = () => { setSettingsActiveTab("lens"); setShowSettingsModal(true) }

  const activeProfile = profiles.find(p => p.id === activeProfileId) ?? profiles[0]

  // ── Onboarding state ───────────────────────────────────────────────────────
  const [showOnboarding, setShowOnboarding] = useState<boolean>(
    () => localStorage.getItem("mono_onboarding_completed") !== "true"
  )

  const handleOnboardingComplete = (profile: ListenerProfile) => {
    localStorage.setItem("mono_onboarding_completed", "true")
    // 이름은 Core 의 멤버 목록·아카이브 참가자에 그대로 쓰인다.
    cmd.setDisplayName(profile.displayName)
    setProfiles(prev =>
      prev.map(p =>
        p.id === activeProfileId ? { ...p, name: profile.displayName } : p
      )
    )
    setShowOnboarding(false)
    setCurrentTab("home")
    setActiveTab("home")
    showToast("Mono 오디오 엔진을 초기화했습니다.")
  }

  const handleSwitchProfile = (id: string) => {
    const isOnAir = playerMode === "host"
    if ((isOnAir || activeLoungeRoom) && id !== activeProfileId) {
      if (!window.confirm("Switching profiles will leave your current live session. Continue?")) return
      handleLeaveLounge()
    }
    setActiveProfileId(id)
    setShowProfileMenu(false)
  }

  const handleAddProfile = (name: string, gear: string) => {
    const words = name.split(" ")
    const tag = words.length >= 2
      ? (words[0][0] + words[words.length - 1][0]).toUpperCase()
      : name.slice(0, 2).toUpperCase()
    const id = String(Date.now())
    setProfiles(prev => [...prev, { id, name, tag, avatarText: tag, gearPreset: gear || "Default Setup", isPrimary: false }])
  }

  return (
    <div style={{ height: "100%", display: "flex", flexDirection: "column", background: "var(--chassis-bg)", color: "var(--text-primary)", minWidth: 1024 }}>
      <TopNav
        activeTab={activeTab}
        setActiveTab={setActiveTab}
        currentTab={currentTab}
        setCurrentTab={(tab) => {
          const tabToActive: Record<CurrentTab, string> = { home: "home", lounges: "live", library: "library", explore: "explore", settings: activeTab }
          if (tab === "lounges" && activeLoungeRoom && playerMode !== "host") {
            handleLeaveLounge()
            return
          }
          navigate(tab, tabToActive[tab] ?? activeTab)
        }}
        onGoHome={() => navigate("home", "home")}
        connectedServices={connectedServices}
        onNavigateToAccounts={navigateToAccounts}
        onGoBack={handleGoBack}
        onGoForward={handleGoForward}
        canGoBack={historyIndex > 0}
        canGoForward={historyIndex < navHistory.length - 1}
        searchQuery={searchQuery}
        setSearchQuery={setSearchQuery}
        setIsSearchActive={setIsSearchActive}
        onOpenMilesDavis={handleOpenCurrentArtist}
        onSelectSomethinElse={handleSelectSomethinElse}
        onReturnToLounge={handleReturnToLounge}
        onNavigateToSettings={() => navigate("settings", activeTab)}
        hasActiveSession={activeLoungeRoom || playerMode === "host"}
        filteredAlbums={filteredAlbums}
        filteredTracks={filteredTracks}
        onSelectAlbum={handleSelectAlbum}
        profiles={profiles}
        activeProfileId={activeProfileId}
        activeProfile={activeProfile}
        showProfileMenu={showProfileMenu}
        setShowProfileMenu={setShowProfileMenu}
        onSwitchProfile={handleSwitchProfile}
        onOpenProfileSettings={() => { setSettingsActiveTab("profiles"); setShowSettingsModal(true) }}
        theme={theme}
        onToggleTheme={handleToggleTheme}
        matchedLounges={matchedLounges}
        onJoinLounge={(roomId) => handleToggleJoinLounge(roomId)}
      />

      <main style={{ flex: 1, overflowY: "auto", paddingBottom: 96 }}>
        {isSearchActive && !selectedAlbum && !selectedArtist ? (
          /* ── Search Results View ──────────────────────────────────────────── */
          <div className="max-w-6xl mx-auto px-6 py-8 space-y-8">

            {/* Search header */}
            <div className="flex items-center justify-between">
              <div>
                <h1 className="text-xl font-serif font-bold text-slate-900">Results for &ldquo;{searchQuery}&rdquo;</h1>
                <p className="text-xs font-mono text-slate-400 mt-0.5">{filteredTracks.length} tracks · {filteredAlbums.length} albums · {matchedLounges.length} live lounge{matchedLounges.length !== 1 ? "s" : ""} · {topArtist ? 1 : 0} artist</p>
              </div>
              <button
                onClick={() => { setSearchQuery(""); setIsSearchActive(false) }}
                className="text-xs font-mono text-slate-500 hover:text-slate-900 border border-slate-200 hover:border-slate-400 px-3.5 py-1.5 rounded-full cursor-pointer transition inline-flex items-center gap-1.5"
                style={{ background: "none" }}
              >
                <MonoIcon.Close size={12} />
                <span>Clear &amp; Close</span>
              </button>
            </div>

            {/* Top Result — 검색어와 가장 잘 맞는 아티스트 */}
            {topArtist && (
              <div>
                <p className="text-xs font-mono font-bold text-slate-400 uppercase tracking-wider mb-3">Top Result</p>
                <div
                  onClick={() => { setIsSearchActive(false); setSearchQuery(""); openArtist(topArtist.id) }}
                  className="flex items-center gap-6 bg-gradient-to-r from-slate-50 to-white border border-slate-200/80 rounded-2xl p-5 cursor-pointer hover:shadow-md transition group"
                >
                  {topArtist.art ? (
                    <img
                      src={topArtist.art}
                      alt={topArtist.name}
                      className="w-20 h-20 rounded-xl object-cover shadow-md border border-slate-200 shrink-0"
                    />
                  ) : (
                    <div className="w-20 h-20 rounded-xl shadow-md border border-slate-200 shrink-0 bg-slate-200" />
                  )}
                  <div className="flex-1 min-w-0">
                    <p className="text-2xl font-serif font-bold text-slate-900 group-hover:text-blue-700 transition leading-tight">{topArtist.name}</p>
                    <p className="text-xs font-mono text-slate-500 mt-0.5 uppercase tracking-wider">{topArtist.count}</p>
                    <div className="flex items-center gap-2 mt-2 flex-wrap">
                      {topArtist.genres.map(g => (
                        <span key={g} className="text-[10px] font-mono bg-slate-100 text-slate-600 px-2.5 py-0.5 rounded-full border border-slate-200/60">{g}</span>
                      ))}
                    </div>
                  </div>
                  <span className="text-xs font-mono text-blue-600 group-hover:text-blue-800 transition shrink-0">View Liner Notes →</span>
                </div>
              </div>
            )}

            {/* 2-column split: Tracks + Albums */}
            {filteredTracks.length === 0 && filteredAlbums.length === 0 ? (
              <div className="py-16 text-center">
                <p className="text-base font-serif text-slate-500">No results found for &ldquo;{searchQuery}&rdquo;</p>
                <p className="text-xs font-mono text-slate-400 mt-2">앨범·아티스트·곡 제목으로 라이브러리를 검색합니다.</p>
              </div>
            ) : (
              <div className="grid grid-cols-2 gap-8">

                {/* Left: Hi-Res Tracks */}
                <div>
                  <p className="text-xs font-mono font-bold text-slate-400 uppercase tracking-wider mb-3">Hi-Res Tracks ({filteredTracks.length})</p>
                  {filteredTracks.length === 0 ? (
                    <p className="text-xs font-mono text-slate-400 px-4">No matching tracks.</p>
                  ) : (
                    <div className="flex flex-col gap-1">
                      {filteredTracks.map((track, i) => (
                        <div
                          key={`${track.title}-${track.album}`}
                          className={`group relative flex items-center gap-3 px-4 py-3 rounded-xl transition cursor-pointer ${track.isCurrent ? "bg-blue-50/70 border border-blue-200/60" : "hover:bg-slate-50 border border-transparent"}`}
                        >
                          <div className="w-6 shrink-0 flex justify-center">
                            {track.isCurrent ? (
                              <span className="flex items-center gap-1 text-blue-600">
                                <span className="w-1.5 h-1.5 rounded-full bg-blue-600 animate-pulse inline-block" />
                                <MonoIcon.PlayMini size={10} color="#2563eb" />
                              </span>
                            ) : (
                              <span className="text-xs font-mono text-slate-400">{String(i + 1).padStart(2, "0")}</span>
                            )}
                          </div>
                          <div className="flex-1 min-w-0">
                            <p className={`text-sm font-serif font-semibold truncate ${track.isCurrent ? "text-blue-700" : "text-slate-900"}`}>{track.title}</p>
                            <p className="text-xs font-mono text-slate-400 truncate">{track.album}</p>
                          </div>
                          <span className={`text-[10px] font-mono border px-2 py-0.5 rounded-full shrink-0 ${track.dr === "DR 14" ? "bg-emerald-50 text-emerald-700 border-emerald-200" : track.dr === "DR 13" ? "bg-amber-50 text-amber-700 border-amber-200" : "bg-slate-100 text-slate-500 border-slate-200"}`}>{track.dr}</span>
                          <span className="text-xs font-mono text-slate-400 shrink-0">{track.duration}</span>
                          <TrackActionMenu
                            track={{ title: track.title, artist: track.artist, album: track.album, duration: track.duration, dr: track.dr }}
                            onPlayNow={handlePlayNow}
                            onPlayNext={handlePlayNext}
                            onAddToQueue={handleAddToQueue}
                            isGuest={playerMode === "guest"}
                          />
                        </div>
                      ))}
                    </div>
                  )}
                </div>

                {/* Right: Master Albums */}
                <div>
                  <p className="text-xs font-mono font-bold text-slate-400 uppercase tracking-wider mb-3">Master Albums ({filteredAlbums.length})</p>
                  {filteredAlbums.length === 0 ? (
                    <p className="text-xs font-mono text-slate-400">No matching albums.</p>
                  ) : (
                    <div className="grid grid-cols-2 gap-4">
                      {filteredAlbums.map(album => (
                        <div
                          key={album.title}
                          onClick={() => { setIsSearchActive(false); setSearchQuery(""); handleSelectAlbum(album) }}
                          className="cursor-pointer group"
                        >
                          <div className="rounded-xl overflow-hidden mb-2 border border-slate-100 shadow-sm aspect-square">
                            <img src={album.coverUrl} alt={album.title} className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300" />
                          </div>
                          <p className="text-sm font-serif font-bold text-slate-900 leading-snug group-hover:text-blue-700 transition">{album.title}</p>
                          <p className="text-xs font-mono text-slate-400">{album.artist} · {album.year}</p>
                          <div className="flex items-center gap-1.5 mt-1 flex-wrap">
                            <span className={`text-[10px] font-mono border px-2 py-0.5 rounded-full ${album.format.includes("DSD") ? "bg-blue-50 text-blue-700 border-blue-200" : "bg-purple-50 text-purple-700 border-purple-200"}`}>{album.format.split(" ").slice(0, 3).join(" ")}</span>
                            <span className={`text-[10px] font-mono border px-2 py-0.5 rounded-full ${album.dr === "DR 14" ? "bg-emerald-50 text-emerald-700 border-emerald-200" : "bg-amber-50 text-amber-700 border-amber-200"}`}>{album.dr}</span>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            )}

            {/* Live Lounges — 검색어와 맞는 실제 룸 */}
            {matchedLounges.length > 0 && (
              <div>
                <p className="text-xs font-mono font-bold text-slate-400 uppercase tracking-wider mb-3">Live Lounges</p>
                <div className="flex flex-col gap-3">
                  {matchedLounges.map(room => (
                    <div
                      key={room.id}
                      onClick={() => { setIsSearchActive(false); setSearchQuery(""); handleToggleJoinLounge(room.id) }}
                      className="flex items-center gap-5 bg-slate-900 hover:bg-slate-800 rounded-2xl px-6 py-5 cursor-pointer transition group"
                    >
                      {room.art ? (
                        <img src={room.art} alt={room.title} className="w-14 h-14 rounded-xl object-cover border border-white/10 shrink-0" />
                      ) : (
                        <div className="w-14 h-14 rounded-xl border border-white/10 shrink-0 bg-white/5" />
                      )}
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 mb-0.5">
                          <span className="w-2 h-2 rounded-full bg-red-500 animate-pulse inline-block shrink-0" />
                          <span className="text-xs font-mono font-bold text-red-400 uppercase tracking-wider">Live Now</span>
                        </div>
                        <p className="text-base font-serif font-bold text-white leading-snug">{room.title}</p>
                        <p className="text-xs font-mono text-slate-400 mt-0.5">
                          {[`${room.listenerCount} Listening`, room.currentArtist ? `${room.currentArtist} — ${room.currentTrackTitle}` : room.currentTrackTitle, room.audioSpec].filter(Boolean).join(" · ")}
                        </p>
                      </div>
                      <span className="text-xs font-mono text-blue-400 group-hover:text-blue-300 transition shrink-0">
                        {joinedLoungeId === room.id ? "Leave Lounge →" : "Join Lounge →"}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            )}

          </div>
        ) : selectedAlbum ? (
          <AlbumDetailView
            selectedAlbum={selectedAlbum}
            currentTrack={currentTrack}
            activeLoungeRoom={activeLoungeRoom}
            playerMode={playerMode}
            onReturnToLounge={handleReturnToLounge}
            onBack={() => {
              setSelectedAlbum(null)
              if (albumPrevView === "artist") {
                // stay on artist detail (selectedArtist still set)
              } else {
                setSelectedArtist(null)
                if (albumPrevView && albumPrevView !== "artist") setCurrentTab(albumPrevView as CurrentTab)
              }
            }}
            onPlayNow={handlePlayNow}
            onPlayNext={handlePlayNext}
            onAddToQueue={handleAddToQueue}
          />
        ) : selectedArtist ? (
          <ArtistDetailView
            selectedArtist={selectedArtist}
            activeLoungeRoom={activeLoungeRoom}
            playerMode={playerMode}
            onReturnToLounge={handleReturnToLounge}
            onBack={() => setSelectedArtist(null)}
            onSelectSomethinElse={handleSelectSomethinElse}
            onSelectAlbum={(title) => navigateToAlbum(title, "artist")}
          />
        ) : currentTab === "lounges" ? (
          activeLoungeRoom ? (
            <LiveLoungeRoom
              currentTrack={currentTrack}
              playerMode={playerMode}
              joinedLoungeId={joinedLoungeId}
              hostSessionPrivacy={hostSessionPrivacy}
              hostSessionSecretKey={hostSessionSecretKey}
              cueSearchQuery={cueSearchQuery}
              setCueSearchQuery={setCueSearchQuery}
              onCueTrack={() => setIsFullscreenPlayer(false)}
              rooms={lounges}
              chatInput={chatInput}
              setChatInput={setChatInput}
              showReactions={showReactions}
              setShowReactions={setShowReactions}
              floatingReactions={floatingReactions}
              triggerReaction={triggerReaction}
              isGearWallExpanded={isGearWallExpanded}
              setIsGearWallExpanded={setIsGearWallExpanded}
              onExitAttempt={handleExitAttempt}
              onOpenShare={handleOpenShare}
              theme={theme}
            />
          ) : (
            <LiveLoungePage
              onJoin={joinLounge}
              onGoLive={(opts) => handleStartLounge(opts)}
              onOpenShare={handleOpenShare}
              rooms={lounges}
              archives={hofSessions}
              mostPlayed={mostPlayedRows}
              onLoadArchive={(archiveId) => {
                void cmd.request({ type: "create_playlist", archiveId }, "create_playlist")
                  .then(() => { void cmd.refreshPlaylists(); showToast("세션을 플레이리스트로 저장했습니다") })
                  .catch(() => showToast("세션을 불러오지 못했습니다"))
              }}
            />
          )
        ) : currentTab === "library" ? (
          <MyLibraryPage onPlayNow={handlePlayNow} onPlayNext={handlePlayNext} onAddToQueue={handleAddToQueue} defaultTab={libraryDefaultTab} onOpenShare={handleOpenShare} onSelectAlbum={handleSelectAlbumAny} onSelectArtist={(artist) => setSelectedArtist(artist)} />
        ) : currentTab === "explore" ? (
          <ExplorePage onPlayNow={handlePlayNow} onPlayNext={handlePlayNext} onAddToQueue={handleAddToQueue} onSelectAlbum={handleSelectAlbumAny} connectedServices={connectedServices} onConnectService={(service) => { setAuthServiceForGate(service); }} />
        ) : currentTab === "settings" ? (
          <SettingsPage initialTab={settingsInitialTab} connectedServices={connectedServices} onToggleService={handleToggleService} theme={theme} onToggleTheme={handleToggleTheme} />
        ) : (
          <HomePage
            displayName={displayName}
            loungeCards={homeLounges}
            archiveCount={archives.length}
            albums={homeAlbumList}
            chips={homeChips}
            insights={homeInsights}
            labels={homeLabels}
            joinedLoungeId={joinedLoungeId}
            onToggleJoin={handleToggleJoinLounge}
            onNavigateToTracks={handleGoToLibraryTracks}
            onPlayAlbum={(album) => { void cmd.playAlbum(album.trackIds) }}
            onSelectAlbum={(album) => {
              const match = live.findAlbum(album.title)
              if (match) handleSelectAlbum(match)
            }}
          />
        )}
      </main>

      {/* End Session Confirmation Modal */}
      {showEndSessionModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4" style={{ backdropFilter: "blur(2px)" }}>
          <div className="w-full max-w-sm rounded-2xl p-6 shadow-2xl" style={{ background: "var(--surface-card)", border: "1px solid var(--border-subtle)" }}>
            <span className="text-xs font-mono font-bold text-red-600 bg-red-50 border border-red-200 px-2 py-0.5 rounded-full inline-block mb-3">SESSION TERMINATION</span>
            <div className="text-base font-bold mb-1.5" style={{ color: "var(--text-primary)" }}>End Live Lounge Session?</div>
            <p className="text-xs leading-relaxed mt-1.5 mb-5" style={{ color: "var(--text-secondary)" }}>Ending this broadcast will terminate the bit-perfect audio stream and close the live lounge for all 42 connected listeners.</p>
            <div className="flex items-center justify-end gap-2">
              <button
                onClick={() => setShowEndSessionModal(false)}
                className="text-xs px-4 py-2 rounded-lg transition cursor-pointer"
                style={{ background: "none", border: "none", color: "var(--text-secondary)" }}
              >
                Keep Session
              </button>
              <button
                onClick={handleStopHosting}
                className="text-xs font-mono font-semibold text-white bg-red-600 hover:bg-red-700 px-4 py-2 rounded-lg shadow-sm transition cursor-pointer"
                style={{ border: "none" }}
              >
                End Session
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Fullscreen Now Playing */}
      <FullscreenPlayer
        isOpen={isFullscreenPlayer}
        onClose={() => setIsFullscreenPlayer(false)}
        currentTrack={currentTrack}
        onOpenArtist={handleOpenCurrentArtist}
        onOpenAlbum={() => navigateToAlbum(currentTrack?.album)}
        onNavigateToLensSettings={navigateToLens}
        onSelectTrack={(track) => handlePlayNow({ title: track.title, artist: track.artist, album: track.album, duration: track.duration, dr: track.dr, art: track.art })}
        isInLoungeSession={activeLoungeRoom || playerMode === "host" || playerMode === "guest"}
        onReturnToLounge={handleReturnToLounge}
      />

      <PlayerBar
        playerMode={playerMode}
        setPlayerMode={setPlayerMode}
        onNavigateToLens={navigateToLens}
        isFullscreenPlayer={isFullscreenPlayer}
        setIsFullscreenPlayer={setIsFullscreenPlayer}
        playing={playing}
        setPlaying={setPlaying}
        activeLoungeRoom={activeLoungeRoom}
        setActiveLoungeRoom={setActiveLoungeRoom}
        onLeaveLounge={handleLeaveLounge}
        onOpenPlayer={handleOpenPlayerOrLounge}
        onStartLounge={handleStartLounge}
        onStopHosting={handleStopHosting}
        onRequestStopHosting={() => setShowEndSessionModal(true)}
        onOpenArtist={handleOpenCurrentArtist}
        onOpenAlbum={() => navigateToAlbum(currentTrack?.album)}
        onReturnToLounge={handleReturnToLounge}
        isViewingArtistOrAlbum={selectedArtist !== null || selectedAlbum !== null}
        joinedLoungeId={joinedLoungeId}
        currentTrack={currentTrack}
        onShareLounge={() => {
          if (playerMode === "host") {
            handleOpenShare({
              type: "lounge",
              id: "my-live-session",
              title: "Live Audiophile Session",
              subtitle: "Hosted by You · Live Stream",
              audioSpec: "Bit-Perfect ASIO Master Stream",
              hostGear: "Your Current Output DAC / Signal Chain",
              visibility: hostSessionPrivacy,
              secretKey: hostSessionSecretKey,
            })
            return
          }
          const room = lounges.find(r => r.id === joinedLoungeId)
          if (!room) return
          handleOpenShare({
            type: "lounge",
            id: room.id,
            title: room.title,
            subtitle: `Host: ${room.hostName} · ${room.currentArtist}`,
            audioSpec: room.audioSpec,
            hostGear: room.hostGear,
            visibility: room.visibility,
            secretKey: room.secretKey,
          })
        }}
        onToggleQueue={() => setIsQueueOpen(v => !v)}
        queueCount={queue.length}
      />

      {/* ── Queue Drawer ────────────────────────────────────────────────── */}
      <QueueDrawer
        isOpen={isQueueOpen}
        onClose={() => setIsQueueOpen(false)}
        currentTrack={currentTrack}
        queue={queue}
        onRemoveFromQueue={handleRemoveFromQueue}
        onReorderQueue={handleReorderQueue}
        onClearQueue={() => cmd.clearQueue()}
        onFlushSession={() => { cmd.pause(); cmd.clearQueue() }}
        onPlayTrackNow={handlePlayTrackNow}
        isGuest={playerMode === "guest"}
        isHost={playerMode === "host"}
        hostName={playerMode === "guest" ? (lounges.find(r => r.id === joinedLoungeId)?.hostName ?? "Host") : undefined}
      />

      {/* ── Account Required Gate ───────────────────────────────────────── */}
      <AccountRequiredGateModal
        isOpen={showAccountGateModal}
        onClose={() => { setShowAccountGateModal(false); setPendingLoungeJoin(null) }}
        onConnectService={(service) => {
          setShowAccountGateModal(false)
          setAuthServiceForGate(service)
        }}
      />

      <StreamingLoginModal
        service={authServiceForGate}
        isOpen={authServiceForGate !== null}
        onClose={() => {
          setAuthServiceForGate(null)
          if (pendingLoungeJoin) setShowAccountGateModal(true)
        }}
        onSuccessfulLogin={(service) => {
          setAuthServiceForGate(null)
          setShowAccountGateModal(false)
          if (pendingLoungeJoin) {
            const { id, key } = pendingLoungeJoin
            executeJoinLounge(id, key)
            const label = service === "qobuz" ? "Qobuz Studio" : "TIDAL Max"
            showToast(`Connected to ${label}. Joining private session…`)
          }
        }}
      />

      {/* ── Onboarding Wizard ───────────────────────────────────────────── */}
      {showOnboarding && (
        <OnboardingWizard
          onComplete={handleOnboardingComplete}
          onExit={() => setShowOnboarding(false)}
          isDark={theme === "dark"}
          onToggleTheme={handleToggleTheme}
          onServiceChange={() => { /* 연동 상태는 Core 가 푸시한다 */ }}
        />
      )}

      {/* ── Core 연결 배너 ──────────────────────────────────────────────── */}
      {connection !== "open" && (
        <div
          style={{
            position: "fixed", top: 0, left: 0, right: 0, zIndex: 600,
            background: connection === "closed" ? "rgba(185,28,28,0.95)" : "rgba(180,83,9,0.95)",
            color: "#fff", padding: "7px 16px", textAlign: "center",
            fontFamily: "'DM Mono', monospace", fontSize: 11.5, letterSpacing: "0.02em",
          }}
        >
          {connection === "closed"
            ? "Mono Core 와 연결이 끊어졌습니다. Mono 를 다시 시작하세요."
            : "Mono Core 에 연결 중…"}
        </div>
      )}

      {/* ── 라이브러리가 비었을 때 ──────────────────────────────────────── */}
      {connection === "open" && catalogLoaded && live.catalog.length === 0 && currentTab === "home" && !showOnboarding && (
        <div
          style={{
            position: "fixed", bottom: 112, left: "50%", transform: "translateX(-50%)",
            zIndex: 400, maxWidth: 520, textAlign: "center",
            background: "var(--surface-card)", border: "1px solid var(--border-subtle)",
            borderRadius: 14, padding: "18px 24px", boxShadow: "0 12px 40px rgba(0,0,0,0.35)",
          }}
        >
          <div style={{ fontSize: 14, fontWeight: 600, color: "var(--text-primary)", marginBottom: 6 }}>
            라이브러리가 비어 있습니다
          </div>
          <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11.5, color: "var(--text-secondary)", lineHeight: 1.6, marginBottom: 14 }}>
            음악 폴더를 추가하거나 스트리밍 계정을 연결하면 여기에 나타납니다.
          </div>
          <div style={{ display: "flex", gap: 10, justifyContent: "center" }}>
            <button
              onClick={() => { setSettingsInitialTab("storage"); navigate("settings", activeTab) }}
              style={{ padding: "8px 16px", borderRadius: 9, border: "none", background: "#2563EB", color: "#fff", fontSize: 12.5, fontWeight: 600, cursor: "pointer", fontFamily: "inherit" }}
            >
              음악 폴더 추가
            </button>
            <button
              onClick={navigateToAccounts}
              style={{ padding: "8px 16px", borderRadius: 9, border: "1px solid var(--border-subtle)", background: "transparent", color: "var(--text-secondary)", fontSize: 12.5, fontWeight: 600, cursor: "pointer", fontFamily: "inherit" }}
            >
              스트리밍 연결
            </button>
          </div>
        </div>
      )}

      {/* ── Toast Notification ──────────────────────────────────────────── */}
      {toastMessage && (
        <div
          style={{
            position: "fixed", bottom: 108, left: "50%", transform: "translateX(-50%)",
            zIndex: 500, pointerEvents: "none",
            background: "rgba(15,23,42,0.95)", backdropFilter: "blur(12px)",
            border: "1px solid rgba(255,255,255,0.1)",
            borderRadius: 32, padding: "10px 20px",
            display: "flex", alignItems: "center", gap: 10,
            boxShadow: "0 8px 32px rgba(0,0,0,0.4)",
            animation: "gate-fade-in 0.2s ease",
          }}
        >
          <MonoIcon.Check size={14} color="#10B981" />
          <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 12, color: "#E2E8F0", whiteSpace: "nowrap" }}>
            {toastMessage}
          </span>
        </div>
      )}

      {/* ── Mono Client Gate ────────────────────────────────────────────── */}
      {clientGateLoungeId !== null && (() => {
        const room = lounges.find(r => r.id === clientGateLoungeId)
        return (
          <div
            style={{
              position: "fixed", inset: 0, zIndex: 400,
              background: "rgba(0,0,0,0.62)", backdropFilter: "blur(6px)",
              display: "flex", alignItems: "center", justifyContent: "center",
            }}
            onClick={(e) => { if (e.target === e.currentTarget) setClientGateLoungeId(null) }}
          >
            <div
              style={{
                width: 440, background: "#FFFFFF", borderRadius: 20,
                boxShadow: "0 32px 80px rgba(0,0,0,0.28), 0 4px 20px rgba(0,0,0,0.1)",
                border: "1px solid #E2E8F0", overflow: "hidden",
              }}
              onClick={(e) => e.stopPropagation()}
            >
              {/* Dark header */}
              <div style={{ background: "#0F172A", padding: "24px 24px 20px" }}>
                <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 14 }}>
                  <div style={{ width: 36, height: 36, borderRadius: 9, background: "rgba(37,99,235,0.2)", border: "1px solid rgba(37,99,235,0.3)", display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0 }}>
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#2563EB" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      <rect x="2" y="3" width="20" height="14" rx="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/>
                    </svg>
                  </div>
                  <div>
                    <div style={{ fontSize: 15, fontWeight: 700, color: "#F1F5F9" }}>Mono Desktop Engine Required</div>
                    <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, color: "#475569", marginTop: 2 }}>Bit-Perfect ASIO · Live Lounge Access</div>
                  </div>
                </div>
                {room && (
                  <div style={{ background: "rgba(255,255,255,0.04)", border: "1px solid rgba(255,255,255,0.06)", borderRadius: 10, padding: "10px 14px", display: "flex", alignItems: "center", gap: 12 }}>
                    <img src={room.art} alt={room.title} style={{ width: 40, height: 40, borderRadius: 7, objectFit: "cover", flexShrink: 0 }} />
                    <div style={{ minWidth: 0 }}>
                      <div style={{ fontSize: 12, fontWeight: 600, color: "#E2E8F0", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{room.title}</div>
                      <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, color: "#64748B", marginTop: 2 }}>{room.audioSpec} · {room.listenerCount} listening</div>
                    </div>
                  </div>
                )}
              </div>

              {/* Body */}
              <div style={{ padding: "20px 24px 24px", display: "flex", flexDirection: "column", gap: 16 }}>
                <p style={{ fontSize: 13, color: "#475569", lineHeight: 1.65, margin: 0 }}>
                  To preserve bit-perfect fidelity and respect streaming licenses (Qobuz / TIDAL HiRes FLAC), Live Lounges require the installed Mono desktop engine.
                </p>

                {/* Actions */}
                <div style={{ display: "flex", flexDirection: "column", gap: 9 }}>
                  <button
                    onClick={() => { window.location.href = `mono://lounge?id=${clientGateLoungeId}` }}
                    style={{
                      width: "100%", padding: "12px 16px",
                      background: "#2563EB", border: "none",
                      borderRadius: 10, cursor: "pointer",
                      fontSize: 13, fontWeight: 700, color: "#FFFFFF",
                      fontFamily: "inherit",
                      display: "flex", alignItems: "center", justifyContent: "center", gap: 8,
                      transition: "background 0.15s",
                    }}
                    onMouseEnter={(e) => ((e.currentTarget as HTMLButtonElement).style.background = "#1D4ED8")}
                    onMouseLeave={(e) => ((e.currentTarget as HTMLButtonElement).style.background = "#2563EB")}
                  >
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/>
                    </svg>
                    Open in Installed App
                  </button>
                  <button
                    onClick={() => window.open("https://mono.audio/download", "_blank")}
                    style={{
                      width: "100%", padding: "11px 16px",
                      background: "#F8FAFC", border: "1px solid #E2E8F0",
                      borderRadius: 10, cursor: "pointer",
                      fontSize: 13, fontWeight: 600, color: "#374151",
                      fontFamily: "inherit",
                      display: "flex", alignItems: "center", justifyContent: "center", gap: 8,
                      transition: "border-color 0.15s",
                    }}
                    onMouseEnter={(e) => ((e.currentTarget as HTMLButtonElement).style.borderColor = "#94A3B8")}
                    onMouseLeave={(e) => ((e.currentTarget as HTMLButtonElement).style.borderColor = "#E2E8F0")}
                  >
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/>
                    </svg>
                    Download Mono for Windows (ASIO)
                  </button>
                </div>

                <button
                  onClick={() => setClientGateLoungeId(null)}
                  style={{ background: "none", border: "none", cursor: "pointer", fontSize: 12, color: "#94A3B8", fontFamily: "'DM Mono', monospace", padding: 0, textAlign: "center", transition: "color 0.15s" }}
                  onMouseEnter={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "#64748B")}
                  onMouseLeave={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "#94A3B8")}
                >
                  Continue in browser (limited)
                </button>
              </div>
            </div>
          </div>
        )
      })()}

      {/* ── Share Modal ─────────────────────────────────────────────────── */}
      <ShareModal
        isOpen={shareModalData !== null}
        onClose={() => setShareModalData(null)}
        shareData={shareModalData}
      />

      {/* ── Profile & Settings Modal ─────────────────────────────────────── */}
      <SettingsModal
        isOpen={showSettingsModal}
        onClose={() => setShowSettingsModal(false)}
        profiles={profiles}
        activeProfileId={activeProfileId}
        activeProfile={activeProfile}
        onSwitchProfile={handleSwitchProfile}
        onAddProfile={handleAddProfile}
        activeTab={settingsActiveTab}
        onTabChange={setSettingsActiveTab}
      />
    </div>
  )
}

