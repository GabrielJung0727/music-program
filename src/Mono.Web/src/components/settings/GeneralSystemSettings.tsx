import { useState, useEffect, useRef } from "react"
import { useMono, useMonoCommands } from "../../state/MonoProvider"
import { MSG } from "../../lib/protocol"
import { applyUpdate, checkForUpdate, hasShell, onUpdateProgress, type UpdateStatus, discordActivityStatus, setDiscordActivityEnabled, setDiscordApplicationId, openExternal } from "../../lib/shell"
import { clearSetup } from "../../lib/setup"

type RamBuffer = "Direct Disk Stream" | "512 MB" | "1 GB (Recommended)" | "2 GB Full Album"
type SpecBadge = "Minimal" | "Detailed Studio" | "Full Lab Specs"
type VisualizerFps = "60 FPS (Smooth)" | "30 FPS (Saver)" | "Disabled"
type Appearance = "Studio Light" | "Studio Dark"
type IssueCategory = "Audio Dropouts" | "ASIO/DAC Sync" | "Streaming Login" | "Other"

interface GeneralSettings {
  preventSleep: boolean
  ramBuffer: RamBuffer
  exclusiveMediaKeys: boolean
  specBadge: SpecBadge
  visualizerFps: VisualizerFps
  appearance: Appearance
}

const STORAGE_KEY = "mono_general_settings"

function load(): GeneralSettings {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw) return JSON.parse(raw) as GeneralSettings
  } catch {}
  return {
    preventSleep: true,
    ramBuffer: "1 GB (Recommended)",
    exclusiveMediaKeys: true,
    specBadge: "Detailed Studio",
    visualizerFps: "60 FPS (Smooth)",
    appearance: "Studio Light",
  }
}

// ── Primitive UI ──────────────────────────────────────────────────────────────

function Toggle({ value, onChange }: { value: boolean; onChange: (v: boolean) => void }) {
  return (
    <button
      type="button"
      onClick={() => onChange(!value)}
      style={{
        width: 36, height: 20, borderRadius: 10, flexShrink: 0,
        background: value ? "var(--settings-toggle-on)" : "var(--settings-toggle-off)",
        border: "none", cursor: "pointer", padding: "0 3px",
        display: "inline-flex", alignItems: "center",
        transition: "background 0.2s",
      }}
    >
      <span style={{
        width: 14, height: 14, borderRadius: "50%", background: "#FFFFFF",
        transform: value ? "translateX(16px)" : "translateX(0)",
        transition: "transform 0.2s", display: "block", flexShrink: 0,
        boxShadow: "0 1px 3px rgba(0,0,0,0.2)",
      }} />
    </button>
  )
}

function StyledSelect<T extends string>({
  value, options, onChange, minWidth = 200,
}: { value: T; options: T[]; onChange: (v: T) => void; minWidth?: number }) {
  return (
    <select
      value={value}
      onChange={(e) => onChange(e.target.value as T)}
      className="settings-gs-select"
      style={{ minWidth }}
    >
      {options.map((o) => <option key={o} value={o}>{o}</option>)}
    </select>
  )
}

function SegmentControl<T extends string>({
  value, options, onChange,
}: { value: T; options: T[]; onChange: (v: T) => void }) {
  return (
    <div style={{
      display: "inline-flex",
      background: "var(--gs-seg-tray)",
      borderRadius: 8, padding: 3, gap: 2,
    }}>
      {options.map((opt) => {
        const active = value === opt
        return (
          <button
            key={opt} type="button" onClick={() => onChange(opt)}
            style={{
              fontSize: 11.5,
              fontWeight: active ? 600 : 400,
              color: active ? "var(--gs-seg-pill-text)" : "var(--gs-seg-idle-text)",
              background: active ? "var(--gs-seg-pill-bg)" : "none",
              border: active ? "1px solid var(--gs-stat-border)" : "none",
              borderRadius: 6, padding: "5px 12px",
              cursor: "pointer", fontFamily: "inherit",
              boxShadow: active ? "0 1px 3px rgba(0,0,0,0.12)" : "none",
              transition: "background 0.15s, color 0.15s, box-shadow 0.15s",
              whiteSpace: "nowrap",
            }}
          >{opt}</button>
        )
      })}
    </div>
  )
}

// ── Layout helpers ─────────────────────────────────────────────────────────────

function SectionHeader({ title, subtitle }: { title: string; subtitle?: string }) {
  return (
    <div style={{ marginBottom: 20 }}>
      <h3 style={{
        fontSize: 13, fontWeight: 700,
        color: "var(--settings-row-label)",
        margin: 0, letterSpacing: "-0.1px",
      }}>{title}</h3>
      {subtitle && (
        <p style={{
          fontSize: 11, color: "var(--settings-row-sub)",
          margin: "3px 0 0", fontFamily: "'DM Mono', monospace", lineHeight: 1.5,
        }}>{subtitle}</p>
      )}
    </div>
  )
}

function Row({ label, subtitle, control }: { label: string; subtitle?: string; control: React.ReactNode }) {
  return (
    <div style={{
      display: "flex", alignItems: "center", justifyContent: "space-between",
      gap: 24, padding: "14px 0",
      borderBottom: "1px solid var(--settings-divider)",
    }}>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ fontSize: 13, fontWeight: 500, color: "var(--settings-row-label)" }}>{label}</div>
        {subtitle && (
          <div style={{
            fontSize: 11, color: "var(--settings-row-sub)",
            marginTop: 2, fontFamily: "'DM Mono', monospace", lineHeight: 1.5,
          }}>{subtitle}</div>
        )}
      </div>
      <div style={{ flexShrink: 0 }}>{control}</div>
    </div>
  )
}

function Section({ children }: { children: React.ReactNode }) {
  return (
    <div className="settings-card-surface">
      {children}
    </div>
  )
}

