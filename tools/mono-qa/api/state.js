import { ensureSeeded, listEntries } from "../lib/store.js";

export default async function handler(req, res) {
  if (req.method !== "GET") {
    res.status(405).json({ error: "method not allowed" });
    return;
  }
  try {
    const [items, feedback, media] = await Promise.all([
      ensureSeeded(),
      listEntries("feedback"),
      listEntries("media"),
    ]);
    res.setHeader("Cache-Control", "no-store");
    res.status(200).json({ items, feedback: feedback || [], media: media || [] });
  } catch (err) {
    res.status(500).json({ error: "state_read_failed", message: String(err && err.message || err) });
  }
}
