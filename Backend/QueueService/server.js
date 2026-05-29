const http = require("http");
const { URL } = require("url");
const crypto = require("crypto");
const WebSocket = require("ws");
const { WebSocketServer } = WebSocket;

const PORT = toInt(process.env.PORT, 5050);
const MATCH_SERVER_ADDRESS = process.env.MATCH_SERVER_ADDRESS || "127.0.0.1";
const MATCH_SERVER_PORT = toInt(process.env.MATCH_SERVER_PORT, 7777);
const MIN_PLAYERS_TO_MATCH = Math.max(2, toInt(process.env.MIN_PLAYERS_TO_MATCH, 2));
const TARGET_PLAYERS_PER_MATCH = Math.max(2, toInt(process.env.TARGET_PLAYERS_PER_MATCH, 2));
const MATCH_TIMEOUT_SECONDS = Math.max(3, toInt(process.env.MATCH_TIMEOUT_SECONDS, 20));
const MATCH_BATCH_WINDOW_SECONDS = Math.max(0, toInt(process.env.MATCH_BATCH_WINDOW_SECONDS, 2));
const PRESENCE_TIMEOUT_SECONDS = Math.max(2, toInt(process.env.PRESENCE_TIMEOUT_SECONDS, 5));
const MATCH_CONNECT_GRACE_SECONDS = Math.max(5, toInt(process.env.MATCH_CONNECT_GRACE_SECONDS, 45));
const QUEUED_TICKET_TTL_SECONDS = Math.max(5, toInt(process.env.QUEUED_TICKET_TTL_SECONDS, Math.max(MATCH_TIMEOUT_SECONDS, 20)));
const SERVER_TICK_RATE = Math.max(10, toInt(process.env.SERVER_TICK_RATE, 128));
const REALTIME_WS_PORT = toInt(process.env.REALTIME_WS_PORT, 5051);
const DEBUG_REALTIME = (process.env.DEBUG_REALTIME || "1") !== "0";
const POSE_HISTORY_KEEP_MS = Math.max(200, toInt(process.env.POSE_HISTORY_KEEP_MS, 500));
const SNAPSHOT_HISTORY_SAMPLES = Math.max(4, toInt(process.env.SNAPSHOT_HISTORY_SAMPLES, 16));
const USE_BINARY_SNAPSHOTS = (process.env.USE_BINARY_SNAPSHOTS || "1") !== "0";
const MAX_PLAYER_SPEED = Number.isFinite(Number(process.env.MAX_PLAYER_SPEED))
  ? Math.max(4, Number(process.env.MAX_PLAYER_SPEED))
  : 12.5;
const PLAYER_MOVE_SPEED = 5.5;
const PLAYER_SPRINT_MULTIPLIER = 1.55;
const PLAYER_CROUCH_MULTIPLIER = 0.55;
const PLAYER_GRAVITY = -24;
const PLAYER_JUMP_HEIGHT = 1.25;
const PLAYER_SPRINT_MIN_FORWARD = 0.1;
const PLAYER_SIDE_SPEED_MULTIPLIER = 0.85;
const PLAYER_BACKWARD_SPEED_MULTIPLIER = 0.75;
const PLAYER_HIT_RADIUS = Number.isFinite(Number(process.env.PLAYER_HIT_RADIUS))
  ? Math.max(0.4, Number(process.env.PLAYER_HIT_RADIUS))
  : 0.95;
const MAX_WEAPON_RANGE = Number.isFinite(Number(process.env.MAX_WEAPON_RANGE))
  ? Math.max(20, Number(process.env.MAX_WEAPON_RANGE))
  : 150;
const DAMAGE_BY_HIT_ZONE = {
  leg: 15,
  body: 25,
  neck: 80,
  head: 100
};

const ticketsById = new Map();
const queuedTicketIds = [];
let activeMatch = null;
const matchesById = new Map();
let currentServerTick = 0;
const wsClientsByTicketId = new Map();
const wsMetaBySocket = new Map();
const lastPoseDebugByTicket = new Map();
const lastSnapshotDebugByOwner = new Map();
const lastMatchSnapshotBroadcastAtMs = new Map();

setInterval(() => {
  currentServerTick += 1;
  tickAllPlayerMovement();
  runMaintenanceSweep();
  broadcastRealtimeSnapshots();
}, Math.max(1, Math.floor(1000 / SERVER_TICK_RATE)));

const server = http.createServer(async (req, res) => {
  enableCors(res);

  if (req.method === "OPTIONS") {
    res.writeHead(204);
    res.end();
    return;
  }

  const requestUrl = new URL(req.url, `http://${req.headers.host || "127.0.0.1"}`);
  const path = requestUrl.pathname;

  if (req.method === "GET" && path === "/health") {
    respondJson(res, 200, {
      ok: true,
      queueSize: queuedTicketIds.length,
      ticketCount: ticketsById.size,
      activeMatchId: activeMatch ? activeMatch.matchId : "",
      activeSessionCount: Array.from(matchesById.values()).filter((m) => m.state !== "Ended").length
    });
    return;
  }

  if (req.method === "GET" && path === "/telemetry/active") {
    respondJson(res, 200, {
      serverTick: currentServerTick,
      queueSize: queuedTicketIds.length,
      activeMatchId: activeMatch ? activeMatch.matchId : "",
      sessions: Array.from(matchesById.values()).map(toSerializableSession)
    });
    return;
  }

  if (req.method === "GET" && path.startsWith("/telemetry/match/")) {
    const matchId = decodeURIComponent(path.replace("/telemetry/match/", ""));
    const session = matchesById.get(matchId);
    if (!session) {
      respondJson(res, 404, { error: "MatchNotFound", matchId });
      return;
    }

    respondJson(res, 200, {
      serverTick: currentServerTick,
      match: toSerializableSession(session),
      players: buildMatchTelemetryPlayers(matchId)
    });
    return;
  }

  if (req.method === "POST" && path === "/enqueue") {
    const body = await readJsonBody(req);
    const playerId = normalizePlayerId(body && body.playerId);
    const ticket = createQueuedTicket(playerId);
    if (DEBUG_REALTIME) {
      console.log(`[http][enqueue] player=${playerId} ticket=${ticket.ticketId}`);
    }

    respondJson(res, 200, {
      ticketId: ticket.ticketId,
      status: ticket.status
    });
    return;
  }

  if (req.method === "POST" && path === "/dequeue") {
    const body = await readJsonBody(req);
    const ticketId = body && body.ticketId;
    if (!ticketId || !ticketsById.has(ticketId)) {
      respondJson(res, 404, {
        success: false,
        status: "NotFound"
      });
      return;
    }

    const ticket = ticketsById.get(ticketId);
    if (ticket.status === "Queued") {
      ticket.status = "Cancelled";
      removeFromQueue(ticket.ticketId);
    }

    respondJson(res, 200, {
      success: true,
      status: ticket.status
    });
    return;
  }

  if (req.method === "POST" && path === "/match/leave") {
    const body = await readJsonBody(req);
    const ticketId = body && body.ticketId;
    if (!ticketId || !ticketsById.has(ticketId)) {
      respondJson(res, 404, {
        success: false,
        status: "NotFound"
      });
      return;
    }

    const ticket = ticketsById.get(ticketId);
    if (ticket.status === "Matched" || ticket.status === "Disconnected") {
      ticket.status = "Left";
      removeFromActiveMatch(ticket.ticketId);
      synchronizeActiveMatchCounts();
    }

    respondJson(res, 200, {
      success: true,
      status: ticket.status
    });
    return;
  }

  if (req.method === "GET" && path.startsWith("/ticket/")) {
    const ticketId = decodeURIComponent(path.replace("/ticket/", ""));
    const ticket = ticketsById.get(ticketId);
    if (!ticket) {
      respondJson(res, 404, {
        ticketId,
        status: "Expired"
      });
      return;
    }

    expireTicketIfNeeded(ticket);
    tryMatchTickets();

    respondJson(res, 200, toTicketStatus(ticket));
    if (DEBUG_REALTIME) {
      console.log(`[http][ticket] id=${ticketId} status=${ticket.status} match=${ticket.matchId || "none"} players=${ticket.matchedPlayerCount || 0}`);
    }
    return;
  }

  if (req.method === "POST" && path === "/match/presence/update") {
    const body = await readJsonBody(req);
    const ticketId = body && body.ticketId;
    const ticket = ticketId ? ticketsById.get(ticketId) : null;
    if (!ticket || ticket.status !== "Matched") {
      respondJson(res, 404, { success: false, status: "NotMatched" });
      return;
    }

    const position = normalizePosition(body && body.position);
    const yaw = normalizeNumber(body && body.yaw, 0);
    const sampleTimeMs = normalizeInt64(body && body.sampleTimeMs, Date.now());
    const serverSampleTimeMs = Date.now();
    ticket.presence = {
      position,
      yaw,
      sampleTick: currentServerTick,
      sampleTimeMs,
      serverSampleTimeMs,
      lastSeenMs: serverSampleTimeMs
    };

    respondJson(res, 200, { success: true });
    return;
  }

  if (req.method === "POST" && path === "/match/presence/sync") {
    const body = await readJsonBody(req);
    const ticketId = body && body.ticketId;
    const ticket = ticketId ? ticketsById.get(ticketId) : null;
    if (!ticket || ticket.status !== "Matched") {
      respondJson(res, 404, { success: false, status: "NotMatched", serverTimeMs: Date.now(), players: [] });
      return;
    }

    const position = normalizePosition(body && body.position);
    const yaw = normalizeNumber(body && body.yaw, 0);
    const sampleTimeMs = normalizeInt64(body && body.sampleTimeMs, Date.now());
    const serverSampleTimeMs = Date.now();
    ticket.presence = {
      position,
      yaw,
      sampleTick: currentServerTick,
      sampleTimeMs,
      serverSampleTimeMs,
      lastSeenMs: serverSampleTimeMs
    };

    const players = collectMatchPresence(ticket);
    respondJson(res, 200, { success: true, serverTimeMs: Date.now(), serverTick: currentServerTick, serverTickRate: SERVER_TICK_RATE, players });
    return;
  }

  if (req.method === "GET" && path.startsWith("/match/presence/")) {
    const ticketId = decodeURIComponent(path.replace("/match/presence/", ""));
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched") {
      respondJson(res, 404, { success: false, status: "NotMatched", players: [] });
      return;
    }

    const players = collectMatchPresence(ticket);
    respondJson(res, 200, { success: true, serverTimeMs: Date.now(), serverTick: currentServerTick, serverTickRate: SERVER_TICK_RATE, players });
    return;
  }

  respondJson(res, 404, { error: "NotFound" });
});

