import { useEffect, useState } from "react";
import { hasShell, listAudioDevices, type AudioDeviceListing } from "../../lib/shell";

export interface AudioEngineConfig {
  driverType: "ASIO" | "WASAPI_EXCLUSIVE";
  deviceId: string;
  /** Output 워커가 부분 일치로 장치를 찾을 때 쓰는 이름. id 는 재부팅 뒤에도 같지만 사람이 못 읽는다. */
  deviceName: string;
  bufferSize: number;
  exclusiveMode: boolean;
}

interface Props {
  onNext?: (config: AudioEngineConfig) => void;
  initialConfig?: Partial<AudioEngineConfig>;
  selectedDeviceId?: string | null;
  onSelectDevice?: (deviceId: string) => void;
}

// ─── Data ─────────────────────────────────────────────────────────────────────

const BUFFER_SIZES = [64, 128, 256, 512, 1024] as const;

type AudioDevice = AudioDeviceListing;

const DRIVER_OPTIONS = [
  {
    type: "ASIO" as const,
    label: "ASIO Direct",
    badge: "Recommended",
    desc: "Direct hardware buffer access. Zero OS mixer latency. Required for DSD.",
  },
  {
    type: "WASAPI_EXCLUSIVE" as const,
    label: "WASAPI Exclusive",
    badge: null,
    desc: "Windows Audio endpoint bypass. No third-party driver needed.",
  },
];

// ─── Helpers ──────────────────────────────────────────────────────────────────

function calcLatencyMs(bufferSize: number): string {
  return ((bufferSize / 44100) * 1000).toFixed(1);
}

