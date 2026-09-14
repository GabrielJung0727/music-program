// MyLibraryPage 가 요구하는 목록들을 카탈로그에서 만든다.
// 디자인의 LIB_* 목업과 같은 필드 이름을 유지해 컴포넌트 본문을 그대로 둔다.

import { formatDuration, formatSpec, type LiveAlbum } from "./adapters"
import type { CatalogTrack, PlaylistView } from "./protocol"

export type LibraryFormat = "hires" | "dsd" | "vinyl" | "standard"

export interface LibAlbum {
  id: string
  art: string
  coverUrl: string
  title: string
  artist: string
  spec: string
  format: LibraryFormat
  trackIds: string[]
}

export interface LibArtist {
  id: string
  art: string
  name: string
  count: string
}

export interface LibComposer {
  art: string
  name: string
  era: string
  works: string
  trackIds: string[]
}

export interface LibTrack {
  id: string
  num: number
  title: string
  artist: string
  album: string
  year: number
  format: string
  dr: number
  time: string
  art: string
}

function classify(t: CatalogTrack): LibraryFormat {
  if (t.isDsd) return "dsd"
  if (t.bitDepth >= 24 || t.sampleRate >= 88200) return "hires"
  return "standard"
}

export function libAlbums(albums: LiveAlbum[], catalog: CatalogTrack[]): LibAlbum[] {
  const byId = new Map(catalog.map((t) => [t.id, t]))
  return albums.map((a) => {
    const first = byId.get(a.trackIds[0])
    return {
      id: a.id,
      art: a.coverUrl,
      coverUrl: a.coverUrl,
      title: a.title,
      artist: a.artist,
      spec: [a.format, a.dr].filter(Boolean).join(" • "),
      format: first ? classify(first) : "standard",
      trackIds: a.trackIds,
    }
  })
}

export function libArtists(catalog: CatalogTrack[]): LibArtist[] {
  const byArtist = new Map<string, { name: string; albums: Set<string>; tracks: number; art: string }>()
  for (const t of catalog) {
    const key = t.artistId || t.artist || "unknown"
    const entry = byArtist.get(key) ?? {
      name: t.artist ?? "Unknown Artist",
      albums: new Set<string>(),
      tracks: 0,
      art: "",
    }
    entry.albums.add(t.albumId)
    entry.tracks += 1
    if (!entry.art && t.artUrl) entry.art = t.artUrl
    byArtist.set(key, entry)
  }

  return [...byArtist.entries()]
    .map(([id, v]) => ({
      id,
      art: v.art,
      name: v.name,
      count: `${v.albums.size} Albums • ${v.tracks} Tracks`,
    }))
    .sort((a, b) => a.name.localeCompare(b.name))
}

export function libComposers(catalog: CatalogTrack[]): LibComposer[] {
  const byComposer = new Map<string, { works: Set<string>; tracks: string[]; art: string; genres: Set<string> }>()
  for (const t of catalog) {
    for (const raw of t.composers) {
      const name = raw.trim()
      if (!name) continue
      const entry = byComposer.get(name) ?? {
        works: new Set<string>(),
        tracks: [] as string[],
        art: "",
        genres: new Set<string>(),
      }
      entry.works.add(t.workKey ?? t.albumId)
      entry.tracks.push(t.id)
      if (!entry.art && t.artUrl) entry.art = t.artUrl
      for (const g of t.genres) entry.genres.add(g)
      byComposer.set(name, entry)
    }
  }

  return [...byComposer.entries()]
    .map(([name, v]) => ({
      art: v.art,
      name,
      // Core 는 시대 구분을 모른다. 장르 태그가 가장 가까운 대체물이다.
      era: [...v.genres][0] ?? "—",
      works: `${v.works.size} Works • ${v.tracks.length} Recordings`,
      trackIds: v.tracks,
    }))
    .sort((a, b) => b.trackIds.length - a.trackIds.length)
}

export function libTracks(catalog: CatalogTrack[]): LibTrack[] {
  return catalog.map((t, i) => ({
    id: t.id,
    num: i + 1,
    title: t.title,
    artist: t.artist ?? "Unknown Artist",
    album: t.album ?? "",
    year: t.year ?? 0,
    format: formatSpec(t),
    dr: Number.parseInt((t.badge ?? "").replace(/\D/g, ""), 10) || 0,
    time: formatDuration(t.durationMs),
    art: t.artUrl ?? "",
  }))
}

export interface LibPlaylist {
  id: string
  name: string
  description: string
  trackCount: number
  duration: string
  art: string
  arts: string[]
  tracks: { id: string; title: string }[]
  dr: string
  spec: string
}

export function libPlaylists(playlists: PlaylistView[], catalog: CatalogTrack[]): LibPlaylist[] {
  const byId = new Map(catalog.map((t) => [t.id, t]))

  return playlists.map((p) => {
    const tracks = p.tracks.map((t) => byId.get(t.id)).filter((t): t is CatalogTrack => !!t)
    const totalMs = tracks.reduce((sum, t) => sum + t.durationMs, 0)
    const drs = tracks
      .map((t) => Number.parseInt((t.badge ?? "").replace(/\D/g, ""), 10))
      .filter((n) => Number.isFinite(n) && n > 0)
    const arts = tracks
      .map((t) => t.artUrl ?? "")
      .filter(Boolean)
      .filter((a, i, all) => all.indexOf(a) === i)
      .slice(0, 4)

    return {
      id: p.id,
      name: p.title,
      description: `${p.tracks.length} Tracks • ${formatDuration(totalMs)}`,
      trackCount: p.tracks.length,
      duration: formatDuration(totalMs),
      art: arts[0] ?? "",
      arts: arts.length > 0 ? arts : [""],
      tracks: p.tracks,
      dr: drs.length > 0 ? `Avg. DR${Math.round(drs.reduce((a, b) => a + b, 0) / drs.length)}` : "",
      spec: tracks[0] ? formatSpec(tracks[0]) : "",
    }
  })
}
