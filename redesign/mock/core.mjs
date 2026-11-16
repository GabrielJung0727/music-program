// 모의 Mono Core.
//
// 진짜 Core(.NET)와 같은 규약을 말한다 — /ws/control 의 MonoMessage, /api/* REST.
// 음원도 출력 장치도 없지만 룸·큐·타임라인은 실제처럼 굴러가므로, 화면을
// Core 없이 띄워 보고 캡처하는 데 쓴다. 디자인 작업 전용이고 배포물에는 들어가지 않는다.
//
//   node mock/core.mjs            → http://127.0.0.1:7802
//   MOCK_PORT=7702 node mock/core.mjs

import http from "node:http"
import { WebSocketServer } from "ws"
import { CATALOG, trackById, albumById, artistById, trackView, snapshotTrack, lyricsFor, rng } from "./catalog.mjs"
import { coverSvg } from "./covers.mjs"

const PORT = Number(process.env.MOCK_PORT ?? 7802)
const RoomMode = { Open: 0, Invite: 1, HostQueue: 2, Audiophile: 3, Solo: 4 }
const json = (v) => JSON.stringify(v)
const now = () => Date.now()
const iso = (ms) => new Date(ms).toISOString()

// ── 이 PC 의 설정 (data/setup.json 자리) ─────────────────────────────────────

const DEFAULT_SETUP = {
  completed: true,
  theme: "dark",
  folders: ["D:\\Music\\Hi-Res", "D:\\Music\\DSD", "\\\\NAS\\archive\\classical"],
  autoWatch: true,
  audio: { driverType: "WASAPI_EXCLUSIVE", deviceId: "dev-adi2", deviceName: "RME ADI-2 DAC FS", bufferSize: 256, exclusiveMode: true },
  profile: {
    displayName: "Gabriel",
    avatarColor: "#6D6DF6",
    gear: { headphonesOrSpeakers: "Sennheiser HD 800 S", amplifier: "Benchmark HPA4", dac: "RME ADI-2 DAC FS" },
  },
  savedAt: "2026-11-02T09:12:00.000Z",
}
let setup = structuredClone(DEFAULT_SETUP)

// ── 스트리밍 계정 ────────────────────────────────────────────────────────────

const accounts = [
  { provider: 1, connected: true, displayName: "gabriel@tidal", liveSdk: true, imported: 12, note: null, authMode: "oauth" },
  { provider: 2, connected: false, displayName: null, liveSdk: false, imported: 0, note: null, authMode: "token" },
]

// ── 이력 · 플레이리스트 · 아카이브 ───────────────────────────────────────────

const T = CATALOG.tracks
const pick = (albumIndex, n) => CATALOG.albums[albumIndex].trackIds[n]

const history = (() => {
  const r = rng("history")
  const out = []
  const favourites = [pick(1, 0), pick(0, 0), pick(16, 0), pick(2, 2), pick(17, 0), pick(8, 1), pick(4, 0), pick(14, 1), pick(1, 2), pick(21, 0)]
  for (let i = 0; i < 96; i++) {
    const id = r() < 0.6 ? favourites[Math.floor(r() * r() * favourites.length)] : T[Math.floor(r() * T.length)].id
    const t = trackById.get(id)
    out.push({
      id: `h-${i}`, trackId: id, roomId: null, roomName: null,
      heardAt: iso(now() - i * 2.1 * 3600_000 - r() * 3600_000),
      completed: r() > 0.2, title: t.title, artist: t.artist,
    })
  }
  return out
})()

const playlistTrack = (id) => {
  const t = trackById.get(id)
  return { id, title: t.title, artist: t.artist, durationMs: t.durationMs, artUrl: t.artUrl }
}

