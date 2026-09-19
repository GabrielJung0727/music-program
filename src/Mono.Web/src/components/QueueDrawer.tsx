import { useState, useEffect } from "react"
import type { QueueTrack } from "../data/types"
import { MonoIcon } from "./icons/MonoIcons"
import { FALLBACK_ART } from "../lib/artwork"

interface Props {
  isOpen: boolean
  onClose: () => void
  currentTrack: QueueTrack | null
  queue: QueueTrack[]
  onRemoveFromQueue: (index: number) => void
  onReorderQueue: (fromIndex: number, toIndex: number) => void
  onClearQueue: () => void
  onFlushSession: () => void
  onPlayTrackNow: (track: QueueTrack, index: number) => void
  isGuest: boolean
  isHost: boolean
  hostName?: string
}

function parseSampleRateKHz(format: string): number | null {
  if (/dsd/i.test(format)) return -1
  const match = format.match(/(\d+(?:\.\d+)?)\s*kHz/i)
  return match ? parseFloat(match[1]) : null
}

function rateLabel(rate: number | null): string {
  if (rate === null) return "?"
  if (rate === -1) return "DSD"
  return `${rate}kHz`
}

function totalRuntime(tracks: QueueTrack[]): string {
  let total = 0
  for (const t of tracks) {
    const parts = t.duration.split(":").map(Number)
    if (parts.length === 2) total += parts[0] * 60 + parts[1]
  }
  const m = Math.floor(total / 60)
  const s = total % 60
  return `${m} min ${String(s).padStart(2, "0")} sec`
}

