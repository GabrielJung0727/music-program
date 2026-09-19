# Mono — Component Specification

> **Revision:** 2026-09-13  
> **Cross-reference:** `docs/ARCHITECTURE.md` for system philosophy, state machine, and TypeScript core interfaces.

---

## 1. Component System Overview

### 1.1 Modular Domain Structure

The application was extracted from a monolithic `App.tsx` into purpose-scoped domain directories. Each directory owns a specific product surface with clear dependency boundaries.

```
src/components/
│
├── TopNav.tsx              Shell — sticky header, tabs, search, profile
├── PlayerBar.tsx           Shell — fixed bottom transport bar
├── FullscreenPlayer.tsx    Shell — full-viewport now-playing overlay
├── QueueDrawer.tsx         Shell — slide-over playback queue panel
├── ShareModal.tsx          Overlay — ISRC universal share modal
├── TrackActionMenu.tsx     Overlay — contextual track action popover
├── SettingsModal.tsx       Overlay — profile & audio engine modal
├── AccountRequiredGateModal.tsx  Gate modal — streaming service auth
├── StreamingLoginModal.tsx Gate modal — Qobuz / TIDAL OAuth flows
│
├── home/
│   └── HomePage.tsx        View — featured lounges, crates, editorial
│
├── detail/
│   ├── AlbumDetailView.tsx View — master album inspection
│   └── ArtistDetailView.tsx View — artist catalog & discography
│
├── lounge/
│   ├── LiveLoungePage.tsx  View — broadcast room discovery directory
│   └── LiveLoungeRoom.tsx  View — synchronized multi-user room
│
├── onboarding/
│   ├── OnboardingWizard.tsx        3-step first-run wizard shell
│   ├── Step1AudioEngine.tsx        ASIO / WASAPI device selection
│   ├── Step2StreamingServices.tsx  Service account connection
│   ├── Step3AudiophileRig.tsx      Listener profile & gear preset
│   └── StreamingAuthModal.tsx      Auth overlay for onboarding
│
└── settings/
    └── GeneralSystemSettings.tsx   App-level config, backup, diagnostics
```

### 1.2 Presentation vs. Orchestration Boundary

`App.tsx` is the single source of truth for all cross-cutting state. No view or shell component manages state that affects another component. The contract is strict:

- **View components** receive data and callbacks via props; they never call `setCurrentTab`, `setPlayerMode`, or any other root state setter directly.
- **Shell components** (`PlayerBar`, `TopNav`, `QueueDrawer`) receive derived values — they never fetch, compute joined records, or resolve IDs.
- **Overlay components** receive fully-assembled data objects (`ShareData`, `QueueTrack[]`) — they do not query `mockData` or resolve IDs.

The one intentional exception: `LiveLoungeRoom` reads `LOUNGE_ROOMS` directly from `mockData` to derive the `LoungeRoom` record from `joinedLoungeId`. This is a contained lookup, not shared state mutation.

### 1.3 Prop-Drilling Contract

Callbacks always flow downward as typed function references. No component emits custom DOM events or uses a shared event bus. The callback naming convention is:

| Prefix | Meaning |
|---|---|
| `on` | User-initiated action (e.g., `onPlayNow`, `onLeaveLounge`) |
| `set` | Direct state setter passed through (e.g., `setCueSearchQuery`) |
| `onOpen` / `onClose` | Overlay visibility control |
| `onNavigate` / `onSelect` | Routing or object selection |

### 1.4 Event Bubbling Isolation

Nested interactive elements inside clickable containers **must always** call `e.stopPropagation()`. Without it, the parent card's navigation handler fires simultaneously with the child button's action.

```tsx
// Required pattern — every action button inside a clickable card
<div onClick={() => onSelectAlbum(album)}>
  <button
    onClick={(e) => {
      e.stopPropagation()  // blocks parent card click
      onPlayNow(track)
    }}
  >
    ▶ Play
  </button>
  {/* TrackActionMenu calls e.stopPropagation() internally in openMenu() */}
  <TrackActionMenu track={track} onPlayNow={onPlayNow} ... />
</div>
```

`TrackActionMenu.openMenu()` already calls `e.stopPropagation()` — callers only need to guard their own inline buttons.

### 1.5 Dual-Key Artwork Resolution

Track and album objects may carry artwork under either `art` (internal `QueueTrack` shape) or `coverUrl` (externally shaped album objects from search results or streaming APIs). All `<img>` `src` attributes must follow the three-tier chain:

```tsx
src={
  item.art ||
  item.coverUrl ||
  "https://images.unsplash.com/photo-1514525253161-7a46d19cd819?auto=format&fit=crop&w=600&q=80"
}
```

The Unsplash fallback resolves to a dark, neutral music-themed image. Never reference `/fallback.png` or other local public assets — asset serving is not guaranteed in all deployment contexts.

---

## 2. Shell & Persistent Playback Components

### `TopNav.tsx`

**Exported type:** `CurrentTab = "home" | "lounges" | "library" | "explore" | "settings"`

**Position:** `sticky top-0 z-50` — sits above all content, below context menus.

#### Props

```typescript
{
  // Core navigation
  activeTab: string
  setActiveTab: (t: string) => void
  currentTab: CurrentTab
  setCurrentTab: (t: CurrentTab) => void
  onGoHome: () => void

  // History navigation (TIDAL-style back/forward)
  onGoBack: () => void
  onGoForward: () => void
  canGoBack: boolean
  canGoForward: boolean

  // Search
  searchQuery?: string                    // default ""
  setSearchQuery?: (v: string) => void
  setIsSearchActive?: (v: boolean) => void

  // Quick-preview navigation shortcuts
  onOpenMilesDavis?: () => void
  onSelectSomethinElse?: () => void
  onReturnToLounge?: () => void

  // Search result data (from App.tsx filtered arrays)
  filteredAlbums?: SearchAlbum[]
  filteredTracks?: SearchTrack[]
  onSelectAlbum?: (album: SearchAlbum) => void

  // Streaming service status (drives Qobuz / TIDAL pills)
  connectedServices?: { qobuz: boolean; tidal: boolean }  // default both true
  onNavigateToAccounts: () => void

  // Session state (determines tab click behavior)
  hasActiveSession?: boolean              // true when activeLoungeRoom || playerMode === "host"

  // Profile system
  profiles?: Profile[]
  activeProfileId?: string
  activeProfile?: Profile
  showProfileMenu?: boolean
  setShowProfileMenu?: (v: boolean) => void
  onSwitchProfile?: (id: string) => void
  onOpenProfileSettings?: () => void
}
```

