# Mono — Design System & Theme Specification

> **Revision:** 2026-09-13  
> **Stack:** Tailwind CSS v4 · Google Fonts (Inter + DM Mono) · Inline CSS for accent-switching  
> **Cross-reference:** `docs/ARCHITECTURE.md` §1.2 Studio Rack Aesthetic, `docs/COMPONENTS.md` §2

---

## 1. Visual Identity & Dual-Accent Philosophy

Mono operates under a **single-surface, dual-signal** design system. The chassis — white or obsidian — never changes. What shifts is the accent signal, which communicates the listener's current session state at a glance without restructuring any layout.

```
Solo Hi-Fi Mode          →   Signature Violet   (#6d28d9 / #7c3aed / #8b5cf6)
Live Lounge / ON AIR     →   Broadcast Blue     (#2563eb / #3b82f6 / #60a5fa)
```

This mirrors physical broadcast studio conventions: violet is the studio reference monitor signal (calibrated, personal, focused); blue is the tally light / transmission indicator (live, shared, public). A listener immediately knows whether they are in private audiophile mode or synchronized with a live host, without reading any label.

### 1.1 Solo Hi-Fi — Signature Violet

**Identity:** Luxurious, focused, studio-grade personal listening.

| Property | Value |
|---|---|
| Primary hex | `#7c3aed` (violet-600) |
| Hover / active | `#6d28d9` (violet-700) |
| Luminous variant (dark chassis) | `#8b5cf6` (violet-500) |
| Shadow glow | `rgba(109,40,217,0.35)` |
| CSS accent-color (range inputs) | `#6d28d9` |

**Scope of violet signal:**
- Play button background and hover glow
- Scrubber / progress bar fill
- Active navigation indicator dot on `TopNav`
- Track title hover on `PlayerBar` (`group-hover/track:text-violet-700`)
- Album format badges in Personal Library (`bg-violet-50 text-violet-700 border-violet-200`)
- ISRC Protocol badge pulse dot in `ShareModal` (`bg-violet-500 animate-pulse`)
- Chat avatar background for listener messages (`bg-violet-100 text-violet-700`)
- Format readout pill in `LiveLoungeRoom` header (`text-violet-700 bg-violet-50 border-violet-200`)
- "Invite-Only" session badge text in dark lounge context (`text-violet-300 border-violet-500/30`)
- `Deep Indie` category accent in lounge filter (`text-violet-600`)
- Discography format badge for FLAC albums (`bg-violet-50 text-violet-700 border-violet-200`)

### 1.2 Live Broadcast — Signal Blue

**Identity:** Studio tally light. Active transmission. Shared synchronization.

| Property | Value |
|---|---|
| Primary broadcast | `#2563eb` (blue-600) |
| Accent blue | `#3b82f6` (blue-500) |
| Soft variant | `#60a5fa` (blue-400) |
| Shadow glow | `rgba(37,99,235,0.35)` |
| Broadcast border | `border-t-2 border-blue-500` on `PlayerBar` |

**Scope of blue signal:**
- `PlayerBar` top broadcast border (2px, replaces 1px slate border)
- `ON AIR` pill badge (`bg-blue-600 text-white border-blue-400/60`)
- Play button accent shifts from violet to `#2563eb` when `activeLoungeRoom || isHost`
- Scrubber fill shifts from violet to `#2563eb`
- `accentColor` and `accentShadow` computed values in `PlayerBar`
- Listener count pill in `LiveLoungeRoom` (`text-emerald-600` for count, blue dot for sync lock)
- Sync progress bar in listener stage (`bg-blue-600`)
- "Bit-Perfect Stream Sync Locked" status dot (`text-blue-600 ●`)
- "Return to Live Lounge (ON AIR)" button (`bg-blue-600 hover:bg-blue-700 text-white`)
- Chat send button (`bg-blue-600 hover:bg-blue-700`)
- Chat input focus ring (`focus:border-blue-500`)
- "Join →" pill in `TopNav` quick-preview (`bg-blue-600`)
- Currently-playing track title in search results (`text-blue-700`)
- "Join Lounge →" text in live lounge search card (`text-blue-400 group-hover:text-blue-300`)
- `FullscreenPlayer` "Synced Lyrics" active tab (`text-blue-600 border-blue-300 bg-blue-50`)
- Artist/album hover state in search results (`group-hover:text-blue-700`)
- DSD format badge in search results (`bg-blue-50 text-blue-700 border-blue-200`)
- Silent session checkbox `accentColor` in `HostModal` (`#2563eb`)
- Mono Desktop Engine icon in client gate modal (`stroke: #2563eb`)

### 1.3 The Accent Transition — Computed in `PlayerBar`

The accent shift is implemented as three CSS custom values, computed once per render in `PlayerBar.tsx`:

```typescript
const accentColor  = activeLoungeRoom || isHost ? "#2563EB" : "#6D28D9"
const accentShadow = activeLoungeRoom || isHost ? "rgba(37,99,235,0.35)" : "rgba(109,40,217,0.35)"
const accentHover  = activeLoungeRoom || isHost ? "#1D4ED8" : "#5B21B6"
```

These three values propagate to:
- Play button `background` (inline style, `transition: "background 0.3s"`)
- Play button `boxShadow`
- Scrubber progress fill `background` (`transition: "background 0.3s"`)
- Play button hover `onMouseEnter` / `onMouseLeave` handlers

The `border-t-2 border-blue-500` on the `<footer>` is a conditional Tailwind class string:
```tsx
className={`... ${activeLoungeRoom ? "border-t-2 border-blue-500" : "border-t border-slate-200"}`}
```

**Transition:** `0.3s` on `background` inline transitions; Tailwind `transition-colors` (150ms) on class-driven elements. The combined effect is a smooth 150–300ms mode shift — fast enough to feel instant, slow enough to be perceptible.

---

## 2. Palette & Design Tokens

### 2.1 Base Surface Tokens

The current implementation is light chassis only. Dark chassis tokens are documented as target specification.

#### Light Chassis (Current)

| Token name | Hex | Tailwind | Usage |
|---|---|---|---|
| `surface-base` | `#ffffff` | `bg-white` | App root, `PlayerBar`, `TopNav`, cards |
| `surface-raised` | `#f8fafc` | `bg-slate-50` | Hovered rows, input backgrounds |
| `surface-sunken` | `#f1f5f9` | `bg-slate-100` | Track number cells, tag chips |
| `border-default` | `#e2e8f0` | `border-slate-200` | Card borders, section dividers |
| `border-subtle` | `#f1f5f9` | `border-slate-100` | Inset separators |
| `text-primary` | `#0f172a` | `text-slate-900` | Headings, track titles |
| `text-secondary` | `#475569` | `text-slate-600` | Artist names, subtitles |
| `text-tertiary` | `#94a3b8` | `text-slate-400` | Timestamps, metadata |
| `text-muted` | `#64748b` | `text-slate-500` | Disabled labels, hints |
| `body-default` | `#111827` | `text-gray-900` | Body copy (`body` element) |

#### Dark Chassis (Target Specification)

The dark chassis activates for the lounge room interior and all dark-modal surfaces (share modal, account gate modal, client gate). It also defines the target theme for a future full dark mode.

| Token name | Hex | Tailwind approx | Usage |
|---|---|---|---|
| `chassis-deep` | `#0f172a` | `bg-slate-900` | Lounge room header, dark modal body |
| `chassis-surface` | `#1e293b` | `bg-slate-800` | Card surfaces inside dark contexts |
| `chassis-raised` | `#334155` | `bg-slate-700` | Hover states inside dark contexts |
| `chassis-border` | `#1e293b` | `border-slate-800` | Structural lines |
| `chassis-border-subtle` | rgba(255,255,255,0.06) | — | Frosted inner card borders |
| `chassis-text-primary` | `#f1f5f9` | `text-slate-100` | Headings in dark context |
| `chassis-text-secondary` | `#94a3b8` | `text-slate-400` | Metadata in dark context |
| `chassis-text-dim` | `#475569` | `text-slate-600` | Ultra-dim labels |

Dark chassis currently active on:
- `ShareModal` inner card (`background: "#FFFFFF"`) — note: share modal body is white; only the lounge card section is dark (`#0B1525 → #0F172A` gradient)
- `AccountRequiredGateModal` (full dark backdrop and card)
- `LiveLoungePage.tsx` lounge grid cards and spotlight hero
- `TopNav` quick-preview Live Lounge row (`bg-slate-900`)
- Search results "Live Lounges" row (`bg-slate-900`)
- `LiveLoungeRoom` left stage when no track is cued (dark background)

### 2.2 Violet (Solo Hi-Fi) Token Set

Full Tailwind + hex reference:

| Token | Tailwind class | Hex |
|---|---|---|
| `violet-50` | `bg-violet-50` | `#f5f3ff` |
| `violet-100` | `bg-violet-100` | `#ede9fe` |
| `violet-200` | `border-violet-200` | `#ddd6fe` |
| `violet-300` | `text-violet-300` | `#c4b5fd` |
| `violet-500` | `bg-violet-500` | `#8b5cf6` |
| `violet-600` | `text-violet-600` / `bg-violet-600` | `#7c3aed` |
| `violet-700` | `text-violet-700` | `#6d28d9` |
| `violet-800` | `border-violet-800` | `#5b21b6` |
| `violet-950` | `bg-violet-950` | `#2e1065` |

**Composed patterns (light chassis):**

