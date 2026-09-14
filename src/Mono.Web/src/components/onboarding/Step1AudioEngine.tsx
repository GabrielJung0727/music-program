import { useState } from "react";

export interface AudioDevice {
  id: string;
  name: string;
  driverType: "ASIO" | "WASAPI_EXCLUSIVE";
  sampleRates: string[];
  isBitPerfectVerified: boolean;
  capabilityTag: string;
}

export interface AudioEngineConfig {
  driverType: "ASIO" | "WASAPI_EXCLUSIVE";
  deviceId: string;
  bufferSize: number;
  exclusiveMode: boolean;
}

interface Props {
  onNext: (config: AudioEngineConfig) => void;
  initialConfig?: AudioEngineConfig;
}

const MOCK_DEVICES: AudioDevice[] = [
  {
    id: "holo-spring-3",
    name: "Holo Audio Spring 3 (ASIO Direct)",
    driverType: "ASIO",
    sampleRates: ["DSD512", "PCM 32/768k"],
    isBitPerfectVerified: true,
    capabilityTag: "DSD512",
  },
  {
    id: "chord-hugo-tt2",
    name: "Chord Hugo TT 2 (Kernel Streaming / ASIO)",
    driverType: "ASIO",
    sampleRates: ["PCM 32/768k"],
    isBitPerfectVerified: true,
    capabilityTag: "PCM 32/768k",
  },
  {
    id: "system-wasapi",
    name: "Default System Output (WASAPI Exclusive)",
    driverType: "WASAPI_EXCLUSIVE",
    sampleRates: ["PCM 24/192k"],
    isBitPerfectVerified: true,
    capabilityTag: "PCM 24/192k",
  },
];

const BUFFER_SIZES = [64, 128, 256, 512, 1024] as const;

function calcLatencyMs(bufferSize: number, sampleRate = 44100): string {
  return ((bufferSize / sampleRate) * 1000).toFixed(1);
}

function latencyLabel(bufferSize: number): string {
  const ms = calcLatencyMs(bufferSize);
  const stable = bufferSize >= 128;
  return `Calculated Latency: ${ms} ms @ 44.1kHz (${stable ? "Stable Stream" : "Aggressive — may glitch"})`;
}

function latencyGaugePercent(bufferSize: number): number {
  const MAX_BUFFER = 1024;
  return Math.round((bufferSize / MAX_BUFFER) * 100);
}