#### Local State

| State | Type | Purpose |
|---|---|---|
| `isServicesOpen` | `boolean` | Controls the streaming services status popover |
| `showQuickPreview` | `boolean` | Controls the search quick-preview dropdown |

#### Visual Sections

1. **Logo mark** — `MONO` wordmark; `onClick → onGoHome()`.
2. **Back / Forward buttons** — disabled when `!canGoBack` / `!canGoForward`; arrow SVG icons.
3. **Tab navigation** — five tabs (`Home`, `Live Lounges`, `Library`, `Explore`, `Settings`). Clicking `Live Lounges` when `hasActiveSession === true` calls `onReturnToLounge()` instead of switching tab.
4. **Global search input** — type-ahead popover (`showQuickPreview`) opens when query is non-empty; Enter key commits and triggers `setIsSearchActive(true)` for the full search view. Escape closes the popover.
5. **Quick-preview popover** — renders up to 3 tracks and 2 albums; includes a `View All Results` action and a static Live Lounge row linking to the active room. `zIndex: 9999`.
6. **Streaming services pill** — Opens a popover listing Qobuz and TIDAL connection status with a `Manage Accounts in Settings` link. Services backdrop `zIndex: 49`; popover `zIndex: 50`.
7. **Settings gear** — Toggles `currentTab` between `"settings"` and `"home"`. Active state: filled stroke.
8. **Profile avatar** — Initials circle with `PROFILE_COLORS` accent. Opens profile switcher menu (`z-50`) listing all profiles with switch and settings entry points. Backdrop `zIndex: 49`.

---

### `PlayerBar.tsx`

**Exported type:** `PlayerMode = "solo" | "host" | "guest"`

**Position:** `fixed bottom-0 left-0 right-0 z-50 h-24`.

#### Props

```typescript
{
  playerMode: PlayerMode
  setPlayerMode: (m: PlayerMode) => void
  activeLoungeRoom: boolean
  setActiveLoungeRoom: (v: boolean) => void
  playing: boolean
  setPlaying: (v: boolean) => void
  currentTrack?: QueueTrack | null
  joinedLoungeId: string | null

  // Navigation callbacks
  onNavigateToLens: () => void
  onOpenPlayer: () => void               // Opens fullscreen player or returns to lounge
  onOpenArtist: (e?: React.MouseEvent) => void
  onReturnToLounge: () => void
  isViewingArtistOrAlbum: boolean        // Determines "Return to Lounge" availability

  // Overlay & session controls
  isFullscreenPlayer: boolean
  setIsFullscreenPlayer: (v: boolean) => void
  onLeaveLounge: () => void              // Guest leave / triggers exit attempt for host
  onStartLounge: () => void             // Idle → host mode entry
  onStopHosting: () => void             // Immediate host session end
  onRequestStopHosting: () => void      // Triggers the end-session confirmation modal
  onShareLounge: () => void
  onToggleQueue: () => void
  queueCount: number
}
```

#### Local State

| State | Type | Initial | Purpose |
|---|---|---|---|
| `progress` | `number` | `39.7` | Playback progress 0–100 (simulated) |
| `volume` | `number` | `72` | Volume level 0–100 |
| `isNowPlayingOpen` | `boolean` | `false` | Controls liner notes / credits modal |
| `isSignalPathOpen` | `boolean` | `false` | Controls signal path popover |
| `isDspBypassed` | `boolean` | `false` | LENS DSP bypass toggle state |
| `isDevicePopoverOpen` | `boolean` | `false` | Controls output device selector |
| `showHiddenDevices` | `boolean` | `false` | Expands system / virtual devices |
| `devices` | `Device[]` | mock list | Output device inventory with volume offsets |

#### Operating Modes

**`solo` (default):**
- Standard Hi-Fi transport: artwork → title/artist → scrubber → format badge → controls → volume → DSP → queue.
- Bottom border: `border-t border-slate-200`.
- `LoungeActionButton` renders `Host Lounge` idle button.

**`host` (broadcasting):**
- Top border switches to `border-t-2 border-blue-500` (2px blue accent line).
- `LoungeActionButton` renders `ON AIR` pill (white pulse dot + blue background) + `End Lounge` button.
- `ON AIR` pill **only** renders when `playerMode === "host"` — it disappears immediately when `handleLeaveLounge` resets `playerMode` to `"solo"`.
- Accent color: `#2563EB` (blue).

**`guest` (listening):**
- Transport play/pause controls are hidden — guest cannot control playback.
- `LoungeActionButton` renders `Listening` indicator (blue dot + gray pill) + `Leave Lounge` button.
- Queue drawer in read-only mode.

#### Audio Telemetry Readouts

All format data is sourced from `currentTrack.format`. The bar parses and displays:

- **Format badge** (e.g., `FLAC 24-Bit / 192kHz`, `DSD 2.8MHz`) — shown in a bordered monospace pill.
- **DR score** — sourced from `currentTrack.dr` (e.g., `DR14`); color-coded green ≥ DR13, amber DR11–12.
- **LENS DSP status** — `isDspBypassed` toggles the LENS indicator from active to `BYPASSED` state.
- **Device name** — active device name from `devices` array (clicking opens device selector popover).
- **Latency computation** — sample rate is read from `format` string via regex; latency estimate shown in signal path.

#### Subcomponents

- **`NowPlayingModal`** — Fixed overlay (`zIndex: 200`). Shows studio personnel, recording metadata, liner notes.
- **`LoungeActionButton`** — Inline functional component; renders one of three layouts based on `playerMode`. No props — reads closure values.
- **Device selector popover** — Lists visible devices first, togglable hidden system devices. Per-device volume offset slider (dB). `Solo` and `Hide` actions per device.

---

### `FullscreenPlayer.tsx`

**Exported interface:** `FullscreenPlayerProps`

**Position:** `fixed inset-0 z-50` — full viewport, above PlayerBar.

#### Props

```typescript
interface FullscreenPlayerProps {
  isOpen: boolean
  onClose: () => void
  currentTrack?: any                     // QueueTrack or extended album track
  isPlaying?: boolean
  onTogglePlay?: () => void
  currentTime?: number
  duration?: number
  onSeek?: (time: number) => void
  onOpenArtist: () => void               // Navigate to ArtistDetailView
  onOpenAlbum: () => void                // Navigate to AlbumDetailView
  onNavigateToLensSettings: () => void   // Deep-link to Settings → LENS tab
  onSelectTrack?: (track: QueueTrack) => void  // Used by standby quick-picks
}
```

