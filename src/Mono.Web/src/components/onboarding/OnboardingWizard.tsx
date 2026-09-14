import { useState, useEffect } from "react";
import { Sun, Moon, FolderOpen, Plus, X, Check } from "lucide-react";
import StreamingAuthModal from "./StreamingAuthModal";
import SettingsPage from "../SettingsPage";
import { type ListenerProfile } from "./Step3AudiophileRig";
import { type AudioEngineConfig } from "./Step1AudioEngine";
import { useMono, useMonoCommands } from "../../state/MonoProvider";
import { StreamingProvider } from "../../lib/protocol";
import { hasShell, pickFolder } from "../../lib/shell";

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

// ─── Step 3: Audio Output — SettingsPage (audio tab) ─────────────────────────

function StepAudioOutput() {
  return (
    <div className="ob-settings-embed rounded-2xl overflow-hidden border" style={{ borderColor: "var(--ob-card-border)" }}>
      <SettingsPage initialTab="audio" />
    </div>
  );
}

// ─── Step 4: Streaming Accounts — SettingsPage (accounts tab) ────────────────

function StepStreamingAccounts({
  services,
  onToggleService,
}: {
  services: { qobuz: boolean; tidal: boolean };
  onToggleService: (service: "qobuz" | "tidal") => void;
}) {
  return (
    <div className="ob-settings-embed rounded-2xl overflow-hidden border" style={{ borderColor: "var(--ob-card-border)" }}>
      <SettingsPage
        initialTab="accounts"
        connectedServices={services}
        onToggleService={onToggleService}
      />
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
  onLaunch,
}: {
  profile: ListenerProfile;
  storage: StorageConfig;
  onLaunch: () => void;
}) {
  const colorDef = AVATAR_COLORS.find((c) => c.key === profile.avatarColor) ?? AVATAR_COLORS[0];
  const initStr = initials(profile.displayName) || "AL";

  // Read streaming state from localStorage (SettingsPage writes there on connect)
  const [liveServices, setLiveServices] = useState({ qobuz: false, tidal: false });
  useEffect(() => {
    try {
      const raw = localStorage.getItem("mono_streaming_services");
      if (raw) {
        const parsed = JSON.parse(raw) as { qobuz?: { connected?: boolean }; tidal?: { connected?: boolean } };
        setLiveServices({
          qobuz: parsed.qobuz?.connected === true,
          tidal: parsed.tidal?.connected === true,
        });
      }
    } catch {}
  }, []);

  const [activeDevice] = useState(() => {
    // SettingsPage keeps device in its own state; surface the label from localStorage if written,
    // otherwise fall back to "Living Room · Holo May L3" (the default selected device)
    return localStorage.getItem("mono_output_device") ?? "Living Room · Holo May L3";
  });

  const connectedCount = [liveServices.qobuz, liveServices.tidal].filter(Boolean).length;
  const serviceLabel = connectedCount === 0 ? "Local library only" : [liveServices.qobuz && "Qobuz Studio", liveServices.tidal && "TIDAL Max"].filter(Boolean).join(" · ");

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
    localStorage.setItem("mono_onboarding_completed", "true");
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
    4: "Streaming Accounts",
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
            {step === 3 && <StepAudioOutput />}
            {step === 4 && (
              <StepStreamingAccounts services={services} onToggleService={handleToggleService} />
            )}
            {step === 5 && <StepProfile profile={profile} setProfile={setProfile} />}
            {step === 6 && (
              <StepReady profile={profile} storage={storage} onLaunch={handleComplete} />
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
                  className="text-[10px] text-zinc-400 hover:text-zinc-600 underline underline-offset-2 transition-colors"
                  style={mono()}
                >
                  Skip (Local Only)
                </button>
              )}
            </div>

            {/* Right: primary CTA */}
            {step === 2 && (
              <button onClick={advance} className="setup-btn-continue text-xs font-medium px-8 py-3 rounded-xl flex items-center gap-2 flex-shrink-0">
                Continue to Output Device
                <svg className="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round"><polyline points="9 18 15 12 9 6" /></svg>
              </button>
            )}
            {step === 3 && (
              <button onClick={advance} className="setup-btn-continue text-xs font-medium px-8 py-3 rounded-xl flex items-center gap-2 flex-shrink-0">
                Next: Streaming Services
                <svg className="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round"><polyline points="9 18 15 12 9 6" /></svg>
              </button>
            )}
            {step === 4 && (
              <button onClick={advance} className="setup-btn-continue text-xs font-medium px-8 py-3 rounded-xl flex items-center gap-2 flex-shrink-0">
                Next: Listener Profile
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