const playlists = [
  { id: "pl-late", title: "Late Night Reference", createdAt: "2026-10-28T22:10:00Z", fromArchiveId: null,
    tracks: [pick(0, 0), pick(1, 2), pick(2, 0), pick(17, 0), pick(3, 2), pick(1, 6), pick(2, 5), pick(0, 5), pick(17, 3)].map(playlistTrack) },
  { id: "pl-test", title: "DAC 테스트 트랙", createdAt: "2026-10-12T10:00:00Z", fromArchiveId: null,
    tracks: [pick(6, 3), pick(5, 2), pick(4, 0), pick(21, 0), pick(8, 1), pick(2, 2)].map(playlistTrack) },
  { id: "pl-sunday", title: "일요일 아침", createdAt: "2026-09-30T08:30:00Z", fromArchiveId: null,
    tracks: [pick(16, 0), pick(16, 4), pick(20, 0), pick(11, 0), pick(19, 1), pick(18, 3), pick(12, 2), pick(20, 3)].map(playlistTrack) },
  { id: "pl-focus", title: "Focus: No Vocals", createdAt: "2026-08-19T13:00:00Z", fromArchiveId: null,
    tracks: [pick(8, 0), pick(8, 4), pick(21, 1), pick(21, 4), pick(23, 2), pick(9, 2), pick(4, 7)].map(playlistTrack) },
  { id: "pl-lounge", title: "금요일 라운지 셋리스트", createdAt: "2026-11-07T21:00:00Z", fromArchiveId: "ar-1",
    tracks: [pick(14, 0), pick(15, 0), pick(14, 3), pick(10, 0), pick(15, 2), pick(12, 0)].map(playlistTrack) },
]

const archives = [
  { id: "ar-1", roomName: "Friday Night Hi-Fi", mode: 0, startedAt: "2026-11-07T12:00:00Z", endedAt: "2026-11-07T14:40:00Z",
    participants: ["Gabriel", "soyeon", "tubehead", "M. Laurent"],
    tracks: [pick(14, 0), pick(15, 0), pick(14, 3), pick(10, 0), pick(15, 2), pick(12, 0)].map((id, i) => ({ id, title: trackById.get(id).title, highlight: i === 1 })), pins: 4, hits: [] },
  { id: "ar-2", roomName: "Blue Hour Jazz", mode: 0, startedAt: "2026-11-02T13:00:00Z", endedAt: "2026-11-02T15:10:00Z",
    participants: ["Gabriel", "dsd_only"],
    tracks: [pick(0, 0), pick(1, 0), pick(2, 2), pick(3, 2), pick(17, 0)].map((id, i) => ({ id, title: trackById.get(id).title, highlight: i === 0 })), pins: 2, hits: [] },
  { id: "ar-3", roomName: "클래식 정주행", mode: 1, startedAt: "2026-10-25T10:00:00Z", endedAt: "2026-10-25T12:30:00Z",
    participants: ["Gabriel", "하늘", "re:verb"],
    tracks: [pick(6, 0), pick(6, 3), pick(4, 0), pick(5, 2)].map((id, i) => ({ id, title: trackById.get(id).title, highlight: i === 1 })), pins: 6, hits: [] },
]

// ── 룸 ──────────────────────────────────────────────────────────────────────

/** @type {Map<string, any>} */
const rooms = new Map()
let roomSeq = 1

function makeRoom({ id, name, mode, hostPeerId, hostName }) {
  const room = {
    id: id ?? `room-${roomSeq++}`,
    name, mode,
    sourceMode: 0, qualityPolicy: 0,
    dspPreset: 0, dspEnabled: false, dspLocked: false,
    convolutionIrPath: null, easyEqJson: null, easyEqGraphicMode: false,
    headroomDb: -3, speakerDelayMsLeft: 0, speakerDelayMsRight: 0, speakerGainLeftDb: 0, speakerGainRightDb: 0,
    deviceEqProfile: null,
    seekingAllowed: true, commentsAllowed: true, chatCollapsed: false, queueLocked: false,
    followHostView: false, autoAdvance: true, smartAutoplay: true, shuffle: false, repeat: 0,
    linerPage: 0, linerScrollY: 0, maxMembers: 32, inviteCode: null, inviteExpiresAt: null,
    archiveDefaultConsent: true, commentRetentionDays: 30, anonymizeArchive: false, cloudSyncOptIn: false,
    playing: false, queueIndex: -1, hostPeerId, resyncEpoch: 1, startedAt: iso(now()),
    mediaTimeMs: 0, durationMs: 0, mediaOriginUnixMs: now(), mediaTimeAtOriginMs: 0,
    queue: [], requests: [], pins: [], reactions: [], reactionCounts: {},
    allowedReactionEmoji: ["👏", "🔥", "❤️", "🎉", "🤯"],
    chat: [], members: [], outputs: [], spectators: [],
    peers: { [hostPeerId]: hostName },
    _timer: null,
  }
  room.members.push({ peerId: hostPeerId, name: hostName, role: 2, isOutput: false, spectator: false, stats: null })
  rooms.set(room.id, room)
  return room
}