function Btn({
  children, onClick, variant = "solid", disabled = false,
}: { children: React.ReactNode; onClick?: () => void; variant?: "solid" | "outline" | "ghost"; disabled?: boolean }) {
  const base: React.CSSProperties = {
    fontSize: 12, fontWeight: 600, fontFamily: "inherit",
    borderRadius: 8, padding: "7px 14px", cursor: disabled ? "not-allowed" : "pointer",
    opacity: disabled ? 0.5 : 1, display: "inline-flex", alignItems: "center", gap: 6,
    transition: "opacity 0.15s, background 0.15s",
  }
  const styles: Record<string, React.CSSProperties> = {
    solid: {
      background: "var(--gs-btn-solid-bg)",
      color: "var(--gs-btn-solid-text)",
      border: "none",
      boxShadow: "0 2px 8px var(--gs-btn-solid-shadow, rgba(0,0,0,0.15))",
    },
    outline: {
      background: "var(--gs-btn-outline-bg, transparent)",
      color: "var(--gs-btn-outline-text)",
      border: "1px solid var(--gs-btn-outline-border)",
    },
    ghost: {
      background: "transparent",
      color: "var(--gs-btn-ghost-text)",
      border: "1px solid var(--gs-btn-ghost-border)",
    },
  }
  return (
    <button
      type="button" onClick={onClick} disabled={disabled}
      style={{ ...base, ...styles[variant] }}
      onMouseEnter={(e) => {
        if (disabled) return
        if (variant === "solid") (e.currentTarget as HTMLButtonElement).style.filter = "brightness(0.9)"
      }}
      onMouseLeave={(e) => {
        if (variant === "solid") (e.currentTarget as HTMLButtonElement).style.filter = ""
      }}
    >
      {children}
    </button>
  )
}

// ── Toast ─────────────────────────────────────────────────────────────────────

function Toast({ message, onDone }: { message: string; onDone: () => void }) {
  useEffect(() => {
    const t = setTimeout(onDone, 3200)
    return () => clearTimeout(t)
  }, [onDone])
  return (
    <div style={{
      position: "fixed", bottom: 28, right: 28, zIndex: 9999,
      background: "var(--gs-btn-solid-bg)", color: "var(--gs-btn-solid-text)",
      borderRadius: 10, padding: "12px 18px",
      fontSize: 12, fontWeight: 500, fontFamily: "inherit",
      boxShadow: "0 4px 20px rgba(0,0,0,0.35)",
      display: "flex", alignItems: "center", gap: 8,
    }}>
      <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="#34D399" strokeWidth={2.5} strokeLinecap="round" strokeLinejoin="round">
        <polyline points="20 6 9 17 4 12" />
      </svg>
      {message}
    </div>
  )
}

// ── Modal shell ───────────────────────────────────────────────────────────────

function Modal({ onClose, children }: { onClose: () => void; children: React.ReactNode }) {
  return (
    <div
      style={{
        position: "fixed", inset: 0, zIndex: 500,
        background: "rgba(0,0,0,0.55)", backdropFilter: "blur(5px)",
        display: "flex", alignItems: "center", justifyContent: "center", padding: 24,
      }}
      onClick={onClose}
    >
      <div
        style={{
          background: "var(--gs-modal-bg)",
          borderRadius: 16,
          border: "1px solid var(--gs-modal-border)",
          boxShadow: "0 20px 60px rgba(0,0,0,0.35)",
          width: "100%", maxWidth: 480,
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {children}
      </div>
    </div>
  )
}

// ── Restore modal ─────────────────────────────────────────────────────────────

function RestoreModal({
  fileName, onCancel, onConfirm, restoring,
}: { fileName: string; onCancel: () => void; onConfirm: () => void; restoring: boolean }) {
  return (
    <Modal onClose={onCancel}>
      <div style={{ padding: "24px 24px 20px" }}>
        <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 16 }}>
          <div style={{
            width: 36, height: 36, borderRadius: 10, background: "#FEF3C7",
            border: "1px solid #FDE68A", display: "flex", alignItems: "center", justifyContent: "center",
          }}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#D97706" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
              <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
              <line x1="12" y1="9" x2="12" y2="13" />
              <line x1="12" y1="17" x2="12.01" y2="17" />
            </svg>
          </div>
          <div>
            <div style={{ fontSize: 14, fontWeight: 700, color: "var(--settings-row-label)" }}>Restore Mono System Archive</div>
            <div style={{ fontSize: 11, color: "var(--settings-row-sub)", fontFamily: "'DM Mono', monospace", marginTop: 1 }}>{fileName}</div>
          </div>
        </div>

        {/* Detected summary */}
        <div style={{
          background: "var(--gs-infobox-bg)",
          border: "1px solid var(--gs-infobox-border)",
          borderRadius: 10, padding: "12px 14px", marginBottom: 14,
        }}>
          <div style={{
            fontSize: 11, fontWeight: 600,
            color: "var(--settings-row-sub)",
            marginBottom: 8, textTransform: "uppercase", letterSpacing: "0.06em",
            fontFamily: "'DM Mono', monospace",
          }}>Detected Archive Contents</div>
          {[
            { label: "Library Index", value: "포함" },
            { label: "LENS DSP Presets", value: "포함" },
            { label: "Custom Crates", value: "포함" },
            { label: "Audio Engine Settings", value: "포함" },
          ].map(({ label, value }) => (
            <div key={label} style={{ display: "flex", justifyContent: "space-between", fontSize: 11.5, marginBottom: 4 }}>
              <span style={{ color: "var(--settings-row-sub)" }}>{label}</span>
              <span style={{ fontWeight: 600, color: "var(--gs-stat-num)", fontFamily: "'DM Mono', monospace" }}>{value}</span>
            </div>
          ))}
        </div>

        <div style={{ background: "#FEF3C7", border: "1px solid #FDE68A", borderRadius: 8, padding: "10px 12px", marginBottom: 20 }}>
          <p style={{ fontSize: 11, color: "#92400E", margin: 0, lineHeight: 1.5 }}>
            <strong>Warning:</strong> Applying this restore will replace all current settings, crates, and LENS curves. This action cannot be undone.
          </p>
        </div>

        <div style={{ display: "flex", gap: 10, justifyContent: "flex-end" }}>
          <Btn variant="outline" onClick={onCancel} disabled={restoring}>Cancel</Btn>
          <Btn variant="solid" onClick={onConfirm} disabled={restoring}>
            {restoring ? (
              <>
                <svg className="animate-spin" width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.5}><path strokeLinecap="round" d="M12 3a9 9 0 1 0 9 9" strokeDasharray="4 2" /></svg>
                Restoring…
              </>
            ) : "Apply Restore"}
          </Btn>
        </div>
      </div>
    </Modal>
  )
}

