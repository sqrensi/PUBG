using System.Collections;
using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Lightweight third-person shot VFX/audio for remote avatars (no input, damage, or ammo).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RemotePlayerShotEffects : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private string muzzleName = "Muzzle";

        [Header("Shot")]
        [SerializeField] private float maxDistance = 180f;
        [SerializeField] private float eyeHeight = 1.65f;
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Tracer")]
        [SerializeField] private bool showTracer = true;
        [SerializeField] private float tracerDuration = 0.05f;
        [SerializeField] private float tracerWidth = 0.01f;
        [SerializeField] private Color tracerColor = new Color(1f, 0.92f, 0.72f, 0.95f);

        [Header("VFX")]
        [SerializeField] private GameObject muzzleFlashVfx;
        [SerializeField] private GameObject worldHitVfx;
        [SerializeField] private GameObject playerHitVfx;
        [SerializeField] private float vfxAutoDestroySeconds = 2f;

        private Transform muzzle;
        private Material tracerMaterial;
        private PlayerAudioController audioController;
        private RemoteWeaponPresentation remoteWeapon;
        private readonly RaycastHit[] hitQueryBuffer = new RaycastHit[32];

        public float ReloadDurationSeconds => 1.8f;

        public void ApplyVisualSettings(
            GameObject muzzleFlash,
            GameObject worldHit,
            GameObject playerHit,
            float shotMaxDistance)
        {
            if (muzzleFlash != null)
            {
                muzzleFlashVfx = muzzleFlash;
            }

            if (worldHit != null)
            {
                worldHitVfx = worldHit;
            }

            if (playerHit != null)
            {
                playerHitVfx = playerHit;
            }

            if (shotMaxDistance > 1f)
            {
                maxDistance = shotMaxDistance;
            }
        }

        private void Awake()
        {
            audioController = GetComponent<PlayerAudioController>();
            remoteWeapon = GetComponent<RemoteWeaponPresentation>();
        }

        public void PlayRemoteShot(
            Vector3 networkOrigin,
            Vector3 networkDirection,
            Vector3 networkEndPoint,
            bool hasNetworkEndPoint,
            float lookPitch)
        {
            EnsureAudioSources();

            var hasNetworkDirection = networkDirection.sqrMagnitude > 0.0001f;
            var direction = hasNetworkDirection
                ? networkDirection.normalized
                : ResolveFallbackDirection(lookPitch);

            var visualOrigin = ResolveThirdPersonMuzzlePosition(lookPitch);
            SpawnVfx(muzzleFlashVfx, visualOrigin, Quaternion.LookRotation(direction, Vector3.up));

            var endPoint = visualOrigin + direction * maxDistance;
            if (TryResolveImpactPoint(
                    networkOrigin,
                    networkEndPoint,
                    hasNetworkEndPoint,
                    lookPitch,
                    direction,
                    out var impactPoint,
                    out var impactNormal,
                    out var usePlayerHitVfx))
            {
                endPoint = impactPoint;
                var hitPrefab = usePlayerHitVfx ? playerHitVfx : worldHitVfx;
                SpawnVfx(hitPrefab, impactPoint, Quaternion.LookRotation(impactNormal, Vector3.up));
            }

            if (showTracer)
            {
                StartCoroutine(SpawnTracer(visualOrigin, endPoint));
            }

            audioController?.PlayShot(false);
        }

        private Vector3 ResolveThirdPersonMuzzlePosition(float lookPitch)
        {
            ResolveMuzzle();
            if (muzzle != null)
            {
                return muzzle.position;
            }

            return transform.position + Vector3.up * 1.2f + ResolveFallbackDirection(lookPitch) * 0.2f;
        }

        private Vector3 ResolveEyePosition()
        {
            return transform.position + Vector3.up * eyeHeight;
        }

        private bool TryResolveImpactPoint(
            Vector3 networkOrigin,
            Vector3 networkEndPoint,
            bool hasNetworkEndPoint,
            float lookPitch,
            Vector3 direction,
            out Vector3 impactPoint,
            out Vector3 impactNormal,
            out bool usePlayerHitVfx)
        {
            impactPoint = default;
            impactNormal = -direction.sqrMagnitude > 0.0001f ? -direction.normalized : Vector3.up;
            usePlayerHitVfx = false;

            if (TryGetNetworkImpactPoint(networkOrigin, networkEndPoint, hasNetworkEndPoint, out impactPoint))
            {
                return true;
            }

            var eyeOrigin = ResolveEyePosition();
            var eyeDirection = ResolveLookDirection(lookPitch);
            if (TryRaycastFromOrigin(eyeOrigin, eyeDirection, out var eyeHit))
            {
                impactPoint = eyeHit.point;
                impactNormal = eyeHit.normal.sqrMagnitude > 0.0001f ? eyeHit.normal : impactNormal;
                usePlayerHitVfx = IsPlayerHitCollider(eyeHit.collider);
                return true;
            }

            return false;
        }

        private bool TryGetNetworkImpactPoint(
            Vector3 networkOrigin,
            Vector3 networkEndPoint,
            bool hasNetworkEndPoint,
            out Vector3 impactPoint)
        {
            impactPoint = networkEndPoint;
            if (hasNetworkEndPoint)
            {
                return true;
            }

            var travelOrigin = networkOrigin.sqrMagnitude > 0.0025f ? networkOrigin : ResolveEyePosition();
            var travelSqr = (networkEndPoint - travelOrigin).sqrMagnitude;
            if (travelSqr <= 0.0025f)
            {
                return false;
            }

            var maxTravel = maxDistance + 1f;
            return travelSqr <= maxTravel * maxTravel;
        }

        private bool TryRaycastFromOrigin(Vector3 origin, Vector3 direction, out RaycastHit closestHit)
        {
            var hitCount = Physics.RaycastNonAlloc(
                new Ray(origin, direction),
                hitQueryBuffer,
                maxDistance,
                hitMask,
                QueryTriggerInteraction.Ignore);
            if (hitCount <= 0)
            {
                return TryRaycastTriggers(origin, direction, out closestHit);
            }

            System.Array.Sort(hitQueryBuffer, 0, hitCount, RaycastHitDistanceComparer.Instance);
            for (var i = 0; i < hitCount; i++)
            {
                var hit = hitQueryBuffer[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (IsRemoteWeaponCollider(hit.collider))
                {
                    continue;
                }

                closestHit = hit;
                return true;
            }

            return TryRaycastTriggers(origin, direction, out closestHit);
        }

        private bool TryRaycastTriggers(Vector3 origin, Vector3 direction, out RaycastHit closestHit)
        {
            var hitCount = Physics.RaycastNonAlloc(
                new Ray(origin, direction),
                hitQueryBuffer,
                maxDistance,
                hitMask,
                QueryTriggerInteraction.Collide);
            if (hitCount <= 0)
            {
                closestHit = default;
                return false;
            }

            System.Array.Sort(hitQueryBuffer, 0, hitCount, RaycastHitDistanceComparer.Instance);
            for (var i = 0; i < hitCount; i++)
            {
                var hit = hitQueryBuffer[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (IsRemoteWeaponCollider(hit.collider))
                {
                    continue;
                }

                closestHit = hit;
                return true;
            }

            closestHit = default;
            return false;
        }

        private static bool IsPlayerHitCollider(Collider targetCollider)
        {
            if (targetCollider == null)
            {
                return false;
            }

            if (targetCollider.GetComponentInParent<FpsCharacterController>() != null)
            {
                return true;
            }

            return targetCollider.GetComponentInParent<PlayerBoneHitbox>(true) != null ||
                   targetCollider.GetComponent<CharacterController>() != null;
        }

        private static bool IsRemoteWeaponCollider(Collider targetCollider)
        {
            var current = targetCollider != null ? targetCollider.transform : null;
            while (current != null)
            {
                var name = current.name;
                if (string.Equals(name, "WeaponModel", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "RemoteWeaponTarget", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private void EnsureAudioSources()
        {
            if (audioController == null)
            {
                audioController = GetComponent<PlayerAudioController>();
            }
        }

        public void PlayRemoteReload(float durationSeconds)
        {
            EnsureAudioSources();
            audioController?.PlayReloadSequence(false, durationSeconds > 0f ? durationSeconds : ReloadDurationSeconds);
        }

        public void PlayRemoteHitPlayer()
        {
            EnsureAudioSources();
            audioController?.PlayHitPlayer(false);
        }

        private Vector3 ResolveLookDirection(float lookPitch)
        {
            var forwardWithPitch = Quaternion.Euler(lookPitch, transform.eulerAngles.y, 0f) * Vector3.forward;
            return forwardWithPitch.sqrMagnitude > 0.00001f ? forwardWithPitch.normalized : transform.forward;
        }

        private Vector3 ResolveFallbackDirection(float lookPitch)
        {
            ResolveMuzzle();
            if (muzzle != null)
            {
                return muzzle.forward;
            }

            return ResolveLookDirection(lookPitch);
        }

        private void ResolveMuzzle()
        {
            if (muzzle != null)
            {
                return;
            }

            if (remoteWeapon == null)
            {
                remoteWeapon = GetComponent<RemoteWeaponPresentation>();
            }

            var weaponRoot = remoteWeapon != null ? remoteWeapon.WeaponRoot : null;
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

        private void SpawnVfx(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null)
            {
                return;
            }

            var instance = Instantiate(prefab, position, rotation);
            PlayAllParticleSystems(instance);
            if (vfxAutoDestroySeconds > 0f)
            {
                Destroy(instance, vfxAutoDestroySeconds);
            }
        }

        private static void PlayAllParticleSystems(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (var i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                if (ps != null)
                {
                    ps.Play(true);
                }
            }
        }

        private IEnumerator SpawnTracer(Vector3 start, Vector3 end)
        {
            var tracerObject = new GameObject("RemoteShotTracer");
            var line = tracerObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = tracerWidth;
            line.endWidth = tracerWidth;
            line.material = ResolveTracerMaterial();
            line.startColor = tracerColor;
            line.endColor = tracerColor;
            line.SetPosition(0, start);
            line.SetPosition(1, end);

            yield return new WaitForSeconds(Mathf.Max(0.01f, tracerDuration));
            Destroy(tracerObject);
        }

        private Material ResolveTracerMaterial()
        {
            if (tracerMaterial != null)
            {
                return tracerMaterial;
            }

            var shader = Shader.Find("Sprites/Default");
            tracerMaterial = shader != null ? new Material(shader) : new Material(Shader.Find("Unlit/Color"));
            return tracerMaterial;
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

        private sealed class RaycastHitDistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly RaycastHitDistanceComparer Instance = new RaycastHitDistanceComparer();
            public int Compare(RaycastHit x, RaycastHit y) => x.distance.CompareTo(y.distance);
        }
    }
}
