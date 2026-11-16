// 데모용 커버 아트. 실제 Core 는 태그·폴더에서 뽑은 이미지를 내보내지만,
// 모의 Core 에는 음원이 없으므로 앨범마다 SVG 한 장을 그려서 준다.

import { rng } from "./catalog.mjs"

const esc = (s) => String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")

const SANS = "'Segoe UI','Helvetica Neue','Malgun Gothic',Arial,sans-serif"
const SERIF = "Georgia,'Times New Roman','Batang',serif"

/** 긴 제목을 줄 단위로 끊는다. SVG 에는 자동 줄바꿈이 없다. */
function wrap(text, max) {
  const words = String(text).split(/\s+/)
  const lines = []
  let line = ""
  for (const w of words) {
    if (line && (line + " " + w).length > max) {
      lines.push(line)
      line = w
    } else {
      line = line ? `${line} ${w}` : w
    }
  }
  if (line) lines.push(line)
  return lines.slice(0, 4)
}

function textBlock(lines, { x, y, size, lh = 1.12, fill, family = SANS, weight = 700, anchor = "start", spacing = 0, upper = false }) {
  return lines
    .map((l, i) =>
      `<text x="${x}" y="${y + i * size * lh}" font-family="${family}" font-size="${size}" font-weight="${weight}" fill="${fill}" text-anchor="${anchor}" letter-spacing="${spacing}">${esc(upper ? l.toUpperCase() : l)}</text>`)
    .join("")
}

function grain(seed) {
  return `<filter id="g" x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="2" seed="${seed}"/><feColorMatrix values="0 0 0 0 0.5  0 0 0 0 0.5  0 0 0 0 0.5  0 0 0 0.55 -0.12"/></filter>`
}