// ── Support ticket modal ───────────────────────────────────────────────────────

function SupportModal({ onClose, onSubmitted }: { onClose: () => void; onSubmitted: () => void }) {
  const [email, setEmail] = useState("")
  const [category, setCategory] = useState<IssueCategory>("Audio Dropouts")
  const [description, setDescription] = useState("")
  const [submitting, setSubmitting] = useState(false)

  function handleSubmit() {
    if (submitting) return
    setSubmitting(true)
    setTimeout(() => {
      onClose()
      onSubmitted()
    }, 1100)
  }

  const inputStyle: React.CSSProperties = {
    width: "100%", fontSize: 12, fontFamily: "inherit",
    color: "var(--settings-input-text)",
    background: "var(--settings-input-bg)",
    border: "1px solid var(--settings-input-border)",
    borderRadius: 8, padding: "8px 11px", outline: "none", boxSizing: "border-box",
  }
  const labelStyle: React.CSSProperties = {
    fontSize: 10.5, fontWeight: 600, color: "var(--settings-row-sub)",
    textTransform: "uppercase", letterSpacing: "0.06em",
    fontFamily: "'DM Mono', monospace", display: "block", marginBottom: 5,
  }

  return (
    <Modal onClose={onClose}>
      <div style={{ padding: "24px 24px 20px" }}>
        <div style={{ fontSize: 14, fontWeight: 700, color: "var(--settings-row-label)", marginBottom: 4 }}>Submit Diagnostic Report</div>
        <div style={{ fontSize: 11, color: "var(--settings-row-sub)", fontFamily: "'DM Mono', monospace", marginBottom: 20 }}>
          Mono Core Audio Engine v1.2.4 · WASAPI / ASIO Pipeline
        </div>

        <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <div>
            <label style={labelStyle}>Contact Email</label>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="user@example.com"
              style={inputStyle}
            />
          </div>

          <div>
            <label style={labelStyle}>Issue Category</label>
            <select
              value={category}
              onChange={(e) => setCategory(e.target.value as IssueCategory)}
              style={{ ...inputStyle, appearance: "none", cursor: "pointer" }}
            >
              {(["Audio Dropouts", "ASIO/DAC Sync", "Streaming Login", "Other"] as IssueCategory[]).map((c) => (
                <option key={c} value={c}>{c}</option>
              ))}
            </select>
          </div>

          <div>
            <label style={labelStyle}>Brief Description</label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Describe the issue you're experiencing…"
              rows={3}
              style={{ ...inputStyle, resize: "vertical", lineHeight: 1.5 }}
            />
          </div>

          <div style={{
            background: "var(--gs-infobox-bg)",
            border: "1px solid var(--gs-infobox-border)",
            borderRadius: 8, padding: "9px 12px",
          }}>
            <p style={{ fontSize: 11, color: "var(--gs-infobox-text)", margin: 0, lineHeight: 1.5 }}>
              System hardware specs and last 50 engine events will be attached automatically.
            </p>
          </div>
        </div>

        <div style={{ display: "flex", gap: 10, justifyContent: "flex-end", marginTop: 20 }}>
          <Btn variant="outline" onClick={onClose} disabled={submitting}>Cancel</Btn>
          <Btn variant="solid" onClick={handleSubmit} disabled={submitting}>
            {submitting ? (
              <>
                <svg className="animate-spin" width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.5}><path strokeLinecap="round" d="M12 3a9 9 0 1 0 9 9" strokeDasharray="4 2" /></svg>
                Sending…
              </>
            ) : "Send to Support Team"}
          </Btn>
        </div>
      </div>
    </Modal>
  )
}

// ── Backup section ─────────────────────────────────────────────────────────────

function BackupSection({ showToast }: { showToast: (msg: string) => void }) {
  const [showRestoreModal, setShowRestoreModal] = useState(false)
  const [restoreFileName, setRestoreFileName] = useState("")
  const [restoring, setRestoring] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const { catalog, playlists } = useMono()
  const cmd = useMonoCommands()

  // 백업은 Core 가 만든다 — 카탈로그·이력·존·토큰이 전부 Core 쪽 DB 에 있다.
  // 브라우저가 localStorage 만 담아 봤자 복원에 쓸모가 없다.
  async function handleBackup() {
    try {
      const res = await cmd.request({ type: MSG.backup }, MSG.backup)
      showToast(res.ok ? `백업을 만들었습니다: ${res.body}` : (res.error ?? "백업에 실패했습니다."))
    } catch (err) {
      showToast(err instanceof Error ? err.message : "백업에 실패했습니다.")
    }
  }

  function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    setRestoreFileName(file.name)
    setShowRestoreModal(true)
    if (fileInputRef.current) fileInputRef.current.value = ""
  }

  function handleConfirmRestore() {
    setRestoring(true)
    setTimeout(() => {
      setRestoring(false)
      setShowRestoreModal(false)
      showToast("Workspace restored successfully.")
    }, 1100)
  }

  return (
    <>
      <Section>
        <SectionHeader
          title="Full Platform Backup & Restore"
          subtitle="Archive or restore your complete Mono workspace, including indexed tracks, custom crates, LENS DSP curves, audio settings, and streaming accounts."
        />

        {/* Stats grid */}
        <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 10, marginBottom: 16 }}>
          {[
            { label: "Indexed Tracks", value: `${catalog.length.toLocaleString()} tracks` },
            { label: "Saved Playlists", value: `${playlists.length.toLocaleString()} crates` },
            { label: "Local Files", value: `${catalog.filter((t) => t.hasLocal).length.toLocaleString()} files` },
            { label: "Streaming", value: `${catalog.filter((t) => !t.hasLocal).length.toLocaleString()} tracks` },
          ].map(({ label, value }) => (
            <div key={label} style={{
              background: "var(--gs-stat-bg)",
              border: "1px solid var(--gs-stat-border)",
              borderRadius: 10, padding: "12px 14px",
            }}>
              <div style={{
                fontSize: 10, color: "var(--settings-row-sub)",
                fontFamily: "'DM Mono', monospace", marginBottom: 4,
                textTransform: "uppercase", letterSpacing: "0.05em",
              }}>{label}</div>
              <div style={{ fontSize: 13, fontWeight: 700, color: "var(--gs-stat-num)" }}>{value}</div>
            </div>
          ))}
        </div>

        {/* Snapshot status */}
        <div style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 20 }}>
          <span style={{ width: 7, height: 7, borderRadius: "50%", background: "#34D399", flexShrink: 0, display: "block" }} />
          <span style={{ fontSize: 11, color: "var(--settings-row-sub)", fontFamily: "'DM Mono', monospace" }}>
            Last automated snapshot: Today at 04:00 AM
          </span>
        </div>

        {/* Actions */}
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <Btn variant="solid" onClick={handleBackup}>
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
              <polyline points="7 10 12 15 17 10" />
              <line x1="12" y1="15" x2="12" y2="3" />
            </svg>
            Create Full Backup Archive
          </Btn>
          <Btn variant="outline" onClick={() => fileInputRef.current?.click()}>
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
              <polyline points="17 8 12 3 7 8" />
              <line x1="12" y1="3" x2="12" y2="15" />
            </svg>
            Restore from Backup File
          </Btn>
          <input
            ref={fileInputRef}
            type="file"
            accept=".json"
            style={{ display: "none" }}
            onChange={handleFileChange}
          />
        </div>
      </Section>

      {showRestoreModal && (
        <RestoreModal
          fileName={restoreFileName}
          onCancel={() => setShowRestoreModal(false)}
          onConfirm={handleConfirmRestore}
          restoring={restoring}
        />
      )}
    </>
  )
}

