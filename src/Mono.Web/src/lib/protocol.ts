// Core 와 주고받는 메시지 규약. src/Mono.Protocol/MessageTypes.cs · RoomSnapshot.cs 의 거울이다.
// 필드를 추가할 때는 반드시 C# 쪽을 먼저 고친다 — 여기가 진실의 원본이 아니다.

export const MSG = {
  hello: "hello",
  welcome: "welcome",
  error: "error",
  redeem: "redeem",

  createRoom: "create_room",
  publishRoom: "publish_room",
  closeRoom: "close_room",
  joinRoom: "join_room",
  leaveRoom: "leave_room",
  listRooms: "list_rooms",
  roomState: "room_state",
  invite: "invite",
  kick: "kick",
  transferHost: "transfer_host",
  setRole: "set_role",
  spectate: "spectate",
  setPolicy: "set_policy",
  setDsp: "set_dsp",
  setConvolutionIr: "set_convolution_ir",
  setEasyEq: "set_easy_eq",
  setSpeakerSetup: "set_speaker_setup",
  setHeadroom: "set_headroom",
  setDeviceEq: "set_device_eq",
  setSourceMode: "set_source_mode",
  setRoomFlags: "set_room_flags",

  enqueue: "enqueue",
  requestTrack: "request_track",
  approveRequest: "approve_request",
  rejectRequest: "reject_request",
  removeQueue: "remove_queue",
  moveQueue: "move_queue",
  clearQueue: "clear_queue",
  play: "play",
  pause: "pause",
  seek: "seek",
  skip: "skip",
  jumpTo: "jump_to",
  resync: "resync",

  pin: "pin",
  removePin: "remove_pin",
  chat: "chat",
  react: "react",
  followArtist: "follow_artist",
  followHost: "follow_host",

  autoplayCandidates: "autoplay_candidates",
  chooseAutoplay: "choose_autoplay",

  catalog: "catalog",
  search: "search",
  graph: "graph",
  scanLibrary: "scan_library",
  folders: "folders",
  album: "album",
  backup: "backup",
  linkStreaming: "link_streaming",
  streamingAccounts: "streaming_accounts",
  reactionHeatmap: "reaction_heatmap",
  wikiBio: "wiki_bio",

  createZone: "create_zone",
  renameZone: "rename_zone",
  setZoneMode: "set_zone_mode",
  zoneAddMember: "zone_add_member",
  zoneRemoveMember: "zone_remove_member",
  deleteZone: "delete_zone",
  listZones: "list_zones",

  endSession: "end_session",
  archive: "archive",
  archives: "archives",
  history: "history",
  playlists: "playlists",
  createPlaylist: "create_playlist",
  loadPlaylist: "load_playlist",
  exportM3u: "export_m3u",
  shareSession: "share_session",

  setVolume: "set_volume",
  endpoints: "endpoints",
} as const

export type MessageType = (typeof MSG)[keyof typeof MSG]

/** Mono.Protocol.MonoMessage. 전 필드 선택적 — Core 가 쓰는 것만 채워 보낸다. */
export interface MonoMessage {
  type: string
  peerId?: string
  displayName?: string
  role?: string
  roomId?: string
  roomName?: string
  mode?: RoomMode
  trackId?: string
  text?: string
  mediaTimeMs?: number
  ok?: boolean
  error?: string
  /** 구조화된 페이로드는 전부 여기 JSON 문자열로 온다. */
  body?: string
  playing?: boolean
  sampleRate?: number
  bitDepth?: number
  channels?: number
  isDsd?: boolean
  durationMs?: number
  epoch?: number
  mediaOriginUnixMs?: number
  mediaTimeAtOriginMs?: number
  inviteCode?: string
  inviteAction?: number
  minutes?: number
  targetPeerId?: string
  member?: number
  index?: number
  delta?: number
  flag?: boolean
  volume?: number
  path?: string
  provider?: StreamingProvider
  token?: string
  dsp?: number
  policy?: QualityPolicy
  sourceMode?: PlaybackSourceMode
  emoji?: string
  device?: string
  pairingCode?: string
  playlistId?: string
  archiveId?: string
  trackIds?: string[]
  zoneId?: string
  zoneMode?: number
  deadlineUnixMs?: number
}

