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
const SERVER_TICK_RATE = Math.max(10, toInt(process.env.SERVER_TICK_RATE, 120));
const REALTIME_WS_PORT = toInt(process.env.REALTIME_WS_PORT, 5051);
const DEBUG_REALTIME = (process.env.DEBUG_REALTIME || "1") !== "0";

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

    if (message.type === "hit") {
      handleWsHit(socket, message);
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
    ticket.presence = {
      position: { x: 0, y: 0, z: 0 },
      yaw: 0,
      lookPitch: 0,
      shotSeq: 0,
      reloadSeq: 0,
      hitPlayerSeq: 0,
      footstepSeq: 0,
      isCrouching: false,
      wallAvoidBlend: 0,
      isDead: false,
      deathSeq: 0,
      deathFallDirX: 0,
      deathFallDirY: 0,
      deathFallDirZ: 0,
      hasPose: false,
      animSpeed: 0,
      isAiming: false,
      isGrounded: true,
      jumpState: 0,
      animPhase: 0,
      sampleTick: 0,
      sampleTimeMs: 0,
      serverSampleTimeMs: 0,
      lastSeenMs: 0
    };
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
    ticket.presence = {
      position: { x: 0, y: 0, z: 0 },
      yaw: 0,
      lookPitch: 0,
      shotSeq: 0,
      reloadSeq: 0,
      hitPlayerSeq: 0,
      footstepSeq: 0,
      isCrouching: false,
      wallAvoidBlend: 0,
      isDead: false,
      deathSeq: 0,
      deathFallDirX: 0,
      deathFallDirY: 0,
      deathFallDirZ: 0,
      hasPose: false,
      animSpeed: 0,
      isAiming: false,
      isGrounded: true,
      jumpState: 0,
      animPhase: 0,
      sampleTick: currentServerTick,
      sampleTimeMs: nowMs,
      serverSampleTimeMs: nowMs,
      lastSeenMs: nowMs
    };
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
  const reloadSeq = Math.max(0, normalizeInt64(message.reloadSeq, 0));
  const hitPlayerSeq = Math.max(0, normalizeInt64(message.hitPlayerSeq, 0));
  const footstepSeq = Math.max(0, normalizeInt64(message.footstepSeq, 0));
  const isCrouching = !!message.isCrouching;
  const wallAvoidBlend = Math.max(0, Math.min(1, normalizeNumber(message.wallAvoidBlend, 0)));
  const isDead = !!message.isDead;
  const deathSeq = Math.max(0, normalizeInt64(message.deathSeq, 0));
  const deathFallDirX = normalizeNumber(message.deathFallDirX, 0);
  const deathFallDirY = normalizeNumber(message.deathFallDirY, 0);
  const deathFallDirZ = normalizeNumber(message.deathFallDirZ, 0);
  const animSpeed = Math.max(0, Math.min(1, normalizeNumber(message.animSpeed, 0)));
  const isAiming = !!message.isAiming;
  const isGrounded = !!message.isGrounded;
  const jumpState = Math.max(0, Math.min(2, normalizeInt64(message.jumpState, isGrounded ? 0 : 2)));
  const rawAnimPhase = normalizeNumber(message.animPhase, 0);
  const animPhase = ((rawAnimPhase % 1) + 1) % 1;
  const poseSeq = normalizeInt64(message.poseSeq, -1);
  const serverSampleTimeMs = Date.now();
  ticket.presence = {
    position,
    yaw,
    lookPitch,
    shotSeq,
    reloadSeq,
    hitPlayerSeq,
    footstepSeq,
    isCrouching,
    wallAvoidBlend,
    isDead,
    deathSeq,
    deathFallDirX,
    deathFallDirY,
    deathFallDirZ,
    hasPose: true,
    animSpeed,
    isAiming,
    isGrounded,
    jumpState,
    animPhase,
    sampleTick: currentServerTick,
    sampleTimeMs: serverSampleTimeMs,
    serverSampleTimeMs,
    lastSeenMs: serverSampleTimeMs
  };
  if (DEBUG_REALTIME) {
    const last = lastPoseDebugByTicket.get(ticket.ticketId) || 0;
    if (serverSampleTimeMs - last >= 1000) {
      lastPoseDebugByTicket.set(ticket.ticketId, serverSampleTimeMs);
      console.log(`[rt][pose] ticket=${ticket.ticketId} match=${ticket.matchId || "none"} pos=(${position.x.toFixed(2)},${position.y.toFixed(2)},${position.z.toFixed(2)})`);
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

  const damage = Math.max(0, Math.min(1000, normalizeNumber(message.damage, 0)));
  if (damage <= 0) {
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
      reloadSeq: Number.isFinite(ticket.presence.reloadSeq) ? ticket.presence.reloadSeq : 0,
      hitPlayerSeq: Number.isFinite(ticket.presence.hitPlayerSeq) ? ticket.presence.hitPlayerSeq : 0,
      footstepSeq: Number.isFinite(ticket.presence.footstepSeq) ? ticket.presence.footstepSeq : 0,
      isCrouching: !!ticket.presence.isCrouching,
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
      sampleTick: ticket.presence.sampleTick || currentServerTick,
      sampleTimeMs: ticket.presence.sampleTimeMs || ticket.presence.serverSampleTimeMs || Date.now()
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
