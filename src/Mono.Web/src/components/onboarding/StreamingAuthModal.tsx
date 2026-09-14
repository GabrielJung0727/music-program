import { useState, useEffect, useRef } from "react";
import { useMono, useMonoCommands } from "../../state/MonoProvider";
import { StreamingProvider } from "../../lib/protocol";
import { openExternal } from "../../lib/shell";

interface Props {
  isOpen: boolean;
  service: "qobuz" | "tidal" | null;
  onClose: () => void;
  onSuccess: (service: "qobuz" | "tidal") => void;
}

/* ─── Qobuz direct-API login ─────────────────────────────────────────────── */

interface QobuzLoginProps {
  onClose: () => void;
  onSuccess: () => void;
}

function QobuzLoginPanel({ onClose, onSuccess }: QobuzLoginProps) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const cmd = useMonoCommands();

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (loading) return;
    setLoading(true);
    setError(null);
    try {
      const res = await cmd.linkStreaming(StreamingProvider.Qobuz, password);
      if (res.ok) onSuccess();
      else setError(res.error ?? res.body ?? "연동에 실패했습니다.");
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }

  const accent = "#C9A227";
  const accentBg = "rgba(201,162,39,0.08)";
  const accentBorder = "rgba(201,162,39,0.25)";

  return (
    <>
      {/* Header */}
      <div className="flex items-start justify-between mb-5">
        <div className="flex items-center gap-3">
          <div
            className="w-9 h-9 rounded-xl flex items-center justify-center flex-shrink-0"
            style={{
              background: "linear-gradient(135deg, #C9A227 0%, #F5D06B 50%, #B8891A 100%)",
              fontFamily: "Georgia, serif",
            }}
          >
            <span style={{ fontSize: 15, fontWeight: 900, color: "#0B1120", letterSpacing: "-1px" }}>Q</span>
          </div>
          <div>
            <h2 className="modal-title">Qobuz Studio Login</h2>
            <p className="modal-subtitle" style={{ fontFamily: "'DM Mono', monospace" }}>
              Direct API · 24-bit / 192kHz Studio Master
            </p>
          </div>
        </div>
        <button type="button" onClick={onClose} className="modal-close-btn">✕</button>
      </div>

      <form onSubmit={handleSubmit} className="flex flex-col gap-4">
        {error && (
          <div
            className="rounded-xl px-3 py-2.5 text-[11px] leading-relaxed"
            style={{ background: "rgba(220,38,38,0.08)", border: "1px solid rgba(220,38,38,0.25)", color: "#DC2626", fontFamily: "'DM Mono', monospace" }}
          >{error}</div>
        )}

        <div className="flex flex-col gap-1.5">
          <label className="modal-field-label">Email or Username</label>
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="user@example.com"
            required
            autoComplete="email"
            className="modal-input-field"
          />
        </div>

        <div className="flex flex-col gap-1.5">
          <label className="modal-field-label">Password</label>
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="Enter password"
            required
            autoComplete="current-password"
            className="modal-input-field"
          />
        </div>

        <div className="modal-callout-amber">
          <span className="text-[8px]">●</span>
          Direct FLAC / Bit-Perfect Passthrough Enabled
        </div>

        <div className="flex items-center gap-3 pt-1">
          <button type="button" onClick={onClose} className="modal-btn-secondary">
            Cancel
          </button>
          <button type="submit" disabled={loading} className="modal-btn-primary">
            {loading ? (
              <>
                <svg className="w-3 h-3 animate-spin" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.5}>
                  <path strokeLinecap="round" d="M12 3a9 9 0 1 0 9 9" strokeDasharray="4 2" />
                </svg>
                Connecting…
              </>
            ) : (
              "Sign In and Connect"
            )}
          </button>
        </div>
      </form>
    </>
  );
}

/* ─── TIDAL Web OAuth panel ───────────────────────────────────────────────── */

type OAuthPhase = "idle" | "waiting" | "done";

interface TidalOAuthProps {
  onClose: () => void;
  onSuccess: () => void;
}

