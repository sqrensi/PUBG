using System.Collections;
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

        [Header("Weapon")]
        [SerializeField] private bool automatic = true;
        [SerializeField] private float fireRate = 9f;
        [SerializeField] private float maxDistance = 180f;
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Tracer")]
        [SerializeField] private bool showTracer = true;
        [SerializeField] private float tracerDuration = 0.05f;
        [SerializeField] private float tracerWidth = 0.01f;
        [SerializeField] private Color tracerColor = new Color(1f, 0.92f, 0.72f, 0.95f);

        private float nextFireTime;
        private Material tracerMaterial;

        public void Configure(Camera localCamera, Transform weaponMuzzle)
        {
            playerCamera = localCamera;
            muzzle = weaponMuzzle;
        }

        private void Awake()
        {
            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>();
            }
        }

        private void Update()
        {
            if (!enabled || Time.time < nextFireTime)
            {
                return;
            }

            if (!ReadFirePressed())
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

            var origin = muzzle != null ? muzzle.position : transform.position + transform.forward * 0.2f;
            var direction = ResolveShootDirection(origin);
            var endPoint = origin + direction * maxDistance;

            if (Physics.Raycast(origin, direction, out var hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore))
            {
                endPoint = hit.point;
            }

            if (showTracer)
            {
                StartCoroutine(SpawnTracer(origin, endPoint));
            }
        }

        private Vector3 ResolveShootDirection(Vector3 muzzleOrigin)
        {
            if (playerCamera == null)
            {
                return transform.forward;
            }

            var ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var target = ray.origin + ray.direction * maxDistance;
            if (Physics.Raycast(ray, out var hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore))
            {
                target = hit.point;
            }

            var shootDir = (target - muzzleOrigin);
            return shootDir.sqrMagnitude > 0.00001f ? shootDir.normalized : transform.forward;
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