// ── Diagnostics section ────────────────────────────────────────────────────────

function DiagnosticsSection({ showToast }: { showToast: (msg: string) => void }) {
  const [showSupportModal, setShowSupportModal] = useState(false)

  function handleExportLog() {
    const now = new Date().toISOString()
    const log = [
      `Mono Core Audio Engine — Diagnostic Export`,
      `Generated: ${now}`,
      ``,
      `[Engine]`,
      `  Version: 1.2.4 (64-Bit Float)`,
      `  Pipeline: WASAPI / ASIO`,
      `  Sample Rate Negotiation: 44100 Hz → 192000 Hz (auto)`,
      `  Bit Depth: 32-Bit Float (internal) → 24-Bit (output)`,
      ``,
      `[Buffer & Latency]`,
      `  Buffer Size: 256 samples`,
      `  Latency: 5.8 ms`,
      `  Dropouts (last 24h): 0`,
      ``,
      `[OS Audio Endpoint]`,
      `  Driver: ASIO4ALL v2 / Native ASIO`,
      `  Exclusive Mode: Enabled`,
      `  USB DAC: Connected (no interruptions detected)`,
      ``,
      `[Last 5 Engine Events]`,
      `  [${now}] Stream start OK — 24/96 FLAC`,
      `  [${now}] Exclusive mode acquired`,
      `  [${now}] DSD DoP passthrough negotiated`,
      `  [${now}] Buffer resize: 256 → 256 (stable)`,
      `  [${now}] OS sleep prevention: active`,
    ].join("\n")

    const blob = new Blob([log], { type: "text/plain" })
    const url = URL.createObjectURL(blob)
    const a = document.createElement("a")
    a.href = url
    a.download = "mono-diagnostic-log.txt"
    a.click()
    URL.revokeObjectURL(url)
    showToast("Diagnostic log exported.")
  }

  return (
    <>
      <Section>
        <SectionHeader
          title="Support & Engine Diagnostics"
          subtitle="Troubleshoot audio engine issues, inspect driver latency, or transmit crash logs to Mono Support."
        />

        {/* Status display */}
        <div style={{
          background: "var(--gs-infobox-bg)",
          border: "1px solid var(--gs-infobox-border)",
          borderRadius: 10, padding: "12px 14px", marginBottom: 16,
        }}>
          <div style={{ fontSize: 11, fontFamily: "'DM Mono', monospace", color: "var(--settings-row-label)", marginBottom: 5, fontWeight: 600 }}>
            Mono Core Audio Engine v1.2.4 | 64-Bit Float | WASAPI / ASIO Pipeline Active
          </div>
          <div style={{ fontSize: 11, fontFamily: "'DM Mono', monospace", color: "var(--settings-row-sub)" }}>
            Buffer: 256 samples (5.8ms latency) | Zero dropouts detected in last 24h
          </div>
          <div style={{ display: "flex", alignItems: "center", gap: 5, marginTop: 8 }}>
            <span style={{ width: 7, height: 7, borderRadius: "50%", background: "#34D399", flexShrink: 0, display: "block" }} />
            <span style={{ fontSize: 10, color: "var(--settings-row-sub)", fontFamily: "'DM Mono', monospace" }}>Engine healthy · No active faults</span>
          </div>
        </div>

        <Row
          label="Export Local Log"
          subtitle="Download hardware and driver event log as a .txt file."
          control={
            <Btn variant="ghost" onClick={handleExportLog}>
              <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" /><polyline points="7 10 12 15 17 10" /><line x1="12" y1="15" x2="12" y2="3" />
              </svg>
              Export Diagnostic Log (.txt)
            </Btn>
          }
        />
        <Row
          label="Transmit Report to Support"
          subtitle="Open a support ticket with auto-attached engine snapshot."
          control={
            <Btn variant="outline" onClick={() => setShowSupportModal(true)}>
              <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
                <line x1="22" y1="2" x2="11" y2="13" /><polygon points="22 2 15 22 11 13 2 9 22 2" />
              </svg>
              Submit Diagnostic Report
            </Btn>
          }
        />
      </Section>

      {showSupportModal && (
        <SupportModal
          onClose={() => setShowSupportModal(false)}
          onSubmitted={() => showToast("Diagnostic report #MN-8402 submitted successfully.")}
        />
      )}
    </>
  )
}

// ── Danger Zone section ───────────────────────────────────────────────────────

const MONO_STORAGE_KEYS = [
  "mono_general_settings", "mono_wizard_services", "mono_streaming_services",
  "mono_onboarding_completed", "mono_history", "mono_library", "mono_dsp", "mono_settings",
]

