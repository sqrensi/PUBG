using UnityEngine;

namespace ShooterPrototype.Player
{
    public enum PlayerBoneHitZone
    {
        Body = 0,
        Leg = 1,
        Neck = 2,
        Head = 3
    }

    [DisallowMultipleComponent]
    public sealed class PlayerBoneHitbox : MonoBehaviour
    {
        [SerializeField] private PlayerBoneHitZone hitZone = PlayerBoneHitZone.Body;

        public PlayerBoneHitZone HitZone => hitZone;

        public void Configure(PlayerBoneHitZone zone)
        {
            hitZone = zone;
        }
    }
}