#### Local State

| State | Type | Initial | Purpose |
|---|---|---|---|
| `sideTab` | `"lyrics" \| "credits"` | `"lyrics"` | Controls right-panel content |
| `searchQuery` | `string` | `""` | Standby view search input |

#### Rendering Branches

**Standby (no `currentTrack`):**
- Dark full-viewport canvas with centered `MONO` wordmark.
- Grid of quick-pick album cards (click calls `onSelectTrack` + `onClose`).
- Search input filters quick picks locally.

**Now Playing (`currentTrack` present):**
- 2-column layout: left stage (artwork + transport) + right panel (lyrics / credits).
- Artwork: `currentTrack.art || currentTrack.coverUrl` — no fallback placeholder in fullscreen (artwork is expected for any track in the now-playing state).
- Format badge and DR score rendered below the artwork if `currentTrack.format` / `currentTrack.dr` are truthy.
- Artist name → `onOpenArtist()`; Album name → `onOpenAlbum()`.
- `LENS DSP` button → `e.stopPropagation(); onNavigateToLensSettings()`.

**Right panel — Lyrics (`sideTab === "lyrics"`):**
- Displays `TRACK_LYRICS` time-array for "Autumn Leaves"; placeholder for all other tracks.
- Musixmatch attribution label rendered at bottom.

**Right panel — Credits (`sideTab === "credits"`):**
- Static personnel list (Miles Davis, Cannonball Adderley, Hank Jones, Sam Jones, Art Blakey).
- "Miles Davis" entry is a button → `onClose(); onOpenArtist()`.

#### Behavior Notes
- `if (!isOpen) return null` — full unmount; no keep-alive.
- ESC close is handled in `App.tsx` via `window.addEventListener("keydown")` — not inside this component.

---

### `QueueDrawer.tsx`

**Position:** `fixed right-0 top-0 bottom-0 z-50 width-384px` slide-over panel. Backdrop at `z-49`.

#### Props

```typescript
{
  isOpen: boolean
  onClose: () => void
  currentTrack: QueueTrack | null
  queue: QueueTrack[]
  onRemoveFromQueue: (index: number) => void
  onReorderQueue: (fromIndex: number, toIndex: number) => void
  onClearQueue: () => void
  onFlushSession: () => void             // Clears currentTrack AND queue
  onPlayTrackNow: (track: QueueTrack, index: number) => void
  isGuest: boolean
  isHost: boolean
  hostName?: string                      // Shown as "Host Setlist by {hostName}" for guests
}
```

#### Local State

| State | Type | Initial | Purpose |
|---|---|---|---|
| `autoPlay` | `boolean` | `true` | Auto-advance toggle (display only) |
| `hoveredIdx` | `number \| null` | `null` | Drives play icon visibility on hover |

#### Behavior

- **Dual mode:** `canControl = !isGuest`. Guests see "Host Setlist" title and cannot reorder, remove, or flush tracks. Host/solo users get full queue management controls.
- **Artwork:** All track art follows the dual-key + fallback chain: `track.art || track.coverUrl || FALLBACK_URL`.
- **Slide animation:** CSS `transform: translateX(0)` (open) / `translateX(100%)` (closed) with `transition: 0.3s cubic-bezier(0.32,0,0.67,0)`.
- **Backdrop:** Separate `div` at `z-49` — `onClick={onClose}`. Panel div does not stop propagation; backdrop is the dismiss target.
- **Runtime total:** `totalRuntime()` sums all tracks' MM:SS durations and renders as `"X min YY sec"`.
- **Row interactions:** `onPlayTrackNow(track, index)` on row click; trash icon on hover calls `onRemoveFromQueue(index)`.
- **Currently playing row:** `currentTrack` is always displayed at the top of the list outside the `queue` array; it receives a blue left border and `NOW` label.

---

## 3. Main Views

### `home/HomePage.tsx`

#### Props

```typescript
{
  joinedLoungeId: string | null
  onToggleJoin: (roomId: string, e: React.MouseEvent) => void
  onNavigateToTracks: () => void
  onPlayAlbum?: (album: LibraryAlbum) => void
  onSelectAlbum?: (album: LibraryAlbum) => void
}
```

#### Local Sub-Components & State

**`LoungeCard`** (lounge directory cards):
- `onClick={(e) => onToggle(card.id, e)` — passes event for `e.stopPropagation()` in toggle handler.
- Join/Leave state driven by `joinedLoungeId === card.id`.

**`AlbumCard`:**
- `onClick={() => onSelect?.(album)` — card body navigates.
- Play button: `onClick={(e) => { e.stopPropagation(); onPlay(album) }}` — isolated with `stopPropagation`.

**`RecentlyAddedSection`:**

| State | Type | Purpose |
|---|---|---|
| `sortKey` | `"recently_added" \| "most_played" \| "alphabetical"` | Album sort order |
| `activeChip` | `"all" \| "hires" \| "dsd" \| "vinyl"` | Format filter |
| `menuOpen` | `boolean` | Sort menu popover |
| `activeSessionTab` | `"live" \| "hof"` | Lounge section tab (Live / Hall of Fame) |

#### Page Sections

1. **Active Live Lounges** — `LOUNGE_ROOMS` cards with real-time listener count badges. Join/Leave toggle.
2. **Recently Added** — `LIBRARY_ALBUMS` grid; format filter pills; sort popover.
3. **Listening Insights** — Static mock telemetry (hours listened, DR average, session count).
4. **Label Archives** — Static editorial banner (Blue Note Records spotlight).

---

### `ExplorePage.tsx`

#### Props

```typescript
interface ExplorePageProps {
  onPlayNow?: (track: TrackActionTarget) => void
  onPlayNext?: (track: TrackActionTarget) => void
  onAddToQueue?: (track: TrackActionTarget) => void
  onSelectAlbum?: (album: any) => void
}
```

All props are optional — the page renders fully without callbacks (actions are simply suppressed).

#### Local State

| State | Type | Initial | Purpose |
|---|---|---|---|
| `partnerTab` | `"qobuz" \| "tidal"` | `"qobuz"` | Top-level partner toggle |
| `qobuzSubTab` | `"releases" \| "playlists" \| "awards" \| "charts"` | `"releases"` | Qobuz sub-navigation |
| `tidalSubTab` | `"whatsnew" \| "explore" \| "playlists" \| "collection"` | `"whatsnew"` | TIDAL sub-navigation |

#### Sections