function TidalOAuthPanel({ onClose, onSuccess }: TidalOAuthProps) {
  const [phase, setPhase] = useState<OAuthPhase>("idle");
  const [copied, setCopied] = useState(false);
  const [authUrl, setAuthUrl] = useState("");
  const [error, setError] = useState<string | null>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const cmd = useMonoCommands();
  const { streamingAccounts } = useMono();

  useEffect(() => {
    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, []);

  // 토큰 교환은 Core 의 /oauth/callback 이 끝내고 모든 Control 에 계정을 밀어 준다.
  // 그 푸시가 도착하면 이 패널을 닫는다.
  const tidalConnected = streamingAccounts.some(
    (a) => a.provider === StreamingProvider.Tidal && a.connected,
  );
  useEffect(() => {
    if (phase === "waiting" && tidalConnected) {
      setPhase("done");
      timerRef.current = setTimeout(onSuccess, 400);
    }
  }, [phase, tidalConnected, onSuccess]);

  async function handleLaunch() {
    setError(null);
    const begin = await cmd.beginOAuth(StreamingProvider.Tidal);
    if (!begin) { setError("Core 에 연결하지 못했습니다."); return; }
    if (begin.already || begin.connected) { setPhase("done"); timerRef.current = setTimeout(onSuccess, 400); return; }
    if (!begin.authUrl) { setError(begin.note ?? "인증 URL 을 받지 못했습니다."); return; }
    setAuthUrl(begin.authUrl);
    openExternal(begin.authUrl);
    setPhase("waiting");
  }

  function handleCopy() {
    if (!authUrl) return;
    navigator.clipboard.writeText(authUrl).catch(() => {});
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  const tidalCyan = "#00C8DC";
  const tidalBg = "rgba(0,200,220,0.07)";
  const tidalBorder = "rgba(0,200,220,0.25)";

  return (
    <>
      {/* Header */}
      <div className="flex items-start justify-between mb-5">
        <div className="flex items-center gap-3">
          {/* TIDAL mark with pulsing ring when waiting */}
          <div className="relative flex-shrink-0">
            {phase === "waiting" && (
              <span
                className="absolute inset-0 rounded-xl animate-ping"
                style={{ background: "rgba(0,200,220,0.25)" }}
              />
            )}
            <div
              className="w-9 h-9 rounded-xl flex items-center justify-center bg-black relative z-10"
              style={{ border: `1.5px solid ${phase === "waiting" ? tidalCyan : "rgba(0,200,220,0.5)"}`, boxShadow: `0 0 ${phase === "waiting" ? "16px" : "10px"} rgba(0,200,220,${phase === "waiting" ? "0.4" : "0.2"})`, transition: "box-shadow 0.3s" }}
            >
              <svg width="18" height="14" viewBox="0 0 18 14" fill="none">
                <path d="M9 0 L12 4 L15 0 L18 4 L15 8 L12 4 L9 8 L6 4 L3 8 L0 4 L3 0 L6 4 Z" fill="#00C8DC" opacity="0.9" />
              </svg>
            </div>
          </div>

          <div>
            <h2 className="modal-title">TIDAL Web Authorization</h2>
            <p className="modal-subtitle" style={{ fontFamily: "'DM Mono', monospace" }}>
              Mono connects via secure web OAuth to stream HiRes FLAC and Master audio.
            </p>
          </div>
        </div>

        <button type="button" onClick={onClose} className="modal-close-btn">✕</button>
      </div>

      {/* OAuth badge */}
      <div
        className="flex items-center justify-between rounded-xl px-3 py-2.5 mb-4"
        style={{ background: tidalBg, border: `1px solid ${tidalBorder}` }}
      >
        <div className="flex items-center gap-2">
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke={tidalCyan} strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
            <rect x="3" y="11" width="18" height="11" rx="2" ry="2" />
            <path d="M7 11V7a5 5 0 0 1 10 0v4" />
          </svg>
          <span className="text-[10px] font-semibold" style={{ fontFamily: "'DM Mono', monospace", color: tidalCyan }}>
            OAuth 2.0 PKCE Direct Link
          </span>
        </div>
        <span className="text-[10px] text-zinc-400" style={{ fontFamily: "'DM Mono', monospace" }}>
          login.tidal.com
        </span>
      </div>

      {/* Status area */}
      <div className="flex flex-col gap-3">
        {error && (
          <div
            className="rounded-xl px-3 py-2.5 text-[11px] leading-relaxed"
            style={{ background: "rgba(220,38,38,0.08)", border: "1px solid rgba(220,38,38,0.25)", color: "#DC2626", fontFamily: "'DM Mono', monospace" }}
          >{error}</div>
        )}
        {phase === "idle" && (
          <p className="text-xs text-zinc-500 leading-relaxed">
            Clicking the button below opens TIDAL's secure authorization page in your browser.
            After you log in, Mono receives an OAuth token — no password is ever shared with this app.
          </p>
        )}

        {phase === "waiting" && (
          <div
            className="flex items-center gap-3 rounded-xl px-3 py-3"
            style={{ background: tidalBg, border: `1px solid ${tidalBorder}` }}
          >
            <svg className="w-3.5 h-3.5 animate-spin flex-shrink-0" viewBox="0 0 24 24" fill="none" stroke={tidalCyan} strokeWidth={2.5}>
              <path strokeLinecap="round" d="M12 3a9 9 0 1 0 9 9" strokeDasharray="4 2" />
            </svg>
            <p className="text-[11px] font-medium" style={{ fontFamily: "'DM Mono', monospace", color: tidalCyan }}>
              Waiting for authorization from login.tidal.com…
            </p>
          </div>
        )}

        {phase === "done" && (
          <div
            className="flex items-center gap-2 rounded-xl px-3 py-2.5"
            style={{ background: "rgba(16,185,129,0.08)", border: "1px solid rgba(16,185,129,0.25)" }}
          >
            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="#10b981" strokeWidth={2.5} strokeLinecap="round" strokeLinejoin="round">
              <polyline points="20 6 9 17 4 12" />
            </svg>
            <p className="text-[11px] font-semibold" style={{ fontFamily: "'DM Mono', monospace", color: "#10b981" }}>
              Authorization confirmed — connecting…
            </p>
          </div>
        )}

        {/* Main action */}
        <button
          type="button"
          onClick={handleLaunch}
          disabled={phase !== "idle"}
          className="w-full py-3 rounded-xl text-xs font-semibold text-white flex items-center justify-center gap-2 transition-all disabled:opacity-50 disabled:cursor-not-allowed"
          style={{ background: phase === "idle" ? "#000" : "#18181b", border: `1px solid ${phase === "idle" ? tidalCyan : "transparent"}`, boxShadow: phase === "idle" ? `0 0 12px rgba(0,200,220,0.18)` : "none" }}
        >
          <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
            <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" />
            <polyline points="15 3 21 3 21 9" />
            <line x1="10" y1="14" x2="21" y2="3" />
          </svg>
          Open TIDAL in Browser to Authorize
        </button>

        {/* Fallback copy link */}
        <button
          type="button"
          onClick={handleCopy}
          className="w-full py-2 rounded-xl text-[10px] font-medium flex items-center justify-center gap-1.5 modal-btn-secondary"
          style={{ fontFamily: "'DM Mono', monospace" }}
        >
          {copied ? (
            <>
              <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="#10b981" strokeWidth={2.5} strokeLinecap="round" strokeLinejoin="round">
                <polyline points="20 6 9 17 4 12" />
              </svg>
              <span style={{ color: "#10b981" }}>Link Copied</span>
            </>
          ) : (
            <>
              <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
                <rect x="9" y="9" width="13" height="13" rx="2" ry="2" />
                <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
              </svg>
              Copy Authorization Link
            </>
          )}
        </button>
      </div>
    </>
  );
}

/* ─── Root modal shell ────────────────────────────────────────────────────── */

export default function StreamingAuthModal({ isOpen, service, onClose, onSuccess }: Props) {
  const isQobuz = service === "qobuz";
  const accent = isQobuz ? "#C9A227" : "#00C8DC";

  // Reset inner panel state on each open by keying on service+isOpen
  const [panelKey, setPanelKey] = useState(0);
  useEffect(() => {
    if (isOpen) setPanelKey((k) => k + 1);
  }, [isOpen, service]);

  if (!isOpen || !service) return null;

  function handleSuccess() {
    if (service) onSuccess(service);
    onClose();
  }

  return (
    <div
      className="fixed inset-0 z-[300] flex items-center justify-center p-4"
      style={{ background: "rgba(0,0,0,0.50)", backdropFilter: "blur(6px)" }}
      onClick={onClose}
    >
      <div
        className="w-full max-w-md modal-studio-surface rounded-2xl relative"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Service accent strip */}
        <div className="h-1 rounded-t-2xl" style={{ background: accent }} />

        <div className="p-6" key={panelKey}>
          {isQobuz ? (
            <QobuzLoginPanel onClose={onClose} onSuccess={handleSuccess} />
          ) : (
            <TidalOAuthPanel onClose={onClose} onSuccess={handleSuccess} />
          )}
        </div>
      </div>
    </div>
  );
}
