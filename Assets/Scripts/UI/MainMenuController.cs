using System.Collections;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.Network;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    public sealed class MainMenuController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private NetworkLauncher networkLauncher;
        [SerializeField] private QueueApiClient queueApiClient;
        [SerializeField] private NetworkConfig networkConfig;

        [Header("UI")]
        [SerializeField] private Button startButton;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text startButtonText;

        [Header("Connection State")]
        [SerializeField] private string idleStatusText = "Готов к подключению";
        [SerializeField] private string searchingStatusText = "Поиск матча...";
        [SerializeField] private string connectingStatusText = "Матч найден. Подключение к серверу...";
        [SerializeField] private string connectionFailedStatusText = "Не удалось подключиться. Проверь сервер и попробуй снова.";
        [SerializeField] private string connectedStatusText = "Подключение успешно.";
        [SerializeField] private string queueCancelledStatusText = "Поиск матча отменен.";

        [Header("Scene Flow")]
        [SerializeField] private bool autoLoadGameSceneOnSuccess = true;
        [SerializeField] private string gameSceneName = "Game";

        [Header("Reliability")]
        [SerializeField] private int enqueueRetryCount = 2;
        [SerializeField] private float enqueueRetryDelaySeconds = 0.4f;

        private Coroutine queuePollingCoroutine;
        private bool isQueueing;
        private string currentTicketId = string.Empty;
        private string localPlayerId;

        private void Awake()
        {
            EnsureDependencies();

            if (startButton != null)
            {
                startButton.onClick.AddListener(OnStartPressed);
            }

            localPlayerId = BuildLocalPlayerId();
            SetStatus(idleStatusText);
            SetStartButtonState(isQueueing: false, interactable: true);
        }

        private void OnEnable()
        {
            EnsureDependencies();

            if (networkLauncher == null)
            {
                Debug.LogWarning("[MainMenuController] NetworkLauncher is not set.");
                return;
            }

            networkLauncher.StatusChanged += HandleStatusChanged;

            SetStatus(idleStatusText);
            SetStartButtonState(isQueueing: false, interactable: true);
        }

        private void OnDisable()
        {
            if (queuePollingCoroutine != null)
            {
                StopCoroutine(queuePollingCoroutine);
                queuePollingCoroutine = null;
            }

            isQueueing = false;
            currentTicketId = string.Empty;

            if (networkLauncher == null)
            {
                return;
            }

            networkLauncher.StatusChanged -= HandleStatusChanged;
        }

        public void OnStartPressed()
        {
            EnsureDependencies();

            if (networkLauncher == null)
            {
                SetStatus("Ошибка: NetworkLauncher не привязан.");
                return;
            }

            if (queueApiClient == null)
            {
                SetStatus("Ошибка: QueueApiClient не привязан.");
                return;
            }

            if (isQueueing)
            {
                CancelQueue();
                return;
            }

            if (networkLauncher.IsConnecting)
            {
                SetStatus("Подключение уже выполняется...");
                return;
            }

            StartQueueSearch();
        }

        private void HandleStatusChanged(string message)
        {
            SetStatus(message);
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }

            Debug.Log($"[MainMenuController] {message}");
        }

        private void StartQueueSearch()
        {
            if (queuePollingCoroutine != null)
            {
                StopCoroutine(queuePollingCoroutine);
            }

            queuePollingCoroutine = StartCoroutine(EnqueueAndPollRoutine());
        }

        private void CancelQueue()
        {
            if (!isQueueing)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(currentTicketId))
            {
                StartCoroutine(SendDequeueBestEffort(currentTicketId));
            }

            if (queuePollingCoroutine != null)
            {
                StopCoroutine(queuePollingCoroutine);
                queuePollingCoroutine = null;
            }

            isQueueing = false;
            currentTicketId = string.Empty;
            SetStatus(queueCancelledStatusText);
            SetStartButtonState(isQueueing: false, interactable: true);
        }

        private IEnumerator EnqueueAndPollRoutine()
        {
            var realtimeClient = FindObjectOfType<RealtimeTransportClient>();
            realtimeClient?.Disconnect();
            networkLauncher?.DisconnectClient("Preparing queue search.");

            if (queueApiClient != null && networkLauncher != null && !string.IsNullOrWhiteSpace(networkLauncher.CurrentTicketId))
            {
                yield return StartCoroutine(SendLeaveMatchBestEffort(networkLauncher.CurrentTicketId));
                networkLauncher.ClearMatchContext();
            }

            SetStatus(searchingStatusText);
            SetStartButtonState(isQueueing: true, interactable: false);

            var enqueueCompleted = false;
            var enqueueOk = false;
            QueueEnqueueResponse enqueueResponse = null;
            var enqueueError = string.Empty;

            var attempts = Mathf.Max(1, enqueueRetryCount + 1);
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                enqueueCompleted = false;
                enqueueOk = false;
                enqueueResponse = null;
                enqueueError = string.Empty;

                yield return StartCoroutine(queueApiClient.Enqueue(localPlayerId, (ok, response, error) =>
                {
                    enqueueCompleted = true;
                    enqueueOk = ok;
                    enqueueResponse = response;
                    enqueueError = error;
                }));

                if (enqueueCompleted && enqueueOk && enqueueResponse != null && !string.IsNullOrWhiteSpace(enqueueResponse.ticketId))
                {
                    break;
                }

                if (attempt < attempts)
                {
                    SetStatus($"{searchingStatusText} retry {attempt}/{attempts - 1}...");
                    yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, enqueueRetryDelaySeconds));
                }
            }

            if (!enqueueCompleted || !enqueueOk || enqueueResponse == null || string.IsNullOrWhiteSpace(enqueueResponse.ticketId))
            {
                isQueueing = false;
                queuePollingCoroutine = null;
                SetStatus($"Не удалось встать в очередь. {enqueueError}".Trim());
                SetStartButtonState(isQueueing: false, interactable: true);
                yield break;
            }

            currentTicketId = enqueueResponse.ticketId;
            isQueueing = true;
            SetStartButtonState(isQueueing: true, interactable: true);

            var pollDelay = GetQueuePollInterval();
            while (isQueueing && !string.IsNullOrWhiteSpace(currentTicketId))
            {
                var statusCompleted = false;
                var statusOk = false;
                QueueTicketStatusResponse statusResponse = null;
                var statusError = string.Empty;

                yield return StartCoroutine(queueApiClient.GetTicketStatus(currentTicketId, (ok, response, error) =>
                {
                    statusCompleted = true;
                    statusOk = ok;
                    statusResponse = response;
                    statusError = error;
                }));

                if (!statusCompleted || !statusOk || statusResponse == null)
                {
                    isQueueing = false;
                    queuePollingCoroutine = null;
                    currentTicketId = string.Empty;
                    SetStatus($"Ошибка статуса очереди: {statusError}".Trim());
                    SetStartButtonState(isQueueing: false, interactable: true);
                    yield break;
                }

                var status = statusResponse.status ?? string.Empty;
                if (status.Equals("Queued", System.StringComparison.OrdinalIgnoreCase))
                {
                    SetStatus($"{searchingStatusText} ({statusResponse.queueDurationSeconds:F1}s)");
                }
                else if (status.Equals("Matched", System.StringComparison.OrdinalIgnoreCase))
                {
                    isQueueing = false;
                    queuePollingCoroutine = null;
                    currentTicketId = string.Empty;
                    SetStartButtonState(isQueueing: false, interactable: false);
                    var playerCount = Mathf.Max(1, statusResponse.matchedPlayerCount);
                    networkLauncher.SetMatchContext(statusResponse.matchId, playerCount, statusResponse.ticketId);
                    SetStatus($"{connectingStatusText} {statusResponse.serverAddress}:{statusResponse.serverPort} | players: {playerCount}");
                    yield return StartCoroutine(ConnectAndEnterGameRoutine(statusResponse.serverAddress, statusResponse.serverPort));
                    yield break;
                }
                else if (status.Equals("Cancelled", System.StringComparison.OrdinalIgnoreCase))
                {
                    isQueueing = false;
                    queuePollingCoroutine = null;
                    currentTicketId = string.Empty;
                    SetStatus(queueCancelledStatusText);
                    SetStartButtonState(isQueueing: false, interactable: true);
                    yield break;
                }
                else if (status.Equals("Expired", System.StringComparison.OrdinalIgnoreCase))
                {
                    isQueueing = false;
                    queuePollingCoroutine = null;
                    currentTicketId = string.Empty;
                    SetStatus("Поиск матча истек. Нажми Start, чтобы попробовать снова.");
                    SetStartButtonState(isQueueing: false, interactable: true);
                    yield break;
                }
                else if (status.Equals("Disconnected", System.StringComparison.OrdinalIgnoreCase) ||
                         status.Equals("Left", System.StringComparison.OrdinalIgnoreCase))
                {
                    isQueueing = false;
                    queuePollingCoroutine = null;
                    currentTicketId = string.Empty;
                    SetStatus("Тикет больше не активен. Нажми Start, чтобы войти в новую очередь.");
                    SetStartButtonState(isQueueing: false, interactable: true);
                    yield break;
                }
                else
                {
                    isQueueing = false;
                    queuePollingCoroutine = null;
                    currentTicketId = string.Empty;
                    SetStatus($"Неизвестный статус тикета: {status}. Нажми Start и попробуй снова.");
                    SetStartButtonState(isQueueing: false, interactable: true);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(pollDelay);
            }

            queuePollingCoroutine = null;
        }

        private void SetStartButtonState(bool isQueueing, bool interactable)
        {
            if (startButton != null)
            {
                startButton.interactable = interactable;
            }

            if (startButtonText != null)
            {
                startButtonText.text = isQueueing ? "Cancel Queue" : "Start";
            }
        }

        private static string BuildLocalPlayerId()
        {
            var deviceId = SystemInfo.deviceUniqueIdentifier;
            var processId = 0;
            try
            {
                processId = System.Diagnostics.Process.GetCurrentProcess().Id;
            }
            catch
            {
                processId = UnityEngine.Random.Range(1000, 99999);
            }

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return $"player-{System.Guid.NewGuid():N}-{processId}";
            }

            return $"player-{Mathf.Abs(deviceId.GetHashCode())}-{processId}";
        }

        private void EnsureDependencies()
        {
            if (networkLauncher == null)
            {
                networkLauncher = FindObjectOfType<NetworkLauncher>();
            }

            if (queueApiClient == null)
            {
                queueApiClient = FindObjectOfType<QueueApiClient>();
            }

            if (queueApiClient == null && networkLauncher != null && !Application.isBatchMode)
            {
                queueApiClient = networkLauncher.GetComponent<QueueApiClient>();
                if (queueApiClient == null)
                {
                    queueApiClient = networkLauncher.gameObject.AddComponent<QueueApiClient>();
                    Debug.Log("[MainMenuController] QueueApiClient auto-created on NetworkLauncher object.");
                }
            }

            if (networkConfig == null && networkLauncher != null)
            {
                networkConfig = networkLauncher.Config;
            }

            if (queueApiClient != null)
            {
                var baseUrl = networkConfig != null ? networkConfig.QueueApiBaseUrl : "http://127.0.0.1:5050";
                var timeout = networkConfig != null ? networkConfig.QueueRequestTimeoutSeconds : 5f;
                queueApiClient.Configure(baseUrl, timeout);
            }
        }

        private float GetQueuePollInterval()
        {
            if (networkConfig != null)
            {
                return Mathf.Max(0.1f, networkConfig.QueuePollIntervalSeconds);
            }

            return 0.5f;
        }

        private IEnumerator SendDequeueBestEffort(string ticketId)
        {
            if (queueApiClient == null || string.IsNullOrWhiteSpace(ticketId))
            {
                yield break;
            }

            yield return StartCoroutine(queueApiClient.Dequeue(ticketId, (_, __, ___) => { }));
        }

        private IEnumerator SendLeaveMatchBestEffort(string ticketId)
        {
            if (queueApiClient == null || string.IsNullOrWhiteSpace(ticketId))
            {
                yield break;
            }

            yield return StartCoroutine(queueApiClient.LeaveMatch(ticketId, (_, __, ___) => { }));
        }

        private void TryLoadGameScene()
        {
            if (string.IsNullOrWhiteSpace(gameSceneName))
            {
                SetStatus("Сцена Game не задана. Остаемся в MainMenu.");
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(gameSceneName))
            {
                SetStatus($"Сцена '{gameSceneName}' не найдена в Build Settings.");
                return;
            }

            SetStatus($"Загрузка сцены '{gameSceneName}'...");
            SceneManager.LoadScene(gameSceneName);
        }

        private IEnumerator ConnectAndEnterGameRoutine(string address, int port)
        {
            var connectTask = networkLauncher.ConnectToServerAsync(address, port);
            while (!connectTask.IsCompleted)
            {
                yield return null;
            }

            if (connectTask.IsFaulted)
            {
                Debug.LogWarning($"[MainMenuController] Connect task faulted: {connectTask.Exception?.GetBaseException().Message}");
            }

            var connected = networkLauncher != null && networkLauncher.IsClientConnected;
            if (!connected)
            {
                if (queueApiClient != null && networkLauncher != null && !string.IsNullOrWhiteSpace(networkLauncher.CurrentTicketId))
                {
                    yield return StartCoroutine(SendLeaveMatchBestEffort(networkLauncher.CurrentTicketId));
                }

                if (networkLauncher != null && !string.IsNullOrWhiteSpace(networkLauncher.LastConnectionError))
                {
                    SetStatus($"{connectionFailedStatusText} ({networkLauncher.LastConnectionError})");
                }
                else
                {
                    SetStatus(connectionFailedStatusText);
                }

                networkLauncher?.ClearMatchContext();
                SetStartButtonState(isQueueing: false, interactable: true);
                yield break;
            }

            SetStatus(connectedStatusText);
            SetStartButtonState(isQueueing: false, interactable: true);
            if (autoLoadGameSceneOnSuccess)
            {
                TryLoadGameScene();
            }
        }
    }
}
