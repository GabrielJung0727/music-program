// Core 의 자료구조를 디자인이 이미 쓰고 있는 모양으로 옮긴다.
// 컴포넌트를 고치는 대신 여기서 맞춰 주는 쪽이, 디자인 원본과의 차이를 작게 유지한다.

import type { QueueTrack, SearchAlbum, SearchTrack, LoungeRoom } from "../data/types"
import {
  StreamingProvider,
  type AlbumDetail,
  type CatalogTrack,
  type RoomListing,
  type RoomSnapshot,
  type SnapshotQueueItem,
  type SnapshotTrack,
} from "./protocol"

export function formatDuration(ms: number | null | undefined): string {
  if (!ms || ms <= 0) return "0:00"
  const total = Math.round(ms / 1000)
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  return h > 0
    ? `${h}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`
    : `${m}:${String(s).padStart(2, "0")}`
}

/** "FLAC 24-Bit / 192kHz" 같은 한 줄 스펙. 디자인 전반이 이 형식을 쓴다. */
export function formatSpec(t: {
  isDsd?: boolean
  dsdRate?: number | null
  bitDepth?: number
  sampleRate?: number
  hasLocal?: boolean
  source?: StreamingProvider
}): string {
  if (t.isDsd) return `DSD ${t.dsdRate ?? 64}`
  const khz = t.sampleRate ? (t.sampleRate / 1000).toFixed(t.sampleRate % 1000 === 0 ? 0 : 1) : null
  const container = t.source === StreamingProvider.Local || t.hasLocal ? "FLAC" : "Hi-Res"
  if (!t.bitDepth || !khz) return container
  return `${container} ${t.bitDepth}-Bit / ${khz}kHz`
}

export function sourceOf(source: StreamingProvider | undefined): QueueTrack["source"] {
  switch (source) {
    case StreamingProvider.Tidal:
      return "tidal"
    case StreamingProvider.Qobuz:
      return "qobuz"
    default:
      return "local"
  }
}

/**
 * Core 의 artUrl 은 "/api/art/{id}" 상대 경로이고, 아트가 없는 트랙에는 null 이다.
 * 없을 때 URL 을 지어내면 화면마다 확정 404 요청이 쏟아진다 — 빈 문자열로 두고
 * UI 의 플레이스홀더에 맡긴다.
 */
function art(url: string | null | undefined, _trackId?: string): string {
  return url ?? ""
}

// ── 트랙 ──────────────────────────────────────────────────────────────────────

export function catalogTrackToQueueTrack(t: CatalogTrack): QueueTrack {
  return {
    id: t.id,
    title: t.title,
    artist: t.artist ?? "Unknown Artist",
    album: t.album ?? "",
    duration: formatDuration(t.durationMs),
    format: formatSpec(t),
    dr: t.badge ?? undefined,
    art: art(t.artUrl, t.id),
    source: sourceOf(t.source),
  }
}

export function snapshotTrackToQueueTrack(t: SnapshotTrack): QueueTrack {
  return {
    id: t.id,
    title: t.title,
    artist: t.artistName ?? "Unknown Artist",
    album: t.albumTitle ?? "",
    duration: formatDuration(t.durationMs),
    format: formatSpec(t),
    dr: t.badge ?? undefined,
    art: art(t.artUrl, t.id),
    source: sourceOf(t.source),
  }
}

/**
 * 큐 항목. id 는 큐 슬롯 id(제거·이동에 쓴다), trackId 는 곡 id 다.
 * 디자인의 QueueTrack 에는 슬롯 개념이 없어 trackId 를 별도 필드로 덧붙인다.
 */
export interface LiveQueueTrack extends QueueTrack {
  trackId: string
  queueIndex: number
}

export function queueItemToTrack(q: SnapshotQueueItem, index: number): LiveQueueTrack {
  return {
    id: q.id,
    trackId: q.trackId,
    queueIndex: index,
    title: q.title,
    artist: q.artist ?? "Unknown Artist",
    album: "",
    duration: formatDuration(q.durationMs),
    format: q.badge ?? "",
    dr: q.badge ?? undefined,
    art: art(q.artUrl, q.trackId),
    source: "local",
  }
}

// ── 앨범 · 검색 ───────────────────────────────────────────────────────────────

function catalogTrackToSearchTrack(t: CatalogTrack, currentId?: string | null): SearchTrack {
  return {
    title: t.title,
    artist: t.artist ?? "Unknown Artist",
    album: t.album ?? "",
    duration: formatDuration(t.durationMs),
    dr: t.badge ?? "",
    isCurrent: currentId != null && t.id === currentId,
  }
}

/** 디자인의 SearchAlbum 에는 곡 id 가 없다. 재생하려면 id 가 필요해 확장한다. */
export interface LiveAlbum extends SearchAlbum {
  id: string
  trackIds: string[]
  artistId?: string
}

