import { useState } from "react";

export interface ListenerProfile {
  displayName: string;
  avatarColor: string;
  gear: {
    headphonesOrSpeakers: string;
    amplifier: string;
    dac: string;
  };
}

interface Props {
  onComplete: (profile: ListenerProfile) => void;
  onBack: () => void;
  detectedDacName?: string;
}

const AVATAR_COLORS: { key: string; bg: string; text: string; label: string }[] = [
  { key: "zinc",    bg: "#18181b", text: "#ffffff", label: "Zinc" },
  { key: "slate",   bg: "#1e293b", text: "#ffffff", label: "Slate" },
  { key: "amber",   bg: "#b45309", text: "#ffffff", label: "Amber" },
  { key: "emerald", bg: "#065f46", text: "#ffffff", label: "Emerald" },
];

function initials(name: string): string {
  return name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? "")
    .join("");
}

function GearInput({
  label,
  value,
  placeholder,
  onChange,
}: {
  label: string;
  value: string;
  placeholder: string;
  onChange: (v: string) => void;
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <label
        className="text-[10px] font-semibold tracking-widest text-zinc-400 uppercase"
        style={{ fontFamily: "'DM Mono', monospace" }}
      >
        {label}
      </label>
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

export default function Step3AudiophileRig({ onComplete, onBack, detectedDacName }: Props) {
  const [displayName, setDisplayName] = useState("Audiophile Listener");
  const [avatarColor, setAvatarColor] = useState("zinc");
  const [headphones, setHeadphones] = useState("");
  const [amplifier, setAmplifier] = useState("");
  const [dac, setDac] = useState(detectedDacName ?? "Bit-Perfect ASIO DAC");

  const colorDef = AVATAR_COLORS.find((c) => c.key === avatarColor) ?? AVATAR_COLORS[0];

  const loungeDacLabel = dac.trim() || "Unknown DAC";
  const loungeHpLabel = headphones.trim() || "—";
  const loungeDisplayName = displayName.trim() || "Listener";

  function handleComplete() {
    onComplete({
      displayName: loungeDisplayName,
      avatarColor,
      gear: {
        headphonesOrSpeakers: headphones,
        amplifier,
        dac,
      },
    });
  }

  return (
    <div className="flex flex-col gap-8 w-full max-w-xl mx-auto px-1">
      {/* Header */}
      <div className="flex flex-col gap-1.5">
        <span
          className="text-[10px] tracking-widest text-zinc-400 uppercase"
          style={{ fontFamily: "'DM Mono', monospace" }}
        >
          Step 03 / 03 · Listener Profile &amp; Rig
        </span>
        <h2 className="text-xl font-semibold ob-title tracking-tight leading-snug">
          Listener Profile &amp; Signature Rig
        </h2>
        <p className="text-sm ob-body leading-relaxed max-w-md">
          Set up your public identity and playback gear. This appears in Live Lounges and
          community listening sessions.
        </p>
      </div>

      {/* Section 1: Identity & Avatar */}
      <div className="flex flex-col gap-4">
        <span
          className="text-[10px] font-semibold tracking-widest text-zinc-400 uppercase"
          style={{ fontFamily: "'DM Mono', monospace" }}
        >
          Public Identity
        </span>

        <div className="flex items-center gap-5">
          {/* Avatar preview */}
          <div
            className="w-14 h-14 rounded-full flex items-center justify-center flex-shrink-0 text-base font-bold tracking-tight select-none transition-colors duration-200"
            style={{ background: colorDef.bg, color: colorDef.text }}
          >
            {initials(displayName) || "AL"}
          </div>

          <div className="flex flex-col gap-2 flex-1 min-w-0">
            {/* Display name */}
            <input
              type="text"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              placeholder="Display name"
              className="setup-input-field"
            />

            {/* Color swatches */}
            <div className="flex items-center gap-2">
              <span
                className="text-[9px] text-zinc-400 mr-1"
                style={{ fontFamily: "'DM Mono', monospace" }}
              >
                COLOR
              </span>
              {AVATAR_COLORS.map((c) => (
                <button
                  key={c.key}
                  onClick={() => setAvatarColor(c.key)}
                  title={c.label}
                  className="setup-swatch w-5 h-5 rounded-full transition-transform focus:outline-none"
                  style={{
                    background: c.bg,
                    outline:
                      avatarColor === c.key
                        ? `2px solid ${c.bg}`
                        : "2px solid transparent",
                    outlineOffset: "2px",
                    transform: avatarColor === c.key ? "scale(1.15)" : "scale(1)",
                  }}
                />
              ))}
            </div>
          </div>
        </div>
      </div>

      {/* Section 2: Gear Metadata */}
      <div className="flex flex-col gap-4">
        <div className="flex flex-col gap-0.5">
          <span
            className="text-[10px] font-semibold tracking-widest text-zinc-400 uppercase"
            style={{ fontFamily: "'DM Mono', monospace" }}
          >
            Hardware Setup
          </span>
          <p className="text-[10px] text-zinc-400" style={{ fontFamily: "'DM Mono', monospace" }}>
            Displayed in Session Gear Lists · Optional
          </p>
        </div>

        <GearInput
          label="Headphones / Speakers"
          value={headphones}
          placeholder="e.g., Sennheiser HD800S / Genelec 8351B"
          onChange={setHeadphones}
        />
        <GearInput
          label="Amplifier / Preamp"
          value={amplifier}
          placeholder="e.g., Ferrum OOR / Benchmark HPA4"
          onChange={setAmplifier}
        />
        <GearInput
          label="Primary DAC"
          value={dac}
          placeholder="e.g., Holo Audio Spring 3"
          onChange={setDac}
        />
      </div>

      {/* Section 3: Lounge Badge Preview */}
      <div className="flex flex-col gap-3">
        <span
          className="text-[10px] font-semibold tracking-widest text-zinc-400 uppercase"
          style={{ fontFamily: "'DM Mono', monospace" }}
        >
          Live Lounge Badge Preview
        </span>
        <div className="ob-preview border border-dashed rounded-xl p-3 flex items-center gap-3">
          {/* Mini avatar */}
          <div
            className="w-8 h-8 rounded-full flex items-center justify-center flex-shrink-0 text-[10px] font-bold select-none"
            style={{ background: colorDef.bg, color: colorDef.text }}
          >
            {initials(displayName) || "AL"}
          </div>
          <div className="flex flex-col gap-0.5 min-w-0">
            <span className="text-[11px] font-semibold ob-title leading-tight truncate">
              {loungeDisplayName}
            </span>
            <span
              className="text-[10px] ob-body truncate"
              style={{ fontFamily: "'DM Mono', monospace" }}
            >
              {loungeDacLabel}
              {loungeHpLabel !== "—" && ` · ${loungeHpLabel}`}
              {" · "}
              <span style={{ color: "var(--badge-verified-text)" }}>Bit-Perfect Stream</span>
            </span>
          </div>
        </div>
      </div>

      {/* Bottom Action Bar */}
      <div className="ob-divider flex items-center justify-between pt-2 border-t">
        <button
          onClick={onBack}
          className="text-xs ob-body hover:text-zinc-900 font-medium px-3 py-2 rounded-lg transition-colors"
        >
          ← Back
        </button>
        <button
          onClick={handleComplete}
          className="setup-btn-continue text-xs font-semibold px-6 py-2.5 rounded-xl"
        >
          Complete &amp; Launch Mono →
        </button>
      </div>
    </div>
  );
}
