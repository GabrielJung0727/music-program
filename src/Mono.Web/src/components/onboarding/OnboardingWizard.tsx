import { useState, useEffect } from "react";
import { Sun, Moon, FolderOpen, Plus, X, Check } from "lucide-react";
import StreamingAuthModal from "./StreamingAuthModal";
import { type ListenerProfile } from "./Step3AudiophileRig";
import Step1AudioEngine, { type AudioEngineConfig } from "./Step1AudioEngine";
import { useMono, useMonoCommands } from "../../state/MonoProvider";
import { StreamingProvider } from "../../lib/protocol";
import { hasShell, pickFolder } from "../../lib/shell";
import { saveSetup, startConfiguredOutput, type AudioSetup } from "../../lib/setup";

// ─── Types ────────────────────────────────────────────────────────────────────

interface StorageConfig {
  folders: string[];
  autoWatch: boolean;
}

// ─── Constants ────────────────────────────────────────────────────────────────

const DEFAULT_MUSIC_FOLDER = "C:\\Users\\AudioUser\\Music";

const AVATAR_COLORS = [
  { key: "zinc",    bg: "#18181b", text: "#ffffff" },
  { key: "slate",   bg: "#1e293b", text: "#ffffff" },
  { key: "amber",   bg: "#92400e", text: "#ffffff" },
  { key: "emerald", bg: "#064e3b", text: "#ffffff" },
  { key: "rose",    bg: "#881337", text: "#ffffff" },
  { key: "indigo",  bg: "#312e81", text: "#ffffff" },
];

const STEPS = [
  { num: "01", label: "Welcome" },
  { num: "02", label: "Storage" },
  { num: "03", label: "Audio Output" },
  { num: "04", label: "Streaming" },
  { num: "05", label: "Profile" },
  { num: "06", label: "Ready" },
];

// ─── Helpers ──────────────────────────────────────────────────────────────────

function initials(name: string): string {
  return name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? "")
    .join("");
}

function mono() {
  return { fontFamily: "'DM Mono', monospace" } as React.CSSProperties;
}

// ─── Shared ───────────────────────────────────────────────────────────────────

function SectionLabel({ children }: { children: React.ReactNode }) {
  return (
    <p className="text-[10px] font-semibold tracking-widest text-zinc-400 uppercase mb-3" style={mono()}>
      {children}
    </p>
  );
}

function GearInput({ label, value, placeholder, onChange }: {
  label: string; value: string; placeholder: string; onChange: (v: string) => void;
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <label className="text-[10px] tracking-widest text-zinc-400 uppercase font-semibold" style={mono()}>{label}</label>
      <input
        type="text"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        className="setup-input-field"
      />
    </div>
  );
}

// ─── Step 1: Welcome & Philosophy ─────────────────────────────────────────────

function StepWelcome({ onStart }: { onStart: () => void }) {
  return (
    <div className="max-w-2xl w-full text-center flex flex-col items-center">
      {/* Illuminated wordmark */}
      <div className="relative select-none mb-4">
        <div
          className="absolute inset-0 blur-3xl opacity-30 rounded-full"
          style={{ background: "radial-gradient(ellipse, #7c3aed 0%, transparent 70%)", transform: "scale(1.6)" }}
        />
        <h1
          className="relative text-6xl font-bold tracking-tight ob-title leading-none"
          style={{ WebkitTextStroke: "1px rgba(167,139,250,0.2)" }}
        >
          Mono
        </h1>
      </div>

      {/* Brand motto */}
      <blockquote
        className="text-xl mb-6"
        style={{
          fontFamily: "Georgia, 'Times New Roman', serif",
          fontStyle: "italic",
          color: "var(--ob-body)",
          letterSpacing: "0.01em",
          lineHeight: 1.5,
        }}
      >
        "Alone or together, the ultimate experience through one sound."
      </blockquote>

      {/* Subtext */}
      <p className="text-sm ob-body max-w-lg mb-8 leading-relaxed" style={mono()}>
        Bit-perfect playback to your hardware DAC. Synchronized collective listening
        across Live Lounges. Zero transcoding. One signal path.
      </p>

      {/* Feature pills */}
      <div className="flex flex-wrap items-center justify-center gap-2 mb-10">
        {[
          "ASIO / WASAPI Exclusive",
          "32-Bit / DSD512",
          "Live Lounge Sync",
          "Qobuz · TIDAL Max",
          "Zero Re-encoding",
        ].map((tag) => (
          <span key={tag} className="ob-chip text-[10px] px-3 py-1 rounded-full font-medium" style={mono()}>
            {tag}
          </span>
        ))}
      </div>

      {/* CTA */}
      <button
        onClick={onStart}
        className="setup-btn-continue text-sm font-semibold px-8 py-3.5 rounded-full flex items-center gap-3 shadow-lg"
        style={{ boxShadow: "0 8px 32px rgba(124,58,237,0.3)" }}
      >
        Start Setup
        <svg className="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
          <polyline points="9 18 15 12 9 6" />
        </svg>
      </button>
    </div>
  );
}

// ─── Step 2: Storage & Source Devices ─────────────────────────────────────────

