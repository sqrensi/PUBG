using System.Collections;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.Network;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace ShooterPrototype.UI
{
    public sealed class GameHudController : MonoBehaviour
    {
        private const string CanvasObjectName = "RuntimeGameHudCanvas";
        private const string MutePrefKey = "client_audio_muted";

        private NetworkLauncher networkLauncher;
        private QueueApiClient queueApiClient;
        private string mainMenuSceneName = "MainMenu";

        private Canvas canvas;
        private Text connectionText;
        private Text playersText;
        private Text pingText;
        private Text fpsText;
        private Text ammoText;
        private Button backButton;
        private Button muteButton;
        private Text muteButtonLabel;
        private Coroutine pingRefreshCoroutine;
        private float fpsSmoothed;
        private int consecutivePingFailures;
        private bool returnToMenuRequested;
        private bool isMuted;

        public void Initialize(NetworkLauncher launcher, string menuSceneName)
        {
            networkLauncher = launcher;
            mainMenuSceneName = string.IsNullOrWhiteSpace(menuSceneName) ? "MainMenu" : menuSceneName;
            queueApiClient = FindObjectOfType<QueueApiClient>();

            if (networkLauncher != null)
            {
                networkLauncher.StatusChanged -= HandleNetworkStatusChanged;
                networkLauncher.StatusChanged += HandleNetworkStatusChanged;
            }

            EnsureHudExists();
            LoadMuteState();
            RefreshConnectionText();
            SetActiveForScene(false);
        }

        public void SetActiveForScene(bool isGameScene)
        {
            if (isGameScene)
            {
                EnsureEventSystemExists();
            }

            if (canvas == null)
            {
                EnsureHudExists();
            }

            if (canvas != null)
            {
                canvas.gameObject.SetActive(isGameScene);
            }

            if (isGameScene)
            {
                RefreshConnectionText();
                RefreshPlayersText();
                RefreshPingText();
                EnsurePingRefreshRunning();
            }
            else
            {
                StopPingRefresh();
            }
        }

        private void OnDestroy()
        {
            StopPingRefresh();

            if (networkLauncher != null)
            {
                networkLauncher.StatusChanged -= HandleNetworkStatusChanged;
            }
        }

        private void HandleNetworkStatusChanged(string status)
        {
            if (connectionText != null && canvas != null && canvas.gameObject.activeSelf)
            {
                connectionText.text = $"Status: {status}";
                RefreshPlayersText();
                RefreshPingText();
            }
        }

        private void HandleBackPressed()
        {
            if (backButton != null)
            {
                backButton.interactable = false;
            }

            StartCoroutine(LeaveMatchAndReturnRoutine());
        }

        private void HandleMutePressed()
        {
            isMuted = !isMuted;
            PlayerPrefs.SetInt(MutePrefKey, isMuted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyMuteState();
        }

        private void LoadMuteState()
        {
            isMuted = PlayerPrefs.GetInt(MutePrefKey, 0) == 1;
            ApplyMuteState();
        }

        private void ApplyMuteState()
        {
            AudioListener.volume = isMuted ? 0f : 1f;
            RefreshMuteButtonText();
        }

        private void EnsureHudExists()
        {
            if (canvas != null)
            {
                return;
            }

            var existingCanvas = transform.Find(CanvasObjectName);
            if (existingCanvas != null)
            {
                canvas = existingCanvas.GetComponent<Canvas>();
            }

            if (canvas == null)
            {
                var canvasObject = new GameObject(CanvasObjectName);
                canvasObject.transform.SetParent(transform, false);

                canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 500;

                canvasObject.AddComponent<CanvasScaler>();
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            EnsureEventSystemExists();
            BuildHudLayout(canvas.gameObject);
        }

        private void BuildHudLayout(GameObject rootCanvasObject)
        {
            if (connectionText != null &&
                playersText != null &&
                pingText != null &&
                fpsText != null &&
                ammoText != null &&
                backButton != null &&
                muteButton != null &&
                muteButtonLabel != null)
            {
                return;
            }

            var panelObject = new GameObject("TopBar");
            panelObject.transform.SetParent(rootCanvasObject.transform, false);

            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.sizeDelta = new Vector2(0f, 108f);
            panelRect.anchoredPosition = Vector2.zero;

            var panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.45f);

            connectionText = CreateLabel(panelObject.transform, "ConnectionText", new Vector2(10f, -10f), "Status: Connected");
            playersText = CreateLabel(panelObject.transform, "PlayersText", new Vector2(10f, -34f), "Players in match: --");
            pingText = CreateLabel(panelObject.transform, "PingText", new Vector2(10f, -58f), "Ping: -- ms");
            fpsText = CreateLabel(panelObject.transform, "FpsText", new Vector2(10f, -82f), "FPS: --");
            ammoText = CreateLabel(panelObject.transform, "AmmoText", new Vector2(10f, -106f), "Ammo: --/--");
            panelRect.sizeDelta = new Vector2(0f, 132f);

            var buttonObject = new GameObject("BackButton");
            buttonObject.transform.SetParent(panelObject.transform, false);

            var buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.sizeDelta = new Vector2(170f, 36f);
            buttonRect.anchoredPosition = new Vector2(-10f, 0f);

            var buttonImage = buttonObject.AddComponent<Image>();
            buttonImage.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            backButton = buttonObject.AddComponent<Button>();
            backButton.onClick.AddListener(HandleBackPressed);

            var buttonLabel = CreateLabel(buttonObject.transform, "Label", Vector2.zero, "Back to Menu");
            var buttonLabelRect = buttonLabel.rectTransform;
            buttonLabelRect.anchorMin = Vector2.zero;
            buttonLabelRect.anchorMax = Vector2.one;
            buttonLabelRect.offsetMin = Vector2.zero;
            buttonLabelRect.offsetMax = Vector2.zero;
            buttonLabel.alignment = TextAnchor.MiddleCenter;

            var muteObject = new GameObject("MuteButton");
            muteObject.transform.SetParent(panelObject.transform, false);

            var muteRect = muteObject.AddComponent<RectTransform>();
            muteRect.anchorMin = new Vector2(1f, 0.5f);
            muteRect.anchorMax = new Vector2(1f, 0.5f);
            muteRect.pivot = new Vector2(1f, 0.5f);
            muteRect.sizeDelta = new Vector2(170f, 36f);
            muteRect.anchoredPosition = new Vector2(-190f, 0f);

            var muteImage = muteObject.AddComponent<Image>();
            muteImage.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            muteButton = muteObject.AddComponent<Button>();
            muteButton.onClick.AddListener(HandleMutePressed);
            muteButtonLabel = CreateLabel(muteObject.transform, "Label", Vector2.zero, "");
            var muteLabelRect = muteButtonLabel.rectTransform;
            muteLabelRect.anchorMin = Vector2.zero;
            muteLabelRect.anchorMax = Vector2.one;
            muteLabelRect.offsetMin = Vector2.zero;
            muteLabelRect.offsetMax = Vector2.zero;
            muteButtonLabel.alignment = TextAnchor.MiddleCenter;
            RefreshMuteButtonText();
        }

        private Text CreateLabel(Transform parent, string objectName, Vector2 anchoredPosition, string textValue)
        {
            var labelObject = new GameObject(objectName);
            labelObject.transform.SetParent(parent, false);

            var rect = labelObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(520f, 22f);
            rect.anchoredPosition = anchoredPosition;

            var label = labelObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 16;
            label.alignment = TextAnchor.UpperLeft;
            label.color = Color.white;
            label.text = textValue;
            return label;
        }

        private void RefreshConnectionText()
        {
            if (connectionText == null)
            {
                return;
            }

            var state = networkLauncher != null && networkLauncher.IsClientConnected
                ? "Connected"
                : "Not connected";
            connectionText.text = $"Status: {state}";
        }

        private void RefreshPingText()
        {
            if (pingText == null)
            {
                return;
            }

            if (networkLauncher != null && networkLauncher.IsClientConnected && networkLauncher.LastConnectLatencyMs > 0)
            {
                pingText.text = $"Ping: {networkLauncher.LastConnectLatencyMs} ms (connect)";
                return;
            }

            pingText.text = "Ping: -- ms";
        }

        private void EnsurePingRefreshRunning()
        {
            if (pingRefreshCoroutine == null)
            {
                pingRefreshCoroutine = StartCoroutine(PingRefreshRoutine());
            }
        }

        private void StopPingRefresh()
        {
            if (pingRefreshCoroutine == null)
            {
                return;
            }

            StopCoroutine(pingRefreshCoroutine);
            pingRefreshCoroutine = null;
        }

        private IEnumerator PingRefreshRoutine()
        {
            while (true)
            {
                if (networkLauncher != null && networkLauncher.IsClientConnected)
                {
                    var pingTask = networkLauncher.MeasureCurrentServerPingMsAsync(1000);
                    while (!pingTask.IsCompleted)
                    {
                        yield return null;
                    }

                    var ping = pingTask.IsCompletedSuccessfully ? pingTask.Result : -1;
                    if (pingText != null)
                    {
                        pingText.text = ping > 0 ? $"Ping: {ping} ms" : "Ping: -- ms";
                    }

                    if (ping > 0)
                    {
                        consecutivePingFailures = 0;
                    }
                    else
                    {
                        consecutivePingFailures++;
                        if (!returnToMenuRequested && consecutivePingFailures >= 4)
                        {
                            returnToMenuRequested = true;
                            networkLauncher.DisconnectClient("Lost connection to dedicated server.");
                            StartCoroutine(LeaveMatchAndReturnRoutine());
                            yield break;
                        }
                    }

                    RefreshPlayersText();
                }
                else
                {
                    consecutivePingFailures = 0;
                    RefreshPingText();
                }

                RefreshFpsText();

                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        private IEnumerator LeaveMatchAndReturnRoutine()
        {
            var realtimeClient = FindObjectOfType<RealtimeTransportClient>();
            StopPingRefresh();

            if (queueApiClient != null && networkLauncher != null && !string.IsNullOrWhiteSpace(networkLauncher.CurrentTicketId))
            {
                var leaveCompleted = false;
                yield return StartCoroutine(queueApiClient.LeaveMatch(networkLauncher.CurrentTicketId, (_, __, ___) =>
                {
                    leaveCompleted = true;
                }));

                // If request hangs/fails, do not block returning to menu.
                if (!leaveCompleted)
                {
                    yield return null;
                }
            }

            realtimeClient?.Disconnect();
            networkLauncher?.DisconnectClient("Client returned to MainMenu.");
            SceneManager.LoadScene(mainMenuSceneName);

            if (backButton != null)
            {
                backButton.interactable = true;
            }

            returnToMenuRequested = false;
            consecutivePingFailures = 0;
        }

        private void RefreshPlayersText()
        {
            if (playersText == null)
            {
                return;
            }

            var count = networkLauncher != null ? networkLauncher.CurrentMatchPlayerCount : 0;
            playersText.text = count > 0
                ? $"Players in match: {count}"
                : "Players in match: --";
        }

        private void RefreshFpsText()
        {
            if (fpsText == null)
            {
                return;
            }

            var dt = Time.unscaledDeltaTime;
            if (dt <= 0.00001f)
            {
                return;
            }

            var currentFps = 1f / dt;
            fpsSmoothed = fpsSmoothed <= 0f
                ? currentFps
                : Mathf.Lerp(fpsSmoothed, currentFps, 0.15f);

            fpsText.text = $"FPS: {Mathf.RoundToInt(fpsSmoothed)}";
            RefreshAmmoText();
        }

        private void RefreshAmmoText()
        {
            if (ammoText == null)
            {
                return;
            }

            var local = FindObjectOfType<ShooterPrototype.Player.LocalPlayerMarker>();
            var weapon = local != null ? local.GetComponent<ShooterPrototype.Player.PlayerWeaponController>() : null;
            if (weapon == null)
            {
                ammoText.text = "Ammo: --/--";
                return;
            }

            var suffix = weapon.IsReloading ? " (reloading)" : string.Empty;
            ammoText.text = $"Ammo: {weapon.CurrentAmmo}/{weapon.MagazineSize}{suffix}";
        }

        private void RefreshMuteButtonText()
        {
            if (muteButtonLabel == null)
            {
                return;
            }

            muteButtonLabel.text = isMuted ? "Sound: OFF" : "Sound: ON";
        }

        private static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null && EventSystem.current.gameObject.activeInHierarchy)
            {
                return;
            }

            var eventSystemObject = new GameObject("RuntimeEventSystem");
            eventSystemObject.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
#else
            eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
