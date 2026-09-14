import { useState } from "react"
import { useMonoCommands } from "../state/MonoProvider"
import { StreamingProvider } from "../lib/protocol"
import { openExternal } from "../lib/shell"

type Service = "qobuz" | "tidal"

interface Props {
  service: Service | null
  isOpen: boolean
  onClose: () => void
  onSuccessfulLogin: (service: Service) => void
}

// ── Shared backdrop / shell ───────────────────────────────────────────────────
function ModalShell({ onClose, children }: { onClose: () => void; children: React.ReactNode }) {
  return (
    <div
      className="modal-auth-backdrop"
      onClick={(e) => { if (e.target === e.currentTarget) onClose() }}
    >
      {children}
    </div>
  )
}

// ── Qobuz Flow ────────────────────────────────────────────────────────────────
function QobuzFlow({ onClose, onSuccess }: { onClose: () => void; onSuccess: () => void }) {
  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const [showPw, setShowPw] = useState(false)
  const [keepLoggedIn, setKeepLoggedIn] = useState(true)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const cmd = useMonoCommands()

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setLoading(true)
    setError(null)
    try {
      // Core 의 Qobuz 어댑터는 토큰(또는 비밀번호)을 그대로 받는다.
      const res = await cmd.linkStreaming(StreamingProvider.Qobuz, password)
      if (res.ok) onSuccess()
      else setError(res.error ?? res.body ?? "연동에 실패했습니다.")
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="modal-auth-surface" style={{ width: 440 }} onClick={(e) => e.stopPropagation()}>

      {/* Header */}
      <div className="modal-auth-header">
        <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between" }}>
          <div>
            <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 10 }}>
              {/* Qobuz emblem */}
              <div style={{
                width: 34, height: 34, borderRadius: 8, flexShrink: 0,
                background: "linear-gradient(135deg, #C9A227 0%, #F5D06B 50%, #B8891A 100%)",
                display: "flex", alignItems: "center", justifyContent: "center",
                boxShadow: "0 2px 12px rgba(201,162,39,0.35)",
              }}>
                <span style={{ fontSize: 14, fontWeight: 900, color: "#0B1120", fontFamily: "Georgia, serif", letterSpacing: "-1px" }}>Q</span>
              </div>
              <div>
                <div style={{ fontSize: 18, fontWeight: 800, color: "var(--modal-auth-title)", fontFamily: "Georgia, 'Times New Roman', serif", letterSpacing: "0.01em" }}>
                  Qobuz
                </div>
                <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 9, fontWeight: 700, color: "#C9A227", letterSpacing: "0.14em", textTransform: "uppercase" }}>
                  Studio
                </div>
              </div>
            </div>
            <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--modal-auth-subtitle)", letterSpacing: "0.04em" }}>
              Direct API Login · 24-Bit / 192kHz Studio Master Streaming
            </div>
          </div>
          <button
            onClick={onClose}
            style={{ background: "none", border: "none", cursor: "pointer", color: "var(--modal-auth-close)", fontSize: 18, lineHeight: 1, padding: 4, transition: "color 0.15s" }}
            onMouseEnter={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "var(--modal-auth-close-hover)")}
            onMouseLeave={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "var(--modal-auth-close)")}
          >
            ✕
          </button>
        </div>
      </div>

      {/* Form body */}
      <div style={{ padding: "28px 32px 32px", background: "var(--modal-auth-bg)" }}>
        {error && !loading && (
          <div style={{
            marginBottom: 18, padding: "10px 14px", borderRadius: 9,
            background: "rgba(220,38,38,0.08)", border: "1px solid rgba(220,38,38,0.25)",
            fontFamily: "'DM Mono', monospace", fontSize: 11.5, color: "#DC2626", lineHeight: 1.5,
          }}>{error}</div>
        )}
        {loading ? (
          <div style={{ display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", gap: 18, paddingTop: 12, paddingBottom: 12 }}>
            <div style={{
              width: 40, height: 40, border: "3px solid rgba(201,162,39,0.2)",
              borderTop: "3px solid #C9A227", borderRadius: "50%",
              animation: "qobuz-spin 0.8s linear infinite",
            }} />
            <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 12, color: "var(--modal-auth-subtitle)", letterSpacing: "0.06em" }}>
              Verifying Qobuz Studio Credentials...
            </div>
            <style>{`@keyframes qobuz-spin { to { transform: rotate(360deg) } }`}</style>
          </div>
        ) : (
          <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 16 }}>

            {/* Email */}
            <div>
              <label style={{ display: "block", fontSize: 11, fontWeight: 600, color: "var(--modal-auth-label)", marginBottom: 6, fontFamily: "'DM Mono', monospace", letterSpacing: "0.05em", textTransform: "uppercase" }}>
                Email / Username
              </label>
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="your@email.com"
                required
                className="modal-auth-input"
                onFocus={(e) => ((e.target as HTMLInputElement).style.borderColor = "#C9A227")}
                onBlur={(e) => ((e.target as HTMLInputElement).style.borderColor = "var(--modal-auth-input-border)")}
              />
            </div>

            {/* Password */}
            <div>
              <label style={{ display: "block", fontSize: 11, fontWeight: 600, color: "var(--modal-auth-label)", marginBottom: 6, fontFamily: "'DM Mono', monospace", letterSpacing: "0.05em", textTransform: "uppercase" }}>
                Password
              </label>
              <div style={{ position: "relative" }}>
                <input
                  type={showPw ? "text" : "password"}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="••••••••"
                  required
                  className="modal-auth-input modal-auth-input--with-toggle"
                  onFocus={(e) => ((e.target as HTMLInputElement).style.borderColor = "#C9A227")}
                  onBlur={(e) => ((e.target as HTMLInputElement).style.borderColor = "var(--modal-auth-input-border)")}
                />
                <button
                  type="button"
                  onClick={() => setShowPw(!showPw)}
                  style={{
                    position: "absolute", right: 12, top: "50%", transform: "translateY(-50%)",
                    background: "none", border: "none", cursor: "pointer",
                    color: "var(--modal-auth-show-hide)", fontSize: 13, lineHeight: 1, padding: 2,
                    transition: "color 0.15s", fontFamily: "inherit",
                  }}
                  onMouseEnter={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "var(--modal-auth-show-hide-hover)")}
                  onMouseLeave={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "var(--modal-auth-show-hide)")}
                >
                  {showPw ? "Hide" : "Show"}
                </button>
              </div>
            </div>

            {/* Keep logged in */}
            <label style={{ display: "flex", alignItems: "flex-start", gap: 10, cursor: "pointer", userSelect: "none" }}>
              <input
                type="checkbox"
                checked={keepLoggedIn}
                onChange={(e) => setKeepLoggedIn(e.target.checked)}
                style={{ marginTop: 2, width: 14, height: 14, accentColor: "#C9A227", cursor: "pointer", flexShrink: 0 }}
              />
              <span style={{ fontSize: 12, color: "var(--modal-auth-checkbox-text)", lineHeight: 1.5 }}>
                Keep me logged in for offline bit-perfect playback
              </span>
            </label>

            {/* Submit */}
            <button
              type="submit"
              style={{
                width: "100%", padding: "12px 20px",
                background: "linear-gradient(135deg, #C9A227 0%, #E8BF3A 50%, #B8891A 100%)",
                border: "none", borderRadius: 10, cursor: "pointer",
                fontSize: 13, fontWeight: 700, color: "#0B1120",
                fontFamily: "inherit", letterSpacing: "0.02em",
                boxShadow: "0 4px 16px rgba(201,162,39,0.3)",
                transition: "opacity 0.15s, transform 0.12s",
                marginTop: 4,
              }}
              onMouseEnter={(e) => { const b = e.currentTarget as HTMLButtonElement; b.style.opacity = "0.9"; b.style.transform = "scale(1.01)" }}
              onMouseLeave={(e) => { const b = e.currentTarget as HTMLButtonElement; b.style.opacity = "1"; b.style.transform = "scale(1)" }}
            >
              Sign In to Qobuz
            </button>
          </form>
        )}
      </div>
    </div>
  )
}

