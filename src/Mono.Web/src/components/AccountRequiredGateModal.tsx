import { useState } from "react"

interface Props {
  isOpen: boolean
  onClose: () => void
  onConnectService: (service: "qobuz" | "tidal") => void
}

export default function AccountRequiredGateModal({ isOpen, onClose, onConnectService }: Props) {
  const [hoveredService, setHoveredService] = useState<"qobuz" | "tidal" | null>(null)

  if (!isOpen) return null

  return (
    <div
      style={{
        position: "fixed", inset: 0, zIndex: 450,
        background: "rgba(0,0,0,0.72)", backdropFilter: "blur(8px)",
        display: "flex", alignItems: "center", justifyContent: "center",
        animation: "gate-fade-in 0.2s ease",
      }}
      onClick={(e) => { if (e.target === e.currentTarget) onClose() }}
    >
      <style>{`
        @keyframes gate-fade-in {
          from { opacity: 0; transform: scale(0.96) translateY(8px); }
          to   { opacity: 1; transform: scale(1) translateY(0); }
        }
      `}</style>

      <div
        style={{
          width: 480,
          background: "linear-gradient(160deg, #0F172A 0%, #0B1120 100%)",
          borderRadius: 20,
          border: "1px solid rgba(255,255,255,0.08)",
          boxShadow: "0 40px 100px rgba(0,0,0,0.5), 0 4px 24px rgba(0,0,0,0.3)",
          overflow: "hidden",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div style={{
          padding: "24px 28px 20px",
          borderBottom: "1px solid rgba(255,255,255,0.06)",
          display: "flex", alignItems: "flex-start", justifyContent: "space-between",
        }}>
          <div style={{ display: "flex", alignItems: "flex-start", gap: 14 }}>
            {/* Lock icon ring */}
            <div style={{
              width: 44, height: 44, borderRadius: 12, flexShrink: 0,
              background: "rgba(109,40,217,0.15)",
              border: "1px solid rgba(109,40,217,0.3)",
              display: "flex", alignItems: "center", justifyContent: "center",
            }}>
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#A78BFA" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/>
                <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
              </svg>
            </div>
            <div>
              <div style={{ fontSize: 16, fontWeight: 700, color: "#F1F5F9", marginBottom: 4 }}>
                Streaming Account Required
              </div>
              <div style={{ fontSize: 12, color: "#64748B", lineHeight: 1.5, maxWidth: 310 }}>
                To stream Hi-Res Bit-Perfect audio in this session, connect your active Qobuz Studio or TIDAL account.
              </div>
            </div>
          </div>
          <button
            onClick={onClose}
            style={{
              background: "none", border: "none", cursor: "pointer",
              color: "#475569", fontSize: 16, lineHeight: 1,
              padding: "2px 4px", transition: "color 0.15s", flexShrink: 0, marginTop: 2,
            }}
            onMouseEnter={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "#94A3B8")}
            onMouseLeave={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "#475569")}
          >
            ✕
          </button>
        </div>

        {/* Body */}
        <div style={{ padding: "20px 28px 28px", display: "flex", flexDirection: "column", gap: 12 }}>

          {/* Private session notice */}
          <div style={{
            background: "rgba(109,40,217,0.08)",
            border: "1px solid rgba(109,40,217,0.2)",
            borderRadius: 10, padding: "10px 14px",
            display: "flex", alignItems: "center", gap: 10,
          }}>
            <span style={{ fontSize: 13, flexShrink: 0 }}>🔒</span>
            <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "#A78BFA", lineHeight: 1.5 }}>
              <span style={{ fontWeight: 700 }}>Invite-Only Session</span>
              {" · "}Bit-perfect ASIO streams require a verified Hi-Res subscription to enter.
            </div>
          </div>

          {/* Service cards */}
          <div style={{ display: "flex", flexDirection: "column", gap: 8, marginTop: 4 }}>
            <div style={{ fontSize: 11, fontWeight: 600, color: "#475569", fontFamily: "'DM Mono', monospace", letterSpacing: "0.06em", textTransform: "uppercase", marginBottom: 2 }}>
              Connect a Streaming Service
            </div>

            {/* Qobuz card */}
            <button
              onClick={() => onConnectService("qobuz")}
              onMouseEnter={() => setHoveredService("qobuz")}
              onMouseLeave={() => setHoveredService(null)}
              style={{
                width: "100%", textAlign: "left", cursor: "pointer",
                background: hoveredService === "qobuz" ? "rgba(255,255,255,0.06)" : "rgba(255,255,255,0.03)",
                border: `1px solid ${hoveredService === "qobuz" ? "rgba(255,255,255,0.12)" : "rgba(255,255,255,0.07)"}`,
                borderRadius: 12, padding: "14px 16px",
                display: "flex", alignItems: "center", gap: 14,
                transition: "all 0.15s",
              }}
            >
              {/* Qobuz icon */}
              <div style={{
                width: 40, height: 40, borderRadius: 10, flexShrink: 0,
                background: "linear-gradient(135deg, #1a1a2e 0%, #16213e 100%)",
                border: "1px solid rgba(255,255,255,0.1)",
                display: "flex", alignItems: "center", justifyContent: "center",
                fontSize: 18,
              }}>
                🎵
              </div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 13.5, fontWeight: 600, color: "#E2E8F0", marginBottom: 3 }}>
                  Connect Qobuz Studio
                </div>
                <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10.5, color: "#64748B" }}>
                  Up to 24-Bit / 192kHz · FLAC Master Quality
                </div>
              </div>
              <div style={{
                fontSize: 11, fontWeight: 600, color: "#2563EB",
                background: "rgba(37,99,235,0.12)",
                border: "1px solid rgba(37,99,235,0.25)",
                borderRadius: 6, padding: "3px 9px",
                fontFamily: "'DM Mono', monospace", whiteSpace: "nowrap",
              }}>
                24-Bit / 192kHz
              </div>
            </button>

            {/* TIDAL card */}
            <button
              onClick={() => onConnectService("tidal")}
              onMouseEnter={() => setHoveredService("tidal")}
              onMouseLeave={() => setHoveredService(null)}
              style={{
                width: "100%", textAlign: "left", cursor: "pointer",
                background: hoveredService === "tidal" ? "rgba(255,255,255,0.06)" : "rgba(255,255,255,0.03)",
                border: `1px solid ${hoveredService === "tidal" ? "rgba(255,255,255,0.12)" : "rgba(255,255,255,0.07)"}`,
                borderRadius: 12, padding: "14px 16px",
                display: "flex", alignItems: "center", gap: 14,
                transition: "all 0.15s",
              }}
            >
              {/* TIDAL icon */}
              <div style={{
                width: 40, height: 40, borderRadius: 10, flexShrink: 0,
                background: "linear-gradient(135deg, #000000 0%, #111111 100%)",
                border: "1px solid rgba(255,255,255,0.1)",
                display: "flex", alignItems: "center", justifyContent: "center",
                fontSize: 18,
              }}>
                🌊
              </div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 13.5, fontWeight: 600, color: "#E2E8F0", marginBottom: 3 }}>
                  Connect TIDAL Max
                </div>
                <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10.5, color: "#64748B" }}>
                  HiRes FLAC · MQA / Dolby Atmos · OAuth 2.0
                </div>
              </div>
              <div style={{
                fontSize: 11, fontWeight: 600, color: "#0D9488",
                background: "rgba(13,148,136,0.12)",
                border: "1px solid rgba(13,148,136,0.25)",
                borderRadius: 6, padding: "3px 9px",
                fontFamily: "'DM Mono', monospace", whiteSpace: "nowrap",
              }}>
                HiRes FLAC
              </div>
            </button>
          </div>

          {/* Footer note */}
          <div style={{ display: "flex", alignItems: "center", gap: 8, paddingTop: 4 }}>
            <div style={{ flex: 1, height: 1, background: "rgba(255,255,255,0.05)" }} />
            <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, color: "#334155", whiteSpace: "nowrap" }}>
              ISRC cross-sync · P2P bit-perfect relay
            </span>
            <div style={{ flex: 1, height: 1, background: "rgba(255,255,255,0.05)" }} />
          </div>
        </div>
      </div>
    </div>
  )
}
