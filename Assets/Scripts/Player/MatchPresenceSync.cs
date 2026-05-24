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
        [SerializeField] private float interpolationBackTimeSeconds = 0.18f;
        [SerializeField] private int interpolationBackTicks = 3;
        [SerializeField] private float extrapolationLimitSeconds = 0.08f;
        [SerializeField] private float remotePositionLerpSpeed = 26f;
        [SerializeField] private float remoteVerticalLerpSpeed = 12f;
        [SerializeField] private float remoteRotationLerpSpeed = 22f;
        [SerializeField] private float remoteHorizontalSmoothTime = 0.05f;
        [SerializeField] private float remoteVerticalSmoothTime = 0.08f;
        [SerializeField] private float teleportSnapDistance = 4f;
        [SerializeField] private float verticalSnapDistance = 1.2f;
        [SerializeField] private float remoteStaleSeconds = 8f;
        [SerializeField] private float snapshotSilenceReconnectSeconds = 2.5f;
        [SerializeField] private bool debugRealtimeLogs = true;

        private RealtimeTransportClient realtimeClient;
        private NetworkLauncher networkLauncher;
        private string localTicketId;
        private GameObject remotePlayerPrefab;
        private Coroutine syncCoroutine;
        private double latestServerTimeSeconds;
        private double latestServerTimeReceiptRealtimeSeconds;
        private double latestServerTickRate = 30.0;
        private float lastSnapshotReceivedAt;
        private float lastConnectRequestAt = -10f;
        private float lastSnapshotDebugAt;
        private PlayerWeaponMount localWeaponMount;
        private PlayerWeaponController localWeaponController;
        private FpsCharacterController localFpsController;
        private ProceduralLocomotionRig localLocomotionRig;
        private readonly Dictionary<string, RemoteAvatar> remoteAvatars = new Dictionary<string, RemoteAvatar>();

        private sealed class RemoteAvatar
        {
            public GameObject Root;
            public float LastSeenAt;
            public Vector2 HorizontalSmoothVelocity;
            public float VerticalSmoothVelocity;
            public int LastAppliedShotSeq = -1;
            public int LastAppliedReloadSeq = -1;
            public int LastAppliedHitPlayerSeq = -1;
            public int LastAppliedFootstepSeq = -1;
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
            lastSnapshotReceivedAt = Time.unscaledTime;
            localWeaponMount = GetComponent<PlayerWeaponMount>();
            localWeaponController = GetComponent<PlayerWeaponController>();
            localFpsController = GetComponent<FpsCharacterController>();
            localLocomotionRig = GetComponent<ProceduralLocomotionRig>();
            if (localLocomotionRig == null)
            {
                localLocomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
            }
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

            var renderTime = GetEstimatedServerTimeSeconds() - interpolationBackTimeSeconds;
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
                var currentPosition = avatar.Root.transform.position;
                var distance = Vector3.Distance(currentPosition, targetPose.Position);
                if (distance >= teleportSnapDistance)
                {
                    avatar.Root.transform.position = targetPose.Position;
                    avatar.HorizontalSmoothVelocity = Vector2.zero;
                    avatar.VerticalSmoothVelocity = 0f;
                }
                else
                {
                    var horizontalCurrent = new Vector2(currentPosition.x, currentPosition.z);
                    var horizontalTarget = new Vector2(targetPose.Position.x, targetPose.Position.z);
                    var horizontalNext = Vector2.SmoothDamp(
                        horizontalCurrent,
                        horizontalTarget,
                        ref avatar.HorizontalSmoothVelocity,
                        Mathf.Max(0.001f, remoteHorizontalSmoothTime),
                        remotePositionLerpSpeed * 2f,
                        Time.deltaTime);

                    var yDelta = Mathf.Abs(targetPose.Position.y - currentPosition.y);
                    var yNext = yDelta >= verticalSnapDistance
                        ? targetPose.Position.y
                        : Mathf.SmoothDamp(
                            currentPosition.y,
                            targetPose.Position.y,
                            ref avatar.VerticalSmoothVelocity,
                            Mathf.Max(0.001f, remoteVerticalSmoothTime),
                            remoteVerticalLerpSpeed * 2f,
                            Time.deltaTime);

                    avatar.Root.transform.position = new Vector3(horizontalNext.x, yNext, horizontalNext.y);
                }
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

                    var wrongTicketConnected = realtimeClient.IsConnected &&
                        !string.Equals(realtimeClient.ConnectedTicketId, localTicketId, StringComparison.Ordinal);
                    var snapshotSilentTooLong = realtimeClient.IsReady &&
                        (Time.unscaledTime - lastSnapshotReceivedAt) > Mathf.Max(0.5f, snapshotSilenceReconnectSeconds);

                    if (wrongTicketConnected || snapshotSilentTooLong)
                    {
                        realtimeClient.Disconnect();
                    }

                    if ((!realtimeClient.IsConnected || !string.Equals(realtimeClient.ConnectedTicketId, localTicketId, StringComparison.Ordinal)) &&
                        (Time.unscaledTime - lastConnectRequestAt) > 0.4f)
                    {
                        lastConnectRequestAt = Time.unscaledTime;
                        realtimeClient.Connect(localTicketId);
                    }

                    if (localLocomotionRig == null)
                    {
                        localLocomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
                    }

                    var isAiming = localWeaponMount != null && localWeaponMount.AdsBlend > 0.5f;
                    var shotSeq = localWeaponController != null ? localWeaponController.LastShotSequence : 0;
                    var reloadSeq = localWeaponController != null ? localWeaponController.LastReloadSequence : 0;
                    var hitPlayerSeq = localWeaponController != null ? localWeaponController.LastHitPlayerSequence : 0;
                    var footstepSeq = localFpsController != null ? localFpsController.LastFootstepSequence : 0;
                    var isCrouching = localFpsController != null && localFpsController.IsCrouching;
                    var wallAvoidBlend = localWeaponMount != null ? localWeaponMount.CurrentWallAvoidBlend : 0f;
                    var lookPitch = localFpsController != null ? localFpsController.CurrentLookPitch : 0f;
                    var animSpeed = localLocomotionRig != null
                        ? localLocomotionRig.CurrentSpeed01
                        : (localFpsController != null ? Mathf.Clamp01(localFpsController.MoveInputMagnitude) : 0f);
                    var animGrounded = localLocomotionRig != null
                        ? localLocomotionRig.CurrentGrounded
                        : (localFpsController == null || localFpsController.IsGrounded);
                    var animJumpState = localLocomotionRig != null
                        ? localLocomotionRig.CurrentJumpState
                        : (animGrounded ? 0 : 2);
                    var animPhase = localLocomotionRig != null
                        ? localLocomotionRig.CurrentAnimPhase01
                        : 0f;

                    realtimeClient.SendPose(
                        currentPos,
                        currentYaw,
                        lookPitch,
                        shotSeq,
                        reloadSeq,
                        hitPlayerSeq,
                        footstepSeq,
                        isCrouching,
                        wallAvoidBlend,
                        isAiming,
                        animSpeed,
                        animGrounded,
                        animJumpState,
                        animPhase);

                    if (realtimeClient.TryGetLatestSnapshot(out var snapshot) && snapshot != null)
                    {
                        ApplyRealtimeSnapshot(snapshot);
                    }
                }

                yield return new WaitForSecondsRealtime(1f / Mathf.Clamp(syncTickRate, 10, 120));
            }
        }

        private void ApplyRealtimeSnapshot(RealtimeTransportClient.RealtimeSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            lastSnapshotReceivedAt = Time.unscaledTime;

            if (snapshot.serverTickRate > 0)
            {
                latestServerTickRate = snapshot.serverTickRate;
            }

            var backByTicksSeconds = Mathf.Max(0, interpolationBackTicks) / (float)latestServerTickRate;
            var backSeconds = Mathf.Max(interpolationBackTimeSeconds, backByTicksSeconds);
            latestServerTimeSeconds = snapshot.serverTick > 0
                ? snapshot.serverTick / latestServerTickRate
                : Time.realtimeSinceStartupAsDouble - backSeconds;
            latestServerTimeReceiptRealtimeSeconds = Time.realtimeSinceStartupAsDouble;

            ApplyRemotePresence(snapshot.players);
            var remoteCount = snapshot.players != null ? snapshot.players.Length : 0;
            networkLauncher?.SetCurrentMatchPlayerCount(remoteCount + 1);
            if (debugRealtimeLogs && Time.unscaledTime - lastSnapshotDebugAt >= 1f)
            {
                lastSnapshotDebugAt = Time.unscaledTime;
                Debug.Log($"[MatchPresenceSync] snapshot players={remoteCount} avatars={remoteAvatars.Count} tick={snapshot.serverTick}");
            }
        }

        private double GetEstimatedServerTimeSeconds()
        {
            if (latestServerTimeReceiptRealtimeSeconds <= 0.0)
            {
                return latestServerTimeSeconds > 0.0
                    ? latestServerTimeSeconds
                    : Time.realtimeSinceStartupAsDouble;
            }

            var elapsedSinceSnapshot = Time.realtimeSinceStartupAsDouble - latestServerTimeReceiptRealtimeSeconds;
            return latestServerTimeSeconds + Math.Max(0.0, elapsedSinceSnapshot);
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
                        var initialPosition = new Vector3(p.position.x, p.position.y, p.position.z);
                        avatar = CreateAvatar(p.ticketId, initialPosition, p.yaw);
                        avatar.LastAppliedShotSeq = Mathf.Max(0, p.shotSeq);
                        avatar.LastAppliedReloadSeq = Mathf.Max(0, p.reloadSeq);
                        avatar.LastAppliedHitPlayerSeq = Mathf.Max(0, p.hitPlayerSeq);
                        avatar.LastAppliedFootstepSeq = Mathf.Max(0, p.footstepSeq);
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

                    var weaponMount = avatar.Root.GetComponent<PlayerWeaponMount>();
                    if (weaponMount != null)
                    {
                        weaponMount.SetNetworkLookPitch(p.lookPitch);
                        weaponMount.SetNetworkAimState(p.isAiming);
                        weaponMount.SetNetworkCrouchState(p.isCrouching);
                        weaponMount.SetNetworkWallAvoidBlend(p.wallAvoidBlend);
                    }

                    TryPlayRemoteShots(avatar, p);
                    TryPlayRemoteReload(avatar, p);
                    TryPlayRemoteHitPlayer(avatar, p);
                    TryPlayRemoteFootsteps(avatar, p);

                    var locomotionRig = avatar.Root.GetComponentInChildren<ProceduralLocomotionRig>(true);
                    if (locomotionRig != null)
                    {
                        locomotionRig.SetNetworkAnimationState(
                            p.animSpeed,
                            p.isGrounded,
                            p.jumpState,
                            p.animPhase,
                            p.isCrouching);
                    }
                }
            }

        }

        private RemoteAvatar CreateAvatar(string ticketId, Vector3 initialPosition, float initialYaw)
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
            root.transform.position = initialPosition;
            root.transform.rotation = Quaternion.Euler(0f, initialYaw, 0f);
            if (debugRealtimeLogs)
            {
                Debug.Log($"[MatchPresenceSync] create avatar ticket={ticketId}");
            }

            var presentation = root.GetComponent<PlayerViewPresentation>();
            if (presentation != null)
            {
                presentation.Configure(false);
            }

            EnsureRemoteVisuals(root);

            var fpsController = root.GetComponent<FpsCharacterController>();
            if (fpsController != null)
            {
                fpsController.enabled = false;
            }

            var weaponController = root.GetComponent<PlayerWeaponController>();
            if (weaponController == null)
            {
                weaponController = root.AddComponent<PlayerWeaponController>();
            }
            weaponController.enabled = false;
            if (root.GetComponent<PlayerAudioController>() == null)
            {
                root.AddComponent<PlayerAudioController>();
            }

            var weaponMount = root.GetComponent<PlayerWeaponMount>();
            if (weaponMount != null)
            {
                weaponMount.SetNetworkMode(true);
                weaponMount.SetNetworkLookPitch(0f);
                weaponMount.SetNetworkAimState(false);
                weaponMount.SetNetworkCrouchState(false);
                weaponMount.SetNetworkWallAvoidBlend(0f);
            }

            var locomotionRig = root.GetComponentInChildren<ProceduralLocomotionRig>(true);
            if (locomotionRig != null)
            {
                locomotionRig.SetNetworkMode(true);
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

        private void TryPlayRemoteShots(RemoteAvatar avatar, RealtimeTransportClient.RealtimePlayerState playerState)
        {
            if (avatar == null || avatar.Root == null || playerState == null)
            {
                return;
            }

            if (playerState.shotSeq <= avatar.LastAppliedShotSeq)
            {
                return;
            }

            var weaponController = avatar.Root.GetComponent<PlayerWeaponController>();
            if (weaponController == null)
            {
                avatar.LastAppliedShotSeq = playerState.shotSeq;
                return;
            }

            var shotsToReplay = Mathf.Clamp(playerState.shotSeq - avatar.LastAppliedShotSeq, 1, 4);
            for (var i = 0; i < shotsToReplay; i++)
            {
                weaponController.PlayRemoteShot(playerState.lookPitch);
            }

            avatar.LastAppliedShotSeq = playerState.shotSeq;
        }

        private void TryPlayRemoteReload(RemoteAvatar avatar, RealtimeTransportClient.RealtimePlayerState playerState)
        {
            if (avatar == null || avatar.Root == null || playerState == null)
            {
                return;
            }

            if (playerState.reloadSeq <= avatar.LastAppliedReloadSeq)
            {
                return;
            }

            var weaponController = avatar.Root.GetComponent<PlayerWeaponController>();
            if (weaponController != null)
            {
                weaponController.PlayRemoteReload(weaponController.ReloadDurationSeconds);
            }

            avatar.LastAppliedReloadSeq = playerState.reloadSeq;
        }

        private void TryPlayRemoteHitPlayer(RemoteAvatar avatar, RealtimeTransportClient.RealtimePlayerState playerState)
        {
            if (avatar == null || avatar.Root == null || playerState == null)
            {
                return;
            }

            if (playerState.hitPlayerSeq <= avatar.LastAppliedHitPlayerSeq)
            {
                return;
            }

            var weaponController = avatar.Root.GetComponent<PlayerWeaponController>();
            weaponController?.PlayRemoteHitPlayer();
            avatar.LastAppliedHitPlayerSeq = playerState.hitPlayerSeq;
        }

        private void TryPlayRemoteFootsteps(RemoteAvatar avatar, RealtimeTransportClient.RealtimePlayerState playerState)
        {
            if (avatar == null || avatar.Root == null || playerState == null)
            {
                return;
            }

            if (playerState.footstepSeq <= avatar.LastAppliedFootstepSeq)
            {
                return;
            }

            var fps = avatar.Root.GetComponent<FpsCharacterController>();
            var count = Mathf.Clamp(playerState.footstepSeq - avatar.LastAppliedFootstepSeq, 1, 3);
            for (var i = 0; i < count; i++)
            {
                fps?.PlayRemoteFootstep();
            }

            avatar.LastAppliedFootstepSeq = playerState.footstepSeq;
        }

        private static void EnsureRemoteVisuals(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var tr = allTransforms[i];
                if (tr == null || tr == root.transform)
                {
                    continue;
                }

                if (string.Equals(tr.name, "FirstPersonHands", StringComparison.Ordinal))
                {
                    tr.gameObject.SetActive(false);
                }
                else if (string.Equals(tr.name, "CameraPivot", StringComparison.Ordinal) ||
                         string.Equals(tr.name, "PlayerCamera", StringComparison.Ordinal))
                {
                    tr.gameObject.SetActive(false);
                }
                else if (string.Equals(tr.name, "ThirdPersonBody", StringComparison.Ordinal))
                {
                    tr.gameObject.SetActive(true);
                }
            }

            var thirdPersonRoot = root.transform.Find("ThirdPersonBody");
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var enabledCount = 0;
            for (var i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null)
                {
                    continue;
                }

                if (thirdPersonRoot != null)
                {
                    r.enabled = r.transform.IsChildOf(thirdPersonRoot);
                }
                else
                {
                    r.enabled = true;
                }
                if (r.enabled && r.gameObject.activeInHierarchy)
                {
                    enabledCount++;
                }
            }

            if (enabledCount > 0)
            {
                return;
            }

            // Safety net: ensure remote player is always visible.
            var fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            fallback.name = "RemoteVisualFallback";
            fallback.transform.SetParent(root.transform, false);
            fallback.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            fallback.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            var collider = fallback.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
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

            if (snapshots.Count > 0)
            {
                var lastIndex = snapshots.Count - 1;
                var last = snapshots[lastIndex];
                var timeDelta = timeSeconds - last.TimeSeconds;

                // Replace same-time sample instead of adding duplicates (common on uneven network delivery).
                if (Math.Abs(timeDelta) <= 0.0001)
                {
                    snapshots[lastIndex] = new PresenceSnapshot
                    {
                        TimeSeconds = timeSeconds,
                        Position = position,
                        Yaw = yaw
                    };
                    return;
                }

                // Ignore almost-identical samples that only add jitter noise.
                if (timeDelta <= 0.05 &&
                    (position - last.Position).sqrMagnitude <= 0.000004f &&
                    Mathf.Abs(Mathf.DeltaAngle(last.Yaw, yaw)) <= 0.08f)
                {
                    return;
                }
            }

            snapshots.Add(new PresenceSnapshot
            {
                TimeSeconds = timeSeconds,
                Position = position,
                Yaw = yaw
            });

            if (snapshots.Count > 64)
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
                var horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
                horizontalVelocity = Vector3.ClampMagnitude(horizontalVelocity, 12f);
                var horizontalOffset = horizontalVelocity * extra;
                var verticalOffset = Mathf.Clamp(velocity.y * extra, -0.18f, 0.18f);
                var pos = new Vector3(
                    last.Position.x + horizontalOffset.x,
                    last.Position.y + verticalOffset,
                    last.Position.z + horizontalOffset.z);
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
