using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Local first-person split view only needs weapon-driven FP arms.
    /// Disables the Synty skeleton Animator so locomotion clips do not move hidden bones
    /// or the shared arm chain. Third-person / remote avatars keep animation enabled.
    /// </summary>
    [DefaultExecutionOrder(305)]
    public sealed class SyntyFirstPersonArmLocomotionGate : MonoBehaviour
    {
        public const int ArmsLocomotionLayerIndex = 1;

        [SerializeField] private Animator animator;
        [SerializeField] private bool suppressArmLocomotionInFirstPerson = true;
        [SerializeField] private bool disableSkeletonAnimatorInFirstPerson = true;
        [SerializeField] private float thirdPersonArmsLayerWeight = 1f;
        [SerializeField] private float firstPersonArmsLayerWeight = 0f;

        private PlayerViewPresentation viewPresentation;
        private SyntyFirstPersonArmsPresenter armsPresenter;
        private bool storedAnimatorEnabled = true;

        public bool IsSkeletonAnimatorFrozen => ShouldFreezeSkeletonForFirstPersonView();

        public void Configure(Animator targetAnimator)
        {
            animator = targetAnimator;
            ApplyAnimatorState();
        }

        public void SetSuppressArmLocomotionInFirstPerson(bool suppress)
        {
            suppressArmLocomotionInFirstPerson = suppress;
            ApplyAnimatorState();
        }

        public void SetDisableSkeletonAnimatorInFirstPerson(bool disable)
        {
            disableSkeletonAnimatorInFirstPerson = disable;
            ApplyAnimatorState();
        }

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            if (animator != null)
            {
                storedAnimatorEnabled = animator.enabled;
            }

            viewPresentation = GetComponent<PlayerViewPresentation>();
            armsPresenter = GetComponent<SyntyFirstPersonArmsPresenter>();
        }

        private void Update()
        {
            ApplyAnimatorState();
        }

        private void LateUpdate()
        {
            ApplyAnimatorState();
        }

        private void ApplyAnimatorState()
        {
            if (animator == null)
            {
                return;
            }

            if (disableSkeletonAnimatorInFirstPerson && ShouldFreezeSkeletonForFirstPersonView())
            {
                if (animator.enabled)
                {
                    storedAnimatorEnabled = true;
                    animator.enabled = false;
                }

                return;
            }

            if (!animator.enabled && storedAnimatorEnabled)
            {
                animator.enabled = true;
            }

            ApplyArmLayerWeight();
        }

        private void ApplyArmLayerWeight()
        {
            if (!suppressArmLocomotionInFirstPerson ||
                animator == null ||
                animator.layerCount <= ArmsLocomotionLayerIndex)
            {
                return;
            }

            var targetWeight = ShouldFreezeSkeletonForFirstPersonView()
                ? firstPersonArmsLayerWeight
                : thirdPersonArmsLayerWeight;
            var currentWeight = animator.GetLayerWeight(ArmsLocomotionLayerIndex);
            if (Mathf.Abs(currentWeight - targetWeight) > 0.0001f)
            {
                animator.SetLayerWeight(ArmsLocomotionLayerIndex, targetWeight);
            }
        }

        private bool ShouldFreezeSkeletonForFirstPersonView()
        {
            if (!suppressArmLocomotionInFirstPerson)
            {
                return false;
            }

            if (viewPresentation == null)
            {
                viewPresentation = GetComponent<PlayerViewPresentation>();
            }

            if (viewPresentation != null && !viewPresentation.IsLocalPlayerView)
            {
                return false;
            }

            if (viewPresentation != null && !viewPresentation.UsesSingleModelForFirstPerson)
            {
                return true;
            }

            if (armsPresenter == null)
            {
                armsPresenter = GetComponent<SyntyFirstPersonArmsPresenter>();
            }

            return armsPresenter == null || armsPresenter.HasFirstPersonArms;
        }
    }
}
