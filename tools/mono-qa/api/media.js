import { addEntry, newId } from "../lib/store.js";

function clip(s, n) {
  return String(s || "").slice(0, n);
}

function safeUrl(u) {
  try {
    const p = new URL(u);
    if (p.protocol === "http:" || p.protocol === "https:") return p.href;
  } catch (e) {}
  return null;
}

export default async function handler(req, res) {
  if (req.method !== "POST") {
    res.status(405).json({ error: "method not allowed" });
    return;
  }
  const body = req.body || {};
  const itemId = clip(body.itemId, 120);
  const filename = clip(body.filename, 200).trim();
  const url = safeUrl(clip(body.url, 2000).trim());
  const note = clip(body.note, 300).trim();
  const uploader = clip(body.uploader, 60).trim();

  if (!itemId || !filename || !url || !uploader) {
    res.status(400).json({ error: "invalid_input" });
    return;
  }

  try {
    const id = newId("media");
    const entry = {
      id, itemId, filename, url, note, uploader,
      submittedAt: new Date().toISOString(),
    };
    await addEntry("media", id, entry);
    res.status(200).json({ ok: true, entry });
  } catch (err) {
    res.status(500).json({ error: "write_failed", message: String(err && err.message || err) });
  }
}
