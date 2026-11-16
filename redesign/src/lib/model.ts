// 화면이 쓰는 뷰 모델. 카탈로그(CatalogTrack[])에서 앨범·아티스트·작곡가를 접어 만든다.
//
// 이전 UI 는 제목 문자열로 곡과 앨범을 되찾았다(findTrackId(title, artist)). 같은 제목의
// 곡이 둘이면 엉뚱한 쪽이 재생된다. 여기서는 처음부터 끝까지 id 를 들고 다닌다.

import { RoomMode, StreamingProvider, type CatalogTrack, type HistoryEntry, type RoomSnapshot } from "./protocol"

// ── 표시 형식 ────────────────────────────────────────────────────────────────

export function fmtTime(ms: number | null | undefined): string {
  if (!ms || ms <= 0) return "0:00"
  const total = Math.floor(ms / 1000)
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  return h > 0 ? `${h}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}` : `${m}:${String(s).padStart(2, "0")}`
}

/** "1시간 12분" 같은 긴 길이 표기. 앨범·플레이리스트 합계에 쓴다. */
export function fmtLong(ms: number): string {
  const min = Math.round(ms / 60000)
  if (min < 60) return `${min}분`
  const h = Math.floor(min / 60)
  const m = min % 60
  return m ? `${h}시간 ${m}분` : `${h}시간`
}

export function initials(name: string | null | undefined): string {
  const parts = (name ?? "").trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) return "?"
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
}

export function relativeDay(iso: string | null | undefined): string {
  if (!iso) return ""
  const diff = Date.now() - new Date(iso).getTime()
  const day = Math.floor(diff / 86400000)
  if (day <= 0) return "오늘"
  if (day === 1) return "어제"
  if (day < 7) return `${day}일 전`
  if (day < 35) return `${Math.floor(day / 7)}주 전`
  return new Date(iso).toLocaleDateString("ko-KR", { year: "numeric", month: "short" })
}

// ── 음질 ────────────────────────────────────────────────────────────────────

export type Tier = "dsd" | "hires" | "cd" | "lossy"

export interface Quality {
  tier: Tier
  /** "24/96", "16/44.1", "DSD128" */
  res: string
  /** "FLAC", "DSF", "TIDAL" … 컨테이너를 모르면 빈 문자열 */
  container: string
}

interface QualitySource {
  isDsd?: boolean
  dsdRate?: number | null
  bitDepth?: number
  sampleRate?: number
  hasLocal?: boolean
  fileExtension?: string | null
  source?: StreamingProvider
}

export function sourceName(source: StreamingProvider | undefined): string {
  return source === StreamingProvider.Tidal ? "TIDAL" : source === StreamingProvider.Qobuz ? "Qobuz" : "로컬"
}

export function qualityOf(t: QualitySource): Quality {
  const container = t.hasLocal || t.source === StreamingProvider.Local || t.source === undefined
    ? (t.fileExtension ?? "").toUpperCase()
    : sourceName(t.source).toUpperCase()

  if (t.isDsd) return { tier: "dsd", res: `DSD${t.dsdRate ?? 64}`, container }

  const khz = t.sampleRate ? String(Math.round(t.sampleRate / 100) / 10) : ""
  const res = t.bitDepth && khz ? `${t.bitDepth}/${khz}` : ""
  const lossy = ["MP3", "AAC", "M4A", "OGG"].includes(container)
  const tier: Tier = lossy ? "lossy" : (t.bitDepth ?? 0) >= 24 || (t.sampleRate ?? 0) >= 88200 ? "hires" : "cd"
  return { tier, res, container }
}

export const TIER_LABEL: Record<Tier, string> = { dsd: "DSD", hires: "Hi-Res", cd: "CD 음질", lossy: "손실 압축" }

// ── 앨범 · 아티스트 · 작곡가 ─────────────────────────────────────────────────

export interface Album {
  id: string
  title: string
  artist: string
  artistId: string
  year: number | null
  label: string
  art: string | null
  tracks: CatalogTrack[]
  trackIds: string[]
  quality: Quality
  /** 수록곡 음질이 섞여 있으면 true — 카드에는 대표 음질만 쓰고 상세에서 곡별로 보여 준다. */
  mixed: boolean
  genres: string[]
  source: StreamingProvider
  durationMs: number
  addedAt: string | null
}

export interface Artist {
  id: string
  name: string
  art: string | null
  albumCount: number
  trackCount: number
  genres: string[]
}

export interface Composer {
  name: string
  art: string | null
  works: number
  trackIds: string[]
  genre: string
}

export function buildAlbums(catalog: CatalogTrack[]): Album[] {
  const groups = new Map<string, CatalogTrack[]>()
  for (const t of catalog) {
    const key = t.albumId || `${t.artistId}::${t.album ?? ""}`
    const bucket = groups.get(key)
    if (bucket) bucket.push(t)
    else groups.set(key, [t])
  }

  const out: Album[] = []
  for (const [key, tracks] of groups) {
    const first = tracks[0]
    const quals = tracks.map(qualityOf)
    const added = tracks.map((t) => t.addedAt).filter((d): d is string => !!d).sort()
    out.push({
      id: first.albumId || key,
      title: first.album || "제목 없는 앨범",
      artist: first.artist || "알 수 없는 아티스트",
      artistId: first.artistId,
      year: first.year ?? null,
      label: first.label ?? "",
      art: tracks.find((t) => t.artUrl)?.artUrl ?? null,
      tracks,
      trackIds: tracks.map((t) => t.id),
      quality: quals[0],
      mixed: new Set(quals.map((q) => `${q.container} ${q.res}`)).size > 1,
      genres: [...new Set(tracks.flatMap((t) => t.genres))],
      source: first.source,
      durationMs: tracks.reduce((sum, t) => sum + t.durationMs, 0),
      addedAt: added[0] ?? null,
    })
  }
  return out
}