**Qobuz (`partnerTab === "qobuz"`):**
- Grand Selection — 5-column album grid; each card `onClick → onSelectAlbum({ art, coverUrl: art, title, artist, year })`.
- Press Awards — 5-column grid; same navigation pattern.
- Curated Playlists — editorial playlist cards (static).

**TIDAL (`partnerTab === "tidal"`):**
- TIDAL Mixes — 4-column music taste grid (static).
- Recommended Tracks — 2-column track rows with `TrackActionMenu`. Menu only renders when at least one of `onPlayNow` / `onPlayNext` / `onAddToQueue` is non-null.
- Suggested Albums — 5-column grid; `onClick → onSelectAlbum({ ...album, coverUrl: album.art })`.

**Event isolation:** Album card `onClick` handlers sit directly on the card `div` — no nested action buttons on these cards, so no `stopPropagation` is required here.

---

### `MyLibraryPage.tsx`

#### Props

```typescript
interface LibraryPageProps {
  onPlayNow?: (track: TrackActionTarget) => void
  onPlayNext?: (track: TrackActionTarget) => void
  onAddToQueue?: (track: TrackActionTarget) => void
  defaultTab?: "albums" | "artists" | "composers" | "playlists" | "tracks"
  onOpenShare?: (data: ShareData) => void
  onSelectAlbum?: (album: any) => void
}
```

#### Local State

| State | Type | Initial | Purpose |
|---|---|---|---|
| `libraryTab` | tab union | `defaultTab` | Active sub-tab |
| `playlists` | `PlaylistEntry[]` | `LIB_PLAYLISTS` | Local playlist CRUD |
| `isNewPlaylistModalOpen` | `boolean` | `false` | New playlist modal visibility |
| `newTitle` / `newDesc` | `string` | `""` | New playlist form inputs |
| `formatFilter` | `"all" \| "hires" \| "dsd" \| "vinyl"` | `"all"` | Albums tab format pill |
| `sortField` | `"album" \| "title" \| "artist" \| "format" \| "dr"` | `"album"` | Tracks tab sort column |
| `sortOrder` | `"asc" \| "desc"` | `"asc"` | Sort direction |
| `hoveredRow` | `number \| null` | `null` | Track row hover for play indicator |

#### Sub-tab Contents

**Albums tab:**
- 5-column grid filtered by `formatFilter`.
- Card `onClick → onSelectAlbum({ ...album, coverUrl: album.art })`.
- No nested action buttons on album cards — no `stopPropagation` needed.

**Artists tab:**
- 6-column circle-avatar grid. Click handlers are present on `div` containers but no `onSelectAlbum` callback is wired — navigation is future work.

**Composers tab:**
- 4-column portrait cards. Static classical work listings per composer.

**Playlists tab:**
- Playlist cards with 4-image mosaic cover (when `pl.arts.length >= 4`) or single image.
- Share button: `e.stopPropagation()` then `onOpenShare({ type: "playlist", ... })`.
- "New Playlist" card → modal with title/description inputs. Create button disabled when `!newTitle.trim()`. On create: prepends new `PlaylistEntry` to `playlists` state.
- New Playlist modal backdrop: `zIndex: 500`.

**Tracks tab:**
- Sortable table. Clicking a column header toggles sort direction if already sorted by that field; otherwise sets a new sort field.
- Sort logic: Format column uses `getAudioFidelityRank()` (DSD512 = 900 down to MP3 = 50). DR column always sorts descending.
- Grouped by album when `sortField === "album"`; flat list for all other fields.
- `TrackActionMenu` per row (only when at least one action callback is provided).

---

### `detail/AlbumDetailView.tsx`

#### Props

```typescript
interface Props {
  selectedAlbum: any                     // SearchAlbum | extended album object
  activeLoungeRoom: boolean
  playerMode: PlayerMode
  onReturnToLounge: () => void
  onBack: () => void
  onPlayNow: (t: TrackActionTarget) => void
  onPlayNext: (t: TrackActionTarget) => void
  onAddToQueue: (t: TrackActionTarget) => void
}
```

No local state — pure presentational component driven entirely by props.

#### Key Derived Values

```typescript
const artSrc = selectedAlbum?.art || selectedAlbum?.coverUrl || ""
const resolvedTracks = selectedAlbum.tracks?.length > 0
  ? selectedAlbum.tracks
  : SEARCH_DB.find(a => a.title === selectedAlbum.title)?.tracks ?? []
const linerNotes = selectedAlbum.wikiSummary
  || selectedAlbum.description
  || selectedAlbum.notes
  || "Recording notes unavailable."
```

`resolvedTracks` falls back to `SEARCH_DB` lookup when the album object arrived without a tracks array — this handles the case where `handleSelectAlbumAny` in App.tsx built a partial object.

#### Sections

1. **Breadcrumb bar** — `activeLoungeRoom && playerMode === "host"` renders `← Return to Live Lounge (ON AIR)` in blue; otherwise renders plain `← Back` pill. Both call their respective callbacks.
2. **Hero panel** — Artwork with ambient glow blur behind it, metadata badges (format, DR), studio field (hidden if falsy), Wikipedia link (hidden if `!wikiUrl`).
3. **Tracklist table** — Rows with track number, title (clickable → `onPlayNow`), artist, duration, DR badge. `TrackActionMenu` per row with `isGuest={playerMode === "guest"}`. Currently-playing row indicator from `track.isCurrent`.
4. **Liner notes** — `linerNotes` prose block + Wikipedia external link.

---

### `detail/ArtistDetailView.tsx`

#### Props

```typescript
interface Props {
  selectedArtist: any                    // MILES_DAVIS_PROFILE shape
  activeLoungeRoom: boolean
  playerMode: PlayerMode
  onReturnToLounge: () => void
  onBack: () => void
  onSelectSomethinElse: () => void       // Hardcoded to "Somethin' Else" album
}
```

No local state. Pure presentational.

#### Sections

1. **Breadcrumb bar** — Same host/guest branch as `AlbumDetailView`: host → `onReturnToLounge`; otherwise → `onBack`.
2. **Artist hero** — Avatar image (`selectedArtist.avatarUrl`), name, role, era, genre tags, Wikipedia link.
3. **Bio block** — `selectedArtist.wikiSummary` prose, label imprints.
4. **Discography grid** — Hardcoded 4-album grid. "Somethin' Else" card carries `playing: true` badge and `onClick={onSelectSomethinElse}`. All other cards are navigable as static visuals (no handler wired — future work).
5. **Top master recordings** — Static track list (Kind of Blue tracks). No playback wired.

---

### `lounge/LiveLoungePage.tsx`

