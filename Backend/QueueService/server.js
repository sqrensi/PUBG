const http = require("http");
const { URL } = require("url");
const crypto = require("crypto");
const WebSocket = require("ws");
const { WebSocketServer } = WebSocket;

const PORT = toInt(process.env.PORT, 5050);
const MATCH_SERVER_ADDRESS = process.env.MATCH_SERVER_ADDRESS || "127.0.0.1";
const MATCH_SERVER_PORT = toInt(process.env.MATCH_SERVER_PORT, 7777);
const MIN_PLAYERS_TO_MATCH = Math.max(1, toInt(process.env.MIN_PLAYERS_TO_MATCH, 1));
const MATCH_TIMEOUT_SECONDS = Math.max(3, toInt(process.env.MATCH_TIMEOUT_SECONDS, 20));
const MATCH_BATCH_WINDOW_SECONDS = Math.max(0, toInt(process.env.MATCH_BATCH_WINDOW_SECONDS, 2));
const PRESENCE_TIMEOUT_SECONDS = Math.max(2, toInt(process.env.PRESENCE_TIMEOUT_SECONDS, 5));
const MATCH_CONNECT_GRACE_SECONDS = Math.max(5, toInt(process.env.MATCH_CONNECT_GRACE_SECONDS, 45));
const SERVER_TICK_RATE = Math.max(10, toInt(process.env.SERVER_TICK_RATE, 120));
const REALTIME_WS_PORT = toInt(process.env.REALTIME_WS_PORT, 5051);

const ticketsById = new Map();
const queuedTicketIds = [];
let activeMatch = null;
let currentServerTick = 0;
const wsClientsByTicketId = new Map();
const wsMetaBySocket = new Map();

setInterval(() => {
  currentServerTick += 1;
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
      ticketCount: ticketsById.size
    });
    return;
  }

  if (req.method === "POST" && path === "/enqueue") {
    const body = await readJsonBody(req);
    const playerId = normalizePlayerId(body && body.playerId);
    const ticket = createQueuedTicket(playerId);

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
    if (ticket.status === "Matched") {
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
  wsMetaBySocket.set(socket, { ticketId: "" });

  socket.on("message", (raw) => {
    let message;
    try {
      message = JSON.parse(raw.toString("utf8"));
    } catch {
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
    }
  });

  socket.on("close", () => {
    handleWsDisconnect(socket);
  });

  socket.on("error", () => {
    handleWsDisconnect(socket);
  });
});

console.log(`[QueueService] realtime websocket listening on ws://127.0.0.1:${REALTIME_WS_PORT}`);

function createQueuedTicket(playerId) {
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
    serverAddress: "",
    serverPort: 0
  };

  ticketsById.set(ticketId, ticket);
  queuedTicketIds.push(ticketId);
  return ticket;
}

function tryMatchTickets() {
  if (queuedTicketIds.length === 0) {
    return;
  }

  const nowMs = Date.now();
  const shouldMatchByCount = queuedTicketIds.length >= MIN_PLAYERS_TO_MATCH;
  const oldestTicket = ticketsById.get(queuedTicketIds[0]);
  const oldestWaitMs = nowMs - oldestTicket.queueEnterTimeMs;
  const batchWindowElapsed = oldestWaitMs >= MATCH_BATCH_WINDOW_SECONDS * 1000;
  const shouldMatchByTimeout = oldestWaitMs >= MATCH_TIMEOUT_SECONDS * 1000;
  if ((!shouldMatchByCount || !batchWindowElapsed) && !shouldMatchByTimeout) {
    return;
  }

  const matchSize = queuedTicketIds.length;
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
    ticket.status = "Matched";
    ticket.matchedAtMs = nowMs;
    ticket.matchId = match.matchId;
    ticket.serverAddress = MATCH_SERVER_ADDRESS;
    ticket.serverPort = MATCH_SERVER_PORT;
    match.ticketIds.add(ticket.ticketId);
  }

  const playerCountInMatch = match.ticketIds.size;
  for (const ticketId of match.ticketIds) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched") {
      continue;
    }

    ticket.matchedPlayerCount = playerCountInMatch;
    if (ticket.presence == null) {
      ticket.presence = {
        position: { x: 0, y: 0, z: 0 },
        yaw: 0,
        sampleTick: 0,
        sampleTimeMs: 0,
        serverSampleTimeMs: 0,
        lastSeenMs: 0
      };
    }
  }
}

