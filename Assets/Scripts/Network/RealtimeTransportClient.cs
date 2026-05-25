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
            public float lookPitch;
            public int shotSeq;
            public int reloadSeq;
            public int hitPlayerSeq;
            public int footstepSeq;
            public bool isCrouching;
            public float wallAvoidBlend;
            public bool isDead;
            public int deathSeq;
            public float deathFallDirX;
            public float deathFallDirY;
            public float deathFallDirZ;
            public float animSpeed;
            public bool isAiming;
            public bool isGrounded;
            public int jumpState;
            public float animPhase;
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
        private sealed class JoinedMessage
        {
            public string type;
            public string ticketId;
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
        private sealed class HitMessage
        {
            public string type;
            public string targetTicketId;
            public float damage;
            public float dirX;
            public float dirY;
            public float dirZ;
        }

        [Serializable]
        public sealed class DamageMessage
        {
            public string type;
            public string attackerTicketId;
            public string targetTicketId;
            public float damage;
            public float dirX;
            public float dirY;
            public float dirZ;
        }

        [Serializable]
        private sealed class PoseMessage
        {
            public string type;
            public PositionDto position;
            public float yaw;
            public float lookPitch;
            public int shotSeq;
            public int reloadSeq;
            public int hitPlayerSeq;
            public int footstepSeq;
            public bool isCrouching;
            public float wallAvoidBlend;
            public bool isDead;
            public int deathSeq;
            public float deathFallDirX;
            public float deathFallDirY;
            public float deathFallDirZ;
            public float animSpeed;
            public bool isAiming;
            public bool isGrounded;
            public int jumpState;
            public float animPhase;
            public int poseSeq;
        }

        [SerializeField] private string websocketUrl = "ws://127.0.0.1:5051";

        private ClientWebSocket socket;
        private CancellationTokenSource cts;
        private SemaphoreSlim sendSemaphore;
        private Task receiveTask;
        private string connectedTicketId = string.Empty;
        private bool isConnecting;
        private bool hasJoinAck;
        private float nextReconnectAllowedAt;
        private PoseMessage pendingPoseMessage;
        private int nextPoseSeq;
        private bool hasPendingPose;
        private bool poseSendLoopRunning;
        private float lastSendErrorLogAt;
        private readonly object snapshotLock = new object();
        private RealtimeSnapshot latestSnapshot;
        private bool hasLatestSnapshot;

        public bool IsConnected => socket != null && socket.State == WebSocketState.Open;
        public bool IsConnecting => isConnecting;
        public bool IsReady => IsConnected && hasJoinAck;
        public string ConnectedTicketId => connectedTicketId;
        public event Action<DamageMessage> DamageReceived;

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

            if (Time.unscaledTime < nextReconnectAllowedAt)
            {
                return;
            }

            if (IsReady && string.Equals(connectedTicketId, ticketId, StringComparison.Ordinal))
            {
                return;
            }

            if (isConnecting)
            {
                return;
            }

            _ = ConnectInternalAsync(ticketId);
        }

        public void Disconnect()
        {
            _ = DisconnectInternalAsync();
        }

        public void SendPose(
            Vector3 position,
            float yaw,
            float lookPitch = 0f,
            int shotSeq = 0,
            int reloadSeq = 0,
            int hitPlayerSeq = 0,
            int footstepSeq = 0,
            bool isCrouching = false,
            float wallAvoidBlend = 0f,
            bool isDead = false,
            int deathSeq = 0,
            Vector3 deathFallDirection = default,
            bool isAiming = false,
            float animSpeed = 0f,
            bool isGrounded = true,
            int jumpState = 0,
            float animPhase = 0f)
        {
            if (!IsConnected)
            {
                return;
            }

            pendingPoseMessage = new PoseMessage
            {
                type = "pose",
                position = new PositionDto
                {
                    x = position.x,
                    y = position.y,
                    z = position.z
                },
                yaw = yaw,
                lookPitch = lookPitch,
                shotSeq = Math.Max(0, shotSeq),
                reloadSeq = Math.Max(0, reloadSeq),
                hitPlayerSeq = Math.Max(0, hitPlayerSeq),
                footstepSeq = Math.Max(0, footstepSeq),
                isCrouching = isCrouching,
                wallAvoidBlend = Mathf.Clamp01(wallAvoidBlend),
                isDead = isDead,
                deathSeq = Math.Max(0, deathSeq),
                deathFallDirX = deathFallDirection.x,
                deathFallDirY = deathFallDirection.y,
                deathFallDirZ = deathFallDirection.z,
                animSpeed = Mathf.Clamp01(animSpeed),
                isAiming = isAiming,
                isGrounded = isGrounded,
                jumpState = Mathf.Clamp(jumpState, 0, 2),
                animPhase = Mathf.Repeat(animPhase, 1f),
                poseSeq = ++nextPoseSeq
            };
            hasPendingPose = true;
            if (!poseSendLoopRunning)
            {
                _ = FlushLatestPoseLoopAsync();
            }
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

        public void SendHit(string targetTicketId, float damage, Vector3 hitDirection)
        {
            if (!IsConnected || string.IsNullOrWhiteSpace(targetTicketId))
            {
                return;
            }

            var direction = hitDirection.sqrMagnitude > 0.0001f ? hitDirection.normalized : Vector3.forward;
            _ = SendJsonAsync(new HitMessage
            {
                type = "hit",
                targetTicketId = targetTicketId.Trim(),
                damage = Mathf.Max(0f, damage),
                dirX = direction.x,
                dirY = direction.y,
                dirZ = direction.z
            }, cts != null ? cts.Token : CancellationToken.None);
        }

        private async Task ConnectInternalAsync(string ticketId)
        {
            if (isConnecting)
            {
                return;
            }

            isConnecting = true;
            await DisconnectInternalAsync();

            try
            {
                cts = new CancellationTokenSource();
                sendSemaphore = new SemaphoreSlim(1, 1);
                socket = new ClientWebSocket();
                hasJoinAck = false;
                await socket.ConnectAsync(new Uri(websocketUrl), cts.Token);

                connectedTicketId = ticketId;
                await SendJsonAsync(new JoinMessage
                {
                    type = "join",
                    ticketId = ticketId
                }, cts.Token);
                Debug.Log($"[RealtimeTransportClient] Join sent ticket={ticketId}");

                receiveTask = ReceiveLoopAsync(socket, cts.Token);
                Debug.Log($"[RealtimeTransportClient] Connected to {websocketUrl} ticket={ticketId}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RealtimeTransportClient] Connect failed: {ex.Message}");
                nextReconnectAllowedAt = Time.unscaledTime + 0.35f;
                await DisconnectInternalAsync();
            }
            finally
            {
                isConnecting = false;
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ownerSocket, CancellationToken token)
        {
            var buffer = new byte[4096];
            var segment = new ArraySegment<byte>(buffer);
            var textBuilder = new StringBuilder(4096);

            while (!token.IsCancellationRequested && ownerSocket != null && ownerSocket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await ownerSocket.ReceiveAsync(segment, token);
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

            if (!token.IsCancellationRequested)
            {
                nextReconnectAllowedAt = Time.unscaledTime + 0.25f;
            }

            await DisconnectInternalAsync(ownerSocket);
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
                DamageMessage damageMessage = null;
                try
                {
                    damageMessage = JsonUtility.FromJson<DamageMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (damageMessage != null && string.Equals(damageMessage.type, "damage", StringComparison.Ordinal))
                {
                    DamageReceived?.Invoke(damageMessage);
                    return;
                }

                var joined = JsonUtility.FromJson<JoinedMessage>(json);
                if (joined != null &&
                    string.Equals(joined.type, "joined", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(joined.ticketId) &&
                    string.Equals(joined.ticketId, connectedTicketId, StringComparison.Ordinal))
                {
                    hasJoinAck = true;
                }
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
                if (Time.unscaledTime - lastSendErrorLogAt > 1f)
                {
                    lastSendErrorLogAt = Time.unscaledTime;
                    Debug.LogWarning($"[RealtimeTransportClient] Send failed payload={payload?.GetType().Name ?? "null"}");
                }
            }
            finally
            {
                sendSemaphore.Release();
            }
        }

        private async Task FlushLatestPoseLoopAsync()
        {
            if (poseSendLoopRunning)
            {
                return;
            }

            poseSendLoopRunning = true;
            try
            {
                while (hasPendingPose && socket != null && socket.State == WebSocketState.Open)
                {
                    var msg = pendingPoseMessage;
                    hasPendingPose = false;
                    await SendJsonAsync(msg, cts != null ? cts.Token : CancellationToken.None);
                }
            }
            catch
            {
                // ignored; regular reconnect flow handles broken socket.
            }
            finally
            {
                poseSendLoopRunning = false;
            }
        }

        private async Task DisconnectInternalAsync(ClientWebSocket ownerSocket = null)
        {
            if (ownerSocket != null && socket != ownerSocket)
            {
                return;
            }

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
            isConnecting = false;
            hasJoinAck = false;
            nextPoseSeq = 0;
            hasPendingPose = false;
            poseSendLoopRunning = false;
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