/** 카탈로그 트랙 목록을 앨범 단위로 접는다. Core 는 앨범 목록 API 를 따로 주지 않는다. */
export function groupIntoAlbums(tracks: CatalogTrack[], currentTrackId?: string | null): LiveAlbum[] {
  const byAlbum = new Map<string, CatalogTrack[]>()
  for (const t of tracks) {
    const key = t.albumId || `${t.artistId}::${t.album ?? ""}`
    const bucket = byAlbum.get(key)
    if (bucket) bucket.push(t)
    else byAlbum.set(key, [t])
  }

  const albums: LiveAlbum[] = []
  for (const [key, group] of byAlbum) {
    const first = group[0]
    const withArt = group.find((t) => t.artUrl) ?? first
    albums.push({
      id: first.albumId || key,
      artistId: first.artistId,
      trackIds: group.map((t) => t.id),
      title: first.album ?? "Unknown Album",
      artist: first.artist ?? "Unknown Artist",
      year: first.year ? String(first.year) : "",
      label: first.label ?? "",
      studio: "",
      coverUrl: art(withArt.artUrl, withArt.id),
      format: formatSpec(first),
      dr: first.badge ?? "",
      wikiUrl: "",
      wikiSummary: "",
      tracks: group.map((t) => catalogTrackToSearchTrack(t, currentTrackId)),
    })
  }

  return albums.sort((a, b) => a.artist.localeCompare(b.artist) || a.title.localeCompare(b.title))
}

export function albumDetailToLiveAlbum(detail: AlbumDetail, currentTrackId?: string | null): LiveAlbum {
  const withArt = detail.tracks.find((t) => t.artUrl) ?? detail.tracks[0]
  return {
    id: detail.id,
    artistId: detail.tracks[0]?.artistId,
    trackIds: detail.tracks.map((t) => t.id),
    title: detail.title,
    artist: detail.artist ?? "Unknown Artist",
    year: detail.year ? String(detail.year) : "",
    label: detail.label ?? "",
    studio: "",
    coverUrl: withArt ? art(withArt.artUrl, withArt.id) : "",
    format: detail.tracks[0] ? formatSpec(detail.tracks[0]) : "",
    dr: detail.tracks[0]?.badge ?? "",
    wikiUrl: "",
    wikiSummary: detail.linerNotes ?? "",
    tracks: detail.tracks.map((t) => catalogTrackToSearchTrack(t, currentTrackId)),
  }
}

// ── 라운지 ────────────────────────────────────────────────────────────────────

const CATEGORIES: LoungeRoom["category"][] = ["popular", "indie", "cozy", "debut"]

/** 룸 목록을 라운지 카드 모양으로. 청취자 수로 카테고리를 정한다. */
export function roomListingToLounge(r: RoomListing, index: number): LoungeRoom {
  const listeners = r.members + r.outputs
  const category: LoungeRoom["category"] =
    listeners >= 20 ? "popular" : listeners >= 8 ? "indie" : listeners >= 3 ? "cozy" : "debut"

  return {
    id: r.id,
    title: r.name,
    hostName: r.host,
    hostAvatar: initials(r.host),
    currentTrackTitle: r.playing ? "재생 중" : "대기 중",
    currentArtist: "",
    audioSpec: r.badge ?? "",
    listenerCount: listeners,
    badge: listeners >= 20 ? "trending" : undefined,
    category: category ?? CATEGORIES[index % CATEGORIES.length],
    art: "",
    visibility: r.needsInvite ? "unlisted" : "public",
  }
}

/**
 * 내가 들어와 있는 룸은 스냅샷이 훨씬 많은 걸 안다 — 목록보다 이쪽을 우선한다.
 */
export function snapshotToLounge(s: RoomSnapshot): LoungeRoom {
  const hostName = (s.hostPeerId && s.peers[s.hostPeerId]) || s.hostPeerId || "호스트"
  const listeners = s.members.filter((m) => !m.isOutput).length
  return {
    id: s.id,
    title: s.name,
    hostName,
    hostAvatar: initials(hostName),
    currentTrackTitle: s.currentTrack?.title ?? "대기 중",
    currentArtist: s.currentTrack?.artistName ?? "",
    audioSpec: s.currentTrack ? formatSpec(s.currentTrack) : (s.pathBadge ?? ""),
    listenerCount: listeners,
    badge: listeners >= 20 ? "trending" : undefined,
    category: listeners >= 20 ? "popular" : listeners >= 8 ? "indie" : listeners >= 3 ? "cozy" : "debut",
    art: s.currentTrack ? art(s.currentTrack.artUrl, s.currentTrack.id) : "",
    visibility: s.mode === 1 ? "unlisted" : "public",
    secretKey: s.inviteCode ?? undefined,
  }
}

export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) return "??"
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
}
