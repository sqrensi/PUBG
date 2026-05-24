using ShooterPrototype.Matchmaking;
using ShooterPrototype.Network;
using ShooterPrototype.Player;
using ShooterPrototype.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShooterPrototype.Bootstrap
{
    public sealed class GameBootstrap : MonoBehaviour
    {
        private static GameBootstrap instance;

        [SerializeField] private LaunchMode overrideLaunchMode = LaunchMode.AutoDetect;
        [SerializeField] private bool keepAliveAcrossScenes = true;
        [SerializeField] private bool runClientInBackground = true;
        [SerializeField] private NetworkConfig networkConfig;
        [SerializeField] private NetworkLauncher networkLauncher;
        [SerializeField] private bool createRuntimeQueueApiClient = true;
        [SerializeField] private bool createRuntimeRealtimeTransportClient = true;
        [SerializeField] private bool createRuntimeGameHud = true;
        [SerializeField] private bool createRuntimePlayerSpawnManager = true;
        [SerializeField] private string mainMenuSceneName = "MainMenu";
        [SerializeField] private string gameSceneName = "Game";

        private bool initialized;
        private QueueApiClient queueApiClient;
        private RealtimeTransportClient realtimeTransportClient;
        private GameHudController gameHudController;
        private PlayerSpawnManager playerSpawnManager;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.Log("[GameBootstrap] Duplicate bootstrap detected. Destroying newest instance.");
                Destroy(gameObject);
                return;
            }

            instance = this;

            if (initialized)
            {
                return;
            }

            initialized = true;

            if (keepAliveAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }

            if (!Application.isBatchMode && runClientInBackground)
            {
                Application.runInBackground = true;
                Debug.Log("[GameBootstrap] Enabled Application.runInBackground for multiplayer local testing.");
            }

            if (!Application.isBatchMode)
            {
                QualitySettings.antiAliasing = Mathf.Max(QualitySettings.antiAliasing, 4);
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            }

            if (networkLauncher == null)
            {
                networkLauncher = GetComponent<NetworkLauncher>();
            }

            if (networkLauncher == null)
            {
                Debug.LogError("[GameBootstrap] NetworkLauncher component is missing.");
                return;
            }

            if (networkConfig == null)
            {
                Debug.LogError("[GameBootstrap] NetworkConfig asset is not assigned.");
                return;
            }

            networkLauncher.Initialize(networkConfig);

            if (!Application.isBatchMode && createRuntimeQueueApiClient)
            {
                queueApiClient = GetComponent<QueueApiClient>();
                if (queueApiClient == null)
                {
                    queueApiClient = gameObject.AddComponent<QueueApiClient>();
                }

                queueApiClient.Configure(networkConfig.QueueApiBaseUrl, networkConfig.QueueRequestTimeoutSeconds);
            }

            if (!Application.isBatchMode && createRuntimeRealtimeTransportClient)
            {
                realtimeTransportClient = GetComponent<RealtimeTransportClient>();
                if (realtimeTransportClient == null)
                {
                    realtimeTransportClient = gameObject.AddComponent<RealtimeTransportClient>();
                }

                realtimeTransportClient.Configure(networkConfig.RealtimeWsUrl);
            }

            var mode = ResolveLaunchMode();
            Debug.Log($"[GameBootstrap] Launch mode resolved as: {mode}");

            if (mode == LaunchMode.DedicatedServer && networkLauncher.CanAutoStartMockServer())
            {
                networkLauncher.StartDedicatedServer();
            }
            else if (mode == LaunchMode.DedicatedServer)
            {
                Debug.LogWarning("[GameBootstrap] Dedicated server mode detected, but auto-start is disabled in NetworkConfig.");
            }

            if (!Application.isBatchMode && createRuntimeGameHud)
            {
                gameHudController = gameObject.GetComponent<GameHudController>();
                if (gameHudController == null)
                {
                    gameHudController = gameObject.AddComponent<GameHudController>();
                }

                gameHudController.Initialize(networkLauncher, mainMenuSceneName);
                HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            }

            if (!Application.isBatchMode && createRuntimePlayerSpawnManager)
            {
                playerSpawnManager = gameObject.GetComponent<PlayerSpawnManager>();
                if (playerSpawnManager == null)
                {
                    playerSpawnManager = gameObject.AddComponent<PlayerSpawnManager>();
                }

                playerSpawnManager.Configure(gameSceneName);
                playerSpawnManager.HandleSceneLoaded(SceneManager.GetActiveScene());
            }

        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private LaunchMode ResolveLaunchMode()
        {
            if (overrideLaunchMode != LaunchMode.AutoDetect)
            {
                return overrideLaunchMode;
            }

            return Application.isBatchMode ? LaunchMode.DedicatedServer : LaunchMode.Client;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode _)
        {
            if (gameHudController != null)
            {
                var isGameScene = scene.name == gameSceneName;
                gameHudController.SetActiveForScene(isGameScene);
            }

            if (playerSpawnManager != null)
            {
                playerSpawnManager.HandleSceneLoaded(scene);
            }
        }
    }
}
