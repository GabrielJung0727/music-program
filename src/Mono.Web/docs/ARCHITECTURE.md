# Mono — Technical Architecture

> **Revision:** 2026-09-13  
> **Stack:** React 19 · TypeScript 5.7 · Vite 8 · Tailwind CSS v4

---

## 1. System Philosophy & Core Foundations

### 1.1 Dual-Tier Architecture

Mono is built on two non-overlapping tiers that never conflate audio transport with metadata signaling.

```
┌──────────────────────────────────────────────────────────────────┐
│  Tier 1 — Local-First Bit-Perfect Desktop Engine                 │
│                                                                  │
│  Native source (FLAC / DSD / streaming)                          │
│    → ISRC identity verification                                  │
│    → LENS DSP (hardware-integrated EQ / crossfeed / room sim)    │
│    → ASIO / WASAPI Exclusive driver (zero OS mixing)             │
│    → Physical DAC → headphones / speakers                        │
│                                                                  │
│  Guarantees: bit-perfect delivery, zero resampling, no           │
│  mixing layer interference. Only the Mono desktop client         │
│  can open ASIO exclusive streams.                                │
└──────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────┐
│  Tier 2 — Open Universal Crate Protocol (ISRC Signaling)         │
│                                                                  │
│  Shareable URLs carry ISRC fingerprints, not audio bytes.        │
│  Recipients resolve the same studio master on their own          │
│  licensed platform (Qobuz / TIDAL / local library).              │
│                                                                  │
│  URL format: https://mono.audio/crate/{id}?spec=isrc             │
│                                                                  │
│  No copyrighted audio is relayed. Live Lounges synchronise       │
│  playback position metadata only; each listener's client         │
│  pulls the stream directly from their licensed source.           │
└──────────────────────────────────────────────────────────────────┘
```

### 1.2 Studio Rack Aesthetic Principles

Every UI decision defers to four tactile hardware affordances borrowed from professional monitoring gear:

| Principle | Implementation |
|---|---|
| **High telemetry density** | DR score, bit depth, sample rate, and format badges visible at rest — never hidden behind hover states on primary views |
| **Monospace typography** | `DM Mono` for all numeric readouts, codec badges, timestamps, and spec labels; serif for editorial copy |
| **Dark chassis contrast** | Slate-900 / Zinc-900 transport bars and lounge rooms against white content panels — hardware shell vs. paper sleeve |
| **Tactile affordances** | Pill badges with live-pulse animations (`●`), animated waveform indicators, and bordered telemetry chips mimic physical meters |

---

## 2. Component Hierarchy & Module Boundaries

### 2.1 Directory Layout

```
src/
├── App.tsx                          # Central state machine & root orchestrator
├── main.tsx                         # React 19 entry point; mounts #root
├── index.css                        # Tailwind v4 import + global font wiring
│
├── data/
│   └── mockData.ts                  # Typed mock data, all exported interfaces
│
└── components/
    ├── TopNav.tsx                   # Tab bar, search, profile switcher
    ├── PlayerBar.tsx                # Bottom transport; dynamic telemetry
    ├── FullscreenPlayer.tsx         # Studio viz, lyrics, DSP toggle overlay
    ├── QueueDrawer.tsx              # Slide-over FIFO queue panel
    ├── ShareModal.tsx               # Universal ISRC share interface
    ├── TrackActionMenu.tsx          # Contextual track action popover
    ├── SettingsModal.tsx            # Profile & audio device modal
    ├── SettingsPage.tsx             # Full-page audio engine settings
    ├── AccountRequiredGateModal.tsx # Streaming service auth gate
    ├── StreamingLoginModal.tsx      # Qobuz / TIDAL OAuth flow
    │
    ├── home/
    │   └── HomePage.tsx             # Featured lounges, recently added, insights
    │
    ├── detail/
    │   ├── AlbumDetailView.tsx      # Master album inspection & tracklist
    │   └── ArtistDetailView.tsx     # Artist catalog & discography
    │
    ├── lounge/
    │   ├── LiveLoungePage.tsx       # Broadcast room discovery directory
    │   └── LiveLoungeRoom.tsx       # Synchronized multi-user listening room
    │
    ├── onboarding/
    │   ├── OnboardingWizard.tsx     # Multi-step onboarding shell
    │   ├── Step1AudioEngine.tsx     # ASIO / WASAPI engine selection
    │   ├── Step2StreamingServices.tsx
    │   ├── Step3AudiophileRig.tsx   # Listener profile & gear preset
    │   └── StreamingAuthModal.tsx
    │
    └── settings/
        └── GeneralSystemSettings.tsx
```