function StepStorage({
  storage, setStorage, onSkip,
}: {
  storage: StorageConfig; setStorage: (s: StorageConfig) => void; onSkip: () => void;
}) {
  const [newFolderInput, setNewFolderInput] = useState("");
  const [addingFolder, setAddingFolder] = useState(false);

  /**
   * 데스크톱 앱에서는 경로를 손으로 칠 이유가 없다. 네이티브 폴더 선택창을 띄우고,
   * 브라우저로 열었을 때만 직접 입력 칸으로 물러난다.
   */
  async function browseForFolder() {
    if (!hasShell()) { setAddingFolder(true); return; }
    const picked = await pickFolder("음악 폴더 선택");
    if (!picked || storage.folders.includes(picked)) return;
    setStorage({ ...storage, folders: [...storage.folders, picked] });
  }

  function addFolder() {
    const trimmed = newFolderInput.trim();
    if (!trimmed || storage.folders.includes(trimmed)) return;
    setStorage({ ...storage, folders: [...storage.folders, trimmed] });
    setNewFolderInput("");
    setAddingFolder(false);
  }

  function removeFolder(path: string) {
    setStorage({ ...storage, folders: storage.folders.filter((f) => f !== path) });
  }

  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-10">
      {/* Left: folder list */}
      <div className="flex flex-col gap-6">
        <div>
          <SectionLabel>Local Music Folders</SectionLabel>
          <div className="flex flex-col gap-2">
            {storage.folders.map((folder) => (
              <div key={folder} className="onboarding-storage-card flex items-center gap-3 px-4 py-3 rounded-xl border">
                <FolderOpen size={15} className="flex-shrink-0 text-violet-500" />
                <span className="flex-1 min-w-0 text-xs ob-title truncate" style={mono()}>{folder}</span>
                {folder === DEFAULT_MUSIC_FOLDER && (
                  <span className="ob-chip text-[9px] px-2 py-0.5 rounded font-medium flex-shrink-0" style={mono()}>OS Default</span>
                )}
                <button
                  onClick={() => removeFolder(folder)}
                  className="flex-shrink-0 w-5 h-5 rounded-md flex items-center justify-center text-zinc-400 hover:text-rose-500 transition-colors"
                  aria-label="Remove folder"
                >
                  <X size={12} />
                </button>
              </div>
            ))}
            {storage.folders.length === 0 && (
              <p className="text-xs ob-body py-6 text-center" style={mono()}>
                No folders selected — Mono will scan only streamed content.
              </p>
            )}
          </div>

          {/* Add folder row */}
          {addingFolder ? (
            <div className="mt-3 flex gap-2">
              <input
                autoFocus
                type="text"
                value={newFolderInput}
                onChange={(e) => setNewFolderInput(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") addFolder();
                  if (e.key === "Escape") { setAddingFolder(false); setNewFolderInput(""); }
                }}
                placeholder="D:\NAS\FLAC  or  /mnt/music"
                className="setup-input-field flex-1 text-xs"
                style={mono()}
              />
              <button onClick={addFolder} className="btn-add-folder-confirm px-3 py-2 rounded-lg text-xs font-medium">Add</button>
              <button onClick={() => { setAddingFolder(false); setNewFolderInput(""); }} className="px-3 py-2 rounded-lg text-xs ob-body hover:ob-title transition-colors">Cancel</button>
            </div>
          ) : (
            <button
              onClick={() => { void browseForFolder() }}
              className="btn-add-folder mt-3 w-full flex items-center justify-center gap-2 px-4 py-3 rounded-xl border border-dashed text-xs"
            >
              <Plus size={13} />
              + Add Custom Folder / NAS Path
            </button>
          )}
        </div>

        {/* Skip */}
        <div className="flex justify-center">
          <button onClick={onSkip} className="text-[11px] ob-body hover:ob-title underline underline-offset-2 transition-colors" style={mono()}>
            Skip for Now — I stream via Qobuz / TIDAL only
          </button>
        </div>
      </div>

      {/* Right: options + info */}
      <div className="flex flex-col gap-6">
        <div>
          <SectionLabel>Folder Monitoring</SectionLabel>
          <div className="onboarding-storage-card flex items-start gap-4 px-5 py-4 rounded-xl border">
            <div className="flex-1 min-w-0">
              <p className="text-sm font-semibold ob-title mb-1">Auto-watch folders for library updates</p>
              <p className="text-[11px] ob-body leading-relaxed">
                Mono monitors folders in real time and imports new FLAC, WAV, DSD, and AIFF files as they appear.
              </p>
            </div>
            <button
              role="switch"
              aria-checked={storage.autoWatch}
              onClick={() => setStorage({ ...storage, autoWatch: !storage.autoWatch })}
              className={["relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200", storage.autoWatch ? "ob-toggle-active" : "ob-toggle-inactive"].join(" ")}
            >
              <span className={["inline-block h-5 w-5 transform rounded-full bg-white shadow transition duration-200", storage.autoWatch ? "translate-x-5" : "translate-x-0"].join(" ")} />
            </button>
          </div>
        </div>

        <div className="ob-infobox border rounded-2xl p-5 flex flex-col gap-4">
          {[
            { title: "Supported Formats", desc: "FLAC · WAV · AIFF · DSD (DSF/DFF) · ALAC · MP3 · AAC" },
            { title: "NAS & Network Drives", desc: "Map UNC paths or mount points. Mono reads directly without re-encoding." },
            { title: "Local Only", desc: "File paths never leave your machine. Metadata is indexed locally." },
          ].map(({ title, desc }) => (
            <div key={title}>
              <p className="text-xs font-semibold ob-title mb-0.5">{title}</p>
              <p className="text-[11px] ob-body leading-relaxed">{desc}</p>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

// ─── Hardware status readout (step 4 footer center) ──────────────────────────

function HardwareStatusReadout({ audio }: { audio: AudioSetup | null }) {
  if (!audio?.deviceName) return null;
  const spec = [audio.driverType === "ASIO" ? "ASIO Direct" : "WASAPI Exclusive", audio.bufferSize + " samples"].join(" · ");
  return (
    <div
      className="flex items-center gap-2 flex-shrink-0 overflow-hidden px-3 py-1.5 rounded-full border"
      style={{
        background: "var(--ob-hw-pill-bg)",
        borderColor: "var(--ob-hw-pill-border)",
        color: "var(--ob-hw-pill-text)",
      }}
    >
      <span className="w-1.5 h-1.5 rounded-full flex-shrink-0" style={{ background: "#10b981", boxShadow: "0 0 6px rgba(16,185,129,0.8)" }} />
      <span className="text-[11px] font-medium truncate" style={{ fontFamily: "'DM Mono', monospace" }}>
        Output: <span className="font-semibold">{audio.deviceName}</span> · {spec} Ready
      </span>
    </div>
  );
}

// ─── Step 4: Hi-Res Streaming Integration ────────────────────────────────────

const STREAMING_FEATURES = [
  {
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.5} strokeLinecap="round" strokeLinejoin="round" className="w-5 h-5">
        <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
      </svg>
    ),
    title: "Zero Transcoding",
    desc: "Mono routes audio directly from provider CDNs. No intermediary re-encoding, ever.",
  },
  {
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.5} strokeLinecap="round" strokeLinejoin="round" className="w-5 h-5">
        <rect x="3" y="11" width="18" height="11" rx="2" ry="2" />
        <path d="M7 11V7a5 5 0 0 1 10 0v4" />
      </svg>
    ),
    title: "Token Licensing",
    desc: "Your credentials are stored locally and encrypted. Mono never proxies authentication.",
  },
  {
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.5} strokeLinecap="round" strokeLinejoin="round" className="w-5 h-5">
        <circle cx="12" cy="12" r="10" />
        <polyline points="12 6 12 12 16 14" />
      </svg>
    ),
    title: "Real-Time Sync",
    desc: "Synchronized playback across Lounge sessions preserves bit-perfect integrity.",
  },
];

