import React from "react"

export interface MonoIconProps extends React.SVGProps<SVGSVGElement> {
  size?: number | string
  strokeWidth?: number
  className?: string
  color?: string
}

const defaultProps = {
  xmlns: "http://www.w3.org/2000/svg",
  viewBox: "0 0 24 24",
  fill: "none",
  stroke: "currentColor",
  strokeLinecap: "round" as const,
  strokeLinejoin: "round" as const,
}

// ── 1. Lounge & Discovery ────────────────────────────────────────────────────

export const MonoFlameIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="M12 2c1.5 3 4 5.5 4 9a6 6 0 1 1-12 0c0-3.5 2-6 3.5-7.5C8 5.5 8.5 7.5 10 9c.5-2 1-5 2-7z" />
    <path d="M12 17.5a2.5 2.5 0 0 0 2.5-2.5c0-1.5-1-2.5-2.5-3.5-1.5 1-2.5 2-2.5 3.5a2.5 2.5 0 0 0 2.5 2.5z" />
  </svg>
)

export const MonoSproutIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="M7 20h10" />
    <path d="M12 20v-8" />
    <path d="M12 12c0-4.5 3.5-7 8-7 0 4.5-2.5 8-7 8" />
    <path d="M12 15c0-3.5-2.5-5.5-6-5.5 0 3.5 2 6 6 6" />
  </svg>
)

export const MonoGuitarIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="m19 5-3 3" />
    <path d="m14 10 2-2" />
    <path d="M17 3l4 4" />
    <path d="M15 9l-4.5 4.5a3.5 3.5 0 0 0-.8 3.8l.3.8-3 3a2.12 2.12 0 0 1-3-3l3-3 .8.3a3.5 3.5 0 0 0 3.8-.8L15 9z" />
    <circle cx="10" cy="14" r="1" fill="currentColor" stroke="none" />
  </svg>
)

export const MonoCupIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="M18 8h1a3 3 0 0 1 0 6h-1" />
    <path d="M4 8h14v7a4 4 0 0 1-4 4H8a4 4 0 0 1-4-4V8z" />
    <path d="M7 2v3" />
    <path d="M11 2v3" />
    <path d="M15 2v3" />
  </svg>
)

export const MonoTrophyIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="M6 9H4a2 2 0 0 1-2-2V5h4" />
    <path d="M18 9h2a2 2 0 0 0 2-2V5h-4" />
    <path d="M4 5h16v4a8 8 0 0 1-16 0V5z" />
    <path d="M12 13v5" />
    <path d="M8 21h8" />
    <path d="M10 18h4" />
  </svg>
)

export const MonoLockIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <rect x="5" y="11" width="14" height="10" rx="2" />
    <path d="M8 11V7a4 4 0 0 1 8 0v4" />
    <circle cx="12" cy="16" r="1.25" fill="currentColor" stroke="none" />
  </svg>
)

export const MonoGlobeIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <circle cx="12" cy="12" r="9" />
    <line x1="3" y1="12" x2="21" y2="12" />
    <path d="M12 3a14 14 0 0 0 0 18" />
    <path d="M12 3a14 14 0 0 1 0 18" />
  </svg>
)

export const MonoCrownIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="M3 18h18" />
    <path d="m4 18 2.5-11 5.5 4.5 5.5-4.5 2.5 11" />
    <circle cx="4" cy="6.5" r="1" fill="currentColor" stroke="none" />
    <circle cx="12" cy="4" r="1" fill="currentColor" stroke="none" />
    <circle cx="20" cy="6.5" r="1" fill="currentColor" stroke="none" />
  </svg>
)

// ── 2. Lounge Reactions ──────────────────────────────────────────────────────

export const MonoSaxophoneIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="M6 3v9a5 5 0 0 0 5 5h2a5 5 0 0 0 5-5v-1l3-1-1-3-2 1v-3h-2v5a3 3 0 0 1-3 3h-2a3 3 0 0 1-3-3V3H6z" />
    <circle cx="10" cy="7" r="0.9" fill="currentColor" stroke="none" />
    <circle cx="10" cy="10" r="0.9" fill="currentColor" stroke="none" />
  </svg>
)

