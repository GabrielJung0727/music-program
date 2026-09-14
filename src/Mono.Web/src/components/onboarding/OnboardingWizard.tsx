import { useCallback, useEffect, useState } from "react";
import { Sun, Moon } from "lucide-react";
import StreamingAuthModal from "./StreamingAuthModal";
import { type ListenerProfile } from "./Step3AudiophileRig";
import { api } from "../../lib/rest";
import { outputStatus, startOutput } from "../../lib/shell";
import { MonoIcon } from "../icons/MonoIcons";
import { type AudioEngineConfig } from "./Step1AudioEngine";
import { useMono } from "../../state/MonoProvider";
import { StreamingProvider } from "../../lib/protocol";

// ─── Types ────────────────────────────────────────────────────────────────────

interface AudioDevice {
  id: string;
  name: string;
  driverType: "ASIO" | "WASAPI_EXCLUSIVE";
  sampleRates: string[];
  bitDepth: string;
  isBitPerfectVerified: boolean;
  description: string;
}

// ─── Constants ────────────────────────────────────────────────────────────────

/** Core 가 아는 출력 엔드포인트를 온보딩의 장치 카드 모양으로 옮긴다. */
function endpointsToDevices(endpoints: EndpointRecord[]): AudioDevice[] {
  return endpoints.map((e) => ({
    id: e.peerId,
    name: e.displayName || e.device || e.peerId,
    driverType: e.exclusiveMode ? "WASAPI_EXCLUSIVE" : "ASIO",
    sampleRates: sampleRateLadder(e.maxSampleRate),
    bitDepth: `${e.maxBitDepth}-bit`,
    isBitPerfectVerified: e.exclusiveMode,
    description: [
      e.device,
      e.supportsDsd ? "DSD 지원" : null,
      e.latencyMs ? `보고 지연 ${e.latencyMs}ms` : null,
      e.online ? "온라인" : "오프라인",
    ].filter(Boolean).join(" · "),
  }))
}

/** 장치가 보고한 최대 레이트까지의 사다리. 화면에 "지원 레이트"로 나간다. */
function sampleRateLadder(maxRate: number): string[] {
  const ladder = [44100, 48000, 88200, 96000, 176400, 192000, 352800, 384000]
  const supported = ladder.filter((r) => r <= (maxRate || 48000))
  return (supported.length > 0 ? supported : [44100]).map((r) => `${r / 1000}kHz`)
}

interface EndpointRecord {
  peerId: string
  displayName: string
  maxSampleRate: number
  maxBitDepth: number
  supportsDsd: boolean
  exclusiveMode: boolean
  latencyMs: number
  hardwareVolume: boolean
  volumePercent: number
  device?: string | null
  roomId?: string | null
  online: boolean
  lastSeen?: string
}

const DAC_NAME_MAP: Record<string, string> = {
  "holo-spring-3": "Holo Audio Spring 3 (ASIO Direct)",
  "chord-hugo-tt2": "Chord Hugo TT 2 (Kernel Streaming / ASIO)",
  "system-wasapi": "Default System Output (WASAPI Exclusive)",
};

const DAC_SPEC_MAP: Record<string, string> = {
  "holo-spring-3": "32-Bit / DSD512 Ready",
  "chord-hugo-tt2": "32-Bit / 768kHz Ready",
  "system-wasapi": "24-Bit / 192kHz Ready",
};

const BUFFER_SIZES = [64, 128, 256, 512, 1024] as const;

const AVATAR_COLORS = [
  { key: "zinc",    bg: "#18181b", text: "#ffffff" },
  { key: "slate",   bg: "#1e293b", text: "#ffffff" },
  { key: "amber",   bg: "#92400e", text: "#ffffff" },
  { key: "emerald", bg: "#064e3b", text: "#ffffff" },
  { key: "rose",    bg: "#881337", text: "#ffffff" },
  { key: "indigo",  bg: "#312e81", text: "#ffffff" },
];

const STEPS = [
  { num: "01", label: "Audio Engine & DAC" },
  { num: "02", label: "Streaming Master Accounts" },
  { num: "03", label: "Listener Profile & Gear" },
];

// ─── Helpers ──────────────────────────────────────────────────────────────────

function calcLatency(bufferSize: number): string {
  return ((bufferSize / 44100) * 1000).toFixed(1);
}

function initials(name: string): string {
  return name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? "")
    .join("");
}

function mono(extra?: string) {
  return { fontFamily: "'DM Mono', monospace", ...(extra ? {} : {}) };
}

// ─── Shared sub-components ────────────────────────────────────────────────────

function SectionLabel({ children }: { children: React.ReactNode }) {
  return (
    <p
      className="text-[10px] font-semibold tracking-widest text-zinc-400 uppercase mb-3"
      style={mono()}
    >
      {children}
    </p>
  );
}

