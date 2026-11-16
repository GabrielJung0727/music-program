import { put, list } from "@vercel/blob";
import { SEED_ITEMS } from "./seed.js";

const PREFIX = "mono-qa";

function dirFor(name) {
  return PREFIX + "/" + name + "/";
}

// Each entry is its own blob file (mono-qa/<collection>/<id>.json). Writes never read-modify-write
// a shared document, so two concurrent submissions can never clobber each other — the failure mode
// a single shared JSON array had (confirmed: a write could silently vanish under concurrent writes).
export async function addEntry(name, id, data) {
  await put(dirFor(name) + id + ".json", JSON.stringify(data), {
    access: "public",
    contentType: "application/json",
    addRandomSuffix: false,
    allowOverwrite: false,
    cacheControlMaxAge: 0,
  });
  return data;
}

export async function listEntries(name) {
  const { blobs } = await list({ prefix: dirFor(name) });
  if (!blobs.length) return null;
  const results = await Promise.all(blobs.map(async (b) => {
    try {
      const res = await fetch(b.url + "?t=" + Date.now(), { cache: "no-store" });
      if (!res.ok) return null;
      return await res.json();
    } catch (e) {
      return null;
    }
  }));
  return results.filter(Boolean);
}

export async function ensureSeeded() {
  const existing = await listEntries("items");
  if (existing !== null) return existing;
  await Promise.all(SEED_ITEMS.map((it) => addEntry("items", it.id, it)));
  return SEED_ITEMS;
}

export function newId(prefix) {
  return prefix + "_" + Date.now().toString(36) + "_" + Math.random().toString(36).slice(2, 8);
}