function expireTicketIfNeeded(ticket) {
  if (!ticket || ticket.status !== "Queued") {
    return;
  }

  const queueAgeMs = Date.now() - ticket.queueEnterTimeMs;
  if (queueAgeMs > MATCH_TIMEOUT_SECONDS * 3000) {
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
  if (activeMatch == null) {
    return [];
  }

  const now = Date.now();
  const players = [];
  for (const ticketId of activeMatch.ticketIds) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched" || !ticket.presence) {
      continue;
    }

    if (ticketId === ownerTicket.ticketId) {
      continue;
    }

    if (now - ticket.presence.lastSeenMs > PRESENCE_TIMEOUT_SECONDS * 1000) {
      continue;
    }

    players.push({
      ticketId,
      position: ticket.presence.position,
      yaw: ticket.presence.yaw,
      sampleTick: ticket.presence.sampleTick || 0,
      sampleTimeMs: ticket.presence.serverSampleTimeMs || ticket.presence.lastSeenMs
    });
  }

  return players;
}

function getOrCreateActiveMatch() {
  if (activeMatch != null) {
    return activeMatch;
  }

  activeMatch = {
    matchId: crypto.randomUUID(),
    createdAtMs: Date.now(),
    ticketIds: new Set()
  };

  return activeMatch;
}

function removeFromActiveMatch(ticketId) {
  if (activeMatch == null) {
    return;
  }

  activeMatch.ticketIds.delete(ticketId);
  if (activeMatch.ticketIds.size === 0) {
    activeMatch = null;
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

  wsClientsByTicketId.set(ticketId, socket);
  wsMetaBySocket.set(socket, { ticketId });
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
  const serverSampleTimeMs = Date.now();
  ticket.presence = {
    position,
    yaw,
    sampleTick: currentServerTick,
    sampleTimeMs: serverSampleTimeMs,
    serverSampleTimeMs,
    lastSeenMs: serverSampleTimeMs
  };
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
  }
}

function broadcastRealtimeSnapshots() {
  if (activeMatch == null) {
    return;
  }

  synchronizeActiveMatchCounts();
  const playersByTicketId = new Map();
  for (const ticketId of activeMatch.ticketIds) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched" || !ticket.presence) {
      continue;
    }

    playersByTicketId.set(ticketId, {
      ticketId,
      position: ticket.presence.position,
      yaw: ticket.presence.yaw,
      sampleTick: ticket.presence.sampleTick || currentServerTick,
      sampleTimeMs: ticket.presence.sampleTimeMs || ticket.presence.serverSampleTimeMs || Date.now()
    });
  }

  for (const [ownerTicketId, socket] of wsClientsByTicketId.entries()) {
    if (!socket || socket.readyState !== WebSocket.OPEN) {
      continue;
    }

    const ownerTicket = ticketsById.get(ownerTicketId);
    if (!ownerTicket || ownerTicket.status !== "Matched") {
      continue;
    }

    const others = [];
    for (const [ticketId, state] of playersByTicketId.entries()) {
      if (ticketId === ownerTicketId) {
        continue;
      }
      others.push(state);
    }

    const payload = {
      type: "snapshot",
      serverTick: currentServerTick,
      serverTickRate: SERVER_TICK_RATE,
      players: others
    };

    try {
      socket.send(JSON.stringify(payload));
    } catch {
      // ignored; close/error handlers will clean up
    }
  }
}

function synchronizeActiveMatchCounts() {
  if (activeMatch == null) {
    return;
  }

  const now = Date.now();
  const matchedTicketIds = [];
  for (const ticketId of activeMatch.ticketIds) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched") {
      continue;
    }

    // Allow clients to connect and start sending presence before timeout pruning.
    const stillInConnectGrace = ticket.matchedAtMs > 0 &&
      (now - ticket.matchedAtMs) <= MATCH_CONNECT_GRACE_SECONDS * 1000;
    const hasRecentPresence = ticket.presence &&
      ticket.presence.lastSeenMs > 0 &&
      (now - ticket.presence.lastSeenMs) <= PRESENCE_TIMEOUT_SECONDS * 1000;

    if (stillInConnectGrace || hasRecentPresence) {
      matchedTicketIds.push(ticketId);
    }
    else {
      ticket.status = "Disconnected";
    }
  }

  activeMatch.ticketIds = new Set(matchedTicketIds);
  const currentCount = activeMatch.ticketIds.size;
  for (const ticketId of activeMatch.ticketIds) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket) {
      continue;
    }

    ticket.matchedPlayerCount = currentCount;
  }

  if (currentCount === 0) {
    activeMatch = null;
  }
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
