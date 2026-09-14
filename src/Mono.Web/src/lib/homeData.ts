// 홈 화면의 섹션들은 Core 가 따로 API 를 주지 않는다.
// 카탈로그와 청취 이력에서 여기서 집계한다.

import type { ArchiveView, CatalogTrack, HistoryEntry } from "./protocol"
import { formatSpec, type LiveAlbum } from "./adapters"

/** HomePage 의 앨범 카드가 요구하는 모양. */
export interface HomeAlbum extends LiveAlbum {
  art: string
  plays: number
  added: string
}

export function homeAlbums(
  albums: LiveAlbum[],
  catalog: CatalogTrack[],
  history: HistoryEntry[],
): HomeAlbum[] {
  const playsByTrack = new Map<string, number>()
  for (const h of history) {
    playsByTrack.set(h.trackId, (playsByTrack.get(h.trackId) ?? 0) + 1)
  }

  const addedByTrack = new Map<string, string>()
  for (const t of catalog) {
    if (t.addedAt) addedByTrack.set(t.id, t.addedAt)
  }

  return albums.map((a) => {
    const plays = a.trackIds.reduce((sum, id) => sum + (playsByTrack.get(id) ?? 0), 0)
    // 앨범의 "추가된 날"은 수록곡 중 가장 이른 날이다.
    const dates = a.trackIds.map((id) => addedByTrack.get(id)).filter((d): d is string => !!d).sort()
    return {
      ...a,
      art: a.coverUrl,
      plays,
      added: dates[0]?.slice(0, 10) ?? "",
    }
  })
}

/** 장르 태그에서 필터 칩을 만든다. 앞의 "All (n)" 은 항상 붙인다. */
export function filterChips(catalog: CatalogTrack[]): string[] {
  const counts = new Map<string, number>()
  for (const t of catalog) {
    for (const g of t.genres) {
      const name = g.trim()
      if (name) counts.set(name, (counts.get(name) ?? 0) + 1)
    }
  }

  const top = [...counts.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, 5)
    .map(([name]) => name)

  return [`All (${catalog.length.toLocaleString()})`, ...top, "Hi-Res 24-Bit+", "DSD Pure"]
}

export interface InsightCard {
  title: string
  value: string
  sub: string
  valueColor: string
  icon: string | null
  dot: string | null
}

/** 카탈로그·이력에서 뽑은 주간 지표. 값이 없으면 "—" 로 비워 둔다. */
export function insightCards(catalog: CatalogTrack[], history: HistoryEntry[]): InsightCard[] {
  const total = catalog.length
  const hiRes = catalog.filter((t) => t.isDsd || t.bitDepth >= 24 || t.sampleRate >= 88200).length
  const hiResRatio = total > 0 ? `${((hiRes / total) * 100).toFixed(1)}%` : "—"

  // DR 배지는 "DR 14" 꼴이다. 숫자만 뽑아 평균을 낸다.
  const drValues = catalog
    .map((t) => Number.parseInt((t.badge ?? "").replace(/\D/g, ""), 10))
    .filter((n) => Number.isFinite(n) && n > 0 && n < 30)
  const avgDr =
    drValues.length > 0
      ? `DR ${(drValues.reduce((a, b) => a + b, 0) / drValues.length).toFixed(1)}`
      : "—"

  const localCount = catalog.filter((t) => t.hasLocal).length
  const bitPerfect = total > 0 ? `${Math.round((localCount / total) * 100)}%` : "—"

  const labelCounts = new Map<string, number>()
  for (const t of catalog) {
    const label = t.label?.trim()
    if (label) labelCounts.set(label, (labelCounts.get(label) ?? 0) + 1)
  }
  const topLabel = [...labelCounts.entries()].sort((a, b) => b[1] - a[1])[0]

  const weekAgo = Date.now() - 7 * 24 * 60 * 60 * 1000
  const recentPlays = history.filter((h) => new Date(h.heardAt).getTime() >= weekAgo).length

  return [
    {
      title: "Hi-Res Ratio",
      value: hiResRatio,
      sub: `24-Bit / 96k+ · ${hiRes.toLocaleString()}곡`,
      valueColor: "#059669",
      icon: "trend-up",
      dot: null,
    },
    {
      title: "Avg. Dynamics",
      value: avgDr,
      sub: drValues.length > 0 ? `${drValues.length.toLocaleString()}곡 측정됨` : "측정된 곡이 없습니다",
      valueColor: "var(--text-primary)",
      icon: "wave",
      dot: null,
    },
    {
      title: "Local Masters",
      value: bitPerfect,
      sub: `로컬 파일 ${localCount.toLocaleString()} / 전체 ${total.toLocaleString()}`,
      valueColor: "var(--text-primary)",
      icon: null,
      dot: "#059669",
    },
    {
      title: "Top Label",
      value: topLabel?.[0] ?? "—",
      sub: topLabel ? `${topLabel[1].toLocaleString()}곡 · 이번 주 ${recentPlays}회 재생` : "레이블 정보 없음",
      valueColor: "var(--text-primary)",
      icon: "tag",
      dot: null,
    },
  ]
}

