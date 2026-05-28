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
        [SerializeField] private float additionalVisualScale = 1f;

        [Header("Hide Procedural Body")]
        [SerializeField] private bool hideProceduralLines = true;
        [SerializeField] private bool disableThreadArmRig = true;
        [SerializeField] private bool disableHeadMaskSelector = true;
        [SerializeField] private bool disableProceduralLocomotionVisuals = true;

        [Header("First Person")]
        [SerializeField] private bool showSyntyMeshInFirstPerson = false;
        [SerializeField] private bool showArmsOnlyInFirstPerson = true;
        [SerializeField] private bool hideHeadInFirstPerson = true;
        [SerializeField] private string firstPersonArmNameContains = "Hand";
        [SerializeField] private string firstPersonLegNameContains = "Leg";
        [SerializeField] private string firstPersonHeadNameContains = "Head";

        private static readonly string[] FirstPersonArmBoneNames =
        {
            "Clavicle_L", "Clavicle_R",
            "Shoulder_L", "Shoulder_R",
            "Elbow_L", "Elbow_R",
            "UpperArm_L", "UpperArm_R",
            "LowerArm_L", "LowerArm_R",
            "Hand_L", "Hand_R"
        };

        private PlayerViewPresentation viewPresentation;
        private SyntyFirstPersonArmsPresenter firstPersonArmsPresenter;
        private bool configured;

        public Transform SyntyVisualRoot => syntyVisualRoot;
        public bool ShowSyntyMeshInFirstPerson => showSyntyMeshInFirstPerson;
        public bool ShowArmsOnlyInFirstPerson => showArmsOnlyInFirstPerson;

        public bool IsRendererUnderSyntyVisual(Transform rendererTransform)
        {
            if (syntyVisualRoot == null || rendererTransform == null)
            {
                return false;
            }

            return rendererTransform == syntyVisualRoot || rendererTransform.IsChildOf(syntyVisualRoot);
        }

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
            ApplyMecanimMode();
            configured = true;
        }

        public void ApplyMecanimMode()
        {
            ApplyProceduralVisibility();

            var locomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
            if (locomotionRig != null && disableProceduralLocomotionVisuals)
            {
                locomotionRig.SetProceduralVisualsEnabled(false);
            }

            var viewPresentation = GetComponent<PlayerViewPresentation>();
            if (viewPresentation != null)
            {
                viewPresentation.RefreshViewMode();
            }
        }

        private void Awake()
        {
            viewPresentation = GetComponent<PlayerViewPresentation>();
            firstPersonArmsPresenter = GetComponent<SyntyFirstPersonArmsPresenter>();
            ApplyVisualTransform();
            ApplyProceduralVisibility();
        }

        private void OnEnable()
        {
            ApplyVisualTransform();
            ApplyProceduralVisibility();
            ApplyMecanimMode();
        }

        private void ApplyVisualTransform()
        {
            if (syntyVisualRoot == null)
            {
                return;
            }

            syntyVisualRoot.localPosition = localPositionOffset;
            syntyVisualRoot.localRotation = Quaternion.Euler(localEulerOffset);
            syntyVisualRoot.localScale = Vector3.one * uniformScale * Mathf.Max(0.01f, additionalVisualScale);
        }

        private void ApplyProceduralVisibility()
        {
            if (hideProceduralLines)
            {
                HideNamedObjects(
                    transform,
                    "TorsoLine",
                    "NeckLine",
                    "LeftLegLine",
                    "RightLegLine",
                    "LeftThread",
                    "RightThread",
                    "WhiteMaskHead");
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
            if (showArmsOnlyInFirstPerson && !showSyntyMeshInFirstPerson)
            {
                if (firstPersonArmsPresenter != null &&
                    rendererTransform != null &&
                    firstPersonArmsPresenter.IsFirstPersonArmsRenderer(rendererTransform.GetComponent<Renderer>()))
                {
                    return true;
                }

                if (IsRendererUnderArmBoneHierarchy(rendererTransform))
                {
                    return true;
                }

                return ContainsName(objectName, firstPersonArmNameContains) ||
                       IsTransformUnderNameToken(rendererTransform, firstPersonArmNameContains);
            }

            if (!hideHeadInFirstPerson || !ContainsName(objectName, firstPersonHeadNameContains))
            {
                if (ContainsName(objectName, firstPersonArmNameContains))
                {
                    return true;
                }

                if (IsTransformUnderNameToken(rendererTransform, firstPersonArmNameContains))
                {
                    return true;
                }
            }

            return false;
        }

        public bool ShouldHideSourceBodyInFirstPerson(Renderer renderer)
        {
            if (!showArmsOnlyInFirstPerson || showSyntyMeshInFirstPerson)
            {
                return false;
            }

            return firstPersonArmsPresenter != null && firstPersonArmsPresenter.IsSourceBodyRenderer(renderer);
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

        private static bool IsTransformUnderNameToken(Transform rendererTransform, string token)
        {
            var current = rendererTransform;
            while (current != null)
            {
                if (ContainsName(current.name, token))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static bool IsRendererUnderArmBoneHierarchy(Transform rendererTransform)
        {
            if (rendererTransform == null)
            {
                return false;
            }

            var current = rendererTransform;
            while (current != null)
            {
                for (var i = 0; i < FirstPersonArmBoneNames.Length; i++)
                {
                    if (string.Equals(current.name, FirstPersonArmBoneNames[i], System.StringComparison.Ordinal))
                    {
                        return true;
                    }
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