server.listen(PORT, "0.0.0.0", () => {
  console.log(`[QueueService] listening on http://127.0.0.1:${PORT}`);
  console.log(
    `[QueueService] match endpoint -> ${MATCH_SERVER_ADDRESS}:${MATCH_SERVER_PORT}, minPlayers=${MIN_PLAYERS_TO_MATCH}, timeout=${MATCH_TIMEOUT_SECONDS}s, batchWindow=${MATCH_BATCH_WINDOW_SECONDS}s`
  );
});

const wsServer = new WebSocketServer({ port: REALTIME_WS_PORT });
wsServer.on("connection", (socket) => {
  if (DEBUG_REALTIME) {
    console.log("[rt][ws-connection] open");
  }
  wsMetaBySocket.set(socket, { ticketId: "" });

  socket.on("message", (raw) => {
    let message;
    try {
      message = JSON.parse(raw.toString("utf8"));
    } catch {
      if (DEBUG_REALTIME) {
        console.log("[rt][message-parse-error]");
      }
      return;
    }

    if (!message || typeof message.type !== "string") {
      return;
    }

    if (message.type === "join") {
      const ticketId = typeof message.ticketId === "string" ? message.ticketId : "";
      handleWsJoin(socket, ticketId);
      return;
    }

    if (message.type === "pose") {
      handleWsPose(socket, message);
      return;
    }

    if (message.type === "shot") {
      handleWsShot(socket, message);
      return;
    }

    if (message.type === "hit") {
      handleWsHit(socket, message);
      return;
    }

    if (message.type === "ping") {
      try {
        socket.send(JSON.stringify({
          type: "pong",
          clientTimeMs: normalizeInt64(message.clientTimeMs, 0)
        }));
      } catch {
        // ignored
      }
      return;
    }

    if (DEBUG_REALTIME) {
      console.log(`[rt][message-unknown] type=${String(message.type)}`);
    }
  });

  socket.on("close", () => {
    if (DEBUG_REALTIME) {
      console.log("[rt][ws-connection] close");
    }
    handleWsDisconnect(socket);
  });

  socket.on("error", () => {
    if (DEBUG_REALTIME) {
      console.log("[rt][ws-connection] error");
    }
    handleWsDisconnect(socket);
  });
});

console.log(`[QueueService] realtime websocket listening on ws://127.0.0.1:${REALTIME_WS_PORT}`);

function createQueuedTicket(playerId) {
  cancelExistingQueuedTicketsForPlayer(playerId);
  removeDisconnectedMatchedTicketsForPlayer(playerId);

  const ticketId = crypto.randomUUID();
  const ticket = {
    ticketId,
    playerId,
    status: "Queued",
    queueEnterTimeMs: Date.now(),
    matchedAtMs: 0,
    matchId: "",
    matchedPlayerCount: 0,
    presence: null,
    telemetry: {
      wsOpenCount: 0,
      wsCloseCount: 0,
      reconnectCount: 0,
      poseReceived: 0,
      poseMissing: 0,
      poseOutOfOrder: 0,
      lastPoseSeq: -1,
      lastWsOpenMs: 0,
      lastWsCloseMs: 0
    },
    serverAddress: "",
    serverPort: 0
  };

  ticketsById.set(ticketId, ticket);
  queuedTicketIds.push(ticketId);
  return ticket;
}

function tryMatchTickets() {
  pruneQueuedTickets();

  if (queuedTicketIds.length === 0) {
    return;
  }

  const nowMs = Date.now();
  assignQueuedTicketsToExistingSessions(nowMs);

  if (queuedTicketIds.length === 0) {
    return;
  }

  const requiredPlayers = Math.max(MIN_PLAYERS_TO_MATCH, TARGET_PLAYERS_PER_MATCH);
  if (queuedTicketIds.length < requiredPlayers) {
    return;
  }

  const oldestTicket = ticketsById.get(queuedTicketIds[0]);
  if (!oldestTicket) {
    return;
  }
  const oldestWaitMs = nowMs - oldestTicket.queueEnterTimeMs;
  const batchWindowElapsed = oldestWaitMs >= MATCH_BATCH_WINDOW_SECONDS * 1000;
  const shouldMatchByTimeout = oldestWaitMs >= MATCH_TIMEOUT_SECONDS * 1000;
  if (!batchWindowElapsed && !shouldMatchByTimeout) {
    return;
  }

  const matchSize = Math.min(TARGET_PLAYERS_PER_MATCH, queuedTicketIds.length);
  const matchedTickets = [];
  for (let i = 0; i < matchSize && queuedTicketIds.length > 0; i++) {
    const ticketId = queuedTicketIds.shift();
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Queued") {
      continue;
    }

    matchedTickets.push(ticket);
  }

  if (matchedTickets.length === 0) {
    return;
  }

  const match = getOrCreateActiveMatch();
  for (const ticket of matchedTickets) {
    matchTicketToSession(ticket, match, nowMs);
  }

  synchronizeActiveMatchCounts();
}

function expireTicketIfNeeded(ticket) {
  if (!ticket || ticket.status !== "Queued") {
    return;
  }

  const queueAgeMs = Date.now() - ticket.queueEnterTimeMs;
  if (queueAgeMs > QUEUED_TICKET_TTL_SECONDS * 1000) {
    ticket.status = "Expired";
    removeFromQueue(ticket.ticketId);
  }
}

function removeFromQueue(ticketId) {
  const idx = queuedTicketIds.indexOf(ticketId);
  if (idx >= 0) {
    queuedTicketIds.splice(idx, 1);
  }
}

function toTicketStatus(ticket) {
  synchronizeActiveMatchCounts();

  let normalizedStatus = ticket.status;
  if (
    normalizedStatus === "Disconnected" ||
    normalizedStatus === "Left" ||
    normalizedStatus === "NotMatched")
  {
    normalizedStatus = "Expired";
  }

  return {
    ticketId: ticket.ticketId,
    playerId: ticket.playerId,
    status: normalizedStatus,
    queueDurationSeconds: (Date.now() - ticket.queueEnterTimeMs) / 1000,
    matchId: ticket.matchId || "",
    matchedPlayerCount: ticket.matchedPlayerCount || 0,
    serverAddress: ticket.serverAddress || "",
    serverPort: ticket.serverPort || 0
  };
}

function collectMatchPresence(ownerTicket) {
  synchronizeActiveMatchCounts();
  if (!ownerTicket || !ownerTicket.matchId) {
    return [];
  }
  return collectRealtimePlayersForMatch(ownerTicket.matchId, ownerTicket.ticketId);
}

function getOrCreateActiveMatch() {
  const nowMs = Date.now();
  const session = {
    matchId: crypto.randomUUID(),
    createdAtMs: nowMs,
    startedAtMs: nowMs,
    endedAtMs: 0,
    lastActivityMs: nowMs,
    state: "Open",
    ticketIds: new Set()
  };
  matchesById.set(session.matchId, session);
  activeMatch = session;
  return session;
}

function removeFromActiveMatch(ticketId) {
  const ticket = ticketsById.get(ticketId);
  const session = ticket && ticket.matchId ? matchesById.get(ticket.matchId) : activeMatch;
  if (!session) {
    return;
  }

  session.ticketIds.delete(ticketId);
  const socket = wsClientsByTicketId.get(ticketId);
  if (socket) {
    safeWsClose(socket, 1000, "removed from match");
    wsClientsByTicketId.delete(ticketId);
    wsMetaBySocket.delete(socket);
  }
  if (session.ticketIds.size === 0) {
    session.state = "Ended";
    session.endedAtMs = Date.now();
    if (activeMatch && activeMatch.matchId === session.matchId) {
      activeMatch = null;
    }
  }
}

