using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class PlayerHitboxCleanup
    {
        private const string LegacyHitboxRootName = "HitboxesRoot";

        public static void RemoveLegacyLineHitboxes(GameObject playerRoot)
        {
            if (playerRoot == null)
            {
                return;
            }

            DestroyNamedObjects(playerRoot.transform, LegacyHitboxRootName);
            DestroyLegacyNamedHitboxes(playerRoot.transform);
        }

        private static void DestroyLegacyNamedHitboxes(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = all.Length - 1; i >= 0; i--)
            {
                var child = all[i];
                if (child == null || child == root)
                {
                    continue;
                }

                var name = child.name;
                if (string.IsNullOrWhiteSpace(name) ||
                    !name.EndsWith("Hitbox", System.StringComparison.Ordinal) ||
                    name.StartsWith("BoneHitbox_", System.StringComparison.Ordinal))
                {
                    continue;
                }

                DestroyObject(child.gameObject);
            }
        }

        private static void DestroyNamedObjects(Transform root, string objectName)
        {
            if (root == null || string.IsNullOrWhiteSpace(objectName))
            {
                return;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = all.Length - 1; i >= 0; i--)
            {
                var current = all[i];
                if (current == null ||
                    !string.Equals(current.name, objectName, System.StringComparison.Ordinal))
                {
                    continue;
                }

                DestroyObject(current.gameObject);
            }
        }

        private static void DestroyObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