#### Props

```typescript
{
  onJoin: () => void                     // Joins the default room (room-1)
  onGoLive: (opts: HostLaunchOpts) => void
  onOpenShare: (data: ShareData) => void
}

export interface HostLaunchOpts {
  visibility: "public" | "unlisted"
  secretKey?: string
}
```

#### Local State (`LiveLoungePage`)

| State | Type | Initial | Purpose |
|---|---|---|---|
| `selectedCategory` | category union | `"all"` | Lounge grid category filter |
| `isHostModalOpen` | `boolean` | `false` | Host modal visibility |
| `activeSpotlightIndex` | `number` | `0` | Featured room index (0–2) |
| `isAutoCyclePlaying` | `boolean` | `true` | Auto-rotates spotlight every 8s |
| `isSpotlightHovered` | `boolean` | `false` | Pauses auto-cycle on hover |
| `spotlightFade` | `boolean` | `true` | CSS opacity for fade transition |

#### Local State (`HostModal`)

| State | Type | Initial | Purpose |
|---|---|---|---|
| `privacy` | `"public" \| "unlisted"` | `"public"` | Session visibility toggle |
| `silentSession` | `boolean` | `false` | Silent mode option (display only) |

#### Spotlight Auto-Cycle

```typescript
// useEffect — 8-second interval, paused on hover or when isAutoCyclePlaying is false
useEffect(() => {
  if (!isAutoCyclePlaying || isSpotlightHovered) return
  const id = setInterval(() => {
    setSpotlightFade(false)
    setTimeout(() => {
      setActiveSpotlightIndex(prev => (prev + 1) % SPOTLIGHT_ROOMS.length)
      setSpotlightFade(true)
    }, 180)  // fade out → swap → fade in
  }, 8000)
  return () => clearInterval(id)
}, [isAutoCyclePlaying, isSpotlightHovered])
```

#### Room Filtering

`getDisplayedRooms()` for `"all"` category interleaves popular rooms (≥ 20 listeners) and discovery rooms at 2:1 ratio. For specific categories, filters `LOUNGE_ROOMS` by `room.category === selectedCategory`.

#### Sections

1. **Spotlight carousel** — 3 featured rooms with fade-transition. Tabbed navigation, dot pagination, play/pause cycle control, "Join" CTA.
2. **Category filter pills** — `"all" | "popular" | "debut" | "indie" | "cozy"`.
3. **Lounge grid** (`LoungeGridCard`) — Displays filtered rooms with share button, listener count, audio spec badge, and "Join Lounge" action. `DiscoveryBadge` renders when `room.badge` is truthy.
4. **Host New Lounge** — CTA triggers `HostModal`. Modal includes public/unlisted privacy toggle (unlisted auto-generates a `secretKey`), silent session toggle, and "Start Broadcasting" confirmation. Backdrop `zIndex: 200`.
5. **Hall of Fame** — Static historical sessions panel. No actions wired.
6. **Most Played** — Static listen count leaderboard. No actions wired.

---

### `lounge/LiveLoungeRoom.tsx`

**All state is fully lifted to `App.tsx`** — this component holds no local `useState`.

#### Props

```typescript
interface Props {
  currentTrack: QueueTrack | null
  playerMode: PlayerMode
  joinedLoungeId: string | null
  hostSessionPrivacy: "public" | "unlisted"
  hostSessionSecretKey: string | undefined

  // Cue search (lifted state)
  cueSearchQuery: string
  setCueSearchQuery: (q: string) => void
  onCueTrack: (t: QueueTrack) => void

  // Chat (lifted state)
  chatInput: string
  setChatInput: (v: string) => void

  // Reaction system (lifted state)
  showReactions: boolean
  setShowReactions: (v: boolean) => void
  floatingReactions: FloatingReaction[]
  triggerReaction: (emoji: string) => void

  // Gear wall (lifted state)
  isGearWallExpanded: boolean
  setIsGearWallExpanded: (v: boolean) => void

  // Session control
  onExitAttempt: () => void             // Host → shows end-session modal; Guest → leaves immediately
  onOpenShare: (data: ShareData) => void
}
```

#### Layout: 12-Column Grid

```
┌────────────────────────────────┬───────────────────────────────┐
│  col-span-8 — Left Stage       │  col-span-4 — Right Panel     │
│                                │                               │
│  • Album artwork (440px sq.)   │  • Sub-tabs: Chat / Members   │
│  • Track title & artist        │  • Chat message stream        │
│  • Live waveform animation     │  • Emoji reaction bar         │
│  • Listener count badge        │  • Listener avatar stack      │
│  • Cue search + result list    │  • Gear wall (collapsible)    │
│  • Quick Cue Master grid       │  • Quick Cue Master list      │
└────────────────────────────────┴───────────────────────────────┘
```

#### Left Stage Rendering Branches

```
currentTrack present?
├─ YES → NowPlayingStage (artwork, title, waveform, cue search)
└─ NO  → isHost?
         ├─ YES → HostStandbyConsole (broadcast config, waiting for first track)
         └─ NO  → ListenerStandby (waiting for host screen)
```

#### Artwork Resolution

```tsx
{(currentTrack.art || (currentTrack as { coverUrl?: string }).coverUrl)
  ? <img src={currentTrack.art || (currentTrack as any).coverUrl} ... />
  : <placeholder svg />
}
```

#### Session Metadata Derivation

```typescript
const isHost = playerMode === "host"
const room = LOUNGE_ROOMS.find(r => r.id === joinedLoungeId)
const isUnlistedSession = isHost
  ? hostSessionPrivacy === "unlisted"
  : room?.visibility === "unlisted"

const shareData: ShareData | null = isHost
  ? { type: "lounge", id: "my-live-session", title: "Live Audiophile Session", ... }
  : room
    ? { type: "lounge", id: room.id, title: room.title, ... }
    : null
```

#### Key Interactions

| Element | Handler | Notes |
|---|---|---|
| `← Leave Lounge` button | `onExitAttempt()` | Host sees end-session modal; guest exits directly |
| Share / Invite button | `onOpenShare(shareData)` | Label: "Invite Listeners" (host) vs "Share" (guest) |
| Gear wall toggle | `setIsGearWallExpanded(!isGearWallExpanded)` | Collapses right panel section |
| Cue search input | `setCueSearchQuery(value)` | Results filter `LIBRARY_ALBUMS` |
| Cue result row | `onCueTrack(track); setCueSearchQuery("")` | Sets track in App, clears search |
| Emoji button | `triggerReaction(emoji)` | Adds floating reaction to `floatingReactions` |
| Chat send | `setChatInput("")` | Clears input (no real send logic) |

