import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react"
import { ControlClient, type ConnectionState } from "../lib/controlClient"
import {
  MSG,
  parseBody,
  QualityPolicy,
  RoomMode,
  StreamingProvider,
  type AlbumDetail,
  type ArchiveView,
  type ArtistGraph,
  type CatalogTrack,
  type HistoryEntry,
  type LibraryFolder,
  type MonoMessage,
  type PlaylistView,
  type RoomListing,
  type RoomSnapshot,
} from "../lib/protocol"
import { groupIntoAlbums, type LiveAlbum } from "../lib/adapters"

/** Core 는 hello 의 displayName 을 멤버 목록·아카이브 참가자에 그대로 쓴다. */
const NAME_KEY = "mono.displayName"

function storedName(): string {
  try {
    return localStorage.getItem(NAME_KEY) || "Listener"
  } catch {
    return "Listener"
  }
}

export interface MonoState {
  connection: ConnectionState
  peerId: string | null
  displayName: string

  /** Core 전체 카탈로그. scan_library·스트리밍 연동 때마다 Core 가 다시 밀어준다. */
  catalog: CatalogTrack[]
  albums: LiveAlbum[]
  catalogLoaded: boolean

  /** 지금 들어와 있는 룸. 솔로 재생도 Core 에서는 룸 하나다. */
  room: RoomSnapshot | null
  rooms: RoomListing[]
  folders: LibraryFolder[]
  playlists: PlaylistView[]
  history: HistoryEntry[]
  archives: ArchiveView[]

  /** 스트리밍 계정 연동 상태. Core 의 link_streaming 이 진실이다. */
  streamingAccounts: StreamingAccount[]

  /** 마지막으로 Core 가 거절한 명령의 사유. UI 가 토스트로 띄운다. */
  lastError: string | null
}

export interface StreamingAccount {
  provider: StreamingProvider
  connected: boolean
  displayName?: string | null
  liveSdk: boolean
  imported: number
  note?: string | null
  authMode: string
}

/** link_streaming 의 oauth 시작 응답. */
export interface OAuthStart {
  provider: StreamingProvider
  state: string
  authUrl: string
  liveSdk: boolean
  demo?: boolean
  already?: boolean
  connected?: boolean
  displayName?: string | null
  note?: string | null
}

export interface MonoCommands {
  setDisplayName(name: string): void
  send(msg: MonoMessage): void
  request(msg: MonoMessage, expect?: string): Promise<MonoMessage>

  // 룸
  createRoom(name: string, mode?: RoomMode): Promise<string | null>
  joinRoom(roomId: string, inviteCode?: string): Promise<void>
  leaveRoom(): void
  refreshRooms(): Promise<void>

  // 트랜스포트
  play(): void
  pause(): void
  togglePlay(): void
  next(): void
  previous(): void
  seek(mediaTimeMs: number): void
  jumpTo(index: number): void
  setVolume(percent: number, targetPeerId?: string): void

  // 큐
  enqueue(trackId: string): void
  enqueueMany(trackIds: string[]): void
  playNow(trackId: string): Promise<void>
  playAlbum(trackIds: string[]): Promise<void>
  removeFromQueue(index: number): void
  moveInQueue(index: number, delta: number): void
  clearQueue(): void

  // 소셜
  chat(text: string): void
  react(emoji: string): void
  pin(text: string): void

  // 라이브러리
  scanLibrary(path?: string): Promise<void>
  refreshFolders(): Promise<void>
  refreshPlaylists(): Promise<void>
  refreshHistory(): Promise<void>
  refreshArchives(): Promise<void>
  search(query: string): Promise<CatalogTrack[]>
  albumDetail(albumId: string): Promise<AlbumDetail | null>
  artistGraph(artistId: string): Promise<ArtistGraph | null>
  createPlaylist(title: string, trackIds: string[]): Promise<void>
  loadPlaylist(playlistId: string): void

  // 설정
  setQualityPolicy(policy: QualityPolicy): void
  setDsp(preset: number, enabled: boolean): void
  setRoomFlag(flag: string, value: boolean): void
  linkStreaming(provider: StreamingProvider, token?: string): Promise<MonoMessage>
  /** OAuth 를 시작하고 인증 URL 을 받는다. 브라우저로 여는 건 호출부 책임. */
  beginOAuth(provider: StreamingProvider): Promise<OAuthStart | null>
  unlinkStreaming(provider: StreamingProvider): Promise<void>
  /** Core 에 현재 연동 상태를 다시 묻는다. 부작용 없는 조회다. */
  refreshStreamingAccounts(): void

  clearError(): void
}

const StateContext = createContext<MonoState | null>(null)
const CommandContext = createContext<MonoCommands | null>(null)