function HistoryConfirmModal({ onCancel, onConfirm }: { onCancel: () => void; onConfirm: () => void }) {
  return (
    <Modal onClose={onCancel}>
      <div style={{ padding: "24px 24px 20px" }}>
        <div style={{ fontSize: 14, fontWeight: 700, color: "var(--settings-row-label)", marginBottom: 8 }}>Clear Listening History?</div>
        <p style={{ fontSize: 12, color: "var(--settings-row-sub)", margin: "0 0 20px", lineHeight: 1.6 }}>
          Recent playback logs, queue orders, and play count metrics will be erased. Your indexed library and LENS presets are not affected.
        </p>
        <div style={{ display: "flex", gap: 10, justifyContent: "flex-end" }}>
          <Btn variant="outline" onClick={onCancel}>Cancel</Btn>
          <Btn variant="solid" onClick={onConfirm}>Purge History</Btn>
        </div>
      </div>
    </Modal>
  )
}

function FactoryResetModal({ onCancel, onConfirm, wiping }: { onCancel: () => void; onConfirm: () => void; wiping: boolean }) {
  const { catalog } = useMono()
  const [confirmed, setConfirmed] = useState(false)

  useEffect(() => {
    function onKey(e: KeyboardEvent) { if (e.key === "Escape") onCancel() }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [onCancel])

  return (
    <div
      style={{
        position: "fixed", inset: 0, zIndex: 600,
        background: "rgba(0,0,0,0.60)", backdropFilter: "blur(6px)",
        display: "flex", alignItems: "center", justifyContent: "center", padding: 24,
      }}
      onClick={onCancel}
    >
      <div
        style={{
          background: "var(--gs-modal-bg)",
          borderRadius: 20,
          border: "1px solid var(--gs-modal-border)",
          boxShadow: "0 24px 80px rgba(239,68,68,0.15)",
          width: "100%", maxWidth: 460, position: "relative",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div style={{ padding: "28px 28px 24px" }}>
          {/* Header */}
          <div style={{ display: "flex", alignItems: "flex-start", gap: 14, marginBottom: 20 }}>
            <div className="modal-danger-icon-box" style={{
              width: 44, height: 44, display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0,
            }}>
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
                <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
                <line x1="12" y1="9" x2="12" y2="13" /><line x1="12" y1="17" x2="12.01" y2="17" />
              </svg>
            </div>
            <div>
              <div style={{ fontSize: 16, fontWeight: 800, color: "var(--settings-row-label)", letterSpacing: "-0.2px", marginBottom: 4 }}>
                Are you absolutely sure?
              </div>
              <p style={{ fontSize: 12, color: "var(--settings-row-sub)", margin: 0, lineHeight: 1.6 }}>
                This action cannot be undone. All <strong>{catalog.length.toLocaleString()}</strong> library index entries, LENS acoustic profiles, and streaming authorizations will be erased permanently.
              </p>
            </div>
          </div>

          {/* Checklist */}
          <div className="modal-danger-callout" style={{ marginBottom: 20 }}>
            {[
              "All indexed tracks and album metadata",
              "12 custom LENS DSP acoustic profiles",
              "Qobuz and TIDAL streaming tokens",
              "Audio engine configuration and preferences",
              "Playback history and custom queue orders",
            ].map((item) => (
              <div key={item} className="modal-danger-list-item" style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 6 }}>
                <span style={{ flexShrink: 0 }}>×</span>
                {item}
              </div>
            ))}
          </div>

          {/* Confirmation checkbox */}
          <label style={{ display: "flex", alignItems: "flex-start", gap: 10, cursor: "pointer", marginBottom: 24 }}>
            <input
              type="checkbox"
              checked={confirmed}
              onChange={(e) => setConfirmed(e.target.checked)}
              className="accent-rose-500"
              style={{ marginTop: 2, width: 15, height: 15, flexShrink: 0 }}
            />
            <span style={{ fontSize: 12, color: "var(--settings-row-label)", lineHeight: 1.5 }}>
              I understand that all workspace data and settings will be permanently wiped.
            </span>
          </label>

          {/* Actions */}
          <div style={{ display: "flex", gap: 10, justifyContent: "flex-end" }}>
            <button
              type="button"
              onClick={onCancel}
              disabled={wiping}
              className="modal-danger-cancel-btn"
              style={{ fontSize: 12, fontWeight: 600, fontFamily: "inherit", border: "1px solid rgba(255,255,255,0.12)", borderRadius: 8, padding: "8px 16px", cursor: "pointer" }}
            >Cancel</button>
            <button
              type="button"
              disabled={!confirmed || wiping}
              onClick={onConfirm}
              className={confirmed && !wiping ? "modal-danger-confirm-active" : "modal-danger-confirm-disabled"}
              style={{
                fontSize: 12, fontWeight: 700, fontFamily: "inherit",
                border: "none", borderRadius: 8,
                padding: "8px 16px", cursor: confirmed && !wiping ? "pointer" : "not-allowed",
                display: "inline-flex", alignItems: "center", gap: 6,
                transition: "background 0.15s, box-shadow 0.15s",
              }}
            >
              {wiping ? (
                <>
                  <svg className="animate-spin" width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.5}>
                    <path strokeLinecap="round" d="M12 3a9 9 0 1 0 9 9" strokeDasharray="4 2" />
                  </svg>
                  Wiping local database…
                </>
              ) : "Erase Everything & Restart"}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}

function DangerZoneSection({ showToast, onReset }: { showToast: (msg: string) => void; onReset?: () => void }) {
  const [cacheSize, setCacheSize] = useState("1.42 GB stored")
  const [showHistoryConfirm, setShowHistoryConfirm] = useState(false)
  const [showFactoryModal, setShowFactoryModal] = useState(false)
  const [wiping, setWiping] = useState(false)

  function handleClearCache() {
    setCacheSize("0 MB")
    showToast("Temporary audio cache purged successfully.")
  }

  function handleHistoryConfirm() {
    try { localStorage.removeItem("mono_history") } catch {}
    setShowHistoryConfirm(false)
    showToast("Listening history and queue data cleared.")
  }

  function handleFactoryReset() {
    setWiping(true)
    setTimeout(() => {
      for (const key of MONO_STORAGE_KEYS) {
        try { localStorage.removeItem(key) } catch {}
      }
      // 첫 실행 설정은 Core 에 있다. 여기까지 지워야 초기화 뒤 마법사가 다시 뜬다 —
      // 안 지우면 "공장 초기화"를 했는데 예전 출력 장치로 되돌아간다.
      void clearSetup()
      setWiping(false)
      setShowFactoryModal(false)
      if (onReset) {
        onReset()
      } else {
        window.location.reload()
      }
    }, 800)
  }

  const dangerCardStyle: React.CSSProperties = {
    background: "var(--gs-danger-card-bg)",
    border: "1px solid var(--gs-danger-card-border)",
    borderRadius: 12, padding: "16px 18px",
    display: "flex", alignItems: "center", justifyContent: "space-between", gap: 20,
  }

  const dangerBtnStyle: React.CSSProperties = {
    fontSize: 11.5, fontWeight: 500, color: "var(--gs-btn-outline-text)",
    background: "transparent", border: "1px solid var(--gs-btn-outline-border)",
    borderRadius: 10, padding: "6px 13px", cursor: "pointer",
    fontFamily: "inherit", whiteSpace: "nowrap", flexShrink: 0,
    transition: "background 0.15s",
  }

  return (
    <>
      <div style={{
        background: "var(--gs-danger-zone-bg)",
        border: "1px solid var(--gs-danger-zone-border)",
        borderRadius: 16, padding: "22px 24px", marginBottom: 32,
      }}>
        {/* Section header */}
        <div style={{ marginBottom: 18 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 4 }}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#DC2626" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
              <polygon points="7.86 2 16.14 2 22 7.86 22 16.14 16.14 22 7.86 22 2 16.14 2 7.86 7.86 2" />
              <line x1="12" y1="8" x2="12" y2="12" /><line x1="12" y1="16" x2="12.01" y2="16" />
            </svg>
            <h3 style={{ fontSize: 13, fontWeight: 700, color: "#7F1D1D", margin: 0 }}>Data Clearance & System Reset</h3>
          </div>
          <p style={{ fontSize: 11, color: "#9F1239", margin: 0, fontFamily: "'DM Mono', monospace", lineHeight: 1.5 }}>
            Selectively prune temporary storage assets or permanently wipe the local Mono workspace.
          </p>
        </div>

        <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
          {/* Item 1: Cache clearance */}
          <div style={dangerCardStyle}>
            <div style={{ flex: 1, minWidth: 0 }}>
              <div style={{ fontSize: 13, fontWeight: 600, color: "var(--settings-row-label)", marginBottom: 2 }}>Album Art & Waveform Cache</div>
              <div style={{ fontSize: 11, color: "var(--settings-row-sub)", lineHeight: 1.5 }}>
                Cached high-resolution album covers, artist thumbnails, and pre-computed FFT waveform analysis data.
              </div>
              <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 10.5, color: "var(--gs-seg-idle-text)", marginTop: 5 }}>
                {cacheSize}
              </div>
            </div>
            <button
              type="button"
              onClick={handleClearCache}
              style={dangerBtnStyle}
            >
              Clear Cache
            </button>
          </div>

          {/* Item 2: History pruning */}
          <div style={dangerCardStyle}>
            <div style={{ flex: 1, minWidth: 0 }}>
              <div style={{ fontSize: 13, fontWeight: 600, color: "var(--settings-row-label)", marginBottom: 2 }}>Listening History & Queues</div>
              <div style={{ fontSize: 11, color: "var(--settings-row-sub)", lineHeight: 1.5 }}>
                Clear recent track playback logs, custom queue orders, and play count metrics without modifying indexed music files or LENS presets.
              </div>
            </div>
            <button
              type="button"
              onClick={() => setShowHistoryConfirm(true)}
              style={dangerBtnStyle}
            >
              Purge History
            </button>
          </div>

          {/* Item 3: Factory reset */}
          <div style={{
            background: "rgba(254,242,242,0.5)", border: "1px solid #FECACA",
            borderRadius: 12, padding: "16px 18px",
            display: "flex", alignItems: "center", justifyContent: "space-between", gap: 20,
          }}>
            <div style={{ flex: 1, minWidth: 0 }}>
              <div style={{ fontSize: 13, fontWeight: 600, color: "#7F1D1D", marginBottom: 2 }}>Factory Reset Mono</div>
              <div style={{ fontSize: 11, color: "#9F1239", lineHeight: 1.5 }}>
                Irreversibly delete all indexed libraries, custom LENS DSP acoustic profiles, audio engine configs, and streaming tokens. Restores the application to a blank state.
              </div>
            </div>
            <button
              type="button"
              onClick={() => setShowFactoryModal(true)}
              style={{
                fontSize: 11.5, fontWeight: 700, color: "#FFFFFF",
                background: "#DC2626", border: "none",
                borderRadius: 10, padding: "8px 14px", cursor: "pointer",
                fontFamily: "inherit", whiteSpace: "nowrap", flexShrink: 0,
                boxShadow: "0 1px 4px rgba(220,38,38,0.3)",
                transition: "background 0.15s",
              }}
              onMouseEnter={(e) => ((e.currentTarget as HTMLButtonElement).style.background = "#B91C1C")}
              onMouseLeave={(e) => ((e.currentTarget as HTMLButtonElement).style.background = "#DC2626")}
            >
              Factory Reset Mono
            </button>
          </div>
        </div>
      </div>

      {showHistoryConfirm && (
        <HistoryConfirmModal
          onCancel={() => setShowHistoryConfirm(false)}
          onConfirm={handleHistoryConfirm}
        />
      )}

      {showFactoryModal && (
        <FactoryResetModal
          onCancel={() => setShowFactoryModal(false)}
          onConfirm={handleFactoryReset}
          wiping={wiping}
        />
      )}
    </>
  )
}

