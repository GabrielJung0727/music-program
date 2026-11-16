import { useMemo, useState } from "react"
import { ArrowDownUp, Check, ListMusic, Play, Plus, Search, Shuffle } from "lucide-react"
import { useApp, type LibTab } from "../state/app"
import { useMono } from "../state/MonoProvider"
import { buildComposers, fmtLong, topGenres, type Album } from "../lib/model"
import { StreamingProvider } from "../lib/protocol"
import { Art, Empty } from "../ui"
import { AlbumCard, Mosaic, TrackHead, TrackRow } from "../ui/tracks"

type Sort = "added" | "title" | "artist" | "year"
const SORT_LABEL: Record<Sort, string> = { added: "최근 추가순", title: "제목순", artist: "아티스트순", year: "발매 연도순" }

function sortAlbums(list: Album[], sort: Sort): Album[] {
  const out = [...list]
  switch (sort) {
    case "added": return out.sort((a, b) => (b.addedAt ?? "").localeCompare(a.addedAt ?? ""))
    case "title": return out.sort((a, b) => a.title.localeCompare(b.title, "ko"))
    case "artist": return out.sort((a, b) => a.artist.localeCompare(b.artist, "ko") || (a.year ?? 0) - (b.year ?? 0))
    case "year": return out.sort((a, b) => (b.year ?? 0) - (a.year ?? 0))
  }
}