export default function Step1AudioEngine({ onNext, initialConfig }: Props) {
  const [driverType, setDriverType] = useState<"ASIO" | "WASAPI_EXCLUSIVE">(
    initialConfig?.driverType ?? "ASIO"
  );
  const [selectedDeviceId, setSelectedDeviceId] = useState<string>(
    initialConfig?.deviceId ?? ""
  );
  const [bufferSize, setBufferSize] = useState<number>(
    initialConfig?.bufferSize ?? 256
  );
  const [scanning, setScanning] = useState(false);
  const [scanKey, setScanKey] = useState(0);

  const filteredDevices = MOCK_DEVICES.filter((d) => d.driverType === driverType);
  const selectedDevice = filteredDevices.find((d) => d.id === selectedDeviceId) ?? null;

  // If current selection is not valid for driver switch, clear it
  const resolvedDeviceId =
    filteredDevices.some((d) => d.id === selectedDeviceId) ? selectedDeviceId : "";

  function handleDriverChange(type: "ASIO" | "WASAPI_EXCLUSIVE") {
    setDriverType(type);
    const stillValid = MOCK_DEVICES.find(
      (d) => d.id === selectedDeviceId && d.driverType === type
    );
    if (!stillValid) setSelectedDeviceId("");
  }

  function handleRescan() {
    if (scanning) return;
    setScanning(true);
    setScanKey((k) => k + 1);
    setTimeout(() => setScanning(false), 1000);
  }

  function handleContinue() {
    if (!resolvedDeviceId) return;
    onNext({
      driverType,
      deviceId: resolvedDeviceId,
      bufferSize,
      exclusiveMode: driverType === "WASAPI_EXCLUSIVE",
    });
  }

  const canContinue = resolvedDeviceId !== "";

  return (
    <div className="flex flex-col gap-8 w-full max-w-xl mx-auto px-1">
      {/* Header */}
      <div className="flex flex-col gap-1.5">
        <span
          style={{ fontFamily: "'DM Mono', monospace" }}
          className="text-[10px] tracking-widest text-zinc-400 uppercase"
        >
          Step 01 / 04 · Engine Setup
        </span>
        <h2 className="text-xl font-semibold ob-title tracking-tight leading-snug">
          Audio Output &amp; Hardware DAC
        </h2>
        <p className="text-sm ob-body leading-relaxed max-w-md">
          Select your primary playback interface. Mono bypasses the OS mixer to
          establish a direct, bit-perfect stream.
        </p>
      </div>

      {/* Section 1: Driver Architecture */}
      <div className="flex flex-col gap-3">
        <span className="text-[10px] font-semibold tracking-widest text-zinc-400 uppercase"
          style={{ fontFamily: "'DM Mono', monospace" }}>
          Driver Architecture
        </span>
        <div className="inline-flex ob-driver-pool rounded-xl p-1 gap-1 self-start">
          {(
            [
              {
                type: "ASIO" as const,
                label: "ASIO",
                badge: "Recommended",
                desc: "Direct hardware buffer access, zero OS latency.",
              },
              {
                type: "WASAPI_EXCLUSIVE" as const,
                label: "WASAPI Exclusive",
                badge: null,
                desc: "Direct Windows Audio Endpoint bypass.",
              },
            ] as const
          ).map(({ type, label, badge, desc }) => {
            const active = driverType === type;
            return (
              <button
                key={type}
                onClick={() => handleDriverChange(type)}
                className={[
                  "flex flex-col items-start px-4 py-2.5 rounded-lg transition-all text-left",
                  active ? "ob-driver-tab-active shadow-sm" : "ob-driver-tab",
                ].join(" ")}
              >
                <span className="flex items-center gap-2 text-xs font-semibold leading-none mb-1">
                  <span
                    className={["inline-block w-1.5 h-1.5 rounded-full", !active ? "bg-zinc-400" : ""].join(" ")}
                    style={active ? { background: "var(--ob-driver-tab-active-text)" } : {}}
                  />
                  {label}
                  {badge && (
                    <span
                      className={[
                        "text-[9px] tracking-wider px-1.5 py-0.5 rounded-full font-mono font-medium",
                        active ? "" : "ob-chip",
                      ].join(" ")}
                      style={active ? { background: "rgba(128,128,128,0.2)", color: "currentColor" } : { fontFamily: "'DM Mono', monospace" }}
                    >
                      {badge}
                    </span>
                  )}
                </span>
                <span className="text-[10px] leading-snug opacity-60">
                  {desc}
                </span>
              </button>
            );
          })}
        </div>
      </div>

      {/* Section 2: Detected Devices */}
      <div className="flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <span
            className="text-[10px] font-semibold tracking-widest text-zinc-400 uppercase"
            style={{ fontFamily: "'DM Mono', monospace" }}
          >
            Detected Devices
          </span>
          <button
            onClick={handleRescan}
            disabled={scanning}
            className="flex items-center gap-1.5 text-xs text-zinc-500 hover:text-zinc-900 transition-colors font-mono disabled:cursor-wait"
            style={{ fontFamily: "'DM Mono', monospace" }}
          >
            {scanning ? (
              <>
                <svg
                  className="w-3 h-3 animate-spin"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth={2}
                >
                  <path
                    strokeLinecap="round"
                    d="M12 3a9 9 0 1 0 9 9"
                    strokeDasharray="4 2"
                  />
                </svg>
                Scanning…
              </>
            ) : (
              <>
                <svg
                  className="w-3 h-3"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth={2}
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    d="M4 4v5h5M20 20v-5h-5M4 9a8 8 0 0 1 15.46-2M20 15a8 8 0 0 1-15.46 2"
                  />
                </svg>
                Rescan Hardware
              </>
            )}
          </button>
        </div>

        <div key={scanKey} className="flex flex-col gap-2">
          {filteredDevices.map((device) => {
            const selected = resolvedDeviceId === device.id;
            return (
              <button
                key={device.id}
                onClick={() => setSelectedDeviceId(device.id)}
                className={[
                  "w-full text-left flex items-start gap-3 px-4 py-3.5 rounded-xl border",
                  selected ? "ob-card-active" : "ob-card",
                ].join(" ")}
              >
                {/* Radio indicator */}
                <div
                  className={["mt-0.5 flex-shrink-0 w-4 h-4 rounded-full border-2 flex items-center justify-center", !selected ? "border-zinc-300" : ""].join(" ")}
                  style={selected ? { borderColor: "var(--ob-radio-active)" } : {}}
                >
                  {selected && (
                    <div className="w-2 h-2 rounded-full" style={{ background: "var(--ob-radio-active)" }} />
                  )}
                </div>

                {/* Content */}
                <div className="flex flex-col gap-2 min-w-0">
                  <span className="text-sm font-semibold ob-title leading-tight">
                    {device.name}
                  </span>
                  <div className="flex flex-wrap items-center gap-1.5">
                    {device.sampleRates.map((tag) => (
                      <span
                        key={tag}
                        className="ob-chip text-[10px] px-2 py-0.5 rounded"
                        style={{ fontFamily: "'DM Mono', monospace" }}
                      >
                        {tag}
                      </span>
                    ))}
                    {device.isBitPerfectVerified && (
                      <span
                        className="badge-bitperfect-verified"
                        style={{ fontFamily: "'DM Mono', monospace" }}
                      >
                        <span className="badge-bitperfect-dot" />
                        Bit-Perfect Verified
                      </span>
                    )}
                  </div>
                </div>
              </button>
            );
          })}

          {filteredDevices.length === 0 && (
            <p
              className="text-xs text-zinc-400 py-4 text-center"
              style={{ fontFamily: "'DM Mono', monospace" }}
            >
              No {driverType === "ASIO" ? "ASIO" : "WASAPI"} devices detected.
              Try Rescan Hardware.
            </p>
          )}
        </div>
      </div>

      {/* Section 3: Buffer Size */}
      <div className="flex flex-col gap-3">
        <div className="flex items-baseline gap-2">
          <span
            className="text-[10px] font-semibold tracking-widest text-zinc-400 uppercase"
            style={{ fontFamily: "'DM Mono', monospace" }}
          >
            Buffer Size &amp; Latency
          </span>
          <span
            className="text-[10px] latency-telemetry-text"
            style={{ fontFamily: "'DM Mono', monospace" }}
          >
            (Default: 256 samples)
          </span>
        </div>
        <div className="flex items-center gap-1.5">
          {BUFFER_SIZES.map((size) => {
            const active = bufferSize === size;
            return (
              <button
                key={size}
                onClick={() => setBufferSize(size)}
                className={[
                  "w-14 py-1.5 rounded-lg text-xs border text-center transition-all",
                  active ? "setup-pill-active" : "setup-pill-inactive",
                ].join(" ")}
                style={{ fontFamily: "'DM Mono', monospace" }}
              >
                {size}
              </button>
            );
          })}
        </div>
        {/* Latency Gauge */}
        <div className="flex flex-col gap-2 pt-0.5">
          <div className="latency-gauge-track">
            <div
              className="latency-gauge-fill"
              style={{ width: `${latencyGaugePercent(bufferSize)}%` }}
            />
          </div>
          <div className="flex items-center justify-between gap-2">
            <span className="latency-telemetry-text">
              {calcLatencyMs(bufferSize)} ms @ 44.1kHz • {bufferSize} samples
            </span>
            <span
              className="inline-flex items-center gap-1.5 latency-telemetry-text"
              style={{ color: bufferSize >= 128 ? "var(--gauge-text-stable)" : "#f59e0b" }}
            >
              <span
                className="inline-block w-1.5 h-1.5 rounded-full flex-shrink-0"
                style={{
                  background: bufferSize >= 128 ? "var(--gauge-dot-stable)" : "#f59e0b",
                  boxShadow: bufferSize >= 128 ? "0 0 4px var(--gauge-dot-stable)" : "0 0 4px #f59e0b",
                }}
              />
              {bufferSize >= 128 ? "Stable" : "Aggressive"}
            </span>
          </div>
        </div>
      </div>

      {/* Bottom Action Bar */}
      <div className="ob-divider flex items-center justify-between pt-2 border-t">
        <span className="text-xs ob-body">
          Configure advanced DSP later in Settings
        </span>
        <button
          onClick={handleContinue}
          disabled={!canContinue}
          className="ob-action-btn text-xs font-semibold px-6 py-2.5 rounded-xl shadow-sm"
        >
          Continue to Services →
        </button>
      </div>
    </div>
  );
}
