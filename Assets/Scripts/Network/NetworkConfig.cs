using UnityEngine;

namespace ShooterPrototype.Network
{
    [CreateAssetMenu(
        fileName = "NetworkConfig",
        menuName = "Shooter Prototype/Network Config",
        order = 1)]
    public sealed class NetworkConfig : ScriptableObject
    {
        [Header("Connection")]
        [SerializeField] private string serverAddress = "127.0.0.1";
        [SerializeField] private int serverPort = 7777;
        [SerializeField] private float connectTimeoutSeconds = 5f;

        [Header("Queue API")]
        [SerializeField] private string queueApiBaseUrl = "http://127.0.0.1:5050";
        [SerializeField] private string realtimeWsUrl = "ws://127.0.0.1:5051";
        [SerializeField] private float queueRequestTimeoutSeconds = 5f;
        [SerializeField] private float queuePollIntervalSeconds = 0.5f;

        [Header("Mock Dedicated Server")]
        [SerializeField] private bool autoStartMockServerInBatchMode = true;
        [SerializeField] private bool allowMockServerInEditor = false;

        public string ServerAddress => serverAddress;
        public int ServerPort => serverPort;
        public float ConnectTimeoutSeconds => connectTimeoutSeconds;
        public string QueueApiBaseUrl => queueApiBaseUrl;
        public string RealtimeWsUrl => realtimeWsUrl;
        public float QueueRequestTimeoutSeconds => queueRequestTimeoutSeconds;
        public float QueuePollIntervalSeconds => queuePollIntervalSeconds;
        public bool AutoStartMockServerInBatchMode => autoStartMockServerInBatchMode;
        public bool AllowMockServerInEditor => allowMockServerInEditor;
    }
}
