// Mono Control — 화면과 명령만. 오디오는 Output 노드에서 난다.
const peerId = localStorage.getItem("mono.peer") || ("web-" + Math.random().toString(16).slice(2, 8));
localStorage.setItem("mono.peer", peerId);
const displayName = localStorage.getItem("mono.name") || "listener";
const ALLOWED_EMOJI = ["❤️", "🎉", "👏", "🔥"];

function initTheme() {
  const saved = localStorage.getItem("mono.theme");
  if (saved) document.documentElement.dataset.theme = saved;
}
function cycleTheme() {
  const cur = localStorage.getItem("mono.theme") || "system";
  const next = cur === "system" ? "light" : cur === "light" ? "dark" : "system";
  if (next === "system") {
    localStorage.removeItem("mono.theme");
    delete document.documentElement.dataset.theme;
  } else {
    localStorage.setItem("mono.theme", next);
    document.documentElement.dataset.theme = next;
  }
}
initTheme();

// ── 첫 실행 온보딩 ───────────────────────────────────
const OB_STEPS = 6;
let obIndex = 0;

function obShow(i) {
  obIndex = i;
  document.querySelectorAll(".ob-step").forEach(s => s.hidden = Number(s.dataset.step) !== i);
  document.querySelectorAll("[data-dot]").forEach(d => d.classList.toggle("on", Number(d.dataset.dot) <= i));
}

function initOnboarding(force) {
  if (!force && localStorage.getItem("mono.onboarded")) return;
  document.getElementById("onboarding").hidden = false;
  const nameInput = document.getElementById("obName");
  if (nameInput && !nameInput.value) nameInput.value = localStorage.getItem("mono.name") || "";
  obShow(0);
}

function closeOnboarding() {
  localStorage.setItem("mono.onboarded", "1");
  document.getElementById("onboarding").hidden = true;
}

function wireOnboarding() {
  document.querySelectorAll(".ob-next").forEach(b => b.onclick = () => {
    if (obIndex === 1) {
      localStorage.setItem("mono.name", $("obName").value.trim() || "listener");
    }
    obShow(Math.min(OB_STEPS - 1, obIndex + 1));
  });
  document.querySelectorAll(".ob-skip").forEach(b => b.onclick = () => obShow(Math.min(OB_STEPS - 1, obIndex + 1)));
  document.querySelector(".ob-finish").onclick = closeOnboarding;

  $("obScan").onclick = () => {
    if (!hub) return note("연결 중입니다. 잠시 후 다시 시도하세요");
    send({ type: "scan_library", path: $("obLibPath").value.trim() || null });
  };
  $("obZoneCreate").onclick = () => {
    const name = $("obZoneName").value.trim();
    if (!name) return;
    if (!hub) return note("연결 중입니다. 잠시 후 다시 시도하세요");
    send({ type: "create_zone", text: name });
    $("obZoneName").value = "";
  };
  $("obTidal").onclick = () => hub && send({ type: "link_streaming", provider: 1, token: "demo-token", displayName: "Tidal" });
  $("obQobuz").onclick = () => hub && send({ type: "link_streaming", provider: 2, token: "demo-token", displayName: "Qobuz" });
  $("obOutputCmd").textContent = "dotnet run --project src/Mono.Output -- --room=<룸ID>";
  $("btnOnboard").onclick = () => initOnboarding(true);
}

let hub = null;
let state = null;          // 마지막 room_state 스냅샷
let roomId = null;
let catalog = [];
let clock = { baseMedia: 0, baseLocal: Date.now(), playing: false, duration: 0 };
let zones = [];
let knownEndpoints = [];
let currentArtistId = null;