function assignQueuedTicketsToExistingSessions(nowMs) {
  if (queuedTicketIds.length === 0) {
    return;
  }

  for (let i = queuedTicketIds.length - 1; i >= 0; i--) {
    const ticketId = queuedTicketIds[i];
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Queued") {
      queuedTicketIds.splice(i, 1);
      continue;
    }

    const joinableSessions = getJoinableSessions();
    if (joinableSessions.length === 0) {
      continue;
    }

    const selected = joinableSessions[Math.floor(Math.random() * joinableSessions.length)];
    queuedTicketIds.splice(i, 1);
    matchTicketToSession(ticket, selected, nowMs);
  }
}

function getJoinableSessions() {
  const sessions = [];
  for (const session of matchesById.values()) {
    if (!session || session.state === "Ended") {
      continue;
    }

    let matchedCount = 0;
    for (const ticketId of session.ticketIds) {
      const ticket = ticketsById.get(ticketId);
      if (ticket && ticket.status === "Matched") {
        matchedCount++;
      }
    }

    if (matchedCount >= 1 && matchedCount < TARGET_PLAYERS_PER_MATCH) {
      sessions.push(session);
    }
  }
  return sessions;
}

function matchTicketToSession(ticket, session, nowMs) {
  if (!ticket || !session) {
    return;
  }

  ticket.status = "Matched";
  ticket.matchedAtMs = nowMs;
  ticket.matchId = session.matchId;
  ticket.serverAddress = MATCH_SERVER_ADDRESS;
  ticket.serverPort = MATCH_SERVER_PORT;
  session.ticketIds.add(ticket.ticketId);
  session.lastActivityMs = nowMs;
  if (session.state === "Open") {
    session.state = "Active";
  }

  if (ticket.presence == null) {
    ticket.presence = createDefaultPresence(0, nowMs);
  }
}

function handleWsJoin(socket, ticketId) {
  if (!ticketId || !ticketsById.has(ticketId)) {
    safeWsClose(socket, 1008, "invalid ticket");
    return;
  }

  const ticket = ticketsById.get(ticketId);
  if (!ticket || ticket.status !== "Matched") {
    safeWsClose(socket, 1008, "ticket not matched");
    return;
  }

  const previous = wsClientsByTicketId.get(ticketId);
  if (previous && previous !== socket) {
    safeWsClose(previous, 1000, "replaced by newer connection");
  }

  const nowMs = Date.now();
  if (!ticket.presence) {
    ticket.presence = createDefaultPresence(currentServerTick, nowMs);
  } else {
    ticket.presence.sampleTick = ticket.presence.sampleTick || currentServerTick;
    ticket.presence.sampleTimeMs = ticket.presence.sampleTimeMs || nowMs;
    ticket.presence.serverSampleTimeMs = nowMs;
    ticket.presence.lastSeenMs = nowMs;
  }

  wsClientsByTicketId.set(ticketId, socket);
  wsMetaBySocket.set(socket, { ticketId });
  touchMatchSession(ticket.matchId);
  const joinTelemetry = ensureTicketTelemetry(ticket);
  joinTelemetry.wsOpenCount += 1;
  joinTelemetry.reconnectCount = Math.max(0, joinTelemetry.wsOpenCount - 1);
  joinTelemetry.lastWsOpenMs = Date.now();
  if (DEBUG_REALTIME) {
    console.log(`[rt][join] ticket=${ticketId} player=${ticket.playerId} match=${ticket.matchId || "none"}`);
  }

  try {
    socket.send(JSON.stringify({
      type: "joined",
      ticketId
    }));
    sendSnapshotToSocket(ticketId, socket);
  } catch {
    safeWsClose(socket, 1011, "failed to send join ack");
  }
}

function handleWsPose(socket, message) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched") {
    return;
  }

  const position = normalizePosition(message.position);
  const yaw = normalizeNumber(message.yaw, 0);
  const lookPitch = normalizeNumber(message.lookPitch, 0);
  const shotSeq = Math.max(0, normalizeInt64(message.shotSeq, 0));
  const shotOriginX = normalizeNumber(message.shotOriginX, 0);
  const shotOriginY = normalizeNumber(message.shotOriginY, 0);
  const shotOriginZ = normalizeNumber(message.shotOriginZ, 0);
  const shotDirX = normalizeNumber(message.shotDirX, 0);
  const shotDirY = normalizeNumber(message.shotDirY, 0);
  const shotDirZ = normalizeNumber(message.shotDirZ, 0);
  const shotEndX = normalizeNumber(message.shotEndX, 0);
  const shotEndY = normalizeNumber(message.shotEndY, 0);
  const shotEndZ = normalizeNumber(message.shotEndZ, 0);
  const shotHasEndPoint = !!message.shotHasEndPoint;
  const reloadSeq = Math.max(0, normalizeInt64(message.reloadSeq, 0));
  const hitPlayerSeq = Math.max(0, normalizeInt64(message.hitPlayerSeq, 0));
  const footstepSeq = Math.max(0, normalizeInt64(message.footstepSeq, 0));
  const isCrouching = !!message.isCrouching;
  const isSprinting = !!message.isSprinting;
  const wallAvoidBlend = Math.max(0, Math.min(1, normalizeNumber(message.wallAvoidBlend, 0)));
  const isDead = !!message.isDead;
  const deathSeq = Math.max(0, normalizeInt64(message.deathSeq, 0));
  const deathFallDirX = normalizeNumber(message.deathFallDirX, 0);
  const deathFallDirY = normalizeNumber(message.deathFallDirY, 0);
  const deathFallDirZ = normalizeNumber(message.deathFallDirZ, 0);
  const animSpeed = Math.max(0, Math.min(1.25, normalizeNumber(message.animSpeed, 0)));
  const isAiming = !!message.isAiming;
  const isGrounded = !!message.isGrounded;
  const jumpState = Math.max(0, Math.min(2, normalizeInt64(message.jumpState, isGrounded ? 0 : 2)));
  const rawAnimPhase = normalizeNumber(message.animPhase, 0);
  const animPhase = ((rawAnimPhase % 1) + 1) % 1;
  const poseSeq = normalizeInt64(message.poseSeq, -1);
  const moveInputX = normalizeNumber(message.moveInputX, 0);
  const moveInputZ = normalizeNumber(message.moveInputZ, 0);
  const jumpPressed = !!message.jumpPressed;
  const inputAuth = !!message.inputAuth;
  const serverSampleTimeMs = Date.now();
  const prevPresence = ticket.presence || {};
  const prevWasDead = !!prevPresence.isDead;

  ticket.inputState = {
    moveInputX,
    moveInputZ,
    jumpPressed,
    inputAuth,
    yaw,
    isCrouching,
    isSprinting,
    isGrounded,
    jumpState,
    clientX: position.x,
    clientY: position.y,
    clientZ: position.z,
    lastInputMs: serverSampleTimeMs
  };

  if (!ticket.presence) {
    ticket.presence = createDefaultPresence(currentServerTick, serverSampleTimeMs);
  }

  const presence = ticket.presence;
  if (!presence.hasPose) {
    presence.position = { x: position.x, y: position.y, z: position.z };
    presence.hasPose = true;
    presence.velocityX = 0;
    presence.velocityY = 0;
    presence.velocityZ = 0;
  }

  if (!isDead && prevWasDead) {
    presence.position = { x: position.x, y: position.y, z: position.z };
    presence.velocityX = 0;
    presence.velocityY = 0;
    presence.velocityZ = 0;
    presence.verticalVelocity = 0;
  } else if (isDead) {
    presence.position = { x: position.x, y: position.y, z: position.z };
    presence.velocityX = 0;
    presence.velocityY = 0;
    presence.velocityZ = 0;
    presence.verticalVelocity = 0;
  } else if (!inputAuth) {
    const clamped = clampPositionToMovement(presence, position, currentServerTick);
    presence.position = clamped;
    presence.yaw = yaw;
  } else {
    presence.yaw = yaw;
    const clamped = clampPositionToMovement(presence, position, currentServerTick);
    presence.position = {
      x: clamped.x,
      y: position.y,
      z: clamped.z
    };
  }

  presence.lookPitch = lookPitch;
  presence.shotSeq = shotSeq;
  if (shotSeq > (prevPresence.shotSeq || 0)) {
    recordShotEvent(ticket, {
      seq: shotSeq,
      shotOriginX,
      shotOriginY,
      shotOriginZ,
      shotDirX,
      shotDirY,
      shotDirZ,
      shotEndX,
      shotEndY,
      shotEndZ,
      shotHasEndPoint,
    });
  }
  presence.reloadSeq = reloadSeq;
  presence.hitPlayerSeq = hitPlayerSeq;
  presence.footstepSeq = footstepSeq;
  presence.isCrouching = isCrouching;
  presence.isSprinting = isSprinting;
  presence.wallAvoidBlend = wallAvoidBlend;
  presence.isDead = isDead;
  presence.deathSeq = deathSeq;
  presence.deathFallDirX = deathFallDirX;
  presence.deathFallDirY = deathFallDirY;
  presence.deathFallDirZ = deathFallDirZ;
  presence.animSpeed = animSpeed;
  presence.isAiming = isAiming;
  presence.isGrounded = isGrounded;
  presence.jumpState = jumpState;
  presence.animPhase = animPhase;
  presence.sampleTimeMs = serverSampleTimeMs;
  presence.serverSampleTimeMs = serverSampleTimeMs;
  presence.lastSeenMs = serverSampleTimeMs;

  if (DEBUG_REALTIME) {
    const last = lastPoseDebugByTicket.get(ticket.ticketId) || 0;
    if (serverSampleTimeMs - last >= 1000) {
      lastPoseDebugByTicket.set(ticket.ticketId, serverSampleTimeMs);
      const pos = presence.position;
      console.log(`[rt][pose] ticket=${ticket.ticketId} match=${ticket.matchId || "none"} pos=(${pos.x.toFixed(2)},${pos.y.toFixed(2)},${pos.z.toFixed(2)}) inputAuth=${inputAuth ? 1 : 0}`);
    }
  }

  const poseTelemetry = ensureTicketTelemetry(ticket);
  poseTelemetry.poseReceived += 1;
  if (poseSeq >= 0) {
    if (poseTelemetry.lastPoseSeq >= 0) {
      if (poseSeq > poseTelemetry.lastPoseSeq + 1) {
        poseTelemetry.poseMissing += (poseSeq - poseTelemetry.lastPoseSeq - 1);
      } else if (poseSeq <= poseTelemetry.lastPoseSeq) {
        poseTelemetry.poseOutOfOrder += 1;
      }
    }
    poseTelemetry.lastPoseSeq = poseSeq;
  }
  touchMatchSession(ticket.matchId);
}