function StepHiResStreaming({
  services,
  onConnect,
}: {
  services: { qobuz: boolean; tidal: boolean };
  onConnect: (service: "qobuz" | "tidal") => void;
}) {
  return (
    <div className="flex flex-col gap-5 w-full">
      {/* Service cards */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
        {/* Qobuz */}
        <div
          className="flex flex-col gap-4 rounded-2xl p-6 border transition-all"
          style={{
            background: "var(--ob-card-bg)",
            borderColor: services.qobuz ? "rgba(124,58,237,0.45)" : "var(--ob-card-border)",
            boxShadow: services.qobuz
              ? "0 0 0 1px rgba(124,58,237,0.12), 0 4px 20px -2px rgba(0,0,0,0.05)"
              : "var(--ob-card-shadow)",
          }}
        >
          <div className="flex items-center gap-3">
            <div
              className="w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0 text-black font-black text-lg select-none"
              style={{ background: "linear-gradient(135deg,#C9A227 0%,#F5D06B 50%,#B8891A 100%)", fontFamily: "Georgia,serif", letterSpacing: "-1px", boxShadow: "0 2px 8px rgba(201,162,39,0.28)" }}
            >
              Q
            </div>
            <div className="min-w-0">
              <p className="text-sm font-bold leading-tight" style={{ color: "var(--ob-title)" }}>Qobuz Studio</p>
              <p className="text-[11px] font-medium mt-0.5" style={{ ...mono(), color: "var(--ob-body)" }}>Direct API · 24-Bit / 192kHz Studio Master</p>
            </div>
            {services.qobuz && (
              <span className="ml-auto flex-shrink-0 inline-flex items-center gap-1.5 text-[10px] font-semibold px-2.5 py-1 rounded-full" style={{ ...mono(), background: "rgba(16,185,129,0.1)", border: "1px solid rgba(16,185,129,0.25)", color: "#10b981" }}>
                <span className="w-1.5 h-1.5 rounded-full" style={{ background: "#10b981", boxShadow: "0 0 5px rgba(16,185,129,0.7)" }} />
                Connected
              </span>
            )}
          </div>
          <ul className="flex flex-col gap-2">
            {["Lossless 16-Bit / 44.1kHz CD Quality", "Hi-Res 24-Bit / 192kHz Studio Master", "Direct CDN · Zero Transcoding"].map((item) => (
              <li key={item} className="flex items-center gap-2.5 text-[11px] font-medium" style={{ ...mono(), color: "var(--ob-list-text, #475569)" }}>
                <span className="w-1.5 h-1.5 rounded-full flex-shrink-0" style={{ background: "#C9A227" }} />
                {item}
              </li>
            ))}
          </ul>
          <button
            onClick={() => onConnect("qobuz")}
            className="ob-connect-btn mt-auto w-full py-2.5 rounded-xl transition-all cursor-pointer"
            style={services.qobuz ? {
              background: "rgba(16,185,129,0.08)",
              border: "1px solid rgba(16,185,129,0.3)",
              color: "#10b981",
              fontSize: 13,
              fontWeight: 600,
            } : undefined}
          >
            {services.qobuz ? "✓ Connected — Click to Disconnect" : "Connect Account"}
          </button>
        </div>

        {/* TIDAL */}
        <div
          className="flex flex-col gap-4 rounded-2xl p-6 border transition-all"
          style={{
            background: "var(--ob-card-bg)",
            borderColor: services.tidal ? "rgba(124,58,237,0.45)" : "var(--ob-card-border)",
            boxShadow: services.tidal
              ? "0 0 0 1px rgba(124,58,237,0.12), 0 4px 20px -2px rgba(0,0,0,0.05)"
              : "var(--ob-card-shadow)",
          }}
        >
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0" style={{ background: "#0D0D0D", border: "1.5px solid rgba(0,200,220,0.4)", boxShadow: "0 0 10px rgba(0,200,220,0.18)" }}>
              <svg width="20" height="16" viewBox="0 0 18 14" fill="none">
                <path d="M9 0 L12 4 L15 0 L18 4 L15 8 L12 4 L9 8 L6 4 L3 8 L0 4 L3 0 L6 4 Z" fill="#00C8DC" opacity="0.9" />
              </svg>
            </div>
            <div className="min-w-0">
              <p className="text-sm font-bold leading-tight" style={{ color: "var(--ob-title)" }}>TIDAL Max</p>
              <p className="text-[11px] font-medium mt-0.5" style={{ ...mono(), color: "var(--ob-body)" }}>OAuth 2.0 · HiRes FLAC &amp; Lossless Master</p>
            </div>
            {services.tidal && (
              <span className="ml-auto flex-shrink-0 inline-flex items-center gap-1.5 text-[10px] font-semibold px-2.5 py-1 rounded-full" style={{ ...mono(), background: "rgba(16,185,129,0.1)", border: "1px solid rgba(16,185,129,0.25)", color: "#10b981" }}>
                <span className="w-1.5 h-1.5 rounded-full" style={{ background: "#10b981", boxShadow: "0 0 5px rgba(16,185,129,0.7)" }} />
                Connected
              </span>
            )}
          </div>
          <ul className="flex flex-col gap-2">
            {["Lossless FLAC · MQA / HiRes FLAC", "TIDAL Connect · Native Device Sync", "PKCE OAuth 2.0 · Secure Token Auth"].map((item) => (
              <li key={item} className="flex items-center gap-2.5 text-[11px] font-medium" style={{ ...mono(), color: "var(--ob-list-text, #475569)" }}>
                <span className="w-1.5 h-1.5 rounded-full flex-shrink-0" style={{ background: "#00C8DC" }} />
                {item}
              </li>
            ))}
          </ul>
          <button
            onClick={() => onConnect("tidal")}
            className="ob-connect-btn mt-auto w-full py-2.5 rounded-xl transition-all cursor-pointer"
            style={services.tidal ? {
              background: "rgba(16,185,129,0.08)",
              border: "1px solid rgba(16,185,129,0.3)",
              color: "#10b981",
              fontSize: 13,
              fontWeight: 600,
            } : undefined}
          >
            {services.tidal ? "✓ Connected — Click to Disconnect" : "Connect Account"}
          </button>
        </div>
      </div>

      {/* Security & architecture panel */}
      <div
        className="grid grid-cols-1 md:grid-cols-3 rounded-2xl overflow-hidden border"
        style={{ borderColor: "var(--ob-card-border)", boxShadow: "var(--ob-card-shadow)" }}
      >
        {STREAMING_FEATURES.map(({ icon, title, desc }, i) => (
          <div
            key={title}
            className="flex flex-col gap-3 p-5"
            style={{
              background: "var(--ob-card-bg)",
              borderRight: i < STREAMING_FEATURES.length - 1 ? "1px solid var(--ob-card-border)" : undefined,
            }}
          >
            <div className="w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0" style={{ background: "var(--ob-chip-bg)", color: "var(--accent-solo)" }}>
              {icon}
            </div>
            <div>
              <p className="text-xs font-bold mb-1" style={{ color: "var(--ob-title)" }}>{title}</p>
              <p className="text-[11px] leading-relaxed" style={{ color: "var(--ob-body)" }}>{desc}</p>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ─── Step 5: Listener Profile ─────────────────────────────────────────────────

function StepProfile({
  profile, setProfile,
}: {
  profile: ListenerProfile; setProfile: (p: ListenerProfile) => void;
}) {
  const colorDef = AVATAR_COLORS.find((c) => c.key === profile.avatarColor) ?? AVATAR_COLORS[0];
  const initStr = initials(profile.displayName) || "AL";

  function patch(partial: Partial<ListenerProfile>) { setProfile({ ...profile, ...partial }); }
  function patchGear(partial: Partial<ListenerProfile["gear"]>) {
    setProfile({ ...profile, gear: { ...profile.gear, ...partial } });
  }

  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-10">
      {/* Left: persona */}
      <div className="flex flex-col gap-7">
        <div>
          <SectionLabel>Public Persona</SectionLabel>
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

          <div>
            <p className="text-[10px] tracking-widest text-zinc-400 uppercase font-semibold mb-2.5" style={mono()}>Avatar Color</p>
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

        {/* Badge preview */}
        <div>
          <SectionLabel>Live Lounge Badge Preview</SectionLabel>
          <div className="ob-preview border border-dashed rounded-2xl p-5 flex flex-col gap-4">
            <div className="flex items-center gap-3">
              <div
                className="w-9 h-9 rounded-full flex items-center justify-center text-[11px] font-bold flex-shrink-0 select-none"
                style={{ background: colorDef.bg, color: colorDef.text }}
              >
                {initStr}
              </div>
              <div className="min-w-0">
                <p className="text-xs font-semibold ob-title truncate">{profile.displayName.trim() || "Audiophile Listener"}</p>
                <p className="text-[10px] ob-body" style={mono()}>
                  {profile.gear.dac.trim() || "DAC pending"} ·{" "}
                  <span style={{ color: "var(--badge-verified-text)" }}>Bit-Perfect Stream</span>
                </p>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <span className="ob-chip inline-flex items-center gap-1.5 text-[9px] font-semibold border px-2.5 py-1 rounded-full" style={mono()}>
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

      {/* Right: hardware rack */}
      <div className="flex flex-col gap-6">
        <div>
          <SectionLabel>Hardware Rack</SectionLabel>
          <p className="text-[10px] text-zinc-400 -mt-2 mb-4" style={mono()}>
            Displayed in session gear lists · optional
          </p>
          <div className="flex flex-col gap-4">
            <GearInput label="Headphones / Monitors" value={profile.gear.headphonesOrSpeakers} placeholder="e.g., Sennheiser HD800S / Genelec 8351B" onChange={(v) => patchGear({ headphonesOrSpeakers: v })} />
            <GearInput label="Amplifier / Preamp" value={profile.gear.amplifier} placeholder="e.g., Ferrum OOR / Benchmark HPA4" onChange={(v) => patchGear({ amplifier: v })} />
            <GearInput label="Primary DAC" value={profile.gear.dac} placeholder="e.g., Holo May L3" onChange={(v) => patchGear({ dac: v })} />
          </div>
        </div>

        <div className="setup-signal-chain-panel">
          <p className="text-[9px] tracking-widest uppercase font-semibold chain-label" style={mono()}>Signal Chain</p>
          {[
            { label: "SOURCE", value: "Mono Audio Engine · Bit-Perfect" },
            { label: "DAC",    value: profile.gear.dac.trim() || "—" },
            { label: "AMP",    value: profile.gear.amplifier.trim() || "—" },
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

// ─── Step 6: Finish & Launch ──────────────────────────────────────────────────

function StepReady({
  profile,
  storage,
  services,
  audio,
  onLaunch,
}: {
  profile: ListenerProfile;
  storage: StorageConfig;
  services: { qobuz: boolean; tidal: boolean };
  audio: AudioEngineConfig;
  onLaunch: () => void;
}) {
  const colorDef = AVATAR_COLORS.find((c) => c.key === profile.avatarColor) ?? AVATAR_COLORS[0];
  const initStr = initials(profile.displayName) || "AL";

  // 장치를 고르지 않았으면 출력은 OS 기본으로 간다. 고르지도 않은 DAC 이름을 적어 두면
  // 사용자는 설정이 끝났다고 믿고, 소리는 다른 데서 난다.
  const activeDevice = audio.deviceName
    ? `${audio.deviceName} · ${audio.driverType === "ASIO" ? "ASIO Direct" : "WASAPI Exclusive"}`
    : "System default output";

  const connectedCount = [services.qobuz, services.tidal].filter(Boolean).length;
  const serviceLabel = connectedCount === 0 ? "Local library only" : [services.qobuz && "Qobuz Studio", services.tidal && "TIDAL Max"].filter(Boolean).join(" · ");

  const rows = [
    { label: "Source",   value: storage.folders.length > 0 ? `${storage.folders.length} local folder${storage.folders.length > 1 ? "s" : ""} indexed` : "Streaming only" },
    { label: "Output",   value: activeDevice },
    { label: "Streaming", value: serviceLabel },
    { label: "Profile",  value: profile.displayName.trim() || "Audiophile Listener" },
  ];

  return (
    <div className="flex flex-col items-center gap-12 py-4">
      {/* Avatar */}
      <div className="flex flex-col items-center gap-4">
        <div className="relative">
          <div
            className="w-20 h-20 rounded-full flex items-center justify-center text-xl font-bold select-none"
            style={{ background: colorDef.bg, color: colorDef.text, boxShadow: "0 0 0 3px var(--ob-card-border-active), 0 0 24px rgba(124,58,237,0.25)" }}
          >
            {initStr}
          </div>
          <span
            className="absolute -bottom-1 -right-1 w-6 h-6 rounded-full flex items-center justify-center"
            style={{ background: "#10b981", boxShadow: "0 0 0 2px var(--ob-surface)" }}
          >
            <Check size={12} color="#fff" strokeWidth={3} />
          </span>
        </div>
        <div className="text-center">
          <p className="text-lg font-semibold ob-title">{profile.displayName.trim() || "Audiophile Listener"}</p>
          <p className="text-[11px] ob-body mt-0.5" style={mono()}>All Set for Bit-Perfect Listening</p>
        </div>
      </div>

      {/* Summary recap */}
      <div className="w-full max-w-md flex flex-col gap-2">
        {rows.map(({ label, value }) => (
          <div key={label} className="onboarding-storage-card flex items-center gap-3 px-4 py-3 rounded-xl border">
            <span className="w-2 h-2 rounded-full bg-emerald-500 flex-shrink-0" style={{ boxShadow: "0 0 5px rgba(16,185,129,0.5)" }} />
            <span className="text-[10px] text-zinc-400 uppercase font-semibold w-20 flex-shrink-0" style={mono()}>{label}</span>
            <span className="text-xs ob-title truncate flex-1" style={mono()}>{value}</span>
          </div>
        ))}
      </div>

      {/* Subtext */}
      <p className="text-[11px] ob-body text-center max-w-sm leading-relaxed" style={mono()}>
        All settings can be changed anytime in{" "}
        <span style={{ color: "var(--accent-solo)" }}>Settings → Audio</span>.
        Your hardware stream is verified and ready.
      </p>

      {/* Primary CTA */}
      <button
        onClick={onLaunch}
        className="setup-btn-continue text-sm font-semibold px-12 py-4 rounded-2xl flex items-center gap-3"
        style={{ minWidth: 280 }}
      >
        Launch Mono &amp; Enter Live Lounges
        <svg className="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
          <polyline points="9 18 15 12 9 6" />
        </svg>
      </button>
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

type Step = 1 | 2 | 3 | 4 | 5 | 6;

export default function OnboardingWizard({
  onComplete, onExit, isDark, onToggleTheme, onServiceChange,
}: WizardProps) {
  const [step, setStep] = useState<Step>(1);

  // 연동 여부는 Core 가 안다. 마법사가 로컬 사본을 따로 들면 설정 화면과 어긋나고,
  // Core 에 토큰이 남아 있어도 "연결 안 됨"으로 보인다.
  const { streamingAccounts, folders: coreFolders } = useMono();
  const cmd = useMonoCommands();
  const services = {
    qobuz: streamingAccounts.some((a) => a.provider === StreamingProvider.Qobuz && a.connected),
    tidal: streamingAccounts.some((a) => a.provider === StreamingProvider.Tidal && a.connected),
  };

  const [authService, setAuthService] = useState<"qobuz" | "tidal" | null>(null);

  const [storage, setStorage] = useState<StorageConfig>({
    folders: [DEFAULT_MUSIC_FOLDER],
    autoWatch: true,
  });

  // Core 가 이미 감시 중인 폴더가 있으면 그걸 보여 준다 — 자리표시자 경로보다 진실에 가깝다.
  useEffect(() => {
    const real = coreFolders.map((f) => f.path).filter(Boolean);
    if (real.length > 0) setStorage((prev) => ({ ...prev, folders: real }));
  }, [coreFolders]);

  const [profile, setProfile] = useState<ListenerProfile>({
    displayName: "Audiophile Listener",
    avatarColor: "zinc",
    gear: { headphonesOrSpeakers: "", amplifier: "", dac: "" },
  });

  const [audioConfig, setAudioConfig] = useState<AudioEngineConfig>({
    driverType: "ASIO",
    deviceId: "",
    deviceName: "",
    bufferSize: 256,
    exclusiveMode: false,
  });
  const [selectedDeviceId, setSelectedDeviceId] = useState<string | null>(null);

  // 연동/해제는 Core 가 수행한다. 마법사는 요청만 보내고, 결과는 streamingAccounts 로 돌아온다.
  function handleToggleService(service: "qobuz" | "tidal") {
    const provider = service === "qobuz" ? StreamingProvider.Qobuz : StreamingProvider.Tidal;
    if (services[service]) {
      void cmd.unlinkStreaming(provider);
      onServiceChange?.(service, false);
      return;
    }
    setAuthService(service);
  }

  // StreamingAuthModal wired to wizard's own connect flow (for steps outside SettingsPage)
  function handleAuthSuccess(service: "qobuz" | "tidal") {
    cmd.refreshStreamingAccounts();
    onServiceChange?.(service, true);
    setAuthService(null);
  }

  function handleComplete() {
    const name = profile.displayName.trim() || "Audiophile Listener";
    // 이름은 룸 멤버 목록·아카이브 참가자에 그대로 쓰인다.
    cmd.setDisplayName(name);
    // 고른 폴더를 실제로 읽어 들인다. 마법사를 끝냈는데 라이브러리가 비어 있으면
    // 사용자는 무엇을 고른 건지 알 수 없다.
    const known = new Set(coreFolders.map((f) => f.path));
    for (const folder of storage.folders) {
      if (folder && folder !== DEFAULT_MUSIC_FOLDER && !known.has(folder)) {
        void cmd.scanLibrary(folder);
      }
    }
    // 고른 값을 이 PC 에 남긴다. 다음 실행 때 마법사를 건너뛰고 이대로 복원한다.
    // Core 에 쓰므로 WebView 캐시를 지워도, 브라우저로 열어도 같은 설정을 본다.
    const chosen = audioConfig.deviceId ? audioConfig : null;
    // 다음 실행이 아니라 지금부터 그 장치로 들리게 한다. 마법사에서 DAC 를 고르고
    // 첫 곡이 내장 스피커로 나오면 무엇을 고른 건지 알 수 없다.
    if (chosen && hasShell()) {
      void (async () => {
        const roomId = await cmd.ensureSoloRoom();
        await startConfiguredOutput(roomId);
      })();
    }
    void saveSetup({
      completed: true,
      theme: isDark ? "dark" : "light",
      folders: storage.folders.filter((f) => f && f !== DEFAULT_MUSIC_FOLDER),
      autoWatch: storage.autoWatch,
      audio: chosen ?? undefined,
      profile: { ...profile, displayName: name },
    });

    onComplete({ ...profile, displayName: name });
  }

  function advance() {
    setStep((s) => Math.min(s + 1, 6) as Step);
  }

  function back() {
    setStep((s) => Math.max(s - 1, 1) as Step);
  }

  const stepTitles: Record<Step, string> = {
    1: "Welcome to Mono",
    2: "Where is your music stored?",
    3: "Audio Output Device",
    4: "Hi-Res Streaming Integration",
    5: "Listener Profile & Signature Rig",
    6: "All Set for Bit-Perfect Listening",
  };

  const stepSubtitles: Record<Step, string> = {
    1: "",
    2: "Select local drives, music folders, or NAS partitions hosting your audio library.",
    3: "Select your primary playback interface. Mono establishes a direct, bit-perfect hardware stream.",
    4: "Link your active subscriptions to enable 24-bit streaming and Live Lounge synchronization.",
    5: "Configure your public identity and hardware rack for community listening sessions.",
    6: "Your Mono environment is configured and ready to launch.",
  };

  const isWelcomeStep = step === 1;
  const isLastStep = step === 6;

  return (
    <div className="ob-surface fixed inset-0 z-50 flex flex-col h-screen max-h-screen overflow-hidden select-none">

      {/* ── Sticky header with 6-step breadcrumb ────────────────────────────── */}
      <header className="ob-header w-full border-b backdrop-blur-md sticky top-0 z-30 px-8 py-4 flex items-center justify-between gap-6">
        {/* Wordmark */}
        <div className="flex items-center gap-3 flex-shrink-0">
          <span className="text-base font-bold tracking-tight ob-title">Mono</span>
          <span className="text-[9px] font-semibold tracking-widest text-zinc-400 uppercase" style={mono()}>Studio Setup</span>
        </div>

        {/* 6-step progress breadcrumb */}
        <nav className="flex items-center gap-0.5 overflow-x-auto">
          {STEPS.map(({ num, label }, i) => {
            const stepNum = (i + 1) as Step;
            const isActive = step === stepNum;
            const isComplete = step > stepNum;
            return (
              <button
                key={num}
                onClick={() => { if (stepNum < step) setStep(stepNum); }}
                disabled={stepNum >= step}
                className={[
                  "flex items-center gap-1 px-2.5 py-1.5 rounded-lg transition-colors whitespace-nowrap",
                  isActive ? "setup-accent-text" : isComplete ? "text-zinc-500 hover:text-zinc-700 cursor-pointer" : "text-zinc-400 cursor-default",
                ].join(" ")}
              >
                <span
                  className={["text-[10px] font-semibold", isActive ? "border-b-2 setup-accent-underline pb-0.5" : ""].join(" ")}
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
            <button onClick={onToggleTheme} className="onboarding-theme-toggle" aria-label="Toggle theme">
              {isDark ? <Sun size={15} /> : <Moon size={15} />}
            </button>
          )}
          <button onClick={onExit} className="text-[10px] text-zinc-400 hover:text-zinc-800 transition-colors" style={mono()}>
            Exit Setup
          </button>
        </div>
      </header>

      {/* ── Canvas ───────────────────────────────────────────────────────────── */}
      <main className={isWelcomeStep
        ? "flex-1 flex flex-col items-center justify-center overflow-hidden px-6 -translate-y-4"
        : "flex-1 overflow-y-auto px-6 py-10"}
      >
        {/* Step header (hidden on welcome — welcome renders its own) */}
        {!isWelcomeStep && (
          <div className="max-w-5xl w-full mx-auto">
            <div className="mb-10">
              <p className="text-[10px] tracking-widest uppercase mb-2" style={mono()}>
                <span className="setup-accent-text">Step {String(step).padStart(2, "0")} / 06</span>
              </p>
              <h1 className="text-2xl font-semibold ob-title tracking-tight leading-tight mb-2">
                {stepTitles[step]}
              </h1>
              {stepSubtitles[step] && (
                <p className="text-sm ob-body leading-relaxed max-w-xl">{stepSubtitles[step]}</p>
              )}
            </div>

            {/* Step content */}
            {step === 2 && (
              <StepStorage storage={storage} setStorage={setStorage} onSkip={advance} />
            )}
            {step === 3 && (
              <Step1AudioEngine
                initialConfig={audioConfig}
                selectedDeviceId={selectedDeviceId}
                onSelectDevice={(id) => setSelectedDeviceId(id || null)}
                onNext={setAudioConfig}
              />
            )}
            {step === 4 && (
              <StepHiResStreaming
                services={services}
                onConnect={(service) => {
                  if (services[service]) {
                    // Already connected → disconnect immediately
                    handleToggleService(service);
                  } else {
                    // Not connected → open auth modal
                    setAuthService(service);
                  }
                }}
              />
            )}
            {step === 5 && <StepProfile profile={profile} setProfile={setProfile} />}
            {step === 6 && (
              <StepReady profile={profile} storage={storage} services={services} audio={audioConfig} onLaunch={handleComplete} />
            )}
          </div>
        )}

        {step === 1 && <StepWelcome onStart={advance} />}
      </main>

      {/* ── Sticky footer navigation (hidden on welcome & done) ──────────────── */}
      {!isWelcomeStep && !isLastStep && (
        <footer className="ob-header ob-divider border-t backdrop-blur-md py-4 px-8 sticky bottom-0 z-30">
          <div className="max-w-5xl w-full mx-auto flex items-center justify-between gap-6">
            {/* Left */}
            <div className="flex items-center gap-4">
              <button
                onClick={back}
                className="text-xs text-zinc-500 hover:text-zinc-900 font-medium transition-colors flex items-center gap-1.5"
              >
                <svg className="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
                  <polyline points="15 18 9 12 15 6" />
                </svg>
                Back
              </button>
              {step === 2 && (
                <button
                  onClick={advance}
                  className="text-[10px] text-zinc-400 hover:text-zinc-600 underline underline-offset-2 transition-colors"
                  style={mono()}
                >
                  Skip for Now
                </button>
              )}
              {step === 4 && (
                <button
                  onClick={advance}
                  className="text-xs font-medium transition-colors"
                  style={{ ...mono(), color: "var(--ob-body)" }}
                  onMouseEnter={(e) => { (e.currentTarget as HTMLButtonElement).style.color = "var(--ob-title)"; }}
                  onMouseLeave={(e) => { (e.currentTarget as HTMLButtonElement).style.color = "var(--ob-body)"; }}
                >
                  Skip (Local Only)
                </button>
              )}
            </div>

            {/* Center: hardware status readout (step 4 only) */}
            {step === 4 && <HardwareStatusReadout audio={audioConfig.deviceId ? audioConfig : null} />}

            {/* Right: primary CTA */}
            {step === 2 && (
              <button onClick={advance} className="setup-btn-continue text-xs font-medium px-8 py-3 rounded-xl flex items-center gap-2 flex-shrink-0">
                Continue to Output Device
                <svg className="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round"><polyline points="9 18 15 12 9 6" /></svg>
              </button>
            )}
            {/* 장치를 못 고르는 PC(브라우저·열거 실패)에서 마법사가 막히면 안 된다.
                고르지 않으면 저장할 오디오 설정이 없고, 출력은 OS 기본 장치로 간다. */}
            {step === 3 && (
              <button
                onClick={() => {
                  (document.getElementById("ae-emit-config") as HTMLButtonElement | null)?.click();
                  advance();
                }}
                className="setup-btn-continue text-xs font-medium px-8 py-3 rounded-xl flex items-center gap-2 flex-shrink-0"
              >
                Next: Streaming Services
                <svg className="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round"><polyline points="9 18 15 12 9 6" /></svg>
              </button>
            )}
            {step === 4 && (
              <button onClick={advance} className="setup-btn-continue text-xs font-medium px-8 py-3 rounded-xl flex items-center gap-2 flex-shrink-0">
                Continue to Profile
                <svg className="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round"><polyline points="9 18 15 12 9 6" /></svg>
              </button>
            )}
            {step === 5 && (
              <button onClick={advance} className="setup-btn-continue text-xs font-medium px-8 py-3 rounded-xl flex items-center gap-2 flex-shrink-0">
                Next: Finish Setup
                <svg className="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round"><polyline points="9 18 15 12 9 6" /></svg>
              </button>
            )}
          </div>
        </footer>
      )}

      {/* Step 6 has its own CTA inside StepReady — no footer needed */}
      {isLastStep && (
        <footer className="ob-header ob-divider border-t backdrop-blur-md py-4 px-8 sticky bottom-0 z-30">
          <div className="max-w-5xl w-full mx-auto flex items-center gap-4">
            <button
              onClick={back}
              className="text-xs text-zinc-500 hover:text-zinc-900 font-medium transition-colors flex items-center gap-1.5"
            >
              <svg className="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
                <polyline points="15 18 9 12 15 6" />
              </svg>
              Back
            </button>
          </div>
        </footer>
      )}

      <StreamingAuthModal
        isOpen={authService !== null}
        service={authService}
        onClose={() => setAuthService(null)}
        onSuccess={handleAuthSuccess}
      />
    </div>
  );
}