#### Z-index Within Component

| Element | z-index |
|---|---|
| Cue search dropdown | `z-20` |
| Floating reaction canvas | `z-30` |
| Reaction picker popover | `z-20` |

---

## 4. Modals & Overlays

### `ShareModal.tsx`

**Position:** `fixed inset-0 zIndex: 400`.

#### Props

```typescript
interface Props {
  isOpen: boolean
  onClose: () => void
  shareData: ShareData | null
}
```

#### `ShareData` Interface

```typescript
export interface ShareData {
  type: "lounge" | "playlist" | "track" | "album"
  id?: string
  title: string
  subtitle?: string
  coverUrl?: string
  audioSpec?: string
  hostGear?: string
  visibility?: "public" | "unlisted"
  secretKey?: string
}
```

#### Rendering Branches on `type`

```typescript
const isPlaylist = shareData.type === "playlist"
const isUnlisted = shareData.visibility === "unlisted"
```

| `type` | Header title | URL format | Privacy notice | ISRC banner |
|---|---|---|---|---|
| `"playlist"` | "Share Playlist" | `https://mono.audio/crate/{id}?spec=isrc` | Hidden | Shown |
| `"lounge"` public | "Share Session" | `https://mono.audio/lounge?id={id}` | Yellow "Mono Client Required" box | Hidden |
| `"lounge"` unlisted | "Share Private Invite Link" | `https://mono.audio/lounge?id={id}&key={secretKey}` | Purple "Invite-Only Session" box | Hidden |

**Custom protocol URL** (all non-playlist): `mono://lounge?id={id}` — drives "Launch Mono App" deep-link button.

#### Local State

| State | Type | Purpose |
|---|---|---|
| `copied` | `boolean` | Toggles copy button to `✓ Copied` for 2 seconds |
| `specsCopied` | `boolean` | Toggles "Copy Audio Specs" button for 2 seconds |

#### Actions

- **Copy link** — `navigator.clipboard.writeText(webGateUrl)` with 2s feedback state.
- **Launch Mono App** — `window.location.href = customProtocolUrl` (triggers desktop client via registered `mono://` protocol handler).
- **Copy Audio Specs** — assembles `title + subtitle + audioSpec + hostGear + URLs` as newline-joined text; copies to clipboard with 2s feedback.

#### Dismiss Behavior
- Backdrop `onClick` → `if (e.target === e.currentTarget) onClose()`.
- Inner card has `onClick={(e) => e.stopPropagation()}`.
- `if (!isOpen || !shareData) return null`.

---

### `TrackActionMenu.tsx`

**Exported interface:** `TrackActionTarget`

```typescript
export interface TrackActionTarget {
  title: string
  artist?: string
  album?: string
  duration?: string
  format?: string
  dr?: string
  art?: string
  source?: "qobuz" | "tidal" | "local"
}
```

**Position:** `position: fixed` — manually positioned via `getBoundingClientRect()`. `zIndex: 9999`.

#### Props

```typescript
interface Props {
  track: TrackActionTarget
  onPlayNow: (track: TrackActionTarget) => void
  onPlayNext: (track: TrackActionTarget) => void
  onAddToQueue: (track: TrackActionTarget) => void
  isGuest?: boolean
}
```

#### Local State

| State | Type | Purpose |
|---|---|---|
| `isOpen` | `boolean` | Menu open/close |
| `pos` | `{ top, left, flipUp }` | Computed position from button bounds |

#### Positioning Logic

```typescript
const openMenu = (e: React.MouseEvent) => {
  e.stopPropagation()  // prevents parent card navigation
  const r = btnRef.current.getBoundingClientRect()
  const menuHeight = isGuest ? 80 : 132   // guest: 1 item; host/solo: 3 items
  const flipUp = r.bottom + menuHeight + 8 > window.innerHeight
  setPos({
    top: flipUp ? r.top - menuHeight - 4 : r.bottom + 4,
    left: Math.max(8, r.right - 192),     // right-aligns to button; clamps to viewport left
    flipUp,
  })
  setIsOpen(v => !v)
}
```

#### Menu Items

| Item | Shown when | Action |
|---|---|---|
| Play Now | Always | `onPlayNow(track); setIsOpen(false)` |
| Play Next | `!isGuest` | `onPlayNext(track); setIsOpen(false)` |
| Add to Queue | `!isGuest` | `onAddToQueue(track); setIsOpen(false)` |

#### Close Behavior

- `mousedown` outside both button and menu refs → `setIsOpen(false)`.
- `Escape` key → `setIsOpen(false)`.
- Both effects run only when `isOpen` and clean up their listeners on close.

---

### `SettingsModal.tsx`

**Position:** `fixed inset-0 z-[200]` — above content, below Mono Client Gate (`z-400`) and Toast (`z-500`).

**Exported type:** `SettingsTab = "profiles" | "engine" | "lens"`

#### Props

```typescript
interface SettingsModalProps {
  isOpen: boolean
  onClose: () => void
  profiles: Profile[]
  activeProfileId: string
  activeProfile: Profile
  onSwitchProfile: (id: string) => void
  onAddProfile: (name: string, gear: string) => void
  activeTab?: SettingsTab
  onTabChange?: (tab: SettingsTab) => void
}
```

#### Local State

| State | Type | Initial | Purpose |
|---|---|---|---|
| `currentTab` | `SettingsTab` | `activeTab ?? "profiles"` | Active left-nav tab |
| `newProfileName` | `string` | `""` | New profile form |
| `newProfileGear` | `string` | `""` | New profile gear preset input |

`useEffect` keeps `currentTab` in sync with `activeTab` prop — allows external deep-linking into specific tabs (e.g., "Open LENS Settings" navigates directly to `"lens"` tab).

#### Tab Contents

**Profiles tab:**
- Lists all `profiles` with name, `tag` avatar, `gearPreset`, and "Active" badge.
- "Switch to" button: shown only when `p.id !== activeProfileId`.
- New profile form: name + gear preset text inputs; `Enter` key submits. "Create Profile" button disabled when name is empty.
- Preview avatar renders initials from `newProfileName.trim()`.

**Engine tab:**
- ASIO buffer size selector (visual, no live state).
- Exclusive mode, integer mode, memory playback toggles.
- Output device list (static display).

**LENS tab:**
- EQ preset grid (Harman Reference, Flat Master, Warm Tube, Custom).
- Crossfeed depth slider.
- Room acoustic profile selector.

---