const slot = (room, trackId, by) => {
  const t = trackById.get(trackId)
  return { id: `q-${room.id}-${Math.random().toString(36).slice(2, 9)}`, trackId, addedByPeerId: by, title: t.title, artist: t.artist, durationMs: t.durationMs, badge: t.badge, artUrl: t.artUrl }
}

function position(room) {
  if (!room.playing) return room.mediaTimeMs
  return Math.min(room.durationMs || Infinity, room.mediaTimeAtOriginMs + (now() - room.mediaOriginUnixMs))
}

/** 타임라인 원점을 다시 잡는다. 재생 위치가 바뀌는 모든 명령이 여기를 지난다. */
function anchor(room, mediaTimeMs) {
  room.mediaTimeMs = mediaTimeMs
  room.mediaTimeAtOriginMs = mediaTimeMs
  room.mediaOriginUnixMs = now()
  room.resyncEpoch++
  schedule(room)
}

function current(room) {
  const q = room.queue[room.queueIndex]
  return q ? trackById.get(q.trackId) : null
}

function load(room, index, play = room.playing) {
  if (index < 0 || index >= room.queue.length) {
    room.playing = false
    anchor(room, 0)
    return
  }
  room.queueIndex = index
  room.durationMs = current(room).durationMs
  room.playing = play
  anchor(room, 0)
}

function schedule(room) {
  clearTimeout(room._timer)
  if (!room.playing || !current(room)) return
  const left = Math.max(200, room.durationMs - position(room))
  room._timer = setTimeout(() => {
    const next = room.repeat === 2 ? room.queueIndex : room.queueIndex + 1
    if (next >= room.queue.length) {
      if (room.repeat === 1) load(room, 0, true)
      else { room.playing = false; anchor(room, room.durationMs) }
    } else load(room, next, true)
    broadcast(room)
  }, left)
}

function pathBadge(t) {
  return t ? t.badge : "Idle"
}

function snapshot(room) {
  const t = current(room)
  const album = t ? albumById.get(t.albumId) : null
  const artist = t ? artistById.get(t.artistId) : null
  const pos = position(room)
  const lyrics = t ? lyricsFor(t) : []
  const line = [...lyrics].reverse().find((l) => l.timeMs <= pos)
  const { _timer, _chosen, ...pub } = room
  const candidates = t
    ? CATALOG.tracks.filter((c) => c.albumId !== t.albumId && c.genres[0] === t.genres[0]).slice(0, 3).map(snapshotTrack)
    : []
  return {
    ...pub,
    mediaTimeMs: Math.round(pos),
    bitPerfect: !room.dspEnabled && !!t,
    srcApplied: false,
    pathBadge: pathBadge(t),
    catalogCount: CATALOG.tracks.length,
    heatmap: [],
    currentTrack: t ? snapshotTrack(t) : null,
    album: album ? { id: album.id, title: album.title, artistId: album.artistId, linerNotes: album.linerNotes, label: album.label, year: album.year, credits: album.credits, artworkPath: null } : null,
    artist: artist ? { id: artist.id, name: artist.name, relatedArtistIds: [], bio: artist.bio, alternateNames: [] } : null,
    albumTracks: album ? album.trackIds.map((id) => snapshotTrack(trackById.get(id))) : [],
    relatedArtists: [],
    linerNotes: album?.linerNotes ?? null,
    credits: album?.credits ?? null,
    lyrics,
    currentLyric: line?.text ?? null,
    // 자동 재생 후보는 대기열의 마지막 곡을 듣는 동안에만 나온다.
    autoplay: room.smartAutoplay && candidates.length && room.queueIndex === room.queue.length - 1
      ? { candidates, deadlineUnixMs: room.mediaOriginUnixMs + (room.durationMs - room.mediaTimeAtOriginMs), chosenId: room._chosen ?? null }
      : null,
  }
}