function GearInput({
  label,
  value,
  placeholder,
  onChange,
  prefilled,
}: {
  label: string;
  value: string;
  placeholder: string;
  onChange: (v: string) => void;
  prefilled?: boolean;
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <label className="text-[10px] tracking-widest text-zinc-400 uppercase font-semibold" style={mono()}>
        {label}
      </label>
      <div className="relative">
        <input
          type="text"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          className="setup-input-field"
        />
        {prefilled && (
          <span
            className="absolute right-3 top-1/2 -translate-y-1/2 text-[9px] text-emerald-600 font-semibold"
            style={mono()}
          >
            AUTO
          </span>
        )}
      </div>
    </div>
  );
}

// ─── Step 1: Audio Engine & Hardware DAC ──────────────────────────────────────

function Step1({
  audioConfig,
  setAudioConfig,
}: {
  audioConfig: AudioEngineConfig;
  setAudioConfig: (c: AudioEngineConfig) => void;
}) {
  const [scanning, setScanning] = useState(false);
  const [scanKey, setScanKey] = useState(0);
  const [devices, setDevices] = useState<AudioDevice[]>([]);
  const [note, setNote] = useState<string | null>(null);

  // Core 의 엔드포인트 목록을 읽어 온다. 아직 Output 을 띄우지 않았다면 비어 있는 게 정상이다.
  const loadDevices = useCallback(async () => {
    try {
      const rows = (await api.endpoints()) as EndpointRecord[];
      setDevices(endpointsToDevices(rows));
    } catch {
      setNote("Core 에 연결하지 못했습니다. Mono 를 다시 시작해 보세요.");
    }
  }, []);

  useEffect(() => { void loadDevices() }, [loadDevices, scanKey]);

  const filteredDevices = devices.filter(
    (d) => d.driverType === audioConfig.driverType
  );
  const selectedDevice = filteredDevices.find((d) => d.id === audioConfig.deviceId) ?? null;

  function handleDriverChange(type: "ASIO" | "WASAPI_EXCLUSIVE") {
    setAudioConfig({
      ...audioConfig,
      driverType: type,
      deviceId: "",
      exclusiveMode: type === "WASAPI_EXCLUSIVE",
    });
  }

  async function handleRescan() {
    if (scanning) return;
    setScanning(true);
    setNote(null);
    // 장치를 찾으려면 이 PC 에 Output 워커가 떠 있어야 한다. 없으면 띄운다.
    const status = await outputStatus();
    if (status && !status.running) {
      const backend = audioConfig.driverType === "ASIO" ? "asio" : "exclusive";
      const res = await startOutput(null, backend);
      if (!res.ok) setNote(res.error ?? "출력을 시작하지 못했습니다.");
    } else if (!status) {
      setNote("데스크톱 앱에서 실행하면 이 PC 의 출력 장치를 자동으로 찾습니다.");
    }
    // Output 이 Core 에 자기소개(output_hello)를 넣을 틈을 준다.
    await new Promise((r) => setTimeout(r, 1200));
    setScanKey((k) => k + 1);
    setScanning(false);
  }

  const latencyMs = calcLatency(audioConfig.bufferSize);
  const stable = audioConfig.bufferSize >= 128;

  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-10">
      {/* Left column: driver + buffer */}
      <div className="flex flex-col gap-8">
        {/* Driver architecture */}
        <div>
          <SectionLabel>Driver Architecture</SectionLabel>
          <div className="flex flex-col gap-2">
            {(
              [
                {
                  type: "ASIO" as const,
                  title: "ASIO Direct",
                  badge: "Recommended",
                  desc: "Direct hardware buffer access. Zero OS mixer latency. Requires ASIO driver installed.",
                },
                {
                  type: "WASAPI_EXCLUSIVE" as const,
                  title: "WASAPI Exclusive",
                  badge: null,
                  desc: "Windows Audio Session API exclusive mode. Bypasses shared audio endpoint.",
                },
              ] as const
            ).map(({ type, title, badge, desc }) => {
              const active = audioConfig.driverType === type;
              return (
                <button
                  key={type}
                  onClick={() => handleDriverChange(type)}
                  className={[
                    "w-full text-left px-5 py-4 rounded-xl border transition-all",
                    active ? "setup-card-selected" : "ob-card",
                  ].join(" ")}
                >
                  <div className="flex items-center gap-2.5 mb-1">
                    <span
                      className={["w-1.5 h-1.5 rounded-full flex-shrink-0", !active ? "bg-zinc-300" : "setup-indicator-dot-active"].join(" ")}
                    />
                    <span className={["text-sm font-semibold ob-title", active ? "" : "opacity-80"].join(" ")}>{title}</span>
                    {badge && (
                      <span
                        className={[
                          "text-[9px] px-1.5 py-0.5 rounded-full font-medium border",
                          active ? "setup-badge-recommended" : "ob-chip border-transparent",
                        ].join(" ")}
                        style={mono()}
                      >
                        {badge}
                      </span>
                    )}
                  </div>
                  <p className="text-[11px] leading-relaxed ml-4 ob-body">
                    {desc}
                  </p>
                </button>
              );
            })}
          </div>
        </div>

        {/* Buffer size */}
        <div>
          <div className="flex items-baseline gap-2 mb-3">
            <SectionLabel>Buffer Size &amp; Latency</SectionLabel>
            <span className="text-[10px] latency-telemetry-text" style={mono()}>
              (Default: 256 samples)
            </span>
          </div>
          <div className="flex items-center gap-2 mb-4">
            {BUFFER_SIZES.map((size) => {
              const active = audioConfig.bufferSize === size;
              return (
                <button
                  key={size}
                  onClick={() => setAudioConfig({ ...audioConfig, bufferSize: size })}
                  className={[
                    "w-14 py-2 rounded-lg text-xs border text-center transition-all",
                    active ? "setup-pill-active" : "setup-pill-inactive",
                  ].join(" ")}
                  style={mono()}
                >
                  {size}
                </button>
              );
            })}
          </div>

          {/* Latency visual bar */}
          <div className="ob-infobox border rounded-xl p-4 flex flex-col gap-2">
            <div className="flex items-center justify-between">
              <span className="text-[10px] text-zinc-400" style={mono()}>
                CALCULATED LATENCY
              </span>
              <span
                className="text-[10px] font-semibold flex items-center gap-1"
                style={{ ...mono(), color: stable ? "var(--gauge-text-stable)" : "#f59e0b" }}
              >
                {stable ? "● Stable" : <><MonoIcon.AlertTriangle size={11} /><span>Aggressive</span></>}
              </span>
            </div>
            <div className="latency-gauge-track">
              <div
                className={stable ? "latency-gauge-fill" : "h-full rounded-full transition-all duration-300 bg-amber-400"}
                style={{
                  width: `${Math.min((audioConfig.bufferSize / 1024) * 100, 100)}%`,
                }}
              />
            </div>
            <p className="text-xs text-zinc-500" style={mono()}>
              {latencyMs} ms @ 44.1kHz · {audioConfig.bufferSize} samples
            </p>
          </div>
        </div>
      </div>

      {/* Right column: detected devices */}
      <div>
        <div className="flex items-center justify-between mb-3">
          <SectionLabel>Detected Hardware</SectionLabel>
          <button
            onClick={handleRescan}
            disabled={scanning}
            className="flex items-center gap-1.5 text-[10px] text-zinc-500 hover:text-zinc-900 transition-colors disabled:cursor-wait -mt-3"
            style={mono()}
          >
            {scanning ? (
              <>
                <svg className="w-3 h-3 animate-spin" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" d="M12 3a9 9 0 1 0 9 9" strokeDasharray="4 2" />
                </svg>
                Scanning…
              </>
            ) : (
              <>
                <svg className="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M4 4v5h5M20 20v-5h-5M4 9a8 8 0 0 1 15.46-2M20 15a8 8 0 0 1-15.46 2" />
                </svg>
                Rescan
              </>
            )}
          </button>
        </div>

        <div key={scanKey} className="flex flex-col gap-3">
          {filteredDevices.length === 0 && (
            <div className="py-8 text-center">
              <p className="text-xs text-zinc-400" style={mono()}>
                {audioConfig.driverType === "ASIO" ? "ASIO" : "WASAPI"} 장치를 찾지 못했습니다.
              </p>
              <p className="text-xs text-zinc-500 mt-2" style={mono()}>
                아래 &ldquo;Rescan&rdquo; 을 누르면 이 PC 의 출력 엔진을 띄우고 다시 찾습니다.
              </p>
            </div>
          )}
          {note && (
            <p className="text-xs py-3 text-center" style={{ ...mono(), color: "#DC2626" }}>{note}</p>
          )}
          {filteredDevices.map((device) => {
            const selected = audioConfig.deviceId === device.id;
            return (
              <button
                key={device.id}
                onClick={() => setAudioConfig({ ...audioConfig, deviceId: device.id })}
                className={[
                  "w-full text-left flex gap-4 px-5 py-4 rounded-xl border",
                  selected ? "setup-card-selected" : "ob-card",
                ].join(" ")}
              >
                {/* Radio */}
                <div
                  className={["mt-1 w-4 h-4 rounded-full border-2 flex items-center justify-center flex-shrink-0", !selected ? "border-zinc-300" : ""].join(" ")}
                  style={selected ? { borderColor: "var(--accent-solo)" } : {}}
                >
                  {selected && <div className="w-2 h-2 rounded-full setup-radio-dot-active" />}
                </div>

                <div className="flex flex-col gap-2 min-w-0 flex-1">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <p className="text-sm font-semibold ob-title leading-tight">
                        {device.name}
                      </p>
                      <p className="text-[10px] ob-body mt-0.5" style={mono()}>
                        {device.description}
                      </p>
                    </div>
                    <span
                      className="ob-chip text-[10px] font-semibold px-2 py-1 rounded-lg flex-shrink-0"
                      style={mono()}
                    >
                      {device.bitDepth}
                    </span>
                  </div>

                  <div className="flex flex-wrap gap-1.5">
                    {device.sampleRates.map((tag) => (
                      <span
                        key={tag}
                        className="ob-chip text-[9px] px-2 py-0.5 rounded"
                        style={mono()}
                      >
                        {tag}
                      </span>
                    ))}
                    {device.isBitPerfectVerified && (
                      <span className="badge-bitperfect-verified" style={mono()}>
                        <span className="badge-bitperfect-dot" />
                        Bit-Perfect Link Verified
                      </span>
                    )}
                  </div>
                </div>
              </button>
            );
          })}
        </div>

        {selectedDevice && (
          <div className="setup-confirmed-banner mt-4">
            <svg className="w-3.5 h-3.5 flex-shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.5} strokeLinecap="round" strokeLinejoin="round">
              <polyline points="20 6 9 17 4 12" />
            </svg>
            <p className="text-[11px] font-medium" style={mono()}>
              {selectedDevice.name} · {selectedDevice.bitDepth} · Direct Stream Confirmed
            </p>
          </div>
        )}
      </div>
    </div>
  );
}