function normalizeShotEvent(message) {
  return {
    seq: Math.max(0, normalizeInt64(message.shotSeq, 0)),
    shotOriginX: normalizeNumber(message.shotOriginX, 0),
    shotOriginY: normalizeNumber(message.shotOriginY, 0),
    shotOriginZ: normalizeNumber(message.shotOriginZ, 0),
    shotDirX: normalizeNumber(message.shotDirX, 0),
    shotDirY: normalizeNumber(message.shotDirY, 0),
    shotDirZ: normalizeNumber(message.shotDirZ, 0),
    shotEndX: normalizeNumber(message.shotEndX, 0),
    shotEndY: normalizeNumber(message.shotEndY, 0),
    shotEndZ: normalizeNumber(message.shotEndZ, 0),
    shotHasEndPoint: !!message.shotHasEndPoint,
  };
}

function recordShotEvent(ticket, event) {
  if (!ticket || !event) {
    return false;
  }

  const seq = Math.max(0, normalizeInt64(event.seq, 0));
  if (seq <= 0) {
    return false;
  }

  if (!ticket.presence) {
    ticket.presence = createDefaultPresence(currentServerTick, Date.now());
  }

  const presence = ticket.presence;
  const prevSeq = Number.isFinite(presence.shotSeq) ? presence.shotSeq : 0;
  if (seq < prevSeq) {
    return false;
  }

  const record = {
    seq,
    shotOriginX: normalizeNumber(event.shotOriginX, 0),
    shotOriginY: normalizeNumber(event.shotOriginY, 0),
    shotOriginZ: normalizeNumber(event.shotOriginZ, 0),
    shotDirX: normalizeNumber(event.shotDirX, 0),
    shotDirY: normalizeNumber(event.shotDirY, 0),
    shotDirZ: normalizeNumber(event.shotDirZ, 0),
    shotEndX: normalizeNumber(event.shotEndX, 0),
    shotEndY: normalizeNumber(event.shotEndY, 0),
    shotEndZ: normalizeNumber(event.shotEndZ, 0),
    shotHasEndPoint: !!event.shotHasEndPoint,
  };

  let ring = Array.isArray(presence.shotRing) ? presence.shotRing.slice() : [];
  const existingIndex = ring.findIndex((item) => item && item.seq === seq);
  if (existingIndex >= 0) {
    ring[existingIndex] = record;
  } else {
    ring.push(record);
    ring.sort((a, b) => (a.seq || 0) - (b.seq || 0));
    while (ring.length > 8) {
      ring.shift();
    }
  }
  presence.shotRing = ring;

  presence.shotSeq = seq;
  presence.shotOriginX = record.shotOriginX;
  presence.shotOriginY = record.shotOriginY;
  presence.shotOriginZ = record.shotOriginZ;
  presence.shotDirX = record.shotDirX;
  presence.shotDirY = record.shotDirY;
  presence.shotDirZ = record.shotDirZ;
  presence.shotEndX = record.shotEndX;
  presence.shotEndY = record.shotEndY;
  presence.shotEndZ = record.shotEndZ;
  presence.shotHasEndPoint = record.shotHasEndPoint;
  return true;
}

function handleWsShot(socket, message) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched") {
    return;
  }

  const event = normalizeShotEvent(message);
  if (!recordShotEvent(ticket, event)) {
    return;
  }

  touchMatchSession(ticket.matchId);
  if (ticket.matchId) {
    broadcastMatchSnapshots(ticket.matchId);
  }
}

function createDefaultPresence(sampleTick, sampleTimeMs) {
  return {
    position: { x: 0, y: 0, z: 0 },
    yaw: 0,
    lookPitch: 0,
    shotSeq: 0,
    shotOriginX: 0,
    shotOriginY: 0,
    shotOriginZ: 0,
    shotDirX: 0,
    shotDirY: 0,
    shotDirZ: 0,
    shotEndX: 0,
    shotEndY: 0,
    shotEndZ: 0,
    shotHasEndPoint: false,
    shotRing: [],
    reloadSeq: 0,
    hitPlayerSeq: 0,
    footstepSeq: 0,
    isCrouching: false,
    isSprinting: false,
    wallAvoidBlend: 0,
    isDead: false,
    deathSeq: 0,
    deathFallDirX: 0,
    deathFallDirY: 0,
    deathFallDirZ: 0,
    hasPose: false,
    verticalVelocity: 0,
    velocityX: 0,
    velocityY: 0,
    velocityZ: 0,
    animSpeed: 0,
    isAiming: false,
    isGrounded: true,
    jumpState: 0,
    animPhase: 0,
    sampleTick: sampleTick || 0,
    sampleTimeMs: sampleTimeMs || 0,
    serverSampleTimeMs: sampleTimeMs || 0,
    lastSeenMs: sampleTimeMs || 0
  };
}

function tickAllPlayerMovement() {
  for (const ticket of ticketsById.values()) {
    if (!ticket || ticket.status !== "Matched" || !ticket.presence || !ticket.presence.hasPose) {
      continue;
    }

    tickPlayerMovement(ticket);
  }
}

function tickPlayerMovement(ticket) {
  const presence = ticket.presence;
  const input = ticket.inputState;
  const dtSec = 1 / SERVER_TICK_RATE;
  const prevX = presence.position.x;
  const prevY = presence.position.y;
  const prevZ = presence.position.z;

  if (presence.isDead) {
    presence.sampleTick = currentServerTick;
    pushStateHistory(ticket);
    pushPoseHistory(ticket, buildPoseHistoryEntry(ticket));
    return;
  }

  if (input && input.inputAuth) {
    // XZ/Y come from the client pose (CharacterController handles walls). Tick loop only tracks velocity/history.
    presence.yaw = Number.isFinite(input.yaw) ? input.yaw : presence.yaw;
    presence.isGrounded = !!input.isGrounded;
    presence.isCrouching = !!input.isCrouching;
    presence.isSprinting = !!input.isSprinting;
    presence.jumpState = Number.isFinite(input.jumpState) ? input.jumpState : presence.jumpState;
  }

  presence.velocityX = (presence.position.x - prevX) / dtSec;
  presence.velocityY = (presence.position.y - prevY) / dtSec;
  presence.velocityZ = (presence.position.z - prevZ) / dtSec;
  presence.sampleTick = currentServerTick;
  pushStateHistory(ticket);
  pushPoseHistory(ticket, buildPoseHistoryEntry(ticket));
}

function buildPoseHistoryEntry(ticket) {
  const presence = ticket.presence;
  return {
    position: {
      x: presence.position.x,
      y: presence.position.y,
      z: presence.position.z
    },
    yaw: presence.yaw,
    lookPitch: presence.lookPitch || 0,
    isCrouching: !!presence.isCrouching,
    sampleTick: currentServerTick,
    timeMs: Date.now()
  };
}

function computeHorizontalDelta(input, dtSec) {
  const yawRad = (normalizeNumber(input.yaw, 0) * Math.PI) / 180;
  const forwardX = Math.sin(yawRad);
  const forwardZ = Math.cos(yawRad);
  const rightX = Math.cos(yawRad);
  const rightZ = -Math.sin(yawRad);

  let inputX = normalizeNumber(input.moveInputX, 0);
  let inputZ = normalizeNumber(input.moveInputZ, 0);
  const rawMag = Math.hypot(inputX, inputZ);
  if (rawMag > 1) {
    inputX /= rawMag;
    inputZ /= rawMag;
  }

  inputX *= PLAYER_SIDE_SPEED_MULTIPLIER;
  if (inputZ < 0) {
    inputZ *= PLAYER_BACKWARD_SPEED_MULTIPLIER;
  }

  let moveDirX = rightX * inputX + forwardX * inputZ;
  let moveDirZ = rightZ * inputX + forwardZ * inputZ;
  const moveDirMag = Math.hypot(moveDirX, moveDirZ);
  if (moveDirMag > 1) {
    moveDirX /= moveDirMag;
    moveDirZ /= moveDirMag;
  }

  let speedMultiplier = 1;
  if (input.isCrouching) {
    speedMultiplier = PLAYER_CROUCH_MULTIPLIER;
  } else if (
    input.isSprinting &&
    normalizeNumber(input.moveInputZ, 0) > PLAYER_SPRINT_MIN_FORWARD &&
    rawMag > 0.12
  ) {
    speedMultiplier = PLAYER_SPRINT_MULTIPLIER;
  }

  const horizontalSpeed = PLAYER_MOVE_SPEED * speedMultiplier;
  return {
    dx: moveDirX * horizontalSpeed * dtSec,
    dz: moveDirZ * horizontalSpeed * dtSec
  };
}