const STYLES = {
  block(a, [bg, c1, c2, ink]) {
    const t = wrap(a.title, 13)
    return `<rect width="600" height="600" fill="${bg}"/>
      <rect y="250" width="600" height="350" fill="${c1}"/>
      <circle cx="420" cy="330" r="170" fill="${c2}" opacity=".92"/>
      <circle cx="420" cy="330" r="90" fill="${bg}" opacity=".9"/>
      <rect x="44" y="44" width="6" height="${40 + t.length * 56}" fill="${c2}"/>
      ${textBlock(t, { x: 68, y: 92, size: 50, fill: ink, weight: 800, spacing: -1 })}
      ${textBlock([a.artist], { x: 68, y: 92 + t.length * 56 + 6, size: 17, fill: ink, weight: 600, spacing: 3, upper: true })}
      <text x="556" y="566" text-anchor="end" font-family="${SANS}" font-size="13" font-weight="700" letter-spacing="3" fill="${ink}" opacity=".8">${esc(a.label.toUpperCase())}</text>`
  },

  sun(a, [bg, c1, c2, ink]) {
    return `<rect width="600" height="600" fill="${bg}"/>
      <circle cx="300" cy="330" r="178" fill="${c1}"/>
      <rect y="372" width="600" height="20" fill="${bg}"/>
      <rect y="408" width="600" height="28" fill="${bg}"/>
      <rect y="456" width="600" height="40" fill="${bg}"/>
      <rect y="512" width="600" height="88" fill="${c2}"/>
      ${textBlock(wrap(a.title, 26), { x: 300, y: 84, size: 30, fill: ink, family: SERIF, weight: 400, anchor: "middle" })}
      <text x="300" y="556" text-anchor="middle" font-family="${SANS}" font-size="14" font-weight="600" letter-spacing="5" fill="${bg}">${esc(a.artist.toUpperCase())}</text>`
  },

  bands(a, [bg, c1, c2, ink], r) {
    let y = 250
    let out = ""
    for (let i = 0; i < 6; i++) {
      const h = 18 + Math.round(r() * 58)
      out += `<rect y="${y}" width="600" height="${h}" fill="${i % 2 ? c2 : c1}" opacity="${(0.45 + r() * 0.55).toFixed(2)}"/>`
      y += h + 6 + Math.round(r() * 12)
      if (y > 600) break
    }
    return `<rect width="600" height="600" fill="${bg}"/>${out}
      ${textBlock(wrap(a.title, 30), { x: 48, y: 84, size: 26, fill: ink, weight: 300, spacing: 0.5 })}
      <text x="48" y="${84 + wrap(a.title, 30).length * 30 + 10}" font-family="${SANS}" font-size="15" font-weight="600" fill="${ink}" opacity=".72">${esc(a.artist)}</text>
      <text x="552" y="84" text-anchor="end" font-family="${SANS}" font-size="12" font-weight="700" letter-spacing="3" fill="${ink}" opacity=".6">${esc(a.label.toUpperCase())}</text>`
  },

  type(a, [bg, c1, c2, ink]) {
    const t = wrap(a.title, 10)
    const size = t.length >= 3 ? 92 : 112
    return `<rect width="600" height="600" fill="${bg}"/>
      <rect x="-80" y="420" width="800" height="70" fill="${c2}" transform="rotate(-9 300 455)"/>
      ${textBlock(t, { x: 40, y: 60 + size * 0.86, size, lh: 0.94, fill: ink, weight: 900, spacing: -4, upper: true })}
      <rect x="40" y="520" width="${Math.min(520, 40 + a.artist.length * 13)}" height="42" fill="${c1}"/>
      <text x="58" y="548" font-family="${SANS}" font-size="17" font-weight="700" letter-spacing="2.5" fill="${bg}">${esc(a.artist.toUpperCase())}</text>`
  },

  frame(a, [bg, c1, c2, ink]) {
    const t = wrap(a.title, 20)
    return `<rect width="600" height="600" fill="${bg}"/>
      <rect x="34" y="34" width="532" height="532" fill="none" stroke="${c1}" stroke-width="1.5"/>
      <rect x="44" y="44" width="512" height="512" fill="none" stroke="${c1}" stroke-width=".6"/>
      <rect x="210" y="34" width="180" height="46" fill="${c1}"/>
      <text x="300" y="63" text-anchor="middle" font-family="${SANS}" font-size="12" font-weight="700" letter-spacing="3" fill="${bg}">${esc(a.label.toUpperCase())}</text>
      ${textBlock(t, { x: 300, y: 300 - (t.length - 1) * 24, size: 42, lh: 1.16, fill: ink, family: SERIF, weight: 400, anchor: "middle" })}
      <line x1="262" y1="${330 + (t.length - 1) * 26}" x2="338" y2="${330 + (t.length - 1) * 26}" stroke="${c1}" stroke-width="1.5"/>
      <text x="300" y="${366 + (t.length - 1) * 26}" text-anchor="middle" font-family="${SERIF}" font-size="20" font-style="italic" fill="${ink}" opacity=".85">${esc(a.artist)}</text>
      <text x="300" y="528" text-anchor="middle" font-family="${SANS}" font-size="12" letter-spacing="4" fill="${ink}" opacity=".55">${a.year}</text>`
  },

  arcs(a, [bg, c1, c2, ink]) {
    let out = ""
    const cols = [c1, c2, bg, c1, c2, bg, c1]
    for (let i = 0; i < 7; i++) {
      out += `<circle cx="600" cy="600" r="${470 - i * 62}" fill="${cols[i]}" opacity="${i === 2 || i === 5 ? 1 : 0.92}"/>`
    }
    return `<rect width="600" height="600" fill="${bg}"/>${out}
      ${textBlock(wrap(a.title, 16), { x: 46, y: 88, size: 34, fill: ink, weight: 700, spacing: -0.5 })}
      <text x="46" y="${88 + wrap(a.title, 16).length * 38 + 8}" font-family="${SANS}" font-size="15" font-weight="500" letter-spacing="1" fill="${ink}" opacity=".78">${esc(a.artist)}</text>`
  },

  mesh(a, [bg, c1, c2, ink], r) {
    const blob = (c, o) =>
      `<circle cx="${Math.round(80 + r() * 440)}" cy="${Math.round(80 + r() * 380)}" r="${Math.round(150 + r() * 110)}" fill="${c}" opacity="${o}" filter="url(#b)"/>`
    return `<defs><filter id="b" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="70"/></filter></defs>
      <rect width="600" height="600" fill="${bg}"/>
      ${blob(c1, 0.95)}${blob(c2, 0.7)}${blob(c1, 0.6)}${blob(c2, 0.35)}
      ${textBlock(wrap(a.title, 18), { x: 46, y: 500 - (wrap(a.title, 18).length - 1) * 40, size: 36, fill: ink, weight: 600, spacing: -0.5 })}
      <text x="46" y="548" font-family="${SANS}" font-size="14" font-weight="600" letter-spacing="3" fill="${ink}" opacity=".75">${esc(a.artist.toUpperCase())}</text>`
  },

  waves(a, [bg, c1, c2, ink], r) {
    let out = ""
    for (let i = 0; i < 7; i++) {
      const y = 250 + i * 48
      const amp = 16 + r() * 26
      const ph = r() * 200
      let d = `M0 ${y}`
      for (let x = 0; x <= 600; x += 30) {
        d += ` L${x} ${(y + Math.sin((x + ph) / (48 + i * 9)) * amp).toFixed(1)}`
      }
      d += " L600 600 L0 600 Z"
      out += `<path d="${d}" fill="${i % 2 ? c2 : c1}" opacity="${(0.28 + i * 0.1).toFixed(2)}"/>`
    }
    return `<rect width="600" height="600" fill="${bg}"/>
      <circle cx="452" cy="170" r="54" fill="${c2}" opacity=".9"/>${out}
      ${textBlock(wrap(a.title, 16), { x: 46, y: 92, size: 38, fill: ink, family: SERIF, weight: 400 })}
      <text x="46" y="${92 + wrap(a.title, 16).length * 42 + 6}" font-family="${SANS}" font-size="14" font-weight="600" letter-spacing="3" fill="${ink}" opacity=".8">${esc(a.artist.toUpperCase())}</text>`
  },

  grid(a, [bg, c1, c2, ink], r) {
    let out = ""
    for (let y = 0; y < 7; y++) {
      for (let x = 0; x < 9; x++) {
        const v = r()
        const rad = 4 + v * 22
        out += `<circle cx="${60 + x * 60}" cy="${64 + y * 56}" r="${rad.toFixed(1)}" fill="${v > 0.82 ? c2 : c1}" opacity="${(0.35 + v * 0.65).toFixed(2)}"/>`
      }
    }
    return `<rect width="600" height="600" fill="${bg}"/>${out}
      <rect y="452" width="600" height="148" fill="${bg}"/>
      ${textBlock(wrap(a.title, 22), { x: 46, y: 512, size: 34, fill: ink, weight: 800, spacing: -0.5, upper: true })}
      <text x="46" y="${512 + wrap(a.title, 22).length * 36}" font-family="Consolas,'Courier New',monospace" font-size="15" fill="${ink}" opacity=".7">${esc(a.artist)} · ${a.year}</text>`
  },

  split(a, [bg, c1, c2, ink]) {
    return `<rect width="600" height="600" fill="${bg}"/>
      <path d="M600 0 L600 600 L0 600 Z" fill="${c1}"/>
      <circle cx="300" cy="300" r="128" fill="${c2}"/>
      <circle cx="300" cy="300" r="128" fill="none" stroke="${bg}" stroke-width="2" opacity=".6"/>
      ${textBlock(wrap(a.title, 16), { x: 46, y: 92, size: 38, fill: ink, weight: 800, spacing: -1 })}
      <text x="554" y="556" text-anchor="end" font-family="${SANS}" font-size="15" font-weight="700" letter-spacing="3" fill="${bg}">${esc(a.artist.toUpperCase())}</text>`
  },
}

const cache = new Map()

export function coverSvg(album) {
  const hit = cache.get(album.id)
  if (hit) return hit
  const r = rng(album.id)
  const draw = STYLES[album.style] ?? STYLES.block
  const seed = Math.floor(r() * 90) + 1
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="600" height="600" viewBox="0 0 600 600"><defs>${grain(seed)}</defs>${draw(album, album.pal, r)}<rect width="600" height="600" filter="url(#g)" opacity=".5"/></svg>`
  cache.set(album.id, svg)
  return svg
}
