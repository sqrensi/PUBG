using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Swaps procedural line visuals for a Synty humanoid mesh while keeping gameplay anchors/hitboxes intact.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SyntyCharacterVisualBinder : MonoBehaviour
    {
        [Header("Visual Root")]
        [SerializeField] private Transform syntyVisualRoot;
        [SerializeField] private Vector3 localPositionOffset = Vector3.zero;
        [SerializeField] private Vector3 localEulerOffset = Vector3.zero;
        [SerializeField] private float uniformScale = 1f;

        [Header("Hide Procedural Body")]
        [SerializeField] private bool hideProceduralLines = true;
        [SerializeField] private bool disableThreadArmRig = false;
        [SerializeField] private bool disableHeadMaskSelector = true;

        [Header("First Person")]
        [SerializeField] private bool hideHeadInFirstPerson = true;
        [SerializeField] private string firstPersonArmNameContains = "Hand";
        [SerializeField] private string firstPersonLegNameContains = "Leg";
        [SerializeField] private string firstPersonHeadNameContains = "Head";

        private PlayerViewPresentation viewPresentation;
        private bool configured;

        public Transform SyntyVisualRoot => syntyVisualRoot;

        public void Configure(
            Transform visualRoot,
            Vector3 positionOffset,
            Vector3 eulerOffset,
            float scale)
        {
            syntyVisualRoot = visualRoot;
            localPositionOffset = positionOffset;
            localEulerOffset = eulerOffset;
            uniformScale = Mathf.Max(0.01f, scale);
            ApplyVisualTransform();
            ApplyProceduralVisibility();
            configured = true;
        }

        private void Awake()
        {
            viewPresentation = GetComponent<PlayerViewPresentation>();
            ApplyVisualTransform();
            ApplyProceduralVisibility();
        }

        private void OnEnable()
        {
            ApplyVisualTransform();
            ApplyProceduralVisibility();
            viewPresentation?.RefreshViewMode();
        }

        private void ApplyVisualTransform()
        {
            if (syntyVisualRoot == null)
            {
                return;
            }

            syntyVisualRoot.localPosition = localPositionOffset;
            syntyVisualRoot.localRotation = Quaternion.Euler(localEulerOffset);
            syntyVisualRoot.localScale = Vector3.one * uniformScale;
        }

        private void ApplyProceduralVisibility()
        {
            if (hideProceduralLines)
            {
                HideNamedObjects(transform, "TorsoLine", "NeckLine", "LeftLegLine", "RightLegLine", "WhiteMaskHead");
            }

            if (disableThreadArmRig)
            {
                var threadArmRig = GetComponentInChildren<ThreadArmRig>(true);
                if (threadArmRig != null)
                {
                    threadArmRig.enabled = false;
                }
            }

            if (disableHeadMaskSelector)
            {
                var headMask = GetComponent<PlayerHeadMaskSelector>();
                if (headMask != null)
                {
                    headMask.enabled = false;
                }
            }
        }

        public bool ShouldShowRendererInFirstPerson(string objectName, Transform rendererTransform)
        {
            if (!hideHeadInFirstPerson || !ContainsName(objectName, firstPersonHeadNameContains))
            {
                if (ContainsName(objectName, firstPersonArmNameContains))
                {
                    return true;
                }

                var current = rendererTransform != null ? rendererTransform : null;
                while (current != null)
                {
                    if (ContainsName(current.name, firstPersonArmNameContains))
                    {
                        return true;
                    }

                    current = current.parent;
                }
            }

            return false;
        }

        public bool ShouldHideRendererInFirstPerson(string objectName, Transform rendererTransform)
        {
            if (!hideHeadInFirstPerson)
            {
                return false;
            }

            if (ContainsName(objectName, firstPersonHeadNameContains))
            {
                return true;
            }

            if (ContainsName(objectName, firstPersonLegNameContains))
            {
                return true;
            }

            var current = rendererTransform != null ? rendererTransform.parent : null;
            while (current != null)
            {
                if (ContainsName(current.name, firstPersonHeadNameContains) ||
                    ContainsName(current.name, firstPersonLegNameContains))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static bool ContainsName(string value, string token)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.IsNullOrWhiteSpace(token) &&
                   value.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void HideNamedObjects(Transform root, params string[] names)
        {
            if (root == null || names == null || names.Length == 0)
            {
                return;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var current = all[i];
                if (current == null)
                {
                    continue;
                }

                for (var n = 0; n < names.Length; n++)
                {
                    if (string.Equals(current.name, names[n], System.StringComparison.Ordinal))
                    {
                        current.gameObject.SetActive(false);
                        break;
                    }
                }
            }
        }
    }
}
