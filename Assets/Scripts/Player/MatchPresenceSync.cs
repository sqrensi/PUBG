using System;
using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Network;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public sealed class MatchPresenceSync : MonoBehaviour
    {
        [SerializeField] private int syncTickRate = 128;
        [Header("Entity interpolation (CS-style)")]
        [SerializeField] private float interpolationBackTimeSeconds = 0.024f;
        [SerializeField] private float interpolationBackTimeMin = 0.016f;
        [SerializeField] private float interpolationBackTimeMax = 0.055f;
        [SerializeField] private int interpolationBackTicks = 1;
        [SerializeField] private bool useAdaptiveInterpolation = true;
        [SerializeField] private bool useSmoothedServerClock = true;
        [SerializeField] private float serverClockSmoothRate = 8f;
        [SerializeField] private bool useFixedLowLatencyInterpolation = false;
        [SerializeField] private float adaptivePingOneWayScale = 0.45f;
        [SerializeField] private float adaptiveJitterMarginSeconds = 0.008f;
        [SerializeField] private int lowLatencyPingThresholdMs = 25;
        [SerializeField] private float extrapolationLimitSeconds = 0.1f;
        [SerializeField] private bool useEntityInterpolation = true;
        [SerializeField] private bool useExtraPositionSmoothing = true;
        [SerializeField] private float remotePositionLerpSpeed = 34f;
        [SerializeField] private float remoteVerticalLerpSpeed = 18f;
        [SerializeField] private float remoteRotationLerpSpeed = 34f;
        [SerializeField] private float remoteHorizontalSmoothTime = 0.012f;
        [SerializeField] private float remoteVerticalSmoothTime = 0.04f;
        [SerializeField] private float remoteDirectFollowDistance = 0.08f;
        [SerializeField] private float remoteMissingGraceSeconds = 1.25f;
        [SerializeField] private float teleportSnapDistance = 4f;
        [SerializeField] private float verticalSnapDistance = 1.2f;
        [SerializeField] private float remoteStaleSeconds = 8f;
        [SerializeField] private float snapshotSilenceReconnectSeconds = 6f;
        [SerializeField] private bool debugRealtimeLogs = false;

        private RealtimeTransportClient realtimeClient;
        private NetworkLauncher networkLauncher;
        private string localTicketId;
        private GameObject remotePlayerPrefab;
        private Coroutine syncCoroutine;
        private double latestServerTimeSeconds;
        private double latestServerTimeReceiptRealtimeSeconds;
        private double latestServerTickRate = 128.0;
        private double smoothedServerTimeSeconds;
        private double smoothedClockLastRealtimeSeconds;
        private bool hasSmoothedServerClock;
        private float lastSnapshotReceivedAt;
        private int lastAppliedServerTick = -1;
        private float lastConnectRequestAt = -10f;
        private float lastSnapshotDebugAt;
        private PlayerWeaponMount localWeaponMount;
        private PlayerWeaponController localWeaponController;
        private FpsCharacterController localFpsController;
        private ProceduralLocomotionRig localLocomotionRig;
        private PlayerHealth localHealth;
        private readonly Dictionary<string, RemoteAvatar> remoteAvatars = new Dictionary<string, RemoteAvatar>();

        private sealed class RemoteAvatar
        {
            public GameObject Root;
            public ProceduralLocomotionRig LocomotionRig;
            public PlayerHealth Health;
            public float LastSeenAt;
            public Vector3 LastKnownPosition;
            public float LastKnownYaw;
            public bool HasKnownPose;
            public bool NetworkGrounded = true;
            public int NetworkJumpState;
            public bool NetworkCrouching;
            public bool NetworkSprinting;
            public float NetworkAnimSpeed;
            public float NetworkAnimPhase;
            public float NetworkLookPitch;
            public float NetworkMoveInputX;
            public float NetworkMoveInputZ;
            public Vector2 HorizontalSmoothVelocity;
            public float VerticalSmoothVelocity;
            public int LastAppliedShotSeq = -1;
            public int LastAppliedReloadSeq = -1;
            public int LastAppliedHitPlayerSeq = -1;
            public int LastAppliedFootstepSeq = -1;
            public bool WasDead;
            public int LastAppliedStateTick = -1;
            public readonly List<PresenceSnapshot> Snapshots = new List<PresenceSnapshot>();
        }

        private struct PresenceSnapshot
        {
            public double TimeSeconds;
            public Vector3 Position;
            public float Yaw;
            public Vector3 Velocity;
            public float YawVelocity;
        }

        private struct InterpolatedPose
        {
            public Vector3 Position;
            public float Yaw;
            public Vector3 Velocity;
            public float YawVelocity;
        }

        public void SetSyncTickRate(int tickRate)
        {
            syncTickRate = Mathf.Clamp(tickRate, 10, 128);
        }

        public void Initialize(NetworkLauncher launcher, RealtimeTransportClient transportClient, string ticketId, GameObject remotePrefab)
        {
            networkLauncher = launcher;
            realtimeClient = transportClient;
            localTicketId = ticketId;
            remotePlayerPrefab = remotePrefab;
            lastSnapshotReceivedAt = Time.unscaledTime;
            lastAppliedServerTick = -1;
            hasSmoothedServerClock = false;
            smoothedServerTimeSeconds = 0.0;
            smoothedClockLastRealtimeSeconds = 0.0;
            localWeaponMount = GetComponent<PlayerWeaponMount>();
            localWeaponController = GetComponent<PlayerWeaponController>();
            localFpsController = GetComponent<FpsCharacterController>();
            localLocomotionRig = GetComponent<ProceduralLocomotionRig>();
            localHealth = GetComponent<PlayerHealth>();
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
            PollLatestSnapshot();

            if (remoteAvatars.Count == 0)
            {
                return;
            }

            var renderTime = GetRenderServerTimeSeconds() - GetInterpolationBackSeconds();
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

                var avatarHealth = avatar.Health;
                if (avatarHealth != null && avatarHealth.IsDead)
                {
                    if (now - avatar.LastSeenAt > remoteStaleSeconds)
                    {
                        staleIds.Add(kv.Key);
                    }

                    continue;
                }

                if (now - avatar.LastSeenAt > remoteMissingGraceSeconds &&
                    avatar.Snapshots.Count == 0 &&
                    !avatar.HasKnownPose)
                {
                    staleIds.Add(kv.Key);
                    continue;
                }

                var targetPose = EvaluatePose(avatar.Snapshots, renderTime, avatar);
                ApplyRemoteTransform(avatar, targetPose.Position, targetPose.Yaw);
                DriveRemoteLocomotion(avatar, targetPose);

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

        private void ApplyRemoteTransform(RemoteAvatar avatar, Vector3 targetPosition, float targetYaw)
        {
            if (avatar?.Root == null)
            {
                return;
            }

            var rootTransform = avatar.Root.transform;
            var currentPosition = rootTransform.position;
            var distance = Vector3.Distance(currentPosition, targetPosition);
            if (distance >= teleportSnapDistance)
            {
                rootTransform.SetPositionAndRotation(
                    targetPosition,
                    Quaternion.Euler(0f, targetYaw, 0f));
                avatar.HorizontalSmoothVelocity = Vector2.zero;
                avatar.VerticalSmoothVelocity = 0f;
                return;
            }

            if (useEntityInterpolation && !useExtraPositionSmoothing)
            {
                rootTransform.SetPositionAndRotation(
                    targetPosition,
                    Quaternion.Euler(0f, targetYaw, 0f));
                avatar.HorizontalSmoothVelocity = Vector2.zero;
                avatar.VerticalSmoothVelocity = 0f;
                return;
            }

            var horizontalCurrent = new Vector2(currentPosition.x, currentPosition.z);
            var horizontalTarget = new Vector2(targetPosition.x, targetPosition.z);
            var horizontalError = Vector2.Distance(horizontalCurrent, horizontalTarget);
            float horizontalNextX;
            float horizontalNextZ;
            if (horizontalError <= remoteDirectFollowDistance)
            {
                horizontalNextX = horizontalTarget.x;
                horizontalNextZ = horizontalTarget.y;
                avatar.HorizontalSmoothVelocity = Vector2.zero;
            }
            else
            {
                var horizontalNext = Vector2.SmoothDamp(
                    horizontalCurrent,
                    horizontalTarget,
                    ref avatar.HorizontalSmoothVelocity,
                    Mathf.Max(0.001f, remoteHorizontalSmoothTime),
                    remotePositionLerpSpeed * 2f,
                    Time.deltaTime);
                horizontalNextX = horizontalNext.x;
                horizontalNextZ = horizontalNext.y;
            }

            var yDelta = Mathf.Abs(targetPosition.y - currentPosition.y);
            var yNext = yDelta >= verticalSnapDistance
                ? targetPosition.y
                : yDelta <= remoteDirectFollowDistance
                    ? targetPosition.y
                    : Mathf.SmoothDamp(
                        currentPosition.y,
                        targetPosition.y,
                        ref avatar.VerticalSmoothVelocity,
                        Mathf.Max(0.001f, remoteVerticalSmoothTime),
                        remoteVerticalLerpSpeed * 2f,
                        Time.deltaTime);

            rootTransform.position = new Vector3(horizontalNextX, yNext, horizontalNextZ);
            var targetRotation = Quaternion.Euler(0f, targetYaw, 0f);
            rootTransform.rotation = Quaternion.Slerp(
                rootTransform.rotation,
                targetRotation,
                Time.deltaTime * remoteRotationLerpSpeed);
        }

        private void DriveRemoteLocomotion(RemoteAvatar avatar, InterpolatedPose pose)
        {
            if (avatar?.LocomotionRig == null)
            {
                return;
            }

            ResolveRemoteMoveInput(avatar, pose, out var moveInputX, out var moveInputZ);
            avatar.LocomotionRig.SetNetworkMoveInput(moveInputX, moveInputZ);
            avatar.LocomotionRig.SetNetworkAnimationState(
                avatar.NetworkAnimSpeed,
                avatar.NetworkGrounded,
                avatar.NetworkJumpState,
                avatar.NetworkAnimPhase,
                avatar.NetworkCrouching,
                avatar.NetworkSprinting);
            avatar.LocomotionRig.SetNetworkLookPitch(avatar.NetworkLookPitch);
        }

        private static void ResolveRemoteMoveInput(
            RemoteAvatar avatar,
            InterpolatedPose pose,
            out float moveInputX,
            out float moveInputZ)
        {
            moveInputX = avatar.NetworkMoveInputX;
            moveInputZ = avatar.NetworkMoveInputZ;
            if (Mathf.Abs(moveInputX) > 0.01f || Mathf.Abs(moveInputZ) > 0.01f)
            {
                return;
            }

            if (avatar.NetworkAnimSpeed <= 0.05f)
            {
                moveInputX = 0f;
                moveInputZ = 0f;
                return;
            }

            var flatVelocity = new Vector3(pose.Velocity.x, 0f, pose.Velocity.z);
            if (flatVelocity.sqrMagnitude < 0.04f)
            {
                return;
            }

            var localDirection = Quaternion.Euler(0f, -pose.Yaw, 0f) * flatVelocity;
            var magnitude = new Vector2(localDirection.x, localDirection.z).magnitude;
            if (magnitude <= 0.01f)
            {
                return;
            }

            moveInputX = localDirection.x / magnitude;
            moveInputZ = localDirection.z / magnitude;
        }

        public void FlushLocalPose()
        {
            SendLocalPose();
        }

        private void SendLocalPose()
        {
            if (realtimeClient == null ||
                networkLauncher == null ||
                !networkLauncher.IsClientConnected ||
                string.IsNullOrWhiteSpace(localTicketId) ||
                !realtimeClient.IsConnected)
            {
                return;
            }

            if (localLocomotionRig == null)
            {
                localLocomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
            }

            var currentPos = transform.position;
            var currentYaw = transform.eulerAngles.y;
            var shotSeq = localWeaponController != null ? localWeaponController.LastShotSequence : 0;
            var reloadSeq = localWeaponController != null ? localWeaponController.LastReloadSequence : 0;
            var hitPlayerSeq = localWeaponController != null ? localWeaponController.LastHitPlayerSequence : 0;
            var footstepSeq = localFpsController != null ? localFpsController.LastFootstepSequence : 0;
            var isCrouching = localFpsController != null && localFpsController.IsCrouching;
            var isSprinting = localFpsController != null && localFpsController.IsSprinting;
            var wallAvoidBlend = localWeaponMount != null ? localWeaponMount.CurrentWallAvoidBlend : 0f;
            var isDead = localHealth != null && localHealth.IsDead;
            var deathSeq = localHealth != null ? localHealth.DeathSequence : 0;
            var deathFallDirection = localHealth != null ? localHealth.DeathFallDirection : Vector3.forward;
            var lookPitch = localFpsController != null ? localFpsController.CurrentLookPitch : 0f;
            var shotOrigin = localWeaponController != null ? localWeaponController.LastShotOrigin : Vector3.zero;
            var shotDirection = localWeaponController != null ? localWeaponController.LastShotDirection : Vector3.zero;
            var animSpeed = localLocomotionRig != null
                ? localLocomotionRig.GetNetworkAnimSpeed01()
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
            var inputAuth = !isDead && localFpsController != null;
            var moveInputX = inputAuth ? localFpsController.NetworkMoveInputX : 0f;
            var moveInputZ = inputAuth ? localFpsController.NetworkMoveInputZ : 0f;
            var jumpPressed = inputAuth && localFpsController.NetworkJumpPressed;

            realtimeClient.SendPose(
                currentPos,
                currentYaw,
                lookPitch,
                shotSeq,
                reloadSeq,
                hitPlayerSeq,
                footstepSeq,
                isCrouching,
                isSprinting,
                wallAvoidBlend,
                isDead,
                deathSeq,
                deathFallDirection,
                false,
                animSpeed,
                animGrounded,
                animJumpState,
                animPhase,
                moveInputX,
                moveInputZ,
                jumpPressed,
                inputAuth,
                shotOrigin,
                shotDirection);
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
                    var wrongTicketConnected = realtimeClient.IsConnected &&
                        !string.Equals(realtimeClient.ConnectedTicketId, localTicketId, StringComparison.Ordinal);
                    var snapshotSilentTooLong = realtimeClient.IsReady &&
                        (Time.unscaledTime - Mathf.Max(
                            lastSnapshotReceivedAt,
                            realtimeClient.LastSnapshotReceivedUnscaledTime)) >
                        Mathf.Max(1f, snapshotSilenceReconnectSeconds);

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

                    SendLocalPose();

                }

                yield return new WaitForSecondsRealtime(1f / Mathf.Clamp(syncTickRate, 10, 128));
            }
        }

        private void PollLatestSnapshot()
        {
            if (realtimeClient == null)
            {
                return;
            }

            if (!realtimeClient.TryGetLatestSnapshot(out var snapshot) || snapshot == null)
            {
                return;
            }

            var serverTick = snapshot.serverTick;
            if (serverTick > 0 && serverTick == lastAppliedServerTick)
            {
                return;
            }

            if (serverTick > 0)
            {
                lastAppliedServerTick = serverTick;
            }

            ApplyRealtimeSnapshot(snapshot);
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
            else if (realtimeClient != null && realtimeClient.LatestServerTickRate > 0)
            {
                latestServerTickRate = realtimeClient.LatestServerTickRate;
            }

            latestServerTimeSeconds = snapshot.serverTick > 0
                ? snapshot.serverTick / latestServerTickRate
                : Time.realtimeSinceStartupAsDouble;
            latestServerTimeReceiptRealtimeSeconds = Time.realtimeSinceStartupAsDouble;

            ApplyRemotePresence(snapshot.players);
            ApplySelfAuthoritativePose(snapshot.selfAuthoritative);
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

        private void AdvanceSmoothedServerClock()
        {
            if (!useSmoothedServerClock)
            {
                return;
            }

            var nowRealtime = Time.realtimeSinceStartupAsDouble;
            var target = GetEstimatedServerTimeSeconds();
            if (!hasSmoothedServerClock)
            {
                smoothedServerTimeSeconds = target;
                smoothedClockLastRealtimeSeconds = nowRealtime;
                hasSmoothedServerClock = true;
                return;
            }

            var dt = Math.Max(0.0, nowRealtime - smoothedClockLastRealtimeSeconds);
            smoothedClockLastRealtimeSeconds = nowRealtime;
            smoothedServerTimeSeconds += dt;

            var error = target - smoothedServerTimeSeconds;
            var smoothRate = Math.Max(1.0, serverClockSmoothRate);
            var correction = error * (1.0 - Math.Exp(-smoothRate * dt));
            smoothedServerTimeSeconds += correction;
        }

        private double GetRenderServerTimeSeconds()
        {
            AdvanceSmoothedServerClock();
            if (useSmoothedServerClock && hasSmoothedServerClock)
            {
                return smoothedServerTimeSeconds;
            }

            return GetEstimatedServerTimeSeconds();
        }

        private float GetInterpolationBackSeconds()
        {
            var tickRate = latestServerTickRate;
            if (realtimeClient != null && realtimeClient.LatestServerTickRate > 0)
            {
                tickRate = realtimeClient.LatestServerTickRate;
            }

            var backByTicks = interpolationBackTicks / (float)Math.Max(1.0, tickRate);
            if (useFixedLowLatencyInterpolation)
            {
                return Mathf.Max(interpolationBackTimeMin, backByTicks);
            }

            var backSeconds = interpolationBackTimeSeconds;
            if (useAdaptiveInterpolation)
            {
                var pingMs = realtimeClient != null && realtimeClient.SmoothedRoundTripMs > 0
                    ? realtimeClient.SmoothedRoundTripMs
                    : (networkLauncher != null ? networkLauncher.LastMeasuredPingMs : -1);
                if (pingMs > 0)
                {
                    if (pingMs <= lowLatencyPingThresholdMs)
                    {
                        backSeconds = interpolationBackTimeMin;
                    }
                    else
                    {
                        var oneWaySeconds = pingMs * 0.001f * Mathf.Clamp(adaptivePingOneWayScale, 0.35f, 0.65f);
                        backSeconds = oneWaySeconds + Mathf.Max(0f, adaptiveJitterMarginSeconds);
                    }
                }
            }

            backSeconds = Mathf.Clamp(backSeconds, interpolationBackTimeMin, interpolationBackTimeMax);
            return Mathf.Max(backSeconds, backByTicks);
        }

        private void ApplySelfAuthoritativePose(RealtimeTransportClient.SelfAuthoritativePose selfPose)
        {
            if (selfPose == null || selfPose.position == null || localFpsController == null)
            {
                return;
            }

            if (localHealth != null && localHealth.IsDead)
            {
                return;
            }

            localFpsController.ReconcileToServer(
                new Vector3(selfPose.position.x, selfPose.position.y, selfPose.position.z),
                selfPose.yaw,
                selfPose.sampleTick);
        }

        private void IngestPlayerStateSamples(RemoteAvatar avatar, RealtimeTransportClient.RealtimePlayerState player)
        {
            if (avatar == null || player == null)
            {
                return;
            }

            var tickRate = latestServerTickRate;
            if (realtimeClient != null && realtimeClient.LatestServerTickRate > 0)
            {
                tickRate = realtimeClient.LatestServerTickRate;
            }

            tickRate = Math.Max(1.0, tickRate);

            if (player.history != null)
            {
                for (var i = 0; i < player.history.Length; i++)
                {
                    var sample = player.history[i];
                    if (sample == null || sample.sampleTick <= avatar.LastAppliedStateTick)
                    {
                        continue;
                    }

                    var timeSeconds = sample.sampleTick / tickRate;
                    var position = new Vector3(sample.x, sample.y, sample.z);
                    var velocity = new Vector3(sample.velX, sample.velY, sample.velZ);
                    AddSnapshot(avatar.Snapshots, timeSeconds, position, sample.yaw, velocity, true);
                    avatar.LastAppliedStateTick = sample.sampleTick;
                }
            }

            if (player.sampleTick > avatar.LastAppliedStateTick && player.position != null)
            {
                var timeSeconds = player.sampleTick / tickRate;
                var position = new Vector3(player.position.x, player.position.y, player.position.z);
                var velocity = new Vector3(player.velX, player.velY, player.velZ);
                AddSnapshot(avatar.Snapshots, timeSeconds, position, player.yaw, velocity, true);
                avatar.LastAppliedStateTick = player.sampleTick;
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
                        var initialPosition = new Vector3(p.position.x, p.position.y, p.position.z);
                        avatar = CreateAvatar(p.ticketId, initialPosition, p.yaw);
                        avatar.LastAppliedShotSeq = Mathf.Max(0, p.shotSeq);
                        avatar.LastAppliedReloadSeq = Mathf.Max(0, p.reloadSeq);
                        avatar.LastAppliedHitPlayerSeq = Mathf.Max(0, p.hitPlayerSeq);
                        avatar.LastAppliedFootstepSeq = Mathf.Max(0, p.footstepSeq);
                        avatar.WasDead = p.isDead;
                        remoteAvatars[p.ticketId] = avatar;
                    }

                    if (avatar.WasDead && !p.isDead)
                    {
                        RemoveAvatar(p.ticketId);
                        continue;
                    }

                    avatar.LastSeenAt = now;
                    var samplePosition = new Vector3(p.position.x, p.position.y, p.position.z);
                    avatar.LastKnownPosition = samplePosition;
                    avatar.LastKnownYaw = p.yaw;
                    avatar.HasKnownPose = true;
                    avatar.NetworkGrounded = p.isGrounded;
                    avatar.NetworkJumpState = p.jumpState;
                    avatar.NetworkCrouching = p.isCrouching;
                    avatar.NetworkSprinting = p.isSprinting;
                    avatar.NetworkAnimSpeed = p.animSpeed;
                    avatar.NetworkAnimPhase = p.animPhase;
                    avatar.NetworkLookPitch = p.lookPitch;
                    avatar.NetworkMoveInputX = p.moveInputX;
                    avatar.NetworkMoveInputZ = p.moveInputZ;
                    IngestPlayerStateSamples(avatar, p);

                    var weaponMount = avatar.Root.GetComponent<PlayerWeaponMount>();
                    if (weaponMount != null)
                    {
                        weaponMount.SetNetworkLookPitch(p.lookPitch);
                        weaponMount.SetNetworkAimState(false);
                        weaponMount.SetNetworkCrouchState(p.isCrouching);
                        weaponMount.SetNetworkSprintState(p.isSprinting);
                        weaponMount.SetNetworkWallAvoidBlend(p.wallAvoidBlend);
                    }

                    TryPlayRemoteShots(avatar, p);
                    TryPlayRemoteReload(avatar, p);
                    TryPlayRemoteHitPlayer(avatar, p);
                    TryPlayRemoteFootsteps(avatar, p);

                    if (avatar.LocomotionRig != null)
                    {
                        ResolveRemoteMoveInput(
                            avatar,
                            new InterpolatedPose
                            {
                                Yaw = p.yaw,
                                Velocity = new Vector3(p.velX, p.velY, p.velZ)
                            },
                            out var moveInputX,
                            out var moveInputZ);
                        avatar.LocomotionRig.SetNetworkMoveInput(moveInputX, moveInputZ);
                        avatar.LocomotionRig.SetNetworkAnimationState(
                            p.animSpeed,
                            avatar.NetworkGrounded,
                            avatar.NetworkJumpState,
                            p.animPhase,
                            avatar.NetworkCrouching,
                            avatar.NetworkSprinting);
                        avatar.LocomotionRig.SetNetworkLookPitch(p.lookPitch);
                    }

                    avatar.Health?.SetNetworkDeadState(
                        p.isDead,
                        p.deathSeq,
                        new Vector3(p.deathFallDirX, p.deathFallDirY, p.deathFallDirZ));
                    avatar.WasDead = p.isDead;
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

            var splitBody = root.GetComponent<SyntySplitBodyPresentation>();
            splitBody?.ApplyViewMode();

            var identity = root.GetComponent<PlayerNetworkIdentity>();
            if (identity == null)
            {
                identity = root.AddComponent<PlayerNetworkIdentity>();
            }
            identity.Configure(ticketId, false);

            EnsureRemoteVisuals(root);

            var remoteBootstrap = root.GetComponent<RemoteThirdPersonPlayerBootstrap>();
            remoteBootstrap?.ApplyRemoteThirdPersonMode();

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
            var health = root.GetComponent<PlayerHealth>();
            if (health == null)
            {
                health = root.AddComponent<PlayerHealth>();
            }
            health.SetNetworkMode(true);
            health.SetNetworkDeadState(false, 0, Vector3.forward);
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
                weaponMount.SetNetworkSprintState(false);
            }

            var locomotionRig = root.GetComponentInChildren<ProceduralLocomotionRig>(true);
            if (locomotionRig != null)
            {
                locomotionRig.SetNetworkMode(true);
            }

            var syntyDriver = root.GetComponentInChildren<SyntyLocomotionDriver>(true);
            if (syntyDriver != null)
            {
                syntyDriver.SetNetworkMode(true);
            }

            var selfSync = root.GetComponent<MatchPresenceSync>();
            if (selfSync != null)
            {
                Destroy(selfSync);
            }

            // Keep CharacterController enabled on remote avatars so they are hittable by raycasts.

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
                LocomotionRig = locomotionRig,
                Health = health,
                LastSeenAt = Time.unscaledTime,
                LastKnownPosition = initialPosition,
                LastKnownYaw = initialYaw,
                HasKnownPose = true
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
            var shotOrigin = new Vector3(playerState.shotOriginX, playerState.shotOriginY, playerState.shotOriginZ);
            var shotDirection = new Vector3(playerState.shotDirX, playerState.shotDirY, playerState.shotDirZ);
            for (var i = 0; i < shotsToReplay; i++)
            {
                weaponController.PlayRemoteShot(shotOrigin, shotDirection, playerState.lookPitch);
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
            var maxReplay = playerState.isSprinting ? 6 : 3;
            var count = Mathf.Clamp(playerState.footstepSeq - avatar.LastAppliedFootstepSeq, 1, maxReplay);
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

                if (string.Equals(tr.name, "FirstPersonHands", StringComparison.Ordinal) ||
                    string.Equals(tr.name, "FirstPersonView", StringComparison.Ordinal))
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
            lastAppliedServerTick = -1;
        }

        private double ResolveSnapshotTimeSeconds(RealtimeTransportClient.RealtimePlayerState playerState)
        {
            if (playerState == null)
            {
                return latestServerTimeSeconds;
            }

            var tickRate = latestServerTickRate;
            if (realtimeClient != null && realtimeClient.LatestServerTickRate > 0)
            {
                tickRate = realtimeClient.LatestServerTickRate;
            }

            if (playerState.sampleTick > 0)
            {
                return playerState.sampleTick / tickRate;
            }

            if (playerState.sampleTimeMs > 0)
            {
                return playerState.sampleTimeMs / 1000.0;
            }

            return latestServerTimeSeconds;
        }

        private static void AddSnapshot(
            List<PresenceSnapshot> snapshots,
            double timeSeconds,
            Vector3 position,
            float yaw,
            Vector3 serverVelocity = default,
            bool useServerVelocity = false)
        {
            if (snapshots.Count > 0 && timeSeconds < snapshots[snapshots.Count - 1].TimeSeconds)
            {
                return;
            }

            var velocity = Vector3.zero;
            var yawVelocity = 0f;

            if (snapshots.Count > 0)
            {
                var lastIndex = snapshots.Count - 1;
                var last = snapshots[lastIndex];
                var timeDelta = timeSeconds - last.TimeSeconds;

                // Replace same-time sample instead of adding duplicates (common on uneven network delivery).
                if (Math.Abs(timeDelta) <= 0.0001)
                {
                    if (useServerVelocity)
                    {
                        velocity = serverVelocity;
                        yawVelocity = Mathf.DeltaAngle(last.Yaw, yaw) / (float)Math.Max(0.0001, timeDelta);
                    }
                    else if (lastIndex > 0)
                    {
                        var prev = snapshots[lastIndex - 1];
                        var span = (float)(last.TimeSeconds - prev.TimeSeconds);
                        if (span > 0.0001f)
                        {
                            velocity = (position - prev.Position) / span;
                            yawVelocity = Mathf.DeltaAngle(prev.Yaw, yaw) / span;
                        }
                    }

                    snapshots[lastIndex] = new PresenceSnapshot
                    {
                        TimeSeconds = timeSeconds,
                        Position = position,
                        Yaw = yaw,
                        Velocity = velocity,
                        YawVelocity = yawVelocity
                    };
                    return;
                }

                // Ignore almost-identical samples that only add jitter noise.
                if (!useServerVelocity &&
                    timeDelta <= 0.05 &&
                    (position - last.Position).sqrMagnitude <= 0.000004f &&
                    Mathf.Abs(Mathf.DeltaAngle(last.Yaw, yaw)) <= 0.08f)
                {
                    return;
                }

                if (useServerVelocity)
                {
                    velocity = serverVelocity;
                    yawVelocity = Mathf.DeltaAngle(last.Yaw, yaw) / (float)Math.Max(0.0001, timeDelta);
                }
                else
                {
                    velocity = (position - last.Position) / (float)Math.Max(0.0001, timeDelta);
                    yawVelocity = Mathf.DeltaAngle(last.Yaw, yaw) / (float)Math.Max(0.0001, timeDelta);
                }
            }
            else if (useServerVelocity)
            {
                velocity = serverVelocity;
            }

            snapshots.Add(new PresenceSnapshot
            {
                TimeSeconds = timeSeconds,
                Position = position,
                Yaw = yaw,
                Velocity = velocity,
                YawVelocity = yawVelocity
            });

            if (snapshots.Count > 96)
            {
                snapshots.RemoveAt(0);
            }
        }

        private InterpolatedPose EvaluatePose(
            List<PresenceSnapshot> snapshots,
            double renderTime,
            RemoteAvatar avatar)
        {
            if (snapshots == null || snapshots.Count == 0)
            {
                if (avatar != null && avatar.HasKnownPose)
                {
                    return new InterpolatedPose
                    {
                        Position = avatar.LastKnownPosition,
                        Yaw = avatar.LastKnownYaw,
                        Velocity = Vector3.zero,
                        YawVelocity = 0f
                    };
                }

                return new InterpolatedPose
                {
                    Position = transform.position,
                    Yaw = transform.eulerAngles.y,
                    Velocity = Vector3.zero,
                    YawVelocity = 0f
                };
            }

            while (snapshots.Count >= 2 && snapshots[1].TimeSeconds <= renderTime)
            {
                snapshots.RemoveAt(0);
            }

            if (snapshots.Count >= 2 &&
                snapshots[0].TimeSeconds <= renderTime &&
                renderTime <= snapshots[1].TimeSeconds)
            {
                var from = snapshots[0];
                var to = snapshots[1];
                var range = Math.Max(0.0001, to.TimeSeconds - from.TimeSeconds);
                var t = Mathf.Clamp01((float)((renderTime - from.TimeSeconds) / range));
                return new InterpolatedPose
                {
                    Position = HermitePosition(t, from.Position, to.Position, from.Velocity, to.Velocity, (float)range),
                    Yaw = HermiteYaw(t, from.Yaw, to.Yaw, from.YawVelocity, to.YawVelocity, (float)range),
                    Velocity = HermiteVelocity(t, from.Position, to.Position, from.Velocity, to.Velocity, (float)range),
                    YawVelocity = HermiteYawVelocity(t, from.Yaw, to.Yaw, from.YawVelocity, to.YawVelocity, (float)range)
                };
            }

            if (snapshots.Count >= 2)
            {
                var prev = snapshots[snapshots.Count - 2];
                var last = snapshots[snapshots.Count - 1];
                var extra = Mathf.Clamp((float)(renderTime - last.TimeSeconds), 0f, extrapolationLimitSeconds);
                var horizontalVelocity = Vector3.ClampMagnitude(
                    new Vector3(last.Velocity.x, 0f, last.Velocity.z),
                    12f);
                var verticalVelocity = Mathf.Clamp(last.Velocity.y, -8f, 8f);
                var extrapolatedY = last.Position.y + verticalVelocity * extra;
                if (avatar != null && !avatar.NetworkGrounded)
                {
                    extrapolatedY = last.Position.y;
                }

                return new InterpolatedPose
                {
                    Position = new Vector3(
                        last.Position.x + horizontalVelocity.x * extra,
                        extrapolatedY,
                        last.Position.z + horizontalVelocity.z * extra),
                    Yaw = last.Yaw + last.YawVelocity * extra,
                    Velocity = last.Velocity,
                    YawVelocity = last.YawVelocity
                };
            }

            var only = snapshots[0];
            return new InterpolatedPose
            {
                Position = only.Position,
                Yaw = only.Yaw,
                Velocity = only.Velocity,
                YawVelocity = only.YawVelocity
            };
        }

        private static Vector3 HermitePosition(
            float t,
            Vector3 p0,
            Vector3 p1,
            Vector3 v0,
            Vector3 v1,
            float segmentDuration)
        {
            var m0 = v0 * segmentDuration;
            var m1 = v1 * segmentDuration;
            var t2 = t * t;
            var t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p0 +
                   (t3 - 2f * t2 + t) * m0 +
                   (-2f * t3 + 3f * t2) * p1 +
                   (t3 - t2) * m1;
        }

        private static Vector3 HermiteVelocity(
            float t,
            Vector3 p0,
            Vector3 p1,
            Vector3 v0,
            Vector3 v1,
            float segmentDuration)
        {
            if (segmentDuration <= 0.0001f)
            {
                return v1;
            }

            var m0 = v0 * segmentDuration;
            var m1 = v1 * segmentDuration;
            var t2 = t * t;
            var derivative = (6f * t2 - 6f * t) * p0 +
                             (3f * t2 - 4f * t + 1f) * m0 +
                             (-6f * t2 + 6f * t) * p1 +
                             (3f * t2 - 2f * t) * m1;
            return derivative / segmentDuration;
        }

        private static float HermiteYaw(
            float t,
            float yaw0,
            float yaw1,
            float yawVel0,
            float yawVel1,
            float segmentDuration)
        {
            var unwrappedTarget = yaw0 + Mathf.DeltaAngle(yaw0, yaw1);
            var m0 = yawVel0 * segmentDuration;
            var m1 = yawVel1 * segmentDuration;
            var t2 = t * t;
            var t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * yaw0 +
                   (t3 - 2f * t2 + t) * m0 +
                   (-2f * t3 + 3f * t2) * unwrappedTarget +
                   (t3 - t2) * m1;
        }

        private static float HermiteYawVelocity(
            float t,
            float yaw0,
            float yaw1,
            float yawVel0,
            float yawVel1,
            float segmentDuration)
        {
            if (segmentDuration <= 0.0001f)
            {
                return yawVel1;
            }

            var unwrappedTarget = yaw0 + Mathf.DeltaAngle(yaw0, yaw1);
            var m0 = yawVel0 * segmentDuration;
            var m1 = yawVel1 * segmentDuration;
            var t2 = t * t;
            var derivative = (6f * t2 - 6f * t) * yaw0 +
                             (3f * t2 - 4f * t + 1f) * m0 +
                             (-6f * t2 + 6f * t) * unwrappedTarget +
                             (3f * t2 - 2f * t) * m1;
            return derivative / segmentDuration;
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
