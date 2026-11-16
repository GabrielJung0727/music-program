import { useEffect, useRef, useState } from "react"
import { ChevronLeft, ChevronRight, LogOut, Moon, Search, Settings, Sun, UserRound, X } from "lucide-react"
import { useApp } from "../state/app"
import { useMono } from "../state/MonoProvider"
import { StreamingProvider } from "../lib/protocol"
import { shell } from "../lib/shell"
import { Avatar } from "../ui"

export default function TopBar({ solid }: { solid: boolean }) {
  const app = useApp()
  const { displayName, streamingAccounts } = useMono()
  const { route } = app
  const inputRef = useRef<HTMLInputElement>(null)
  const [query, setQuery] = useState(route.page === "search" ? route.q : "")

  // 검색 화면을 벗어나면 입력도 비운다. 남겨 두면 다른 화면에서 검색 중인 것처럼 보인다.
  useEffect(() => {
    if (route.page !== "search") setQuery("")
    else if (route.q !== query) setQuery(route.q)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [route])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault()
        inputRef.current?.focus()
        inputRef.current?.select()
      }
    }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [])

  const onSearch = (value: string) => {
    setQuery(value)
    if (value.trim()) app.go({ page: "search", q: value }, { replace: route.page === "search" })
    else if (route.page === "search") app.back()
  }

  const tidal = streamingAccounts.some((a) => a.provider === StreamingProvider.Tidal && a.connected)
  const qobuz = streamingAccounts.some((a) => a.provider === StreamingProvider.Qobuz && a.connected)

  return (
    <header className={`topbar ${solid ? "is-solid" : ""}`}>
      <button className="icon-btn" onClick={app.back} disabled={!app.canBack} aria-label="뒤로">
        <ChevronLeft size={20} />
      </button>
      <button className="icon-btn" onClick={app.forward} disabled={!app.canForward} aria-label="앞으로">
        <ChevronRight size={20} />
      </button>

      <div className="search">
        <Search size={16} />
        <input
          ref={inputRef}
          value={query}
          onChange={(e) => onSearch(e.target.value)}
          onKeyDown={(e) => { if (e.key === "Escape") { onSearch(""); e.currentTarget.blur() } }}
          placeholder="앨범, 아티스트, 곡, 라운지 검색"
          spellCheck={false}
          aria-label="검색"
        />
        {query ? (
          <button className="icon-btn icon-btn--sm clear" onClick={() => onSearch("")} aria-label="검색어 지우기"><X size={15} /></button>
        ) : (
          <kbd>Ctrl K</kbd>
        )}
      </div>

      <div className="grow" />

      <button className="svc" onClick={() => app.go({ page: "settings", tab: "accounts" })} title="스트리밍 계정">
        <span className={tidal ? "" : "off"}><i className={`lamp ${tidal ? "lamp--ok" : ""}`} />TIDAL</span>
        <span className={qobuz ? "" : "off"}><i className={`lamp ${qobuz ? "lamp--ok" : ""}`} />Qobuz</span>
      </button>

      <button
        className="me"
        onClick={(e) => app.openMenu(e, [
          { title: displayName },
          { label: "프로필과 계정", icon: <UserRound size={15} />, onSelect: () => app.go({ page: "settings", tab: "general" }) },
          { label: "설정", icon: <Settings size={15} />, onSelect: () => app.go({ page: "settings" }) },
          {
            label: app.theme === "dark" ? "밝은 테마로" : "어두운 테마로",
            icon: app.theme === "dark" ? <Sun size={15} /> : <Moon size={15} />,
            onSelect: () => app.setTheme(app.theme === "dark" ? "light" : "dark"),
          },
          ...(shell() ? [
            { sep: true },
            { label: "트레이로 숨기기", icon: <LogOut size={15} />, onSelect: () => { void shell()?.app.minimizeToTray() } },
          ] : []),
        ], "right")}
      >
        <Avatar name={displayName} color={app.setup?.profile?.avatarColor} />
        <span>{displayName}</span>
      </button>
    </header>
  )
}
