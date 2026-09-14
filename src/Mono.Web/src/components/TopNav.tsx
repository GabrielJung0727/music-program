import { useState } from "react"
import {
  type Profile,
  type SearchTrack,
  type SearchAlbum,
  INITIAL_PROFILES,
  PROFILE_COLORS,
} from "../data/types"
import { MonoIcon } from "./icons/MonoIcons"

export type CurrentTab = "home" | "lounges" | "library" | "explore" | "settings"

function IconSearch() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="11" cy="11" r="8" /><path d="m21 21-4.35-4.35" />
    </svg>
  )
}

function IconSettings() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z" />
      <circle cx="12" cy="12" r="3" />
    </svg>
  )
}

function IconChevron({ size = 12 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <path d="m6 9 6 6 6-6" />
    </svg>
  )
}

export default function TopNav({
  activeTab, setActiveTab, currentTab, setCurrentTab, onNavigateToAccounts,
  onGoBack, onGoForward, canGoBack, canGoForward,
  searchQuery = "", setSearchQuery = () => {}, setIsSearchActive = () => {},
  onOpenMilesDavis = () => {}, onSelectSomethinElse = () => {}, onReturnToLounge = () => {}, onNavigateToSettings = () => {},
  filteredAlbums = [], filteredTracks = [], onSelectAlbum = () => {},
  profiles = [], activeProfileId = "1", activeProfile = INITIAL_PROFILES[0],
  showProfileMenu = false, setShowProfileMenu = () => {},
  onSwitchProfile = () => {}, onOpenProfileSettings = () => {}, onGoHome = () => {},
  connectedServices = { qobuz: true, tidal: true },
  hasActiveSession = false,
  theme = "dark", onToggleTheme = () => {},
  matchedLounges = [], onJoinLounge = () => {},
}: {
  activeTab: string
  setActiveTab: (t: string) => void
  currentTab: CurrentTab
  setCurrentTab: (t: CurrentTab) => void
  onNavigateToAccounts: () => void
  onGoBack: () => void
  onGoForward: () => void
  canGoBack: boolean
  canGoForward: boolean
  searchQuery?: string
  setSearchQuery?: (v: string) => void
  setIsSearchActive?: (v: boolean) => void
  onOpenMilesDavis?: () => void
  onSelectSomethinElse?: () => void
  onReturnToLounge?: () => void
  onNavigateToSettings?: () => void
  hasActiveSession?: boolean
  filteredAlbums?: SearchAlbum[]
  filteredTracks?: SearchTrack[]
  onSelectAlbum?: (album: SearchAlbum) => void
  profiles?: Profile[]
  activeProfileId?: string
  activeProfile?: Profile
  showProfileMenu?: boolean
  setShowProfileMenu?: (v: boolean) => void
  onSwitchProfile?: (id: string) => void
  onOpenProfileSettings?: () => void
  onGoHome?: () => void
  connectedServices?: { qobuz: boolean; tidal: boolean }
  theme?: "light" | "dark"
  onToggleTheme?: () => void
  /** 검색어와 맞는 실제 라운지. 없으면 드롭다운에서 그 줄이 사라진다. */
  matchedLounges?: { id: string; title: string; hostName: string; listenerCount: number; art: string }[]
  onJoinLounge?: (roomId: string) => void
}) {
  const [isServicesOpen, setIsServicesOpen] = useState<boolean>(false)
  const [showQuickPreview, setShowQuickPreview] = useState<boolean>(false)
  const qobuzConnected = connectedServices?.qobuz ?? true
  const tidalConnected = connectedServices?.tidal ?? true
  const connectedCount = (qobuzConnected ? 1 : 0) + (tidalConnected ? 1 : 0)

  const tabs: Array<{ id: string; label: string; page?: CurrentTab; arrow?: boolean }> = [
    { id: "home", label: "Home", page: "home" },
    { id: "live", label: "Live Lounges", page: "lounges" },
    { id: "library", label: "My Library", page: "library" },
    { id: "explore", label: "Explore", page: "explore" },
  ]

  return (
    <header
      style={{ height: 64, borderBottom: "1px solid var(--border-subtle)", background: "color-mix(in srgb, var(--surface-card) 92%, transparent)", backdropFilter: "blur(12px)", WebkitBackdropFilter: "blur(12px)", transition: "background 0.2s ease, border-color 0.2s ease" }}
      className="sticky top-0 z-50 flex items-center px-12 gap-10"
    >
      {/* History nav buttons */}
      <div className="flex items-center gap-1 mr-3" style={{ color: "var(--text-muted)" }}>
        <button
          onClick={onGoBack}
          disabled={!canGoBack}
          className={`w-10 h-10 flex items-center justify-center rounded-full cursor-pointer transition-colors ${!canGoBack ? "opacity-30 pointer-events-none" : ""}`}
          style={{ background: "none", border: "none", fontSize: 24, lineHeight: 1, color: "var(--text-secondary)" }}
          onMouseEnter={(e) => { (e.currentTarget as HTMLButtonElement).style.background = "var(--surface-elevated)" }}
          onMouseLeave={(e) => { (e.currentTarget as HTMLButtonElement).style.background = "none" }}
        >
          ‹
        </button>
        <button
          onClick={onGoForward}
          disabled={!canGoForward}
          className={`w-10 h-10 flex items-center justify-center rounded-full cursor-pointer transition-colors ${!canGoForward ? "opacity-30 pointer-events-none" : ""}`}
          style={{ background: "none", border: "none", fontSize: 24, lineHeight: 1, color: "var(--text-secondary)" }}
          onMouseEnter={(e) => { (e.currentTarget as HTMLButtonElement).style.background = "var(--surface-elevated)" }}
          onMouseLeave={(e) => { (e.currentTarget as HTMLButtonElement).style.background = "none" }}
        >
          ›
        </button>
      </div>

      {/* Logo */}
      <span
        onClick={onGoHome}
        style={{ fontSize: 22, fontWeight: 700, color: "var(--text-primary)", letterSpacing: "-0.5px", minWidth: 60, cursor: "pointer", userSelect: "none", transition: "color 0.2s ease" }}
      >
        Mono
      </span>

      {/* Nav Tabs */}
      <nav className="flex items-center" style={{ gap: 28, marginLeft: 12 }}>
        {tabs.map((tab) => {
          const isActive = activeTab === tab.id
          return (
            <button
              key={tab.id}
              onClick={() => {
                if (tab.id === "live" && hasActiveSession) {
                  onReturnToLounge()
                  return
                }
                setActiveTab(tab.id)
                if (tab.page !== undefined) setCurrentTab(tab.page)
              }}
              className={`nav-tab${isActive && currentTab !== "settings" ? " is-active" : ""}`}
            >
              {isActive && currentTab !== "settings" && (
                <span className="nav-tab-dot" />
              )}
              {tab.label}
              {tab.arrow && (
                <span style={{ color: "#9CA3AF", marginTop: 1 }}>
                  <IconChevron />
                </span>
              )}
            </button>
          )
        })}
      </nav>

      {/* Right Utilities */}
      <div className="flex items-center gap-3 ml-auto">
        {/* Search */}
        <div className="relative" style={{ width: 260 }}>
          <div className="relative flex items-center">
            <span style={{ position: "absolute", left: 11, color: "#9CA3AF", pointerEvents: "none", zIndex: 1 }}>
              <IconSearch />
            </span>
            <input
              type="text"
              placeholder="Search music, lounges..."
              value={searchQuery}
              onChange={(e) => {
                const v = e.target.value
                setSearchQuery(v)
                if (v.trim().length > 0) { setShowQuickPreview(true); setIsSearchActive(false) }
                else { setShowQuickPreview(false); setIsSearchActive(false) }
              }}
              onFocus={() => { if (searchQuery.trim().length > 0) setShowQuickPreview(true) }}
              onKeyDown={(e) => {
                if (e.key === "Enter") { setShowQuickPreview(false); setIsSearchActive(true) }
                if (e.key === "Escape") { setShowQuickPreview(false) }
              }}
              style={{
                width: "100%",
                height: 34,
                background: "var(--surface-elevated)",
                border: "1px solid transparent",
                borderRadius: 20,
                paddingLeft: 32,
                paddingRight: searchQuery.length > 0 ? 30 : 12,
                fontSize: 13,
                color: "var(--text-primary)",
                outline: "none",
                fontFamily: "inherit",
                transition: "border-color 0.15s, background 0.2s ease",
              }}
              onBlur={(e) => {
                ;(e.target as HTMLInputElement).style.borderColor = "transparent"
                setTimeout(() => setShowQuickPreview(false), 150)
              }}
            />
            {searchQuery.length > 0 && (
              <button
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => { setSearchQuery(""); setIsSearchActive(false); setShowQuickPreview(false) }}
                style={{
                  position: "absolute", right: 9, background: "none", border: "none",
                  cursor: "pointer", color: "#9CA3AF",
                  display: "flex", alignItems: "center", padding: 2, zIndex: 1,
                }}
                aria-label="Clear search"
              >
                <MonoIcon.Close size={13} />
              </button>
            )}
          </div>

          {/* Quick Preview Popover */}
          {showQuickPreview && searchQuery.trim().length > 0 && (
            <div
              className="absolute left-0 right-0 rounded-2xl shadow-2xl overflow-hidden"
              style={{ top: "calc(100% + 8px)", zIndex: 9999, minWidth: 320, background: "var(--surface-card)", border: "1px solid var(--border-subtle)", backdropFilter: "blur(12px)" }}
              onMouseDown={(e) => e.preventDefault()}
            >
              {filteredAlbums.length === 0 && filteredTracks.length === 0 ? (
                <div className="px-4 py-5 text-center">
                  <p className="text-xs font-mono text-slate-400">No matching audiophile records found.</p>
                  <p className="text-[10px] font-mono text-slate-300 mt-1">Try "Blue", "Miles", "Kind", or "Train"</p>
                </div>
              ) : (
                <>
                  {filteredAlbums.slice(0, 2).map(album => (
                    <div
                      key={album.title}
                      onClick={() => { setShowQuickPreview(false); setSearchQuery(""); setIsSearchActive(false); onSelectAlbum(album) }}
                      className="flex items-center gap-3 px-4 py-3 hover:bg-slate-50 cursor-pointer transition group"
                    >
                      <img src={album.coverUrl} alt={album.title} className="w-9 h-9 rounded-lg object-cover border border-slate-200 shrink-0" />
                      <div className="flex-1 min-w-0">
                        <p className="text-sm font-serif font-semibold text-slate-900 truncate group-hover:text-blue-700 transition">{album.title}</p>
                        <p className="text-[10px] font-mono text-slate-400 truncate">{album.artist} · {album.year}</p>
                      </div>
                      <span className={`text-[10px] font-mono border px-2 py-0.5 rounded-full shrink-0 ${album.format.includes("DSD") ? "bg-blue-50 text-blue-700 border-blue-200" : "bg-purple-50 text-purple-700 border-purple-200"}`}>
                        {album.format.split(" ").slice(0, 2).join(" ")}
                      </span>
                    </div>
                  ))}

                  {filteredTracks.slice(0, 3).map(track => (
                    <div
                      key={`${track.title}-${track.album}`}
                      className={`flex items-center gap-3 px-4 py-3 cursor-pointer transition ${track.isCurrent ? "bg-blue-50/40 hover:bg-blue-50/60" : "hover:bg-slate-50"}`}
                    >
                      <div className={`w-9 h-9 rounded-lg flex items-center justify-center shrink-0 ${track.isCurrent ? "bg-blue-100" : "bg-slate-100"}`}>
                        <MonoIcon.PlayMini size={12} className={track.isCurrent ? "text-blue-600" : "text-slate-400"} />
                      </div>
                      <div className="flex-1 min-w-0">
                        <p className={`text-sm font-serif font-semibold truncate ${track.isCurrent ? "text-blue-700" : "text-slate-900"}`}>{track.title}</p>
                        <p className="text-[10px] font-mono text-slate-400 truncate">{track.artist} · {track.album} · {track.duration}</p>
                      </div>
                      <span className={`text-[10px] font-mono border px-2 py-0.5 rounded-full shrink-0 ${track.dr === "DR 14" ? "bg-emerald-50 text-emerald-700 border-emerald-200" : track.dr === "DR 13" ? "bg-amber-50 text-amber-700 border-amber-200" : "bg-slate-100 text-slate-500 border-slate-200"}`}>{track.dr}</span>
                    </div>
                  ))}
                </>
              )}

              {/* Live Lounge row — 검색어와 맞는 실제 룸 */}
              {matchedLounges.map(room => (
                <div
                  key={room.id}
                  onClick={() => { setShowQuickPreview(false); setSearchQuery(""); setIsSearchActive(false); onJoinLounge(room.id) }}
                  className="flex items-center gap-3 px-4 py-3 bg-slate-900 hover:bg-slate-800 cursor-pointer transition group"
                >
                  <div className="w-9 h-9 rounded-lg overflow-hidden border border-white/10 shrink-0 bg-white/5">
                    {room.art && <img src={room.art} alt={room.title} className="w-full h-full object-cover" />}
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-1.5">
                      <span className="w-1.5 h-1.5 rounded-full bg-red-500 animate-pulse inline-block" />
                      <p className="text-xs font-serif font-semibold text-white truncate">{room.title}</p>
                    </div>
                    <p className="text-[10px] font-mono text-slate-400 mt-0.5 flex items-center gap-1">
                      <span>Host: {room.hostName}</span>
                      <MonoIcon.Crown size={11} className="text-amber-400 inline" />
                      <span>· {room.listenerCount} listening</span>
                    </p>
                  </div>
                  <span className="text-[10px] font-mono bg-blue-600 text-white px-2.5 py-1 rounded-full shrink-0 group-hover:bg-blue-500 transition">Join →</span>
                </div>
              ))}

              {/* Footer */}
              <div className="flex items-center justify-between px-4 py-2.5" style={{ background: "var(--surface-elevated)" }}>
                <span className="text-[10px] font-mono text-slate-400">Press Enter ↵ for comprehensive search</span>
                <button
                  onMouseDown={(e) => e.preventDefault()}
                  onClick={() => { setShowQuickPreview(false); setIsSearchActive(true) }}
                  className="text-[10px] font-mono text-blue-600 hover:text-blue-800 cursor-pointer transition"
                  style={{ background: "none", border: "none", fontFamily: "inherit", padding: 0 }}
                >
                  View All Results →
                </button>
              </div>
            </div>
          )}
        </div>

        {/* Services button + popover */}
        <div style={{ position: "relative", display: "inline-block" }}>
          <button
            onClick={() => setIsServicesOpen(!isServicesOpen)}
            className="flex items-center gap-2 select-none"
            style={{
              padding: "6px 12px", borderRadius: 9999, border: "1px solid var(--border-subtle)",
              background: isServicesOpen ? "var(--surface-elevated)" : "transparent",
              cursor: "pointer", transition: "border-color 0.15s, background 0.15s", fontFamily: "inherit",
              display: "flex", alignItems: "center", gap: 8,
            }}
            onMouseEnter={(e) => {
              (e.currentTarget as HTMLButtonElement).style.borderColor = "var(--border-strong)"
              ;(e.currentTarget as HTMLButtonElement).style.background = "var(--surface-elevated)"
            }}
            onMouseLeave={(e) => {
              (e.currentTarget as HTMLButtonElement).style.borderColor = "var(--border-subtle)"
              ;(e.currentTarget as HTMLButtonElement).style.background = isServicesOpen ? "var(--surface-elevated)" : "transparent"
            }}
          >
            <span style={{
              width: 8, height: 8, borderRadius: "50%", flexShrink: 0,
              background: connectedCount > 0 ? "#10b981" : "#CBD5E1",
              transition: "background 0.2s",
              display: "inline-block",
            }} />
            <span style={{ fontSize: 12, fontWeight: 500, color: "var(--text-secondary)", whiteSpace: "nowrap" }}>
              Services ({connectedCount})
            </span>
          </button>

          {isServicesOpen && (
            <>
              <div style={{ position: "fixed", inset: 0, zIndex: 49 }} onClick={() => setIsServicesOpen(false)} />
              <div style={{
                position: "absolute", right: 0, top: "calc(100% + 8px)", width: 256,
                background: "var(--surface-card)", borderRadius: 12, boxShadow: "0 10px 40px rgba(0,0,0,0.18), 0 2px 8px rgba(0,0,0,0.1)",
                border: "1px solid var(--border-subtle)", padding: 16, zIndex: 50,
              }}>
                <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, fontWeight: 600, letterSpacing: "0.1em", color: "var(--text-muted)", textTransform: "uppercase", marginBottom: 12 }}>
                  Connected Partners
                </div>

                <div style={{ opacity: qobuzConnected ? 1 : 0.55, transition: "opacity 0.2s" }}>
                  <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                    <span style={{ width: 6, height: 6, borderRadius: "50%", background: qobuzConnected ? "var(--accent-violet)" : "var(--border-strong)", display: "inline-block", flexShrink: 0, transition: "background 0.2s" }} />
                    <span style={{ fontSize: 12, fontWeight: 600, color: "var(--text-primary)" }}>Qobuz Studio</span>
                  </div>
                  <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--text-muted)", paddingLeft: 14, marginTop: 2 }}>
                    {qobuzConnected ? "24-Bit / 192kHz Studio Master" : "Disconnected"}
                  </div>
                </div>

                <div style={{ marginTop: 12, opacity: tidalConnected ? 1 : 0.55, transition: "opacity 0.2s" }}>
                  <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                    <span style={{ width: 6, height: 6, borderRadius: "50%", background: tidalConnected ? "var(--accent-violet)" : "var(--border-strong)", display: "inline-block", flexShrink: 0, transition: "background 0.2s" }} />
                    <span style={{ fontSize: 12, fontWeight: 600, color: "var(--text-primary)" }}>TIDAL Max</span>
                  </div>
                  <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--text-muted)", paddingLeft: 14, marginTop: 2 }}>
                    {tidalConnected ? "HiRes FLAC Direct" : "Disconnected"}
                  </div>
                </div>

                <div style={{ borderTop: "1px solid var(--border-subtle)", marginTop: 12, marginBottom: 12 }} />
                <button
                  onClick={() => { onNavigateToAccounts(); setIsServicesOpen(false) }}
                  className="btn-manage-accounts"
                >
                  Manage Accounts in Settings
                </button>
              </div>
            </>
          )}
        </div>

        {/* Theme toggle */}
        <button
          onClick={onToggleTheme}
          title={theme === "dark" ? "Switch to Light Mode" : "Switch to Dark Mode"}
          style={{
            width: 36, height: 36, borderRadius: 8, border: "none", cursor: "pointer",
            display: "flex", alignItems: "center", justifyContent: "center",
            background: "none", color: "var(--text-secondary)",
            transition: "background 0.15s, color 0.15s",
          }}
          onMouseEnter={(e) => {
            (e.currentTarget as HTMLButtonElement).style.background = "var(--surface-elevated)"
            ;(e.currentTarget as HTMLButtonElement).style.color = "var(--text-primary)"
          }}
          onMouseLeave={(e) => {
            (e.currentTarget as HTMLButtonElement).style.background = "none"
            ;(e.currentTarget as HTMLButtonElement).style.color = "var(--text-secondary)"
          }}
        >
          {theme === "dark" ? (
            /* Sun icon */
            <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="12" cy="12" r="4"/>
              <path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41"/>
            </svg>
          ) : (
            /* Moon icon */
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/>
            </svg>
          )}
        </button>

        {/* Settings gear */}
        <button
          onClick={() => onNavigateToSettings()}
          title="Settings"
          style={{
            width: 36, height: 36, borderRadius: 8, border: "none", cursor: "pointer",
            display: "flex", alignItems: "center", justifyContent: "center",
            background: currentTab === "settings" ? "var(--surface-elevated)" : "none",
            color: currentTab === "settings" ? "var(--text-primary)" : "var(--text-secondary)",
            transition: "background 0.15s, color 0.15s",
          }}
          onMouseEnter={(e) => {
            if (currentTab !== "settings") {
              (e.currentTarget as HTMLButtonElement).style.background = "var(--surface-elevated)"
              ;(e.currentTarget as HTMLButtonElement).style.color = "var(--text-primary)"
            }
          }}
          onMouseLeave={(e) => {
            if (currentTab !== "settings") {
              (e.currentTarget as HTMLButtonElement).style.background = "none"
              ;(e.currentTarget as HTMLButtonElement).style.color = "var(--text-secondary)"
            }
          }}
        >
          <IconSettings />
        </button>

        {/* Avatar + Profile Quick Switcher */}
        <div style={{ position: "relative" }}>
          {showProfileMenu && (
            <div style={{ position: "fixed", inset: 0, zIndex: 49 }} onClick={() => setShowProfileMenu(false)} />
          )}
          <button
            onClick={() => setShowProfileMenu(!showProfileMenu)}
            style={{
              width: 32, height: 32, borderRadius: "50%",
              background: PROFILE_COLORS[activeProfileId] ?? "#6D28D9",
              display: "flex", alignItems: "center", justifyContent: "center",
              fontSize: 12, fontWeight: 700, color: "#ffffff",
              cursor: "pointer", flexShrink: 0, border: "none",
              outline: showProfileMenu ? "2px solid #6D28D9" : "none",
              outlineOffset: 2, transition: "outline 0.15s",
            }}
            title={activeProfile.name}
          >
            {activeProfile.avatarText}
          </button>

          {showProfileMenu && (
            <div className="absolute right-0 mt-2.5 w-72 rounded-2xl shadow-2xl z-50"
              style={{ top: "calc(100% + 8px)", background: "var(--surface-card)", border: "1px solid var(--border-subtle)", backdropFilter: "blur(12px)" }}
            >
              <div style={{ padding: "14px 16px 10px" }}>
                <div style={{ fontSize: 10, fontWeight: 700, color: "var(--text-muted)", letterSpacing: "0.08em", textTransform: "uppercase", marginBottom: 10, fontFamily: "'DM Mono', monospace" }}>
                  Listening Profiles
                </div>
                {profiles.map(p => (
                  <button
                    key={p.id}
                    onClick={() => onSwitchProfile(p.id)}
                    style={{
                      width: "100%", display: "flex", alignItems: "center", gap: 10,
                      padding: "8px 10px", borderRadius: 10, border: "none",
                      background: p.id === activeProfileId ? "var(--surface-elevated)" : "transparent",
                      cursor: "pointer", transition: "background 0.12s", fontFamily: "inherit",
                      marginBottom: 2,
                    }}
                    onMouseEnter={(e) => { if (p.id !== activeProfileId) (e.currentTarget as HTMLButtonElement).style.background = "#F8FAFC" }}
                    onMouseLeave={(e) => { if (p.id !== activeProfileId) (e.currentTarget as HTMLButtonElement).style.background = "transparent" }}
                  >
                    <span style={{
                      width: 30, height: 30, borderRadius: "50%", flexShrink: 0,
                      background: PROFILE_COLORS[p.id] ?? "#64748B",
                      display: "flex", alignItems: "center", justifyContent: "center",
                      fontSize: 11, fontWeight: 700, color: "#fff",
                    }}>
                      {p.avatarText}
                    </span>
                    <div style={{ flex: 1, minWidth: 0, textAlign: "left" }}>
                      <div style={{ fontSize: 13, fontWeight: 600, color: "var(--text-primary)", lineHeight: 1.3 }}>{p.name}</div>
                      <div style={{ fontSize: 10, color: "var(--text-muted)", fontFamily: "'DM Mono', monospace", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{p.gearPreset}</div>
                    </div>
                    {p.id === activeProfileId && (
                      <span style={{ color: PROFILE_COLORS[p.id] ?? "#6D28D9", flexShrink: 0, display: "flex", alignItems: "center" }}>
                        <MonoIcon.Check size={14} strokeWidth={2.5} />
                      </span>
                    )}
                  </button>
                ))}
              </div>

              <div style={{ padding: "10px 12px", display: "flex", gap: 6, borderTop: "1px solid var(--border-subtle)" }}>
                <button
                  onClick={() => { setShowProfileMenu(false); onOpenProfileSettings() }}
                  style={{
                    flex: 1, fontSize: 12, fontWeight: 500, color: "var(--text-secondary)",
                    background: "var(--surface-elevated)", border: "1px solid var(--border-subtle)", borderRadius: 8,
                    padding: "8px 0", cursor: "pointer", fontFamily: "inherit", transition: "all 0.12s",
                  }}
                  onMouseEnter={(e) => { (e.currentTarget as HTMLButtonElement).style.color = "var(--text-primary)" }}
                  onMouseLeave={(e) => { (e.currentTarget as HTMLButtonElement).style.color = "var(--text-secondary)" }}
                >
                  + Add New Profile
                </button>
                <button
                  onClick={() => { setShowProfileMenu(false); onOpenProfileSettings() }}
                  style={{
                    flex: 1, fontSize: 12, fontWeight: 500, color: "var(--accent-violet)",
                    background: "var(--accent-glow)", border: "1px solid var(--border-strong)", borderRadius: 8,
                    padding: "8px 0", cursor: "pointer", fontFamily: "inherit", transition: "all 0.12s",
                    display: "flex", alignItems: "center", justifyContent: "center", gap: 5,
                  }}
                >
                  <MonoIcon.Settings size={13} />
                  <span>Profile Settings</span>
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </header>
  )
}
