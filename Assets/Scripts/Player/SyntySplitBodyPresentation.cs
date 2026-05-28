using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Keeps third-person bones/animator alive for remotes while local FP shows weapon-driven arms only.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public sealed class SyntySplitBodyPresentation : MonoBehaviour
    {
        [SerializeField] private GameObject firstPersonViewRoot;
        [SerializeField] private GameObject thirdPersonBodyRoot;

        private PlayerViewPresentation viewPresentation;
        private SyntyCharacterVisualBinder syntyBinder;
        private SyntyFirstPersonArmsPresenter armsPresenter;

        public void Configure(GameObject firstPersonView, GameObject thirdPersonBody)
        {
            firstPersonViewRoot = firstPersonView;
            thirdPersonBodyRoot = thirdPersonBody;
            ApplyViewMode();
        }

        private void Awake()
        {
            viewPresentation = GetComponent<PlayerViewPresentation>();
            syntyBinder = GetComponent<SyntyCharacterVisualBinder>();
            armsPresenter = GetComponent<SyntyFirstPersonArmsPresenter>();
        }

        private void OnEnable()
        {
            ApplyViewMode();
        }

        public void ApplyViewMode()
        {
            if (viewPresentation == null)
            {
                viewPresentation = GetComponent<PlayerViewPresentation>();
            }

            if (viewPresentation == null || viewPresentation.UsesSingleModelForFirstPerson)
            {
                return;
            }

            var isLocal = viewPresentation.IsLocalPlayerView;

            if (firstPersonViewRoot != null)
            {
                firstPersonViewRoot.SetActive(isLocal);
            }

            if (thirdPersonBodyRoot != null)
            {
                thirdPersonBodyRoot.SetActive(true);
            }

            ApplyThirdPersonRendererVisibility(isLocal);
            armsPresenter?.ApplyFirstPersonVisibility(isLocal);
        }

        private void ApplyThirdPersonRendererVisibility(bool localFirstPerson)
        {
            if (thirdPersonBodyRoot == null)
            {
                return;
            }

            if (syntyBinder == null)
            {
                syntyBinder = GetComponent<SyntyCharacterVisualBinder>();
            }

            if (armsPresenter == null)
            {
                armsPresenter = GetComponent<SyntyFirstPersonArmsPresenter>();
            }

            var renderers = thirdPersonBodyRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (!localFirstPerson)
                {
                    if (IsFirstPersonOnlyRenderer(renderer))
                    {
                        renderer.enabled = false;
                        continue;
                    }

                    renderer.enabled = true;
                    continue;
                }

                if (renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                {
                    renderer.enabled = true;
                    continue;
                }

                if (IsFirstPersonOnlyRenderer(renderer))
                {
                    renderer.enabled = false;
                    continue;
                }

                if (syntyBinder != null && syntyBinder.IsRendererUnderSyntyVisual(renderer.transform))
                {
                    renderer.enabled = false;
                    continue;
                }

                if (IsLegacyProceduralLine(renderer.gameObject.name))
                {
                    renderer.enabled = false;
                }
            }
        }

        private bool IsFirstPersonOnlyRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return false;
            }

            if (firstPersonViewRoot != null &&
                renderer.transform.IsChildOf(firstPersonViewRoot.transform))
            {
                return true;
            }

            return renderer.gameObject.name.IndexOf("_FirstPersonArms", System.StringComparison.Ordinal) >= 0 ||
                   (armsPresenter != null && armsPresenter.IsFirstPersonArmsRenderer(renderer));
        }

        private static bool IsLegacyProceduralLine(string objectName)
        {
            return string.Equals(objectName, "TorsoLine", System.StringComparison.Ordinal) ||
                   string.Equals(objectName, "NeckLine", System.StringComparison.Ordinal) ||
                   string.Equals(objectName, "LeftLegLine", System.StringComparison.Ordinal) ||
                   string.Equals(objectName, "RightLegLine", System.StringComparison.Ordinal) ||
                   string.Equals(objectName, "LeftThread", System.StringComparison.Ordinal) ||
                   string.Equals(objectName, "RightThread", System.StringComparison.Ordinal);
        }
    }
}
