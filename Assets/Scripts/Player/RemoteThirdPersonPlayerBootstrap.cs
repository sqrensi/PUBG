using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Applies third-person remote presentation when this prefab is spawned for other players.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class RemoteThirdPersonPlayerBootstrap : MonoBehaviour
    {
        [SerializeField] private bool applyOnAwake = true;
        [SerializeField] private RuntimeAnimatorController remoteAnimatorController;

        private void Awake()
        {
            if (applyOnAwake)
            {
                ApplyRemoteThirdPersonMode();
            }
        }

        public void RefreshRemoteWeaponPresentation()
        {
            ApplyRemoteThirdPersonMode();
        }

        public void ApplyRemoteThirdPersonMode()
        {
            var thirdPersonBody = transform.Find("ThirdPersonBody");
            if (thirdPersonBody != null)
            {
                thirdPersonBody.gameObject.SetActive(true);
            }

            EnableThirdPersonAnimator(thirdPersonBody);
            WireRemoteWeapon(thirdPersonBody);
            WireRemoteLookPitchPosture(thirdPersonBody);
            EnsureBoneHitboxes(thirdPersonBody);
            EnsureRemoteShotEffects();
        }

        private void WireRemoteWeapon(Transform thirdPersonBody)
        {
            if (thirdPersonBody == null)
            {
                return;
            }

            var presentation = GetComponent<RemoteWeaponPresentation>();
            if (presentation == null)
            {
                presentation = gameObject.AddComponent<RemoteWeaponPresentation>();
            }

            presentation.Configure(thirdPersonBody);
            presentation.EnsureAttached();
        }

        private void EnableThirdPersonAnimator(Transform thirdPersonBody)
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
                if (remoteAnimatorController != null)
                {
                    animator.runtimeAnimatorController = remoteAnimatorController;
                }

                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                if (animator.layerCount > 0)
                {
                    for (var layer = 0; layer < animator.layerCount; layer++)
                    {
                        animator.SetLayerWeight(layer, 1f);
                    }
                }
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

        public void SetRemoteAnimatorController(RuntimeAnimatorController controller)
        {
            remoteAnimatorController = controller;
        }

        private void EnsureBoneHitboxes(Transform thirdPersonBody)
        {
            PlayerHitboxCleanup.RemoveLegacyLineHitboxes(gameObject);

            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (syntyVisual == null)
            {
                return;
            }

            var boneRig = GetComponent<PlayerBoneHitboxRig>();
            if (boneRig == null)
            {
                boneRig = gameObject.AddComponent<PlayerBoneHitboxRig>();
            }

            boneRig.Configure(syntyVisual);
            boneRig.BuildOrRefreshHitboxes();
        }

        private void EnsureRemoteShotEffects()
        {
            if (GetComponent<RemotePlayerShotEffects>() == null)
            {
                gameObject.AddComponent<RemotePlayerShotEffects>();
            }
        }

        private void WireRemoteLookPitchPosture(Transform thirdPersonBody)
        {
            if (thirdPersonBody == null)
            {
                return;
            }

            var locomotionRig = thirdPersonBody.GetComponent<ProceduralLocomotionRig>();
            if (locomotionRig == null)
            {
                return;
            }

            var pitchPosture = GetComponent<RemoteLookPitchPosture>();
            if (pitchPosture == null)
            {
                pitchPosture = gameObject.AddComponent<RemoteLookPitchPosture>();
            }

            pitchPosture.Configure(thirdPersonBody, locomotionRig);
        }
    }
}
