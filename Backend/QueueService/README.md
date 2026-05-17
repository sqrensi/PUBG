# Queue Service (Node.js)

Minimal local Queue + Matchmaker service for day 3-4 MVP flow.

## Endpoints

- `POST /enqueue` body: `{ "playerId": "player-123" }`
- `POST /dequeue` body: `{ "ticketId": "..." }`
- `POST /match/leave` body: `{ "ticketId": "..." }`
- `POST /match/presence/update` body: `{ "ticketId":"...", "position":{"x":0,"y":0,"z":0}, "yaw":0 }`
- `GET /match/presence/:ticketId`
- `GET /ticket/:ticketId`
- `GET /health`

Ticket statuses:

- `Queued`
- `Matched`
- `Cancelled`
- `Expired`

When matched, response includes `serverAddress` and `serverPort` for Unity client connect.

## Run (PowerShell)

```powershell
cd "c:\me\unity\ShooterPrototype\Backend\QueueService"
npm start
```

Optional env vars:

- `PORT` (default `5050`)
- `MATCH_SERVER_ADDRESS` (default `127.0.0.1`)
- `MATCH_SERVER_PORT` (default `7777`)
- `MIN_PLAYERS_TO_MATCH` (default `1`)
- `MATCH_TIMEOUT_SECONDS` (default `20`)
- `MATCH_BATCH_WINDOW_SECONDS` (default `2`) - waits briefly to group near-simultaneous joins into one match