### `AccountRequiredGateModal.tsx`

**Position:** `fixed inset-0 zIndex: 450`.

#### Props

```typescript
interface Props {
  isOpen: boolean
  onClose: () => void
  onConnectService: (service: "qobuz" | "tidal") => void
}
```

Dark-themed modal explaining that a streaming service must be connected before joining a lounge. Two service cards (Qobuz / TIDAL) with hover effects. Backdrop click closes. Inner card `e.stopPropagation()`. `if (!isOpen) return null`.

---

### `StreamingLoginModal.tsx`

**Position:** Backdrop `zIndex: 300`. Renders `null` when `!isOpen || !service`.

#### Props

```typescript
interface Props {
  service: "qobuz" | "tidal" | null
  isOpen: boolean
  onClose: () => void
  onSuccessfulLogin: (service: "qobuz" | "tidal") => void
}
```

#### Internal Flows

**`QobuzFlow`** — Email/password form with:
- Show/hide password toggle.
- 1.2-second simulated auth on submit (`setLoading(true)` → spinner → `onSuccess()`).
- `keepLoggedIn` checkbox (display only).

**`TidalFlow`** — Three-state OAuth simulation:
```
"launch"     → "Open TIDAL.com to Authorize ↗" button
     ↓
"authorize"  → Simulated browser chrome with scope list + Confirm / Cancel
     ↓
"exchanging" → Spinner + "Exchanging OAuth Token (PKCE)..." → 1.5s → onSuccess()
```

---

### `SettingsPage.tsx`

Full-page settings view rendered inside `<main>` when `currentTab === "settings"`.

#### Props

```typescript
{
  initialTab?: "audio" | "lens" | "storage" | "accounts" | "general"
  connectedServices?: { qobuz: boolean; tidal: boolean }
  onToggleService?: (service: "qobuz" | "tidal") => void
}
```

#### Key Local State

| State | Type | Initial | Purpose |
|---|---|---|---|
| `settingsTab` | tab union | `initialTab` | Active left-nav section |
| `authService` | service \| null | `null` | Triggers `StreamingLoginModal` |
| `exclusiveMode` | `boolean` | `true` | WASAPI Exclusive toggle |
| `memoryPlayback` | `boolean` | `true` | RAM buffer toggle |
| `dsdStrategy` | `"native" \| "dop" \| "pcm"` | `"dop"` | DSD output method |
| `bufferSize` | `number` | `256` | ASIO samples; renders live `latencyMs` |
| `lensMasterBypass` | `boolean` | `true` | LENS engine master switch |
| `peqActive` | `boolean` | `true` | Parametric EQ enable |
| `peqPreset` | `string` | `"Harman Reference Target"` | Selected EQ curve |
| `crossfeedActive` | `boolean` | `true` | Bauer crossfeed enable |
| `crossfeedPreset` | preset union | `"bauer"` | Crossfeed algorithm |

**Latency computation:**
```typescript
const latencyMs = ((bufferSize / 44100) * 1000).toFixed(1)
// Renders: "256 Samples (5.8 ms latency)"
```

#### Audio Tab Sections

- **Output device** — Static device list (visual only; no live enumeration in browser context).
- **WASAPI Exclusive Mode** — Toggle. When active: "WASAPI Exclusive — OS audio mixer bypassed".
- **Memory Playback** — RAM buffer pre-load toggle.
- **DSD Strategy** — Three-way button group: `Native DSD` / `DoP (DSD over PCM)` / `Forced PCM`.
- **Buffer size** — Range slider 64–2048 samples; live latency display.

#### LENS Tab Sections

- **Master bypass** — Toggle collapses all LENS processing.
- **Parametric EQ** — SVG frequency response curve. Control point dots appear/disappear with `peqActive`. Opacity `0.3` when bypassed.
- **Crossfeed** — Bauer/Subtle/Wide algorithm selector. On/off toggle.

#### General Tab

Delegates entirely to `<GeneralSystemSettings />`.

---

### `settings/GeneralSystemSettings.tsx`

**Configuration scope:** App-level preferences, backup/restore, diagnostics, danger zone.

#### Overlay z-index Values

| Element | z-index |
|---|---|
| `Modal` shell (reusable) | `500` |
| `FactoryResetModal` | `600` |
| Toast notification | `9999` |

#### Sections

**Appearance & Behavior:**
- System toggles: `preventSleep`, `exclusiveMediaKeys`.
- `ramBuffer` selector: `"512mb" | "1gb" | "2gb" | "4gb"`.
- `specBadge` selector: `"all" | "format_only" | "dr_only" | "hidden"`.
- `visualizerFps` selector: `"60" | "30" | "15"`.
- `appearance` segment: `"system" | "light" | "dark"`.
- All changes write immediately to `localStorage` key `mono_general_settings`.

**Backup & Restore:**
- "Create Full Backup Archive" — assembles all `mono_*` localStorage keys into JSON, triggers download as `mono-full-backup-2026.json`.
- "Restore from Backup File" — Hidden `<input type="file">` opened programmatically; shows confirmation modal with `restoring` spinner simulation (1.1s).

**Diagnostics:**
- "Export Diagnostic Log" — Downloads `mono-diagnostic-log.txt` (structured timestamp + settings dump).
- "Submit Diagnostic Report" — Opens `SupportModal` (email, issue category, description). Simulates 1.1s submit.

**Danger Zone:**
- "Clear Cache" — Sets cache display to `"0 MB"` + toast.
- "Purge History" — Confirmation modal → `localStorage.removeItem("mono_history")` + toast.
- "Factory Reset Mono" — `FactoryResetModal` (`zIndex: 600`) with checkbox gate. On confirm: clears all `MONO_STORAGE_KEYS`, then calls `onReset?.()` or `window.location.reload()`. `FactoryResetModal` registers `Escape` key listener to cancel.

---

### `onboarding/OnboardingWizard.tsx`

**Position:** `fixed inset-0 z-50`. Shown when `localStorage.getItem("mono_onboarding_completed") !== "true"`.

#### Props

```typescript
interface WizardProps {
  onComplete: (profile: ListenerProfile) => void
  onExit: () => void
}
```

#### Wizard Steps

**Step 1 — Audio Engine (`Step1AudioEngine.tsx`):**
- Driver type selection: `"ASIO"` (Windows exclusive) / `"WASAPI"` (Windows shared or exclusive).
- Mock device enumeration with rescan animation (1s spinner on "Rescan" click).
- Device radio selection sets `audioConfig.deviceId`. "Continue" disabled until a device is selected.