const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const mmss = (ms) => {
  const total = Math.max(0, Math.round(ms / 1000));
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, "0")}`;
};

wireOnboarding();
initOnboarding();

async function start() {
  hub = new signalR.HubConnectionBuilder()
    .withUrl(`/hub?peer=${peerId}&name=${encodeURIComponent(displayName)}`)
    .withAutomaticReconnect()
    .build();
  hub.on("msg", onMsg);
  await hub.start();
  await send({ type: "hello", peerId, role: "control", displayName });
  wire();
  send({ type: "list_zones" });
  send({ type: "endpoints" });
  setInterval(tick, 120);
}

function send(payload) {
  if (!hub) return Promise.resolve();
  return hub.invoke("Send", { roomId, ...payload }).catch((e) => note(String(e)));
}

function note(text) {
  $("side").textContent = typeof text === "string" ? text : JSON.stringify(text, null, 2);
}

// ── 수신 ─────────────────────────────────────────────
function onMsg(msg) {
  switch (msg.type) {
    case "room_state":
      if (!msg.body) return;
      state = JSON.parse(msg.body);
      roomId = state.id;
      clock = {
        baseMedia: state.mediaTimeMs,
        baseLocal: Date.now(),
        playing: state.playing,
        duration: state.durationMs
      };
      render();
      break;
    case "timeline":
      clock = {
        baseMedia: msg.mediaTimeMs ?? clock.baseMedia,
        baseLocal: Date.now(),
        playing: msg.playing ?? clock.playing,
        duration: msg.durationMs ?? clock.duration
      };
      break;
    case "catalog": {
      try { catalog = JSON.parse(msg.body); } catch { catalog = []; }
      const libSummary = `${catalog.length}곡 · 로컬 ${catalog.filter(t => t.hasLocal).length} · 스트리밍 ${catalog.filter(t => t.source !== 0).length}`;
      $("libMeta").textContent = libSummary;
      $("obLibMeta").textContent = libSummary;
      renderCatalog();
      break;
    }
    case "list_rooms":
      renderRooms(JSON.parse(msg.body || "[]"));
      break;
    case "history":
      renderVault("청음 히스토리", JSON.parse(msg.body || "[]").map(h => ({
        title: `${h.artist ? h.artist + " — " : ""}${h.title}`,
        meta: `${new Date(h.heardAt).toLocaleString()} · ${h.roomName || "개인"} ${h.completed ? "· 완청" : ""}`,
        actions: [{ label: "큐에 추가", cmd: { type: "load_playlist", trackId: h.trackId } }]
      })));
      break;
    case "playlists":
      renderVault("플레이리스트", JSON.parse(msg.body || "[]").map(p => ({
        title: p.title,
        meta: `${p.tracks.length}곡 · ${new Date(p.createdAt).toLocaleString()}`,
        actions: [
          { label: "큐에 싣기", cmd: { type: "load_playlist", playlistId: p.id } },
          { label: "큐 교체", cmd: { type: "load_playlist", playlistId: p.id, flag: true } },
          { label: "M3U 내려받기", href: `/api/m3u/${p.id}` }
        ]
      })));
      break;
    case "archives":
      renderVault("세션 아카이브", JSON.parse(msg.body || "[]").map(a => ({
        title: a.roomName,
        meta: `${new Date(a.startedAt).toLocaleString()} → ${new Date(a.endedAt).toLocaleTimeString()} · ${a.tracks.length}곡 · 핀 ${a.pins}`,
        body: a.tracks.map(t => `${t.highlight ? "♥ " : "· "}${t.title}`).join("\n"),
        actions: [
          { label: "하이라이트 플레이리스트", cmd: { type: "create_playlist", archiveId: a.id } },
          { label: "큐에 싣기", cmd: { type: "load_playlist", archiveId: a.id } },
          { label: "세션 요약", href: `/api/session/${a.id}` }
        ]
      })));
      break;
    case "endpoints":
      knownEndpoints = JSON.parse(msg.body || "[]");
      renderEndpoints(knownEndpoints);
      renderZoneMemberPicker();
      break;
    case "list_zones":
      zones = JSON.parse(msg.body || "[]");
      renderZones();
      break;
    case "wiki_bio":
      renderWikiBio(msg);
      break;
    case "graph":
      renderGraphDetail(JSON.parse(msg.body || "{}"));
      break;
    case "pairing_issued":
      note(`페어링 코드 ${msg.pairingCode}\n원격 Control에서: redeem ${msg.pairingCode}\n${msg.body || ""}`);
      break;
    case "export_m3u":
    case "share_session":
      note(msg.body || "");
      break;
    case "archive":
      note(msg.body || "");
      state = null;
      roomId = null;
      $("roomChip").textContent = "룸 없음";
      send({ type: "list_rooms" });
      break;
    case "error":
      note("⚠ " + msg.error);
      break;
    default:
      if (msg.error) note("⚠ " + msg.error);
      else if (msg.body) note(msg.body);
  }
}

// ── 로컬 틱: 상태 사이의 재생 위치를 부드럽게 ────────
function tick() {
  if (!state) return;
  const media = clock.playing ? clock.baseMedia + (Date.now() - clock.baseLocal) : clock.baseMedia;
  const duration = clock.duration || state.durationMs || 1;
  const ratio = Math.max(0, Math.min(1, media / duration));
  $("seekfill").style.width = (ratio * 100) + "%";
  $("elapsed").textContent = mmss(media);
  $("remain").textContent = "-" + mmss(Math.max(0, duration - media));
  highlightLyrics(media);
  state.mediaTimeMs = media;
  tickAutoplayCountdown();
}

function tickAutoplayCountdown() {
  const card = $("autoplayCard");
  if (card.hidden || !card.dataset.deadline) return;
  const remaining = Math.max(0, Math.round((Number(card.dataset.deadline) - Date.now()) / 1000));
  $("autoplayCountdown").textContent = remaining + "s";
}

function highlightLyrics(media) {
  const lines = state?.lyrics || [];
  if (!lines.length) return;
  let active = -1;
  for (let i = 0; i < lines.length; i++) if (lines[i].timeMs <= media) active = i;
  const nodes = $("lyrics").children;
  for (let i = 0; i < nodes.length; i++) {
    const on = i === active;
    if (on !== nodes[i].classList.contains("on")) {
      nodes[i].classList.toggle("on", on);
      if (on) nodes[i].scrollIntoView({ block: "center", behavior: "smooth" });
    }
  }
}

// ── 렌더 ─────────────────────────────────────────────
function render() {
  if (!state) return;
  const track = state.currentTrack;
  if (track?.artistId !== currentArtistId) {
    currentArtistId = track?.artistId || null;
    $("wikiBio").innerHTML = "";
  }
  $("badge").textContent = state.pathBadge || "Idle";
  $("badge").classList.toggle("warn", !!state.srcApplied);
  $("roomChip").textContent = `${state.name} · ${modeName(state.mode)} · ${state.sourceMode === 0 ? "Clock-sync" : "Fan-out"}`;
  $("title").textContent = track?.title || "큐가 비어 있습니다";
  $("artist").textContent = [track?.artistName, track?.albumTitle].filter(Boolean).join(" — ");
  $("format").textContent = track
    ? `${track.badge}${state.dspEnabled ? " · DSP " + dspName(state.dspPreset) : ""}${state.dspLocked ? " (잠금)" : ""}`
    : "";
  $("art").innerHTML = track?.artUrl
    ? `<img src="${track.artUrl}" alt="" loading="lazy" />`
    : "♪";

  $("lyrics").innerHTML = (state.lyrics || []).length
    ? state.lyrics.map(l => `<p data-t="${l.timeMs}">${esc(l.text)}</p>`).join("")
    : `<p class="dim">가사 없음 — 라이너 노트를 보세요</p>`;

  $("seekNote").textContent = state.mode === 3
    ? "Audiophile: 구간 시킹 금지 (곡 단위 이동만)"
    : state.seekingAllowed ? "" : "호스트가 시킹을 잠갔습니다";

  // 큐
  $("queue").innerHTML = (state.queue || []).map((q, i) => `
    <li class="${i === state.queueIndex ? "now" : ""}">
      <span class="qart">${q.artUrl ? `<img src="${q.artUrl}" loading="lazy" alt="">` : "♪"}</span>
      <span class="qmain"><b>${esc(q.title)}</b><small>${esc(q.artist || "")} · ${esc(q.badge)}</small></span>
      <span class="qtime">${mmss(q.durationMs)}</span>
      <span class="qbtns">
        <button data-jump="${i}" title="이 곡으로">▶</button>
        <button data-move="${i}" data-d="-1" title="위로">↑</button>
        <button data-move="${i}" data-d="1" title="아래로">↓</button>
        <button data-rm="${i}" title="빼기">×</button>
      </span>
    </li>`).join("");

  $("requests").innerHTML = (state.requests || []).map(r => `
    <div class="card">
      <b>${esc(r.title)}</b><small>${esc(r.fromName)} 요청</small>
      <span class="qbtns"><button data-ap="${r.id}">승인</button><button data-rj="${r.id}">거절</button></span>
    </div>`).join("") || `<p class="dim">요청 없음</p>`;

  // 소셜
  $("chatlog").innerHTML = (state.chat || []).map(c =>
    `<div><b>${esc(c.peerName)}</b> ${esc(c.text)}</div>`).join("");
  $("chatlog").scrollTop = $("chatlog").scrollHeight;
  $("pins").innerHTML = (state.pins || []).map(p => `
    <div class="card ${p.onCurrentTrack ? "" : "dim"}">
      <button class="pinjump" data-pin="${p.id}">${mmss(p.mediaTimeMs)}</button>
      ${esc(p.text)} <small>${esc(p.peerName)}</small>
      <button class="x" data-unpin="${p.id}">×</button>
    </div>`).join("") || `<p class="dim">핀 없음</p>`;
  $("pinmarks").innerHTML = (state.pins || []).filter(p => p.onCurrentTrack).map(p =>
    `<i style="left:${Math.min(100, 100 * p.mediaTimeMs / (state.durationMs || 1))}%" title="${esc(p.text)}"></i>`).join("");
  renderHeatmap(state.heatmap || [], state.durationMs || 0);
  renderReactionCounts(state.reactionCounts || {});
  renderAutoplay(state.autoplay);

  // 라이너 · 관계도
  $("liner").innerHTML = [
    state.album ? `<h3>${esc(state.album.title)}</h3>` : "",
    state.album?.label || state.album?.year ? `<p class="meta">${esc(state.album.label || "")} ${state.album.year || ""}</p>` : "",
    state.linerNotes ? `<p>${esc(state.linerNotes)}</p>` : "",
    state.credits ? `<p class="meta">크레딧: ${esc(state.credits)}</p>` : "",
    state.artist?.bio ? `<p class="meta">${esc(state.artist.bio)}</p>` : ""
  ].join("");
  $("albumTracks").innerHTML = (state.albumTracks || []).map(t =>
    `<div class="card" data-add="${t.id}"><b>${esc(t.title)}</b><small>${esc(t.badge)}</small></div>`).join("");
  $("graph").innerHTML = [
    ...(state.artist ? [`<button class="card" data-graph="${state.artist.id}">${esc(state.artist.name)} 그래프</button>`] : []),
    ...(state.relatedArtists || []).map(a =>
      `<div class="card"><b>${esc(a.name)}</b>
        <span class="qbtns"><button data-graph="${a.id}">그래프</button><button data-follow="${a.id}">따라가기</button></span></div>`)
  ].join("");

  // 기기 · 멤버
  $("outputs").innerHTML = (state.outputs || []).map(o => `
    <div class="card">
      <b>${esc(o.displayName)}</b> ${o.spectator ? `<span class="tag warn">참관</span>` : `<span class="tag">${o.exclusiveMode ? "Exclusive" : "Shared"}</span>`}
      <small>${esc(o.badge)}${o.note ? " · " + esc(o.note) : ""}</small>
      <small>${o.stats ? `offset ${o.stats.offsetMs.toFixed(1)}ms · jitter ${o.stats.jitterMs.toFixed(1)}ms · buffer ${o.stats.bufferMs}ms · resync ${o.stats.resyncs} ${o.stats.locked ? "· 락" : ""}` : "측정 대기"}</small>
      <div class="row tight">
        <input type="range" min="0" max="100" value="${o.volumePercent}" data-vol="${o.peerId}" />
        <span class="tag">${o.volumePercent}%</span>
        <span class="tag">${o.hardwareVolume ? "HW" : "SW"}</span>
      </div>
    </div>`).join("") || `<p class="dim">붙어 있는 엔드포인트 없음 — dotnet run --project src/Mono.Output -- --room=${state.id}</p>`;

  $("members").innerHTML = (state.members || []).map(m => `
    <div class="card">
      <b>${esc(m.name)}</b> <span class="tag">${roleName(m.role)}</span>
      ${m.peerId === state.hostPeerId ? `<span class="tag gold">호스트</span>` : ""}
      <span class="qbtns">
        <button data-dj="${m.peerId}">DJ</button>
        <button data-host="${m.peerId}">호스트 이양</button>
        <button data-kick="${m.peerId}">킥</button>
      </span>
    </div>`).join("");

  // 호스트 폼 상태 반영
  $("dsp").value = String(state.dspPreset);
  $("policy").value = String(state.qualityPolicy);
  $("sourceMode").value = String(state.sourceMode);
  $("maxMembers").value = state.maxMembers;
  $("retention").value = state.commentRetentionDays;
  $("inviteMeta").textContent = state.inviteCode
    ? `코드 ${state.inviteCode} · ${state.inviteExpiresAt ? new Date(state.inviteExpiresAt).toLocaleString() + "까지" : "무기한"}`
    : "초대 코드 없음";
  toggleFlag("seek", state.seekingAllowed);
  toggleFlag("comments", state.commentsAllowed);
  toggleFlag("chat", state.chatCollapsed);
  toggleFlag("auto_advance", state.autoAdvance);
  toggleFlag("smart_autoplay", state.smartAutoplay);
  toggleFlag("dsp_lock", state.dspLocked);
  toggleFlag("follow_host", state.followHostView);
  $("btnAnon").classList.toggle("on", !!state.anonymizeArchive);
  $("btnConsent").classList.toggle("on", !!state.archiveDefaultConsent);
  $("btnCloud").classList.toggle("on", !!state.cloudSyncOptIn);
  $("btnLockQ").classList.toggle("on", !!state.queueLocked);
  $("btnPlay").classList.toggle("on", !!state.playing);

  // 동기화 요약
  const stats = (state.outputs || []).map(o => o.stats).filter(Boolean);
  $("sync").textContent = stats.length
    ? `sync ±${Math.max(...stats.map(s => Math.abs(s.offsetMs))).toFixed(1)}ms · jitter ${Math.max(...stats.map(s => s.jitterMs)).toFixed(1)}ms · buf ${Math.max(...stats.map(s => s.bufferMs))}ms`
    : "sync — (엔드포인트 대기)";

  bindRoomActions();
  renderCatalog();
}

function toggleFlag(name, on) {
  const b = document.querySelector(`[data-flag="${name}"]`);
  if (b) b.classList.toggle("on", !!on);
}

function bindRoomActions() {
  const on = (sel, handler) => document.querySelectorAll(sel).forEach(el => el.onclick = () => handler(el));
  on("[data-jump]", el => send({ type: "jump_to", index: Number(el.dataset.jump) }));
  on("[data-move]", el => send({ type: "move_queue", index: Number(el.dataset.move), delta: Number(el.dataset.d) }));
  on("[data-rm]", el => send({ type: "remove_queue", index: Number(el.dataset.rm) }));
  on("[data-ap]", el => send({ type: "approve_request", text: el.dataset.ap }));
  on("[data-rj]", el => send({ type: "reject_request", text: el.dataset.rj }));
  on("[data-pin]", el => send({ type: "seek_pin", text: el.dataset.pin }));
  on("[data-unpin]", el => send({ type: "remove_pin", text: el.dataset.unpin }));
  on("[data-follow]", el => send({ type: "follow_artist", text: el.dataset.follow }));
  on("[data-graph]", el => send({ type: "graph", text: el.dataset.graph }));
  on("[data-add]", el => enqueue(el.dataset.add));
  on("[data-dj]", el => send({ type: "set_role", targetPeerId: el.dataset.dj, member: 1 }));
  on("[data-host]", el => send({ type: "transfer_host", targetPeerId: el.dataset.host }));
  on("[data-kick]", el => send({ type: "kick", targetPeerId: el.dataset.kick }));
  document.querySelectorAll("[data-vol]").forEach(el => {
    el.onchange = () => send({ type: "set_volume", targetPeerId: el.dataset.vol, volume: Number(el.value) });
  });
}

function renderCatalog() {
  const q = ($("search").value || "").toLowerCase();
  const items = catalog.filter(t => !q ||
    `${t.title} ${t.artist} ${t.album} ${t.label || ""} ${(t.artistAliases || []).join(" ")}`.toLowerCase().includes(q));
  $("grid").innerHTML = items.slice(0, 400).map(t => `
    <div class="tile" data-id="${t.id}" title="${esc(t.badge)}">
      <div class="cover">${t.artUrl ? `<img src="${t.artUrl}" loading="lazy" alt="">` : "♪"}</div>
      <b>${esc(t.title)}</b>
      <small>${esc(t.artist || "")}</small>
      <small class="dim">${esc(t.badge)}</small>
    </div>`).join("");
  document.querySelectorAll("#grid .tile").forEach(el => el.onclick = () => enqueue(el.dataset.id));
}

function enqueue(trackId) {
  if (!roomId) return note("먼저 룸을 만들거나 참여하세요");
  // Host Queue / Audiophile에서 게스트는 자동으로 요청으로 넘어간다.
  const guestOnly = state && state.hostPeerId !== peerId && (state.mode === 2 || state.mode === 3 || state.queueLocked);
  send({ type: guestOnly ? "request_track" : "enqueue", trackId });
}

function renderRooms(rooms) {
  $("roomList").innerHTML = rooms.map(r => `
    <div class="card">
      <b>${esc(r.name)}</b> <span class="tag">${modeName(r.mode)}</span>
      <small>${esc(r.host)} · ${r.members}명 · 엔드포인트 ${r.outputs} · 큐 ${r.queued}</small>
      <small class="dim">${esc(r.badge)} · ${r.id}</small>
      <button data-join="${r.id}">${r.needsInvite ? "코드로 참여" : "참여"}</button>
    </div>`).join("") || `<p class="dim">열린 라운지가 없습니다</p>`;
  document.querySelectorAll("[data-join]").forEach(el => el.onclick = () =>
    send({ type: "join_room", roomId: el.dataset.join, inviteCode: $("invite").value || null }));
}

function renderEndpoints(list) {
  const html = list.map(e => `
    <div class="card ${e.online ? "" : "dim"}">
      <b>${esc(e.displayName)}</b> <span class="tag">${e.online ? "온라인" : "오프라인"}</span>
      <small>${e.maxBitDepth}/${e.maxSampleRate}${e.supportsDsd ? " · DSD" : ""} · ${e.exclusiveMode ? "Exclusive" : "Shared"} · ${e.latencyMs}ms</small>
      <small class="dim">${esc(e.device || "")} · ${new Date(e.lastSeen).toLocaleString()}</small>
    </div>`).join("") || `<p class="dim">등록된 기기 없음</p>`;
  $("endpoints").innerHTML = html;
  $("obEndpoints").innerHTML = html;
}

// ── 반응 히트맵 · 스마트 오토플레이 · 존 · 위키 ─────────
function renderHeatmap(hits, durationMs) {
  const el = $("heatmap");
  if (!durationMs || hits.length === 0) { el.innerHTML = ""; return; }
  const bucket = 10_000;
  const bucketCount = Math.max(1, Math.ceil(durationMs / bucket));
  const byBucket = new Map(hits.map(h => [Math.floor(h.bucketMs / bucket), h.count]));
  const max = Math.max(1, ...hits.map(h => h.count));
  let bars = "";
  for (let i = 0; i < bucketCount; i++) {
    const count = byBucket.get(i) || 0;
    const pct = Math.max(6, Math.round((count / max) * 100));
    bars += `<i style="height:${pct}%;opacity:${count ? 0.35 + 0.65 * (count / max) : .12}" title="${count ? count + "회 반응" : ""}"></i>`;
  }
  el.innerHTML = bars;
}

function renderReactionCounts(counts) {
  ALLOWED_EMOJI.forEach(emoji => {
    const b = document.querySelector(`[data-count="${emoji}"]`);
    if (b) b.textContent = counts[emoji] || 0;
  });
  $("reactMeta").textContent = "곡당 최대 3번";
}

function renderAutoplay(autoplay) {
  const card = $("autoplayCard");
  if (!autoplay) { card.hidden = true; return; }
  card.hidden = false;
  card.dataset.deadline = autoplay.deadlineUnixMs || "";
  $("autoplayChoices").innerHTML = autoplay.candidates.map(t => `
    <button data-choose="${t.id}" class="${autoplay.chosenId === t.id ? "chosen" : ""}">
      <b>${esc(t.title)}</b>
      <small>${esc(t.artistName || "")}</small>
    </button>`).join("");
  document.querySelectorAll("[data-choose]").forEach(el =>
    el.onclick = () => send({ type: "choose_autoplay", trackId: el.dataset.choose }));
}

function renderZones() {
  $("zones").innerHTML = zones.map(z => `
    <div class="card">
      <b>${esc(z.name)}</b>
      <span class="qbtns">
        <button data-zone-mode="${z.id}" data-mode="${z.mode === 0 ? 1 : 0}">${z.mode === 0 ? "Sync" : "Independent"}</button>
        <button data-zone-del="${z.id}" class="ghost">삭제</button>
      </span>
      <div class="list">
        ${z.members.map(m => `
          <div class="row tight">
            <span class="tag ${m.online ? "gold" : ""}">${esc(m.name)}</span>
            <button data-zone-rm="${z.id}" data-peer="${m.peerId}" class="ghost">빼기</button>
          </div>`).join("") || `<p class="dim">기기 없음</p>`}
      </div>
      <div class="row tight">
        <select data-zone-add="${z.id}">
          <option value="">+ 기기 추가</option>
          ${knownEndpoints.filter(e => !z.members.some(m => m.peerId === e.peerId))
            .map(e => `<option value="${e.peerId}">${esc(e.displayName)}${e.online ? "" : " (오프라인)"}</option>`).join("")}
        </select>
      </div>
    </div>`).join("") || `<p class="dim">존이 없습니다 — 여러 출력기기를 묶어 함께 재생하세요</p>`;

  document.querySelectorAll("[data-zone-mode]").forEach(el =>
    el.onclick = () => send({ type: "set_zone_mode", zoneId: el.dataset.zoneMode, zoneMode: Number(el.dataset.mode) }));
  document.querySelectorAll("[data-zone-del]").forEach(el =>
    el.onclick = () => send({ type: "delete_zone", zoneId: el.dataset.zoneDel }));
  document.querySelectorAll("[data-zone-rm]").forEach(el =>
    el.onclick = () => send({ type: "zone_remove_member", zoneId: el.dataset.zoneRm, targetPeerId: el.dataset.peer }));
  document.querySelectorAll("[data-zone-add]").forEach(el =>
    el.onchange = () => { if (el.value) send({ type: "zone_add_member", zoneId: el.dataset.zoneAdd, targetPeerId: el.value }); });
}

function renderZoneMemberPicker() { renderZones(); }

function renderWikiBio(msg) {
  if (!msg.ok || !msg.body) {
    $("wikiBio").innerHTML = `<p class="dim">위키백과에서 이력을 찾지 못했습니다</p>`;
    return;
  }
  const w = JSON.parse(msg.body);
  $("wikiBio").innerHTML = `
    ${w.thumbnailUrl ? `<img src="${w.thumbnailUrl}" alt="" />` : ""}
    <h3>${esc(w.title)}</h3>
    <p>${esc(w.extract)}</p>
    <a class="source" href="${w.sourceUrl}" target="_blank" rel="noopener">출처: 위키백과 (${w.lang})</a>`;
}

function renderGraphDetail(g) {
  if (g.error) return note(g.error);
  $("graphDetail").innerHTML = `
    <h3>${esc(g.artist.name)}</h3>
    <p class="meta">${esc(g.artist.bio || "")}</p>
    <p><b>앨범</b> ${g.albums.map(a => `${esc(a.title)}${a.year ? " (" + a.year + ")" : ""}`).join(", ")}</p>
    <p><b>연관</b> ${(g.related || []).map(a => `<button class="link" data-graph="${a.id}">${esc(a.name)}</button>`).join(" ")}</p>
    <p><b>같은 시대</b> ${(g.neighbours || []).map(a => `<button class="link" data-graph="${a.id}">${esc(a.name)}</button>`).join(" ")}</p>
    <p><b>트랙</b></p>
    ${g.tracks.map(t => `<div class="card" data-add="${t.id}">${esc(t.title)} <small>${esc(t.badge)}</small></div>`).join("")}`;
  bindRoomActions();
}

function renderVault(title, items) {
  $("vault").innerHTML = `<h2>${esc(title)}</h2>` + items.map((it, i) => `
    <div class="card">
      <b>${esc(it.title)}</b><small>${esc(it.meta)}</small>
      ${it.body ? `<pre class="mini">${esc(it.body)}</pre>` : ""}
      <span class="qbtns">${it.actions.map((a, j) => a.href
        ? `<a class="btnlink" href="${a.href}" target="_blank" rel="noopener">${esc(a.label)}</a>`
        : `<button data-vault="${i}-${j}">${esc(a.label)}</button>`).join("")}</span>
    </div>`).join("") || `<p class="dim">비어 있습니다</p>`;
  document.querySelectorAll("[data-vault]").forEach(el => {
    const [i, j] = el.dataset.vault.split("-").map(Number);
    el.onclick = () => send(items[i].actions[j].cmd);
  });
}

const modeName = (m) => ["Open Lounge", "Invite", "Host Queue", "Audiophile"][m] ?? m;
const dspName = (d) => ["Off", "Headphones", "Speakers", "Room IR", "Crossfeed"][d] ?? d;
const roleName = (r) => ["호스트", "DJ", "리스너", "참관"][r] ?? r;

// ── 입력 배선 ────────────────────────────────────────
function wire() {
  document.querySelectorAll(".tab").forEach(tab => tab.onclick = () => {
    const group = tab.parentElement;
    group.querySelectorAll(".tab").forEach(t => t.classList.toggle("on", t === tab));
    const col = group.parentElement;
    col.querySelectorAll(".pane").forEach(p => p.classList.toggle("on", p.dataset.pane === tab.dataset.tab));
  });

  $("btnCreate").onclick = () => send({ type: "create_room", roomId: null, roomName: $("roomName").value, mode: Number($("mode").value) });
  $("btnJoin").onclick = () => send({ type: "join_room", roomId: $("joinId").value, inviteCode: $("invite").value || null });
  $("btnLeave").onclick = () => send({ type: "leave_room" });
  $("btnRooms").onclick = () => send({ type: "list_rooms" });
  $("btnPlay").onclick = () => send({ type: "play" });
  $("btnPause").onclick = () => send({ type: "pause" });
  $("btnPrev").onclick = () => send({ type: "skip", index: -1 });
  $("btnNext").onclick = () => send({ type: "skip", index: 1 });
  $("btnResync").onclick = () => send({ type: "resync" });
  $("btnClearQ").onclick = () => send({ type: "clear_queue" });
  $("btnLockQ").onclick = () => send({ type: "set_room_flags", text: "queue_lock", flag: !state?.queueLocked });

  $("seekbar").onclick = (e) => {
    const rect = e.currentTarget.getBoundingClientRect();
    const ratio = (e.clientX - rect.left) / rect.width;
    send({ type: "seek", mediaTimeMs: Math.round((state?.durationMs || 0) * ratio) });
  };

  $("chatForm").onsubmit = (e) => {
    e.preventDefault();
    const text = $("chat").value.trim();
    if (text) send({ type: "chat", text });
    $("chat").value = "";
  };
  $("pinForm").onsubmit = (e) => {
    e.preventDefault();
    const text = $("pinText").value.trim();
    if (text) send({ type: "pin", mediaTimeMs: Math.round(state?.mediaTimeMs || 0), text });
    $("pinText").value = "";
  };
  document.querySelectorAll(".react").forEach(b => b.onclick = () => send({ type: "react", emoji: b.dataset.emoji }));
  $("btnTheme").onclick = cycleTheme;
  $("btnWiki").onclick = () => {
    if (!currentArtistId) return note("먼저 곡을 재생하세요");
    send({ type: "wiki_bio", text: currentArtistId });
  };
  $("btnZoneCreate").onclick = () => {
    const name = $("zoneName").value.trim();
    if (!name) return;
    send({ type: "create_zone", text: name });
    $("zoneName").value = "";
  };

  $("dsp").onchange = (e) => send({ type: "set_dsp", dsp: Number(e.target.value) });
  $("policy").onchange = (e) => send({ type: "set_policy", policy: Number(e.target.value) });
  $("sourceMode").onchange = (e) => send({ type: "set_source_mode", sourceMode: Number(e.target.value) });
  $("maxMembers").onchange = (e) => send({ type: "set_room_flags", text: "max", index: Number(e.target.value) });
  $("retention").onchange = (e) => send({ type: "set_policy", minutes: Number(e.target.value) });
  $("btnAnon").onclick = () => send({ type: "set_policy", flag: !state?.anonymizeArchive });
  $("btnConsent").onclick = () => send({ type: "set_policy", consent: !state?.archiveDefaultConsent });
  $("btnCloud").onclick = () => send({ type: "set_room_flags", text: "cloud", flag: !state?.cloudSyncOptIn });
  document.querySelectorAll("[data-flag]").forEach(b => b.onclick = () => {
    const key = b.dataset.flag;
    const current = {
      seek: state?.seekingAllowed, comments: state?.commentsAllowed, chat: state?.chatCollapsed,
      auto_advance: state?.autoAdvance, smart_autoplay: state?.smartAutoplay,
      dsp_lock: state?.dspLocked, follow_host: state?.followHostView
    }[key];
    send({ type: "set_room_flags", text: key, flag: !current });
  });

  $("btnInviteRotate").onclick = () => send({ type: "invite", inviteAction: 0, minutes: 360 });
  $("btnInviteExtend").onclick = () => send({ type: "invite", inviteAction: 1, minutes: 60 });
  $("btnInviteRevoke").onclick = () => send({ type: "invite", inviteAction: 2 });

  $("btnScan").onclick = () => send({ type: "scan_library" });
  $("btnTidal").onclick = () => send({ type: "link_streaming", provider: 1, token: "demo-token", displayName: "Tidal" });
  $("btnQobuz").onclick = () => send({ type: "link_streaming", provider: 2, token: "demo-token", displayName: "Qobuz" });
  $("search").oninput = renderCatalog;

  $("btnEndpoints").onclick = () => send({ type: "endpoints" });
  $("btnArchives").onclick = () => send({ type: "archives" });
  $("btnPlaylists").onclick = () => send({ type: "playlists" });
  $("btnHist").onclick = () => send({ type: "history" });
  $("btnPair").onclick = () => send({ type: "pair" });
  $("btnEndYes").onclick = () => send({ type: "end_session", consent: true });
  $("btnEndNo").onclick = () => send({ type: "end_session", consent: false });
}

start();