export interface LabelTile {
  name: string
  subtitle: string
  count: string
  gradFrom: string
  gradTo: string
  accent: string
}

const LABEL_PALETTE = [
  { gradFrom: "#F8FAFC", gradTo: "#EFF6FF", accent: "#2563EB" },
  { gradFrom: "#FFFBEB", gradTo: "#FEF3C7", accent: "#92400E" },
  { gradFrom: "#F5F3FF", gradTo: "#EDE9FE", accent: "#6D28D9" },
]

/** 보유 곡이 가장 많은 레이블 셋. */
export function labelTiles(catalog: CatalogTrack[]): LabelTile[] {
  const byLabel = new Map<string, Set<string>>()
  for (const t of catalog) {
    const label = t.label?.trim()
    if (!label) continue
    const albums = byLabel.get(label) ?? new Set<string>()
    albums.add(t.albumId)
    byLabel.set(label, albums)
  }

  return [...byLabel.entries()]
    .sort((a, b) => b[1].size - a[1].size)
    .slice(0, 3)
    .map(([name, albums], i) => ({
      name,
      subtitle: `${name} 아카이브`,
      count: `${albums.size.toLocaleString()} albums`,
      ...LABEL_PALETTE[i % LABEL_PALETTE.length],
    }))
}

/** 라운지의 "가장 많이 재생된" 순위. */
export function mostPlayed(
  history: HistoryEntry[],
  catalog: CatalogTrack[],
): { rank: number; track: string; artist: string; plays: number; dr: string }[] {
  const counts = new Map<string, { plays: number; title: string; artist: string }>()
  for (const h of history) {
    const prev = counts.get(h.trackId)
    if (prev) prev.plays += 1
    else counts.set(h.trackId, { plays: 1, title: h.title, artist: h.artist ?? "" })
  }

  const badges = new Map(catalog.map((t) => [t.id, t.badge ?? ""]))

  return [...counts.entries()]
    .sort((a, b) => b[1].plays - a[1].plays)
    .slice(0, 6)
    .map(([trackId, v], i) => ({
      rank: i + 1,
      track: v.title,
      artist: v.artist,
      plays: v.plays,
      dr: badges.get(trackId) ?? "",
    }))
}

/** 종료된 세션을 Hall of Fame 카드 모양으로. */
export function hallOfFame(
  archives: ArchiveView[],
  catalog: CatalogTrack[],
): { id: string; title: string; host: string; tracks: number; date: string; art: string }[] {
  const artOf = new Map(catalog.map((t) => [t.id, t.artUrl ?? ""]))

  return archives
    .slice()
    .sort((a, b) => (b.endedAt ?? b.startedAt).localeCompare(a.endedAt ?? a.startedAt))
    .slice(0, 6)
    .map((a) => ({
      id: a.id,
      title: a.roomName,
      host: a.participants[0] ?? "—",
      tracks: a.tracks.length,
      date: new Date(a.endedAt ?? a.startedAt).toLocaleDateString([], { year: "numeric", month: "short" }),
      art: (a.tracks[0] && artOf.get(a.tracks[0].id)) ?? "",
    }))
}

/** 홈 상단의 라운지 레일 카드. */
export function loungeCards(
  rooms: { id: string; title: string; hostName: string; currentTrackTitle: string; currentArtist: string; listenerCount: number; art: string; audioSpec: string }[],
) {
  return rooms.slice(0, 8).map((r) => ({
    id: r.id,
    roomId: r.id,
    title: r.title,
    host: r.hostName,
    track: r.currentArtist ? `${r.currentArtist} — ${r.currentTrackTitle}` : r.currentTrackTitle,
    listeners: r.listenerCount,
    art: r.art,
    spec: r.audioSpec,
  }))
}

export type LoungeCard = ReturnType<typeof loungeCards>[number]

export { formatSpec }
