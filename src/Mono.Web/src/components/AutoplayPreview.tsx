import { useEffect, useState } from "react"
import { useMono, useMonoCommands } from "../state/MonoProvider"

/** Show the server's upcoming choices without requiring the queue drawer to be open. */
export default function AutoplayPreview() {
  const { room, peerId } = useMono()
  const cmd = useMonoCommands()
  const [now, setNow] = useState(Date.now())
  const autoplay = room?.autoplay

  useEffect(() => {
    if (!autoplay) return
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [autoplay])

  if (!room?.smartAutoplay || !autoplay?.candidates.length) return null
  const canChoose = room.hostPeerId === peerId
  const seconds = Math.max(0, Math.ceil((autoplay.deadlineUnixMs - now) / 1000))

  return (
    <aside aria-label="Upcoming autoplay tracks" style={{ position: "fixed", right: 20, bottom: 100, width: 300, zIndex: 250, padding: 14, borderRadius: 12, color: "var(--text-primary)", background: "var(--surface-card)", border: "1px solid var(--border-subtle)", boxShadow: "0 12px 30px rgba(0,0,0,0.24)" }}>
      <strong style={{ fontSize: 13 }}>Up next · {seconds}s</strong>
      <div style={{ fontSize: 11, color: "var(--text-secondary)", margin: "3px 0 8px" }}>Choose a track for Smart Auto-Play</div>
      {autoplay.candidates.map(candidate => (
        <button key={candidate.id} type="button" disabled={!canChoose}
          onClick={() => cmd.chooseAutoplay(candidate.id)}
          style={{ display: "block", width: "100%", padding: "7px 8px", marginTop: 5, textAlign: "left", color: "var(--text-primary)", background: "var(--surface-elevated)", border: candidate.id === autoplay.chosenId ? "1px solid var(--accent-violet)" : "1px solid var(--border-subtle)", borderRadius: 7, cursor: canChoose ? "pointer" : "default" }}>
          {candidate.title} · {candidate.artistName ?? "Unknown Artist"}
        </button>
      ))}
    </aside>
  )
}