export default function Library({ tab }: { tab: LibTab }) {
  const app = useApp()
  const { catalog, playlists, catalogLoaded } = useMono()
  const { albums, artists } = app

  const [filter, setFilter] = useState("all")
  const [sort, setSort] = useState<Sort>("added")
  const [find, setFind] = useState("")
  const [shown, setShown] = useState(200)

  const genres = useMemo(() => topGenres(catalog, 5), [catalog])
  const composers = useMemo(() => buildComposers(catalog), [catalog])
  const needle = find.trim().toLowerCase()

  const match = (a: Album) => {
    switch (filter) {
      case "all": return true
      case "hires": return a.quality.tier === "hires"
      case "dsd": return a.quality.tier === "dsd"
      case "local": return a.source === StreamingProvider.Local
      case "stream": return a.source !== StreamingProvider.Local
      default: return a.genres.includes(filter)
    }
  }

  const filteredAlbums = useMemo(
    () => sortAlbums(albums.filter(match).filter((a) => !needle || `${a.title} ${a.artist}`.toLowerCase().includes(needle)), sort),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [albums, filter, sort, needle],
  )
  const filteredArtists = useMemo(() => artists.filter((a) => !needle || a.name.toLowerCase().includes(needle)), [artists, needle])
  const filteredTracks = useMemo(
    () => catalog.filter((t) => !needle || `${t.title} ${t.artist} ${t.album}`.toLowerCase().includes(needle)),
    [catalog, needle],
  )

  const tabs: { id: LibTab; label: string; count: number }[] = [
    { id: "albums", label: "앨범", count: albums.length },
    { id: "artists", label: "아티스트", count: artists.length },
    { id: "tracks", label: "곡", count: catalog.length },
    { id: "composers", label: "작곡가", count: composers.length },
    { id: "playlists", label: "플레이리스트", count: playlists.length },
  ]

  const chips: { id: string; label: string; n?: number }[] = [
    { id: "all", label: "전체", n: albums.length },
    { id: "hires", label: "Hi-Res", n: albums.filter((a) => a.quality.tier === "hires").length },
    { id: "dsd", label: "DSD", n: albums.filter((a) => a.quality.tier === "dsd").length },
    { id: "local", label: "로컬" },
    { id: "stream", label: "스트리밍" },
    ...genres.map((g) => ({ id: g.name, label: g.name })),
  ]

  if (catalogLoaded && catalog.length === 0) {
    return (
      <div className="page">
        <Empty
          icon={<ListMusic size={40} strokeWidth={1.2} />}
          title="아직 음악이 없습니다"
          action={<button className="btn btn--primary" onClick={() => app.go({ page: "settings", tab: "library" })}>음악 폴더 추가</button>}
        >
          FLAC · WAV · AIFF · ALAC · DSF/DFF · MP3 · AAC 파일을 읽습니다.
        </Empty>
      </div>
    )
  }

  return (
    <div className="page">
      <section>
        <h1 className="t-h1">라이브러리</h1>
        <div className="tabs" style={{ marginTop: 22 }}>
          {tabs.map((t) => (
            <button key={t.id} className={`tab ${tab === t.id ? "is-on" : ""}`} onClick={() => { setFind(""); app.go({ page: "library", tab: t.id }, { replace: true }) }}>
              {t.label}<span className="count">{t.count.toLocaleString()}</span>
            </button>
          ))}
        </div>

        <div className="toolbar">
          {tab === "albums" ? (
            <div className="row gap-8 grow" style={{ flexWrap: "wrap" }}>
              {chips.map((c) => (
                <button key={c.id} className={`chip ${filter === c.id ? "is-on" : ""}`} onClick={() => setFilter(c.id)}>
                  {c.label}{c.n !== undefined && <span className="count">{c.n}</span>}
                </button>
              ))}
            </div>
          ) : tab === "tracks" ? (
            <div className="row gap-8 grow">
              <button className="btn btn--primary" onClick={() => app.playTracks(filteredTracks.map((t) => t.id), 0)} disabled={filteredTracks.length === 0}>
                <Play size={15} fill="currentColor" />전체 재생
              </button>
              <button
                className="btn btn--line"
                onClick={() => app.playTracks(filteredTracks.map((t) => t.id).sort(() => Math.random() - 0.5), 0)}
                disabled={filteredTracks.length === 0}
              >
                <Shuffle size={15} />셔플
              </button>
            </div>
          ) : tab === "playlists" ? (
            <div className="row grow">
              <button className="btn btn--line" onClick={() => app.openModal({ kind: "playlist", trackIds: [] })}><Plus size={15} />새 플레이리스트</button>
            </div>
          ) : <div className="grow" />}

          {tab === "albums" && (
            <button
              className="btn btn--quiet btn--sm"
              onClick={(e) => app.openMenu(e, (Object.keys(SORT_LABEL) as Sort[]).map((s) => ({
                label: SORT_LABEL[s],
                icon: sort === s ? <Check size={15} /> : <span style={{ width: 15 }} />,
                onSelect: () => setSort(s),
              })), "right")}
            >
              <ArrowDownUp size={14} />{SORT_LABEL[sort]}
            </button>
          )}
          {tab !== "playlists" && tab !== "composers" && (
            <label className="finder">
              <Search size={14} />
              <input value={find} onChange={(e) => setFind(e.target.value)} placeholder="이 목록에서 찾기" spellCheck={false} />
            </label>
          )}
        </div>
      </section>

      <section style={{ marginTop: 26 }}>
        {tab === "albums" && (
          filteredAlbums.length === 0
            ? <Empty title="조건에 맞는 앨범이 없습니다">필터를 바꾸거나 찾는 말을 지워 보세요.</Empty>
            : <div className="grid">{filteredAlbums.map((a) => <AlbumCard key={a.id} album={a} />)}</div>
        )}

        {tab === "artists" && (
          <div className="grid">
            {filteredArtists.map((a) => (
              <div key={a.id} className="card card--artist" role="link" tabIndex={0} onClick={() => app.go({ page: "artist", id: a.id })}>
                <Art src={a.art} round />
                <div style={{ textAlign: "center" }}>
                  <div className="card__title clip">{a.name}</div>
                  <div className="card__sub">앨범 {a.albumCount} · {a.trackCount}곡</div>
                </div>
              </div>
            ))}
          </div>
        )}

        {tab === "tracks" && (
          <div className="tlist">
            <TrackHead cols={["#", "제목", "앨범", "음질", "길이", null]} />
            {filteredTracks.slice(0, shown).map((t, i) => (
              <TrackRow key={t.id} track={t} index={i + 1} showArt showAlbum onPlay={() => app.playTracks(filteredTracks.map((x) => x.id), i)} />
            ))}
            {filteredTracks.length > shown && (
              <button className="btn btn--line" style={{ alignSelf: "center", marginTop: 20 }} onClick={() => setShown((n) => n + 400)}>
                {(filteredTracks.length - shown).toLocaleString()}곡 더 보기
              </button>
            )}
          </div>
        )}

        {tab === "composers" && (
          composers.length === 0
            ? <Empty title="작곡가 태그가 있는 곡이 없습니다">곡의 Composer 태그를 읽어 작곡가별로 묶습니다.</Empty>
            : (
              <div className="rows">
                {composers.map((c) => (
                  <div key={c.name} className="row-card" onDoubleClick={() => app.playTracks(c.trackIds, 0)}>
                    <Art src={c.art} size={56} round />
                    <div className="grow">
                      <div className="t-h3 clip">{c.name}</div>
                      <div className="t-small dimmer">{c.genre ? `${c.genre} · ` : ""}작품 {c.works} · 녹음 {c.trackIds.length}</div>
                    </div>
                    <button className="btn btn--sm btn--line" onClick={() => app.playTracks(c.trackIds, 0)}><Play size={13} fill="currentColor" />재생</button>
                  </div>
                ))}
              </div>
            )
        )}

        {tab === "playlists" && (
          playlists.length === 0
            ? <Empty title="플레이리스트가 없습니다">곡의 ⋯ 메뉴에서 「새 플레이리스트」를 고르거나, 끝난 라운지 세션을 플레이리스트로 저장할 수 있습니다.</Empty>
            : (
              <div className="grid">
                {playlists.map((p) => (
                  <div key={p.id} className="card" role="link" tabIndex={0} onClick={() => app.go({ page: "playlist", id: p.id })}>
                    <Mosaic arts={p.tracks.map((t) => t.artUrl)} />
                    <div>
                      <div className="card__title clip-2">{p.title}</div>
                      <div className="card__sub">{p.tracks.length}곡 · {fmtLong(p.tracks.reduce((s, t) => s + (t.durationMs ?? 0), 0))}</div>
                    </div>
                  </div>
                ))}
              </div>
            )
        )}
      </section>
    </div>
  )
}