// Mono.Shared 의 enum 은 System.Text.Json 기본 설정상 숫자로 직렬화된다.
// C# 의 Mono.Shared.RoomMode 와 값이 같아야 한다 — 이 숫자가 그대로 와이어에 실린다.
// Solo 를 2 로 알고 보내던 동안 혼자 듣기가 HostQueue 룸으로 만들어졌고, 곡을 틀 때마다
// 공개 라운지가 하나씩 생겼다. 새 값은 C# 쪽과 같이, 항상 뒤에 붙인다.
export const RoomMode = { Open: 0, Invite: 1, HostQueue: 2, Audiophile: 3, Solo: 4 } as const
export type RoomMode = (typeof RoomMode)[keyof typeof RoomMode]

export const StreamingProvider = { Local: 0, Tidal: 1, Qobuz: 2 } as const
export type StreamingProvider = (typeof StreamingProvider)[keyof typeof StreamingProvider]

export const QualityPolicy = { Original: 0, MatchAll: 1, HostPriority: 2 } as const
export type QualityPolicy = (typeof QualityPolicy)[keyof typeof QualityPolicy]

export const PlaybackSourceMode = { ClockSync: 0, FanOut: 1 } as const
export type PlaybackSourceMode = (typeof PlaybackSourceMode)[keyof typeof PlaybackSourceMode]

export const MemberRole = { Listener: 0, CoHost: 1, Host: 2 } as const

// ── room_state 의 body ────────────────────────────────────────────────────────

export interface SnapshotTrack {
  id: string
  title: string
  albumId?: string | null
  artistId?: string | null
  artistName?: string | null
  albumTitle?: string | null
  sampleRate: number
  bitDepth: number
  channels: number
  isDsd: boolean
  dsdRate?: number | null
  durationMs: number
  source: StreamingProvider
  streamingQuality: number
  mergedLocalAndStreaming: boolean
  hasLocal: boolean
  badge?: string | null
  artUrl?: string | null
}

export interface SnapshotQueueItem {
  id: string
  trackId: string
  addedByPeerId: string
  title: string
  artist?: string | null
  durationMs: number
  badge?: string | null
  artUrl?: string | null
}

export interface SnapshotMember {
  peerId: string
  name?: string | null
  role: number
  isOutput: boolean
  spectator: boolean
  stats?: PeerStats | null
}

export interface PeerStats {
  offsetMs?: number
  jitterMs?: number
  rttMs?: number
  bufferMs?: number
  resyncs?: number
  locked?: boolean
  deviceState?: number
  deviceError?: string | null
}

export interface SnapshotOutput {
  peerId: string
  displayName?: string | null
  maxSampleRate: number
  maxBitDepth: number
  supportsDsd: boolean
  exclusiveMode: boolean
  reportedLatencyMs: number
  hardwareVolume: boolean
  volumePercent: number
  device?: string | null
  spectator: boolean
  badge?: string | null
  note?: string | null
  stats?: PeerStats | null
}

export interface SnapshotChatLine {
  peerId: string
  peerName?: string | null
  text: string
  at: string
}

export interface SnapshotPin {
  id: string
  peerId: string
  peerName?: string | null
  trackId: string
  mediaTimeMs: number
  text: string
  createdAt: string
  onCurrentTrack: boolean
}

export interface SnapshotRequest {
  id: string
  trackId: string
  fromPeerId: string
  fromName?: string | null
  title?: string | null
}

export interface SnapshotAlbum {
  id: string
  title: string
  artistId?: string | null
  linerNotes?: string | null
  label?: string | null
  year?: number | null
  credits?: string | null
  artworkPath?: string | null
}

export interface SnapshotArtist {
  id: string
  name: string
  relatedArtistIds: string[]
  bio?: string | null
  alternateNames: string[]
}

export interface SnapshotHeatBucket {
  trackId: string
  bucketMs: number
  count: number
}

export interface SnapshotLyricLine {
  timeMs: number
  text: string
}

export interface SnapshotAutoplay {
  candidates: SnapshotTrack[]
  deadlineUnixMs: number
  chosenId?: string | null
}

export interface RoomSnapshot {
  id: string
  name: string
  mode: RoomMode
  sourceMode: PlaybackSourceMode
  qualityPolicy: QualityPolicy

  dspPreset: number
  dspEnabled: boolean
  dspLocked: boolean
  convolutionIrPath?: string | null
  easyEqJson?: string | null
  easyEqGraphicMode: boolean
  headroomDb: number
  speakerDelayMsLeft: number
  speakerDelayMsRight: number
  speakerGainLeftDb: number
  speakerGainRightDb: number
  deviceEqProfile?: string | null

  seekingAllowed: boolean
  commentsAllowed: boolean
  chatCollapsed: boolean
  queueLocked: boolean
  followHostView: boolean
  autoAdvance: boolean
  smartAutoplay: boolean
  linerPage: number
  linerScrollY: number
  maxMembers: number
  inviteCode?: string | null
  inviteExpiresAt?: string | null

