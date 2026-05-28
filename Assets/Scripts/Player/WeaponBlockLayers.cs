using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class WeaponBlockLayers
    {
        public const string LayerName = "WeaponBlock";

        public static int LayerIndex => LayerMask.NameToLayer(LayerName);

        public static int Mask
        {
            get
            {
                var layer = LayerIndex;
                return layer >= 0 ? 1 << layer : 0;
            }
        }

        public static bool IsConfigured => LayerIndex >= 0;
    }
}