// ─── Step 2: Streaming Services ───────────────────────────────────────────────

function Step2({
  connectedServices,
  onConnect,
  onDisconnect,
}: {
  connectedServices: { qobuz: boolean; tidal: boolean };
  onConnect: (service: "qobuz" | "tidal") => void;
  onDisconnect: (service: "qobuz" | "tidal") => void;
}) {
  return (
    <div className="flex flex-col gap-8">
      {/* Side-by-side service cards */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
        {/* Qobuz card */}
        <div className={[
          "flex flex-col rounded-2xl border overflow-hidden transition-all",
          connectedServices.qobuz ? "streaming-card-connected" : "ob-card border",
        ].join(" ")}>
          {/* Card header */}
          <div className="px-6 pt-6 pb-5 border-b border-inherit">
            <div className="flex items-start justify-between gap-3 mb-3">
              <div
                className="w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0"
                style={{
                  background: "linear-gradient(135deg, #C9A227 0%, #F5D06B 50%, #B8891A 100%)",
                  boxShadow: "0 2px 10px rgba(201,162,39,0.3)",
                  fontFamily: "Georgia, 'Times New Roman', serif",
                }}
              >
                <span style={{ fontSize: 16, fontWeight: 900, color: "#0B1120", letterSpacing: "-1px" }}>
                  Q
                </span>
              </div>
              {connectedServices.qobuz ? (
                <span className="badge-bitperfect-verified" style={mono()}>
                  <span className="badge-bitperfect-dot" />
                  Studio Active
                </span>
              ) : null}
            </div>
            <h3 className="text-sm font-bold ob-title mb-0.5">Qobuz Studio</h3>
            <p className="text-[10px] ob-body leading-relaxed" style={mono()}>
              Direct API · 24-Bit / 192kHz Studio Master
            </p>
          </div>

          {/* Card body */}
          <div className="px-6 py-5 flex flex-col gap-4 flex-1">
            <div className="flex flex-col gap-2">
              {[
                "Lossless 16-Bit / 44.1kHz CD Quality",
                "Hi-Res 24-Bit / 192kHz Studio Master",
                "Direct CDN · Zero Transcoding",
              ].map((feat) => (
                <div key={feat} className="flex items-center gap-2">
                  <span className="w-1 h-1 rounded-full bg-zinc-400/50 flex-shrink-0" />
                  <span className="text-[11px] ob-body">{feat}</span>
                </div>
              ))}
            </div>

            <div className="mt-auto pt-2">
              {connectedServices.qobuz ? (
                <div className="flex items-center justify-between">
                  <span className="streaming-telemetry" style={mono()}>
                    Connected (Studio 24-Bit / 192kHz)
                  </span>
                  <button
                    type="button"
                    className="streaming-btn-disconnect"
                    style={mono()}
                    onClick={() => onDisconnect("qobuz")}
                  >
                    Disconnect
                  </button>
                </div>
              ) : (
                <button
                  type="button"
                  onClick={() => onConnect("qobuz")}
                  className="setup-btn-connect w-full text-xs py-2.5 rounded-xl"
                >
                  Connect Account
                </button>
              )}
            </div>
          </div>
        </div>

        {/* TIDAL card */}
        <div className={[
          "flex flex-col rounded-2xl border overflow-hidden transition-all",
          connectedServices.tidal ? "streaming-card-connected" : "ob-card border",
        ].join(" ")}>
          {/* Card header */}
          <div className="px-6 pt-6 pb-5 border-b border-inherit">
            <div className="flex items-start justify-between gap-3 mb-3">
              <div
                className="w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0 bg-black"
                style={{ border: "1.5px solid rgba(0,200,220,0.5)", boxShadow: "0 0 14px rgba(0,200,220,0.2)" }}
              >
                <svg width="20" height="16" viewBox="0 0 18 14" fill="none">
                  <path d="M9 0 L12 4 L15 0 L18 4 L15 8 L12 4 L9 8 L6 4 L3 8 L0 4 L3 0 L6 4 Z" fill="#00C8DC" opacity="0.9" />
                </svg>
              </div>
              {connectedServices.tidal ? (
                <span className="badge-bitperfect-verified" style={mono()}>
                  <span className="badge-bitperfect-dot" />
                  Max Active
                </span>
              ) : null}
            </div>
            <h3 className="text-sm font-bold ob-title mb-0.5">TIDAL Max</h3>
            <p className="text-[10px] ob-body leading-relaxed" style={mono()}>
              OAuth 2.0 · HiRes FLAC &amp; Lossless Master
            </p>
          </div>

          {/* Card body */}
          <div className="px-6 py-5 flex flex-col gap-4 flex-1">
            <div className="flex flex-col gap-2">
              {[
                "Lossless FLAC · MQA / HiRes FLAC",
                "TIDAL Connect · Native Device Sync",
                "PKCE OAuth 2.0 · Secure Token Auth",
              ].map((feat) => (
                <div key={feat} className="flex items-center gap-2">
                  <span className="w-1 h-1 rounded-full bg-zinc-400/50 flex-shrink-0" />
                  <span className="text-[11px] ob-body">{feat}</span>
                </div>
              ))}
            </div>

            <div className="mt-auto pt-2">
              {connectedServices.tidal ? (
                <div className="flex items-center justify-between">
                  <span className="streaming-telemetry" style={mono()}>
                    Connected (Max HiRes FLAC)
                  </span>
                  <button
                    type="button"
                    className="streaming-btn-disconnect"
                    style={mono()}
                    onClick={() => onDisconnect("tidal")}
                  >
                    Disconnect
                  </button>
                </div>
              ) : (
                <button
                  type="button"
                  onClick={() => onConnect("tidal")}
                  className="setup-btn-connect w-full text-xs py-2.5 rounded-xl"
                >
                  Connect Account
                </button>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* Bottom callout */}
      <div className="ob-infobox border rounded-2xl p-6 grid grid-cols-1 md:grid-cols-3 gap-6">
        {[
          {
            icon: (
              <svg className="w-4 h-4 text-zinc-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round">
                <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
              </svg>
            ),
            title: "Zero Transcoding",
            desc: "Mono routes audio directly from provider CDNs. No intermediary re-encoding, ever.",
          },
          {
            icon: (
              <svg className="w-4 h-4 text-zinc-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round">
                <rect x="3" y="11" width="18" height="11" rx="2" /><path d="M7 11V7a5 5 0 0 1 10 0v4" />
              </svg>
            ),
            title: "Token Licensing",
            desc: "Your credentials are stored locally and encrypted. Mono never proxies authentication.",
          },
          {
            icon: (
              <svg className="w-4 h-4 text-zinc-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round">
                <circle cx="12" cy="12" r="10" /><polyline points="12 6 12 12 16 14" />
              </svg>
            ),
            title: "Real-Time Sync",
            desc: "Synchronized playback across Lounge sessions preserves bit-perfect integrity.",
          },
        ].map(({ icon, title, desc }) => (
          <div key={title} className="flex gap-3">
            <div className="flex-shrink-0 mt-0.5 ob-body">{icon}</div>
            <div>
              <p className="text-xs font-semibold ob-title mb-0.5">{title}</p>
              <p className="text-[11px] ob-body leading-relaxed">{desc}</p>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ─── Step 3: Listener Profile & Gear ─────────────────────────────────────────

function Step3({
  profile,
  setProfile,
  detectedDacName,
}: {
  profile: ListenerProfile;
  setProfile: (p: ListenerProfile) => void;
  detectedDacName: string;
}) {
  const colorDef = AVATAR_COLORS.find((c) => c.key === profile.avatarColor) ?? AVATAR_COLORS[0];
  const initStr = initials(profile.displayName) || "AL";
  const loungeDac = profile.gear.dac.trim() || "Unknown DAC";
  const loungeHp = profile.gear.headphonesOrSpeakers.trim();

  function patch(partial: Partial<ListenerProfile>) {
    setProfile({ ...profile, ...partial });
  }
  function patchGear(partial: Partial<ListenerProfile["gear"]>) {
    setProfile({ ...profile, gear: { ...profile.gear, ...partial } });
  }

  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-10">
      {/* Left: Public persona */}
      <div className="flex flex-col gap-7">
        <div>
          <SectionLabel>Public Persona</SectionLabel>

          {/* Avatar + name */}
          <div className="flex items-center gap-5 mb-5">
            <div
              className="w-16 h-16 rounded-full flex items-center justify-center text-lg font-bold flex-shrink-0 transition-colors duration-200 select-none"
              style={{ background: colorDef.bg, color: colorDef.text }}
            >
              {initStr}
            </div>
            <div className="flex-1 min-w-0">
              <label className="block text-[10px] tracking-widest text-zinc-400 uppercase font-semibold mb-1.5" style={mono()}>
                Display Name
              </label>
              <input
                type="text"
                value={profile.displayName}
                onChange={(e) => patch({ displayName: e.target.value })}
                placeholder="Audiophile Listener"
                className="setup-input-field text-sm"
              />
            </div>
          </div>

          {/* Avatar color swatches */}
          <div>
            <p className="text-[10px] tracking-widest text-zinc-400 uppercase font-semibold mb-2.5" style={mono()}>
              Avatar Color
            </p>
            <div className="flex items-center gap-2.5">
              {AVATAR_COLORS.map((c) => (
                <button
                  key={c.key}
                  onClick={() => patch({ avatarColor: c.key })}
                  title={c.key}
                  className="setup-swatch w-6 h-6 rounded-full transition-transform focus:outline-none flex-shrink-0"
                  style={{
                    background: c.bg,
                    outline: profile.avatarColor === c.key ? `2.5px solid ${c.bg}` : "2.5px solid transparent",
                    outlineOffset: "2px",
                    transform: profile.avatarColor === c.key ? "scale(1.2)" : "scale(1)",
                  }}
                />
              ))}
            </div>
          </div>
        </div>

        {/* Live lounge badge preview */}
        <div>
          <SectionLabel>Live Lounge Badge Preview</SectionLabel>
          <div className="ob-preview border border-dashed rounded-2xl p-5 flex flex-col gap-4">
            {/* Host card preview */}
            <div className="flex items-center gap-3">
              <div
                className="w-9 h-9 rounded-full flex items-center justify-center text-[11px] font-bold flex-shrink-0 select-none"
                style={{ background: colorDef.bg, color: colorDef.text }}
              >
                {initStr}
              </div>
              <div className="min-w-0">
                <p className="text-xs font-semibold ob-title truncate leading-tight">
                  {profile.displayName.trim() || "Audiophile Listener"}
                </p>
                <p className="text-[10px] ob-body truncate" style={mono()}>
                  {loungeDac}
                  {loungeHp ? ` · ${loungeHp}` : ""}
                  {" · "}
                  <span style={{ color: "var(--badge-verified-text)" }}>Bit-Perfect Stream</span>
                </p>
              </div>
            </div>
            {/* On-air host badge */}
            <div className="flex items-center gap-2">
              <span
                className="ob-chip inline-flex items-center gap-1.5 text-[9px] font-semibold border px-2.5 py-1 rounded-full"
                style={mono()}
              >
                <span className="w-1.5 h-1.5 rounded-full bg-zinc-400" />
                SESSION HOST
              </span>
              <span className="badge-bitperfect-verified" style={mono()}>
                <span className="badge-bitperfect-dot" />
                BIT-PERFECT
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* Right: Hardware rack */}
      <div className="flex flex-col gap-6">
        <div>
          <SectionLabel>Hardware Rack</SectionLabel>
          <p className="text-[10px] text-zinc-400 -mt-2 mb-4" style={mono()}>
            Displayed in session gear lists · optional
          </p>
          <div className="flex flex-col gap-4">
            <GearInput
              label="Headphones / Monitors"
              value={profile.gear.headphonesOrSpeakers}
              placeholder="e.g., Sennheiser HD800S / Genelec 8351B"
              onChange={(v) => patchGear({ headphonesOrSpeakers: v })}
            />
            <GearInput
              label="Amplifier / Preamp"
              value={profile.gear.amplifier}
              placeholder="e.g., Ferrum OOR / Benchmark HPA4"
              onChange={(v) => patchGear({ amplifier: v })}
            />
            <GearInput
              label="Primary DAC"
              value={profile.gear.dac}
              placeholder="e.g., Holo Audio Spring 3"
              onChange={(v) => patchGear({ dac: v })}
              prefilled={!!detectedDacName}
            />
          </div>
        </div>

        {/* Rack visual */}
        <div className="setup-signal-chain-panel">
          <p className="text-[9px] tracking-widest uppercase font-semibold chain-label" style={mono()}>
            Signal Chain
          </p>
          {[
            { label: "SOURCE", value: "Mono Audio Engine · Bit-Perfect ASIO" },
            { label: "DAC", value: profile.gear.dac.trim() || "—" },
            { label: "AMP", value: profile.gear.amplifier.trim() || "—" },
            { label: "OUTPUT", value: profile.gear.headphonesOrSpeakers.trim() || "—" },
          ].map(({ label, value }, i) => (
            <div key={label} className="flex items-center gap-3">
              <span className="chain-label">{label}</span>
              <div className="flex items-center gap-2 flex-1 min-w-0">
                {i > 0 && (
                  <svg className="w-3 h-3 chain-arrow flex-shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2}>
                    <polyline points="6 9 12 15 18 9" />
                  </svg>
                )}
                <span className="chain-value truncate">{value}</span>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

// ─── Wizard shell ─────────────────────────────────────────────────────────────

interface WizardProps {
  onComplete: (profile: ListenerProfile) => void;
  onExit: () => void;
  isDark?: boolean;
  onToggleTheme?: () => void;
  onServiceChange?: (service: "qobuz" | "tidal", connected: boolean) => void;
}

export default function OnboardingWizard({
  onComplete,
  onExit,
  isDark,
  onToggleTheme,
  onServiceChange,
}: WizardProps) {
  const [step, setStep] = useState<1 | 2 | 3>(1);
  // 연동 여부는 Core 가 안다. 마법사가 로컬 사본을 따로 들면 설정 화면과 어긋난다.
  const { streamingAccounts } = useMono();
  const services = {
    qobuz: streamingAccounts.some((a) => a.provider === StreamingProvider.Qobuz && a.connected),
    tidal: streamingAccounts.some((a) => a.provider === StreamingProvider.Tidal && a.connected),
  };
  const [authService, setAuthService] = useState<"qobuz" | "tidal" | null>(null);

  function persistServiceKey(service: "qobuz" | "tidal", connected: boolean) {
    // 실제 상태는 Core 의 link_streaming 푸시로 들어온다. 상위에는 알림만 올린다.
    onServiceChange?.(service, connected);
  }

  function handleConnect(service: "qobuz" | "tidal") {
    setAuthService(service);
  }

  function handleDisconnect(service: "qobuz" | "tidal") {
    persistServiceKey(service, false);
  }

  function handleAuthSuccess(service: "qobuz" | "tidal") {
    persistServiceKey(service, true);
    setAuthService(null);
  }

  const [audioConfig, setAudioConfig] = useState<AudioEngineConfig>({
    driverType: "ASIO",
    deviceId: "",
    bufferSize: 256,
    exclusiveMode: false,
  });

  const [profile, setProfile] = useState<ListenerProfile>({
    displayName: "Audiophile Listener",
    avatarColor: "zinc",
    gear: {
      headphonesOrSpeakers: "",
      amplifier: "",
      dac: "",
    },
  });

  // When step 1 is done and user advances, pre-fill DAC name in profile
  function advanceFromStep1() {
    if (!audioConfig.deviceId) return;
    const dacName = DAC_NAME_MAP[audioConfig.deviceId] ?? "Bit-Perfect ASIO DAC";
    if (!profile.gear.dac) {
      setProfile((p) => ({ ...p, gear: { ...p.gear, dac: dacName } }));
    }
    setStep(2);
  }

  function handleComplete() {
    localStorage.setItem("mono_onboarding_completed", "true");
    onComplete({
      ...profile,
      displayName: profile.displayName.trim() || "Audiophile Listener",
    });
  }

  const selectedDacId = audioConfig.deviceId;
  const statusReadout = selectedDacId
    ? `Output: ${DAC_NAME_MAP[selectedDacId] ?? "—"} · ${DAC_SPEC_MAP[selectedDacId] ?? "—"}`
    : "No output device selected";

  const canAdvanceStep1 = !!audioConfig.deviceId;

  const stepTitles = [
    "Audio Engine & Hardware Output",
    "Hi-Res Streaming Integration",
    "Listener Profile & Signature Rig",
  ];

  const stepSubtitles = [
    "Select your primary playback interface. Mono establishes a direct, bit-perfect hardware stream.",
    "Link your active subscriptions to enable 24-bit streaming and Live Lounge synchronization.",
    "Configure your public identity and hardware rack for community listening sessions.",
  ];

  return (
    <div className="ob-surface fixed inset-0 z-50 flex flex-col overflow-y-auto select-none">
      {/* ── Top Studio Header ───────────────────────────────────────────────── */}
      <header className="ob-header w-full border-b backdrop-blur-md sticky top-0 z-30 px-8 py-4 flex items-center justify-between gap-6">
        {/* Wordmark */}
        <div className="flex items-center gap-3 flex-shrink-0">
          <span className="text-base font-bold tracking-tight ob-title">Mono</span>
          <span
            className="text-[9px] font-semibold tracking-widest text-zinc-400 uppercase"
            style={mono()}
          >
            Studio Setup
          </span>
        </div>

        {/* Stepper */}
        <nav className="flex items-center gap-1 overflow-x-auto">
          {STEPS.map(({ num, label }, i) => {
            const stepNum = (i + 1) as 1 | 2 | 3;
            const isActive = step === stepNum;
            const isComplete = step > stepNum;
            return (
              <button
                key={num}
                onClick={() => {
                  if (stepNum < step) setStep(stepNum);
                }}
                disabled={stepNum >= step}
                className={[
                  "flex items-center gap-1.5 px-3 py-1.5 rounded-lg transition-colors whitespace-nowrap",
                  isActive ? "setup-accent-text" : isComplete ? "text-zinc-500 hover:text-zinc-700 cursor-pointer" : "text-zinc-400 cursor-default",
                ].join(" ")}
              >
                <span
                  className={[
                    "text-[10px] font-semibold",
                    isActive
                      ? "border-b-2 setup-accent-underline pb-0.5"
                      : "",
                  ].join(" ")}
                  style={mono()}
                >
                  {num} · {label}
                </span>
                {isComplete && (
                  <svg className="w-3 h-3 text-emerald-500 flex-shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.5} strokeLinecap="round" strokeLinejoin="round">
                    <polyline points="20 6 9 17 4 12" />
                  </svg>
                )}
              </button>
            );
          })}
        </nav>

        {/* Right controls */}
        <div className="flex items-center gap-3 flex-shrink-0">
          {onToggleTheme && (
            <button
              onClick={onToggleTheme}
              className="onboarding-theme-toggle"
              aria-label="Toggle theme"
            >
              {isDark ? <Sun size={15} /> : <Moon size={15} />}
            </button>
          )}
          <button
            onClick={onExit}
            className="text-[10px] text-zinc-400 hover:text-zinc-800 transition-colors"
            style={mono()}
          >
            Exit Setup
          </button>
        </div>
      </header>

      {/* ── Canvas ──────────────────────────────────────────────────────────── */}
      <main className="flex-1 max-w-5xl w-full mx-auto px-6 py-10">
        {/* Step header */}
        <div className="mb-10">
          <p
            className="text-[10px] tracking-widest uppercase mb-2"
            style={mono()}
          >
            <span className="setup-accent-text">Step {step === 1 ? "01" : step === 2 ? "02" : "03"} / 03</span>
          </p>
          <h1 className="text-2xl font-semibold ob-title tracking-tight leading-tight mb-2">
            {stepTitles[step - 1]}
          </h1>
          <p className="text-sm ob-body leading-relaxed max-w-xl">
            {stepSubtitles[step - 1]}
          </p>
        </div>

        {/* Step content */}
        {step === 1 && (
          <Step1 audioConfig={audioConfig} setAudioConfig={setAudioConfig} />
        )}
        {step === 2 && (
          <Step2
            connectedServices={services}
            onConnect={handleConnect}
            onDisconnect={handleDisconnect}
          />
        )}
        {step === 3 && (
          <Step3
            profile={profile}
            setProfile={setProfile}
            detectedDacName={DAC_NAME_MAP[audioConfig.deviceId] ?? ""}
          />
        )}
      </main>

      {/* ── Bottom Console Bar ───────────────────────────────────────────────── */}
      <footer className="ob-header ob-divider border-t backdrop-blur-md py-4 px-8 sticky bottom-0 z-30">
        <div className="max-w-5xl w-full mx-auto flex items-center justify-between gap-6">
          {/* Left */}
          <div className="flex items-center gap-4">
            {step > 1 ? (
              <button
                onClick={() => setStep((s) => (s - 1) as 1 | 2 | 3)}
                className="text-xs text-zinc-500 hover:text-zinc-900 font-medium transition-colors flex items-center gap-1.5"
              >
                <svg className="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
                  <polyline points="15 18 9 12 15 6" />
                </svg>
                Previous Step
              </button>
            ) : (
              <span className="text-xs text-zinc-300" style={mono()}>
                Configure advanced DSP later in Settings
              </span>
            )}
            {step === 2 && (
              <button
                onClick={() => setStep(3)}
                className="text-[10px] text-zinc-400 hover:text-zinc-600 underline underline-offset-2 transition-colors"
                style={mono()}
              >
                Skip (Local Only)
              </button>
            )}
          </div>

          {/* Center: hardware status */}
          <div className="hidden md:flex items-center gap-2 flex-1 justify-center">
            {audioConfig.deviceId ? (
              <>
                <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 flex-shrink-0" />
                <span className="text-[10px] text-zinc-500 truncate" style={mono()}>
                  {statusReadout}
                </span>
              </>
            ) : (
              <span className="text-[10px] text-zinc-300" style={mono()}>
                No output device configured
              </span>
            )}
          </div>

          {/* Right: primary action */}
          {step === 1 && (
            <button
              onClick={advanceFromStep1}
              disabled={!canAdvanceStep1}
              className="setup-btn-continue text-xs font-medium px-8 py-3 rounded-xl flex items-center gap-2 disabled:opacity-40 disabled:cursor-not-allowed flex-shrink-0"
            >
              Continue to Streaming
              <svg className="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
                <polyline points="9 18 15 12 9 6" />
              </svg>
            </button>
          )}
          {step === 2 && (
            <button
              onClick={() => setStep(3)}
              className="setup-btn-continue text-xs font-medium px-8 py-3 rounded-xl flex items-center gap-2 flex-shrink-0"
            >
              Continue to Profile
              <svg className="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
                <polyline points="9 18 15 12 9 6" />
              </svg>
            </button>
          )}
          {step === 3 && (
            <button
              onClick={handleComplete}
              className="setup-btn-continue text-xs font-medium px-8 py-3 rounded-xl flex items-center gap-2 flex-shrink-0"
            >
              Complete &amp; Launch Mono
              <svg className="w-3.5 h-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
                <polyline points="9 18 15 12 9 6" />
              </svg>
            </button>
          )}
        </div>
      </footer>

      <StreamingAuthModal
        isOpen={authService !== null}
        service={authService}
        onClose={() => setAuthService(null)}
        onSuccess={handleAuthSuccess}
      />
    </div>
  );
}