// ── TIDAL Flow ────────────────────────────────────────────────────────────────
type TidalState = "launch" | "authorize" | "exchanging"

function TidalFlow({ onClose, onSuccess }: { onClose: () => void; onSuccess: () => void }) {
  const [state, setState] = useState<TidalState>("launch")
  const [note, setNote] = useState<string | null>(null)
  const cmd = useMonoCommands()

  // Core 가 PKCE 를 만들고 authorize URL 을 준다. 리디렉션은 Core 의 /oauth/callback 으로
  // 돌아오고, 거기서 토큰 교환까지 끝낸 뒤 모든 Control 에 계정·카탈로그를 밀어 준다.
  const handleLaunch = async () => {
    const begin = await cmd.beginOAuth(StreamingProvider.Tidal)
    if (!begin) { setNote("Core 에 연결하지 못했습니다."); return }
    if (begin.already || begin.connected) { onSuccess(); return }
    if (!begin.authUrl) { setNote(begin.note ?? "인증 URL 을 받지 못했습니다."); return }
    openExternal(begin.authUrl)
    setState("authorize")
  }

  const handleConfirmAuthorize = () => {
    setState("exchanging")
    // 토큰 교환은 Core 의 콜백이 끝낸다. 여기서는 계정 푸시를 기다린다.
    onSuccess()
  }

  return (
    <div className="modal-auth-surface" style={{ width: 460 }} onClick={(e) => e.stopPropagation()}>

      {/* Header */}
      <div className="modal-auth-header">
        <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between" }}>
          <div>
            <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 10 }}>
              <div style={{
                width: 34, height: 34, borderRadius: 8, background: "#000", flexShrink: 0,
                border: "1.5px solid rgba(0,200,220,0.5)",
                display: "flex", alignItems: "center", justifyContent: "center",
                boxShadow: "0 0 16px rgba(0,200,220,0.2)",
              }}>
                <svg width="18" height="14" viewBox="0 0 18 14" fill="none">
                  <path d="M9 0 L12 4 L15 0 L18 4 L15 8 L12 4 L9 8 L6 4 L3 8 L0 4 L3 0 L6 4 Z" fill="#00C8DC" opacity="0.9" />
                </svg>
              </div>
              <div>
                <div style={{ fontSize: 18, fontWeight: 800, color: "var(--modal-auth-title)", fontFamily: "'Inter', system-ui, sans-serif", letterSpacing: "0.12em" }}>
                  TIDAL
                </div>
                <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 9, fontWeight: 600, color: "#00C8DC", letterSpacing: "0.14em", textTransform: "uppercase" }}>
                  HiFi Max
                </div>
              </div>
            </div>
            <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--modal-auth-subtitle)", letterSpacing: "0.04em" }}>
              TIDAL Connect &amp; Web OAuth Authorization
            </div>
          </div>
          <button
            onClick={onClose}
            style={{ background: "none", border: "none", cursor: "pointer", color: "var(--modal-auth-close)", fontSize: 18, lineHeight: 1, padding: 4, transition: "color 0.15s" }}
            onMouseEnter={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "var(--modal-auth-close-hover)")}
            onMouseLeave={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "var(--modal-auth-close)")}
          >
            ✕
          </button>
        </div>
      </div>

      {/* Body */}
      <div style={{ padding: "28px 32px 32px", background: "var(--modal-auth-bg)" }}>

        {note && (
          <div style={{
            marginBottom: 18, padding: "10px 14px", borderRadius: 9,
            background: "rgba(220,38,38,0.08)", border: "1px solid rgba(220,38,38,0.25)",
            fontFamily: "'DM Mono', monospace", fontSize: 11.5, color: "#DC2626", lineHeight: 1.5,
          }}>{note}</div>
        )}

        {/* State A — Launch */}
        {state === "launch" && (
          <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
            <p style={{ fontSize: 13, color: "var(--modal-auth-body-text)", lineHeight: 1.6, margin: 0 }}>
              TIDAL requires web-based authentication to stream HiRes FLAC. You&apos;ll be directed to authorize Mono Audio Player via TIDAL&apos;s secure OAuth flow.
            </p>

            {/* Auth card */}
            <div style={{
              background: "rgba(0,200,220,0.05)", border: "1px solid rgba(0,200,220,0.18)",
              borderRadius: 14, padding: "18px 20px",
              display: "flex", alignItems: "center", gap: 16,
            }}>
              <div style={{
                width: 44, height: 44, borderRadius: 10,
                background: "rgba(0,200,220,0.08)", border: "1px solid rgba(0,200,220,0.2)",
                display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0,
              }}>
                <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#00C8DC" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                  <circle cx="12" cy="12" r="10"/>
                  <line x1="2" y1="12" x2="22" y2="12"/>
                  <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/>
                </svg>
              </div>
              <div>
                <div style={{ fontSize: 13, fontWeight: 600, color: "var(--modal-auth-title)", marginBottom: 3 }}>
                  tidal.com/authorize
                </div>
                <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--modal-auth-subtitle)" }}>
                  Secure PKCE OAuth 2.0 · TLS 1.3
                </div>
              </div>
            </div>

            <button
              onClick={() => { void handleLaunch() }}
              style={{
                width: "100%", padding: "13px 20px",
                background: "var(--modal-tidal-launch-bg)",
                border: "1.5px solid var(--modal-tidal-launch-border)",
                borderRadius: 10, cursor: "pointer",
                fontSize: 13, fontWeight: 700, color: "var(--modal-tidal-launch-text)",
                fontFamily: "inherit", letterSpacing: "0.04em",
                display: "flex", alignItems: "center", justifyContent: "center", gap: 8,
                transition: "background 0.15s, box-shadow 0.15s",
                boxSizing: "border-box",
              }}
              onMouseEnter={(e) => {
                const b = e.currentTarget as HTMLButtonElement
                b.style.background = "var(--modal-tidal-launch-hover-bg)"
                b.style.boxShadow = "var(--modal-tidal-launch-hover-shadow)"
              }}
              onMouseLeave={(e) => {
                const b = e.currentTarget as HTMLButtonElement
                b.style.background = "var(--modal-tidal-launch-bg)"
                b.style.boxShadow = "none"
              }}
            >
              Open TIDAL.com to Authorize ↗
            </button>
          </div>
        )}

        {/* State B — Browser authorization simulation */}
        {state === "authorize" && (
          <div style={{ display: "flex", flexDirection: "column", gap: 18 }}>
            {/* Browser chrome — stays dark intentionally (simulates a browser window) */}
            <div style={{
              background: "var(--modal-tidal-sim-bg)", border: "1px solid var(--modal-tidal-sim-border)",
              borderRadius: 12, overflow: "hidden",
            }}>
              {/* Tab bar */}
              <div style={{
                background: "var(--modal-tidal-sim-bar-bg)", padding: "8px 14px",
                display: "flex", alignItems: "center", gap: 8,
                borderBottom: "1px solid var(--modal-tidal-sim-bar-border)",
              }}>
                <div style={{ display: "flex", gap: 5 }}>
                  {["#FF5F57", "#FEBC2E", "#28C840"].map((c, i) => (
                    <div key={i} style={{ width: 10, height: 10, borderRadius: "50%", background: c, opacity: 0.7 }} />
                  ))}
                </div>
                <div style={{
                  flex: 1, background: "var(--modal-tidal-sim-url-bg)", borderRadius: 6, padding: "4px 10px",
                  fontFamily: "'DM Mono', monospace", fontSize: 10, color: "#6B7280",
                  display: "flex", alignItems: "center", gap: 6,
                }}>
                  <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="#22C55E" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                    <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>
                  </svg>
                  tidal.com/oauth/authorize?client_id=mono&response_type=code&code_challenge_method=S256
                </div>
              </div>

              {/* Auth scopes content */}
              <div style={{ padding: "20px 22px", display: "flex", flexDirection: "column", gap: 16 }}>
                <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                  <svg width="20" height="16" viewBox="0 0 18 14" fill="none">
                    <path d="M9 0 L12 4 L15 0 L18 4 L15 8 L12 4 L9 8 L6 4 L3 8 L0 4 L3 0 L6 4 Z" fill="#00C8DC" opacity="0.9" />
                  </svg>
                  <span style={{ fontSize: 13, fontWeight: 700, color: "var(--modal-tidal-sim-content-text)", letterSpacing: "0.1em" }}>TIDAL</span>
                </div>
                <div>
                  <div style={{ fontSize: 14, fontWeight: 600, color: "var(--modal-tidal-sim-content-text)", marginBottom: 6 }}>
                    Mono Audio Player is requesting access to your TIDAL HiFi/Max subscription.
                  </div>
                  <div style={{ fontSize: 12, color: "var(--modal-tidal-sim-meta-text)", marginBottom: 14, lineHeight: 1.5 }}>
                    This app will have permission to:
                  </div>
                  <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                    {[
                      { icon: "▶", label: "Stream Lossless Audio" },
                      { icon: "♪", label: "Read Playlists" },
                      { icon: "◈", label: "Access My Collection" },
                    ].map((scope) => (
                      <div key={scope.label} style={{ display: "flex", alignItems: "center", gap: 10 }}>
                        <div style={{
                          width: 22, height: 22, borderRadius: 6,
                          background: "rgba(0,200,220,0.1)", border: "1px solid rgba(0,200,220,0.2)",
                          display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0,
                          fontSize: 10, color: "#00C8DC",
                        }}>
                          {scope.icon}
                        </div>
                        <span style={{ fontSize: 12, color: "#94A3B8" }}>{scope.label}</span>
                      </div>
                    ))}
                  </div>
                </div>
              </div>
            </div>

            {/* Action buttons */}
            <div style={{ display: "flex", gap: 10 }}>
              <button
                onClick={onClose}
                style={{
                  flex: 1, padding: "11px 16px",
                  background: "transparent", border: "1px solid var(--modal-auth-cancel-border)",
                  borderRadius: 10, cursor: "pointer",
                  fontSize: 13, fontWeight: 600, color: "var(--modal-auth-cancel-text)",
                  fontFamily: "inherit", transition: "border-color 0.15s, color 0.15s",
                }}
                onMouseEnter={(e) => {
                  const b = e.currentTarget as HTMLButtonElement
                  b.style.borderColor = "var(--modal-auth-cancel-hover-border)"
                  b.style.color = "var(--modal-auth-cancel-hover-text)"
                }}
                onMouseLeave={(e) => {
                  const b = e.currentTarget as HTMLButtonElement
                  b.style.borderColor = "var(--modal-auth-cancel-border)"
                  b.style.color = "var(--modal-auth-cancel-text)"
                }}
              >
                Cancel
              </button>
              <button
                onClick={handleConfirmAuthorize}
                style={{
                  flex: 2, padding: "11px 16px",
                  background: "#00C8DC", border: "none",
                  borderRadius: 10, cursor: "pointer",
                  fontSize: 13, fontWeight: 700, color: "#000000",
                  fontFamily: "inherit", letterSpacing: "0.02em",
                  boxShadow: "0 4px 16px rgba(0,200,220,0.3)",
                  transition: "opacity 0.15s",
                }}
                onMouseEnter={(e) => ((e.currentTarget as HTMLButtonElement).style.opacity = "0.88")}
                onMouseLeave={(e) => ((e.currentTarget as HTMLButtonElement).style.opacity = "1")}
              >
                Confirm &amp; Authorize
              </button>
            </div>
          </div>
        )}

        {/* State C — Exchanging token */}
        {state === "exchanging" && (
          <div style={{ display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", gap: 18, paddingTop: 12, paddingBottom: 12 }}>
            <div style={{
              width: 40, height: 40, border: "3px solid rgba(0,200,220,0.15)",
              borderTop: "3px solid #00C8DC", borderRadius: "50%",
              animation: "tidal-spin 0.8s linear infinite",
            }} />
            <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 12, color: "var(--modal-auth-subtitle)", letterSpacing: "0.06em" }}>
              Exchanging OAuth Token (PKCE)...
            </div>
            <style>{`@keyframes tidal-spin { to { transform: rotate(360deg) } }`}</style>
          </div>
        )}

      </div>
    </div>
  )
}

// ── Public component ──────────────────────────────────────────────────────────
export default function StreamingLoginModal({ service, isOpen, onClose, onSuccessfulLogin }: Props) {
  if (!isOpen || !service) return null

  const handleSuccess = () => {
    onSuccessfulLogin(service)
    onClose()
  }

  return (
    <ModalShell onClose={onClose}>
      {service === "qobuz" ? (
        <QobuzFlow onClose={onClose} onSuccess={handleSuccess} />
      ) : (
        <TidalFlow onClose={onClose} onSuccess={handleSuccess} />
      )}
    </ModalShell>
  )
}
