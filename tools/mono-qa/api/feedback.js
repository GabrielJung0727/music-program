import { addEntry, newId } from "../lib/store.js";

function clip(s, n) {
  return String(s || "").slice(0, n);
}

export default async function handler(req, res) {
  if (req.method !== "POST") {
    res.status(405).json({ error: "method not allowed" });
    return;
  }
  const body = req.body || {};
  const itemId = clip(body.itemId, 120);
  const area = clip(body.area, 40);
  const role = body.role === "audio" ? "audio" : "devir";
  const author = clip(body.author, 60).trim();
  const status = ["ok","warn","bad"].includes(body.status) ? body.status : "ok";
  const note = clip(body.note, 2000).trim();
  const mediaLabel = clip(body.mediaLabel, 140).trim();

  if (!itemId || !author) {
    res.status(400).json({ error: "invalid_input" });
    return;
  }

  try {
    const id = newId("fb");
    const entry = {
      id, itemId, area, role, author, status, note, mediaLabel,
      submittedAt: new Date().toISOString(),
    };
    await addEntry("feedback", id, entry);
    res.status(200).json({ ok: true, entry });
  } catch (err) {
    res.status(500).json({ error: "write_failed", message: String(err && err.message || err) });
  }
}
