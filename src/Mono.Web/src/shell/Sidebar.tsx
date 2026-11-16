import { Compass, Home, Library, Plus, Radio, Settings } from "lucide-react"
import { useApp, type Route } from "../state/app"
import { useMono } from "../state/MonoProvider"
import { Mosaic } from "../ui/tracks"

/** 앱 아이콘의 "m" 을 그대로 가져온 워드마크. 점 하나만 신호색이다. */
export function BrandMark({ size = 26 }: { size?: number }) {
  return (
    <svg className="brand__mark" width={size} height={size} viewBox="150 280 760 470" aria-hidden>
      <path
        d="M 230 670 C 230 460, 316 360, 416 360 C 484 360, 506 460, 512 490 C 518 460, 540 360, 608 360 C 708 360, 794 460, 794 670"
        fill="none" stroke="currentColor" strokeWidth="76" strokeLinecap="round" strokeLinejoin="round"
      />
      <circle cx="880" cy="670" r="40" />
    </svg>
  )
}

const NAV: { route: Route; label: string; icon: typeof Home; pages: Route["page"][] }[] = [
  { route: { page: "home" }, label: "홈", icon: Home, pages: ["home"] },
  { route: { page: "library" }, label: "라이브러리", icon: Library, pages: ["library", "album", "artist"] },
  { route: { page: "lounges" }, label: "라운지", icon: Radio, pages: ["lounges", "room"] },
  { route: { page: "explore" }, label: "스트리밍", icon: Compass, pages: ["explore"] },
]

export default function Sidebar() {
  const app = useApp()
  const { playlists, connection, catalog, room } = useMono()
  const { route, role } = app

  const listeners = room ? room.members.filter((m) => !m.isOutput).length : 0

  return (
    <aside className="sidebar">
      <button className="brand" onClick={() => app.go({ page: "home" })} aria-label="Mono 홈">
        <BrandMark />
        <span className="brand__word">Mono</span>
      </button>

      <nav className="side-nav">
        {NAV.map(({ route: to, label, icon: Icon, pages }) => (
          <button
            key={label}
            className={`side-item ${pages.includes(route.page) ? "is-on" : ""}`}
            onClick={() => app.go(to)}
          >
            <Icon size={18} strokeWidth={1.9} />
            <span>{label}</span>
            {to.page === "lounges" && (role === "host" || role === "guest") && <i className="lamp lamp--live tail" />}
          </button>
        ))}
      </nav>

      <div className="side-sec">
        <span className="label">플레이리스트</span>
        <button
          className="icon-btn icon-btn--sm"
          aria-label="새 플레이리스트"
          onClick={() => app.openModal({ kind: "playlist", trackIds: [] })}
          style={{ margin: "-6px -7px -6px 0" }}
        >
          <Plus size={16} />
        </button>
      </div>

      <div className="side-scroll">
        {playlists.map((p) => (
          <button
            key={p.id}
            className={`side-pl ${route.page === "playlist" && route.id === p.id ? "is-on" : ""}`}
            onClick={() => app.go({ page: "playlist", id: p.id })}
          >
            <Mosaic arts={p.tracks.map((t) => t.artUrl)} size={36} />
            <span className="grow">
              <span className="side-pl__name clip" style={{ display: "block" }}>{p.title}</span>
              <span className="side-pl__meta">{p.tracks.length}곡{p.fromArchiveId ? " · 라운지 기록" : ""}</span>
            </span>
          </button>
        ))}
        {playlists.length === 0 && (
          <p className="t-cap dimmer" style={{ padding: "4px 11px", lineHeight: 1.6 }}>
            곡의 ⋯ 메뉴에서 플레이리스트를 만들 수 있습니다.
          </p>
        )}
      </div>

      {(role === "host" || role === "guest") && room && (
        <button className={`side-live ${role === "guest" ? "side-live--guest" : ""}`} onClick={() => app.go({ page: "room" })}>
          <span className="row gap-8">
            <i className={`lamp ${role === "host" ? "lamp--live" : "lamp--accent"}`} />
            <span className="label" style={{ color: role === "host" ? "var(--live-text)" : "var(--accent-text)" }}>
              {role === "host" ? "On Air" : "라운지 청취 중"}
            </span>
          </span>
          <span className="t-small clip" style={{ fontWeight: 620 }}>{room.name}</span>
          <span className="t-cap dim">{listeners}명이 함께 듣는 중</span>
        </button>
      )}

      <div className="side-foot">
        <button
          className={`side-item ${route.page === "settings" ? "is-on" : ""}`}
          onClick={() => app.go({ page: "settings" })}
        >
          <Settings size={18} strokeWidth={1.9} />
          <span>설정</span>
        </button>
        <div className="core-status">
          <i className={`lamp ${connection === "open" ? "lamp--ok" : "lamp--warn"}`} />
          <span className="clip">
            {connection === "open"
              ? <>Core 연결됨 · <span className="mono">{catalog.length.toLocaleString()}</span>곡</>
              : connection === "connecting" ? "Core 에 연결하는 중…" : "Core 연결이 끊겨 다시 시도하는 중"}
          </span>
        </div>
      </div>
    </aside>
  )
}
