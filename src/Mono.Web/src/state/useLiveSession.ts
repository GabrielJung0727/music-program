import { useCallback, useEffect, useMemo, useState } from "react"
import type { LoungeRoom, QueueTrack, SearchAlbum, SearchTrack } from "../data/types"
import {
  queueItemToTrack,
  roomListingToLounge,
  snapshotToLounge,
  snapshotTrackToQueueTrack,
  type LiveQueueTrack,
} from "../lib/adapters"
import { useMono, useMonoCommands } from "./MonoProvider"

/**
 * App.tsx 가 쓰던 로컬 useState 를 Core 의 실제 상태로 갈아 끼운다.
 * 반환 모양은 디자인이 이미 쓰고 있는 타입 그대로라 컴포넌트는 손댈 게 없다.
 */
export function useLiveSession() {
  const { room, rooms, albums, catalog } = useMono()
  const cmd = useMonoCommands()

  const currentTrack: QueueTrack | null = useMemo(
    () => (room?.currentTrack ? snapshotTrackToQueueTrack(room.currentTrack) : null),
    [room?.currentTrack],
  )

  /** Core 의 큐는 현재 곡을 포함한다. 드로어는 "다음 곡들"만 보여 주므로 잘라 낸다. */
  const queue: LiveQueueTrack[] = useMemo(() => {
    if (!room) return []
    return room.queue
      .map((q, i) => queueItemToTrack(q, i))
      .filter((_, i) => i > room.queueIndex)
  }, [room])

  const playing = room?.playing ?? false

  // ── 라운지 ──────────────────────────────────────────────────────────────

  /** 내가 들어와 있는 룸은 스냅샷 쪽이 더 자세하다. 목록의 같은 항목을 덮어쓴다. */
  const lounges: LoungeRoom[] = useMemo(() => {
    const mapped = rooms.map(roomListingToLounge)
    if (!room) return mapped
    const mine = snapshotToLounge(room)
    const index = mapped.findIndex((r) => r.id === mine.id)
    if (index >= 0) {
      const next = [...mapped]
      next[index] = mine
      return next
    }
    return [mine, ...mapped]
  }, [rooms, room])

  // ── 검색 ────────────────────────────────────────────────────────────────

  const searchAlbums = useCallback(
    (query: string): SearchAlbum[] => {
      const q = query.toLowerCase().trim()
      if (!q) return []
      return albums.filter(
        (a) =>
          a.title.toLowerCase().includes(q) ||
          a.artist.toLowerCase().includes(q) ||
          a.label.toLowerCase().includes(q),
      )
    },
    [albums],
  )

  const searchTracks = useCallback(
    (query: string): SearchTrack[] => {
      const q = query.toLowerCase().trim()
      if (!q) return []
      return albums
        .flatMap((a) => a.tracks)
        .filter(
          (t) =>
            t.title.toLowerCase().includes(q) ||
            t.artist.toLowerCase().includes(q) ||
            t.album.toLowerCase().includes(q),
        )
    },
    [albums],
  )

  /** 제목으로 곡 id 를 찾는다. 디자인 컴포넌트들이 id 없이 제목만 넘겨 주기 때문이다. */
  const findTrackId = useCallback(
    (title: string, artist?: string): string | null => {
      const t = title.toLowerCase().trim()
      const a = artist?.toLowerCase().trim()
      const exact = catalog.find(
        (c) => c.title.toLowerCase() === t && (!a || (c.artist ?? "").toLowerCase() === a),
      )
      if (exact) return exact.id
      return catalog.find((c) => c.title.toLowerCase() === t)?.id ?? null
    },
    [catalog],
  )

  const findAlbum = useCallback(
    (title: string) => albums.find((a) => a.title.toLowerCase() === title.toLowerCase()) ?? null,
    [albums],
  )

  return {
    room,
    currentTrack,
    queue,
    playing,
    lounges,
    albums,
    catalog,
    searchAlbums,
    searchTracks,
    findTrackId,
    findAlbum,
    cmd,
  }
}

/** 짧게 떴다 사라지는 안내. App.tsx 의 showToast 를 그대로 대체한다. */
export function useToast() {
  const [message, setMessage] = useState<string | null>(null)

  const show = useCallback((text: string) => setMessage(text), [])

  useEffect(() => {
    if (!message) return
    const id = setTimeout(() => setMessage(null), 3500)
    return () => clearTimeout(id)
  }, [message])

  return { message, show }
}
