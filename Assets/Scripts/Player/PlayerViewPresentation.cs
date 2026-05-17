using UnityEngine;

namespace ShooterPrototype.Player
{
    public sealed class PlayerViewPresentation : MonoBehaviour
    {
        [Header("View Roots")]
        [SerializeField] private GameObject firstPersonRoot;
        [SerializeField] private GameObject thirdPersonRoot;
        [SerializeField] private bool isLocalPlayer = true;

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

        private void Awake()
        {
            ApplyViewMode();
        }

        private void ApplyViewMode()
        {
            if (firstPersonRoot != null)
            {
                firstPersonRoot.SetActive(isLocalPlayer);
            }

            if (thirdPersonRoot != null)
            {
                thirdPersonRoot.SetActive(!isLocalPlayer);
            }
        }
    }
}
