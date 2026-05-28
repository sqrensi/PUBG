#if UNITY_EDITOR
using ShooterPrototype.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShooterPrototype.EditorTools
{
    public static class WeaponBlockSetup
    {
        private const string LayerName = WeaponBlockLayers.LayerName;

        [MenuItem("Shooter Prototype/Setup/Ensure WeaponBlock Layer")]
        public static void EnsureWeaponBlockLayer()
        {
            EnsureLayerExists(LayerName, 8);
            Debug.Log($"[WeaponBlockSetup] Layer '{LayerName}' is ready.");
        }

        [MenuItem("Shooter Prototype/Setup/Apply WeaponBlock Layer To Primitive Colliders")]
        public static void ApplyWeaponBlocksToPrimitiveColliders()
        {
            EnsureWeaponBlockLayer();
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogWarning("[WeaponBlockSetup] No active scene.");
                return;
            }

            WeaponBlockUtility.ResetProcessedScenesForTests();
            var updated = WeaponBlockUtility.EnsureSceneWeaponBlocks(scene);
            if (updated > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            Debug.Log($"[WeaponBlockSetup] Applied WeaponBlock layer to {updated} primitive collider(s) in '{scene.name}'.");
        }

        [MenuItem("Shooter Prototype/Setup/Remove All WeaponBlock Proxies From Scene")]
        public static void RemoveAllWeaponBlockProxiesFromScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogWarning("[WeaponBlockSetup] No active scene.");
                return;
            }

            var removed = WeaponBlockUtility.RemoveAllWeaponBlockProxiesFromScene(scene);
            if (removed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            Debug.Log($"[WeaponBlockSetup] Removed {removed} WeaponBlockProxy object(s) from '{scene.name}'.");
        }

        private static void EnsureLayerExists(string layerName, int preferredUserLayer)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");

            var existingIndex = -1;
            for (var i = 8; i <= 31; i++)
            {
                var layerProperty = layers.GetArrayElementAtIndex(i);
                if (layerProperty.stringValue == layerName)
                {
                    existingIndex = i;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                return;
            }

            var targetIndex = preferredUserLayer;
            if (!string.IsNullOrEmpty(layers.GetArrayElementAtIndex(preferredUserLayer).stringValue))
            {
                targetIndex = -1;
                for (var i = 8; i <= 31; i++)
                {
                    if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                    {
                        targetIndex = i;
                        break;
                    }
                }
            }

            if (targetIndex < 0)
            {
                Debug.LogError("[WeaponBlockSetup] No free user layer slot for WeaponBlock.");
                return;
            }

            layers.GetArrayElementAtIndex(targetIndex).stringValue = layerName;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