### 2.2 Component Responsibility Contracts

#### `src/App.tsx` — Root Orchestrator
- Owns all mutable application state; no child component manages cross-cutting state independently.
- Implements navigation history as a `NavSnapshot[]` stack with a `historyIndex` cursor (Back / Forward mirroring TIDAL's navigation model).
- Acts as the single event bus: child callbacks bubble up to App handlers; App dispatches state downward via props.
- Renders conditional view trees based on `currentTab`, `selectedAlbum`, `selectedArtist`, and `isSearchActive`.

#### `src/components/TopNav.tsx`
- **Inputs:** `currentTab`, `connectedServices`, `profiles`, search query state.
- **Outputs:** `setCurrentTab`, `onGoBack`, `onGoForward`, `setIsSearchActive`, `onSwitchProfile`.
- Renders streaming service link indicators (Qobuz teal / TIDAL blue) in the top-right toolbar.
- Manages profile switcher popover state locally; delegates profile selection to `onSwitchProfile`.

#### `src/components/PlayerBar.tsx`
- Fixed to `bottom-0`, `z-50`, height `h-24`.
- Renders three distinct layouts based on `playerMode`:
  - **`solo`:** Standard Hi-Fi transport (artwork, title/artist, play controls, volume, DSP, queue, format badge).
  - **`host`:** Adds blue `ON AIR` pill with pulse dot + `End Lounge` button; blue `border-t-2` accent.
  - **`guest`:** Replaces play controls with `Listening` indicator + `Leave Lounge` button; locks queue mutations.
- Exposes `PlayerMode = "solo" | "host" | "guest"` — imported by multiple consumers.
- The broadcast ring (blue top border + `ON AIR` pill) renders **only** when `playerMode === "host"`. It is hidden immediately when `handleLeaveLounge` resets `playerMode` to `"solo"`.

#### `src/components/FullscreenPlayer.tsx`
- Full-viewport overlay (`z-200`) triggered by `isFullscreenPlayer`.
- Displays synchronized lyrics via `TRACK_LYRICS` time-array (simulated playback cursor).
- DSP bypass toggle, liner notes panel, credits expansion, signal path visualization.
- ESC key listener closes the overlay (`useEffect` in `App.tsx`).

#### `src/components/QueueDrawer.tsx`
- Slide-over panel from right edge, `z-50`, width `384px`.
- Renders backdrop at `z-49` behind the panel.
- Two modes: **Play Queue** (solo/host — full reorder/delete control) vs. **Host Setlist** (guest — read-only).
- `toQueueTrack()` helper in `App.tsx` normalizes `TrackActionTarget` into `QueueTrack` before insertion.

#### `src/components/lounge/LiveLoungeRoom.tsx`
- Renders only when `activeLoungeRoom === true`.
- 12-column grid: left stage (artwork, live telemetry, waveform) + right panel (chat, listener list, gear wall, cue search).
- Sub-header bar contains `← Leave Lounge` → `onExitAttempt` (host sees end-session confirmation modal; guest exits directly).
- Inline share button calls `onOpenShare(shareData)` with session-specific `ShareData`.

#### `src/components/ShareModal.tsx`
- Accepts `ShareData` (see §6 for full interface).
- Renders two distinct modes: **Lounge** (live session invite with custom protocol URL) vs. **Playlist** (ISRC Crate Link with ISRC telemetry banner).
- `isPlaylist = shareData.type === "playlist"` gate drives all conditional sections without TS2367 narrowing errors.

#### `src/components/TrackActionMenu.tsx`
- Fixed-position popover rendered via `ReactDOM.createPortal`-style manual positioning (reads button `getBoundingClientRect` on open).
- `z-index: 9999` — always above PlayerBar (`z-50`) and drawers.
- Opens with `e.stopPropagation()` to prevent event bubbling through parent card click handlers.
- `flipUp` detection: if `r.bottom + menuHeight + 8 > window.innerHeight`, menu opens upward.

---

## 3. Global State Machine (`App.tsx`)

### 3.1 State Inventory

```typescript
// ── Active Playback ──────────────────────────────────────────────
const [currentTrack, setCurrentTrack]     = useState<QueueTrack | null>(null)
const [playing, setPlaying]               = useState<boolean>(true)
const [queue, setQueue]                   = useState<QueueTrack[]>(MOCK_QUEUE)
// Progress and volume are managed locally inside PlayerBar to avoid
// re-rendering the full tree on every animation frame tick.

// ── Session & Broadcasting ───────────────────────────────────────
const [playerMode, setPlayerMode]         = useState<PlayerMode>("solo")
//   "solo"  — standard local playback
//   "host"  — broadcasting; ON AIR indicator active
//   "guest" — joined a lounge; read-only transport
const [activeLoungeRoom, setActiveLoungeRoom] = useState<boolean>(false)
const [joinedLoungeId, setJoinedLoungeId]     = useState<string | null>(null)
const [hostSessionPrivacy, setHostSessionPrivacy] = useState<"public" | "unlisted">("public")
const [hostSessionSecretKey, setHostSessionSecretKey] = useState<string | undefined>(undefined)

// ── Navigation & Routing ─────────────────────────────────────────
const [currentTab, setCurrentTab]         = useState<CurrentTab>("lounges")
const [selectedAlbum, setSelectedAlbum]   = useState<any | null>(null)
const [selectedArtist, setSelectedArtist] = useState<any | null>(null)
const [isSearchActive, setIsSearchActive] = useState<boolean>(false)
const [searchQuery, setSearchQuery]       = useState<string>("")

// Nav history stack (Back / Forward)
const [navHistory, setNavHistory]         = useState<NavSnapshot[]>([...])
const [historyIndex, setHistoryIndex]     = useState<number>(0)

// ── Overlay & Modal State ────────────────────────────────────────
const [isFullscreenPlayer, setIsFullscreenPlayer]   = useState<boolean>(false)
const [isQueueOpen, setIsQueueOpen]                 = useState<boolean>(false)
const [shareModalData, setShareModalData]           = useState<ShareData | null>(null)
const [showEndSessionModal, setShowEndSessionModal] = useState<boolean>(false)
const [showAccountGateModal, setShowAccountGateModal] = useState<boolean>(false)
const [toastMessage, setToastMessage]               = useState<string | null>(null)
const [showOnboarding, setShowOnboarding]           = useState<boolean>(...)

// ── Profiles & Services ──────────────────────────────────────────
const [profiles, setProfiles]             = useState<Profile[]>(INITIAL_PROFILES)
const [activeProfileId, setActiveProfileId] = useState<string>("1")
const [connectedServices, setConnectedServices] = useState<{ qobuz: boolean; tidal: boolean }>(...)
```

### 3.2 Key Action Handlers

```typescript
// Queue operations
handlePlayNow(track)          // setCurrentTrack(toQueueTrack(track)); setPlaying(true)
handlePlayNext(track)         // Prepend to queue head
handleAddToQueue(track)       // Append to queue tail
handleRemoveFromQueue(index)  // Filter by index
handleReorderQueue(from, to)  // Splice move

// Session lifecycle
handleStartLounge(opts?)      // setPlayerMode("host"); setHostSessionPrivacy(...)
handleLeaveLounge()           // setPlayerMode("solo"); setActiveLoungeRoom(false); setJoinedLoungeId(null)
handleStopHosting()           // Same as above + navigate to lounges tab + close fullscreen
handleExitAttempt()           // Host → showEndSessionModal; Guest → handleLeaveLounge()

// Navigation
navigate(tab, navActiveTab, fullscreen?)  // setCurrentTab + pushNav snapshot
handleGoBack()                // Walk navHistory backward; album/artist views intercept first
handleGoForward()             // Walk navHistory forward
```

### 3.3 Session Teardown Lifecycle

When a user leaves or ends a lounge, the teardown sequence must run in order:

```
1. setPlayerMode("solo")
   └─ PlayerBar reads playerMode; drops ON AIR pill & blue border immediately.
   └─ LoungeActionButton switches to "Host Lounge" idle state.

2. setActiveLoungeRoom(false)
   └─ App render tree exits LiveLoungeRoom; shows LiveLoungePage directory.

3. setJoinedLoungeId(null)
   └─ Clears room reference used by PlayerBar share handler and QueueDrawer hostName.
```

Failure to run step 1 before navigating causes the blue broadcast ring to persist on unrelated views — this is the canonical regression guard.

### 3.4 Navigation View Resolution

`App.tsx` resolves the rendered view via a priority cascade:

```
isSearchActive && !selectedArtist && !selectedAlbum  →  SearchResultsView (inline)
selectedArtist && selectedAlbum                       →  AlbumDetailView
selectedArtist (only)                                 →  ArtistDetailView
currentTab === "lounges" && activeLoungeRoom          →  LiveLoungeRoom
currentTab === "lounges"                              →  LiveLoungePage
currentTab === "library"                              →  MyLibraryPage
currentTab === "explore"                              →  ExplorePage
currentTab === "settings"                             →  SettingsPage
(default)                                             →  HomePage
```

---

## 4. Audio Engine & ISRC Signaling Protocol

### 4.1 Audio-Free Signaling Architecture

Mono's Live Lounge feature synchronizes listening sessions without relaying copyrighted audio. The protocol operates as follows:

```
Host client                    Mono Signaling Layer              Guest client
────────────────               ──────────────────────            ────────────────
1. Resolve track ISRC          2. Broadcast ISRC +               3. Receive ISRC
   (e.g. USSM15800123)            playback position                 fingerprint

4. Play local/licensed ───────── no audio bytes ─────────────── 5. Resolve same
   FLAC on ASIO driver            transmitted                        ISRC on own
                                                                      Qobuz/TIDAL
                                                                   6. Play in sync
```

**ISRC fingerprint resolution:** A single ISRC code (e.g., `US-SM1-58-00123`) uniquely identifies the studio master recording regardless of platform. Mono maps this to the listener's preferred licensed source, ensuring they always hear the same pressing rather than a remaster or alternate take.

**Crate URL specification:**
```
https://mono.audio/crate/{crate-id}?spec=isrc
```
The `spec=isrc` query parameter signals to receiving clients that the payload encodes ISRC master fingerprints (ISRC Universal Protocol v2.4), enabling cross-platform sync across TIDAL, Qobuz, and the Mono ASIO Engine.

**Lounge invite URL:**
```
https://mono.audio/lounge?id={room-id}
https://mono.audio/lounge?id={room-id}&key={secretKey}   (unlisted / invite-only)
mono://lounge?id={room-id}                                (custom protocol → desktop client)
```

### 4.2 Audio DSP Pipeline

```
Native Source
  │  Local FLAC / DSD stored on disk
  │  OR real-time licensed stream (Qobuz HiRes / TIDAL Max)
  │
  ▼
ISRC Verification
  │  Fingerprint matched against session host's broadcast ISRC
  │  Rejects remaster / alternate takes silently; flags mismatch in telemetry
  │
  ▼
LENS DSP Engine  (hardware-integrated)
  │  ┌─ EQ presets: Harman Target / Flat Master / Warm Tube
  │  ├─ Crossfeed (stereo-to-binaural spatial)
  │  └─ Room simulation (optional; bypassed for headphone-only rigs)
  │
  ▼
ASIO / WASAPI Exclusive Driver
  │  Bypasses OS audio mixer entirely
  │  Zero resampling — sample rate matches source exactly
  │  Exclusive mode: no other app can share the device
  │
  ▼
Hardware Output
     DAC → headphone amp / power amp → transducers
```

**DSP preset state** (`dspPreset: "harman" | "flat" | "warm"`) and `crossfeedOn: boolean` are managed in `App.tsx` and passed to `PlayerBar` for the signal path popover.

**Format support matrix:**

| Format | Sample Rate | Bit Depth | Driver Mode |
|---|---|---|---|
| FLAC | 44.1 – 192 kHz | 16 / 24 | ASIO / WASAPI Exclusive |
| DSD64 | 2.8224 MHz | 1-bit | DoP or Native DSD via ASIO |
| DSD128 | 5.6448 MHz | 1-bit | Native DSD via ASIO |
| MQA (TIDAL) | Up to 384 kHz unfolded | 24 | WASAPI Exclusive |

---

## 5. Defensive Engineering Standards

### 5.1 Dual-Key Artwork Fallback

Track objects may arrive with artwork under either `art` (internal `QueueTrack`) or `coverUrl` (externally shaped album objects). All artwork `src` attributes must follow the three-tier fallback chain:

```tsx
// Standard pattern — used in PlayerBar, QueueDrawer, LiveLoungeRoom
src={
  track.art ||
  (track as any).coverUrl ||
  "https://images.unsplash.com/photo-1514525253161-7a46d19cd819?auto=format&fit=crop&w=600&q=80"
}
```

The Unsplash fallback URL resolves to a dark, neutral music-themed image — never a broken icon. Do not use local `/fallback.png` references; Vite's public asset serving is not guaranteed in all deployment contexts.

### 5.2 Event Bubbling Protection

Every action button (Play, Share, `•••` More) rendered inside a clickable card or row **must** stop propagation. Without this, the parent card's navigation `onClick` fires simultaneously:

```tsx
// Correct — parent card navigates; child button triggers isolated action
<div onClick={() => onSelectAlbum(album)}>   {/* card body */}
  <button
    onClick={(e) => {
      e.stopPropagation()
      onPlayAlbum(album)
    }}
  >
    ▶
  </button>
  <TrackActionMenu
    track={track}
    onPlayNow={onPlayNow}   // TrackActionMenu calls e.stopPropagation() in openMenu internally
    ...
  />
</div>
```

`TrackActionMenu` calls `e.stopPropagation()` inside its `openMenu` handler — callers do not need to add an additional guard for the `•••` button specifically.

### 5.3 Layer Hierarchy (z-index)

All z-index values are set explicitly. Never use arbitrary values outside this table:

| Layer | z-index | Components |
|---|---|---|
| Context menus / popover dropdowns | `9999` | `TrackActionMenu` |
| Share modal / client gate overlay | `400` | `ShareModal`, Mono Client Gate |
| Fullscreen player overlay | `200` | `FullscreenPlayer` |
| Now Playing / Signal Path modals | `200` | `NowPlayingModal` (inside PlayerBar) |
| Fixed transport bar | `50` (`z-50`) | `PlayerBar` |
| Queue drawer panel | `50` (`z-50`) | `QueueDrawer` panel |
| Queue drawer backdrop | `49` (`z-49`) | `QueueDrawer` backdrop |
| End session confirmation | `50` (`z-50`) | End session modal in `App.tsx` |
| Toast notifications | `500` | Toast in `App.tsx` |

Context menus must sit above PlayerBar (`z-50`). Toast notifications sit above everything else to guarantee visibility.

### 5.4 Guard: Host Conflict on Lounge Join

Before any lounge join is committed, `guardHostConflict()` checks for an active host session:

```typescript
const guardHostConflict = (onConfirmed: () => void): boolean => {
  if (playerMode !== "host") { onConfirmed(); return true }
  const ok = window.confirm("Active Broadcast in Progress — terminate?")
  if (!ok) return false
  setPlayerMode("solo")
  setActiveLoungeRoom(false)
  setIsFullscreenPlayer(false)
  onConfirmed()
  return true
}
```

Any new lounge join path (`handleToggleJoinLounge`, `executeJoinLounge`) must pass through this guard.

### 5.5 Deep-Link Handler

On mount, `App.tsx` parses `?lounge=<id>` and `?key=<secretKey>` from `window.location.search`. Two gates must pass before `executeJoinLounge()` is called:

1. **Desktop client check** — `window.__monoDesktopClient === true`; if false, shows the Mono Client Gate modal.
2. **Streaming service check** — at least one of `connectedServices.qobuz` or `connectedServices.tidal` must be true; if not, shows `AccountRequiredGateModal` and stores the pending join in `pendingLoungeJoin`.

The URL is cleaned with `window.history.replaceState` immediately after parsing regardless of outcome.

---

## 6. TypeScript Core Interfaces

### `QueueTrack` — Runtime playback unit

```typescript
interface QueueTrack {
  id: string                              // Unique queue slot ID (e.g. "qt-now", "q-1234-0")
  title: string
  artist: string
  album: string
  duration: string                        // "MM:SS" display string
  format: string                          // e.g. "FLAC 24-Bit / 192kHz", "DSD 2.8MHz"
  dr?: string                             // Dynamic range score, e.g. "DR14"
  art?: string                            // Artwork URL (primary key)
  source?: "qobuz" | "tidal" | "local"
}
```

### `Track` — Full master track record (extended)

```typescript
interface Track {
  id: string
  title: string
  artist: string
  album: string
  duration: string                        // "MM:SS"
  format: string                          // Codec + spec string
  sampleRate: number                      // Hz, e.g. 192000
  bitDepth: number                        // e.g. 24, or 1 for DSD
  dr?: string                             // "DR14"
  isrc?: string                           // e.g. "US-SM1-58-00123"
  art?: string                            // Artwork URL (primary)
  coverUrl?: string                       // Artwork URL (secondary / external shape)
  source?: "qobuz" | "tidal" | "local"
  isCurrent?: boolean                     // Playback cursor hint
}
```

### `SearchTrack` — Search result track (lightweight)

```typescript
interface SearchTrack {
  title: string
  artist: string
  album: string
  duration: string
  dr: string
  isCurrent?: boolean
}
```

### `Album` — Master album record

```typescript
interface Album {
  title: string
  artist: string
  year: string
  label: string                           // e.g. "Blue Note Records (BLP 1595)"
  studio: string                          // Recording studio name & location
  coverUrl: string                        // Primary artwork URL
  art?: string                            // Alternate artwork key (internal)
  format: string                          // e.g. "FLAC 24-Bit / 192kHz"
  dr: string                              // Album-level DR score
  wikiUrl: string
  wikiSummary: string                     // Liner note editorial copy
  tracks: SearchTrack[]
}
```

### `ShareData` — Universal share payload

```typescript
interface ShareData {
  type: "lounge" | "playlist" | "track" | "album"
  id?: string                             // Room or crate identifier
  title: string
  subtitle?: string                       // Host name, artist, or context line
  coverUrl?: string
  audioSpec?: string                      // e.g. "Bit-Perfect ASIO Master Stream"
  hostGear?: string                       // e.g. "Chord DAVE → Audeze LCD-4"
  visibility?: "public" | "unlisted"
  secretKey?: string                      // Required for unlisted rooms
}
```

**Branching logic on `type`:**
- `"playlist"` → Renders ISRC Crate Link + ISRC telemetry banner. Suppresses custom protocol button.
- `"lounge"` → Renders Live Now pill, session invite link, and "Launch Mono App" deep-link button.
- `"track"` / `"album"` → Reserved; rendered as lounge fallback until dedicated layouts are built.

### `LoungeRoom` — Broadcast session descriptor

```typescript
interface LoungeRoom {
  id: string
  title: string
  hostName: string
  hostAvatar: string                      // 2-letter initials for avatar fallback
  hostGear?: string                       // Signal chain descriptor
  currentTrackTitle: string
  currentArtist: string
  audioSpec: string                       // e.g. "FLAC 24/192", "Vinyl Rip DR14"
  listenerCount: number
  badge?: "debut" | "indie" | "cozy" | "trending"
  category: "indie" | "debut" | "cozy" | "popular"
  art: string                             // Room artwork URL
  visibility?: "public" | "unlisted"
  secretKey?: string                      // Present only when visibility === "unlisted"
}
```

### `Profile` — Listener identity

```typescript
type Profile = {
  id: string
  name: string
  tag: string                             // 2-char display abbreviation
  avatarText: string                      // Rendered inside avatar circle
  gearPreset: string                      // e.g. "Holo May KTE • Flat Master"
  isPrimary: boolean
}
```

### `PlayerMode` — Transport mode discriminant

```typescript
type PlayerMode = "solo" | "host" | "guest"
```

| Value | Transport controls | Queue control | Broadcast indicators |
|---|---|---|---|
| `"solo"` | Full | Full | None |
| `"host"` | Full | Full | ON AIR pill, blue border |
| `"guest"` | Hidden | Read-only | Listening pill, Leave button |

### `CurrentTab` — Top-level navigation route

```typescript
type CurrentTab = "home" | "lounges" | "library" | "explore" | "settings"
```

---

## 7. Build & Toolchain

| Tool | Version | Role |
|---|---|---|
| React | 19 | UI runtime |
| TypeScript | 5.7 | Static typing |
| Vite | 8 | Dev server & bundler |
| `@tailwindcss/vite` | v4 plugin | Utility CSS (no config file) |
| `@vitejs/plugin-react` | — | JSX transform |
| oxfmt | — | Formatter |

**Tailwind v4 conventions:**
- No `tailwind.config.ts` — configuration lives in `src/index.css` via `@import 'tailwindcss'` and `@theme` blocks.
- CSS `@import` statements must precede all other non-comment declarations in `src/index.css`.
- Arbitrary values (`z-[60]`, `text-[11px]`) are used where design-token values do not exist in the default scale.

**Font wiring:**
- `DM Mono` — loaded via Google Fonts CSS2 `@import` in `src/index.css`; used for all telemetry and monospace contexts via `fontFamily: "'DM Mono', monospace"` inline or `font-mono` Tailwind class.
- Serif body / editorial copy uses the system serif stack (`Georgia, serif`).

**Alias:**
- `@` → `src/` (configured in `vite.config.ts`).

---

## 8. Extension Points & Known Gaps

| Area | Current state | Extension path |
|---|---|---|
| Real ISRC resolution | Stubbed with mock data | Replace `SEARCH_DB` lookup with live Qobuz / TIDAL search API |
| Playback progress | Simulated `setInterval` inside `PlayerBar` | Wire to actual HTMLAudioElement or native ASIO callback |
| Live Lounge sync | Simulated with static room data | Replace with WebSocket signaling layer (`wss://signal.mono.audio`) |
| `ShareData.type === "track"` | Falls back to lounge layout | Dedicated track share card |
| `ShareData.type === "album"` | Falls back to lounge layout | Dedicated album share card |
| ASIO driver bridge | Desktop-client gated | Implement via Electron IPC or Tauri command |
| Onboarding persistence | `localStorage` key `mono_onboarding_completed` | Sync to user account on auth |