function pushStateHistory(ticket) {
  if (!ticket || !ticket.presence || !ticket.presence.hasPose) {
    return;
  }

  if (!Array.isArray(ticket.stateHistory)) {
    ticket.stateHistory = [];
  }

  const presence = ticket.presence;
  ticket.stateHistory.push({
    sampleTick: currentServerTick,
    x: presence.position.x,
    y: presence.position.y,
    z: presence.position.z,
    yaw: presence.yaw || 0,
    velX: Number.isFinite(presence.velocityX) ? presence.velocityX : 0,
    velY: Number.isFinite(presence.velocityY) ? presence.velocityY : 0,
    velZ: Number.isFinite(presence.velocityZ) ? presence.velocityZ : 0
  });

  const last = ticket.stateHistory.length >= 2
    ? ticket.stateHistory[ticket.stateHistory.length - 2]
    : null;
  if (last &&
      last.sampleTick === currentServerTick - 1 &&
      last.x === presence.position.x &&
      last.y === presence.position.y &&
      last.z === presence.position.z &&
      Math.abs(last.yaw - (presence.yaw || 0)) < 0.01) {
    ticket.stateHistory.pop();
    return;
  }

  const maxEntries = SNAPSHOT_HISTORY_SAMPLES * 3;
  while (ticket.stateHistory.length > maxEntries) {
    ticket.stateHistory.shift();
  }
}

function getBroadcastStateHistory(ticket) {
  if (!ticket || !Array.isArray(ticket.stateHistory)) {
    return [];
  }

  return ticket.stateHistory.slice(-SNAPSHOT_HISTORY_SAMPLES).map((entry) => ({
    sampleTick: entry.sampleTick,
    x: entry.x,
    y: entry.y,
    z: entry.z,
    yaw: entry.yaw,
    velX: entry.velX,
    velY: entry.velY,
    velZ: entry.velZ
  }));
}

function pushPoseHistory(ticket, entry) {
  if (!ticket) {
    return;
  }

  if (!Array.isArray(ticket.poseHistory)) {
    ticket.poseHistory = [];
  }

  ticket.poseHistory.push(entry);
  const cutoff = Date.now() - POSE_HISTORY_KEEP_MS;
  while (ticket.poseHistory.length > 0 && ticket.poseHistory[0].timeMs < cutoff) {
    ticket.poseHistory.shift();
  }

  const maxEntries = Math.ceil((POSE_HISTORY_KEEP_MS / 1000) * SERVER_TICK_RATE) + 8;
  while (ticket.poseHistory.length > maxEntries) {
    ticket.poseHistory.shift();
  }
}

function clampPositionToMovement(prevPresence, nextPosition, sampleTick) {
  if (!prevPresence || !prevPresence.hasPose || !prevPresence.position || !nextPosition) {
    return nextPosition;
  }

  const prevTick = prevPresence.sampleTick || (sampleTick - 1);
  const dtTicks = Math.max(1, sampleTick - prevTick);
  const dtSec = dtTicks / SERVER_TICK_RATE;
  const maxHorizStep = MAX_PLAYER_SPEED * dtSec * 1.35;
  const dx = nextPosition.x - prevPresence.position.x;
  const dz = nextPosition.z - prevPresence.position.z;
  const horiz = Math.hypot(dx, dz);
  if (horiz <= maxHorizStep) {
    return nextPosition;
  }

  const scale = maxHorizStep / Math.max(0.0001, horiz);
  return {
    x: prevPresence.position.x + dx * scale,
    y: nextPosition.y,
    z: prevPresence.position.z + dz * scale
  };
}

function getPoseAtOrBeforeTick(ticket, tick) {
  if (!ticket) {
    return null;
  }

  let best = null;
  const history = Array.isArray(ticket.poseHistory) ? ticket.poseHistory : [];
  for (let i = 0; i < history.length; i++) {
    const entry = history[i];
    if (!entry || !Number.isFinite(entry.sampleTick)) {
      continue;
    }

    if (entry.sampleTick <= tick && (!best || entry.sampleTick > best.sampleTick)) {
      best = entry;
    }
  }

  if (best) {
    return best;
  }

  if (ticket.presence && ticket.presence.hasPose) {
    return ticket.presence;
  }

  return null;
}

function resolveServerDamage(message) {
  const hitZone = typeof message.hitZone === "string" ? message.hitZone.trim().toLowerCase() : "";
  if (Object.prototype.hasOwnProperty.call(DAMAGE_BY_HIT_ZONE, hitZone)) {
    return DAMAGE_BY_HIT_ZONE[hitZone];
  }

  return Math.max(0, Math.min(1000, normalizeNumber(message.damage, 25)));
}

function validateLagCompensatedHit(attacker, target, message) {
  const maxRewindTicks = Math.max(8, Math.ceil(SERVER_TICK_RATE * 0.75));
  const requestedTick = normalizeInt64(message.shotTick, currentServerTick);
  const shotTick = Math.max(
    currentServerTick - maxRewindTicks,
    Math.min(currentServerTick, requestedTick)
  );

  const victimPose = getPoseAtOrBeforeTick(target, shotTick);
  const attackerPose = getPoseAtOrBeforeTick(attacker, shotTick);
  if (!victimPose || !attackerPose || !victimPose.position || !attackerPose.position) {
    return false;
  }

  const hx = normalizeNumber(message.hitX, 0);
  const hy = normalizeNumber(message.hitY, 0);
  const hz = normalizeNumber(message.hitZ, 0);
  const vx = victimPose.position.x;
  const vy = victimPose.position.y;
  const vz = victimPose.position.z;
  const victimCenterY = vy + (victimPose.isCrouching ? 0.65 : 0.92);
  const dx = hx - vx;
  const dy = hy - victimCenterY;
  const dz = hz - vz;
  if (Math.hypot(dx, dy, dz) > PLAYER_HIT_RADIUS) {
    return false;
  }

  const ax = attackerPose.position.x;
  const ay = attackerPose.position.y + 1.55;
  const az = attackerPose.position.z;
  const toHitX = hx - ax;
  const toHitY = hy - ay;
  const toHitZ = hz - az;
  const shotDist = Math.hypot(toHitX, toHitY, toHitZ);
  if (shotDist > MAX_WEAPON_RANGE || shotDist < 0.05) {
    return false;
  }

  let dirX = normalizeNumber(message.dirX, 0);
  let dirY = normalizeNumber(message.dirY, 0);
  let dirZ = normalizeNumber(message.dirZ, 0);
  const dirMag = Math.hypot(dirX, dirY, dirZ);
  if (dirMag <= 0.0001) {
    return false;
  }

  dirX /= dirMag;
  dirY /= dirMag;
  dirZ /= dirMag;
  const dot = (dirX * (toHitX / shotDist)) + (dirY * (toHitY / shotDist)) + (dirZ * (toHitZ / shotDist));
  return dot >= 0.82;
}

function handleWsHit(socket, message) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const attackerTicket = ticketsById.get(meta.ticketId);
  if (!attackerTicket || attackerTicket.status !== "Matched" || !attackerTicket.matchId) {
    return;
  }

  const rawTargetTicketId = typeof message.targetTicketId === "string" ? message.targetTicketId.trim() : "";
  if (!rawTargetTicketId || rawTargetTicketId === attackerTicket.ticketId) {
    return;
  }

  const targetTicket = ticketsById.get(rawTargetTicketId);
  if (!targetTicket || targetTicket.status !== "Matched" || targetTicket.matchId !== attackerTicket.matchId) {
    return;
  }

  const damage = resolveServerDamage(message);
  if (damage <= 0) {
    return;
  }

  if (!validateLagCompensatedHit(attackerTicket, targetTicket, message)) {
    if (DEBUG_REALTIME) {
      console.log(`[rt][hit-reject] attacker=${attackerTicket.ticketId} target=${targetTicket.ticketId}`);
    }
    return;
  }

  let dirX = normalizeNumber(message.dirX, 0);
  let dirY = normalizeNumber(message.dirY, 0);
  let dirZ = normalizeNumber(message.dirZ, 0);
  const dirMag = Math.sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
  if (dirMag > 0.0001) {
    dirX /= dirMag;
    dirY /= dirMag;
    dirZ /= dirMag;
  } else {
    dirX = 0;
    dirY = 0;
    dirZ = 1;
  }

  const targetSocket = wsClientsByTicketId.get(targetTicket.ticketId);
  if (!targetSocket || targetSocket.readyState !== WebSocket.OPEN) {
    return;
  }

  try {
    targetSocket.send(JSON.stringify({
      type: "damage",
      attackerTicketId: attackerTicket.ticketId,
      targetTicketId: targetTicket.ticketId,
      damage,
      dirX,
      dirY,
      dirZ
    }));
  } catch {
    // ignored
  }
}

