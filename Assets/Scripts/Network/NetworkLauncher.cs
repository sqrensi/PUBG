using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ShooterPrototype.Network
{
    public sealed class NetworkLauncher : MonoBehaviour
    {
        [SerializeField] private NetworkConfig config;

        private TcpListener mockServerListener;
        private CancellationTokenSource serverCancellation;
        private bool isConnecting;
        private bool isClientConnected;
        private string lastConnectionError = string.Empty;
        private int lastConnectLatencyMs = -1;
        private int lastMeasuredPingMs = -1;
        private string currentMatchId = string.Empty;
        private int currentMatchPlayerCount;
        private string currentTicketId = string.Empty;
        private string connectedServerAddress = string.Empty;
        private int connectedServerPort;

        public event Action<string> StatusChanged;
        public event Action<bool> ConnectionFinished;

        public bool IsMockServerRunning => mockServerListener != null;
        public bool IsConnecting => isConnecting;
        public bool IsClientConnected => isClientConnected;
        public NetworkConfig Config => config;
        public string LastConnectionError => lastConnectionError;
        public int LastConnectLatencyMs => lastConnectLatencyMs;
        public int LastMeasuredPingMs => lastMeasuredPingMs > 0 ? lastMeasuredPingMs : lastConnectLatencyMs;
        public string CurrentMatchId => currentMatchId;
        public int CurrentMatchPlayerCount => currentMatchPlayerCount;
        public string CurrentTicketId => currentTicketId;

        public void Initialize(NetworkConfig networkConfig)
        {
            config = networkConfig;
            EmitStatus("NetworkLauncher initialized.");
        }

        public void StartDedicatedServer()
        {
            if (IsMockServerRunning)
            {
                EmitStatus("Dedicated server is already running.");
                return;
            }

            if (config == null)
            {
                EmitStatus("NetworkConfig is missing. Cannot start dedicated server.");
                return;
            }

            try
            {
                mockServerListener = new TcpListener(
                    System.Net.IPAddress.Any,
                    config.ServerPort);
                mockServerListener.Start();

                serverCancellation = new CancellationTokenSource();
                _ = AcceptLoopAsync(serverCancellation.Token);

                EmitStatus(
                    $"Dedicated server started on {config.ServerAddress}:{config.ServerPort}.");
            }
            catch (Exception exception)
            {
                EmitStatus($"Failed to start dedicated server: {exception.Message}");
                StopDedicatedServer();
            }
        }

        public void StopDedicatedServer()
        {
            if (!IsMockServerRunning)
            {
                return;
            }

            try
            {
                serverCancellation?.Cancel();
                mockServerListener?.Stop();
            }
            catch (Exception exception)
            {
                EmitStatus($"Error while stopping server: {exception.Message}");
            }
            finally
            {
                mockServerListener = null;
                serverCancellation?.Dispose();
                serverCancellation = null;
                EmitStatus("Dedicated server stopped.");
            }
        }

        public void ConnectToConfiguredServer()
        {
            if (config == null)
            {
                EmitStatus("NetworkConfig is missing. Cannot connect.");
                ConnectionFinished?.Invoke(false);
                return;
            }

            _ = ConnectToServerAsync(config.ServerAddress, config.ServerPort);
        }

        public async Task ConnectToServerAsync(string address, int port)
        {
            if (isConnecting)
            {
                EmitStatus("Connection is already in progress.");
                return;
            }

            if (config == null)
            {
                EmitStatus("NetworkConfig is missing. Cannot connect.");
                ConnectionFinished?.Invoke(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(address))
            {
                lastConnectionError = "Server address is empty.";
                EmitStatus(lastConnectionError);
                ConnectionFinished?.Invoke(false);
                return;
            }

            if (port <= 0 || port > 65535)
            {
                lastConnectionError = $"Invalid server port: {port}.";
                EmitStatus(lastConnectionError);
                ConnectionFinished?.Invoke(false);
                return;
            }

            isConnecting = true;
            isClientConnected = false;
            lastConnectLatencyMs = -1;
            lastConnectionError = string.Empty;
            EmitStatus($"Connecting to server {address}:{port}...");

            var timeout = Mathf.Max(1, Mathf.RoundToInt(config.ConnectTimeoutSeconds * 1000f));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var connected = await TryConnectTcpAsync(address, port, timeout);
            stopwatch.Stop();

            isConnecting = false;

            if (connected)
            {
                isClientConnected = true;
                lastConnectLatencyMs = Mathf.Max(1, (int)stopwatch.ElapsedMilliseconds);
                lastMeasuredPingMs = lastConnectLatencyMs;
                connectedServerAddress = address;
                connectedServerPort = port;
                EmitStatus($"Connected to server {address}:{port}.");
            }
            else
            {
                isClientConnected = false;
                lastConnectLatencyMs = -1;
                connectedServerAddress = string.Empty;
                connectedServerPort = 0;
                lastConnectionError = $"Failed to connect to server {address}:{port}.";
                EmitStatus(lastConnectionError);
            }

            ConnectionFinished?.Invoke(connected);
        }

        public bool CanAutoStartMockServer()
        {
            if (config == null)
            {
                return false;
            }

            if (Application.isBatchMode && config.AutoStartMockServerInBatchMode)
            {
                return true;
            }

            if (!Application.isBatchMode && config.AllowMockServerInEditor)
            {
                return true;
            }

            return false;
        }

        public void SetMatchContext(string matchId, int playerCount, string ticketId = null)
        {
            currentMatchId = string.IsNullOrWhiteSpace(matchId) ? string.Empty : matchId;
            currentMatchPlayerCount = Mathf.Max(0, playerCount);
            if (!string.IsNullOrWhiteSpace(ticketId))
            {
                currentTicketId = ticketId;
            }
        }

        public void SetCurrentMatchPlayerCount(int playerCount)
        {
            currentMatchPlayerCount = Mathf.Max(0, playerCount);
        }

        public void ClearMatchContext()
        {
            currentMatchId = string.Empty;
            currentMatchPlayerCount = 0;
            currentTicketId = string.Empty;
        }

        public void DisconnectClient(string reason = "Client disconnected.")
        {
            if (!isClientConnected)
            {
                isConnecting = false;
                lastConnectLatencyMs = -1;
                connectedServerAddress = string.Empty;
                connectedServerPort = 0;
                ClearMatchContext();
                EmitStatus("Disconnect requested, but client is already disconnected.");
                return;
            }

            isClientConnected = false;
            lastConnectLatencyMs = -1;
            ClearMatchContext();
            connectedServerAddress = string.Empty;
            connectedServerPort = 0;
            EmitStatus(reason);
        }

        public async Task<int> MeasureCurrentServerPingMsAsync(int timeoutMilliseconds = 1000)
        {
            if (!isClientConnected || string.IsNullOrWhiteSpace(connectedServerAddress) || connectedServerPort <= 0)
            {
                return -1;
            }

            var timeout = Mathf.Max(100, timeoutMilliseconds);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var connected = await TryConnectTcpAsync(connectedServerAddress, connectedServerPort, timeout);
            stopwatch.Stop();

            if (!connected)
            {
                return -1;
            }

            var pingMs = Mathf.Max(1, (int)stopwatch.ElapsedMilliseconds);
            lastMeasuredPingMs = pingMs;
            return pingMs;
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && mockServerListener != null)
            {
                try
                {
                    var client = await mockServerListener.AcceptTcpClientAsync();
                    client.Close();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        EmitStatus($"Server accept loop error: {exception.Message}");
                    }

                    break;
                }
            }
        }

        private async Task<bool> TryConnectTcpAsync(string address, int port, int timeoutMilliseconds)
        {
            TcpClient client = null;
            try
            {
                client = new TcpClient();
                var connectTask = client.ConnectAsync(address, port);
                var timeoutTask = Task.Delay(timeoutMilliseconds);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask != connectTask)
                {
                    return false;
                }

                // Re-await to propagate possible socket exceptions.
                await connectTask;
                return client.Connected;
            }
            catch
            {
                return false;
            }
            finally
            {
                client?.Close();
            }
        }

        private void EmitStatus(string message)
        {
            Debug.Log($"[NetworkLauncher] {message}");
            StatusChanged?.Invoke(message);
        }

        private void OnDestroy()
        {
            StopDedicatedServer();
        }
    }
}
