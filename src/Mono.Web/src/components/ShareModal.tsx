import { useState } from "react"
import { MonoIcon } from "./icons/MonoIcons"

export interface ShareData {
  type: "lounge" | "playlist" | "track" | "album"
  id?: string
  title: string
  subtitle?: string
  coverUrl?: string
  audioSpec?: string
  hostGear?: string
  visibility?: "public" | "unlisted"
  secretKey?: string
}

interface Props {
  isOpen: boolean
  onClose: () => void
  shareData: ShareData | null
}

export default function ShareModal({ isOpen, onClose, shareData }: Props) {
  const [copied, setCopied] = useState(false)
  const [specsCopied, setSpecsCopied] = useState(false)

  if (!isOpen || !shareData) return null

  const isPlaylist = shareData.type === "playlist"
  const isUnlisted = shareData.visibility === "unlisted"
  const keyParam = isUnlisted && shareData.secretKey ? `&key=${shareData.secretKey}` : ""
  const webGateUrl = isPlaylist
    ? `https://mono.audio/crate/${shareData.id || "crate"}?spec=isrc`
    : `https://mono.audio/lounge?id=${shareData.id ?? ""}${keyParam}`
  const customProtocolUrl = `mono://lounge?id=${shareData.id ?? ""}${keyParam}`

  const handleCopyLink = async () => {
    try {
      await navigator.clipboard.writeText(webGateUrl)
    } catch { /* sandboxed — no-op */ }
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  const handleLaunchApp = () => {
    window.location.href = customProtocolUrl
  }

  const handleCopySpecs = async () => {
    const text = [
      shareData.title,
      shareData.subtitle,
      shareData.audioSpec && `Audio: ${shareData.audioSpec}`,
      shareData.hostGear && `Gear: ${shareData.hostGear}`,
      `Web Gate: ${webGateUrl}`,
      `Custom Protocol: ${customProtocolUrl}`,
    ].filter(Boolean).join("\n")
    try {
      await navigator.clipboard.writeText(text)
    } catch { /* no-op */ }
    setSpecsCopied(true)
    setTimeout(() => setSpecsCopied(false), 2000)
  }

  return (
    <div
      style={{
        position: "fixed", inset: 0, zIndex: 400,
        background: "rgba(0,0,0,0.58)", backdropFilter: "blur(6px)",
        display: "flex", alignItems: "center", justifyContent: "center",
        animation: "share-fade-in 0.18s ease",
      }}
      onClick={(e) => { if (e.target === e.currentTarget) onClose() }}
    >
      <style>{`
        @keyframes share-fade-in {
          from { opacity: 0; transform: scale(0.97) translateY(6px); }
          to   { opacity: 1; transform: scale(1) translateY(0); }
        }
      `}</style>

      <div
        style={{
          width: 468, background: "var(--surface-card)", borderRadius: 20,
          boxShadow: "0 32px 80px rgba(0,0,0,0.32), 0 4px 20px rgba(0,0,0,0.12)",
          border: "1px solid var(--border-subtle)", overflow: "hidden",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div style={{ padding: "20px 24px 16px", borderBottom: "1px solid #F1F5F9", display: "flex", alignItems: "center", justifyContent: "space-between" }}>
          <div>
            <div style={{ fontSize: 15, fontWeight: 700, color: "#0F172A", marginBottom: 2 }}>
              {isPlaylist ? "Share Playlist" : isUnlisted ? "Share Private Invite Link" : "Share Session"}
            </div>
            {isPlaylist ? (
              <div className="flex items-center gap-1.5 px-2 py-0.5 rounded-md bg-violet-500/10 border border-violet-500/25 text-violet-700 font-mono text-[10px] tracking-wider uppercase" style={{ marginTop: 4, display: "inline-flex" }}>
                <span className="w-1.5 h-1.5 rounded-full bg-violet-500 animate-pulse" style={{ display: "inline-block", width: 6, height: 6, borderRadius: "50%", background: "#8B5CF6", flexShrink: 0 }} />
                ISRC Universal Protocol v2.4
              </div>
            ) : (
              <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10.5, color: "#94A3B8", letterSpacing: "0.04em" }}>
                {isUnlisted ? "Invite-Only · Not Listed on Public Feed" : "Live Lounge · Bit-Perfect Stream"}
              </div>
            )}
          </div>
          <button
            onClick={onClose}
            style={{ background: "none", border: "none", cursor: "pointer", color: "#94A3B8", padding: 4, transition: "color 0.15s", display: "flex", alignItems: "center" }}
            onMouseEnter={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "#334155")}
            onMouseLeave={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "#94A3B8")}
            aria-label="Close"
          >
            <MonoIcon.Close size={16} />
          </button>
        </div>

        <div style={{ padding: "20px 24px 24px", display: "flex", flexDirection: "column", gap: 16 }}>

          {/* Crate Card */}
          <div style={{
            background: "linear-gradient(145deg, #0B1525 0%, #0F172A 100%)",
            border: "1px solid #1E293B",
            borderRadius: 14,
            padding: "16px 18px",
            display: "flex", alignItems: "center", gap: 14,
            position: "relative", overflow: "hidden",
          }}>
            {/* Ambient glow */}
            <div style={{
              position: "absolute", top: -20, left: -20, width: 100, height: 100,
              borderRadius: "50%", background: "rgba(37,99,235,0.14)",
              pointerEvents: "none", filter: "blur(28px)",
            }} />

            {/* Cover art */}
            <div style={{
              width: 60, height: 60, borderRadius: 9, overflow: "hidden",
              background: "#1E293B", flexShrink: 0,
              border: "1px solid rgba(255,255,255,0.07)",
              boxShadow: "0 4px 16px rgba(0,0,0,0.5)",
            }}>
              {shareData.coverUrl ? (
                <img src={shareData.coverUrl} alt={shareData.title} style={{ width: "100%", height: "100%", objectFit: "cover" }} />
              ) : (
                <div style={{ width: "100%", height: "100%", display: "flex", alignItems: "center", justifyContent: "center", color: "#60A5FA" }}>
                  <MonoIcon.Broadcast size={24} />
                </div>
              )}
            </div>

            {/* Meta */}
            <div style={{ flex: 1, minWidth: 0, position: "relative" }}>
              {/* Live pill */}
              <div style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 5 }}>
                {isPlaylist ? (
                  <span style={{
                    fontFamily: "'DM Mono', monospace", fontSize: 9, fontWeight: 700,
                    color: "#7C3AED", background: "rgba(124,58,237,0.15)",
                    border: "1px solid rgba(124,58,237,0.3)",
                    borderRadius: 4, padding: "2px 7px", letterSpacing: "0.1em",
                    display: "inline-flex", alignItems: "center", gap: 4,
                  }}>
                    <MonoIcon.Playlist size={11} />
                    <span>MONO CURATED PLAYLIST</span>
                  </span>
                ) : (
                  <span style={{
                    fontFamily: "'DM Mono', monospace", fontSize: 9, fontWeight: 700,
                    color: "#2563EB", background: "rgba(37,99,235,0.18)",
                    border: "1px solid rgba(37,99,235,0.35)",
                    borderRadius: 4, padding: "2px 7px", letterSpacing: "0.1em",
                    display: "inline-flex", alignItems: "center", gap: 4,
                  }}>
                    <span style={{ width: 5, height: 5, borderRadius: "50%", background: "#2563EB", display: "inline-block" }} />
                    LIVE NOW
                  </span>
                )}
              </div>

              <div style={{ fontSize: 13, fontWeight: 700, color: "#F1F5F9", marginBottom: 3, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>
                {shareData.title}
              </div>
              <div style={{ fontSize: 12, color: "#94A3B8", marginBottom: 7, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>
                {shareData.subtitle}
              </div>

              <div style={{ display: "flex", alignItems: "center", gap: 6, flexWrap: "wrap" }}>
                {shareData.audioSpec && (
                  <span style={{
                    fontFamily: "'DM Mono', monospace", fontSize: 9.5, fontWeight: 600,
                    color: "#C9A227", background: "rgba(201,162,39,0.1)",
                    border: "1px solid rgba(201,162,39,0.22)",
                    borderRadius: 4, padding: "2px 7px",
                  }}>
                    {shareData.audioSpec}
                  </span>
                )}
                {shareData.hostGear && (
                  <span style={{
                    fontFamily: "'DM Mono', monospace", fontSize: 9.5, color: "#475569",
                    whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis", maxWidth: 200,
                  }}>
                    {shareData.hostGear}
                  </span>
                )}
              </div>
            </div>

            {/* Mono watermark */}
            <div style={{ position: "absolute", bottom: 10, right: 14 }}>
              <span style={{ fontFamily: "Georgia, serif", fontSize: 9, fontWeight: 700, color: "rgba(255,255,255,0.1)", letterSpacing: "0.14em" }}>
                MONO
              </span>
            </div>
          </div>

          {/* Client gate / privacy notice */}
          {isPlaylist ? null : isUnlisted ? (
            <div style={{
              background: "rgba(109,40,217,0.06)", border: "1px solid rgba(109,40,217,0.2)",
              borderRadius: 10, padding: "10px 14px",
              display: "flex", alignItems: "flex-start", gap: 10,
            }}>
              <span style={{ color: "#7C3AED", flexShrink: 0, marginTop: 1, display: "flex", alignItems: "center" }}>
                <MonoIcon.Lock size={14} />
              </span>
              <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "#5B21B6", lineHeight: 1.55 }}>
                <span style={{ fontWeight: 700 }}>Invite-Only Session</span>
                {" · "}Anyone with this secret link can join this bit-perfect session. It will not appear on the public feed.
              </div>
            </div>
          ) : (
            <div className="share-modal-notice">
              <span style={{ color: "#64748B", flexShrink: 0, marginTop: 1, display: "flex", alignItems: "center" }}>
                <MonoIcon.Lock size={14} />
              </span>
              <div className="share-modal-notice-text">
                <span style={{ fontWeight: 700 }}>Mono Client Required</span>
                {" · "}Bit-Perfect ASIO session streams are only accessible via the Mono Desktop Player.
              </div>
            </div>
          )}

          {/* ISRC telemetry banner — playlist only */}
          {isPlaylist && (
            <div className="rounded-lg bg-zinc-50 border border-zinc-200/80 p-3 space-y-1.5 font-mono text-[11px]">
              <div className="flex items-center justify-between text-zinc-600">
                <span className="text-zinc-400 uppercase tracking-wider text-[10px]">SYNC SPEC</span>
                <span className="font-semibold text-zinc-800">ISRC Master Fingerprint Match</span>
              </div>
              <div className="flex items-center justify-between text-zinc-600">
                <span className="text-zinc-400 uppercase tracking-wider text-[10px]">COMPATIBILITY</span>
                <span className="text-zinc-700">TIDAL · Qobuz · Mono ASIO Engine</span>
              </div>
              <div className="pt-1.5 border-t border-zinc-200/60 text-[10px] text-zinc-500 font-sans truncate">
                Exact studio master recording fingerprints mapped across platforms.
              </div>
            </div>
          )}

          {/* Web gate link row */}
          <div>
            <div style={{ fontSize: 10.5, fontWeight: 600, color: "#64748B", marginBottom: 6, fontFamily: "'DM Mono', monospace", letterSpacing: "0.05em", textTransform: "uppercase" }}>
              {isPlaylist ? "ISRC Crate Link" : isUnlisted ? "Secret Invite Link" : "Lounge Link"}
            </div>
            <div style={{ display: "flex", gap: 8, alignItems: "stretch" }}>
              <div className="share-modal-input">
                {webGateUrl}
              </div>
              <button
                onClick={handleCopyLink}
                style={{
                  flexShrink: 0, padding: "9px 16px", borderRadius: 9, cursor: "pointer",
                  fontSize: 12, fontWeight: 600, fontFamily: "inherit",
                  background: copied ? "#059669" : "#0F172A",
                  color: "#FFFFFF", border: "none",
                  transition: "background 0.2s", whiteSpace: "nowrap",
                  display: "inline-flex", alignItems: "center", gap: 6,
                }}
              >
                {copied ? (
                  <>
                    <MonoIcon.Check size={13} strokeWidth={2.5} />
                    <span>Copied to Clipboard!</span>
                  </>
                ) : isPlaylist ? "Copy ISRC Crate Link" : isUnlisted ? "Copy Secret Link" : "Copy Lounge Link"}
              </button>
            </div>
          </div>

          {/* Action buttons row */}
          <div style={{ display: "flex", gap: 10 }}>
            {/* Launch Mono App — lounge only */}
            {!isPlaylist && <button
              onClick={handleLaunchApp}
              className="share-modal-btn-secondary"
              style={{ display: "flex", alignItems: "center", justifyContent: "center", gap: 8, fontWeight: 700, fontSize: 13 }}
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <rect x="2" y="3" width="20" height="14" rx="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/>
              </svg>
              Launch Mono App
            </button>}

            {/* Copy audio specs */}
            <button
              onClick={handleCopySpecs}
              className="share-modal-btn-secondary"
              style={specsCopied ? { background: "#ECFDF5", borderColor: "#A7F3D0", color: "#059669", display: "inline-flex", alignItems: "center", gap: 6 } : { display: "inline-flex", alignItems: "center", gap: 6 }}
            >
              {specsCopied ? (
                <>
                  <MonoIcon.Check size={13} strokeWidth={2.5} />
                  <span>Copied</span>
                </>
              ) : (
                <>
                  <MonoIcon.Clipboard size={13} />
                  <span>Copy Audio Specs</span>
                </>
              )}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
