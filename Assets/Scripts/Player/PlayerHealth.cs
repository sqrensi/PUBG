using System.Collections;
using ShooterPrototype.Network;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public sealed class PlayerHealth : MonoBehaviour
    {
        [Header("Health")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float respawnDelaySeconds = 3f;
        [SerializeField] private bool logDamage = false;
        [Header("Death Fall")]
        [SerializeField] private bool enableDeathFall = true;
        [SerializeField] private float deathFallBackwardImpulse = 1.55f;
        [SerializeField] private float deathFallUpImpulse = 0f;
        [SerializeField] private float deathFallAngularImpulse = 0f;
        [SerializeField] private float deathFallDownImpulse = 3.4f;
        [SerializeField] private bool useSimpleDeathFall = true;
        [SerializeField] private float simpleDeathFallDuration = 0.06f;
        [SerializeField] private float simpleDeathPitch = 90f;
        [SerializeField] private float simpleDeathDropDistance = 0.06f;
        [SerializeField] private float remoteSimpleDeathFallDuration = 0.05f;
        [SerializeField] private float remoteSimpleDeathPitch = 90f;
        [SerializeField] private float remoteSimpleDeathDropDistance = 0.05f;
        [Header("Death Grounding")]
        [SerializeField] private float deathGroundProbeHeight = 1.2f;
        [SerializeField] private float deathGroundProbeDistance = 4f;
        [SerializeField] private float deathGroundClearance = 0.04f;
        [Header("Remote Death Fall Override")]
        [SerializeField] private float remoteDeathFallBackwardImpulse = 1.9f;
        [SerializeField] private float remoteDeathFallUpImpulse = 0f;
        [SerializeField] private float remoteDeathFallAngularImpulse = 0f;
        [SerializeField] private float remoteDeathFallDownImpulse = 4.4f;
        [SerializeField] private float remoteDeathLinearDamping = 0.28f;
        [SerializeField] private float remoteDeathAngularDamping = 12f;

        private float currentHealth;
        private bool isDead;
        private Vector3 initialSpawnPosition;
        private Quaternion initialSpawnRotation;
        private RealtimeTransportClient realtimeClient;
        private Coroutine respawnRoutine;
        private FpsCharacterController fpsController;
        private PlayerWeaponController weaponController;
        private CharacterController characterController;
        private ProceduralLocomotionRig locomotionRig;
        private PlayerNetworkIdentity identity;
        private Rigidbody deathRigidbody;
        private CapsuleCollider deathCapsule;
        private bool networkMode;
        private int deathSequence;
        private int lastNetworkDeathSeq = -1;
        private Coroutine simpleDeathFallRoutine;
        private Vector3 deathFallBasePosition;
        private Vector3 deathFallDirection = Vector3.forward;

        public float MaxHealth => Mathf.Max(1f, maxHealth);
        public float CurrentHealth => Mathf.Clamp(currentHealth, 0f, MaxHealth);
        public bool IsDead => isDead;
        public int DeathSequence => deathSequence;
        public Vector3 DeathFallDirection => deathFallDirection;

        private void Awake()
        {
            currentHealth = MaxHealth;
            initialSpawnPosition = transform.position;
            initialSpawnRotation = transform.rotation;
            fpsController = GetComponent<FpsCharacterController>();
            weaponController = GetComponent<PlayerWeaponController>();
            characterController = GetComponent<CharacterController>();
            identity = GetComponent<PlayerNetworkIdentity>();
            locomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
        }

        private void OnEnable()
        {
            if (!networkMode)
            {
                TryBindRealtimeClient();
            }
        }

        private void Update()
        {
            if (!networkMode && realtimeClient == null)
            {
                TryBindRealtimeClient();
            }
        }

        private void OnDisable()
        {
            if (realtimeClient != null)
            {
                realtimeClient.DamageReceived -= HandleDamageReceived;
            }

            StopDeathFallPhysics();
        }

        private void TryBindRealtimeClient()
        {
            if (realtimeClient != null)
            {
                return;
            }

            realtimeClient = FindObjectOfType<RealtimeTransportClient>();
            if (realtimeClient != null)
            {
                realtimeClient.DamageReceived += HandleDamageReceived;
            }
        }

        private void HandleDamageReceived(RealtimeTransportClient.DamageMessage damageMessage)
        {
            if (networkMode || damageMessage == null || isDead)
            {
                return;
            }

            if (identity != null && !string.IsNullOrWhiteSpace(identity.TicketId))
            {
                if (!string.Equals(identity.TicketId, damageMessage.targetTicketId, System.StringComparison.Ordinal))
                {
                    return;
                }
            }

            var hitDirection = new Vector3(damageMessage.dirX, damageMessage.dirY, damageMessage.dirZ);
            ApplyDamage(damageMessage.damage, damageMessage.attackerTicketId, hitDirection);
        }

        private void ApplyDamage(float amount, string attackerTicketId, Vector3 hitDirection)
        {
            var damage = Mathf.Max(0f, amount);
            if (damage <= 0f)
            {
                return;
            }

            var horizontalDir = Vector3.ProjectOnPlane(hitDirection, Vector3.up);
            if (horizontalDir.sqrMagnitude > 0.0001f)
            {
                deathFallDirection = horizontalDir.normalized;
            }

            currentHealth = Mathf.Max(0f, currentHealth - damage);
            if (logDamage)
            {
                Debug.Log($"[PlayerHealth] took damage={damage:0.##} hp={currentHealth:0.##}/{MaxHealth:0.##} attacker={attackerTicketId}");
            }

            if (currentHealth <= 0.001f)
            {
                HandleDeath();
            }
        }

        private void HandleDeath()
        {
            if (isDead)
            {
                return;
            }

            deathSequence++;
            EnterDeathState(startRespawn: true);
        }

        private IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, respawnDelaySeconds));

            var (position, rotation) = ResolveRespawnPose();
            transform.position = position;
            transform.rotation = rotation;
            ExitDeathState(restoreHealth: true);

            respawnRoutine = null;
        }

        public void SetNetworkMode(bool enabled)
        {
            networkMode = enabled;
            if (enabled && realtimeClient != null)
            {
                realtimeClient.DamageReceived -= HandleDamageReceived;
            }
        }

        public void SetNetworkDeadState(bool dead, int deathSeq, Vector3 networkDeathFallDirection)
        {
            if (!networkMode)
            {
                return;
            }

            deathSeq = Mathf.Max(0, deathSeq);
            if (dead)
            {
                if (deathSeq <= lastNetworkDeathSeq && isDead)
                {
                    return;
                }

                lastNetworkDeathSeq = Mathf.Max(lastNetworkDeathSeq, deathSeq);
                var horizontalDir = Vector3.ProjectOnPlane(networkDeathFallDirection, Vector3.up);
                if (horizontalDir.sqrMagnitude > 0.0001f)
                {
                    deathFallDirection = horizontalDir.normalized;
                }
                EnterDeathState(startRespawn: false);
                return;
            }

            if (!isDead)
            {
                return;
            }

            ExitDeathState(restoreHealth: false);
        }

        private void EnterDeathState(bool startRespawn)
        {
            if (isDead)
            {
                return;
            }

            isDead = true;
            if (fpsController != null)
            {
                fpsController.enabled = false;
            }

            if (characterController != null)
            {
                characterController.enabled = false;
            }

            if (weaponController != null)
            {
                weaponController.enabled = false;
            }

            if (enableDeathFall)
            {
                StartDeathFallPhysics();
            }

            if (startRespawn)
            {
                if (respawnRoutine != null)
                {
                    StopCoroutine(respawnRoutine);
                }

                respawnRoutine = StartCoroutine(RespawnRoutine());
            }
        }

        private void ExitDeathState(bool restoreHealth)
        {
            StopDeathFallPhysics();
            isDead = false;

            if (restoreHealth)
            {
                currentHealth = MaxHealth;
            }

            if (characterController != null)
            {
                characterController.enabled = true;
            }

            if (!networkMode && fpsController != null)
            {
                fpsController.enabled = true;
            }

            if (!networkMode && weaponController != null)
            {
                weaponController.RestoreAfterRespawn();
                weaponController.enabled = true;
            }

        }

        private void StartSimpleDeathFall()
        {
            StopDeathFallPhysics();
            if (simpleDeathFallRoutine != null)
            {
                StopCoroutine(simpleDeathFallRoutine);
            }

            deathFallBasePosition = transform.position;
            simpleDeathFallRoutine = StartCoroutine(SimpleDeathFallRoutine());
        }

        private IEnumerator SimpleDeathFallRoutine()
        {
            var duration = Mathf.Max(0.02f, networkMode ? remoteSimpleDeathFallDuration : simpleDeathFallDuration);
            var startPos = transform.position;
            var startRot = transform.rotation;
            var dropDistance = networkMode ? remoteSimpleDeathDropDistance : simpleDeathDropDistance;
            var pitch = networkMode ? remoteSimpleDeathPitch : simpleDeathPitch;
            var targetPos = ResolveGroundedDeathPosition(deathFallBasePosition, Mathf.Max(0f, dropDistance));
            var fallDir = Vector3.ProjectOnPlane(deathFallDirection, Vector3.up);
            if (fallDir.sqrMagnitude <= 0.0001f)
            {
                fallDir = transform.forward;
            }
            var targetRot = ComputeSimpleFallTargetRotation(startRot, fallDir.normalized, Mathf.Clamp(pitch, 0f, 120f));
            var t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                var blend = Mathf.Clamp01(t / duration);
                var eased = Mathf.SmoothStep(0f, 1f, blend);
                transform.position = Vector3.Lerp(startPos, targetPos, eased);
                transform.rotation = Quaternion.Slerp(startRot, targetRot, eased);
                yield return null;
            }

            transform.position = targetPos;
            transform.rotation = targetRot;
            simpleDeathFallRoutine = null;
        }

        private Quaternion ComputeSimpleFallTargetRotation(Quaternion startRot, Vector3 worldFallDir, float angle)
        {
            if (!networkMode)
            {
                // Keep local behavior closer to previous setup.
                var baseRot = Quaternion.LookRotation(worldFallDir, Vector3.up);
                return baseRot * Quaternion.Euler(angle, 0f, 0f);
            }

            // Remote: deterministic "just fall" behavior.
            // - backward hit -> fall on back
            // - side hit -> fall on side
            // without flip/spin artifacts.
            var yawOnly = Quaternion.Euler(0f, startRot.eulerAngles.y, 0f);
            var localDir = Quaternion.Inverse(yawOnly) * worldFallDir;

            if (Mathf.Abs(localDir.x) > Mathf.Abs(localDir.z))
            {
                // Side fall.
                var roll = localDir.x > 0f ? -angle : angle;
                return yawOnly * Quaternion.Euler(0f, 0f, roll);
            }

            // Front/back fall.
            // localDir.z < 0 means impact from front pushing backward -> fall on back.
            var pitchSign = localDir.z < 0f ? -1f : 1f;
            return yawOnly * Quaternion.Euler(pitchSign * angle, 0f, 0f);
        }

        private Vector3 ResolveGroundedDeathPosition(Vector3 basePosition, float dropDistance)
        {
            var fallback = basePosition + Vector3.down * dropDistance;
            var origin = basePosition + Vector3.up * Mathf.Max(0.1f, deathGroundProbeHeight);
            var hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                Mathf.Max(0.1f, deathGroundProbeDistance),
                ~0,
                QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
            {
                return fallback;
            }

            var bestDistance = float.MaxValue;
            var bestPoint = fallback;
            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    bestPoint = hit.point + Vector3.up * Mathf.Max(0f, deathGroundClearance);
                }
            }

            if (bestDistance >= float.MaxValue)
            {
                return fallback;
            }

            return bestPoint;
        }

        private (Vector3 Position, Quaternion Rotation) ResolveRespawnPose()
        {
            var root = GameObject.Find("SpawnPoints");
            if (root != null && root.transform.childCount > 0)
            {
                var idx = Random.Range(0, root.transform.childCount);
                var candidate = root.transform.GetChild(idx);
                if (candidate != null)
                {
                    return (candidate.position, candidate.rotation);
                }
            }

            return (initialSpawnPosition, initialSpawnRotation);
        }

        private void StartDeathFallPhysics()
        {
            if (simpleDeathFallRoutine != null)
            {
                StopCoroutine(simpleDeathFallRoutine);
                simpleDeathFallRoutine = null;
            }

            if (deathCapsule == null)
            {
                deathCapsule = GetComponent<CapsuleCollider>();
                if (deathCapsule == null)
                {
                    deathCapsule = gameObject.AddComponent<CapsuleCollider>();
                }
            }

            if (characterController != null)
            {
                deathCapsule.radius = Mathf.Max(0.05f, characterController.radius);
                deathCapsule.height = Mathf.Max(characterController.height, deathCapsule.radius * 2f);
                deathCapsule.center = characterController.center;
            }

            deathCapsule.enabled = true;

            if (deathRigidbody == null)
            {
                deathRigidbody = GetComponent<Rigidbody>();
                if (deathRigidbody == null)
                {
                    deathRigidbody = gameObject.AddComponent<Rigidbody>();
                }
            }

            deathRigidbody.isKinematic = false;
            deathRigidbody.useGravity = true;
            deathRigidbody.constraints = RigidbodyConstraints.None;
            deathRigidbody.linearVelocity = Vector3.zero;
            deathRigidbody.angularVelocity = Vector3.zero;
            var fallDir = Vector3.ProjectOnPlane(deathFallDirection, Vector3.up);
            if (fallDir.sqrMagnitude <= 0.0001f)
            {
                fallDir = transform.forward;
            }
            fallDir.Normalize();
            var backwardImpulse = networkMode
                ? Mathf.Max(0f, remoteDeathFallBackwardImpulse)
                : Mathf.Max(0f, deathFallBackwardImpulse);
            var upImpulse = networkMode
                ? Mathf.Max(0f, remoteDeathFallUpImpulse)
                : Mathf.Max(0f, deathFallUpImpulse);
            var downImpulse = networkMode
                ? Mathf.Max(0f, remoteDeathFallDownImpulse)
                : Mathf.Max(0f, deathFallDownImpulse);
            var angularImpulse = networkMode
                ? Mathf.Max(0f, remoteDeathFallAngularImpulse)
                : Mathf.Max(0f, deathFallAngularImpulse);

            if (networkMode)
            {
                // Remote death: fast but stable drop without spin.
                deathRigidbody.linearDamping = Mathf.Max(0f, remoteDeathLinearDamping);
                deathRigidbody.angularDamping = Mathf.Max(0f, remoteDeathAngularDamping);
                deathRigidbody.constraints = RigidbodyConstraints.FreezeRotationY;
            }
            else
            {
                // Local death: avoid spinning; just fall in place.
                deathRigidbody.linearDamping = 0.35f;
                deathRigidbody.angularDamping = 12f;
                deathRigidbody.constraints = RigidbodyConstraints.FreezeRotationY;
            }

            deathRigidbody.AddForce(
                (fallDir * backwardImpulse) +
                (Vector3.up * upImpulse) +
                (Vector3.down * downImpulse),
                ForceMode.VelocityChange);
            if (angularImpulse > 0.0001f)
            {
                deathRigidbody.AddTorque(Random.onUnitSphere * angularImpulse, ForceMode.VelocityChange);
            }
        }

        private void StopDeathFallPhysics()
        {
            if (simpleDeathFallRoutine != null)
            {
                StopCoroutine(simpleDeathFallRoutine);
                simpleDeathFallRoutine = null;
            }

            if (deathRigidbody != null)
            {
                deathRigidbody.linearVelocity = Vector3.zero;
                deathRigidbody.angularVelocity = Vector3.zero;
                deathRigidbody.useGravity = false;
                deathRigidbody.isKinematic = true;
                deathRigidbody.constraints = RigidbodyConstraints.None;
            }

            if (deathCapsule != null)
            {
                deathCapsule.enabled = false;
            }
        }
    }
}
