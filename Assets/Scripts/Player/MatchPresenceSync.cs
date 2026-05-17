using System;
using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Network;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public sealed class MatchPresenceSync : MonoBehaviour
    {
        [SerializeField] private int syncTickRate = 120;
        [SerializeField] private float interpolationBackTimeSeconds = 0.1f;
        [SerializeField] private int interpolationBackTicks = 2;
        [SerializeField] private float extrapolationLimitSeconds = 0.035f;
        [SerializeField] private float remoteRotationLerpSpeed = 34f;
        [SerializeField] private float remoteStaleSeconds = 8f;

        private RealtimeTransportClient realtimeClient;
        private NetworkLauncher networkLauncher;
        private string localTicketId;
        private GameObject remotePlayerPrefab;
        private Coroutine syncCoroutine;
        private double latestServerTimeSeconds;
        private double latestServerTickRate = 30.0;
        private readonly Dictionary<string, RemoteAvatar> remoteAvatars = new Dictionary<string, RemoteAvatar>();

        private sealed class RemoteAvatar
        {
            public GameObject Root;
            public float LastSeenAt;
            public readonly List<PresenceSnapshot> Snapshots = new List<PresenceSnapshot>();
        }

        private struct PresenceSnapshot
        {
            public double TimeSeconds;
            public Vector3 Position;
            public float Yaw;
        }

        public void Initialize(NetworkLauncher launcher, RealtimeTransportClient transportClient, string ticketId, GameObject remotePrefab)
        {
            networkLauncher = launcher;
            realtimeClient = transportClient;
            localTicketId = ticketId;
            remotePlayerPrefab = remotePrefab;
        }

        private void OnEnable()
        {
            if (syncCoroutine == null)
            {
                syncCoroutine = StartCoroutine(SyncRoutine());
            }
        }

        private void OnDisable()
        {
            if (syncCoroutine != null)
            {
                StopCoroutine(syncCoroutine);
                syncCoroutine = null;
            }

            ClearRemoteAvatars();
        }

        private void Update()
        {
            if (remoteAvatars.Count == 0)
            {
                return;
            }

            var renderTime = latestServerTimeSeconds - interpolationBackTimeSeconds;
            var now = Time.unscaledTime;
            var staleIds = ListPool<string>.Get();
            foreach (var kv in remoteAvatars)
            {
                var avatar = kv.Value;
                if (avatar.Root == null)
                {
                    staleIds.Add(kv.Key);
                    continue;
                }

                var targetPose = EvaluatePose(avatar.Snapshots, renderTime);
                avatar.Root.transform.position = targetPose.Position;
                var targetRotation = Quaternion.Euler(0f, targetPose.Yaw, 0f);
                avatar.Root.transform.rotation = Quaternion.Slerp(
                    avatar.Root.transform.rotation,
                    targetRotation,
                    Time.deltaTime * remoteRotationLerpSpeed);

                if (now - avatar.LastSeenAt > remoteStaleSeconds)
                {
                    staleIds.Add(kv.Key);
                }
            }

            for (var i = 0; i < staleIds.Count; i++)
            {
                RemoveAvatar(staleIds[i]);
            }
            ListPool<string>.Release(staleIds);
        }

        private IEnumerator SyncRoutine()
        {
            while (true)
            {
                if (realtimeClient != null &&
                    networkLauncher != null &&
                    networkLauncher.IsClientConnected &&
                    !string.IsNullOrWhiteSpace(localTicketId))
                {
                    var currentPos = transform.position;
                    var currentYaw = transform.eulerAngles.y;

                    if (!realtimeClient.IsConnected || !string.Equals(realtimeClient.ConnectedTicketId, localTicketId, StringComparison.Ordinal))
                    {
                        realtimeClient.Connect(localTicketId);
                    }

                    realtimeClient.SendPose(currentPos, currentYaw);

                    if (realtimeClient.TryGetLatestSnapshot(out var snapshot) && snapshot != null)
                    {
                        if (snapshot.serverTickRate > 0)
                        {
                            latestServerTickRate = snapshot.serverTickRate;
                        }

                        var backByTicksSeconds = Mathf.Max(0, interpolationBackTicks) / (float)latestServerTickRate;
                        var backSeconds = Mathf.Max(interpolationBackTimeSeconds, backByTicksSeconds);
                        latestServerTimeSeconds = snapshot.serverTick > 0
                            ? snapshot.serverTick / latestServerTickRate
                            : Time.realtimeSinceStartupAsDouble - backSeconds;

                        ApplyRemotePresence(snapshot.players);
                        var remoteCount = snapshot.players != null ? snapshot.players.Length : 0;
                        networkLauncher.SetCurrentMatchPlayerCount(remoteCount + 1);
                    }
                }

                yield return new WaitForSecondsRealtime(1f / Mathf.Clamp(syncTickRate, 10, 120));
            }
        }

        private void ApplyRemotePresence(RealtimeTransportClient.RealtimePlayerState[] players)
        {
            var now = Time.unscaledTime;

            if (players != null)
            {
                for (var i = 0; i < players.Length; i++)
                {
                    var p = players[i];
                    if (p == null || string.IsNullOrWhiteSpace(p.ticketId) || p.position == null)
                    {
                        continue;
                    }

                    if (!remoteAvatars.TryGetValue(p.ticketId, out var avatar))
                    {
                        avatar = CreateAvatar(p.ticketId);
                        remoteAvatars[p.ticketId] = avatar;
                    }

                    avatar.LastSeenAt = now;
                    AddSnapshot(
                        avatar.Snapshots,
                        p.sampleTick > 0
                            ? p.sampleTick / latestServerTickRate
                            : (p.sampleTimeMs > 0 ? p.sampleTimeMs / 1000.0 : latestServerTimeSeconds),
                        new Vector3(p.position.x, p.position.y, p.position.z),
                        p.yaw);
                }
            }

        }

        private RemoteAvatar CreateAvatar(string ticketId)
        {
            GameObject root;
            if (remotePlayerPrefab != null)
            {
                root = Instantiate(remotePlayerPrefab);
            }
            else
            {
                root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(root.GetComponent<Collider>());
            }

            root.name = $"Remote_{ticketId.Substring(0, Mathf.Min(6, ticketId.Length))}";
            root.transform.position = transform.position;

            var presentation = root.GetComponent<PlayerViewPresentation>();
            if (presentation != null)
            {
                presentation.Configure(false);
            }

            var fpsController = root.GetComponent<FpsCharacterController>();
            if (fpsController != null)
            {
                fpsController.enabled = false;
            }

            var selfSync = root.GetComponent<MatchPresenceSync>();
            if (selfSync != null)
            {
                Destroy(selfSync);
            }

            var characterController = root.GetComponent<CharacterController>();
            if (characterController != null)
            {
                characterController.enabled = false;
            }

            var cameras = root.GetComponentsInChildren<Camera>(true);
            for (var i = 0; i < cameras.Length; i++)
            {
                cameras[i].enabled = false;
            }

            var listeners = root.GetComponentsInChildren<AudioListener>(true);
            for (var i = 0; i < listeners.Length; i++)
            {
                listeners[i].enabled = false;
            }

            return new RemoteAvatar
            {
                Root = root,
                LastSeenAt = Time.unscaledTime
            };
        }

        private void RemoveAvatar(string ticketId)
        {
            if (!remoteAvatars.TryGetValue(ticketId, out var avatar))
            {
                return;
            }

            if (avatar.Root != null)
            {
                Destroy(avatar.Root);
            }

            remoteAvatars.Remove(ticketId);
        }

        private void ClearRemoteAvatars()
        {
            foreach (var kv in remoteAvatars)
            {
                if (kv.Value.Root != null)
                {
                    Destroy(kv.Value.Root);
                }
            }

            remoteAvatars.Clear();
        }

        private static void AddSnapshot(List<PresenceSnapshot> snapshots, double timeSeconds, Vector3 position, float yaw)
        {
            if (snapshots.Count > 0 && timeSeconds < snapshots[snapshots.Count - 1].TimeSeconds)
            {
                return;
            }

            snapshots.Add(new PresenceSnapshot
            {
                TimeSeconds = timeSeconds,
                Position = position,
                Yaw = yaw
            });

            if (snapshots.Count > 30)
            {
                snapshots.RemoveAt(0);
            }
        }

        private (Vector3 Position, float Yaw) EvaluatePose(List<PresenceSnapshot> snapshots, double renderTime)
        {
            if (snapshots == null || snapshots.Count == 0)
            {
                return (transform.position, transform.eulerAngles.y);
            }

            while (snapshots.Count >= 2 && snapshots[1].TimeSeconds <= renderTime)
            {
                snapshots.RemoveAt(0);
            }

            if (snapshots.Count >= 2 && snapshots[0].TimeSeconds <= renderTime && renderTime <= snapshots[1].TimeSeconds)
            {
                var from = snapshots[0];
                var to = snapshots[1];
                var range = Math.Max(0.0001, to.TimeSeconds - from.TimeSeconds);
                var t = Mathf.Clamp01((float)((renderTime - from.TimeSeconds) / range));
                var pos = Vector3.Lerp(from.Position, to.Position, t);
                var yaw = Mathf.LerpAngle(from.Yaw, to.Yaw, t);
                return (pos, yaw);
            }

            if (snapshots.Count >= 2)
            {
                var prev = snapshots[snapshots.Count - 2];
                var last = snapshots[snapshots.Count - 1];
                var dt = Math.Max(0.0001, last.TimeSeconds - prev.TimeSeconds);
                var velocity = (last.Position - prev.Position) / (float)dt;
                var extra = Mathf.Clamp((float)(renderTime - last.TimeSeconds), 0f, extrapolationLimitSeconds);
                var pos = last.Position + velocity * extra;
                return (pos, last.Yaw);
            }

            return (snapshots[0].Position, snapshots[0].Yaw);
        }
    }

    internal static class ListPool<T>
    {
        private static readonly Stack<List<T>> Pool = new Stack<List<T>>();
        public static List<T> Get() => Pool.Count > 0 ? Pool.Pop() : new List<T>();
        public static void Release(List<T> list)
        {
            list.Clear();
            Pool.Push(list);
        }
    }

}