export function buildArtists(catalog: CatalogTrack[]): Artist[] {
  const map = new Map<string, { name: string; albums: Set<string>; tracks: number; art: string | null; genres: Set<string> }>()
  for (const t of catalog) {
    const key = t.artistId || t.artist || "unknown"
    const entry = map.get(key) ?? { name: t.artist || "알 수 없는 아티스트", albums: new Set<string>(), tracks: 0, art: null, genres: new Set<string>() }
    entry.albums.add(t.albumId)
    entry.tracks += 1
    entry.art ??= t.artUrl ?? null
    for (const g of t.genres) entry.genres.add(g)
    map.set(key, entry)
  }
  return [...map.entries()]
    .map(([id, v]) => ({ id, name: v.name, art: v.art, albumCount: v.albums.size, trackCount: v.tracks, genres: [...v.genres] }))
    .sort((a, b) => a.name.localeCompare(b.name, "ko"))
}

export function buildComposers(catalog: CatalogTrack[]): Composer[] {
  const map = new Map<string, { works: Set<string>; trackIds: string[]; art: string | null; genre: string }>()
  for (const t of catalog) {
    for (const raw of t.composers) {
      const name = raw.trim()
      if (!name) continue
      const entry = map.get(name) ?? { works: new Set<string>(), trackIds: [], art: null, genre: t.genres[0] ?? "" }
      entry.works.add(t.workKey ?? t.albumId)
      entry.trackIds.push(t.id)
      entry.art ??= t.artUrl ?? null
      map.set(name, entry)
    }
  }
  return [...map.entries()]
    .map(([name, v]) => ({ name, art: v.art, works: v.works.size, trackIds: v.trackIds, genre: v.genre }))
    .sort((a, b) => b.trackIds.length - a.trackIds.length)
}

export function topGenres(catalog: CatalogTrack[], limit = 6): { name: string; count: number }[] {
  const counts = new Map<string, number>()
  for (const t of catalog) {
    const g = t.genres[0]?.trim()
    if (g) counts.set(g, (counts.get(g) ?? 0) + 1)
  }
  return [...counts.entries()].sort((a, b) => b[1] - a[1]).slice(0, limit).map(([name, count]) => ({ name, count }))
}

// ── 통계 ────────────────────────────────────────────────────────────────────

export interface LibraryStats {
  tracks: number
  albums: number
  artists: number
  durationMs: number
  tiers: Record<Tier, number>
  weekPlays: number
}

export function libraryStats(catalog: CatalogTrack[], albums: Album[], history: HistoryEntry[]): LibraryStats {
  const tiers: Record<Tier, number> = { dsd: 0, hires: 0, cd: 0, lossy: 0 }
  let durationMs = 0
  for (const t of catalog) {
    tiers[qualityOf(t).tier] += 1
    durationMs += t.durationMs
  }
  const weekAgo = Date.now() - 7 * 86400000
  return {
    tracks: catalog.length,
    albums: albums.length,
    artists: new Set(catalog.map((t) => t.artistId)).size,
    durationMs,
    tiers,
    weekPlays: history.filter((h) => new Date(h.heardAt).getTime() >= weekAgo).length,
  }
}

export function mostPlayed(history: HistoryEntry[], byId: Map<string, CatalogTrack>, limit = 6): { track: CatalogTrack; plays: number }[] {
  const counts = new Map<string, number>()
  for (const h of history) counts.set(h.trackId, (counts.get(h.trackId) ?? 0) + 1)
  return [...counts.entries()]
    .sort((a, b) => b[1] - a[1])
    .map(([id, plays]) => ({ track: byId.get(id), plays }))
    .filter((r): r is { track: CatalogTrack; plays: number } => !!r.track)
    .slice(0, limit)
}

/** 이력에서 최근에 들은 앨범 순서. 같은 앨범은 한 번만. */
export function recentAlbumIds(history: HistoryEntry[], byId: Map<string, CatalogTrack>): string[] {
  const seen = new Set<string>()
  const sorted = [...history].sort((a, b) => b.heardAt.localeCompare(a.heardAt))
  for (const h of sorted) {
    const albumId = byId.get(h.trackId)?.albumId
    if (albumId) seen.add(albumId)
  }
  return [...seen]
}

// ── 세션 ────────────────────────────────────────────────────────────────────

/**
 * 내가 지금 어떤 자리에 있는가. 룸 스냅샷에서 바로 계산한다.
 *
 * 이전 UI 는 이걸 로컬 state(playerMode)로 따로 들고 있어서, 창을 새로 열면 호스트가
 * 솔로로 돌아가 있었다. 룸이 진실이면 따로 기억할 것이 없다.
 */
export type SessionRole = "idle" | "solo" | "host" | "guest"

export function sessionRole(room: RoomSnapshot | null, peerId: string | null): SessionRole {
  if (!room) return "idle"
  if (room.mode === RoomMode.Solo) return "solo"
  return room.hostPeerId === peerId ? "host" : "guest"
}

export const ROOM_MODE_LABEL: Record<number, string> = {
  [RoomMode.Open]: "공개",
  [RoomMode.Invite]: "초대 전용",
  [RoomMode.HostQueue]: "호스트 큐",
  [RoomMode.Audiophile]: "오디오파일",
  [RoomMode.Solo]: "솔로",
}

/** 이름에서 고정된 색 하나. 아바타 배경에 쓴다 — 같은 사람은 늘 같은 색이다. */
export function hueOf(name: string): number {
  let h = 0
  for (const ch of name) h = (h * 31 + ch.charCodeAt(0)) % 360
  return h
}