```
Format badge (library/solo):   bg-violet-50  text-violet-700  border-violet-200
ISRC protocol header badge:    bg-violet-500/10  border-violet-500/25  text-violet-700
Invite-Only badge (dark ctx):  bg-zinc-800  text-violet-300  border-violet-500/30
Chat listener avatar:          bg-violet-100  text-violet-700
Active range thumb:            background: #6d28d9  (CSS input[type=range] global)
```

### 2.3 Blue (Broadcast) Token Set

| Token | Tailwind class | Hex |
|---|---|---|
| `blue-50` | `bg-blue-50` | `#eff6ff` |
| `blue-200` | `border-blue-200` | `#bfdbfe` |
| `blue-300` | `border-blue-300` | `#93c5fd` |
| `blue-400` | `text-blue-400` / `border-blue-400` | `#60a5fa` |
| `blue-500` | `border-blue-500` | `#3b82f6` |
| `blue-600` | `bg-blue-600` / `text-blue-600` | `#2563eb` |
| `blue-700` | `hover:bg-blue-700` / `text-blue-700` | `#1d4ed8` |
| `blue-800` | `hover:text-blue-800` | `#1e40af` |

**Composed patterns:**

```
ON AIR pill:                   bg-blue-600  text-white  border-blue-400/60  (font-mono)
Broadcast border:              border-t-2 border-blue-500  (PlayerBar footer)
Sync progress fill:            bg-blue-600  (inline width%)
Idle "Host Lounge" button:     bg-blue-50/70  text-blue-600  border-blue-200/80
DSD format badge:              bg-blue-50  text-blue-700  border-blue-200
Synced Lyrics active tab:      bg-blue-50  text-blue-600  border-blue-300
Join pill (quick-preview):     bg-blue-600  text-white  (hover: bg-blue-500)
Return-to-lounge button:       bg-blue-600  hover:bg-blue-700  text-white  font-mono font-bold
Chat send button:              bg-blue-600  hover:bg-blue-700  text-white  rounded-full
```

### 2.4 Telemetry Semantic Colors

These colors carry factual information about audio quality. They are not decorative.

#### Dynamic Range (DR) Rating Scale

```
DR ≥ 14  →  Emerald   bg-emerald-50  text-emerald-700  border-emerald-200   High dynamics, studio-grade
DR 13    →  Amber     bg-amber-50    text-amber-700    border-amber-200     Good dynamics, minor limiting
DR 12    →  Slate     bg-slate-100   text-slate-500    border-slate-200     Moderate compression
DR ≤ 11  →  Slate     bg-slate-100   text-slate-600    border-slate-200     Heavy compression (implicitly flagged)
```

All DR badges: `text-[10px] font-mono border px-2 py-0.5 rounded-full`

#### Format / Codec Badges

```
DSD (any rate)   →  Blue    bg-blue-50    text-blue-700    border-blue-200
FLAC (any depth) →  Purple  bg-purple-50  text-purple-700  border-purple-200
Vinyl rip        →  Amber   bg-amber-50   text-amber-700   border-amber-200  (press award overlay)
```

Note: Violet (`violet-*`) is used for FLAC badges in the ArtistDetailView discography context; `purple-*` is used in search results and `TopNav` quick-preview. Both resolve to a similar tint — violet represents personal library framing; purple represents catalog browsing framing.

#### Stream / Broadcast Status Colors

```
Live / active session:     Red     bg-red-500 animate-pulse (●)    Live Now indicator
Listener count:            Emerald text-emerald-600 bg-emerald-50   "● 42 Listening" pill
Host-invite share button:  Emerald bg-emerald-500/10 text-emerald-600 border-emerald-500/30
Termination badge:         Red     text-red-600 bg-red-50 border-red-200   "SESSION TERMINATION"
End session button:        Red     bg-red-600 hover:bg-red-700 text-white
```

#### Qobuz Brand Color

```
Qobuz accents:   Gold / Amber   #C9A227   (login modal border, spinner, gradient header)
                 Gradient:      linear-gradient(135deg, #C9A227 0%, #F5D06B 50%, #B8891A 100%)
```

#### Category Accent Colors (Lounge Filter Pills)

```
"popular"    →  Blue        text-blue-600     (default / inferred)
"debut"      →  Emerald     text-emerald-600
"indie"      →  Violet      text-violet-600
"cozy"       →  Amber       text-amber-600
```

#### Chat Identity Colors

```
Host message:     Amber    bg-amber-500 text-white (avatar)   text-amber-600 / text-amber-900 (username/msg)
Listener (AK):   Violet   bg-violet-100 text-violet-700
Listener (EF):   Emerald  bg-emerald-100 text-emerald-700
```

### 2.5 Neutral / Slate Chassis Palette

The full Slate scale is the structural spine. Violet and Blue are applied only where semantic signal value exists.

