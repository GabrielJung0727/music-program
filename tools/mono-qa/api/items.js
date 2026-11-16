import { addEntry, newId } from "../lib/store.js";

const ADDABLE_AREAS = ["INSTALL","ONBOARDING","HOME_LIBRARY","NOWPLAYING","LOUNGE","SETTINGS","VISUAL","NEW_FEATURE"];

function clip(s, n) {
  return String(s || "").slice(0, n);
}

export default async function handler(req, res) {
  if (req.method !== "POST") {
    res.status(405).json({ error: "method not allowed" });
    return;
  }
  const body = req.body || {};
  const area = clip(body.area, 40);
  const title = clip(body.title, 140).trim();
  const detail = clip(body.detail, 2000).trim();
  const doc = clip(body.doc, 200).trim();
  const author = clip(body.author, 60).trim();

  if (!ADDABLE_AREAS.includes(area) || !title || !detail || !author) {
    res.status(400).json({ error: "invalid_input" });
    return;
  }

  try {
    const id = newId("item");
    const item = {
      id, area, title, detail, doc,
      forRoles: ["devir"],
      fixed: false,
      priority: false,
      flag: "",
      order: Date.now(),
      createdBy: author,
      createdAt: new Date().toISOString(),
    };
    await addEntry("items", id, item);
    res.status(200).json({ ok: true, item });
  } catch (err) {
    res.status(500).json({ error: "write_failed", message: String(err && err.message || err) });
  }
}