const OUTPUT = (volume = 62) => ({
  peerId: "out-local", displayName: "Mono Output · 이 PC",
  maxSampleRate: 768000, maxBitDepth: 32, supportsDsd: true, exclusiveMode: true,
  reportedLatencyMs: 5.8, hardwareVolume: true, volumePercent: volume,
  device: "RME ADI-2 DAC FS", spectator: false, badge: "WASAPI Exclusive", note: null,
  stats: { offsetMs: 0.02, jitterMs: 0.11, rttMs: 0.4, bufferMs: 180, resyncs: 0, locked: true, deviceState: 1, deviceError: null },
})

// ── 미리 열려 있는 라운지들 ──────────────────────────────────────────────────

function seedLounge({ id, name, host, mode = RoomMode.Open, tracks, at = 0, listeners, chat = [], playing = true }) {
  const hostId = `bot-${id}`
  const room = makeRoom({ id, name, mode, hostPeerId: hostId, hostName: host })
  const r = rng(id)
  const names = ["soyeon", "tubehead", "M. Laurent", "dsd_only", "하늘", "re:verb", "kanta", "Odile", "minjae", "vinylcat", "Aria", "nmos", "june", "플랫", "t.okada", "Wes", "luna", "hi-z", "보라", "Niko"]
  for (let i = 0; i < listeners; i++) {
    const peerId = `${id}-l${i}`
    const nm = names[(i + Math.floor(r() * 5)) % names.length] + (i >= names.length ? `_${i}` : "")
    room.members.push({ peerId, name: nm, role: 0, isOutput: false, spectator: false, stats: { offsetMs: r() * 0.4, jitterMs: r() * 0.3, rttMs: 8 + r() * 60, bufferMs: 180, resyncs: 0, locked: true } })
    room.peers[peerId] = nm
  }
  room.outputs.push({ ...OUTPUT(70), peerId: `${id}-out`, displayName: `${host} · Output`, device: "Holo Audio May KTE" })
  // 셋리스트는 [앨범 번호, 곡 번호] 쌍. 비어 있으면 호스트가 아직 곡을 고르지 않은 방이다.
  room.queue = tracks.map(([a, t]) => slot(room, pick(a, t), hostId))
  room.queueIndex = room.queue.length ? at : -1
  room.durationMs = current(room)?.durationMs ?? 0
  room.playing = playing && room.queue.length > 0
  room.mediaTimeAtOriginMs = Math.round(room.durationMs * (0.2 + r() * 0.4))
  room.mediaOriginUnixMs = now()
  room.mediaTimeMs = room.mediaTimeAtOriginMs
  room.startedAt = iso(now() - (20 + r() * 90) * 60000)
  room.chat = chat.map(([who, text], i) => {
    const m = who === "host" ? room.members[0] : room.members[1 + (i % Math.max(1, listeners))]
    return { peerId: m.peerId, peerName: m.name, text, at: iso(now() - (chat.length - i) * 47000) }
  })
  room.reactionCounts = { "👏": Math.round(r() * 20), "🔥": Math.round(r() * 14), "❤️": Math.round(r() * 30) }
  if (mode === RoomMode.Invite) room.inviteCode = "MONO-7F3K"
  schedule(room)
  return room
}

