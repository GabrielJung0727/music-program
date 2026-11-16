// 화면들이 공유하는 표시용 타입. Core 의 자료구조는 src/lib/protocol.ts 에 있고,
// 그쪽에서 이 모양으로 옮기는 건 src/lib/adapters.ts 가 한다.

export interface QueueTrack {
  id: string
  title: string
  artist: string
  album: string
  duration: string
  format: string
  dr?: string
  art?: string
  source?: "qobuz" | "tidal" | "local"
}

export interface SearchTrack {
  title: string
  artist: string
  album: string
  duration: string
  dr: string
  isCurrent?: boolean
}

export interface SearchAlbum {
  title: string
  artist: string
  year: string
  label: string
  studio: string
  coverUrl: string
  /** 일부 호출부는 coverUrl 대신 art 로 넘긴다. 읽을 때는 art ?? coverUrl 순서. */
  art?: string
  format: string
  dr: string
  wikiUrl: string
  wikiSummary: string
  tracks: SearchTrack[]
}

export interface LoungeRoom {
  id: string
  title: string
  hostName: string
  hostAvatar: string
  hostGear?: string
  currentTrackTitle: string
  currentArtist: string
  audioSpec: string
  listenerCount: number
  badge?: "debut" | "indie" | "cozy" | "trending"
  category: "indie" | "debut" | "cozy" | "popular"
  art: string
  visibility?: "public" | "unlisted"
  secretKey?: string
}

/**
 * 청취 프로필 — 이 기기에서 쓰는 장비 프리셋 묶음이다.
 * Core 는 프로필을 모른다(룸 멤버는 peerId 로만 구분한다). 로컬 개념으로 남긴다.
 */
export type Profile = {
  id: string
  name: string
  tag: string
  avatarText: string
  gearPreset: string
  isPrimary: boolean
}

export const INITIAL_PROFILES: Profile[] = [
  { id: "1", name: "Listener", tag: "LS", avatarText: "LS", gearPreset: "기본 출력", isPrimary: true },
]

export const PROFILE_COLORS: Record<string, string> = {
  "1": "#6D28D9",
  "2": "#0F766E",
  "3": "#1D4ED8",
}
