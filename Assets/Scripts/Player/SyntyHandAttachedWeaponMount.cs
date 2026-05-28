using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Parents the weapon to the animated hand bones so it sways with locomotion clips
    /// instead of floating on the camera anchor with IK hands.
    /// </summary>
    [DefaultExecutionOrder(340)]
    public sealed class SyntyHandAttachedWeaponMount : MonoBehaviour
    {
        [Header("Mode")]
        [SerializeField] private bool attachWeaponToHandsInFirstPerson = false;

        [Header("Hand Bones")]
        [SerializeField] private Transform primaryHandBone;
        [SerializeField] private bool alignGripTargetToHand = true;

        [Header("Manual Weapon Offset On Hand")]
        [SerializeField] private bool useManualWeaponOffset;
        [SerializeField] private Vector3 weaponLocalPosition = Vector3.zero;
        [SerializeField] private Vector3 weaponLocalEulerAngles = Vector3.zero;

        private PlayerWeaponMount weaponMount;
        private SyntyWeaponHandBinder handBinder;
        private SyntyFirstPersonArmLocomotionGate armLocomotionGate;
        private PlayerViewPresentation viewPresentation;
        private SyntyFirstPersonArmsPresenter armsPresenter;

        private Transform storedWeaponParent;
        private Vector3 storedWeaponLocalPosition;
        private Quaternion storedWeaponLocalRotation;
        private Vector3 storedWeaponLocalScale;
        private bool isAttached;

        public void Configure(Transform syntyVisualRoot, PlayerWeaponMount mount)
        {
            weaponMount = mount;
            if (syntyVisualRoot != null)
            {
                primaryHandBone = primaryHandBone != null
                    ? primaryHandBone
                    : FindBone(syntyVisualRoot, "Hand_R");
            }
        }

        private void Awake()
        {
            if (weaponMount == null)
            {
                weaponMount = GetComponent<PlayerWeaponMount>();
            }

            handBinder = GetComponent<SyntyWeaponHandBinder>();
            armLocomotionGate = GetComponent<SyntyFirstPersonArmLocomotionGate>();
            viewPresentation = GetComponent<PlayerViewPresentation>();
            armsPresenter = GetComponent<SyntyFirstPersonArmsPresenter>();
        }

        private void LateUpdate()
        {
            if (weaponMount == null)
            {
                return;
            }

            var shouldAttach = ShouldAttachWeaponToHands();
            weaponMount.SetHandAttachedWeaponActive(shouldAttach);

            if (handBinder != null)
            {
                handBinder.SetHandIkEnabled(!shouldAttach);
            }

            if (armLocomotionGate != null)
            {
                armLocomotionGate.SetSuppressArmLocomotionInFirstPerson(!shouldAttach);
            }

            if (!shouldAttach)
            {
                if (isAttached)
                {
                    DetachWeaponFromHand();
                }

                return;
            }

            if (!isAttached)
            {
                AttachWeaponToHand();
            }

            MaintainWeaponOnHand();
        }

        private bool ShouldAttachWeaponToHands()
        {
            if (!attachWeaponToHandsInFirstPerson || primaryHandBone == null)
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

            if (armsPresenter == null)
            {
                armsPresenter = GetComponent<SyntyFirstPersonArmsPresenter>();
            }

            return armsPresenter == null || armsPresenter.HasFirstPersonArms;
        }

        private void AttachWeaponToHand()
        {
            var weapon = weaponMount.MountedWeaponRoot;
            if (weapon == null || primaryHandBone == null)
            {
                return;
            }

            storedWeaponParent = weapon.parent;
            storedWeaponLocalPosition = weapon.localPosition;
            storedWeaponLocalRotation = weapon.localRotation;
            storedWeaponLocalScale = weapon.localScale;

            weapon.SetParent(primaryHandBone, true);

            if (alignGripTargetToHand)
            {
                var grip = weaponMount.RightHandGripTarget;
                if (grip != null)
                {
                    weapon.position += primaryHandBone.position - grip.position;
                }
            }

            if (useManualWeaponOffset)
            {
                weapon.localPosition = weaponLocalPosition;
                weapon.localRotation = Quaternion.Euler(weaponLocalEulerAngles);
            }
            else if (!alignGripTargetToHand)
            {
                weapon.localPosition = storedWeaponLocalPosition;
                weapon.localRotation = storedWeaponLocalRotation;
            }

            isAttached = true;
        }

        private void MaintainWeaponOnHand()
        {
            var weapon = weaponMount.MountedWeaponRoot;
            if (weapon == null || primaryHandBone == null)
            {
                return;
            }

            if (weapon.parent != primaryHandBone)
            {
                weapon.SetParent(primaryHandBone, true);
            }

            if (!useManualWeaponOffset)
            {
                return;
            }

            weapon.localPosition = weaponLocalPosition;
            weapon.localRotation = Quaternion.Euler(weaponLocalEulerAngles);
        }

        private void DetachWeaponFromHand()
        {
            var weapon = weaponMount.MountedWeaponRoot;
            if (weapon == null)
            {
                isAttached = false;
                return;
            }

            var parent = storedWeaponParent != null ? storedWeaponParent : weaponMount.WeaponAnchorTransform;
            weapon.SetParent(parent, false);
            weapon.localPosition = storedWeaponLocalPosition;
            weapon.localRotation = storedWeaponLocalRotation;
            weapon.localScale = storedWeaponLocalScale;
            isAttached = false;
        }

        private static Transform FindBone(Transform root, params string[] boneNames)
        {
            if (root == null || boneNames == null || boneNames.Length == 0)
            {
                return null;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            for (var n = 0; n < boneNames.Length; n++)
            {
                var boneName = boneNames[n];
                if (string.IsNullOrWhiteSpace(boneName))
                {
                    continue;
                }

                for (var i = 0; i < all.Length; i++)
                {
                    var current = all[i];
                    if (current != null && string.Equals(current.name, boneName, System.StringComparison.Ordinal))
                    {
                        return current;
                    }
                }
            }

            return null;
        }
    }
}