function latencyGaugePct(bufferSize: number): number {
  const lo = Math.log2(64);
  const hi = Math.log2(1024);
  return Math.round(((Math.log2(bufferSize) - lo) / (hi - lo)) * 100);
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function Step1AudioEngine({
  onNext,
  initialConfig,
  selectedDeviceId: controlledDeviceId,
  onSelectDevice,
}: Props) {
  const [driverType, setDriverType] = useState<"ASIO" | "WASAPI_EXCLUSIVE">(
    initialConfig?.driverType ?? "ASIO"
  );
  const [internalDeviceId, setInternalDeviceId] = useState<string>(
    initialConfig?.deviceId ?? ""
  );
  const [bufferSize, setBufferSize] = useState<number>(
    initialConfig?.bufferSize ?? 256
  );
  const [scanning, setScanning] = useState(false);
  const [devices, setDevices] = useState<AudioDevice[]>([]);
  const [scanned, setScanned] = useState(false);

  // 장치 목록은 이 PC 에 실제로 달린 것만 보여 준다. 브라우저에서는 열거할 방법이 없다.
  async function rescan() {
    setScanning(true);
    try {
      setDevices(await listAudioDevices());
    } finally {
      setScanning(false);
      setScanned(true);
    }
  }

  useEffect(() => { void rescan() }, []);

  // Parent-controlled selection wins; fall back to internal
  const selectedDeviceId: string = controlledDeviceId ?? internalDeviceId;

  function selectDevice(id: string) {
    const next = selectedDeviceId === id ? "" : id;
    setInternalDeviceId(next);
    onSelectDevice?.(next);
  }

  // 고른 드라이버에 해당하는 장치만. ASIO 드라이버가 없는 PC 에서는 이 목록이 비는 게 맞다.
  const visibleDevices = devices.filter((d) => d.driverType === driverType);
  const selectedDevice = devices.find((d) => d.id === selectedDeviceId) ?? null;

  function handleDriverSwitch(type: "ASIO" | "WASAPI_EXCLUSIVE") {
    setDriverType(type);
  }

  function handleRescan() {
    if (scanning) return;
    void rescan();
  }

  // Called by the wizard footer's "Next: Streaming Services" button via onNext
  // (wizard passes onNext={(cfg) => { setAudioConfig(cfg); advance(); }})
  function emitConfig() {
    if (!selectedDevice) return;
    onNext?.({
      driverType,
      deviceId: selectedDevice.id,
      deviceName: selectedDevice.name,
      bufferSize,
      exclusiveMode: driverType === "WASAPI_EXCLUSIVE",
    });
  }

  // Expose emitConfig on the DOM so the wizard footer can trigger it
  // without coupling parent ↔ child state unnecessarily
  const latencyMs = calcLatencyMs(bufferSize);
  const gaugePct = latencyGaugePct(bufferSize);
  const isStable = bufferSize >= 128;

  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 w-full">

      {/* ── LEFT COLUMN ───────────────────────────────────────────────────── */}
      <div className="flex flex-col gap-5">

        {/* Driver Architecture */}
        <div className="ae-panel">
          <p className="ae-section-label">Driver Architecture</p>
          <div className="flex flex-col gap-2.5">
            {DRIVER_OPTIONS.map(({ type, label, badge, desc }) => {
              const active = driverType === type;
              return (
                <button
                  key={type}
                  onClick={() => handleDriverSwitch(type)}
                  className={["ae-selcard", active ? "ae-active" : ""].join(" ")}
                >
                  {/* Radio dot */}
                  <div className="ae-radio">
                    {active && (
                      <div
                        className="w-1.5 h-1.5 rounded-full"
                        style={{ background: "#a78bfa" }}
                      />
                    )}
                  </div>

                  {/* Text */}
                  <div className="flex flex-col gap-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="ae-card-name">{label}</span>
                      {badge && (
                        <span className={active ? "ae-rec-badge" : "ae-rec-badge-dim"}>
                          {badge}
                        </span>
                      )}
                    </div>
                    <span className="ae-card-desc">{desc}</span>
                  </div>
                </button>
              );
            })}
          </div>
        </div>

        {/* Buffer Size */}
        <div className="ae-panel">
          <p className="ae-section-label">Hardware Buffer Size</p>

          {/* Pills */}
          <div className="flex items-center gap-1.5 mb-4">
            {BUFFER_SIZES.map((size) => {
              const active = bufferSize === size;
              return (
                <button
                  key={size}
                  onClick={() => setBufferSize(size)}
                  className={["ae-buffer-pill", active ? "ae-active" : ""].join(" ")}
                >
                  {size}
                </button>
              );
            })}
          </div>

          {/* Latency card */}
          <div className="ae-latency-card">
            {/* Gauge bar */}
            <div className="ae-latency-track">
              <div
                className="h-full rounded-full transition-all duration-300"
                style={{
                  width: `${gaugePct}%`,
                  background: isStable
                    ? "linear-gradient(90deg, #059669, #10b981)"
                    : "linear-gradient(90deg, #d97706, #f59e0b)",
                  boxShadow: isStable
                    ? "0 0 6px rgba(16,185,129,0.5)"
                    : "0 0 6px rgba(245,158,11,0.5)",
                }}
              />
            </div>

            {/* Readout row */}
            <div className="flex items-center justify-between gap-2">
              <span className="ae-latency-readout">
                {latencyMs} ms @ 44.1kHz &bull; {bufferSize} samples
              </span>
              <span
                className="inline-flex items-center gap-1.5 text-[10px] font-semibold"
                style={{
                  fontFamily: "'DM Mono', monospace",
                  color: isStable ? "#059669" : "#d97706",
                }}
              >
                <span
                  className="w-1.5 h-1.5 rounded-full flex-shrink-0"
                  style={{
                    background: isStable ? "#10b981" : "#f59e0b",
                    boxShadow: isStable
                      ? "0 0 5px rgba(16,185,129,0.7)"
                      : "0 0 5px rgba(245,158,11,0.7)",
                  }}
                />
                {isStable ? "Stable" : "Aggressive"}
              </span>
            </div>

            {/* Hint */}
            <p className="ae-latency-hint">
              {isStable
                ? "Stream-stable. Recommended for all DACs and long listening sessions."
                : "Low latency mode — may cause glitches on slower USB controllers."}
            </p>
          </div>
        </div>
      </div>

      {/* ── RIGHT COLUMN ──────────────────────────────────────────────────── */}
      <div className="ae-panel flex flex-col" style={{ minHeight: 0 }}>
        {/* Header */}
        <div className="flex items-center justify-between mb-4">
          <p className="ae-section-label" style={{ marginBottom: 0 }}>Detected Hardware</p>
          <button
            onClick={handleRescan}
            disabled={scanning}
            className="ae-rescan-btn"
          >
            <svg
              className={["w-3 h-3", scanning ? "animate-spin" : ""].join(" ")}
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth={2}
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <path d="M4 4v5h5M20 20v-5h-5M4 9a8 8 0 0 1 15.46-2M20 15a8 8 0 0 1-15.46 2" />
            </svg>
            {scanning ? "Scanning…" : "↻ Rescan"}
          </button>
        </div>

        {/* Scrollable device list */}
        <div
          className="ae-device-scroll flex flex-col gap-2.5 overflow-y-auto pr-1"
          style={{ maxHeight: 460 }}
        >
          {visibleDevices.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-10 gap-3">
              <svg
                className="w-8 h-8 opacity-20"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth={1.5}
              >
                <circle cx="12" cy="12" r="10" />
                <line x1="8" y1="12" x2="16" y2="12" />
              </svg>
              <p
                className="text-xs text-center ae-latency-hint"
                style={{ opacity: 1 }}
              >
                {!hasShell() ? (
                  <>
                    출력 장치는 Mono 데스크톱 앱에서만 찾을 수 있습니다.
                    <br />
                    브라우저에서는 목록이 비어 있습니다.
                  </>
                ) : !scanned ? (
                  <>장치를 찾는 중…</>
                ) : (
                  <>
                    {driverType === "ASIO" ? "ASIO" : "WASAPI"} 장치를 찾지 못했습니다.
                    <br />
                    다른 아키텍처를 고르거나 Rescan 을 눌러 보세요.
                  </>
                )}
              </p>
            </div>
          ) : (
            visibleDevices.map((device) => {
              const selected = selectedDeviceId === device.id;
              return (
                <button
                  key={device.id}
                  onClick={() => selectDevice(device.id)}
                  className={["ae-selcard", selected ? "ae-active" : ""].join(" ")}
                  style={{ padding: "1rem" }}
                >
                  {/* Radio dot */}
                  <div className="ae-radio" style={{ marginTop: 3 }}>
                    {selected && (
                      <div
                        className="w-1.5 h-1.5 rounded-full"
                        style={{ background: "#a78bfa" }}
                      />
                    )}
                  </div>

                  {/* Content */}
                  <div className="flex flex-col gap-2 min-w-0 flex-1">
                    {/* Name + arch chip */}
                    <div className="flex items-start justify-between gap-2 flex-wrap">
                      <span className="ae-card-name leading-tight">{device.name}</span>
                      <span className="ae-arch-chip">{device.architecture}</span>
                    </div>

                    {/* Subtitle */}
                    <p className="ae-card-desc">{device.subtitle}</p>

                    {/* Spec + DSD badges */}
                    <div className="flex flex-wrap items-center gap-1.5">
                      {device.specs.map((s) => (
                        <span key={s} className="ae-spec-badge">{s}</span>
                      ))}
                      {device.dsdSupport && (
                        <span className="ae-dsd-badge">{device.dsdSupport}</span>
                      )}
                    </div>

                    {/* Bit-perfect / shared mode status */}
                    {device.bitPerfectVerified ? (
                      <div className="flex items-center gap-1.5 mt-0.5">
                        <span
                          className="w-1.5 h-1.5 rounded-full flex-shrink-0"
                          style={{
                            background: "#10b981",
                            boxShadow: "0 0 5px rgba(16,185,129,0.6)",
                          }}
                        />
                        <span className="ae-bitperfect">● Bit-Perfect Link Verified</span>
                      </div>
                    ) : (
                      <div className="flex items-center gap-1.5 mt-0.5">
                        <span
                          className="w-1.5 h-1.5 rounded-full flex-shrink-0"
                          style={{ background: "#a1a1aa" }}
                        />
                        <span className="ae-shared-mode">Shared Mode — not bit-perfect</span>
                      </div>
                    )}
                  </div>
                </button>
              );
            })
          )}
        </div>
      </div>

      {/*
        The parent OnboardingWizard renders its own Back / Next footer.
        onNext is called by the wizard's "Next: Streaming Services" button
        via the onNext prop passed in from OnboardingWizard.tsx.
        We expose a hidden trigger so the wizard can call emitConfig()
        if it needs the config before advancing.
      */}
      <button
        id="ae-emit-config"
        onClick={emitConfig}
        style={{ display: "none" }}
        aria-hidden="true"
      />
    </div>
  );
}
