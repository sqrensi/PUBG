using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Network;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShooterPrototype.Player
{
    public sealed class PlayerWeaponController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Transform muzzle;
        [SerializeField] private string muzzleName = "Muzzle";

        [Header("Weapon")]
        [SerializeField] private bool automatic = true;
        [SerializeField] private float fireRate = 9f;
        [SerializeField] private float maxDistance = 180f;
        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField] private bool detectTriggerHitboxes = true;
        [SerializeField] private float legDamage = 15f;
        [SerializeField] private float bodyDamage = 25f;
        [SerializeField] private float neckDamage = 80f;
        [SerializeField] private float headDamage = 100f;
        [SerializeField] private int magazineSize = 30;
        [SerializeField] private float reloadDuration = 1.8f;
        [SerializeField] private bool autoReloadWhenEmpty = true;

        [Header("Wall Collision Fire Block")]
        [SerializeField] private bool blockFireWhenWeaponCollides = true;
        [SerializeField, Range(0f, 1f)] private float fireBlockWallAvoidThreshold = 0.3f;
        [SerializeField] private bool blockFireWhenSprinting = true;

        [Header("Spray / Recoil")]
        [SerializeField] private float sprayResetDelay = 0.24f;
        [SerializeField] private float spreadStartDegrees = 0.08f;
        [SerializeField] private float spreadPerShotDegrees = 0.22f;
        [SerializeField] private float spreadMaxDegrees = 2.3f;
        [SerializeField] private float recoilPitchMin = 5.2f;
        [SerializeField] private float recoilPitchMax = 7.4f;
        [SerializeField] private float recoilYawScale = 1.35f;
        [SerializeField] private float hipFireSpreadMultiplier = 1.75f;
        [SerializeField] private float adsSpreadMultiplier = 0.65f;
        [SerializeField] private float crouchSpreadMultiplier = 0.82f;
        [SerializeField] private float movingSpreadMultiplier = 1.9f;
        [SerializeField] private float jumpSpreadMultiplier = 2.8f;
        [SerializeField] private float movingSpreadInputThreshold = 0.08f;
        [SerializeField] private float hipFireRecoilMultiplier = 1.75f;
        [SerializeField] private float adsRecoilMultiplier = 1.35f;
        [SerializeField] private float crouchRecoilMultiplier = 1.2f;
        [SerializeField] private Vector2[] sprayPattern = new[]
        {
            new Vector2(0.0f, 1.0f), new Vector2(0.12f, 1.15f), new Vector2(-0.16f, 1.3f),
            new Vector2(0.22f, 1.55f), new Vector2(-0.28f, 1.7f), new Vector2(0.33f, 1.85f),
            new Vector2(-0.38f, 2.0f), new Vector2(0.44f, 2.1f), new Vector2(-0.5f, 2.2f),
            new Vector2(0.58f, 2.35f), new Vector2(-0.62f, 2.45f), new Vector2(0.66f, 2.55f)
        };

        [Header("Tracer")]
        [SerializeField] private bool showTracer = true;
        [SerializeField] private float tracerDuration = 0.05f;
        [SerializeField] private float tracerWidth = 0.01f;
        [SerializeField] private Color tracerColor = new Color(1f, 0.92f, 0.72f, 0.95f);

        [Header("VFX")]
        [SerializeField] private GameObject muzzleFlashVfx;
        [SerializeField] private GameObject playerHitVfx;
        [SerializeField] private GameObject worldHitVfx;
        [SerializeField] private float vfxAutoDestroySeconds = 2f;
        [SerializeField] private float headshotSingleRegistrationSeconds = 3.2f;

        private float nextFireTime;
        private float lastShotAt = -100f;
        private int burstShotCount;
        private int shotSequence;
        private int reloadSequence;
        private int hitPlayerSequence;
        private Vector3 lastShotOrigin;
        private Vector3 lastShotDirection = Vector3.forward;
        private int currentAmmo;
        private bool isReloading;
        private Coroutine reloadCoroutine;
        private Material tracerMaterial;
        private FpsCharacterController fpsController;
        private PlayerWeaponMount weaponMount;
        private PlayerAudioController audioController;
        private RealtimeTransportClient realtimeClient;
        private readonly RaycastHit[] hitQueryBuffer = new RaycastHit[32];
        private readonly Dictionary<string, float> headshotRegistrationUntilByTarget = new Dictionary<string, float>();

        public void Configure(Camera localCamera, Transform weaponMuzzle)
        {
            playerCamera = localCamera;
            muzzle = weaponMuzzle;
        }

        public int LastShotSequence => shotSequence;
        public Vector3 LastShotOrigin => lastShotOrigin;
        public Vector3 LastShotDirection => lastShotDirection;
        public int LastReloadSequence => reloadSequence;
        public int LastHitPlayerSequence => hitPlayerSequence;
        public int CurrentAmmo => currentAmmo;
        public int MagazineSize => Mathf.Max(1, magazineSize);
        public bool IsReloading => isReloading;
        public float ReloadDurationSeconds => Mathf.Max(0.05f, reloadDuration);

        private void Awake()
        {
            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>();
            }

            fpsController = GetComponent<FpsCharacterController>();
            weaponMount = GetComponent<PlayerWeaponMount>();
            audioController = GetComponent<PlayerAudioController>();
            realtimeClient = FindObjectOfType<RealtimeTransportClient>();
            currentAmmo = Mathf.Max(1, magazineSize);
        }

        private void Update()
        {
            TryResolveRuntimeMuzzle();
            var firePressed = ReadFirePressed();

            if (!enabled)
            {
                fpsController?.SetAutoRecoilRecoveryActive(false);
                return;
            }

            fpsController?.SetAutoRecoilRecoveryActive(firePressed && !isReloading);

            if (ReadReloadPressed())
            {
                TryStartReload();
            }

            // Reload input has priority over fire cadence: pressing reload while shooting
            // immediately stops firing and starts the reload flow.
            if (isReloading)
            {
                return;
            }

            if (Time.time < nextFireTime)
            {
                return;
            }

            if (!firePressed)
            {
                return;
            }

            if (currentAmmo <= 0)
            {
                if (autoReloadWhenEmpty)
                {
                    TryStartReload();
                }
                return;
            }

            if (ShouldBlockFireByWallCollision())
            {
                return;
            }

            if (ShouldBlockFireBySprint())
            {
                return;
            }

            FireOnce();
        }

        private bool ReadFirePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
            {
                return false;
            }
            return automatic
                ? Mouse.current.leftButton.isPressed
                : Mouse.current.leftButton.wasPressedThisFrame;
