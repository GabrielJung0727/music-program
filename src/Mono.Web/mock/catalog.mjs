// 데모 카탈로그. 실제 Core 의 CatalogStore.TrackView 와 같은 모양으로 만든다.
// 아티스트·앨범은 전부 지어낸 것이다 — 화면을 채워 보기 위한 자료일 뿐이다.

const LOCAL = 0, TIDAL = 1, QOBUZ = 2

/** 시드가 같으면 늘 같은 수열. 실행할 때마다 화면이 달라지면 캡처를 비교할 수 없다. */
export function rng(seed) {
  let s = 0
  for (const ch of String(seed)) s = (s * 31 + ch.charCodeAt(0)) >>> 0
  return () => {
    s = (s + 0x6d2b79f5) >>> 0
    let t = s
    t = Math.imul(t ^ (t >>> 15), t | 1)
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

const slug = (s) =>
  s.toLowerCase().normalize("NFKD").replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "")

// style: 커버 생성기의 템플릿, pal: [바탕, 주색, 보조색, 글자색]
const ALBUMS = [
  {
    title: "Night Ferry", artist: "Halden Quartet", year: 1961, label: "Bluewater", genres: ["Jazz", "Hard Bop"],
    rate: 96000, bits: 24, ext: "flac", dr: 13, style: "block", pal: ["#0b2545", "#13315c", "#8da9c4", "#eef4ed"],
    tracks: ["Night Ferry", "Harbor Lights at Two", "Dockside Blues", "Lantern Row", "Slow Boat, Fast Hands", "The Last Crossing"],
    notes: "1961년 겨울, 단 하루의 세션으로 녹음된 Halden Quartet의 대표작. 테너와 피아노가 주고받는 긴 호흡의 솔로가 앨범 전체를 끌고 간다. 이번 24/96 리마스터는 오리지널 3트랙 마스터 테이프에서 직접 옮겼다.",
    credits: "Elias Halden — tenor saxophone\nMarcus Bell — piano\nWalt Okoye — bass\nJoe Draper — drums\nRecorded at Kessel Sound, New Jersey",
  },
  {
    title: "Paper Lanterns", artist: "Mira Okafor Trio", year: 2019, label: "Northlight", genres: ["Jazz", "Piano Trio"],
    rate: 192000, bits: 24, ext: "flac", dr: 14, style: "sun", pal: ["#f4e9d8", "#e07a5f", "#3d405b", "#2b2d42"],
    tracks: ["Paper Lanterns", "Fig Tree Morning", "Small Hours", "A Letter Unsent", "Koi", "Rain on Corrugated Roofs", "Homeward", "Lullaby for Ada"],
    notes: "Mira Okafor가 자신의 트리오와 함께한 세 번째 스튜디오 앨범. 여백이 많은 편곡과 따뜻한 스튜디오 잔향이 특징이며, 24-Bit / 192kHz로 녹음됐다.",
    credits: "Mira Okafor — piano\nSven Aalto — double bass\nDede Mensah — drums\nEngineered by Hana Lindqvist",
  },
  {
    title: "Slow Cities", artist: "Theo Vance", year: 2022, label: "Northlight", genres: ["Jazz", "Contemporary"],
    dsd: 64, ext: "dsf", dr: 15, style: "bands", pal: ["#1b1b1e", "#c2b280", "#5c5346", "#f2efe6"],
    tracks: ["Lisbon, 6 a.m.", "Tram No. 28", "Slow Cities", "Courtyard", "Glass and Rain", "Late Train to Porto", "Afterimage"],
    notes: "트럼페터 Theo Vance의 DSD 원테이크 녹음. 편집 없이 한 방에서 연주한 그대로 담았다.",
    credits: "Theo Vance — trumpet, flugelhorn\nInês Duarte — guitar\nPaulo Reis — bass\nNuno Faria — drums",
  },
  {
    title: "After Hours at Kessel's", artist: "Ray Delacroix Quintet", year: 1958, label: "Bluewater", genres: ["Jazz", "Hard Bop"],
    rate: 96000, bits: 24, ext: "flac", dr: 12, style: "type", pal: ["#f2c14e", "#1a1a1a", "#c1272d", "#1a1a1a"],
    tracks: ["Kessel's Door", "Two for the Road", "Blue Smoke", "Minor Incident", "Cora", "Closing Time"],
    notes: "새벽 두 시, 클럽 문을 닫은 뒤 이어진 잼 세션의 기록.",
    credits: "Ray Delacroix — trumpet\nLou Abrams — alto saxophone\nHank Ellery — piano",
  },
  {
    title: "Bach: Goldberg Variations", artist: "Ilse Hartmann", year: 2017, label: "Aurora Classics", genres: ["Classical", "Baroque"],
    rate: 96000, bits: 24, ext: "flac", dr: 16, style: "frame", pal: ["#efe6d5", "#7a5c3e", "#2f2a25", "#2f2a25"],
    composer: "Johann Sebastian Bach", work: "Goldberg Variations, BWV 988",
    tracks: ["Aria", "Variatio 1", "Variatio 2", "Variatio 3. Canone all'Unisono", "Variatio 4", "Variatio 5", "Variatio 13", "Variatio 25. Adagio", "Variatio 30. Quodlibet", "Aria da Capo"],
    notes: "Ilse Hartmann의 두 번째 골드베르크. 모던 피아노로 연주했지만 페달을 거의 쓰지 않아 성부가 또렷하다.",
    credits: "Ilse Hartmann — piano (Steinway D)\nRecorded at Teldex Studio, Berlin",
  },
  {
    title: "Debussy · Ravel: String Quartets", artist: "Quatuor Lumière", year: 2020, label: "Aurora Classics", genres: ["Classical", "Chamber"],
    dsd: 128, ext: "dsf", dr: 15, style: "arcs", pal: ["#22333b", "#c6ac8f", "#eae0d5", "#eae0d5"],
    composer: "Claude Debussy", work: "String Quartet in G minor, Op. 10",
    tracks: ["Debussy: I. Animé et très décidé", "Debussy: II. Assez vif et bien rythmé", "Debussy: III. Andantino", "Debussy: IV. Très modéré", "Ravel: I. Allegro moderato", "Ravel: II. Assez vif", "Ravel: III. Très lent", "Ravel: IV. Vif et agité"],
    notes: "두 프랑스 현악 사중주를 DSD128로 담은 음반.",
    credits: "Quatuor Lumière\nRecorded at Salle Colonne, Paris",
  },
  {
    title: "Mahler: Symphony No. 5", artist: "Nordhavn Philharmonic", year: 2015, label: "Aurora Classics", genres: ["Classical", "Orchestral"],
    rate: 96000, bits: 24, ext: "flac", dr: 17, style: "frame", pal: ["#1d1d1b", "#b08d57", "#e9e2d0", "#e9e2d0"],
    composer: "Gustav Mahler", work: "Symphony No. 5 in C-sharp minor",
    tracks: ["I. Trauermarsch", "II. Stürmisch bewegt", "III. Scherzo", "IV. Adagietto", "V. Rondo-Finale"],
    long: true,
    notes: "Astrid Nyberg가 지휘한 실황 녹음.",
    credits: "Nordhavn Philharmonic\nAstrid Nyberg — conductor",
  },
  {
    title: "Schubert: Winterreise", artist: "Jonas Feld & Clara Weiss", year: 2018, label: "Aurora Classics", genres: ["Classical", "Lied"],
    rate: 96000, bits: 24, ext: "flac", dr: 15, style: "bands", pal: ["#dfe7ec", "#5b7083", "#1f2a33", "#1f2a33"],
    composer: "Franz Schubert", work: "Winterreise, D. 911",
    tracks: ["Gute Nacht", "Die Wetterfahne", "Gefrorne Tränen", "Der Lindenbaum", "Frühlingstraum", "Die Krähe", "Der Wegweiser", "Der Leiermann"],
    notes: "바리톤 Jonas Feld와 피아니스트 Clara Weiss의 겨울 나그네.",
    credits: "Jonas Feld — baritone\nClara Weiss — piano",
  },
  {
    title: "Glass Hours", artist: "Aiko Tanabe", year: 2021, label: "Kumo", genres: ["Ambient", "Electronic"],
    rate: 48000, bits: 24, ext: "flac", dr: 11, style: "mesh", pal: ["#0d1b2a", "#415a77", "#e0e1dd", "#e0e1dd"],
    tracks: ["First Light", "Glass Hours", "Vapor", "Kiri", "Stillwater", "A Room Without Corners", "Last Light"],
    notes: "모듈러 신시사이저와 필드 레코딩으로 만든 일곱 개의 긴 호흡.",
    credits: "Aiko Tanabe — synthesizers, tape, field recordings",
  },
  {
    title: "Low Tide Signals", artist: "Portside", year: 2016, label: "Kumo", genres: ["Electronic", "Downtempo"],
    rate: 44100, bits: 16, ext: "flac", dr: 9, style: "waves", pal: ["#003049", "#669bbc", "#fdf0d5", "#fdf0d5"],
    tracks: ["Signal One", "Breakwater", "Low Tide", "Foghorn", "Night Swim", "Pier 9", "Signal Two", "Undertow"],
    notes: "", credits: "",
  },
  {
    title: "Halogen", artist: "VEGA/9", year: 2023, label: "Static Bloom", genres: ["Electronic", "House"],
    rate: 44100, bits: 24, ext: null, dr: 8, style: "grid", pal: ["#111111", "#ff4d6d", "#ffd60a", "#ffffff"],
    source: TIDAL,
    tracks: ["Halogen", "Parallel Lines", "Sodium", "Afterglow", "Neon Tetra", "Coil", "Exit Sign"],
    notes: "", credits: "",
  },
  {
    title: "Small Weather", artist: "The Lanterns", year: 2020, label: "Static Bloom", genres: ["Indie", "Rock"],
    rate: 96000, bits: 24, ext: "flac", dr: 10, style: "sun", pal: ["#e9edc9", "#588157", "#344e41", "#344e41"],
    tracks: ["Front Porch", "Small Weather", "Honeycomb", "Ghost in the Carport", "Paper Route", "Second Summer", "Tin Roof", "Goodnight, Marianne"],
    notes: "", credits: "",
  },
  {
    title: "Salt & Static", artist: "Juno Reyes", year: 2024, label: "Static Bloom", genres: ["Indie", "Pop"],
    rate: 48000, bits: 24, ext: null, dr: 8, style: "split", pal: ["#ffb4a2", "#6d6875", "#e5989b", "#2b2d42"],
    source: QOBUZ,
    tracks: ["Salt", "Static", "Postcard From Nowhere", "Swimming Pool Blue", "Call Me Sometime", "Tangerine", "Low Battery", "Ocean Avenue"],
    notes: "", credits: "",
  },
  {
    title: "Marigold Motel", artist: "Harbor Lights", year: 2014, label: "Driftwood", genres: ["Indie", "Folk Rock"],
    rate: 44100, bits: 16, ext: "flac", dr: 11, style: "block", pal: ["#f4a261", "#e76f51", "#264653", "#264653"],
    tracks: ["Marigold Motel", "Vacancy", "Route 9", "Room 12", "Ice Machine", "Checkout"],
    notes: "", credits: "",
  },
  {
    title: "Velvet Arithmetic", artist: "Nia Calloway", year: 2022, label: "Goldroom", genres: ["Soul", "R&B"],
    rate: 96000, bits: 24, ext: "flac", dr: 9, style: "arcs", pal: ["#3c1642", "#086375", "#affc41", "#f7f7f2"],
    tracks: ["Velvet Arithmetic", "Honey & Smoke", "Count on Me (Interlude)", "Midnight Math", "Slow Dance Theory", "Gold Teeth", "Remainder", "Sum of Us"],
    notes: "", credits: "",
  },
  {
    title: "Sunday Kind of Blue", artist: "The Amber Room", year: 1973, label: "Goldroom", genres: ["Soul", "Funk"],
    rate: 96000, bits: 24, ext: "flac", dr: 13, style: "type", pal: ["#e36414", "#0f4c5c", "#fb8b24", "#fff8e8"],
    tracks: ["Sunday Kind of Blue", "Amber", "Get Up, Stay Up", "Corner Store", "Mama's Kitchen", "Long Way Home", "Easy Now"],
    notes: "", credits: "",
  },
  {
    key: "garden-3am", artistKey: "yoon-seoha",
    title: "새벽 세 시의 정원", artist: "윤서하", year: 2023, label: "달빛레코드", genres: ["Indie", "Folk"],
    rate: 96000, bits: 24, ext: "flac", dr: 12, style: "mesh", pal: ["#1f2041", "#4b3f72", "#ffc857", "#f6f1e7"],
    tracks: ["새벽 세 시의 정원", "창문 너머", "비 오는 날의 버스", "작은 방", "우리가 걷던 길", "잠들지 못한 밤", "봄이 오면"],
    notes: "윤서하의 첫 정규 앨범. 통기타와 목소리만으로 채운 일곱 곡.",
    credits: "윤서하 — 보컬, 기타\n한지우 — 첼로",
  },
  {
    key: "hangang-nocturne", artistKey: "lee-doyun-trio",
    title: "한강 야상곡", artist: "이도윤 트리오", year: 2021, label: "달빛레코드", genres: ["Jazz", "Piano Trio"],
    rate: 192000, bits: 24, ext: "flac", dr: 14, style: "waves", pal: ["#0a1128", "#034078", "#f4d58d", "#fefcfb"],
    tracks: ["한강 야상곡", "마포대교", "밤섬", "첫차", "노들", "새벽 안개"],
    notes: "서울의 밤을 주제로 한 피아노 트리오 앨범. 24-Bit / 192kHz 녹음.",
    credits: "이도윤 — 피아노\n박건 — 베이스\n최라온 — 드럼",
  },
  {
    key: "end-of-summer", artistKey: "dalmuri",
    title: "여름의 끝", artist: "달무리", year: 2019, label: "달빛레코드", genres: ["Indie", "Dream Pop"],
    rate: 44100, bits: 16, ext: "flac", dr: 9, style: "split", pal: ["#bde0fe", "#ffafcc", "#cdb4db", "#33415c"],
    tracks: ["여름의 끝", "파도 소리", "유리병 편지", "해질녘", "모래성", "마지막 불꽃놀이", "가을로"],
    notes: "", credits: "",
  },
  {
    title: "Río Abajo", artist: "Camila Ortega", year: 2018, label: "Driftwood", genres: ["Latin", "Folk"],
    rate: 96000, bits: 24, ext: "flac", dr: 13, style: "sun", pal: ["#fefae0", "#bc6c25", "#283618", "#283618"],
    tracks: ["Río Abajo", "La Casa Azul", "Naranjo", "Canción de Cuna", "Sal y Limón", "Tierra Adentro", "Despedida"],
    notes: "", credits: "",
  },
  {
    title: "The Long Field", artist: "Wren Abbott", year: 2017, label: "Driftwood", genres: ["Folk", "Singer-Songwriter"],
    rate: 88200, bits: 24, ext: "flac", dr: 14, style: "bands", pal: ["#d8c9a3", "#8a7650", "#40372a", "#2e271d"],
    tracks: ["The Long Field", "Barn Swallow", "Winter Wheat", "Kettle Song", "Ash & Elder", "Fencepost", "Thaw"],
    notes: "", credits: "",
  },
  {
    title: "Northern Rooms", artist: "Elias Brandt", year: 2020, label: "Northlight", genres: ["Modern Classical", "Piano"],
    rate: 192000, bits: 24, ext: "flac", dr: 16, style: "frame", pal: ["#e5e5e5", "#8d99ae", "#2b2d42", "#2b2d42"],
    composer: "Elias Brandt",
    tracks: ["Room I — North Window", "Room II — Stairwell", "Room III — Kitchen, Evening", "Room IV — The Empty Chair", "Room V — Attic", "Room VI — Hallway Light"],
    notes: "펠트를 덧댄 업라이트 피아노로 녹음한 여섯 개의 방.",
    credits: "Elias Brandt — piano",
  },
  {
    title: "Cathedral of Small Things", artist: "Sable Choir", year: 2022, label: "Aurora Classics", genres: ["Choral", "Modern Classical"],
    rate: 96000, bits: 24, ext: null, dr: 15, style: "arcs", pal: ["#f1faee", "#457b9d", "#1d3557", "#1d3557"],
    source: QOBUZ,
    tracks: ["Matins", "Thimble", "Moth Wing", "Salt Cellar", "Evensong", "Candle Stub"],
    notes: "", credits: "",
  },
  {
    title: "Tape Loops for a Quiet House", artist: "Odd Meridian", year: 2018, label: "Kumo", genres: ["Ambient", "Experimental"],
    rate: 48000, bits: 24, ext: null, dr: 12, style: "grid", pal: ["#2d3142", "#bfc0c0", "#ef8354", "#ffffff"],
    source: TIDAL,
    tracks: ["Loop 1 (Kitchen)", "Loop 2 (Stairs)", "Loop 3 (Radiator)", "Loop 4 (Window)", "Loop 5 (Sleep)"],
    notes: "", credits: "",
  },
]

const BIOS = {
  "Halden Quartet": "Halden Quartet은 1950년대 후반 뉴저지에서 결성된 하드밥 쿼텟이다. 테너 색소포니스트 Elias Halden을 중심으로 Bluewater 레이블에서 여섯 장의 앨범을 남겼다.",
  "Mira Okafor Trio": "Mira Okafor는 라고스에서 태어나 스톡홀름에서 활동하는 피아니스트다. 2014년 결성한 트리오로 Northlight에서 꾸준히 음반을 내고 있다.",
  "윤서하": "윤서하는 서울에서 활동하는 싱어송라이터다. 2023년 첫 정규 앨범 「새벽 세 시의 정원」으로 데뷔했다.",
  "이도윤 트리오": "피아니스트 이도윤이 이끄는 재즈 트리오. 서울의 풍경을 주제로 한 연작을 발표하고 있다.",
}

function buildCatalog() {
  const tracks = []
  const albums = []
  const artists = new Map()
  const base = Date.UTC(2026, 10, 14) // 2026-11-14

  ALBUMS.forEach((a, ai) => {
    const key = a.key ?? slug(a.title)
    const albumId = `al-${key}`
    const artistId = `ar-${a.artistKey ?? slug(a.artist)}`
    const source = a.source ?? LOCAL
    const r = rng(a.title)
    if (!artists.has(artistId)) {
      artists.set(artistId, { id: artistId, name: a.artist, bio: BIOS[a.artist] ?? null, alternateNames: [], genres: a.genres })
    }
    const addedAt = new Date(base - ai * 2.6 * 86400000 - r() * 86400000).toISOString()
    const album = {
      id: albumId, title: a.title, artistId, artist: a.artist, year: a.year, label: a.label,
      linerNotes: a.notes || null, credits: a.credits || null,
      style: a.style, pal: a.pal, genres: a.genres, trackIds: [],
    }
    a.tracks.forEach((title, ti) => {
      const id = `tr-${key}-${String(ti + 1).padStart(2, "0")}`
      const durationMs = a.long
        ? Math.round((560 + r() * 520) * 1000)
        : Math.round((150 + r() * 260) * 1000)
      tracks.push({
        id, title, albumId, artistId,
        artist: a.artist, artistAliases: [], album: a.title, year: a.year, label: a.label,
        sampleRate: a.dsd ? 2822400 * (a.dsd / 64) : a.rate,
        bitDepth: a.dsd ? 1 : a.bits,
        isDsd: !!a.dsd, dsdRate: a.dsd ?? null,
        durationMs, source,
        streamingQuality: source === LOCAL ? 0 : a.bits >= 24 ? 3 : 2,
        mergedLocalAndStreaming: false,
        genres: a.genres,
        composers: a.composer ? [a.composer] : [],
        workKey: a.work ? slug(a.work) : null,
        workTitle: a.work ?? null,
        hasLyrics: ti % 3 === 0,
        hasLocal: source === LOCAL,
        fileExtension: source === LOCAL ? a.ext : null,
        artUrl: `/api/art/${id}`,
        addedAt,
        // QualityPolicyEngine.Badge 와 같은 형식: "24/96000 · Bit-perfect Local"
        badge: `${a.dsd ? `DSD${a.dsd}` : `${a.bits}/${a.rate}`} · Bit-perfect ${source === LOCAL ? "Local" : source === TIDAL ? "Tidal HiRes" : "Qobuz HiRes"}`,
        trackNo: ti + 1,
      })
      album.trackIds.push(id)
    })
    albums.push(album)
  })

  return { tracks, albums, artists: [...artists.values()] }
}

export const CATALOG = buildCatalog()
export const trackById = new Map(CATALOG.tracks.map((t) => [t.id, t]))
export const albumById = new Map(CATALOG.albums.map((a) => [a.id, a]))
export const artistById = new Map(CATALOG.artists.map((a) => [a.id, a]))

/** Core 가 와이어에 싣는 필드만 남긴다. */
export function trackView(t) {
  const { trackNo, ...rest } = t
  return rest
}

export function snapshotTrack(t) {
  return {
    id: t.id, title: t.title, albumId: t.albumId, artistId: t.artistId,
    artistName: t.artist, albumTitle: t.album,
    sampleRate: t.sampleRate, bitDepth: t.bitDepth, channels: 2,
    isDsd: t.isDsd, dsdRate: t.dsdRate, durationMs: t.durationMs,
    source: t.source, streamingQuality: t.streamingQuality,
    mergedLocalAndStreaming: false, hasLocal: t.hasLocal,
    fileExtension: t.fileExtension, badge: t.badge, artUrl: t.artUrl,
  }
}

// ── 가사 ──────────────────────────────────────────────────────────────────────

const LYRICS = {
  "tr-garden-3am-01": [
    "새벽 세 시, 아무도 없는 정원에",
    "어제 심은 말들이 조용히 자라고",
    "나는 물뿌리개를 들고 서서",
    "네 이름을 한 번 더 불러 본다",
    "",
    "달은 담장 위에 걸터앉아",
    "우리가 놓친 계절을 세고",
    "창문마다 꺼진 불빛들이",
    "천천히 별이 되어 간다",
    "",
    "괜찮아, 아침은 늦게 와도 돼",
    "여기 이 정원에서는",
    "시든 것들도 다시 피어나니까",
    "새벽 세 시, 너와 나의 정원에",
  ],
}

const GENERIC = [
  "The harbor lights go out by two",
  "and every boat forgets its name",
  "I count the windows still awake",
  "and yours is burning just the same",
  "",
  "Slow down, the city isn't going anywhere",
  "Slow down, there's a late train in the rain",
  "We could be the last ones on the platform",
  "watching all the signals change",
  "",
  "Paper lanterns on the water",
  "carry everything we said",
  "Let them drift a little farther",
  "let the morning come instead",
]

export function lyricsFor(track) {
  if (!track.hasLyrics) return []
  const lines = LYRICS[track.id] ?? GENERIC
  const step = Math.max(6000, Math.floor((track.durationMs - 30000) / lines.length))
  return lines.map((text, i) => ({ timeMs: 12000 + i * step, text }))
}