function handleWsDisconnect(socket) {
  const meta = wsMetaBySocket.get(socket);
  wsMetaBySocket.delete(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const registered = wsClientsByTicketId.get(meta.ticketId);
  if (registered === socket) {
    wsClientsByTicketId.delete(meta.ticketId);
    const ticket = ticketsById.get(meta.ticketId);
    if (ticket) {
      const closeTelemetry = ensureTicketTelemetry(ticket);
      closeTelemetry.wsCloseCount += 1;
      closeTelemetry.lastWsCloseMs = Date.now();
      touchMatchSession(ticket.matchId);
    }
    if (DEBUG_REALTIME) {
      console.log(`[rt][disconnect] ticket=${meta.ticketId}`);
    }
  }
}

function encodeSnapshotBinary(payload) {
  if (!payload) {
    return null;
  }

  try {
    const chunks = [];
    const header = Buffer.alloc(11);
    header.write("RTS1", 0, 4, "ascii");
    header.writeUInt8(5, 4);
    header.writeUInt32LE(payload.serverTick >>> 0, 5);
    header.writeUInt16LE(payload.serverTickRate >>> 0, 9);
    chunks.push(header);

    const selfAuth = payload.selfAuthoritative;
    const hasSelfAuth = !!(selfAuth && selfAuth.position);
    chunks.push(Buffer.from([hasSelfAuth ? 1 : 0]));

    if (hasSelfAuth) {
      const selfBuf = Buffer.alloc(20);
      selfBuf.writeUInt32LE((selfAuth.sampleTick || payload.serverTick) >>> 0, 0);
      selfBuf.writeFloatLE(selfAuth.position.x, 4);
      selfBuf.writeFloatLE(selfAuth.position.y, 8);
      selfBuf.writeFloatLE(selfAuth.position.z, 12);
      selfBuf.writeFloatLE(selfAuth.yaw || 0, 16);
      chunks.push(selfBuf);
    }

    const players = Array.isArray(payload.players) ? payload.players : [];
    chunks.push(Buffer.from([Math.min(255, players.length)]));

    for (let i = 0; i < players.length; i++) {
      const player = players[i];
      const ticketId = typeof player.ticketId === "string" ? player.ticketId : "";
      const ticketBytes = Buffer.from(ticketId, "utf8");
      chunks.push(Buffer.from([Math.min(255, ticketBytes.length)]));
      chunks.push(ticketBytes);

      const pos = player.position || { x: 0, y: 0, z: 0 };
      const body = Buffer.alloc(35);
      body.writeUInt32LE((player.sampleTick || payload.serverTick) >>> 0, 0);
      body.writeFloatLE(pos.x, 4);
      body.writeFloatLE(pos.y, 8);
      body.writeFloatLE(pos.z, 12);
      body.writeFloatLE(player.yaw || 0, 16);
      body.writeFloatLE(player.velX || 0, 20);
      body.writeFloatLE(player.velY || 0, 24);
      body.writeFloatLE(player.velZ || 0, 28);
      let flags2 = 0;
      if (player.isDead) flags2 |= 1;
      if (player.isGrounded !== false) flags2 |= 2;
      if (player.isCrouching) flags2 |= 4;
      if (player.isSprinting) flags2 |= 8;
      if (player.isAiming) flags2 |= 16;
      body.writeUInt16LE(flags2, 32);
      body.writeUInt8(Math.max(0, Math.min(2, player.jumpState || 0)), 34);
      chunks.push(body);

      const meta = Buffer.alloc(93);
      meta.writeFloatLE(player.lookPitch || 0, 0);
      meta.writeUInt32LE((player.shotSeq || 0) >>> 0, 4);
      meta.writeUInt32LE((player.reloadSeq || 0) >>> 0, 8);
      meta.writeUInt32LE((player.hitPlayerSeq || 0) >>> 0, 12);
      meta.writeUInt32LE((player.footstepSeq || 0) >>> 0, 16);
      meta.writeFloatLE(player.wallAvoidBlend || 0, 20);
      meta.writeFloatLE(player.animSpeed || 0, 24);
      meta.writeFloatLE(player.animPhase || 0, 28);
      meta.writeUInt32LE((player.deathSeq || 0) >>> 0, 32);
      meta.writeFloatLE(player.deathFallDirX || 0, 36);
      meta.writeFloatLE(player.deathFallDirY || 0, 40);
      meta.writeFloatLE(player.deathFallDirZ || 0, 44);
      meta.writeFloatLE(player.shotOriginX || 0, 48);
      meta.writeFloatLE(player.shotOriginY || 0, 52);
      meta.writeFloatLE(player.shotOriginZ || 0, 56);
      meta.writeFloatLE(player.shotDirX || 0, 60);
      meta.writeFloatLE(player.shotDirY || 0, 64);
      meta.writeFloatLE(player.shotDirZ || 0, 68);
      meta.writeFloatLE(player.moveInputX || 0, 72);
      meta.writeFloatLE(player.moveInputZ || 0, 76);
      meta.writeFloatLE(player.shotEndX || 0, 80);
      meta.writeFloatLE(player.shotEndY || 0, 84);
      meta.writeFloatLE(player.shotEndZ || 0, 88);
      meta.writeUInt8(player.shotHasEndPoint ? 1 : 0, 92);
      chunks.push(meta);

      const recentShots = Array.isArray(player.recentShots) ? player.recentShots.slice(-8) : [];
      const ringCount = Math.min(255, recentShots.length);
      chunks.push(Buffer.from([ringCount]));
      for (let s = 0; s < ringCount; s++) {
        const ev = recentShots[s] || {};
        const shotBuf = Buffer.alloc(41);
        shotBuf.writeUInt32LE((ev.seq || 0) >>> 0, 0);
        shotBuf.writeFloatLE(ev.shotOriginX || 0, 4);
        shotBuf.writeFloatLE(ev.shotOriginY || 0, 8);
        shotBuf.writeFloatLE(ev.shotOriginZ || 0, 12);
        shotBuf.writeFloatLE(ev.shotDirX || 0, 16);
        shotBuf.writeFloatLE(ev.shotDirY || 0, 20);
        shotBuf.writeFloatLE(ev.shotDirZ || 0, 24);
        shotBuf.writeFloatLE(ev.shotEndX || 0, 28);
        shotBuf.writeFloatLE(ev.shotEndY || 0, 32);
        shotBuf.writeFloatLE(ev.shotEndZ || 0, 36);
        shotBuf.writeUInt8(ev.shotHasEndPoint ? 1 : 0, 40);
        chunks.push(shotBuf);
      }

      const history = Array.isArray(player.history) ? player.history : [];
      const historyCount = Math.min(255, history.length);
      chunks.push(Buffer.from([historyCount]));
      for (let h = 0; h < historyCount; h++) {
        const sample = history[h];
        const sampleBuf = Buffer.alloc(32);
        sampleBuf.writeUInt32LE((sample.sampleTick || 0) >>> 0, 0);
        sampleBuf.writeFloatLE(sample.x || 0, 4);
        sampleBuf.writeFloatLE(sample.y || 0, 8);
        sampleBuf.writeFloatLE(sample.z || 0, 12);
        sampleBuf.writeFloatLE(sample.yaw || 0, 16);
        sampleBuf.writeFloatLE(sample.velX || 0, 20);
        sampleBuf.writeFloatLE(sample.velY || 0, 24);
        sampleBuf.writeFloatLE(sample.velZ || 0, 28);
        chunks.push(sampleBuf);
      }
    }

    return Buffer.concat(chunks);
  } catch {
    return null;
  }
}

function broadcastRealtimeSnapshots() {
  synchronizeActiveMatchCounts();

  for (const [ownerTicketId, socket] of wsClientsByTicketId.entries()) {
    if (!socket || socket.readyState !== WebSocket.OPEN) {
      continue;
    }

    const ownerTicket = ticketsById.get(ownerTicketId);
    if (!ownerTicket || ownerTicket.status !== "Matched") {
      continue;
    }

    try {
      sendSnapshotToSocket(ownerTicketId, socket);
    } catch {
      // ignored; close/error handlers will clean up
    }
  }
}

function sendSnapshotToSocket(ownerTicketId, socket) {
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    return;
  }

  const ownerTicket = ticketsById.get(ownerTicketId);
  if (!ownerTicket || ownerTicket.status !== "Matched" || !ownerTicket.matchId) {
    return;
  }
  const others = collectRealtimePlayersForMatch(ownerTicket.matchId, ownerTicketId);
  if (DEBUG_REALTIME) {
    const nowMs = Date.now();
    const last = lastSnapshotDebugByOwner.get(ownerTicketId) || 0;
    if (nowMs - last >= 1000) {
      lastSnapshotDebugByOwner.set(ownerTicketId, nowMs);
      console.log(`[rt][snapshot] owner=${ownerTicketId} match=${ownerTicket.matchId} others=${others.length}`);
    }
  }

  const payload = {
    type: "snapshot",
    serverTick: currentServerTick,
    serverTickRate: SERVER_TICK_RATE,
    players: others
  };

  if (ownerTicket.presence && ownerTicket.presence.hasPose && ownerTicket.presence.position) {
    payload.selfAuthoritative = {
      position: ownerTicket.presence.position,
      yaw: ownerTicket.presence.yaw || 0,
      sampleTick: ownerTicket.presence.sampleTick || currentServerTick
    };
  }

  if (USE_BINARY_SNAPSHOTS) {
    const binaryPayload = encodeSnapshotBinary(payload);
    if (binaryPayload) {
      socket.send(binaryPayload);
      return;
    }
  }

  socket.send(JSON.stringify(payload));
}