#else
            return automatic ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
#endif
        }

        private void FireOnce()
        {
            nextFireTime = Time.time + (1f / Mathf.Max(0.01f, fireRate));
            UpdateBurstState();
            TryResolveRuntimeMuzzle();

            var origin = muzzle != null ? muzzle.position : transform.position + transform.forward * 0.2f;
            var direction = ResolveShootDirection(origin);
            lastShotOrigin = origin;
            lastShotDirection = direction;
            shotSequence++;
            currentAmmo = Mathf.Max(0, currentAmmo - 1);
            SimulateShotEffects(origin, direction, applyRecoil: true);
            audioController?.PlayShot(true);
            if (autoReloadWhenEmpty && currentAmmo <= 0)
            {
                TryStartReload();
            }
        }

        public void PlayRemoteShot(Vector3 networkOrigin, Vector3 networkDirection, float lookPitch)
        {
            TryResolveRuntimeMuzzle();
            var hasNetworkShot = networkDirection.sqrMagnitude > 0.0001f;
            var origin = hasNetworkShot
                ? networkOrigin
                : muzzle != null
                    ? muzzle.position
                    : transform.position + Vector3.up * 1.2f + transform.forward * 0.2f;
            var direction = hasNetworkShot
                ? networkDirection.normalized
                : ResolveRemoteShootDirection(lookPitch);
            SimulateShotEffects(origin, direction, applyRecoil: false);
            audioController?.PlayShot(false);
        }

        public void PlayRemoteReload(float durationSeconds)
        {
            weaponMount?.PlayReloadAnimation(durationSeconds > 0f ? durationSeconds : ReloadDurationSeconds);
            audioController?.PlayReloadSequence(false, durationSeconds > 0f ? durationSeconds : ReloadDurationSeconds);
        }

        public void PlayRemoteHitPlayer()
        {
            audioController?.PlayHitPlayer(false);
        }

        public void RestoreAfterRespawn()
        {
            currentAmmo = MagazineSize;
            isReloading = false;
            if (reloadCoroutine != null)
            {
                StopCoroutine(reloadCoroutine);
                reloadCoroutine = null;
            }

            weaponMount?.SetLocalReloading(false);
        }

        private Vector3 ResolveShootDirection(Vector3 muzzleOrigin)
        {
            if (playerCamera == null)
            {
                return transform.forward;
            }

            var ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            ray = ApplySpreadToRay(ray);
            var target = ray.origin + ray.direction * maxDistance;
            if (TryRaycastIgnoringSelf(ray, maxDistance, out var hit))
            {
                target = hit.point;
            }

            var shootDir = (target - muzzleOrigin);
            return shootDir.sqrMagnitude > 0.00001f ? shootDir.normalized : transform.forward;
        }

        private void UpdateBurstState()
        {
            if (Time.time - lastShotAt > Mathf.Max(0.01f, sprayResetDelay))
            {
                burstShotCount = 0;
            }

            burstShotCount++;
            lastShotAt = Time.time;
        }

        private Ray ApplySpreadToRay(Ray baseRay)
        {
            var (spreadMultiplier, _) = ResolveStanceMultipliers();
            var spread = spreadStartDegrees + (burstShotCount - 1) * Mathf.Max(0f, spreadPerShotDegrees);
            spread *= Mathf.Max(0.05f, spreadMultiplier);
            spread = Mathf.Clamp(spread, 0f, Mathf.Max(0f, spreadMaxDegrees));

            var patternOffset = Vector2.zero;
            if (sprayPattern != null && sprayPattern.Length > 0)
            {
                var index = Mathf.Clamp(burstShotCount - 1, 0, sprayPattern.Length - 1);
                patternOffset = sprayPattern[index];
            }

            var randomOffset = Random.insideUnitCircle * spread * 0.18f;
            var yawOffset = patternOffset.x * recoilYawScale + randomOffset.x;
            var pitchOffset = patternOffset.y * 0.35f + randomOffset.y;
            var offsetRotation = Quaternion.AngleAxis(yawOffset, Vector3.up) *
                                 Quaternion.AngleAxis(-pitchOffset, Vector3.right);
            return new Ray(baseRay.origin, offsetRotation * baseRay.direction);
        }

        private void ApplyRecoilKick()
        {
            if (fpsController == null)
            {
                return;
            }

            var (_, recoilMultiplier) = ResolveStanceMultipliers();
            recoilMultiplier = Mathf.Max(0.05f, recoilMultiplier);

            var pattern = Vector2.zero;
            if (sprayPattern != null && sprayPattern.Length > 0)
            {
                var index = Mathf.Clamp(burstShotCount - 1, 0, sprayPattern.Length - 1);
                pattern = sprayPattern[index];
            }

            var pitch = Random.Range(Mathf.Min(recoilPitchMin, recoilPitchMax), Mathf.Max(recoilPitchMin, recoilPitchMax));
            pitch += pattern.y * 1.1f;
            var yaw = pattern.x * recoilYawScale * 0.9f + Random.Range(-0.32f, 0.32f);
            pitch *= recoilMultiplier;
            yaw *= recoilMultiplier;
            fpsController.ApplyRecoil(pitch, yaw);
        }

        private (float Spread, float Recoil) ResolveStanceMultipliers()
        {
            var isAds = weaponMount != null
                ? weaponMount.AdsBlend > 0.5f
                : ReadAimPressed();
            var spread = isAds ? adsSpreadMultiplier : hipFireSpreadMultiplier;
            var recoil = isAds ? adsRecoilMultiplier : hipFireRecoilMultiplier;

            if (fpsController != null && fpsController.IsCrouching)
            {
                spread *= crouchSpreadMultiplier;
                recoil *= crouchRecoilMultiplier;
            }

            if (fpsController != null)
            {
                var moving = fpsController.IsGrounded && fpsController.MoveInputMagnitude > Mathf.Clamp01(movingSpreadInputThreshold);
                if (moving)
                {
                    spread *= Mathf.Max(1f, movingSpreadMultiplier);
                }

                if (!fpsController.IsGrounded)
                {
                    spread *= Mathf.Max(1f, jumpSpreadMultiplier);
                }
            }

            return (spread, recoil);
        }

        private void SimulateShotEffects(Vector3 origin, Vector3 direction, bool applyRecoil)
        {
            var endPoint = origin + direction * maxDistance;
            SpawnVfx(muzzleFlashVfx, origin, Quaternion.LookRotation(direction, Vector3.up));
            if (applyRecoil)
            {
                ApplyRecoilKick();
            }

            if (TryRaycastIgnoringSelf(new Ray(origin, direction), maxDistance, out var hit))
            {
                endPoint = hit.point;
                SpawnHitVfx(hit, direction, applyRecoil);
            }

            if (showTracer)
            {
                StartCoroutine(SpawnTracer(origin, endPoint));
            }
        }

        private Vector3 ResolveRemoteShootDirection(float lookPitch)
        {
            if (muzzle != null)
            {
                return muzzle.forward;
            }

            var forwardWithPitch = Quaternion.Euler(lookPitch, transform.eulerAngles.y, 0f) * Vector3.forward;
            return forwardWithPitch.sqrMagnitude > 0.00001f ? forwardWithPitch.normalized : transform.forward;
        }

        private void SpawnHitVfx(RaycastHit hit, Vector3 shotDirection, bool notifyNetworkHit)
        {
            var hitZone = ResolveHitZone(hit.collider);
            var playerHit = IsPlayerHit(hit.collider);
            var registerThisHit = !playerHit || ShouldRegisterHitOnce(hit.collider, hitZone);
            var targetVfx = playerHit ? playerHitVfx : worldHitVfx;
            if (targetVfx == null)
            {
                if (playerHit)
                {
                    if (registerThisHit)
                    {
                        TryPlayHeadshotAudio(hitZone);
                        if (notifyNetworkHit)
                        {
                            TrySendPlayerHitToServer(hit.collider, shotDirection, hitZone, hit.point);
                        }
                    }
                }
                return;
            }

            var normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal : -hit.transform.forward;
            SpawnVfx(targetVfx, hit.point, Quaternion.LookRotation(normal, Vector3.up));
            if (playerHit)
            {
                if (registerThisHit)
                {
                    TryPlayHeadshotAudio(hitZone);
                    if (notifyNetworkHit)
                    {
                        TrySendPlayerHitToServer(hit.collider, shotDirection, hitZone, hit.point);
                    }
                }
            }
        }

        private bool ShouldRegisterHitOnce(Collider targetCollider, HitZone hitZone)
        {
            if (hitZone != HitZone.Head || targetCollider == null)
            {
                return true;
            }

            var targetKey = ResolveTargetRegistrationKey(targetCollider);
            if (string.IsNullOrEmpty(targetKey))
            {
                return true;
            }

            if (headshotRegistrationUntilByTarget.TryGetValue(targetKey, out var lockUntil) &&
                Time.time < lockUntil)
            {
                return false;
            }

            headshotRegistrationUntilByTarget[targetKey] =
                Time.time + Mathf.Max(0.1f, headshotSingleRegistrationSeconds);
            return true;
        }

        private static string ResolveTargetRegistrationKey(Collider targetCollider)
        {
            var identity = targetCollider.GetComponentInParent<PlayerNetworkIdentity>();
            if (identity != null && !string.IsNullOrWhiteSpace(identity.TicketId))
            {
                return identity.TicketId.Trim();
            }

            return targetCollider.GetInstanceID().ToString();
        }

        private bool IsPlayerHit(Collider targetCollider)
        {
            if (targetCollider == null || targetCollider.transform.IsChildOf(transform))
            {
                return false;
            }

            return targetCollider.GetComponentInParent<FpsCharacterController>() != null ||
                   targetCollider.GetComponentInParent<PlayerNetworkIdentity>() != null;
        }

        private float ResolveDamage(HitZone hitZone)
        {
            switch (hitZone)
            {
                case HitZone.Leg:
                    return Mathf.Max(0f, legDamage);
                case HitZone.Neck:
                    return Mathf.Max(0f, neckDamage);
                case HitZone.Head:
                    return Mathf.Max(0f, headDamage);
                default:
                    return Mathf.Max(0f, bodyDamage);
            }
        }

        private static HitZone ResolveHitZone(Collider targetCollider)
        {
            if (targetCollider == null)
            {
                return HitZone.Body;
            }

            var current = targetCollider.transform;
            while (current != null)
            {
                var name = current.name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var lower = name.ToLowerInvariant();
                    if (lower.Contains("head"))
                    {
                        return HitZone.Head;
                    }

                    if (lower.Contains("neck"))
                    {
                        return HitZone.Neck;
                    }

                    if (lower.Contains("leg") || lower.Contains("foot"))
                    {
                        return HitZone.Leg;
                    }
                }

                current = current.parent;
            }

            return HitZone.Body;
        }

        private void TryPlayHeadshotAudio(HitZone hitZone)
        {
            if (hitZone != HitZone.Head)
            {
                return;
            }

            hitPlayerSequence++;
            audioController?.PlayHitPlayer(true);
        }

        private QueryTriggerInteraction ResolveHitQueryTriggerInteraction()
        {
            return detectTriggerHitboxes
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;
        }

        private bool TryRaycastIgnoringSelf(Ray ray, float distance, out RaycastHit closestHit)
        {
            var hitCount = Physics.RaycastNonAlloc(
                ray,
                hitQueryBuffer,
                distance,
                hitMask,
                ResolveHitQueryTriggerInteraction());
            if (hitCount <= 0)
            {
                closestHit = default;
                return false;
            }

            System.Array.Sort(hitQueryBuffer, 0, hitCount, RaycastHitDistanceComparer.Instance);
            for (var i = 0; i < hitCount; i++)
            {
                var hit = hitQueryBuffer[i];
                if (hit.collider == null)
                {
                    continue;
                }

                if (hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                closestHit = hit;
                return true;
            }

            closestHit = default;
            return false;
        }

        private sealed class RaycastHitDistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly RaycastHitDistanceComparer Instance = new RaycastHitDistanceComparer();
            public int Compare(RaycastHit x, RaycastHit y) => x.distance.CompareTo(y.distance);
        }

        private enum HitZone
        {
            Body = 0,
            Leg = 1,
            Neck = 2,
            Head = 3
        }

        private void TrySendPlayerHitToServer(
            Collider targetCollider,
            Vector3 shotDirection,
            HitZone hitZone,
            Vector3 hitPoint)
        {
            if (targetCollider == null)
            {
                return;
            }

            var identity = targetCollider.GetComponentInParent<PlayerNetworkIdentity>();
            if (identity == null || identity.IsLocalPlayer || string.IsNullOrWhiteSpace(identity.TicketId))
            {
                return;
            }

            if (realtimeClient == null)
            {
                realtimeClient = FindObjectOfType<RealtimeTransportClient>();
            }

            var shotTick = realtimeClient != null ? realtimeClient.LatestServerTick : 0;
            realtimeClient?.SendHit(
                identity.TicketId,
                ResolveDamage(hitZone),
                shotDirection,
                shotSequence,
                shotTick,
                hitPoint,
                hitZone.ToString().ToLowerInvariant());
        }

        private void SpawnVfx(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null)
            {
                return;
            }

            var instance = Instantiate(prefab, position, rotation);
            if (vfxAutoDestroySeconds > 0f)
            {
                Destroy(instance, vfxAutoDestroySeconds);
            }
        }

        private bool TryStartReload()
        {
            if (isReloading || currentAmmo >= MagazineSize)
            {
                return false;
            }

            if (reloadCoroutine != null)
            {
                StopCoroutine(reloadCoroutine);
                reloadCoroutine = null;
            }

            reloadSequence++;
            reloadCoroutine = StartCoroutine(ReloadRoutine());
            return true;
        }

        private bool ShouldBlockFireByWallCollision()
        {
            if (!blockFireWhenWeaponCollides || weaponMount == null)
            {
                return false;
            }

            return weaponMount.CurrentWallAvoidBlend >= Mathf.Clamp01(fireBlockWallAvoidThreshold);
        }

        private bool ShouldBlockFireBySprint()
        {
            if (!blockFireWhenSprinting)
            {
                return false;
            }

            if (fpsController == null)
            {
                fpsController = GetComponent<FpsCharacterController>();
            }

            return fpsController != null && fpsController.IsSprinting;
        }

        private IEnumerator ReloadRoutine()
        {
            isReloading = true;
            var reloadTime = Mathf.Max(0.05f, reloadDuration);
            nextFireTime = Time.time + reloadTime;
            weaponMount?.SetLocalReloading(true);
            weaponMount?.PlayReloadAnimation(reloadTime);
            audioController?.PlayReloadSequence(true, reloadTime);
            yield return new WaitForSeconds(reloadTime);
            currentAmmo = MagazineSize;
            isReloading = false;
            weaponMount?.SetLocalReloading(false);
            reloadCoroutine = null;
        }

        private void TryResolveRuntimeMuzzle()
        {
            if (muzzle != null)
            {
                return;
            }

            if (weaponMount == null)
            {
                weaponMount = GetComponent<PlayerWeaponMount>();
                if (weaponMount == null)
                {
                    return;
                }
            }

            var weaponRoot = weaponMount.MountedWeaponRoot;
            if (weaponRoot == null)
            {
                return;
            }

            muzzle = FindChildRecursive(weaponRoot, muzzleName);
            if (muzzle != null)
            {
                return;
            }

            var fallbackNames = new[] { "MuzzlePoint", "MuzzleFlash", "BarrelEnd", "Barrel", "FirePoint" };
            for (var i = 0; i < fallbackNames.Length; i++)
            {
                muzzle = FindChildRecursive(weaponRoot, fallbackNames[i]);
                if (muzzle != null)
                {
                    return;
                }
            }
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t != null && string.Equals(t.name, childName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return t;
                }
            }

            return null;
        }

        private static bool ReadAimPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
            return Input.GetMouseButton(1);
#endif
        }

        private static bool ReadReloadPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.R);
#endif
        }

        private IEnumerator SpawnTracer(Vector3 from, Vector3 to)
        {
            var tracerObject = new GameObject("ShotTracer");
            var line = tracerObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = tracerWidth;
            line.endWidth = tracerWidth;
            line.startColor = tracerColor;
            line.endColor = tracerColor;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            line.sortingOrder = 40;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;

            if (tracerMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                {
                    shader = Shader.Find("Unlit/Color");
                }
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                if (shader != null)
                {
                    tracerMaterial = new Material(shader);
                    if (tracerMaterial.HasProperty("_Color"))
                    {
                        tracerMaterial.color = tracerColor;
                    }
                }
            }

            if (tracerMaterial != null)
            {
                line.sharedMaterial = tracerMaterial;
            }

            line.SetPosition(0, from);
            line.SetPosition(1, to);

            yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, tracerDuration));
            Destroy(tracerObject);
        }
    }
}