seedLounge({
  id: "lounge-bluehour", name: "Blue Hour: 하드밥 정주행", host: "Miles K.", tracks: [[0, 1], [3, 2], [17, 0], [2, 2], [1, 0], [0, 3]], listeners: 23,
  chat: [
    ["host", "오늘은 Bluewater 리마스터만 틉니다. 24/96 그대로 나가요."],
    ["x", "베이스 질감이 원반보다 훨씬 또렷하네요"],
    ["x", "HD 800 S로 듣는 중인데 심벌 잔향이 길게 남아요 👏"],
    ["host", "다음 곡은 Dockside Blues. 테너 솔로 2분 10초부터 집중!"],
    ["x", "이 세션 아카이브로 남겨 주세요"],
    ["x", "방금 솔로 핀 꽂았습니다"],
  ],
})
seedLounge({ id: "lounge-dsd", name: "DSD Only Club", host: "dsd_only", mode: RoomMode.Audiophile, tracks: [[5, 2], [2, 0], [5, 4], [2, 4]], listeners: 11 })
seedLounge({ id: "lounge-seoul", name: "서울의 밤, 피아노 트리오", host: "하늘", tracks: [[17, 0], [17, 1], [17, 2], [17, 3]], listeners: 8 })
seedLounge({ id: "lounge-ambient", name: "Quiet House / Ambient", host: "kanta", tracks: [[8, 3], [23, 1], [21, 0], [9, 2]], listeners: 5 })
seedLounge({ id: "lounge-soul", name: "Sunday Soul Kitchen", host: "Odile", tracks: [[15, 2], [14, 0], [12, 3], [15, 4], [10, 0], [14, 3], [15, 0]], listeners: 31 })
seedLounge({ id: "lounge-classics", name: "말러 5번 같이 듣기", host: "re:verb", mode: RoomMode.Invite, tracks: [[6, 3], [4, 0], [5, 2]], listeners: 4 })
seedLounge({ id: "lounge-indie", name: "새 앨범 첫 감상회", host: "minjae", tracks: [], listeners: 2, playing: false })

// ── 접속 ────────────────────────────────────────────────────────────────────

/** @type {Map<import("ws").WebSocket, { peerId: string, name: string, roomId: string | null }>} */
const peers = new Map()
let peerSeq = 1

const send = (ws, msg) => ws.readyState === 1 && ws.send(json(msg))
const stateMsg = (room) => ({ type: "room_state", roomId: room.id, ok: true, body: json(snapshot(room)) })

function broadcast(room) {
  for (const [ws, p] of peers) if (p.roomId === room.id) send(ws, stateMsg(room))
}

function listing() {
  return [...rooms.values()].filter((r) => r.mode !== RoomMode.Solo).map((r) => ({
    id: r.id, name: r.name, mode: r.mode, sourceMode: r.sourceMode, qualityPolicy: r.qualityPolicy,
    playing: r.playing, host: r.peers[r.hostPeerId] ?? r.hostPeerId, needsInvite: r.mode === RoomMode.Invite,
    members: r.members.length, outputs: r.outputs.length, queued: r.queue.length, badge: pathBadge(current(r)),
  }))
}

function leave(ws) {
  const p = peers.get(ws)
  if (!p?.roomId) return
  const room = rooms.get(p.roomId)
  p.roomId = null
  if (!room) return
  room.members = room.members.filter((m) => m.peerId !== p.peerId)
  if (room.hostPeerId === p.peerId && !room.id.startsWith("lounge-")) {
    clearTimeout(room._timer)
    rooms.delete(room.id)
  } else broadcast(room)
}

function albumDetail(albumId) {
  const a = albumById.get(albumId)
  if (!a) return null
  return { id: a.id, title: a.title, artist: a.artist, year: a.year, label: a.label, linerNotes: a.linerNotes, credits: a.credits, tracks: a.trackIds.map((id) => trackView(trackById.get(id))) }
}

function graph(artistId) {
  const artist = artistById.get(artistId)
  if (!artist) return { error: "아티스트를 찾지 못했습니다" }
  const albums = CATALOG.albums.filter((a) => a.artistId === artistId)
  const related = CATALOG.artists.filter((o) => o.id !== artistId && o.genres[0] === artist.genres[0]).slice(0, 6)
  return {
    artist: { id: artist.id, name: artist.name, bio: artist.bio, alternateNames: [] },
    albums: albums.map((a) => ({ id: a.id, title: a.title, year: a.year, label: a.label, credits: a.credits, linerNotes: a.linerNotes, artUrl: `/api/art/${a.trackIds[0]}`, trackCount: a.trackIds.length })),
    tracks: CATALOG.tracks.filter((t) => t.artistId === artistId).map(trackView),
    related: related.map((o) => ({ id: o.id, name: o.name })),
  }
}