export default function QueueDrawer({
  isOpen, onClose, currentTrack, queue,
  onRemoveFromQueue, onReorderQueue, onClearQueue, onFlushSession, onPlayTrackNow,
  isGuest, hostName,
}: Props) {
  const [autoPlay, setAutoPlay] = useState(true)
  const [hoveredIdx, setHoveredIdx] = useState<number | null>(null)

  useEffect(() => {
    if (!isOpen) return
    const handler = (e: KeyboardEvent) => { if (e.key === "Escape") onClose() }
    window.addEventListener("keydown", handler)
    return () => window.removeEventListener("keydown", handler)
  }, [isOpen, onClose])

  const canControl = !isGuest
  const title = isGuest ? "Host Setlist" : "Play Queue"
  const allTracks = currentTrack ? [currentTrack, ...queue] : queue
  const runtime = totalRuntime(allTracks)

  return (
    <>
      {/* Backdrop */}
      {isOpen && (
        <div
          className="queue-drawer-backdrop"
          style={{ position: "fixed", inset: 0, zIndex: 49 }}
          onClick={onClose}
        />
      )}

      {/* Drawer panel */}
      <div
        data-queue-drawer
        className="queue-drawer-panel"
        style={{
          position: "fixed", right: 0, top: "4rem", bottom: "5rem",
          width: 384, zIndex: 50,
          display: "flex", flexDirection: "column",
          transform: isOpen ? "translateX(0)" : "translateX(100%)",
          transition: "transform 0.3s cubic-bezier(0.32,0,0.67,0)",
        }}
      >
        {/* ── Header ──────────────────────────────────────────────────────── */}
        <div className="queue-drawer-header" style={{
          padding: "16px 20px",
          flexShrink: 0,
          display: "flex", alignItems: "center", justifyContent: "space-between",
        }}>
          {/* Left: title + inline status */}
          <div style={{ display: "flex", alignItems: "center", gap: 0 }}>
            <span className="queue-drawer-title" style={{ letterSpacing: "-0.2px" }}>{title}</span>
            {isGuest ? (
              hostName && (
                <span style={{
                  fontFamily: "'DM Mono', monospace", fontSize: 10, color: "#71717A",
                  background: "rgba(128,128,128,0.1)", border: "1px solid rgba(128,128,128,0.2)",
                  borderRadius: 4, padding: "2px 7px", marginLeft: 8,
                }}>
                  by {hostName}
                </span>
              )
            ) : (
              <span className="queue-status-dot" style={{
                fontFamily: "'DM Mono', monospace", fontSize: 10,
                marginLeft: 8, display: "flex", alignItems: "center", gap: 4,
              }}>
                <span className="queue-engine-dot" style={{ width: 5, height: 5, borderRadius: "50%", display: "inline-block", flexShrink: 0 }} />
                Bit-Perfect
              </span>
            )}
          </div>

          {/* Right: flush + close */}
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            {canControl && (currentTrack !== null || queue.length > 0) && (
              <button
                onClick={onFlushSession}
                className="btn-queue-flush"
                style={{
                  display: "flex", alignItems: "center", gap: 5,
                  fontFamily: "'DM Mono', monospace",
                  cursor: "pointer", whiteSpace: "nowrap",
                  transition: "all 0.15s",
                }}
                title="Stop playback and clear all queue items"
              >
                <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
                  <polyline points="3 6 5 6 21 6" />
                  <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
                  <path d="M10 11v6M14 11v6" />
                  <path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
                </svg>
                Flush Session
              </button>
            )}
            <button
              onClick={onClose}
              className="queue-close-btn"
              style={{ background: "none", border: "none", cursor: "pointer", lineHeight: 1, padding: 4, transition: "color 0.15s", display: "flex", alignItems: "center", justifyContent: "center" }}
              aria-label="Close"
            >
              <MonoIcon.Close size={15} />
            </button>
          </div>
        </div>

        {/* ── Scrollable body ──────────────────────────────────────────────── */}
        <div style={{ flex: 1, overflowY: "auto", overflowX: "hidden" }}>

          {/* Now Playing */}
          {currentTrack && (
            <div style={{ padding: "16px 20px 10px" }}>
              <div style={{
                fontSize: 10, fontWeight: 700, color: "#A1A1AA",
                fontFamily: "'DM Mono', monospace", letterSpacing: "0.08em",
                textTransform: "uppercase", marginBottom: 8,
              }}>
                Now Playing
              </div>

              <div className="queue-now-playing-card" style={{
                borderRadius: 12, padding: "12px 13px",
                display: "flex", alignItems: "center", gap: 12,
              }}>
                {/* Art */}
                <div style={{
                  width: 46, height: 46, borderRadius: 8, overflow: "hidden",
                  flexShrink: 0, border: "1px solid rgba(0,0,0,0.08)",
                }}>
                  <img src={currentTrack.art || (currentTrack as any).coverUrl || FALLBACK_ART} alt={currentTrack.album} style={{ width: "100%", height: "100%", objectFit: "cover" }} />
                </div>

                <div style={{ flex: 1, minWidth: 0 }}>
                  <div className="queue-np-title" style={{ fontSize: 13, fontWeight: 600, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", marginBottom: 2 }}>
                    {currentTrack.title}
                  </div>
                  <div className="queue-np-artist" style={{ fontSize: 11, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", marginBottom: 6 }}>
                    {currentTrack.artist}
                  </div>
                  <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                    <span className="queue-np-badge" style={{
                      fontFamily: "'DM Mono', monospace", fontSize: 10, fontWeight: 500,
                      borderRadius: 4, padding: "2px 7px",
                    }}>
                      {currentTrack.format}
                    </span>
                    {currentTrack.dr && (
                      <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, color: "#A1A1AA" }}>
                        {currentTrack.dr}
                      </span>
                    )}
                  </div>
                </div>
              </div>

              {/* RAM buffer gauge */}
              <div style={{ marginTop: 8 }}>
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 4 }}>
                  <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, color: "#A1A1AA" }}>RAM Buffer</span>
                  <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, color: "#71717A" }}>100% Cached</span>
                </div>
                <div style={{ height: 1.5, background: "#E4E4E7", borderRadius: 2, overflow: "hidden" }}>
                  <div style={{ height: "100%", width: "100%", background: "#3F3F46", borderRadius: 2 }} />
                </div>
              </div>
            </div>
          )}

          {/* Divider */}
          {currentTrack && queue.length > 0 && (
            <div style={{ height: 1, background: "#F4F4F5", margin: "4px 16px 0" }} />
          )}

          {/* Up Next */}
          {queue.length > 0 && (
            <div style={{ padding: "12px 16px 8px" }}>
              <div style={{
                fontSize: 10, fontWeight: 700, color: "#A1A1AA",
                fontFamily: "'DM Mono', monospace", letterSpacing: "0.08em",
                textTransform: "uppercase", marginBottom: 6,
              }}>
                Up Next · {queue.length} track{queue.length !== 1 ? "s" : ""}
              </div>

              {queue.map((track, idx) => {
                const prevFormat = idx === 0 ? currentTrack?.format : queue[idx - 1]?.format
                const prevRate = prevFormat ? parseSampleRateKHz(prevFormat) : null
                const thisRate = parseSampleRateKHz(track.format)
                const showDacWarning = prevRate !== null && thisRate !== null && prevRate !== thisRate

                return (
                  <div key={track.id}>
                    {/* DAC clock-shift divider */}
                    {showDacWarning && (
                      <div style={{
                        borderTop: "1px dashed #E4E4E7",
                        margin: "6px 0",
                        paddingTop: 6,
                        textAlign: "center",
                        fontFamily: "'DM Mono', monospace", fontSize: 10,
                        color: "#A1A1AA", letterSpacing: "0.05em",
                      }}>
                        DAC CLOCK: {rateLabel(prevRate)} → {rateLabel(thisRate)}
                      </div>
                    )}

                    {/* Track row */}
                    <div
                      data-queue-track
                      onMouseEnter={() => setHoveredIdx(idx)}
                      onMouseLeave={() => setHoveredIdx(null)}
                      style={{
                        display: "flex", alignItems: "center", gap: 10,
                        padding: "7px 8px", borderRadius: 8,
                        background: "transparent",
                        transition: "background 0.12s",
                        cursor: "default",
                      }}
                    >
                      {/* Index / play button */}
                      <div style={{ width: 18, flexShrink: 0, textAlign: "center" }}>
                        {canControl && hoveredIdx === idx ? (
                          <button
                            onClick={() => onPlayTrackNow(track, idx)}
                            style={{ background: "none", border: "none", cursor: "pointer", padding: 0, color: "#18181B", lineHeight: 1 }}
                            title="Play now"
                          >
                            <svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor">
                              <polygon points="5,3 19,12 5,21" />
                            </svg>
                          </button>
                        ) : (
                          <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, color: "#D4D4D8" }}>
                            {idx + 1}
                          </span>
                        )}
                      </div>

                      {/* Album art */}
                      <div style={{
                        width: 34, height: 34, borderRadius: 6, overflow: "hidden",
                        background: "#F4F4F5", flexShrink: 0,
                        border: "1px solid #E4E4E7",
                      }}>
                        <img src={track.art || (track as any).coverUrl || FALLBACK_ART} alt={track.album} style={{ width: "100%", height: "100%", objectFit: "cover" }} />
                      </div>

                      {/* Meta */}
                      <div style={{ flex: 1, minWidth: 0 }}>
                        <div className="queue-track-title track-title" style={{ fontSize: 12, fontWeight: 500, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", marginBottom: 1 }}>
                          {track.title}
                        </div>
                        <p className="track-artist" style={{ fontSize: 11, color: "#A1A1AA", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", margin: 0 }}>
                          {track.artist}
                        </p>
                      </div>

                      {/* Duration */}
                      <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 10.5, color: "#A1A1AA", flexShrink: 0 }}>
                        {track.duration}
                      </span>

                      {/* Format badge — shown when not hovering with controls */}
                      {!(canControl && hoveredIdx === idx) && (
                        <span className="queue-service-badge" style={{
                          fontFamily: "'DM Mono', monospace", fontSize: 9.5, fontWeight: 500,
                          borderRadius: 4, padding: "2px 6px", flexShrink: 0, whiteSpace: "nowrap",
                          textTransform: "uppercase", letterSpacing: "0.05em",
                        }}>
                          {track.source === "tidal" ? "TIDAL" : track.source === "local" ? "Local" : "Qobuz"}
                        </span>
                      )}

                      {/* Solo/Host reorder + remove controls */}
                      {canControl && hoveredIdx === idx && (
                        <div style={{ display: "flex", alignItems: "center", gap: 1, flexShrink: 0 }}>
                          <button
                            onClick={() => idx > 0 && onReorderQueue(idx, idx - 1)}
                            disabled={idx === 0}
                            title="Move up"
                            style={{
                              background: "none", border: "none", padding: 3, lineHeight: 1,
                              cursor: idx === 0 ? "not-allowed" : "pointer",
                              color: idx === 0 ? "#E4E4E7" : "#A1A1AA",
                              transition: "color 0.12s",
                            }}
                            onMouseEnter={(e) => { if (idx > 0) (e.currentTarget as HTMLButtonElement).style.color = "#18181B" }}
                            onMouseLeave={(e) => { if (idx > 0) (e.currentTarget as HTMLButtonElement).style.color = "#A1A1AA" }}
                          >
                            <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                              <polyline points="18 15 12 9 6 15" />
                            </svg>
                          </button>
                          <button
                            onClick={() => idx < queue.length - 1 && onReorderQueue(idx, idx + 1)}
                            disabled={idx === queue.length - 1}
                            title="Move down"
                            style={{
                              background: "none", border: "none", padding: 3, lineHeight: 1,
                              cursor: idx === queue.length - 1 ? "not-allowed" : "pointer",
                              color: idx === queue.length - 1 ? "#E4E4E7" : "#A1A1AA",
                              transition: "color 0.12s",
                            }}
                            onMouseEnter={(e) => { if (idx < queue.length - 1) (e.currentTarget as HTMLButtonElement).style.color = "#18181B" }}
                            onMouseLeave={(e) => { if (idx < queue.length - 1) (e.currentTarget as HTMLButtonElement).style.color = "#A1A1AA" }}
                          >
                            <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                              <polyline points="6 9 12 15 18 9" />
                            </svg>
                          </button>
                          <button
                            onClick={() => onRemoveFromQueue(idx)}
                            title="Remove"
                            style={{
                              background: "none", border: "none", padding: 3, lineHeight: 1,
                              cursor: "pointer", color: "#A1A1AA", marginLeft: 2,
                              transition: "color 0.12s",
                            }}
                            onMouseEnter={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "#EF4444")}
                            onMouseLeave={(e) => ((e.currentTarget as HTMLButtonElement).style.color = "#A1A1AA")}
                          >
                            <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                              <line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" />
                            </svg>
                          </button>
                        </div>
                      )}

                      {/* Guest hover label */}
                      {isGuest && hoveredIdx === idx && hostName && (
                        <span style={{ fontFamily: "'DM Mono', monospace", fontSize: 9, color: "#A1A1AA", flexShrink: 0, whiteSpace: "nowrap" }}>
                          by {hostName}
                        </span>
                      )}
                    </div>
                  </div>
                )
              })}
            </div>
          )}

          {/* Sparse queue placeholder */}
          {queue.length === 0 && currentTrack && (
            <div style={{ padding: "28px 20px 16px", textAlign: "center" }}>
              <div style={{
                fontFamily: "'DM Mono', monospace", fontSize: 11,
                color: "#52525b", letterSpacing: "0.04em",
                userSelect: "none",
              }}>
                Empty queue · Drag tracks or select from Repertoire
              </div>
            </div>
          )}

          {/* Empty / idle state */}
          {queue.length === 0 && !currentTrack && (
            <div style={{ padding: "60px 20px 48px", textAlign: "center" }}>
              <div style={{ marginBottom: 16, display: "flex", justifyContent: "center" }}>
                <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#D4D4D8" strokeWidth="1.2" strokeLinecap="round" strokeLinejoin="round">
                  <circle cx="12" cy="12" r="10" />
                  <circle cx="12" cy="12" r="3" />
                  <line x1="12" y1="2" x2="12" y2="5" />
                  <line x1="12" y1="19" x2="12" y2="22" />
                  <line x1="2" y1="12" x2="5" y2="12" />
                  <line x1="19" y1="12" x2="22" y2="12" />
                </svg>
              </div>
              <div style={{
                fontFamily: "'DM Mono', monospace", fontSize: 11,
                letterSpacing: "0.08em", textTransform: "uppercase",
                color: "#A1A1AA", marginBottom: 6,
              }}>
                Queue Flushed · Engine Idle
              </div>
              <div style={{ fontSize: 12, color: "#71717A", lineHeight: 1.5 }}>
                Select a master record or playlist<br />to begin a new session.
              </div>
            </div>
          )}
        </div>

        {/* ── Footer ──────────────────────────────────────────────────────── */}
        <div className="queue-autoplay-footer" style={{ padding: "14px 16px 18px", flexShrink: 0 }}>
          {/* Summary */}
          <div style={{
            fontFamily: "'DM Mono', monospace", fontSize: 10.5, color: "#71717A",
            marginBottom: 10,
          }}>
            {allTracks.length} track{allTracks.length !== 1 ? "s" : ""} · {runtime}
          </div>

          {/* Smart Auto-Play toggle */}
          <label
            className="queue-autoplay-card"
            style={{
              display: "flex", alignItems: "center", justifyContent: "space-between",
              cursor: canControl ? "pointer" : "default",
              padding: "10px 12px", borderRadius: 10,
            }}
          >
            <div>
              <div className="queue-ap-title" style={{ fontSize: 12, fontWeight: 600, marginBottom: 2 }}>
                Smart Master Auto-Play
              </div>
              <div className="queue-ap-subtitle" style={{ fontFamily: "'DM Mono', monospace", fontSize: 10, lineHeight: 1.4 }}>
                {autoPlay ? "Hi-Res matching masters queue on end" : "Stops after last queued track"}
              </div>
            </div>
            {/* Toggle switch */}
            <div
              onClick={() => canControl && setAutoPlay(v => !v)}
              className={`queue-toggle-track${autoPlay ? " is-active" : ""}`}
              style={{
                width: 34, height: 19, borderRadius: 10,
                position: "relative", flexShrink: 0, marginLeft: 12,
                transition: "background 0.2s, border-color 0.2s",
                cursor: canControl ? "pointer" : "not-allowed",
                opacity: canControl ? 1 : 0.4,
              }}
            >
              <div style={{
                position: "absolute", top: 2,
                left: autoPlay ? 15 : 2,
                width: 13, height: 13, borderRadius: "50%",
                background: "#FFFFFF",
                boxShadow: "0 1px 3px rgba(0,0,0,0.15)",
                transition: "left 0.2s",
              }} />
            </div>
          </label>
        </div>
      </div>
    </>
  )
}