  archiveDefaultConsent: boolean
  commentRetentionDays: number
  anonymizeArchive: boolean
  cloudSyncOptIn: boolean

  playing: boolean
  queueIndex: number
  hostPeerId?: string | null
  resyncEpoch: number
  startedAt: string
  mediaTimeMs: number
  durationMs: number
  mediaOriginUnixMs: number
  mediaTimeAtOriginMs: number

  bitPerfect: boolean
  srcApplied: boolean
  pathBadge?: string | null
  catalogCount: number

  queue: SnapshotQueueItem[]
  requests: SnapshotRequest[]
  pins: SnapshotPin[]
  reactions: { peerId: string; trackId: string; emoji: string; at: string; mediaTimeMs: number }[]
  reactionCounts: Record<string, number>
  allowedReactionEmoji: string[]
  heatmap: SnapshotHeatBucket[]
  chat: SnapshotChatLine[]
  members: SnapshotMember[]
  outputs: SnapshotOutput[]
  spectators: string[]
  peers: Record<string, string>

  currentTrack?: SnapshotTrack | null
  album?: SnapshotAlbum | null
  artist?: SnapshotArtist | null
  albumTracks: SnapshotTrack[]
  relatedArtists: SnapshotArtist[]
  linerNotes?: string | null
  credits?: string | null
  lyrics: SnapshotLyricLine[]
  currentLyric?: string | null
  autoplay?: SnapshotAutoplay | null
}

// ── catalog 의 body ───────────────────────────────────────────────────────────

/** CatalogStore.TrackView 의 거울. */
export interface CatalogTrack {
  id: string
  title: string
  albumId: string
  artistId: string
  artist?: string | null
  artistAliases: string[]
  album?: string | null
  year?: number | null
  label?: string | null
  sampleRate: number
  bitDepth: number
  isDsd: boolean
  dsdRate?: number | null
  durationMs: number
  source: StreamingProvider
  streamingQuality: number
  mergedLocalAndStreaming: boolean
  genres: string[]
  composers: string[]
  workKey?: string | null
  workTitle?: string | null
  hasLyrics: boolean
  hasLocal: boolean
  artUrl?: string | null
  /** 라이브러리에 처음 들어온 시각(ISO 8601). 홈의 "최근 추가" 정렬 근거. */
  addedAt?: string | null
  badge?: string | null
}

/** list_rooms 의 body. */
export interface RoomListing {
  id: string
  name: string
  mode: RoomMode
  sourceMode: PlaybackSourceMode
  qualityPolicy: QualityPolicy
  playing: boolean
  host: string
  needsInvite: boolean
  members: number
  outputs: number
  queued: number
  badge?: string | null
}

/** album 명령의 body. */
export interface AlbumDetail {
  id: string
  title: string
  artist?: string | null
  year?: number | null
  label?: string | null
  linerNotes?: string | null
  credits?: string | null
  tracks: CatalogTrack[]
}

/** graph 명령의 body. */
export interface ArtistGraph {
  error?: string
  artist?: { id: string; name: string; bio?: string | null; alternateNames?: string[] }
  albums?: {
    id: string
    title: string
    year?: number | null
    label?: string | null
    credits?: string | null
    linerNotes?: string | null
    artUrl?: string | null
    trackCount: number
  }[]
  tracks?: CatalogTrack[]
  related?: { id: string; name: string }[]
}

/** folders 명령의 body. */
export interface LibraryFolder {
  path: string
  exists: boolean
  trackCount: number
}

/** playlists 명령의 body. */
export interface PlaylistView {
  id: string
  title: string
  createdAt: string
  fromArchiveId?: string | null
  tracks: { id: string; title: string; artist?: string | null; durationMs: number; artUrl?: string | null }[]
}

/** history 명령의 body. */
export interface HistoryEntry {
  id: string
  trackId: string
  roomId?: string | null
  roomName?: string | null
  heardAt: string
  completed: boolean
  title: string
  artist?: string | null
}

/** archives 명령의 body. */
export interface ArchiveView {
  id: string
  roomName: string
  mode: RoomMode
  startedAt: string
  endedAt?: string | null
  participants: string[]
  tracks: { id: string; title: string; highlight: boolean }[]
  pins: number
  hits: unknown[]
}

/** body 를 파싱한다. 깨져 있으면 화면을 지키기 위해 null 을 준다. */
export function parseBody<T>(msg: MonoMessage): T | null {
  if (!msg.body) return null
  try {
    return JSON.parse(msg.body) as T
  } catch {
    return null
  }
}