function handle(ws, msg) {
  const p = peers.get(ws)
  const room = (msg.roomId && rooms.get(msg.roomId)) || (p?.roomId ? rooms.get(p.roomId) : null)
  const reply = (type, body, extra = {}) => send(ws, { type, ok: true, body: body === undefined ? undefined : json(body), ...extra })
  const touch = () => room && broadcast(room)

  switch (msg.type) {
    case "hello": {
      const peerId = msg.peerId ?? `peer-${peerSeq++}`
      peers.set(ws, { peerId, name: msg.displayName ?? "Listener", roomId: null })
      send(ws, { type: "welcome", peerId, ok: true, body: "Mono Core (mock) · catalog ready" })
      reply("catalog", CATALOG.tracks.map(trackView))
      return
    }

    case "list_rooms": return reply("list_rooms", listing())
    case "catalog": return reply("catalog", CATALOG.tracks.map(trackView))
    case "folders": return reply("folders", [
      { path: "D:\\Music\\Hi-Res", exists: true, trackCount: 112 },
      { path: "D:\\Music\\DSD", exists: true, trackCount: 15 },
      { path: "\\\\NAS\\archive\\classical", exists: true, trackCount: 19 },
    ])
    case "playlists": return reply("playlists", playlists)
    case "history": return reply("history", history)
    case "archives": return reply("archives", archives)
    case "streaming_accounts": return reply("streaming_accounts", accounts)
    case "endpoints": return reply("endpoints", [])

    case "link_streaming": {
      const acc = accounts.find((a) => a.provider === msg.provider)
      if (msg.text === "oauth") {
        return reply("link_streaming", { provider: msg.provider, state: "mock", authUrl: "https://example.invalid/oauth", liveSdk: false, demo: true })
      }
      if (acc) {
        acc.connected = !!msg.token
        acc.displayName = acc.connected ? "gabriel" : null
      }
      return reply("link_streaming", accounts)
    }

    case "search": {
      const q = (msg.text ?? "").toLowerCase().trim()
      const hits = q ? CATALOG.tracks.filter((t) => `${t.title} ${t.artist} ${t.album}`.toLowerCase().includes(q)).slice(0, 60) : []
      return reply("search", hits.map(trackView))
    }
    case "album": return reply("album", albumDetail(msg.text))
    case "graph": return reply("graph", graph(msg.text))
    case "scan_library": return setTimeout(() => reply("catalog", CATALOG.tracks.map(trackView)), 900)

    case "create_playlist": {
      const from = msg.archiveId ? archives.find((a) => a.id === msg.archiveId) : null
      playlists.unshift({
        id: `pl-${Date.now()}`, title: msg.text || from?.roomName || "새 플레이리스트", createdAt: iso(now()), fromArchiveId: from?.id ?? null,
        tracks: (from ? from.tracks.map((t) => t.id) : msg.trackIds ?? []).filter((id) => trackById.has(id)).map(playlistTrack),
      })
      return reply("create_playlist", { ok: true })
    }
    case "add_playlist_track": {
      const pl = playlists.find((x) => x.id === msg.playlistId)
      if (pl && trackById.has(msg.trackId)) pl.tracks.push(playlistTrack(msg.trackId))
      return reply("add_playlist_track", { ok: true })
    }

    case "create_room": {
      leave(ws)
      const r = makeRoom({ name: msg.roomName ?? "Room", mode: msg.mode ?? RoomMode.Open, hostPeerId: p.peerId, hostName: p.name })
      r.outputs.push(OUTPUT())
      p.roomId = r.id
      return send(ws, stateMsg(r))
    }
    case "join_room": {
      const r = rooms.get(msg.roomId)
      if (!r) return send(ws, { type: "error", error: "그 라운지는 이미 닫혔습니다" })
      leave(ws)
      p.roomId = r.id
      if (!r.members.some((m) => m.peerId === p.peerId)) {
        r.members.push({ peerId: p.peerId, name: p.name, role: 0, isOutput: false, spectator: false, stats: null })
        r.peers[p.peerId] = p.name
      }
      return broadcast(r)
    }
    case "leave_room": return leave(ws)
  }

  if (!room) return

  switch (msg.type) {
    case "publish_room":
      room.name = msg.roomName ?? room.name
      room.mode = msg.mode ?? RoomMode.Open
      room.inviteCode = room.mode === RoomMode.Invite ? "MONO-4QX9" : null
      return touch()
    case "close_room": room.mode = RoomMode.Solo; return touch()

    case "enqueue":
      if (trackById.has(msg.trackId)) room.queue.push(slot(room, msg.trackId, p.peerId))
      if (room.queueIndex < 0) load(room, 0, false)
      return touch()
    case "play_list":
      room.queue = (msg.trackIds ?? []).filter((id) => trackById.has(id)).map((id) => slot(room, id, p.peerId))
      load(room, msg.index ?? 0, true)
      return touch()
    case "jump_to": load(room, msg.index ?? 0, true); return touch()
    case "skip": {
      const next = room.queueIndex + (msg.delta ?? 1)
      if (msg.delta < 0 && position(room) > 3000) anchor(room, 0)
      else if (next >= 0 && next < room.queue.length) load(room, next)
      return touch()
    }
    case "play":
      if (!current(room)) return
      room.playing = true
      anchor(room, room.mediaTimeMs)
      return touch()
    case "pause": {
      const at = position(room)
      room.playing = false
      anchor(room, at)
      return touch()
    }
    case "seek": anchor(room, Math.max(0, Math.min(msg.mediaTimeMs ?? 0, room.durationMs))); return touch()
    case "set_shuffle": room.shuffle = !!msg.flag; return touch()
    case "set_repeat": room.repeat = msg.index ?? 0; return touch()

    case "remove_queue": {
      const i = msg.index ?? -1
      if (i < 0 || i >= room.queue.length) return
      room.queue.splice(i, 1)
      if (i < room.queueIndex) room.queueIndex--
      return touch()
    }
    case "move_queue": {
      const from = msg.index ?? -1
      const to = from + (msg.delta ?? 0)
      if (from < 0 || from >= room.queue.length || to < 0 || to >= room.queue.length) return
      const cur = room.queue[room.queueIndex]
      const [item] = room.queue.splice(from, 1)
      room.queue.splice(to, 0, item)
      room.queueIndex = room.queue.indexOf(cur)
      return touch()
    }
    case "clear_queue": {
      const cur = room.queue[room.queueIndex]
      room.queue = cur ? [cur] : []
      room.queueIndex = cur ? 0 : -1
      return touch()
    }
    case "load_playlist": {
      const pl = playlists.find((x) => x.id === msg.playlistId)
      if (!pl) return
      room.queue = pl.tracks.map((t) => slot(room, t.id, p.peerId))
      load(room, 0, true)
      return touch()
    }

    case "set_volume": {
      const out = room.outputs.find((o) => !msg.targetPeerId || o.peerId === msg.targetPeerId)
      if (out) out.volumePercent = msg.volume ?? out.volumePercent
      return touch()
    }
    case "set_dsp":
      // 진짜 Core 와 같다: flag 는 보지 않는다. 프리셋이 Off(0)가 아니면 켜진 것이다.
      if (room.dspLocked) return
      room.dspPreset = msg.dsp ?? 0
      room.dspEnabled = room.dspPreset !== 0
      return touch()
    case "set_policy": room.qualityPolicy = msg.policy ?? 0; return touch()
    case "set_headroom": room.headroomDb = msg.volume ?? room.headroomDb; return touch()
    case "set_room_flags": {
      const field = { seek: "seekingAllowed", comments: "commentsAllowed", chat: "chatCollapsed", queue_lock: "queueLocked", follow_host: "followHostView", auto_advance: "autoAdvance", smart_autoplay: "smartAutoplay", cloud: "cloudSyncOptIn", dsp_lock: "dspLocked" }[msg.text]
      if (field) room[field] = msg.flag ?? !room[field]
      return touch()
    }

    case "chat":
      room.chat.push({ peerId: p.peerId, peerName: p.name, text: msg.text ?? "", at: iso(now()) })
      return touch()
    case "react":
      room.reactionCounts[msg.emoji] = (room.reactionCounts[msg.emoji] ?? 0) + 1
      return touch()
    case "pin": {
      const t = current(room)
      if (!t) return
      room.pins.push({ id: `pin-${now()}`, peerId: p.peerId, peerName: p.name, trackId: t.id, mediaTimeMs: Math.round(position(room)), text: msg.text ?? "", createdAt: iso(now()), onCurrentTrack: true })
      return touch()
    }
    case "choose_autoplay": room._chosen = msg.trackId; return touch()
  }
}