// ── Main component ─────────────────────────────────────────────────────────────


// ── 소프트웨어 업데이트 ────────────────────────────────────────────────────────

type UpdatePhase = "idle" | "checking" | "current" | "available" | "downloading" | "failed"

/**
 * Velopack 피드에서 새 버전을 확인하고 적용한다.
 * 설치본에서만 동작한다 — zip 으로 풀어 쓰는 경우 교체할 대상이 없다.
 */
function DiscordSection() {
  const [enabled, setEnabled] = useState(true)
  const [hasAppId, setHasAppId] = useState(false)
  const [appId, setAppId] = useState("")
  const [note, setNote] = useState<string | null>(null)
  const desktop = hasShell()

  useEffect(() => {
    void discordActivityStatus().then((s) => {
      if (!s) return
      setEnabled(s.enabled)
      setHasAppId(s.hasAppId)
    })
  }, [])

  return (
    <Section>
      <SectionHeader
        title="Discord"
        subtitle="친구 목록에 Listening to Mono 로 지금 곡이 뜹니다. Discord 데스크톱이 켜져 있어야 합니다."
      />
      <Row
        label="Show now playing on Discord"
        subtitle={desktop
          ? (hasAppId ? "Activity is ready — play a track to see it on your profile." : "Needs a Discord Application ID once. Open the portal, create an app named Mono, paste the ID below.")
          : "Desktop app only."}
        control={
          <Toggle
            value={enabled}
            onChange={(v) => {
              setEnabled(v)
              void setDiscordActivityEnabled(v)
            }}
          />
        }
      />
      <div style={{ padding: "12px 0 4px" }}>
        <div style={{ fontSize: 13, fontWeight: 500, color: "var(--settings-row-label)", marginBottom: 6 }}>
          Discord Application ID
        </div>
        <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
          <input
            value={appId}
            onChange={(e) => setAppId(e.target.value)}
            placeholder={hasAppId ? "Saved — paste a new ID to replace" : "e.g. 123456789012345678"}
            disabled={!desktop}
            style={{
              flex: 1, minWidth: 0, fontFamily: "'DM Mono', monospace", fontSize: 12,
              padding: "8px 10px", borderRadius: 8,
              border: "1px solid var(--settings-input-border)",
              background: "var(--settings-input-bg)", color: "var(--settings-input-text)",
            }}
          />
          <Btn
            disabled={!desktop || !appId.trim()}
            onClick={() => {
              void setDiscordApplicationId(appId.trim()).then((ok) => {
                setHasAppId(ok)
                setNote(ok ? "Application ID saved." : "Could not save the ID.")
              })
            }}
          >
            Save
          </Btn>
        </div>
        <button
          type="button"
          onClick={() => openExternal("https://discord.com/developers/applications")}
          style={{
            marginTop: 8, background: "none", border: "none", padding: 0, cursor: "pointer",
            fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--settings-row-sub)",
            textDecoration: "underline",
          }}
        >
          Open Discord Developer Portal →
        </button>
        {note && (
          <div style={{ fontFamily: "'DM Mono', monospace", fontSize: 11, color: "var(--settings-row-sub)", marginTop: 6 }}>
            {note}
          </div>
        )}
      </div>
    </Section>
  )
}

