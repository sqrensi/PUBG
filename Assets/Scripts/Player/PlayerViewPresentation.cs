using UnityEngine;

namespace ShooterPrototype.Player
{
    public sealed class PlayerViewPresentation : MonoBehaviour
    {
        [Header("View Roots")]
        [SerializeField] private GameObject firstPersonRoot;
        [SerializeField] private GameObject thirdPersonRoot;
        [SerializeField] private bool isLocalPlayer = true;
        [SerializeField] private bool useSingleModelForFirstPerson = true;
        [SerializeField] private string armNameContains = "Thread";
        [SerializeField] private string weaponNameContains = "Weapon";

        public void ConfigureRoots(GameObject firstPerson, GameObject thirdPerson)
        {
            firstPersonRoot = firstPerson;
            thirdPersonRoot = thirdPerson;
        }

        public void Configure(bool localPlayer)
        {
            isLocalPlayer = localPlayer;
            ApplyViewMode();
        }

        public void RefreshViewMode()
        {
            ApplyViewMode();
        }

        private void Awake()
        {
            ApplyViewMode();
        }

        private void ApplyViewMode()
        {
            if (useSingleModelForFirstPerson && thirdPersonRoot != null)
            {
                if (firstPersonRoot != null)
                {
                    firstPersonRoot.SetActive(false);
                }

                thirdPersonRoot.SetActive(true);
                ApplySingleModelVisibility();
                return;
            }

            if (firstPersonRoot != null)
            {
                firstPersonRoot.SetActive(isLocalPlayer);
            }

            if (thirdPersonRoot != null)
            {
                thirdPersonRoot.SetActive(!isLocalPlayer);
            }
        }

        private void ApplySingleModelVisibility()
        {
            if (thirdPersonRoot == null)
            {
                return;
            }

            var renderers = thirdPersonRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!isLocalPlayer)
                {
                    renderer.enabled = true;
                    continue;
                }

                // Keep shadow-only proxies enabled for local first-person view.
                // They are invisible but needed to cast the character shadow.
                if (renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                {
                    renderer.enabled = true;
                    continue;
                }

                var objectName = renderer.gameObject.name;
                renderer.enabled = IsFirstPersonVisibleRenderer(renderer.transform, objectName);
            }
        }

        private bool IsFirstPersonVisibleRenderer(Transform rendererTransform, string objectName)
        {
            if (!string.IsNullOrWhiteSpace(objectName) &&
                objectName.IndexOf(armNameContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(objectName) &&
                objectName.IndexOf(weaponNameContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var current = rendererTransform != null ? rendererTransform.parent : null;
            while (current != null)
            {
                var parentName = current.name;
                if ((!string.IsNullOrWhiteSpace(parentName) &&
                     parentName.IndexOf(armNameContains, System.StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(parentName) &&
                     parentName.IndexOf(weaponNameContains, System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }
    }
}