// ── HTTP ────────────────────────────────────────────────────────────────────

function body(req) {
  return new Promise((resolve) => {
    let raw = ""
    req.on("data", (c) => (raw += c))
    req.on("end", () => { try { resolve(JSON.parse(raw || "{}")) } catch { resolve({}) } })
  })
}

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, "http://x")
  const path = decodeURIComponent(url.pathname)
  const out = (code, value, type = "application/json; charset=utf-8") => {
    res.writeHead(code, { "content-type": type, "cache-control": "no-store", "access-control-allow-origin": "*" })
    res.end(typeof value === "string" ? value : json(value))
  }

  if (path.startsWith("/api/art/")) {
    const track = trackById.get(path.slice("/api/art/".length))
    if (!track) return out(404, { error: "not found" })
    res.writeHead(200, { "content-type": "image/svg+xml", "cache-control": "public, max-age=3600" })
    return res.end(coverSvg(albumById.get(track.albumId)))
  }
  if (path === "/api/setup") {
    if (req.method === "PUT") setup = { ...setup, ...(await body(req)), savedAt: iso(now()) }
    if (req.method === "DELETE") setup = { completed: false }
    // 캡처 스크립트가 온보딩 화면을 찍은 뒤 원래대로 돌려놓을 때 쓴다.
    if (req.method === "POST") setup = structuredClone(DEFAULT_SETUP)
    return out(200, setup)
  }
  // 진짜 Core 의 GET /api/rooms/{id} 와 같다. 라운지 표지가 이걸로 지금 곡과 다음 곡을 읽는다.
  if (path.startsWith("/api/rooms/")) {
    const room = rooms.get(path.slice("/api/rooms/".length))
    return room ? out(200, snapshot(room)) : out(404, { error: "not found" })
  }
  if (path === "/api/health") return out(200, { ok: true, product: "Mono Core (mock)", rooms: rooms.size, tracks: T.length, endpoints: 1, controlPort: 7700, matpPort: 7701 })
  if (path === "/api/catalog") return out(200, CATALOG.tracks.map(trackView))
  if (path === "/api/endpoints") return out(200, [])
  if (path === "/api/archives") return out(200, archives)
  if (path === "/api/playlists") return out(200, playlists)
  if (path.startsWith("/api/reactions/")) {
    const r = rng(path)
    return out(200, Array.from({ length: 24 }, (_, i) => ({ trackId: path.slice(15), bucketMs: i * 10000, count: Math.round(r() * r() * 12) })))
  }
  if (path.startsWith("/api/wiki/")) {
    const artist = artistById.get(path.slice("/api/wiki/".length))
    if (!artist?.bio) return out(404, { error: "not found" })
    // Mono.Shared.ArtistWikiSummary 와 같은 모양.
    return out(200, { artistId: artist.id, title: artist.name, extract: artist.bio, sourceUrl: "https://ko.wikipedia.org/", thumbnailUrl: null, lang: "ko" })
  }
  out(404, { error: "not found" })
})

const wss = new WebSocketServer({ noServer: true })
server.on("upgrade", (req, socket, head) => {
  if (!req.url.startsWith("/ws/control")) return socket.destroy()
  wss.handleUpgrade(req, socket, head, (ws) => {
    ws.on("message", (data) => {
      let msg
      try { msg = JSON.parse(String(data)) } catch { return }
      try { handle(ws, msg) } catch (err) { console.error("[mock-core]", msg.type, err) }
    })
    ws.on("close", () => { leave(ws); peers.delete(ws) })
  })
})

server.listen(PORT, "127.0.0.1", () => console.log(`[mock-core] http://127.0.0.1:${PORT}  ·  ${T.length} tracks, ${rooms.size} lounges`))
