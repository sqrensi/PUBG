using System.Collections.Generic;
using ShooterPrototype.Network;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShooterPrototype.Player
{
    public sealed class PlayerSpawnManager : MonoBehaviour
    {
        [Header("Scene")]
        [SerializeField] private string gameSceneName = "Game";

        [Header("Player")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private GameObject remotePlayerPrefab;
        [SerializeField] private string localPlayerObjectName = "LocalPlayer";
        [SerializeField] private bool enableMatchPresenceSync = true;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float spawnProbeHeight = 8f;
        [SerializeField] private float spawnProbeDistance = 40f;

        [Header("Spawn Points")]
        [SerializeField] private Transform spawnPointsRoot;
        [SerializeField] private bool autoCollectSpawnPointsFromRoot = true;
        [SerializeField] private bool useRandomSpawnPoint = true;
        [SerializeField] private List<Transform> spawnPoints = new List<Transform>();

        public void Configure(string targetGameSceneName)
        {
            gameSceneName = string.IsNullOrWhiteSpace(targetGameSceneName) ? "Game" : targetGameSceneName;
        }

        public void HandleSceneLoaded(Scene scene)
        {
            if (scene.name != gameSceneName)
            {
                return;
            }

            EnsureSpawnPoints(scene);
            SpawnLocalPlayerIfNeeded();
        }

        [ContextMenu("Rebuild Spawn Points From Root")]
        public void RebuildSpawnPointsFromRoot()
        {
            if (spawnPoints == null)
            {
                spawnPoints = new List<Transform>();
            }
            else
            {
                spawnPoints.Clear();
            }

            if (spawnPointsRoot == null)
            {
                return;
            }

            for (var i = 0; i < spawnPointsRoot.childCount; i++)
            {
                var child = spawnPointsRoot.GetChild(i);
                if (child != null)
                {
                    spawnPoints.Add(child);
                }
            }
        }

        private void EnsureSpawnPoints(Scene scene)
        {
            if (!autoCollectSpawnPointsFromRoot)
            {
                return;
            }

            if (spawnPointsRoot == null || !spawnPointsRoot.gameObject.scene.IsValid() || spawnPointsRoot.gameObject.scene != scene)
            {
                var rootCandidate = GameObject.Find("SpawnPoints");
                spawnPointsRoot = rootCandidate != null ? rootCandidate.transform : null;
            }

            if (spawnPointsRoot != null)
            {
                RebuildSpawnPointsFromRoot();
            }
        }

        private void SpawnLocalPlayerIfNeeded()
        {
            if (playerPrefab == null)
            {
                Debug.LogWarning("[PlayerSpawnManager] Player prefab is not assigned.");
                return;
            }

            var existingLocalPlayer = FindObjectOfType<LocalPlayerMarker>();
            if (existingLocalPlayer != null)
            {
                return;
            }

            var hasSpawn = TryResolveSpawnPose(out var resolvedPosition, out var resolvedRotation);
            if (!hasSpawn)
            {
                resolvedPosition = ResolveGroundedSpawnPosition(Vector3.zero);
                resolvedRotation = Quaternion.identity;
            }

            var instance = Instantiate(playerPrefab, resolvedPosition, resolvedRotation);
            instance.name = localPlayerObjectName;
            if (instance.GetComponent<LocalPlayerMarker>() == null)
            {
                instance.AddComponent<LocalPlayerMarker>();
            }
            var identity = instance.GetComponent<PlayerNetworkIdentity>();
            if (identity == null)
            {
                identity = instance.AddComponent<PlayerNetworkIdentity>();
            }
            if (instance.GetComponent<PlayerAudioController>() == null)
            {
                instance.AddComponent<PlayerAudioController>();
            }
            if (instance.GetComponent<PlayerHealth>() == null)
            {
                instance.AddComponent<PlayerHealth>();
            }

            var weaponController = instance.GetComponent<PlayerWeaponController>();
            if (weaponController == null)
            {
                weaponController = instance.AddComponent<PlayerWeaponController>();
            }
            var localCamera = instance.GetComponentInChildren<Camera>(true);
            if (localCamera != null)
            {
                ApplyCameraClaritySettings(localCamera);
                weaponController.Configure(localCamera, null);
            }

            if (enableMatchPresenceSync)
            {
                AttachPresenceSync(instance);
            }

            Debug.Log($"[PlayerSpawnManager] Spawned local player at {resolvedPosition}.");
        }

        private void AttachPresenceSync(GameObject localPlayer)
        {
            var launcher = FindObjectOfType<NetworkLauncher>();
            if (launcher == null || string.IsNullOrWhiteSpace(launcher.CurrentTicketId))
            {
                return;
            }

            var realtimeClient = FindObjectOfType<RealtimeTransportClient>();
            if (realtimeClient == null)
            {
                realtimeClient = launcher.GetComponent<RealtimeTransportClient>();
                if (realtimeClient == null)
                {
                    realtimeClient = launcher.gameObject.AddComponent<RealtimeTransportClient>();
                }

                var wsUrl = launcher.Config != null ? launcher.Config.RealtimeWsUrl : "ws://127.0.0.1:5051";
                realtimeClient.Configure(wsUrl);
                Debug.Log($"[PlayerSpawnManager] RealtimeTransportClient auto-created. ws={wsUrl}");
            }

            var sync = localPlayer.GetComponent<MatchPresenceSync>();
            if (sync == null)
            {
                sync = localPlayer.AddComponent<MatchPresenceSync>();
            }

            var remotePrefab = remotePlayerPrefab != null ? remotePlayerPrefab : playerPrefab;
            realtimeClient.Connect(launcher.CurrentTicketId);
            sync.Initialize(launcher, realtimeClient, launcher.CurrentTicketId, remotePrefab);
            var identity = localPlayer.GetComponent<PlayerNetworkIdentity>();
            identity?.Configure(launcher.CurrentTicketId, true);
        }

        private static void ApplyCameraClaritySettings(Camera targetCamera)
        {
            if (targetCamera == null)
            {
                return;
            }

            targetCamera.allowMSAA = true;
            targetCamera.allowHDR = true;
        }

        private bool TryResolveSpawnPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            var candidates = new List<Transform>();
            if (spawnPoints != null)
            {
                for (var i = 0; i < spawnPoints.Count; i++)
                {
                    var p = spawnPoints[i];
                    if (p != null)
                    {
                        candidates.Add(p);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            if (useRandomSpawnPoint)
            {
                Shuffle(candidates);
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var grounded = ResolveGroundedSpawnPosition(candidate.position, out var groundedOk);
                if (!groundedOk)
                {
                    continue;
                }

                position = grounded;
                rotation = candidate.rotation;
                return true;
            }

            // Fallback to first point if none has ground hit.
            position = candidates[0].position;
            rotation = candidates[0].rotation;
            return true;
        }

        private Vector3 ResolveGroundedSpawnPosition(Vector3 requestedPosition)
        {
            return ResolveGroundedSpawnPosition(requestedPosition, out _);
        }

        private Vector3 ResolveGroundedSpawnPosition(Vector3 requestedPosition, out bool grounded)
        {
            var origin = requestedPosition + Vector3.up * spawnProbeHeight;
            if (Physics.Raycast(origin, Vector3.down, out var hit, spawnProbeDistance, groundMask, QueryTriggerInteraction.Ignore))
            {
                var offset = 0.05f;
                grounded = true;
                return hit.point + Vector3.up * offset;
            }

            grounded = false;
            return requestedPosition;
        }

        private static void Shuffle<T>(List<T> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private Transform ResolveSpawnPoint()
        {
            if (spawnPoints == null || spawnPoints.Count == 0)
            {
                return null;
            }

            var validPoints = new List<Transform>();
            for (var i = 0; i < spawnPoints.Count; i++)
            {
                if (spawnPoints[i] != null)
                {
                    validPoints.Add(spawnPoints[i]);
                }
            }

            if (validPoints.Count == 0)
            {
                return null;
            }

            if (!useRandomSpawnPoint)
            {
                return validPoints[0];
            }

            var idx = Random.Range(0, validPoints.Count);
            return validPoints[idx];
        }
    }
}
