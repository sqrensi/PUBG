using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Applies third-person remote presentation when this prefab is spawned for other players.
    /// Local FP components stay disabled; TP body, animator and weapon mount remain active.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class RemoteThirdPersonPlayerBootstrap : MonoBehaviour
    {
        [SerializeField] private bool applyOnAwake = true;

        private void Awake()
        {
            if (applyOnAwake)
            {
                ApplyRemoteThirdPersonMode();
            }
        }

        public void ApplyRemoteThirdPersonMode()
        {
            var cameraPivot = transform.Find("CameraPivot");
            var firstPersonView = cameraPivot != null ? cameraPivot.Find("FirstPersonView") : null;
            var thirdPersonBody = transform.Find("ThirdPersonBody");

            var viewPresentation = GetComponent<PlayerViewPresentation>();
            if (viewPresentation != null && thirdPersonBody != null)
            {
                if (firstPersonView != null)
                {
                    viewPresentation.ConfigureSplitView(
                        firstPersonView.gameObject,
                        thirdPersonBody.gameObject,
                        false);
                }
                else
                {
                    viewPresentation.Configure(false);
                }
            }

            if (firstPersonView != null)
            {
                firstPersonView.gameObject.SetActive(false);
            }

            if (thirdPersonBody != null)
            {
                thirdPersonBody.gameObject.SetActive(true);
            }

            var fpsController = GetComponent<FpsCharacterController>();
            if (fpsController != null)
            {
                fpsController.enabled = false;
            }

            var handBinder = GetComponent<SyntyWeaponHandBinder>();
            if (handBinder != null)
            {
                handBinder.enabled = false;
                handBinder.SetHandIkEnabled(false);
            }

            var handAttachedMount = GetComponent<SyntyHandAttachedWeaponMount>();
            if (handAttachedMount != null)
            {
                handAttachedMount.enabled = false;
            }

            var armGate = GetComponent<SyntyFirstPersonArmLocomotionGate>();
            if (armGate != null)
            {
                armGate.SetDisableSkeletonAnimatorInFirstPerson(false);
                armGate.SetSuppressArmLocomotionInFirstPerson(false);
            }

            var headMask = GetComponent<PlayerHeadMaskSelector>();
            if (headMask != null)
            {
                headMask.enabled = false;
            }

            var sync = GetComponent<MatchPresenceSync>();
            if (sync != null)
            {
                sync.enabled = false;
            }

            var localMarker = GetComponent<LocalPlayerMarker>();
            if (localMarker != null)
            {
                localMarker.enabled = false;
            }

            EnableThirdPersonAnimator(thirdPersonBody);
            WireThirdPersonWeaponMount(thirdPersonBody, firstPersonView, cameraPivot);

            var locomotionRig = thirdPersonBody != null
                ? thirdPersonBody.GetComponent<ProceduralLocomotionRig>()
                : null;
            if (locomotionRig != null)
            {
                locomotionRig.SetProceduralVisualsEnabled(false);
            }

            var splitBody = GetComponent<SyntySplitBodyPresentation>();
            splitBody?.ApplyViewMode();

            var armsPresenter = GetComponent<SyntyFirstPersonArmsPresenter>();
            armsPresenter?.ApplyFirstPersonVisibility(false);

            DisableLocalCamerasAndListeners();
        }

        private static void EnableThirdPersonAnimator(Transform thirdPersonBody)
        {
            if (thirdPersonBody == null)
            {
                return;
            }

            var syntyVisual = thirdPersonBody.Find("SyntyVisual");
            if (syntyVisual == null)
            {
                return;
            }

            var animator = syntyVisual.GetComponent<Animator>();
            if (animator != null)
            {
                animator.enabled = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            var renderers = syntyVisual.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (renderer.gameObject.name.EndsWith("_FirstPersonArms", System.StringComparison.Ordinal))
                {
                    renderer.enabled = false;
                    continue;
                }

                renderer.enabled = true;
            }
        }

        private void WireThirdPersonWeaponMount(
            Transform thirdPersonBody,
            Transform firstPersonView,
            Transform cameraPivot)
        {
            if (thirdPersonBody == null)
            {
                return;
            }

            var weaponMount = GetComponent<PlayerWeaponMount>();
            if (weaponMount == null)
            {
                return;
            }

            var tpWeaponAnchor = ResolveThirdPersonWeaponAnchor(thirdPersonBody, firstPersonView);
            if (tpWeaponAnchor == null)
            {
                return;
            }

            tpWeaponAnchor.gameObject.SetActive(true);

            var fpWeaponAnchor = firstPersonView != null ? firstPersonView.Find("WeaponAnchor") : null;
            if (fpWeaponAnchor != null && fpWeaponAnchor != tpWeaponAnchor)
            {
                fpWeaponAnchor.gameObject.SetActive(false);
            }

            weaponMount.SetHandAttachedWeaponActive(false);
            weaponMount.ConfigureWeaponParent(tpWeaponAnchor, cameraPivot);
        }

        private static Transform ResolveThirdPersonWeaponAnchor(
            Transform thirdPersonBody,
            Transform firstPersonView)
        {
            var tpAnchor = thirdPersonBody.Find("WeaponAnchor");
            if (tpAnchor != null)
            {
                return tpAnchor;
            }

            var fpAnchor = firstPersonView != null ? firstPersonView.Find("WeaponAnchor") : null;
            if (fpAnchor == null)
            {
                return null;
            }

            var duplicate = new GameObject("WeaponAnchor");
            var anchor = duplicate.transform;
            anchor.SetParent(thirdPersonBody, false);
            anchor.position = fpAnchor.position;
            anchor.rotation = fpAnchor.rotation;
            anchor.localScale = fpAnchor.localScale;
            return anchor;
        }

        private void DisableLocalCamerasAndListeners()
        {
            var cameras = GetComponentsInChildren<Camera>(true);
            for (var i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null)
                {
                    cameras[i].enabled = false;
                }
            }

            var listeners = GetComponentsInChildren<AudioListener>(true);
            for (var i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null)
                {
                    listeners[i].enabled = false;
                }
            }
        }
    }
}