| Tailwind | Hex | Primary usage |
|---|---|---|
| `slate-50` | `#f8fafc` | Hovered card surfaces |
| `slate-100` | `#f1f5f9` | Track number cells, chip backgrounds |
| `slate-200` | `#e2e8f0` | Default borders, separator lines |
| `slate-300` | `#cbd5e1` | Subtle dots, light dividers |
| `slate-400` | `#94a3b8` | Secondary metadata text |
| `slate-500` | `#64748b` | Muted labels, disabled text |
| `slate-600` | `#475569` | Artist names, subtitles |
| `slate-700` | `#334155` | Medium-weight body |
| `slate-800` | `#1e293b` | Dark card surfaces |
| `slate-900` | `#0f172a` | Dark chassis, modal headers |

Zinc is used within `TrackActionMenu` and lounge modals for warm-dark contrast:

| Tailwind | Usage |
|---|---|
| `zinc-50` | ISRC telemetry banner background |
| `zinc-100` | Action button hover backgrounds |
| `zinc-200` | Divider lines in telemetry panels |
| `zinc-400` | Muted icon color (default state) |
| `zinc-800` | Invite-only session badge background |
| `zinc-900` | Deep overlay backgrounds |

---

## 3. Typography System

### 3.1 Typeface Roles

Two typefaces cover all text needs. Both are loaded from Google Fonts in `src/index.css` — `@import` rules precede all other declarations.

```css
@import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap');
@import url('https://fonts.googleapis.com/css2?family=DM+Mono:ital,wght@0,400;0,500;1,400&display=swap');
```

| Typeface | CSS family | Weights loaded | Role |
|---|---|---|---|
| **Inter** | `'Inter', sans-serif` | 400, 500, 600, 700 | Navigation labels, editorial copy, artist names, album titles, modal prose |
| **DM Mono** | `'DM Mono', monospace` | 400, 500 (roman + italic) | All numeric telemetry, codec badges, timestamps, ISRC codes, format specs, section sub-labels |
| **Georgia** (system) | `Georgia, serif` | system | Liner notes editorial prose, search result headlines (`font-serif`) |

`body { font-family: 'Inter', sans-serif; }` is the global default. DM Mono is applied inline or via `font-mono` Tailwind class wherever precision readout applies.

### 3.2 Typographic Scale

The scale is flat — most text sits in the 10–15px range to support high information density without scroll depth.

| Size | Tailwind | Usage |
|---|---|---|
| 9px | `text-[9px]` | Sub-badges inside share cards (`LIVE NOW`, `MONO CURATED PLAYLIST`) |
| 10px | `text-[10px]` | DR badges, format badges, section sub-labels, ISRC spec tags |
| 10.5px | `text-[10.5px]` | DM Mono metadata labels in modals |
| 11px | `text-xs` (partial) / `text-[11px]` | Primary monospace telemetry readouts, timestamps, spec lines, nav sub-labels |
| 12px | `text-xs` | Standard secondary body, player bar artist names, chat messages |
| 13px | `text-sm` (partial) / `text-[13px]` | Modal body copy, track title in tracklists |
| 14px | `text-sm` | Primary track display text, section headings |
| 15px | `text-[15px]` | Modal section headings |
| 16px | — | Settings section headers (inline) |
| 18px | `text-lg` | Onboarding step headings |
| 20px | `text-xl` | Search results page heading |
| 24px | `text-2xl` | Artist name in fullscreen player |

### 3.3 Telemetry Readout Patterns

All precision audio data uses DM Mono. Specific patterns by data type:

```tsx
// Bit depth + sample rate
<span style={{ fontFamily: "'DM Mono', monospace" }}>24-Bit / 192kHz FLAC</span>
<span style={{ fontFamily: "'DM Mono', monospace" }}>DSD 2.8MHz</span>
<span style={{ fontFamily: "'DM Mono', monospace" }}>FLAC 16-Bit / 44.1kHz</span>

// DR rating badge — font-mono, sized at text-[10px]
<span className="text-[10px] font-mono border px-2 py-0.5 rounded-full">DR14</span>
<span className="text-[10px] font-mono border px-2 py-0.5 rounded-full">DR 14</span>

// Timecode / duration
<span style={{ fontFamily: "'DM Mono', monospace" }}>10:58</span>
<span className="text-xs font-mono">00:00 / 05:40</span>

// ISRC fingerprint
<span style={{ fontFamily: "'DM Mono', monospace", letterSpacing: "0.05em" }}>US-SM1-58-00123</span>

// ISRC protocol version badge
<span className="font-mono text-[10px] tracking-wider uppercase">ISRC Universal Protocol v2.4</span>

// Latency readout (SettingsPage)
<span>256 Samples (5.8 ms latency)</span>  // dynamically computed, DM Mono context

// Audio spec line (lounge cards, queue drawer, PlayerBar)
<span style={{ fontFamily: "'DM Mono', monospace" }}>FLAC 24/192</span>
<span style={{ fontFamily: "'DM Mono', monospace" }}>Bit-Perfect ASIO Master Stream</span>
```