function collectRealtimePlayersForMatch(matchId, ownerTicketId) {
  const players = [];
  if (!matchId) {
    return players;
  }

  const nowMs = Date.now();
  for (const ticket of ticketsById.values()) {
    if (!ticket || ticket.status !== "Matched" || ticket.matchId !== matchId || !ticket.presence) {
      continue;
    }

    if (ticket.ticketId === ownerTicketId) {
      continue;
    }

    // Skip players that have not sent a real pose yet.
    // Prevents remote spawn at default zero before first update arrives.
    if (!ticket.presence.hasPose) {
      continue;
    }

    const stillInConnectGrace = ticket.matchedAtMs > 0 &&
      (nowMs - ticket.matchedAtMs) <= MATCH_CONNECT_GRACE_SECONDS * 1000;
    const hasRecentPresence = ticket.presence.lastSeenMs > 0 &&
      (nowMs - ticket.presence.lastSeenMs) <= (PRESENCE_TIMEOUT_SECONDS * 1000 * 2);
    const hasLiveSocket = isTicketSocketLive(ticket.ticketId);
    if (!stillInConnectGrace && !hasRecentPresence && !hasLiveSocket) {
      continue;
    }

    players.push({
      ticketId: ticket.ticketId,
      position: ticket.presence.position,
      yaw: ticket.presence.yaw,
      lookPitch: ticket.presence.lookPitch || 0,
      shotSeq: Number.isFinite(ticket.presence.shotSeq) ? ticket.presence.shotSeq : 0,
      shotOriginX: Number.isFinite(ticket.presence.shotOriginX) ? ticket.presence.shotOriginX : 0,
      shotOriginY: Number.isFinite(ticket.presence.shotOriginY) ? ticket.presence.shotOriginY : 0,
      shotOriginZ: Number.isFinite(ticket.presence.shotOriginZ) ? ticket.presence.shotOriginZ : 0,
      shotDirX: Number.isFinite(ticket.presence.shotDirX) ? ticket.presence.shotDirX : 0,
      shotDirY: Number.isFinite(ticket.presence.shotDirY) ? ticket.presence.shotDirY : 0,
      shotDirZ: Number.isFinite(ticket.presence.shotDirZ) ? ticket.presence.shotDirZ : 0,
      shotEndX: Number.isFinite(ticket.presence.shotEndX) ? ticket.presence.shotEndX : 0,
      shotEndY: Number.isFinite(ticket.presence.shotEndY) ? ticket.presence.shotEndY : 0,
      shotEndZ: Number.isFinite(ticket.presence.shotEndZ) ? ticket.presence.shotEndZ : 0,
      shotHasEndPoint: !!ticket.presence.shotHasEndPoint,
      recentShots: Array.isArray(ticket.presence.shotRing) ? ticket.presence.shotRing.slice(-8) : [],
      reloadSeq: Number.isFinite(ticket.presence.reloadSeq) ? ticket.presence.reloadSeq : 0,
      hitPlayerSeq: Number.isFinite(ticket.presence.hitPlayerSeq) ? ticket.presence.hitPlayerSeq : 0,
      footstepSeq: Number.isFinite(ticket.presence.footstepSeq) ? ticket.presence.footstepSeq : 0,
      isCrouching: !!ticket.presence.isCrouching,
      isSprinting: !!ticket.presence.isSprinting,
      wallAvoidBlend: Number.isFinite(ticket.presence.wallAvoidBlend) ? ticket.presence.wallAvoidBlend : 0,
      isDead: !!ticket.presence.isDead,
      deathSeq: Number.isFinite(ticket.presence.deathSeq) ? ticket.presence.deathSeq : 0,
      deathFallDirX: Number.isFinite(ticket.presence.deathFallDirX) ? ticket.presence.deathFallDirX : 0,
      deathFallDirY: Number.isFinite(ticket.presence.deathFallDirY) ? ticket.presence.deathFallDirY : 0,
      deathFallDirZ: Number.isFinite(ticket.presence.deathFallDirZ) ? ticket.presence.deathFallDirZ : 0,
      animSpeed: ticket.presence.animSpeed || 0,
      isAiming: !!ticket.presence.isAiming,
      isGrounded: ticket.presence.isGrounded !== false,
      jumpState: Number.isFinite(ticket.presence.jumpState) ? ticket.presence.jumpState : 0,
      animPhase: Number.isFinite(ticket.presence.animPhase) ? ticket.presence.animPhase : 0,
      velX: Number.isFinite(ticket.presence.velocityX) ? ticket.presence.velocityX : 0,
      velY: Number.isFinite(ticket.presence.velocityY) ? ticket.presence.velocityY : 0,
      velZ: Number.isFinite(ticket.presence.velocityZ) ? ticket.presence.velocityZ : 0,
      moveInputX: ticket.inputState?.inputAuth ? normalizeNumber(ticket.inputState.moveInputX, 0) : 0,
      moveInputZ: ticket.inputState?.inputAuth ? normalizeNumber(ticket.inputState.moveInputZ, 0) : 0,
      sampleTick: ticket.presence.sampleTick || currentServerTick,
      sampleTimeMs: ticket.presence.sampleTimeMs || ticket.presence.serverSampleTimeMs || Date.now(),
      history: getBroadcastStateHistory(ticket)
    });
  }

  return players;
}

function broadcastMatchSnapshots(matchId) {
  if (!matchId) {
    return;
  }

  const nowMs = Date.now();
  const lastAtMs = lastMatchSnapshotBroadcastAtMs.get(matchId) || 0;
  // Throttle pose-triggered bursts; periodic broadcastRealtimeSnapshots() still runs every server tick.
  if (nowMs - lastAtMs < 16) {
    return;
  }
  lastMatchSnapshotBroadcastAtMs.set(matchId, nowMs);

  for (const [ticketId, socket] of wsClientsByTicketId.entries()) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched" || ticket.matchId !== matchId) {
      continue;
    }
    if (!socket || socket.readyState !== WebSocket.OPEN) {
      continue;
    }

    try {
      sendSnapshotToSocket(ticketId, socket);
    } catch {
      // ignored
    }
  }
}

function synchronizeActiveMatchCounts() {
  const now = Date.now();
  let firstActiveSession = null;

  for (const session of matchesById.values()) {
    if (!session || session.state === "Ended") {
      continue;
    }

    const matchedTicketIds = [];
    for (const ticketId of session.ticketIds) {
      const ticket = ticketsById.get(ticketId);
      if (!ticket || ticket.status !== "Matched") {
        continue;
      }

      const stillInConnectGrace = ticket.matchedAtMs > 0 &&
        (now - ticket.matchedAtMs) <= MATCH_CONNECT_GRACE_SECONDS * 1000;
      const hasRecentPresence = ticket.presence &&
        ticket.presence.lastSeenMs > 0 &&
        (now - ticket.presence.lastSeenMs) <= PRESENCE_TIMEOUT_SECONDS * 1000;
      const hasLiveSocket = isTicketSocketLive(ticketId);

      if (stillInConnectGrace || hasRecentPresence || hasLiveSocket) {
        matchedTicketIds.push(ticketId);
      } else {
        ticket.status = "Disconnected";
        dropTicketRealtimeConnection(ticketId, "ticket disconnected");
      }
    }

    session.ticketIds = new Set(matchedTicketIds);
    session.lastActivityMs = now;
    if (matchedTicketIds.length > 0 && session.state === "Open") {
      session.state = "Active";
    }
    if (matchedTicketIds.length === 0) {
      session.state = "Ended";
      session.endedAtMs = now;
    } else if (firstActiveSession == null) {
      firstActiveSession = session;
    }

    for (const ticketId of matchedTicketIds) {
      const ticket = ticketsById.get(ticketId);
      if (ticket) {
        ticket.matchedPlayerCount = matchedTicketIds.length;
      }
    }
  }

  activeMatch = firstActiveSession;
}

function normalizePlayerId(playerId) {
  if (typeof playerId !== "string" || playerId.trim().length === 0) {
    return "anonymous";
  }
  return playerId.trim();
}

function normalizePosition(position) {
  return {
    x: normalizeNumber(position && position.x, 0),
    y: normalizeNumber(position && position.y, 0),
    z: normalizeNumber(position && position.z, 0)
  };
}

function normalizeNumber(value, fallback) {
  const n = Number(value);
  return Number.isFinite(n) ? n : fallback;
}

function normalizeInt64(value, fallback) {
  const n = Number.parseInt(value, 10);
  return Number.isFinite(n) ? n : fallback;
}

function enableCors(res) {
  res.setHeader("Access-Control-Allow-Origin", "*");
  res.setHeader("Access-Control-Allow-Methods", "GET,POST,OPTIONS");
  res.setHeader("Access-Control-Allow-Headers", "Content-Type");
}

function respondJson(res, statusCode, payload) {
  res.writeHead(statusCode, { "Content-Type": "application/json; charset=utf-8" });
  res.end(JSON.stringify(payload));
}