**Step 2 — Streaming Services (`Step2StreamingServices.tsx`):**
- Connect Qobuz or TIDAL via `StreamingAuthModal` (same OAuth simulation as `StreamingLoginModal`).
- Connected state persisted to `localStorage` (`mono_streaming_services`).
- "Skip (Local Only)" link advances to Step 3.

**Step 3 — Audiophile Rig (`Step3AudiophileRig.tsx`):**
- Display name input.
- Avatar color swatch selector (6 options).
- Gear inputs (headphones, amp, DAC). DAC field auto-filled from Step 1 device selection (renders `AUTO` badge when prefilled).
- "Complete & Launch Mono" → `localStorage.setItem("mono_onboarding_completed", "true"); onComplete(profile)`.

**Step navigation:** Forward is linear (Step 1 → 2 → 3). Backward is free — clicking a completed step number jumps back. Step indicator shows checkmark (✓) for completed steps.

---

## 5. Defensive Engineering Standards

### 5.1 Event Isolation — Mandatory `stopPropagation`

Every interactive element that is a descendant of a clickable container **must** terminate propagation:

```tsx
// Card container — navigates on click
<div onClick={() => onSelectAlbum(album)}>

  {/* Play button — must isolate */}
  <button
    onClick={(e) => {
      e.stopPropagation()
      onPlayNow({ title: album.title, artist: album.artist, ... })
    }}
  >
    ▶
  </button>

  {/* Share button — must isolate */}
  <button
    onClick={(e) => {
      e.stopPropagation()
      onOpenShare({ type: "playlist", id: album.id, title: album.title })
    }}
  >
    ↗
  </button>

  {/* TrackActionMenu — isolates internally in openMenu() */}
  <TrackActionMenu track={track} onPlayNow={onPlayNow} ... />

</div>
```

**Violations cause:** both the card navigation AND the button action fire on a single click — a reliable regression that manifests as unintended route transitions.

### 5.2 Dual-Key Media Resolution

All `<img>` elements rendering track or album artwork must check both `art` and `coverUrl` before falling back:

```tsx
// Standard three-tier chain — used in PlayerBar, QueueDrawer, LiveLoungeRoom
<img
  src={
    item.art ||
    item.coverUrl ||
    "https://images.unsplash.com/photo-1514525253161-7a46d19cd819?auto=format&fit=crop&w=600&q=80"
  }
  alt={item.title}
/>
```

**Root cause:** Internal `QueueTrack` uses `art`. External album objects from search results and streaming API responses use `coverUrl`. The `toQueueTrack()` helper in `App.tsx` normalizes to `art`, but objects flowing directly from `handleSelectAlbumAny` or Explore page callbacks retain `coverUrl` only.

**Prohibited:** Single-key access (`src={item.art}`) without fallback will render broken image icons for streaming catalog albums.

### 5.3 Layer Hierarchy — Z-index Table

All z-index values are explicit inline styles or Tailwind arbitrary values. The full stack from bottom to top:

| Layer | z-index | Component(s) |
|---|---|---|
| Base content | `0` (auto) | All view content (`HomePage`, `ExplorePage`, etc.) |
| Lounge reaction canvas | `30` | Floating reactions in `LiveLoungeRoom` |
| Cue dropdown / reaction picker | `20` | `LiveLoungeRoom` internal popovers |
| New Playlist modal | `500` | `MyLibraryPage` |
| Sticky top nav | `50` (Tailwind `z-50`) | `TopNav` |
| Services / profile popovers | `49`–`50` | `TopNav` service and profile menus |
| Fixed transport bar | `50` (Tailwind `z-50`) | `PlayerBar` |
| Queue drawer panel | `50` (Tailwind `z-50`) | `QueueDrawer` |
| Queue drawer backdrop | `49` | `QueueDrawer` |
| End session modal | `50` (Tailwind `z-50`) | Inline in `App.tsx` |
| Fullscreen player | `50` (Tailwind `z-50`) | `FullscreenPlayer` |
| Streaming login modal | `300` | `StreamingLoginModal` |
| Account gate modal | `450` | `AccountRequiredGateModal` |
| Share modal | `400` | `ShareModal` |
| Host modal (lounge page) | `200` | `LiveLoungePage` `HostModal` |
| Settings modal | `200` (`z-[200]`) | `SettingsModal` |
| Now Playing modal | `200` | `NowPlayingModal` (inside `PlayerBar`) |
| General settings modals | `500` | `GeneralSystemSettings` `Modal` shell |
| Factory reset modal | `600` | `GeneralSystemSettings` `FactoryResetModal` |
| Context menus (track actions) | `9999` | `TrackActionMenu` |
| Search quick-preview | `9999` | `TopNav` quick-preview popover |
| Toast notification | `9999` | `GeneralSystemSettings` toast |
| App-level toast | `500` | Inline `toastMessage` in `App.tsx` |

**Regression guard:** `TrackActionMenu` must always be above `PlayerBar` (`z-50`). If the menu z-index is ever reduced to `z-[60]` (CSS value 60), it will clip underneath the fixed transport bar when triggered from tracks near the bottom of any view. The current value of `9999` is intentional.

### 5.4 Modal Dismiss Contracts

All modals must implement the backdrop-guard dismiss pattern to prevent accidental close when clicking inside the modal content:

```tsx
// Backdrop — only closes when clicking the backdrop itself
<div
  style={{ position: "fixed", inset: 0, zIndex: N }}
  onClick={(e) => { if (e.target === e.currentTarget) onClose() }}
>
  {/* Inner card — blocks propagation to backdrop */}
  <div onClick={(e) => e.stopPropagation()}>
    {/* content */}
  </div>
</div>
```

Components using `if (!isOpen) return null` fully unmount on close — no hidden DOM nodes. Components that animate out (e.g., `QueueDrawer`) use CSS `transform` transitions and remain mounted to preserve the slide animation.

### 5.5 Guest Mode Restrictions

When `playerMode === "guest"` or `isGuest === true`, the following UI elements are locked:

| Component | Guest restriction |
|---|---|
| `PlayerBar` | Play/pause, previous/next, scrubber — all hidden |
| `QueueDrawer` | Remove, reorder, clear, flush — all hidden. Title: "Host Setlist" |
| `TrackActionMenu` | "Play Next" and "Add to Queue" items hidden (`isGuest` prop) |
| `AlbumDetailView` | `TrackActionMenu` receives `isGuest={playerMode === "guest"}` |

Guest users can still: view the queue, read liner notes, open fullscreen view (read-only), and share the lounge session.
