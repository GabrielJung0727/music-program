import { useEffect, useState } from "react"
import { type Profile, PROFILE_COLORS } from "../data/types"

export type SettingsTab = "profiles" | "engine" | "lens"

interface SettingsModalProps {
  isOpen: boolean
  onClose: () => void
  profiles: Profile[]
  activeProfileId: string
  activeProfile: Profile
  onSwitchProfile: (id: string) => void
  onAddProfile: (name: string, gear: string) => void
  activeTab?: SettingsTab
  onTabChange?: (tab: SettingsTab) => void
}

export default function SettingsModal({
  isOpen,
  onClose,
  profiles,
  activeProfileId,
  activeProfile,
  onSwitchProfile,
  onAddProfile,
  activeTab,
  onTabChange,
}: SettingsModalProps) {
  const [currentTab, setCurrentTab] = useState<SettingsTab>(activeTab ?? "profiles")

  useEffect(() => {
    if (activeTab) setCurrentTab(activeTab)
  }, [activeTab])

  function handleTabClick(tab: SettingsTab) {
    setCurrentTab(tab)
    onTabChange?.(tab)
  }
  const [newProfileName, setNewProfileName] = useState("")
  const [newProfileGear, setNewProfileGear] = useState("")

  if (!isOpen) return null

  const handleAddProfile = () => {
    if (!newProfileName.trim()) return
    onAddProfile(newProfileName.trim(), newProfileGear.trim())
    setNewProfileName("")
    setNewProfileGear("")
  }

  return (
    <div
      className="fixed inset-0 z-[200] flex items-center justify-center p-6"
      style={{ background: "rgba(15,23,42,0.45)", backdropFilter: "blur(6px)" }}
      onClick={(e) => { if (e.target === e.currentTarget) onClose() }}
    >
      <div
        style={{
          width: "100%", maxWidth: 620, background: "#FFFFFF", borderRadius: 24,
          boxShadow: "0 32px 80px rgba(0,0,0,0.22)", border: "1px solid #E2E8F0",
          display: "flex", flexDirection: "column", maxHeight: "85vh", overflow: "hidden",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Modal header */}
        <div style={{ padding: "24px 28px 0", display: "flex", alignItems: "flex-start", justifyContent: "space-between" }}>
          <div>
            <h2 style={{ fontSize: 20, fontWeight: 700, color: "#0F172A", letterSpacing: "-0.3px", margin: 0 }}>Settings</h2>
            <p style={{ fontSize: 12, color: "#94A3B8", fontFamily: "'DM Mono', monospace", marginTop: 4 }}>
              Manage audiophile multi-user profiles &amp; audio engines
            </p>
          </div>
          <button
            onClick={onClose}
            style={{ background: "none", border: "none", cursor: "pointer", color: "#94A3B8", fontSize: 18, lineHeight: 1, padding: "4px 8px", transition: "color 0.15s" }}
            onMouseEnter={(e) => { (e.currentTarget as HTMLButtonElement).style.color = "#334155" }}
            onMouseLeave={(e) => { (e.currentTarget as HTMLButtonElement).style.color = "#94A3B8" }}
          >
            ✕
          </button>
        </div>

        {/* Tab bar */}
        <div style={{ padding: "16px 28px 0", borderBottom: "1px solid #F1F5F9", display: "flex", gap: 0 }}>
          {([
            { id: "profiles" as const, label: "Profiles & Users" },
            { id: "engine" as const, label: "Audio Engine (ASIO)" },
            { id: "lens" as const, label: "LENS DSP Presets" },
          ]).map(tab => (
            <button
              key={tab.id}
              onClick={() => handleTabClick(tab.id)}
              style={{
                background: "none", border: "none", borderBottom: currentTab === tab.id ? "2px solid #6D28D9" : "2px solid transparent",
                padding: "0 0 12px", marginRight: 24, cursor: "pointer", fontFamily: "inherit",
                fontSize: 13, fontWeight: currentTab === tab.id ? 600 : 400,
                color: currentTab === tab.id ? "#6D28D9" : "#94A3B8", transition: "color 0.12s",
              }}
              onMouseEnter={(e) => { if (currentTab !== tab.id) (e.currentTarget as HTMLButtonElement).style.color = "#475569" }}
              onMouseLeave={(e) => { if (currentTab !== tab.id) (e.currentTarget as HTMLButtonElement).style.color = "#94A3B8" }}
            >
              {tab.label}
            </button>
          ))}
        </div>

        {/* Modal content */}
        <div style={{ flex: 1, overflowY: "auto", padding: "24px 28px 28px" }}>

          {/* ── Profiles & Users tab ── */}
          {currentTab === "profiles" && (
            <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
              {/* Existing profiles */}
              <div>
                <div style={{ fontSize: 11, fontWeight: 700, color: "#94A3B8", letterSpacing: "0.08em", textTransform: "uppercase", fontFamily: "'DM Mono', monospace", marginBottom: 12 }}>
                  Your Profiles
                </div>
                <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                  {profiles.map(p => (
                    <div
                      key={p.id}
                      style={{
                        display: "flex", alignItems: "center", gap: 14, padding: "14px 16px",
                        background: p.id === activeProfileId ? "#FAFAFF" : "#F8FAFC",
                        border: `1px solid ${p.id === activeProfileId ? "#DDD6FE" : "#E2E8F0"}`,
                        borderRadius: 14, transition: "border-color 0.15s",
                      }}
                    >
                      <span style={{
                        width: 40, height: 40, borderRadius: "50%", flexShrink: 0,
                        background: PROFILE_COLORS[p.id] ?? "#64748B",
                        display: "flex", alignItems: "center", justifyContent: "center",
                        fontSize: 13, fontWeight: 700, color: "#fff",
                      }}>
                        {p.avatarText}
                      </span>
                      <div style={{ flex: 1, minWidth: 0 }}>
                        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                          <span style={{ fontSize: 14, fontWeight: 600, color: "#0F172A" }}>{p.name}</span>
                          {p.id === activeProfileId && (
                            <span style={{
                              fontSize: 10, fontWeight: 700, color: "#6D28D9",
                              background: "#EDE9FE", border: "1px solid #DDD6FE",
                              borderRadius: 9999, padding: "2px 8px", fontFamily: "'DM Mono', monospace",
                            }}>
                              Active
                            </span>
                          )}
                        </div>
                        <div style={{ fontSize: 11, color: "#94A3B8", fontFamily: "'DM Mono', monospace", marginTop: 2, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>
                          {p.gearPreset}
                        </div>
                      </div>
                      {p.id !== activeProfileId && (
                        <button
                          onClick={() => onSwitchProfile(p.id)}
                          style={{
                            fontSize: 12, fontWeight: 500, color: "#6D28D9",
                            background: "#F5F3FF", border: "1px solid #DDD6FE",
                            borderRadius: 8, padding: "6px 14px", cursor: "pointer",
                            fontFamily: "inherit", transition: "all 0.12s", flexShrink: 0,
                          }}
                          onMouseEnter={(e) => { (e.currentTarget as HTMLButtonElement).style.background = "#EDE9FE" }}
                          onMouseLeave={(e) => { (e.currentTarget as HTMLButtonElement).style.background = "#F5F3FF" }}
                        >
                          Switch to
                        </button>
                      )}
                    </div>
                  ))}
                </div>
              </div>

              {/* Add new profile form */}
              <div style={{ borderTop: "1px solid #F1F5F9", paddingTop: 20 }}>
                <div style={{ fontSize: 11, fontWeight: 700, color: "#94A3B8", letterSpacing: "0.08em", textTransform: "uppercase", fontFamily: "'DM Mono', monospace", marginBottom: 14 }}>
                  Add New Profile
                </div>
                <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
                  <div>
                    <label style={{ fontSize: 12, fontWeight: 500, color: "#475569", display: "block", marginBottom: 6 }}>Profile Name</label>
                    <input
                      value={newProfileName}
                      onChange={(e) => setNewProfileName(e.target.value)}
                      onKeyDown={(e) => { if (e.key === "Enter") handleAddProfile() }}
                      placeholder="e.g. Studio Reference"
                      style={{
                        width: "100%", boxSizing: "border-box",
                        border: "1px solid #E2E8F0", borderRadius: 10, padding: "10px 14px",
                        fontSize: 13, fontFamily: "inherit", color: "#0F172A",
                        outline: "none", transition: "border-color 0.15s",
                      }}
                      onFocus={(e) => { (e.target as HTMLInputElement).style.borderColor = "#A78BFA" }}
                      onBlur={(e) => { (e.target as HTMLInputElement).style.borderColor = "#E2E8F0" }}
                    />
                  </div>
                  <div>
                    <label style={{ fontSize: 12, fontWeight: 500, color: "#475569", display: "block", marginBottom: 6 }}>Gear / DSP Preset</label>
                    <input
                      value={newProfileGear}
                      onChange={(e) => setNewProfileGear(e.target.value)}
                      onKeyDown={(e) => { if (e.key === "Enter") handleAddProfile() }}
                      placeholder="e.g. Chord Hugo 2 • Neutral"
                      style={{
                        width: "100%", boxSizing: "border-box",
                        border: "1px solid #E2E8F0", borderRadius: 10, padding: "10px 14px",
                        fontSize: 13, fontFamily: "inherit", color: "#0F172A",
                        outline: "none", transition: "border-color 0.15s",
                      }}
                      onFocus={(e) => { (e.target as HTMLInputElement).style.borderColor = "#A78BFA" }}
                      onBlur={(e) => { (e.target as HTMLInputElement).style.borderColor = "#E2E8F0" }}
                    />
                  </div>
                  {newProfileName.trim() && (
                    <div style={{ display: "flex", alignItems: "center", gap: 10, padding: "10px 14px", background: "#F5F3FF", borderRadius: 10, border: "1px solid #EDE9FE" }}>
                      <span style={{
                        width: 32, height: 32, borderRadius: "50%", background: "#6D28D9",
                        display: "flex", alignItems: "center", justifyContent: "center",
                        fontSize: 11, fontWeight: 700, color: "#fff", flexShrink: 0,
                      }}>
                        {(() => {
                          const words = newProfileName.trim().split(" ")
                          return words.length >= 2
                            ? (words[0][0] + words[words.length - 1][0]).toUpperCase()
                            : newProfileName.slice(0, 2).toUpperCase()
                        })()}
                      </span>
                      <div>
                        <div style={{ fontSize: 13, fontWeight: 600, color: "#0F172A" }}>{newProfileName}</div>
                        <div style={{ fontSize: 10, color: "#94A3B8", fontFamily: "'DM Mono', monospace" }}>{newProfileGear || "Default Setup"}</div>
                      </div>
                    </div>
                  )}
                  <button
                    onClick={handleAddProfile}
                    disabled={!newProfileName.trim()}
                    style={{
                      fontSize: 13, fontWeight: 600, color: "#fff",
                      background: newProfileName.trim() ? "#6D28D9" : "#CBD5E1",
                      border: "none", borderRadius: 10, padding: "11px 0",
                      cursor: newProfileName.trim() ? "pointer" : "not-allowed",
                      fontFamily: "inherit", transition: "background 0.15s",
                    }}
                    onMouseEnter={(e) => { if (newProfileName.trim()) (e.currentTarget as HTMLButtonElement).style.background = "#5B21B6" }}
                    onMouseLeave={(e) => { if (newProfileName.trim()) (e.currentTarget as HTMLButtonElement).style.background = "#6D28D9" }}
                  >
                    Create Profile
                  </button>
                </div>
              </div>
            </div>
          )}

          {/* ── Audio Engine tab ── */}
          {currentTab === "engine" && (
            <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
              <div style={{ padding: "16px 20px", background: "#F8FAFC", border: "1px solid #E2E8F0", borderRadius: 14 }}>
                <div style={{ fontSize: 13, fontWeight: 600, color: "#0F172A", marginBottom: 4 }}>ASIO Exclusive Mode</div>
                <div style={{ fontSize: 12, color: "#64748B" }}>Bypasses OS audio mixer for bit-perfect output. Active for profile: <strong>{activeProfile.name}</strong></div>
              </div>
              <div style={{ padding: "16px 20px", background: "#F8FAFC", border: "1px solid #E2E8F0", borderRadius: 14 }}>
                <div style={{ fontSize: 13, fontWeight: 600, color: "#0F172A", marginBottom: 4 }}>Current Output Device</div>
                <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "#6D28D9", marginTop: 4 }}>{activeProfile.gearPreset}</div>
              </div>
              <div style={{ fontSize: 12, color: "#94A3B8", fontFamily: "'DM Mono', monospace", textAlign: "center", padding: "12px 0" }}>
                Full audio engine config available in Settings → Audio Engine
              </div>
            </div>
          )}

          {/* ── DSP Presets tab ── */}
          {currentTab === "lens" && (
            <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
              {[
                { name: "Harman Reference Target", desc: "Psychoacoustically optimised — industry standard", active: true },
                { name: "Flat (Studio Monitor)", desc: "Ruler-flat response for critical mastering", active: false },
                { name: "Warm Tube Curve", desc: "+2dB low-end warmth, smooth high-frequency roll-off", active: false },
              ].map(preset => (
                <div
                  key={preset.name}
                  style={{
                    display: "flex", alignItems: "center", justifyContent: "space-between",
                    padding: "14px 16px", background: preset.active ? "#FAFAFF" : "#F8FAFC",
                    border: `1px solid ${preset.active ? "#DDD6FE" : "#E2E8F0"}`, borderRadius: 14,
                  }}
                >
                  <div>
                    <div style={{ fontSize: 13, fontWeight: 600, color: "#0F172A" }}>{preset.name}</div>
                    <div style={{ fontSize: 11, color: "#94A3B8", marginTop: 2 }}>{preset.desc}</div>
                  </div>
                  {preset.active && (
                    <span style={{ fontSize: 10, fontWeight: 700, color: "#6D28D9", background: "#EDE9FE", border: "1px solid #DDD6FE", borderRadius: 9999, padding: "2px 8px", fontFamily: "'DM Mono', monospace" }}>
                      Active
                    </span>
                  )}
                </div>
              ))}
              <div style={{ fontSize: 12, color: "#94A3B8", fontFamily: "'DM Mono', monospace", textAlign: "center", paddingTop: 4 }}>
                Full LENS DSP configuration in Settings → LENS DSP Suite
              </div>
            </div>
          )}

        </div>
      </div>
    </div>
  )
}