### 3.4 Letter Spacing & Text Transform

Monospace section labels use uppercase tracking to separate them visually from the content they label:

```tsx
// Standard telemetry section label pattern
style={{
  fontFamily: "'DM Mono', monospace",
  fontSize: 10,
  fontWeight: 600,
  letterSpacing: "0.05em",     // or "0.06em" / "0.1em" for stronger separation
  textTransform: "uppercase",
  color: "#94A3B8",
}}
```

Tailwind equivalent (used in section headers and search result labels):
```tsx
className="text-xs font-mono font-bold text-slate-400 uppercase tracking-wider"
```

---

## 4. Animation & Motion Tokens

### 4.1 Transition Durations

All interactive state changes use short, sub-200ms transitions to preserve the tactile feel of hardware controls.

| Duration | Usage |
|---|---|
| `0.12s` | Background on track row hover (`transition: "background 0.12s"`) |
| `0.15s` | Color, border-color, opacity on nav tabs, buttons, service cards |
| `0.18s` | Transform + box-shadow on album cards |
| `0.2s` | Box-shadow on catalog grid cards |
| `0.25s` | Transform (scale) on album cover hover images |
| `0.3s` | `accentColor` background transition on play button and scrubber fill |
| `0.3s` | `QueueDrawer` slide-over: `cubic-bezier(0.32,0,0.67,0)` |
| `180ms` | `LiveLoungePage` spotlight crossfade (opacity) |

Tailwind `transition` class (default 150ms ease) is used for color and opacity on most buttons and nav elements.

### 4.2 Pulse Animations

```tsx
// Keyframe: Tailwind animate-pulse (built-in 2s ease-in-out infinite)
<span className="w-1.5 h-1.5 rounded-full bg-red-500 animate-pulse" />  // Live Now indicator
<span className="w-1.5 h-1.5 rounded-full bg-blue-600 animate-pulse" /> // Currently playing in search
<span className="w-1.5 h-1.5 rounded-full bg-violet-500 animate-pulse" /> // ISRC Protocol badge dot

// Custom keyframe: ON AIR pulse dot (defined inline in PlayerBar)
<span style={{
  width: 6, height: 6, borderRadius: "50%", background: "#fff",
  display: "inline-block",
  animation: "pulse 1.5s ease-in-out infinite",
  flexShrink: 0,
}} />
```