export const MonoWineIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="M7 3h10v5a5 5 0 0 1-10 0V3z" />
    <line x1="12" y1="13" x2="12" y2="21" />
    <line x1="8" y1="21" x2="16" y2="21" />
  </svg>
)

export const MonoClapIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="M13.5 6.5a2.12 2.12 0 0 0-3 0L7 10a2.12 2.12 0 0 0 3 3l3.5-3.5" />
    <path d="M10.5 3.5a2.12 2.12 0 0 0-3 0L4 7a2.12 2.12 0 0 0 3 3l3.5-3.5" />
    <path d="M14 10l4 4a3 3 0 0 1-4.24 4.24L10 14.5" />
    <line x1="18" y1="4" x2="20" y2="2" />
    <line x1="20.5" y1="8" x2="22.5" y2="8" />
  </svg>
)

export const MonoSmileIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <circle cx="12" cy="12" r="9" />
    <path d="M8 14s1.5 2.5 4 2.5 4-2.5 4-2.5" />
    <line x1="9" y1="9.5" x2="9.01" y2="9.5" strokeWidth={2.5} />
    <line x1="15" y1="9.5" x2="15.01" y2="9.5" strokeWidth={2.5} />
  </svg>
)

// ── 3. Audio & Endpoints ─────────────────────────────────────────────────────

export const MonoHeadphonesIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="M3 14h3a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-5a9 9 0 0 1 18 0v5a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3" />
  </svg>
)

export const MonoSpeakerIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <rect x="5" y="3" width="14" height="18" rx="2" />
    <circle cx="12" cy="7" r="1.5" />
    <circle cx="12" cy="14" r="3.5" />
    <circle cx="12" cy="14" r="1" fill="currentColor" stroke="none" />
  </svg>
)

export const MonoBitPerfectIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <rect x="2.5" y="6" width="19" height="12" rx="6" />
    <circle cx="8" cy="12" r="2.2" fill="currentColor" stroke="none" />
    <polyline points="13 12 15 14 18 10" />
  </svg>
)

export const MonoDspActiveIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <rect x="2.5" y="6" width="19" height="12" rx="6" />
    <line x1="8" y1="14.5" x2="8" y2="9.5" />
    <line x1="12" y1="16" x2="12" y2="8" />
    <line x1="16" y1="13.5" x2="16" y2="10.5" />
  </svg>
)

// ── 4. Fullscreen, Meta & Status ─────────────────────────────────────────────

export const MonoSparkleIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="M12 2l2.4 6.6L21 11l-6.6 2.4L12 20l-2.4-6.6L3 11l6.6-2.4L12 2z" />
  </svg>
)

export const MonoLyricsIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <line x1="9" y1="7" x2="19" y2="7" />
    <line x1="9" y1="12" x2="19" y2="12" />
    <line x1="9" y1="17" x2="15" y2="17" />
    <path d="M5 6v3a2 2 0 0 1-2 2" />
    <path d="M5 14v3a2 2 0 0 1-2 2" />
  </svg>
)

export const MonoMasterRecordingIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <rect x="3" y="4" width="18" height="16" rx="2" />
    <path d="M7 12h2l1.5-4 3 8 1.5-4h2" />
  </svg>
)

export const MonoStarIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" />
  </svg>
)

// ── 5. Actions & General Utilities ───────────────────────────────────────────

export const MonoCloseIcon = ({ size = 18, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <line x1="18" y1="6" x2="6" y2="18" />
    <line x1="6" y1="6" x2="18" y2="18" />
  </svg>
)

export const MonoCheckIcon = ({ size = 18, strokeWidth = 2, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <polyline points="20 6 9 17 4 12" />
  </svg>
)

export const MonoPlayMiniIcon = ({ size = 16, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} stroke="none" fill={color || "currentColor"} className={className} style={style} {...rest}>
    <polygon points="7 4 19 12 7 20 7 4" />
  </svg>
)

export const MonoBroadcastIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <circle cx="12" cy="12" r="2.2" fill="currentColor" stroke="none" />
    <path d="M16.24 7.76a6 6 0 0 1 0 8.49" />
    <path d="M7.76 16.24a6 6 0 0 1 0-8.49" />
    <path d="M19.07 4.93a10 10 0 0 1 0 14.14" />
    <path d="M4.93 19.07a10 10 0 0 1 0-14.14" />
  </svg>
)

export const MonoClipboardIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2" />
    <rect x="8" y="2" width="8" height="4" rx="1" />
    <line x1="8" y1="11" x2="16" y2="11" />
    <line x1="8" y1="15" x2="13" y2="15" />
  </svg>
)

