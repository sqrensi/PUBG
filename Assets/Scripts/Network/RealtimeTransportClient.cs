using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ShooterPrototype.Network
{
    public sealed class RealtimeTransportClient : MonoBehaviour
    {
        [Serializable]
        public sealed class RealtimePlayerState
        {
            public string ticketId;
            public PositionDto position;
            public float yaw;
            public int sampleTick;
            public long sampleTimeMs;
        }

        [Serializable]
        public sealed class RealtimeSnapshot
        {
            public string type;
            public int serverTick;
            public int serverTickRate;
            public RealtimePlayerState[] players;
        }

        [Serializable]
        public sealed class PositionDto
        {
            public float x;
            public float y;
            public float z;
        }

        [Serializable]
        private sealed class JoinMessage
        {
            public string type;
            public string ticketId;
        }

        [Serializable]
        private sealed class PoseMessage
        {
            public string type;
            public PositionDto position;
            public float yaw;
        }

        [SerializeField] private string websocketUrl = "ws://127.0.0.1:5051";

        private ClientWebSocket socket;
        private CancellationTokenSource cts;
        private SemaphoreSlim sendSemaphore;
        private Task receiveTask;
        private string connectedTicketId = string.Empty;
        private readonly object snapshotLock = new object();
        private RealtimeSnapshot latestSnapshot;
        private bool hasLatestSnapshot;

        public bool IsConnected => socket != null && socket.State == WebSocketState.Open;
        public string ConnectedTicketId => connectedTicketId;

        public void Configure(string wsUrl)
        {
            if (!string.IsNullOrWhiteSpace(wsUrl))
            {
                websocketUrl = wsUrl.Trim();
            }
        }

        public void Connect(string ticketId)
        {
            if (string.IsNullOrWhiteSpace(ticketId))
            {
                return;
            }

            if (IsConnected && string.Equals(connectedTicketId, ticketId, StringComparison.Ordinal))
            {
                return;
            }

            _ = ConnectInternalAsync(ticketId);
        }

        public void Disconnect()
        {
            _ = DisconnectInternalAsync();
        }

        public void SendPose(Vector3 position, float yaw)
        {
            if (!IsConnected)
            {
                return;
            }

            var msg = new PoseMessage
            {
                type = "pose",
                position = new PositionDto
                {
                    x = position.x,
                    y = position.y,
                    z = position.z
                },
                yaw = yaw
            };

            _ = SendJsonAsync(msg, cts != null ? cts.Token : CancellationToken.None);
        }

        public bool TryGetLatestSnapshot(out RealtimeSnapshot snapshot)
        {
            lock (snapshotLock)
            {
                if (!hasLatestSnapshot || latestSnapshot == null)
                {
                    snapshot = null;
                    return false;
                }

                snapshot = latestSnapshot;
                return true;
            }
        }

        private async Task ConnectInternalAsync(string ticketId)
        {
            await DisconnectInternalAsync();

            try
            {
                cts = new CancellationTokenSource();
                sendSemaphore = new SemaphoreSlim(1, 1);
                socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri(websocketUrl), cts.Token);

                connectedTicketId = ticketId;
                await SendJsonAsync(new JoinMessage
                {
                    type = "join",
                    ticketId = ticketId
                }, cts.Token);

                receiveTask = ReceiveLoopAsync(cts.Token);
                Debug.Log($"[RealtimeTransportClient] Connected to {websocketUrl} ticket={ticketId}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RealtimeTransportClient] Connect failed: {ex.Message}");
                await DisconnectInternalAsync();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[4096];
            var segment = new ArraySegment<byte>(buffer);
            var textBuilder = new StringBuilder(4096);

            while (!token.IsCancellationRequested && socket != null && socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(segment, token);
                }
                catch
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                var chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                textBuilder.Append(chunk);

                if (!result.EndOfMessage)
                {
                    continue;
                }

                var json = textBuilder.ToString();
                textBuilder.Clear();
                TryHandleIncomingJson(json);
            }

            await DisconnectInternalAsync();
        }

        private void TryHandleIncomingJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            RealtimeSnapshot snapshot;
            try
            {
                snapshot = JsonUtility.FromJson<RealtimeSnapshot>(json);
            }
            catch
            {
                return;
            }

            if (snapshot == null || !string.Equals(snapshot.type, "snapshot", StringComparison.Ordinal))
            {
                return;
            }

            lock (snapshotLock)
            {
                latestSnapshot = snapshot;
                hasLatestSnapshot = true;
            }
        }

        private async Task SendJsonAsync(object payload, CancellationToken token)
        {
            if (socket == null || socket.State != WebSocketState.Open || sendSemaphore == null)
            {
                return;
            }

            var json = JsonUtility.ToJson(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            await sendSemaphore.WaitAsync(token);
            try
            {
                if (socket != null && socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(segment, WebSocketMessageType.Text, true, token);
                }
            }
            catch
            {
                // ignored; receive loop/disconnect handles recovery
            }
            finally
            {
                sendSemaphore.Release();
            }
        }

        private async Task DisconnectInternalAsync()
        {
            var localCts = cts;
            cts = null;

            try
            {
                localCts?.Cancel();
            }
            catch
            {
                // ignored
            }

            if (socket != null)
            {
                try
                {
                    if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", CancellationToken.None);
                    }
                }
                catch
                {
                    // ignored
                }
                finally
                {
                    socket.Dispose();
                    socket = null;
                }
            }

            if (sendSemaphore != null)
            {
                sendSemaphore.Dispose();
                sendSemaphore = null;
            }

            connectedTicketId = string.Empty;
            lock (snapshotLock)
            {
                latestSnapshot = null;
                hasLatestSnapshot = false;
            }

            localCts?.Dispose();
        }

        private void OnDestroy()
        {
            _ = DisconnectInternalAsync();
        }
    }
}