export function useMono(): MonoState {
  const ctx = useContext(StateContext)
  if (!ctx) throw new Error("useMono must be used inside <MonoProvider>")
  return ctx
}

export function useMonoCommands(): MonoCommands {
  const ctx = useContext(CommandContext)
  if (!ctx) throw new Error("useMonoCommands must be used inside <MonoProvider>")
  return ctx
}

export function MonoProvider({ children }: { children: ReactNode }) {
  const [displayName, setName] = useState(storedName)
  const clientRef = useRef<ControlClient | null>(null)
  if (!clientRef.current) clientRef.current = new ControlClient(displayName)
  const client = clientRef.current

  const [connection, setConnection] = useState<ConnectionState>("connecting")
  const [peerId, setPeerId] = useState<string | null>(null)
  const [catalog, setCatalog] = useState<CatalogTrack[]>([])
  const [catalogLoaded, setCatalogLoaded] = useState(false)
  const [room, setRoom] = useState<RoomSnapshot | null>(null)
  const [rooms, setRooms] = useState<RoomListing[]>([])
  const [folders, setFolders] = useState<LibraryFolder[]>([])
  const [playlists, setPlaylists] = useState<PlaylistView[]>([])
  const [history, setHistory] = useState<HistoryEntry[]>([])
  const [archives, setArchives] = useState<ArchiveView[]>([])
  const [lastError, setLastError] = useState<string | null>(null)
  const [streamingAccounts, setStreamingAccounts] = useState<StreamingAccount[]>([])

  /** 내가 들어와 있는 룸 id. room_state 는 내가 없는 룸의 것도 올 수 있다. */
  const myRoomRef = useRef<string | null>(null)

  useEffect(() => {
    const offState = client.onState((s) => {
      setConnection(s)
      setPeerId(client.peerId)
    })

    const off = client.on((msg) => {
      switch (msg.type) {
        case MSG.catalog: {
          const tracks = parseBody<CatalogTrack[]>(msg)
          if (tracks) {
            setCatalog(tracks)
            setCatalogLoaded(true)
          }
          break
        }

        case MSG.roomState: {
          const snap = parseBody<RoomSnapshot>(msg)
          if (!snap) break
          // 내가 속하지 않은 룸의 상태는 목록만 갱신하고 현재 룸은 건드리지 않는다.
          if (myRoomRef.current && snap.id !== myRoomRef.current) break
          myRoomRef.current = snap.id
          setRoom(snap)
          break
        }

        case MSG.listRooms: {
          const list = parseBody<RoomListing[]>(msg)
          if (list) setRooms(list)
          break
        }

        case MSG.folders: {
          const list = parseBody<LibraryFolder[]>(msg)
          if (list) setFolders(list)
          break
        }

        case MSG.playlists: {
          const list = parseBody<PlaylistView[]>(msg)
          if (list) setPlaylists(list)
          break
        }

        case MSG.history: {
          const list = parseBody<HistoryEntry[]>(msg)
          if (list) setHistory(list)
          break
        }

        case MSG.archives: {
          const list = parseBody<ArchiveView[]>(msg)
          if (list) setArchives(list)
          break
        }

        case MSG.streamingAccounts: {
          const accounts = parseBody<StreamingAccount[]>(msg)
          if (Array.isArray(accounts)) setStreamingAccounts(accounts)
          break
        }

        case MSG.linkStreaming: {
          // body 는 계정 목록이거나(성공) 사람이 읽는 한 줄이다(해제·오류).
          const accounts = parseBody<StreamingAccount[]>(msg)
          if (Array.isArray(accounts)) setStreamingAccounts(accounts)
          break
        }

        case MSG.error:
          if (msg.error) setLastError(msg.error)
          break
      }
    })

    client.connect()
    return () => {
      off()
      offState()
      client.close()
    }
  }, [client])

  // 붙자마자 룸·폴더·플레이리스트 목록을 한 번 받아 온다.
  useEffect(() => {
    if (connection !== "open") return
    client.send({ type: MSG.listRooms })
    client.send({ type: MSG.folders })
    client.send({ type: MSG.playlists })
    client.send({ type: MSG.history })
    client.send({ type: MSG.archives })
    client.send({ type: MSG.streamingAccounts })
  }, [connection, client])

  const albums = useMemo(
    () => groupIntoAlbums(catalog, room?.currentTrack?.id),
    [catalog, room?.currentTrack?.id],
  )

  // ── 명령 ──────────────────────────────────────────────────────────────────

  const send = useCallback((msg: MonoMessage) => client.send(msg), [client])

  const request = useCallback(
    (msg: MonoMessage, expect?: string) => client.request(msg, expect),
    [client],
  )

  /** 솔로 재생용 룸을 필요할 때 한 번 만든다. Core 에서는 혼자 듣기도 룸이다. */
  const ensureRoom = useCallback(async (): Promise<string | null> => {
    if (myRoomRef.current) return myRoomRef.current
    try {
      const res = await client.request(
        { type: MSG.createRoom, roomName: `${client.displayName}의 방`, mode: RoomMode.Solo },
        MSG.roomState,
      )
      const snap = parseBody<RoomSnapshot>(res)
      if (snap) {
        myRoomRef.current = snap.id
        setRoom(snap)
        return snap.id
      }
    } catch (err) {
      setLastError(err instanceof Error ? err.message : String(err))
    }
    return null
  }, [client])

  const commands = useMemo<MonoCommands>(() => {
    const withRoom = (msg: MonoMessage) => {
      if (!myRoomRef.current) return
      client.send({ ...msg, roomId: myRoomRef.current })
    }

    return {
      setDisplayName(name: string) {
        const trimmed = name.trim() || "Listener"
        setName(trimmed)
        client.displayName = trimmed
        try {
          localStorage.setItem(NAME_KEY, trimmed)
        } catch {
          // 시크릿 모드 등 저장이 막힌 환경 — 이름은 이번 세션에만 유지된다.
        }
      },

      send,
      request,

      async createRoom(name, mode = RoomMode.Open) {
        try {
          const res = await client.request(
            { type: MSG.createRoom, roomName: name, mode },
            MSG.roomState,
          )
          const snap = parseBody<RoomSnapshot>(res)
          if (!snap) return null
          myRoomRef.current = snap.id
          setRoom(snap)
          return snap.id
        } catch (err) {
          setLastError(err instanceof Error ? err.message : String(err))
          return null
        }
      },

      async joinRoom(id, inviteCode) {
        try {
          const res = await client.request(
            { type: MSG.joinRoom, roomId: id, inviteCode },
            MSG.roomState,
          )
          const snap = parseBody<RoomSnapshot>(res)
          if (snap) {
            myRoomRef.current = snap.id
            setRoom(snap)
          }
        } catch (err) {
          setLastError(err instanceof Error ? err.message : String(err))
        }
      },

      leaveRoom() {
        if (!myRoomRef.current) return
        client.send({ type: MSG.leaveRoom, roomId: myRoomRef.current })
        myRoomRef.current = null
        setRoom(null)
        client.send({ type: MSG.listRooms })
      },

      async refreshRooms() {
        client.send({ type: MSG.listRooms })
      },

      play: () => withRoom({ type: MSG.play }),
      pause: () => withRoom({ type: MSG.pause }),
      togglePlay: () => withRoom({ type: room?.playing ? MSG.pause : MSG.play }),
      next: () => withRoom({ type: MSG.skip, delta: 1 }),
      previous: () => withRoom({ type: MSG.skip, delta: -1 }),
      seek: (mediaTimeMs) => withRoom({ type: MSG.seek, mediaTimeMs: Math.max(0, Math.round(mediaTimeMs)) }),
      jumpTo: (index) => withRoom({ type: MSG.jumpTo, index }),
      setVolume: (percent, targetPeerId) =>
        withRoom({
          type: MSG.setVolume,
          volume: Math.max(0, Math.min(100, Math.round(percent))),
          targetPeerId,
        }),

      enqueue: (trackId) => withRoom({ type: MSG.enqueue, trackId }),

      enqueueMany(trackIds) {
        for (const trackId of trackIds) withRoom({ type: MSG.enqueue, trackId })
      },

      async playNow(trackId) {
        const id = await ensureRoom()
        if (!id) return
        client.send({ type: MSG.enqueue, roomId: id, trackId })
        // 방금 넣은 곡으로 건너뛴다. 큐가 비어 있었다면 Core 가 알아서 0번을 재생한다.
        client.send({ type: MSG.play, roomId: id })
      },

      async playAlbum(trackIds) {
        const id = await ensureRoom()
        if (!id || trackIds.length === 0) return
        client.send({ type: MSG.clearQueue, roomId: id })
        for (const trackId of trackIds) client.send({ type: MSG.enqueue, roomId: id, trackId })
        client.send({ type: MSG.play, roomId: id })
      },

      removeFromQueue: (index) => withRoom({ type: MSG.removeQueue, index }),
      moveInQueue: (index, delta) => withRoom({ type: MSG.moveQueue, index, delta }),
      clearQueue: () => withRoom({ type: MSG.clearQueue }),

      chat: (text) => {
        if (text.trim()) withRoom({ type: MSG.chat, text: text.trim() })
      },
      react: (emoji) => withRoom({ type: MSG.react, emoji }),
      pin: (text) => {
        if (text.trim()) withRoom({ type: MSG.pin, text: text.trim() })
      },

      async scanLibrary(path) {
        try {
          await client.request({ type: MSG.scanLibrary, path }, MSG.catalog, 120_000)
          client.send({ type: MSG.folders })
        } catch (err) {
          setLastError(err instanceof Error ? err.message : String(err))
        }
      },

      async refreshFolders() {
        client.send({ type: MSG.folders })
      },

      async refreshPlaylists() {
        client.send({ type: MSG.playlists })
      },

      async refreshHistory() {
        client.send({ type: MSG.history })
      },

      async refreshArchives() {
        client.send({ type: MSG.archives })
      },

      async search(query) {
        try {
          const res = await client.request({ type: MSG.search, text: query }, MSG.search)
          return parseBody<CatalogTrack[]>(res) ?? []
        } catch {
          return []
        }
      },

      async albumDetail(albumId) {
        try {
          const res = await client.request({ type: MSG.album, text: albumId }, MSG.album)
          return parseBody<AlbumDetail>(res)
        } catch {
          return null
        }
      },

      async artistGraph(artistId) {
        try {
          const res = await client.request({ type: MSG.graph, text: artistId }, MSG.graph)
          return parseBody<ArtistGraph>(res)
        } catch {
          return null
        }
      },

      async createPlaylist(title, trackIds) {
        try {
          await client.request({ type: MSG.createPlaylist, text: title, trackIds }, MSG.playlists)
        } catch (err) {
          setLastError(err instanceof Error ? err.message : String(err))
        }
      },

      loadPlaylist: (playlistId) => withRoom({ type: MSG.loadPlaylist, playlistId }),

      setQualityPolicy: (policy) => withRoom({ type: MSG.setPolicy, policy }),
      setDsp: (preset, enabled) => withRoom({ type: MSG.setDsp, dsp: preset, flag: enabled }),
      setRoomFlag: (flag, value) => withRoom({ type: MSG.setRoomFlags, text: flag, flag: value }),

      linkStreaming: (provider, token) =>
        client.request({ type: MSG.linkStreaming, provider, token }, MSG.linkStreaming, 60_000),

      async beginOAuth(provider) {
        try {
          const res = await client.request(
            { type: MSG.linkStreaming, provider, text: "oauth" },
            MSG.linkStreaming,
            60_000,
          )
          return parseBody<OAuthStart>(res)
        } catch (err) {
          setLastError(err instanceof Error ? err.message : String(err))
          return null
        }
      },

      async unlinkStreaming(provider) {
        // 토큰 없이 보내면 Core 가 연결을 끊는다.
        client.send({ type: MSG.linkStreaming, provider })
        client.send({ type: MSG.streamingAccounts })
      },

      refreshStreamingAccounts() {
        client.send({ type: MSG.streamingAccounts })
      },

      clearError: () => setLastError(null),
    }
  }, [client, send, request, ensureRoom, room?.playing])

  const state = useMemo<MonoState>(
    () => ({
      connection,
      peerId,
      displayName,
      catalog,
      albums,
      catalogLoaded,
      room,
      rooms,
      folders,
      playlists,
      history,
      archives,
      streamingAccounts,
      lastError,
    }),
    [connection, peerId, displayName, catalog, albums, catalogLoaded, room, rooms, folders, playlists, history, archives, streamingAccounts, lastError],
  )

  return (
    <StateContext.Provider value={state}>
      <CommandContext.Provider value={commands}>{children}</CommandContext.Provider>
    </StateContext.Provider>
  )
}

/**
 * 재생 위치를 초 단위로 보간한다. Core 는 room_state 를 이벤트마다만 밀어 주므로
 * 진행 바는 mediaOriginUnixMs 기준으로 클라이언트가 스스로 굴려야 매끄럽다.
 */
export function useMediaClock(): { positionMs: number; durationMs: number } {
  const { room } = useMono()
  const [, tick] = useState(0)

  useEffect(() => {
    if (!room?.playing) return
    const id = setInterval(() => tick((n) => n + 1), 250)
    return () => clearInterval(id)
  }, [room?.playing])

  if (!room) return { positionMs: 0, durationMs: 0 }
  if (!room.playing) return { positionMs: room.mediaTimeMs, durationMs: room.durationMs }

  const elapsed = Date.now() - room.mediaOriginUnixMs
  const position = room.mediaTimeAtOriginMs + elapsed
  return {
    positionMs: Math.max(0, Math.min(position, room.durationMs || position)),
    durationMs: room.durationMs,
  }
}