export const MonoSettingsIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <circle cx="12" cy="12" r="3" />
    <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" />
  </svg>
)

export const MonoTrendUpIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <polyline points="23 6 13.5 15.5 8.5 10.5 1 18" />
    <polyline points="17 6 23 6 23 12" />
  </svg>
)

export const MonoTagIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z" />
    <circle cx="7" cy="7" r="1.2" fill="currentColor" stroke="none" />
  </svg>
)

export const MonoWaveIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="M2 12c2.5-6 4.5-6 7 0s4.5 6 7 0 4.5-6 6 0" />
  </svg>
)

export const MonoAlertTriangleIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
    <line x1="12" y1="9" x2="12" y2="13" />
    <line x1="12" y1="17" x2="12.01" y2="17" strokeWidth={2.5} />
  </svg>
)

export const MonoPlaylistIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="M21 15V6a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h9" />
    <polygon points="10 9 14 12 10 15 10 9" fill="currentColor" stroke="none" />
    <path d="M17 19h4" />
  </svg>
)

export const MonoCollectionIcon = ({ size = 20, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <polygon points="12 2 2 7 12 12 22 7 12 2" />
    <polyline points="2 17 12 22 22 17" />
    <polyline points="2 12 12 17 22 12" />
  </svg>
)

export const MonoArrowLeftIcon = ({ size = 18, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <line x1="19" y1="12" x2="5" y2="12" />
    <polyline points="12 19 5 12 12 5" />
  </svg>
)

export const MonoChevronDownIcon = ({ size = 16, strokeWidth = 2, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <polyline points="6 9 12 15 18 9" />
  </svg>
)

export const MonoExternalLinkIcon = ({ size = 16, strokeWidth = 1.75, className, color, style, ...rest }: MonoIconProps) => (
  <svg {...defaultProps} width={size} height={size} strokeWidth={strokeWidth} stroke={color || "currentColor"} className={className} style={style} {...rest}>
    <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" />
    <polyline points="15 3 21 3 21 9" />
    <line x1="10" y1="14" x2="21" y2="3" />
  </svg>
)

// ── MonoIcon Namespace ───────────────────────────────────────────────────────

export const MonoIcon = {
  Flame: MonoFlameIcon,
  Sprout: MonoSproutIcon,
  Guitar: MonoGuitarIcon,
  Cup: MonoCupIcon,
  Trophy: MonoTrophyIcon,
  Lock: MonoLockIcon,
  Globe: MonoGlobeIcon,
  Crown: MonoCrownIcon,
  Saxophone: MonoSaxophoneIcon,
  Wine: MonoWineIcon,
  Clap: MonoClapIcon,
  Smile: MonoSmileIcon,
  Headphones: MonoHeadphonesIcon,
  Speaker: MonoSpeakerIcon,
  BitPerfect: MonoBitPerfectIcon,
  DspActive: MonoDspActiveIcon,
  Sparkle: MonoSparkleIcon,
  Lyrics: MonoLyricsIcon,
  MasterRecording: MonoMasterRecordingIcon,
  Star: MonoStarIcon,
  Close: MonoCloseIcon,
  Check: MonoCheckIcon,
  PlayMini: MonoPlayMiniIcon,
  Broadcast: MonoBroadcastIcon,
  Clipboard: MonoClipboardIcon,
  Settings: MonoSettingsIcon,
  TrendUp: MonoTrendUpIcon,
  Tag: MonoTagIcon,
  Wave: MonoWaveIcon,
  AlertTriangle: MonoAlertTriangleIcon,
  Playlist: MonoPlaylistIcon,
  Collection: MonoCollectionIcon,
  ArrowLeft: MonoArrowLeftIcon,
  ChevronDown: MonoChevronDownIcon,
  ExternalLink: MonoExternalLinkIcon,
}

export default MonoIcon