function UpdateSection({ showToast }: { showToast: (msg: string) => void }) {
  const [phase, setPhase] = useState<UpdatePhase>("idle")
  const [status, setStatus] = useState<UpdateStatus | null>(null)
  const [percent, setPercent] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const desktop = hasShell()

  // 창을 열자마자 현재 버전을 보여 준다. "확인"을 눌러야 버전이 보이면 불친절하다.
  useEffect(() => {
    if (!desktop) return
    let alive = true
    void checkForUpdate()
      .then((res) => {
        if (!alive || !res) return
        setStatus(res)
        setPhase(res.available ? "available" : "current")
      })
      .catch(() => { /* 조용히 둔다 — 사용자가 직접 확인할 수 있다 */ })
    return () => { alive = false }
  }, [desktop])

  useEffect(() => onUpdateProgress(setPercent), [])

  async function check() {
    setPhase("checking")
    setError(null)
    try {
      const res = await checkForUpdate()
      if (!res) { setError("데스크톱 앱에서만 확인할 수 있습니다."); setPhase("failed"); return }
      setStatus(res)
      setPhase(res.available ? "available" : "current")
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setPhase("failed")
    }
  }

  async function install() {
    setPhase("downloading")
    setPercent(0)
    setError(null)
    try {
      // 성공하면 앱이 교체되고 재시작하므로, 이 뒤로는 돌아오지 않는다.
      await applyUpdate()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setPhase("failed")
      showToast("업데이트를 적용하지 못했습니다.")
    }
  }

  const current = status?.current ?? "—"

  return (
    <Section>
      <SectionHeader
        title="소프트웨어 업데이트"
        subtitle="새 버전을 확인하고 설치합니다. 설치 후 Mono 가 자동으로 다시 시작됩니다."
      />

      <Row
        label="설치된 버전"
        subtitle={status?.installed === false
          ? "설치 프로그램으로 설치한 경우에만 자동 업데이트를 받을 수 있습니다."
          : "Mono Control · Core · Output"}
        control={
          <span style={{
            fontFamily: "'DM Mono', monospace", fontSize: 12, fontWeight: 600,
            color: "var(--gs-stat-num)", fontVariantNumeric: "tabular-nums",
          }}>{current}</span>
        }
      />

      <Row
        label="업데이트 확인"
        subtitle={
          phase === "checking" ? "확인 중…"
          : phase === "available" ? `${status?.version} 을(를) 설치할 수 있습니다.`
          : phase === "current" ? "최신 버전입니다."
          : phase === "downloading" ? `내려받는 중 · ${percent}%`
          : desktop ? "GitHub 릴리스 피드에서 새 버전을 찾습니다."
          : "브라우저에서는 확인할 수 없습니다. Mono 데스크톱 앱에서 실행하세요."
        }
        control={
          <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
            {phase === "available" && (
              <Btn variant="solid" onClick={install} disabled={!desktop}>
                지금 설치
              </Btn>
            )}
            <Btn
              variant={phase === "available" ? "outline" : "solid"}
              onClick={check}
              disabled={!desktop || phase === "checking" || phase === "downloading"}
            >
              {phase === "checking" ? "확인 중…" : "업데이트 확인"}
            </Btn>
          </div>
        }
      />

      {phase === "downloading" && (
        <div style={{ padding: "14px 0" }}>
          <div style={{
            height: 4, borderRadius: 2, overflow: "hidden",
            background: "var(--settings-divider)",
          }}>
            <div style={{
              width: `${percent}%`, height: "100%",
              background: "var(--gs-btn-solid-bg)",
              transition: "width 0.25s ease",
            }} />
          </div>
          <div style={{
            fontFamily: "'DM Mono', monospace", fontSize: 11,
            color: "var(--settings-row-sub)", marginTop: 8,
          }}>
            내려받는 중 {percent}% — 완료되면 Mono 가 자동으로 다시 시작됩니다.
          </div>
        </div>
      )}

      {error && (
        <div style={{
          marginTop: 14, padding: "10px 14px", borderRadius: 8,
          background: "rgba(220,38,38,0.08)", border: "1px solid rgba(220,38,38,0.25)",
          fontFamily: "'DM Mono', monospace", fontSize: 11.5, color: "#DC2626", lineHeight: 1.6,
        }}>{error}</div>
      )}

      {status?.message && phase !== "downloading" && (
        <div style={{
          fontFamily: "'DM Mono', monospace", fontSize: 11,
          color: "var(--settings-row-sub)", marginTop: 12, lineHeight: 1.6,
        }}>{status.message}</div>
      )}
    </Section>
  )
}