The `ON AIR` pulse runs at 1.5s (faster than Tailwind's 2s) to convey urgency of live transmission.

### 4.3 Spotlight Crossfade (LiveLoungePage)

The 3-room spotlight carousel uses opacity crossfade on an 8-second auto-cycle interval:

```typescript
// Transition sequence (180ms total fade window)
setSpotlightFade(false)   // opacity: 0 — content fades out
setTimeout(() => {
  setActiveSpotlightIndex(next)
  setSpotlightFade(true)  // opacity: 1 — new content fades in
}, 180)

// CSS applied to content container
style={{ opacity: spotlightFade ? 1 : 0, transition: "opacity 0.18s ease" }}
```

### 4.4 Custom Keyframe Animations

```css
/* ShareModal entrance */
@keyframes share-fade-in {
  from { opacity: 0; transform: scale(0.97) translateY(6px); }
  to   { opacity: 1; transform: scale(1) translateY(0); }
}

/* TrackActionMenu entrance */
@keyframes tam-in {
  from { opacity: 0; transform: scale(0.95) translateY(-4px); }   /* or +4px if flipUp */
  to   { opacity: 1; transform: scale(1) translateY(0); }
}

/* Client Gate / Toast */
@keyframes gate-fade-in {
  from { opacity: 0; }
  to   { opacity: 1; }
}
```

All custom `@keyframes` are defined inline in `<style>` tags inside their respective component's JSX, scoped to the component's render tree.

---

## 5. Component Theming Standards

### 5.1 `TopNav` — Glassmorphism Shell

```
Background:        bg-white / bg-white border-b border-slate-200
Position:          sticky top-0 z-50
Height:            h-14 (56px)
```

**Active tab indicator:**  
A small dot below the active tab label. Violet in Home / Library / Explore; Blue when `hasActiveSession` and the Lounges tab is active.

```tsx
// The dot renders as a 1.5px × 1.5px circle (Tailwind: w-1.5 h-1.5)
// Color is not explicitly switched — it inherits from tab accent context
// Currently: a blue dot is implied for lounge context by the tab's active class
```

**Search bar:**  
```
Idle:      border border-slate-200 rounded-full bg-white focus-within:ring-1 focus-within:ring-slate-300
Active:    No additional accent — keeps neutral chrome to avoid competing with content
```

**Streaming service pills (inside services popover):**  
```
Qobuz connected:   Green dot (implied by label text)
TIDAL connected:   Blue dot (implied by brand context)
Not connected:     Gray (#CBD5E1) dot
```

**Profile avatar:**  
Two-letter initials on a per-profile color from `PROFILE_COLORS`:
```typescript
const PROFILE_COLORS = {
  "1": "#6D28D9",  // violet — primary profile
  "2": "#0F766E",  // teal
  "3": "#1D4ED8",  // blue
}
```

### 5.2 `PlayerBar` — Transport Dock

**Solo mode chassis:**
```
Background:        bg-white
Border:            border-t border-slate-200 (1px, slate)
Height:            h-24 (96px)
Play button:       background: #6D28D9, shadow: rgba(109,40,217,0.35)
Scrubber fill:     background: #6D28D9
Track title hover: text-violet-700 (via group-hover/track:text-violet-700)
```

**Host / Lounge mode chassis:**
```
Background:        bg-white (unchanged)
Border:            border-t-2 border-blue-500 (2px, electric blue — the broadcast line)
Play button:       background: #2563EB, shadow: rgba(37,99,235,0.35)
Scrubber fill:     background: #2563EB
ON AIR pill:       bg-blue-600 text-white border-blue-400/60 font-mono font-semibold h-7
ON AIR dot:        w-1.5 h-1.5 rounded-full bg-white animation: pulse 1.5s
```

**Guest mode chassis:**
```
Border:            border-t-2 border-blue-500 (activeLoungeRoom === true)
Play controls:     Hidden (rendered null for guest)
Listening pill:    bg-zinc-100 text-zinc-500 border-zinc-200/80 (neutral, not blue)
Leave button:      bg-red-50 text-red-700 border-red-300/60
```

**Idle "Host Lounge" button:**
```
bg-blue-50/70 text-blue-600 border-blue-200/80 hover:bg-blue-100/80
Broadcast icon (SVG radio waves) at 10×10px
```

**Format badge (when track is loaded):**
```
background: rgba(255,255,255,0.6) backdrop-blur
border: 1px solid rgba(228,228,231,0.9)
font: DM Mono, 10px
color: #52525B (zinc-600)
```

**Signal Path popover:** Dark glass overlay with DM Mono telemetry lines. No accent color — purely informational.

**Device selector popover:** Neutral slate surface. Active device: accent `accentColor` dot. `Solo` and `Hide` actions use `text-blue-600` and `text-slate-400` respectively.

### 5.3 `FullscreenPlayer` — Studio Visualization

**Standby state (no track):**
```
Background:        bg-slate-900 (deep dark chassis)
Logo:              'MONO' in slate-700 letterpress style
Quick picks:       White card tiles on dark surface
```

**Now Playing state:**
```
Background:        bg-slate-900
Left column:       Artwork fills a max-540px square with rounded-2xl, shadow-2xl
Right panel tabs:  "Synced Lyrics" active: bg-blue-50 text-blue-600 border-blue-300
                   "Track Credits" active: bg-slate-100 text-slate-900 border-slate-200
LENS DSP badge:    bg-slate-800 text-slate-300 border-slate-700 (inline pill)
Artist name:       text-blue-600 hover:text-blue-800 (click → ArtistDetailView)
Album name:        text-slate-400 hover:underline
Format/DR badges:  Same semantic palette as telemetry tokens (§2.4)
```

### 5.4 `QueueDrawer` — Slide-Over Panel

```
Background:        rgba(255,255,255,0.97) backdrop-blur-20px
Border-left:       1px solid rgba(228,228,231,0.9)
Shadow:            -8px 0 30px rgba(0,0,0,0.06)
Width:             384px
```

**Currently playing row:**
```
Left accent:       2px solid #2563EB (border-l-2 border-blue-600)
Background:        rgba(239,246,255,0.6) (blue-50 tint)
Label:             'NOW' font-mono text-[10px] text-blue-600
```

**Queue track rows:**
```
Hover:             bg-zinc-50 (subtle warm lift)
Artwork:           40×40px rounded-md (using dual-key fallback)
Remove button:     text-zinc-300 hover:text-red-500 (trash icon, revealed on hover)
```

**Guest / Host Setlist mode:**
```
Header title:      "Host Setlist" instead of "Play Queue"
Flush / Clear:     Hidden
Row click:         Disabled (canControl = false)
```

### 5.5 `ShareModal` — ISRC Protocol Interface

```
Backdrop:          rgba(0,0,0,0.58) backdrop-blur-6px zIndex: 400
Card:              #ffffff, border-radius: 20, border: 1px #E2E8F0
Shadow:            0 32px 80px rgba(0,0,0,0.24)
Width:             468px
```

**Lounge card (dark inner section):**
```
Background:        linear-gradient(145deg, #0B1525 0%, #0F172A 100%)
Border:            1px solid #1E293B
Ambient glow:      rgba(37,99,235,0.14) blurred circle (blue, top-left)
```

**LIVE NOW badge (lounge):**
```
color: #2563EB, bg: rgba(37,99,235,0.18), border: rgba(37,99,235,0.35)
font: DM Mono 9px, weight 700, tracking 0.1em
dot: 5×5px circle, #2563EB
```

**Mono Curated Playlist badge:**
```
color: #7C3AED, bg: rgba(124,58,237,0.15), border: rgba(124,58,237,0.3)
pulse dot: #8B5CF6 (violet-500)
Header badge: bg-violet-500/10 border-violet-500/25 text-violet-700
```

**Audio spec chip (inside dark card):**
```
color: #C9A227 (Qobuz gold), bg: rgba(201,162,39,0.1), border: rgba(201,162,39,0.22)
font: DM Mono 9.5px weight 600
```

**ISRC telemetry banner (playlist type):**
```
Background:        bg-zinc-50 border-zinc-200/80
Font:              font-mono text-[11px]
Labels:            text-zinc-400 uppercase tracking-wider text-[10px]
Values:            font-semibold text-zinc-800
```

### 5.6 `TrackActionMenu` — Contextual Popover

```
Background:        rgba(255,255,255,0.97) backdrop-blur-12px
Border:            1px solid #E4E4E7 (zinc-200)
Border-radius:     12px
Shadow:            0 10px 25px -5px rgba(0,0,0,0.08), 0 8px 10px -6px rgba(0,0,0,0.04)
z-index:           9999
Width:             192px
Animation:         tam-in 0.1s ease (scale 0.95 → 1.0, 100ms)
```

**Menu item hover:**
```
Background:        #F4F4F5 (zinc-100)
Border-radius:     8px
Icon:              text-zinc-600
Label:             text-zinc-900 font-medium
Sub-label:         text-zinc-400 text-[11px]
```

### 5.7 Onboarding Wizard

```
Background:        bg-slate-50 (full viewport)
Card surfaces:     bg-white border border-slate-200
Step indicator active:   bg-violet-600 text-white (Signature Violet — this is personal setup)
Step indicator complete: bg-violet-100 text-violet-600 (checkmark)
Progress bar fill:       bg-violet-600
Primary CTA button:      bg-violet-600 hover:bg-violet-700 text-white
ASIO badge accent:       blue-600 (ASIO driver is tied to broadcast/professional identity)
```

---

## 6. Accessibility & State Guardrails

### 6.1 Contrast Compliance

DR badges are the highest-risk small-text elements. Verified contrast ratios:

| Foreground | Background | Computed ratio | WCAG AA (4.5:1) |
|---|---|---|---|
| `#047857` (emerald-700) | `#ecfdf5` (emerald-50) | ~5.8:1 | ✓ Pass |
| `#92400e` (amber-800 approx) | `#fffbeb` (amber-50) | ~6.1:1 | ✓ Pass |
| `#475569` (slate-600) | `#f1f5f9` (slate-100) | ~4.6:1 | ✓ Pass (marginal) |
| `#6d28d9` (violet-700) | `#f5f3ff` (violet-50) | ~6.5:1 | ✓ Pass |
| `#1e40af` (blue-800 approx) | `#eff6ff` (blue-50) | ~8.4:1 | ✓ Pass |
| `#94a3b8` (slate-400) | `#ffffff` | ~2.7:1 | ✗ Fail — intentional for tertiary metadata only |

`text-slate-400` on white is intentionally below 4.5:1 — it is used exclusively for timestamps and secondary metadata that supports, rather than conveys, essential information. It must never carry DR ratings, format codes, or ISRC identifiers.

**DM Mono minimum size:** 10px at weight 400 is the floor for telemetry readouts. Nothing at 9px or below may be the sole carrier of functional data.

### 6.2 Accent Transition — Zero State Leaks

When `handleLeaveLounge()` fires in `App.tsx`, the following blue signals must extinguish immediately (within the same render cycle):

```
PlayerBar border:      border-t-2 border-blue-500  →  border-t border-slate-200
PlayerBar play button: background #2563EB           →  background #6D28D9
Scrubber fill:         background #2563EB           →  background #6D28D9
ON AIR pill:           visible                      →  unmounts (playerMode !== "host")
Listening pill:        visible                      →  unmounts (playerMode !== "guest")
```

Sequence guarantee (from `ARCHITECTURE.md §3.3`):
1. `setPlayerMode("solo")` — accentColor recomputes to `#6D28D9` in the same render
2. `setActiveLoungeRoom(false)` — border class switches in the same render
3. `setJoinedLoungeId(null)` — clears hostName / room reference

The 0.3s `transition: "background 0.3s"` on play button and scrubber means the blue-to-violet shift is animated — it does not snap. This is intentional: a smooth fade down from broadcast blue to personal violet mirrors a studio going off-air.

### 6.3 Animation Preference

No `prefers-reduced-motion` media query is currently implemented. Future implementation target:

```css
@media (prefers-reduced-motion: reduce) {
  .animate-pulse { animation: none; }
  * { transition-duration: 0ms !important; }
}
```

Until implemented, the pulse animations (`animate-pulse`, the `ON AIR` dot's custom `1.5s pulse`) remain active for all users.

### 6.4 Focus Indicators

Focus rings are not explicitly styled — the browser default outline applies. Custom focus rings should follow this pattern when implemented:

```css
/* Target: Signature Violet in solo context */
:focus-visible { outline: 2px solid #7c3aed; outline-offset: 2px; }

/* Target: Broadcast Blue in lounge context (requires JS class toggle) */
.lounge-context :focus-visible { outline: 2px solid #2563eb; outline-offset: 2px; }
```

### 6.5 Scrollbar Theming

Global scrollbar override in `src/index.css`:
```css
::-webkit-scrollbar        { width: 6px; height: 6px; }
::-webkit-scrollbar-track  { background: transparent; }
::-webkit-scrollbar-thumb  { background: #e5e7eb; border-radius: 3px; }
```

Thin, light, recessive — scrollbars do not compete with the accent signal system. On dark chassis surfaces (lounge room, dark modals), the scrollbar blends into the `#0f172a` background invisibly.

---

## 7. Design Token Quick Reference

The complete flat token list for implementation or future design-tool sync:

```
// ── Accent Signals ───────────────────────────────────────────────
--color-violet-primary:       #7c3aed
--color-violet-active:        #6d28d9
--color-violet-luminous:      #8b5cf6
--color-violet-shadow:        rgba(109,40,217,0.35)
--color-violet-bg-light:      #f5f3ff
--color-violet-border-light:  #ddd6fe

--color-blue-broadcast:       #2563eb
--color-blue-accent:          #3b82f6
--color-blue-soft:            #60a5fa
--color-blue-shadow:          rgba(37,99,235,0.35)
--color-blue-bg-light:        #eff6ff
--color-blue-border-light:    #bfdbfe

// ── Surface ──────────────────────────────────────────────────────
--surface-white:              #ffffff
--surface-raised:             #f8fafc
--surface-sunken:             #f1f5f9
--surface-chassis-deep:       #0f172a
--surface-chassis-surface:    #1e293b

// ── Text ─────────────────────────────────────────────────────────
--text-primary:               #0f172a
--text-secondary:             #475569
--text-tertiary:              #94a3b8
--text-muted:                 #64748b
--text-mono-dim:              #71717a

// ── Telemetry Semantic ───────────────────────────────────────────
--color-dr-high-fg:           #047857   // emerald-700 (DR ≥ 14)
--color-dr-high-bg:           #ecfdf5   // emerald-50
--color-dr-mid-fg:            #92400e   // amber-800
--color-dr-mid-bg:            #fffbeb   // amber-50
--color-dr-low-fg:            #475569   // slate-600
--color-dr-low-bg:            #f1f5f9   // slate-100

--color-format-flac-fg:       #6d28d9   // violet-700 / purple-700
--color-format-flac-bg:       #f5f3ff   // violet-50 / purple-50
--color-format-dsd-fg:        #1d4ed8   // blue-700
--color-format-dsd-bg:        #eff6ff   // blue-50

--color-live-pulse:           #ef4444   // red-500 (● Live Now)
--color-listener-fg:          #047857   // emerald-700
--color-listener-bg:          #ecfdf5   // emerald-50

// ── Brand Partners ───────────────────────────────────────────────
--color-qobuz-gold:           #c9a227
--color-qobuz-gradient:       linear-gradient(135deg, #C9A227 0%, #F5D06B 50%, #B8891A 100%)

// ── Motion ───────────────────────────────────────────────────────
--duration-instant:           0.12s
--duration-fast:              0.15s
--duration-medium:            0.25s
--duration-accent-shift:      0.3s
--duration-drawer-slide:      0.3s   // cubic-bezier(0.32,0,0.67,0)
--duration-spotlight-fade:    0.18s
--duration-on-air-pulse:      1.5s
--duration-live-pulse:        2.0s   // Tailwind animate-pulse default

// ── Typography ───────────────────────────────────────────────────
--font-sans:                  'Inter', sans-serif
--font-mono:                  'DM Mono', monospace
--font-serif:                 Georgia, serif
--font-size-badge:            10px
--font-size-telemetry:        11px
--font-size-body:             12px
--font-size-label:            13px
--letter-spacing-telemetry:   0.05em
--letter-spacing-badge:       0.1em
```
