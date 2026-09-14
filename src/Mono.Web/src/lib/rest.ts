// Core 의 REST 표면. 룸 상태와 무관한 읽기 전용 조회는 WebSocket 대신 여기로 간다.

export interface Health {
  rooms: number
  tracks: number
  endpoints: number
  [key: string]: unknown
}

export interface HeatBucket {
  trackId: string
  bucketMs: number
  count: number
}

export interface WikiBio {
  title?: string
  summary?: string
  url?: string
  [key: string]: unknown
}

async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  const res = await fetch(path, { signal, headers: { accept: "application/json" } })
  if (!res.ok) throw new Error(`${path} → ${res.status}`)
  return (await res.json()) as T
}

export const api = {
  health: (signal?: AbortSignal) => get<Health>("/api/health", signal),
  rooms: (signal?: AbortSignal) => get<unknown[]>("/api/rooms", signal),
  catalog: (signal?: AbortSignal) => get<unknown[]>("/api/catalog", signal),
  endpoints: (signal?: AbortSignal) => get<unknown[]>("/api/endpoints", signal),
  archives: (signal?: AbortSignal) => get<unknown[]>("/api/archives", signal),
  playlists: (signal?: AbortSignal) => get<unknown[]>("/api/playlists", signal),
  heatmap: (trackId: string, signal?: AbortSignal) =>
    get<HeatBucket[]>(`/api/reactions/${encodeURIComponent(trackId)}`, signal),
  wiki: (artistId: string, signal?: AbortSignal) =>
    get<WikiBio>(`/api/wiki/${encodeURIComponent(artistId)}`, signal),
}

/** 앨범 아트 URL. Core 가 태그/파일에서 뽑아 캐시한다. */
export function artUrl(trackId: string | null | undefined, size?: number): string | null {
  if (!trackId) return null
  const q = size ? `?size=${size}` : ""
  return `/api/art/${encodeURIComponent(trackId)}${q}`
}

/** 로컬/스트리밍 공용 재생 URL — 오디오 미리듣기용. 실제 동기 재생은 Output 이 한다. */
export function streamUrl(trackId: string): string {
  return `/api/stream/tidal/${encodeURIComponent(trackId)}`
}