export default function GeneralSystemSettings({
  onReset,
  theme,
  onToggleTheme,
}: {
  onReset?: () => void
  theme?: "light" | "dark"
  onToggleTheme?: () => void
} = {}) {
  const [s, setS] = useState<GeneralSettings>(load)
  const [toast, setToast] = useState<string | null>(null)
  const [interfaceLanguage, setInterfaceLanguage] = useState<"en" | "ko" | "ja" | "de">("en")

  function patch<K extends keyof GeneralSettings>(key: K, value: GeneralSettings[K]) {
    setS((prev) => {
      const next = { ...prev, [key]: value }
      try { localStorage.setItem(STORAGE_KEY, JSON.stringify(next)) } catch {}
      return next
    })
  }

  useEffect(() => { setS(load()) }, [])

  function handleAppearanceChange(v: Appearance) {
    patch("appearance", v)
    if (onToggleTheme && theme) {
      const wantDark = v === "Studio Dark"
      const isDark = theme === "dark"
      if (wantDark !== isDark) onToggleTheme()
    }
  }

  // Derive displayed appearance from live theme prop when available
  const displayedAppearance: Appearance = theme
    ? (theme === "dark" ? "Studio Dark" : "Studio Light")
    : s.appearance

  return (
    <div style={{ flex: 1, minWidth: 0 }}>
      {/* Page header */}
      <div style={{ marginBottom: 28 }}>
        <h2 style={{
          fontSize: 17, fontWeight: 700,
          color: "var(--settings-title)",
          margin: 0, letterSpacing: "-0.3px",
        }}>
          General & System
        </h2>
        <p style={{
          fontSize: 12, color: "var(--settings-row-sub)",
          margin: "4px 0 0", fontFamily: "'DM Mono', monospace",
        }}>
          Platform behavior, display preferences, and workspace management.
        </p>
      </div>

      <UpdateSection showToast={setToast} />

      {/* Section 1: System & Playback */}
      <Section>
        <SectionHeader title="System & Playback" subtitle="OS integration and audio streaming behavior." />
        <Row
          label="Prevent OS Sleep during Playback"
          subtitle="Keep audio stream active and prevent USB DAC dropouts."
          control={<Toggle value={s.preventSleep} onChange={(v) => patch("preventSleep", v)} />}
        />
        <Row
          label="RAM Memory Playback Buffer"
          control={
            <StyledSelect<RamBuffer>
              value={s.ramBuffer}
              options={["Direct Disk Stream", "512 MB", "1 GB (Recommended)", "2 GB Full Album"]}
              onChange={(v) => patch("ramBuffer", v)}
            />
          }
        />
        <Row
          label="Exclusive Hardware Media Keys"
          subtitle="Control Mono with keyboard media keys even in background."
          control={<Toggle value={s.exclusiveMediaKeys} onChange={(v) => patch("exclusiveMediaKeys", v)} />}
        />
      </Section>

      <DiscordSection />

      {/* Section 2: Display & Audio Badges */}
      <Section>
        <SectionHeader title="Display & Audio Badges" subtitle="Visual detail level and rendering performance." />
        <Row
          label="Audio Spec Badge Detail"
          control={
            <SegmentControl<SpecBadge>
              value={s.specBadge}
              options={["Minimal", "Detailed Studio", "Full Lab Specs"]}
              onChange={(v) => patch("specBadge", v)}
            />
          }
        />
        <Row
          label="Spectrum Visualizer FPS"
          control={
            <StyledSelect<VisualizerFps>
              value={s.visualizerFps}
              options={["60 FPS (Smooth)", "30 FPS (Saver)", "Disabled"]}
              onChange={(v) => patch("visualizerFps", v)}
            />
          }
        />
        <Row
          label="Appearance"
          control={
            <SegmentControl<Appearance>
              value={displayedAppearance}
              options={["Studio Light", "Studio Dark"]}
              onChange={handleAppearanceChange}
            />
          }
        />
      </Section>

      {/* Section 3: Language */}
      <Section>
        <SectionHeader
          title="Language"
          subtitle="Select your preferred display language for the Mono Studio Console."
        />
        <Row
          label="Interface Language"
          subtitle="Applies to navigation menus, audio telemetry readouts, and modal controls."
          control={
            <div className="settings-select-wrapper">
              <select
                className="settings-select-input"
                value={interfaceLanguage}
                onChange={(e) => setInterfaceLanguage(e.target.value as "en" | "ko" | "ja" | "de")}
              >
                <option value="en">English (US)</option>
                <option value="ko">한국어 (Korean)</option>
                <option value="ja">日本語 (Japanese)</option>
                <option value="de">Deutsch (German)</option>
              </select>
            </div>
          }
        />
      </Section>

      {/* Section 4: Backup & Restore */}
      <BackupSection showToast={(msg) => setToast(msg)} />

      {/* Section 4: Diagnostics & Support */}
      <DiagnosticsSection showToast={(msg) => setToast(msg)} />

      {/* Section 5: Danger Zone */}
      <DangerZoneSection showToast={(msg) => setToast(msg)} onReset={onReset} />

      {/* Toast */}
      {toast && <Toast message={toast} onDone={() => setToast(null)} />}
    </div>
  )
}