function readJsonBody(req) {
  return new Promise((resolve) => {
    let raw = "";
    req.on("data", (chunk) => {
      raw += chunk.toString("utf8");
      if (raw.length > 1024 * 1024) {
        raw = "";
      }
    });
    req.on("end", () => {
      if (!raw) {
        resolve({});
        return;
      }

      try {
        resolve(JSON.parse(raw));
      } catch {
        resolve({});
      }
    });
    req.on("error", () => resolve({}));
  });
}

function toInt(value, fallback) {
  const n = Number.parseInt(value, 10);
  return Number.isFinite(n) ? n : fallback;
}

function safeWsClose(socket, code, reason) {
  try {
    socket.close(code, reason);
  } catch {
    // ignored
  }
}

function isTicketSocketLive(ticketId) {
  const socket = wsClientsByTicketId.get(ticketId);
  return !!socket && socket.readyState === WebSocket.OPEN;
}

function dropTicketRealtimeConnection(ticketId, reason) {
  const socket = wsClientsByTicketId.get(ticketId);
  if (!socket) {
    return;
  }

  wsClientsByTicketId.delete(ticketId);
  wsMetaBySocket.delete(socket);
  safeWsClose(socket, 1000, reason || "connection closed");
}

function runMaintenanceSweep() {
  pruneQueuedTickets();
  synchronizeActiveMatchCounts();
  runMatchLifecycleManager();
}

function canRecycleActiveMatchForNewQueue(nowMs) {
  if (activeMatch == null || activeMatch.ticketIds.size === 0) {
    return true;
  }

  let hasMatchedTickets = false;
  let hasRecentOrLivePlayers = false;
  for (const ticketId of activeMatch.ticketIds) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched") {
      continue;
    }

    hasMatchedTickets = true;
    const liveSocket = isTicketSocketLive(ticketId);
    const hasRecentPresence = ticket.presence &&
      ticket.presence.lastSeenMs > 0 &&
      (nowMs - ticket.presence.lastSeenMs) <= PRESENCE_TIMEOUT_SECONDS * 1000;
    const inConnectGrace = ticket.matchedAtMs > 0 &&
      (nowMs - ticket.matchedAtMs) <= MATCH_CONNECT_GRACE_SECONDS * 1000;

    if (liveSocket || hasRecentPresence || inConnectGrace) {
      hasRecentOrLivePlayers = true;
      break;
    }
  }

  if (!hasMatchedTickets) {
    return true;
  }

  return !hasRecentOrLivePlayers;
}

function recycleActiveMatchIfNeeded() {
  if (activeMatch == null) {
    return;
  }

  for (const ticketId of activeMatch.ticketIds) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched") {
      continue;
    }

    ticket.status = "Disconnected";
    dropTicketRealtimeConnection(ticketId, "match recycled");
  }

  activeMatch.state = "Ended";
  activeMatch.endedAtMs = Date.now();
  activeMatch = null;
}

function pruneQueuedTickets() {
  if (queuedTicketIds.length === 0) {
    return;
  }

  for (let i = queuedTicketIds.length - 1; i >= 0; i--) {
    const ticketId = queuedTicketIds[i];
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Queued") {
      queuedTicketIds.splice(i, 1);
      continue;
    }

    expireTicketIfNeeded(ticket);
    if (ticket.status !== "Queued") {
      queuedTicketIds.splice(i, 1);
    }
  }
}

function cancelExistingQueuedTicketsForPlayer(playerId) {
  if (!playerId) {
    return;
  }

  for (let i = queuedTicketIds.length - 1; i >= 0; i--) {
    const ticketId = queuedTicketIds[i];
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Queued") {
      queuedTicketIds.splice(i, 1);
      continue;
    }

    if (ticket.playerId !== playerId) {
      continue;
    }

    ticket.status = "Cancelled";
    queuedTicketIds.splice(i, 1);
  }
}

function removeDisconnectedMatchedTicketsForPlayer(playerId) {
  if (!playerId) {
    return;
  }

  for (const ticket of ticketsById.values()) {
    if (!ticket || ticket.playerId !== playerId) {
      continue;
    }

    if (ticket.status !== "Matched" && ticket.status !== "Disconnected") {
      continue;
    }

    ticket.status = "Disconnected";
    if (ticket.matchId) {
      const session = matchesById.get(ticket.matchId);
      if (session) {
        session.ticketIds.delete(ticket.ticketId);
      }
    }
    dropTicketRealtimeConnection(ticket.ticketId, "stale player ticket replaced");
  }
}

function tryRestoreActiveMatchByTicket(ownerTicket) {
  if (!ownerTicket || !ownerTicket.matchId) {
    return;
  }

  const existingSession = matchesById.get(ownerTicket.matchId);
  if (existingSession && existingSession.state !== "Ended") {
    activeMatch = existingSession;
    return;
  }

  const ticketIds = [];
  for (const ticket of ticketsById.values()) {
    if (!ticket || ticket.status !== "Matched") {
      continue;
    }

    if (ticket.matchId === ownerTicket.matchId) {
      ticketIds.push(ticket.ticketId);
    }
  }

  if (ticketIds.length === 0) {
    return;
  }

  const nowMs = Date.now();
  activeMatch = {
    matchId: ownerTicket.matchId,
    createdAtMs: ownerTicket.matchedAtMs || nowMs,
    startedAtMs: ownerTicket.matchedAtMs || nowMs,
    endedAtMs: 0,
    lastActivityMs: nowMs,
    state: "Active",
    ticketIds: new Set(ticketIds)
  };
  matchesById.set(activeMatch.matchId, activeMatch);
}

function tryRestoreAnyActiveMatch() {
  for (const session of matchesById.values()) {
    if (session && session.state !== "Ended" && session.ticketIds.size > 0) {
      activeMatch = session;
      return;
    }
  }

  for (const ticket of ticketsById.values()) {
    if (!ticket || ticket.status !== "Matched" || !ticket.matchId) {
      continue;
    }

    tryRestoreActiveMatchByTicket(ticket);
    if (activeMatch != null) {
      return;
    }
  }
}

function runMatchLifecycleManager() {
  const nowMs = Date.now();
  const endedTtlMs = Math.max(60000, MATCH_TIMEOUT_SECONDS * 1000 * 10);
  for (const [matchId, session] of matchesById.entries()) {
    const activeTickets = Array.from(session.ticketIds).filter((ticketId) => {
      const ticket = ticketsById.get(ticketId);
      return !!ticket && ticket.status === "Matched";
    });

    session.ticketIds = new Set(activeTickets);
    session.lastActivityMs = nowMs;

    if (activeTickets.length === 0 && session.state !== "Ended") {
      session.state = "Ended";
      session.endedAtMs = nowMs;
    } else if (activeTickets.length > 0 && session.state === "Open") {
      session.state = "Active";
    }

    if (session.state === "Ended" && session.endedAtMs > 0 && (nowMs - session.endedAtMs) > endedTtlMs) {
      matchesById.delete(matchId);
    }
  }
}

function ensureTicketTelemetry(ticket) {
  if (!ticket.telemetry) {
    ticket.telemetry = {
      wsOpenCount: 0,
      wsCloseCount: 0,
      reconnectCount: 0,
      poseReceived: 0,
      poseMissing: 0,
      poseOutOfOrder: 0,
      lastPoseSeq: -1,
      lastWsOpenMs: 0,
      lastWsCloseMs: 0
    };
  }
  return ticket.telemetry;
}

function touchMatchSession(matchId) {
  if (!matchId) {
    return;
  }
  const session = matchesById.get(matchId);
  if (session) {
    session.lastActivityMs = Date.now();
  }
}

function toSerializableSession(session) {
  return {
    matchId: session.matchId,
    state: session.state,
    createdAtMs: session.createdAtMs,
    startedAtMs: session.startedAtMs || session.createdAtMs,
    endedAtMs: session.endedAtMs || 0,
    lastActivityMs: session.lastActivityMs || 0,
    playerCount: session.ticketIds ? session.ticketIds.size : 0,
    ticketIds: session.ticketIds ? Array.from(session.ticketIds) : []
  };
}

function buildMatchTelemetryPlayers(matchId) {
  const result = [];
  for (const ticket of ticketsById.values()) {
    if (!ticket || ticket.matchId !== matchId) {
      continue;
    }

    const telemetry = ensureTicketTelemetry(ticket);
    const expected = telemetry.poseReceived + telemetry.poseMissing;
    const packetLossPercent = expected > 0 ? (telemetry.poseMissing / expected) * 100 : 0;
    result.push({
      ticketId: ticket.ticketId,
      playerId: ticket.playerId,
      status: ticket.status,
      wsOpenCount: telemetry.wsOpenCount,
      wsCloseCount: telemetry.wsCloseCount,
      reconnectCount: telemetry.reconnectCount,
      poseReceived: telemetry.poseReceived,
      poseMissing: telemetry.poseMissing,
      poseOutOfOrder: telemetry.poseOutOfOrder,
      packetLossPercent: Number(packetLossPercent.toFixed(2)),
      lastWsOpenMs: telemetry.lastWsOpenMs,
      lastWsCloseMs: telemetry.lastWsCloseMs
    });
  }
  return result;
}
