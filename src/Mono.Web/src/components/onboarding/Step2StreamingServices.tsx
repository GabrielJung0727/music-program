interface Props {
  onNext: (services: { qobuz: boolean; tidal: boolean }) => void;
  onBack: () => void;
  onSkip: () => void;
  connectedServices: { qobuz: boolean; tidal: boolean };
  onConnectService: (service: "qobuz" | "tidal") => void;
}

interface ServiceCardProps {
  service: "qobuz" | "tidal";
  connected: boolean;
  onConnect: () => void;
  onDisconnect: () => void;
}

function QobuzMark() {
  return (
    <div
      className="w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0"
      style={{
        background: "linear-gradient(135deg, #C9A227 0%, #F5D06B 50%, #B8891A 100%)",
        boxShadow: "0 2px 8px rgba(201,162,39,0.25)",
        fontFamily: "Georgia, 'Times New Roman', serif",
      }}
    >
      <span className="text-sm font-black" style={{ color: "#0B1120", letterSpacing: "-1px" }}>
        Q
      </span>
    </div>
  );
}

function TidalMark() {
  return (
    <div
      className="w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0 bg-black"
      style={{ border: "1.5px solid rgba(0,200,220,0.5)", boxShadow: "0 0 10px rgba(0,200,220,0.15)" }}
    >
      <svg width="18" height="14" viewBox="0 0 18 14" fill="none">
        <path d="M9 0 L12 4 L15 0 L18 4 L15 8 L12 4 L9 8 L6 4 L3 8 L0 4 L3 0 L6 4 Z" fill="#00C8DC" opacity="0.9" />
      </svg>
    </div>
  );
}

function ServiceCard({ service, connected, onConnect, onDisconnect }: ServiceCardProps) {
  const isQobuz = service === "qobuz";

  const name = isQobuz ? "Qobuz Studio" : "TIDAL Max";
  const spec = isQobuz
    ? "Up to 24-Bit / 192kHz Studio Master (Direct API)"
    : "HiRes FLAC & Lossless Master Stream (OAuth 2.0)";
  const connectedLabel = isQobuz ? "● Connected (Studio Active)" : "● Connected (Max Active)";
  const connectLabel = isQobuz ? "Connect Qobuz" : "Connect TIDAL";

  return (
    <div className="ob-card flex items-start gap-4 px-5 py-4 rounded-xl border">
      {isQobuz ? <QobuzMark /> : <TidalMark />}

      <div className="flex flex-col gap-1.5 flex-1 min-w-0">
        <div className="flex items-start justify-between gap-3">
          <div>
            <p className="text-sm font-semibold ob-title leading-tight">{name}</p>
            <p
              className="text-[10px] ob-body mt-0.5"
              style={{ fontFamily: "'DM Mono', monospace" }}
            >
              {spec}
            </p>
          </div>

          {connected ? (
            <div className="flex items-center gap-2 flex-shrink-0">
              <span
                className="inline-flex items-center gap-1 text-[10px] font-medium text-emerald-700 bg-emerald-50 border border-emerald-200/80 px-2 py-0.5 rounded-full"
                style={{ fontFamily: "'DM Mono', monospace" }}
              >
                {connectedLabel}
              </span>
              <button
                onClick={onDisconnect}
                className="text-[10px] text-zinc-400 hover:text-zinc-700 transition-colors underline underline-offset-2"
                style={{ fontFamily: "'DM Mono', monospace" }}
              >
                Disconnect
              </button>
            </div>
          ) : (
            <button
              onClick={onConnect}
              className="ob-action-btn text-xs font-semibold px-4 py-2 rounded-xl flex-shrink-0"
            >
              {connectLabel}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}

export default function Step2StreamingServices({
  onNext,
  onBack,
  onSkip,
  connectedServices,
  onConnectService,
}: Props) {
  function handleDisconnect(_service: "qobuz" | "tidal") {
    // In a real app this would call an API; here it's a no-op placeholder
    // since the parent controls connectedServices state
  }

  return (
    <div className="flex flex-col gap-8 w-full max-w-xl mx-auto px-1">
      {/* Header */}
      <div className="flex flex-col gap-1.5">
        <span
          className="text-[10px] tracking-widest text-zinc-400 uppercase"
          style={{ fontFamily: "'DM Mono', monospace" }}
        >
          Step 02 / 04 · Streaming Accounts
        </span>
        <h2 className="text-xl font-semibold ob-title tracking-tight leading-snug">
          Connect Hi-Res Master Streaming
        </h2>
        <p className="text-sm ob-body leading-relaxed max-w-md">
          Link your active subscription to stream 24-bit bit-perfect audio and synchronize with
          live host lounges.
        </p>
      </div>

      {/* Section 1: Service Cards */}
      <div className="flex flex-col gap-3">
        <span
          className="text-[10px] font-semibold tracking-widest text-zinc-400 uppercase"
          style={{ fontFamily: "'DM Mono', monospace" }}
        >
          Streaming Services
        </span>
        <ServiceCard
          service="qobuz"
          connected={connectedServices.qobuz}
          onConnect={() => onConnectService("qobuz")}
          onDisconnect={() => handleDisconnect("qobuz")}
        />
        <ServiceCard
          service="tidal"
          connected={connectedServices.tidal}
          onConnect={() => onConnectService("tidal")}
          onDisconnect={() => handleDisconnect("tidal")}
        />
      </div>

      {/* Section 2: Bit-Perfect Guarantee */}
      <div className="ob-infobox border rounded-xl p-3 flex items-start gap-2.5">
        <svg
          className="w-3.5 h-3.5 ob-body flex-shrink-0 mt-0.5"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth={2}
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
        </svg>
        <p className="text-xs ob-body leading-relaxed">
          Mono streams directly from provider CDNs using your authenticated credentials. Zero
          intermediary transcoding, zero DSP tampering.
        </p>
      </div>

      {/* Bottom Action Bar */}
      <div className="ob-divider flex items-center justify-between pt-2 border-t">
        <div className="flex items-center gap-4">
          <button
            onClick={onBack}
            className="text-xs ob-body hover:text-zinc-900 font-medium px-3 py-2 rounded-lg transition-colors"
          >
            ← Back
          </button>
          <button
            onClick={onSkip}
            className="text-xs text-zinc-400 hover:text-zinc-600 transition-colors underline underline-offset-2"
          >
            Skip for Now (Local Only)
          </button>
        </div>
        <button
          onClick={() => onNext(connectedServices)}
          className="ob-action-btn text-xs font-semibold px-6 py-2.5 rounded-xl shadow-sm"
        >
          Continue to Profile →
        </button>
      </div>
    </div>
  );
}
