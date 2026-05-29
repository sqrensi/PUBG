#if UNITY_EDITOR
using ShooterPrototype.Player;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    public static class PlayerCleanPrefabCreator
    {
        public const string SourcePrefabPath = "Assets/Prefabs/Player/PlayerLocal.prefab";
        public const string TargetPrefabPath = "Assets/Prefabs/Player/PlayerClean.prefab";
        public const string DefaultCharacterFbxPath = "Assets/Characters/Ch18_nonPBR.fbx";

        private static readonly string[] ProceduralLineObjectNames =
        {
            "TorsoLine",
            "NeckLine",
            "LeftLegLine",
            "RightLegLine",
            "LeftThread",
            "RightThread"
        };

        private static readonly string[] ProceduralVisualObjectNames =
        {
            "WhiteMaskHead"
        };

        [MenuItem("Shooter Prototype/Setup/Setup PlayerClean Character Arms (Ch18)")]
        public static void SetupPlayerCleanCharacterArms()
        {
            SetupPlayerCleanCharacterArms(DefaultCharacterFbxPath);
        }

        public static void SetupPlayerCleanCharacterArms(string characterFbxPath)
        {
            if (!System.IO.File.Exists(characterFbxPath))
            {
                Debug.LogError($"[PlayerCleanPrefabCreator] Character FBX not found: {characterFbxPath}");
                return;
            }

            EnsureModelReadWriteEnabled(characterFbxPath);

            var prefabPath = TargetPrefabPath;
            var playerRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (playerRoot == null)
            {
                CreateCleanPlayerPrefabInternal();
                playerRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }

            if (playerRoot == null)
            {
                Debug.LogError($"[PlayerCleanPrefabCreator] Missing prefab: {prefabPath}");
                return;
            }

            var characterModel = AssetDatabase.LoadAssetAtPath<GameObject>(characterFbxPath);
            if (characterModel == null)
            {
                Debug.LogError($"[PlayerCleanPrefabCreator] Failed to load character: {characterFbxPath}");
                return;
            }

            var instance = PrefabUtility.InstantiatePrefab(playerRoot) as GameObject;
            if (instance == null)
            {
                Debug.LogError("[PlayerCleanPrefabCreator] Failed to instantiate PlayerClean.");
                return;
            }

            try
            {
                instance.name = "PlayerClean";
                StripProceduralVisuals(instance);
                SyntyPlayerPrefabCreator.CleanupPlayerGeneratedContent(instance);

                var thirdPersonBody = instance.transform.Find("ThirdPersonBody");
                if (thirdPersonBody != null)
                {
                    SyntyPlayerPrefabCreator.CleanupGeneratedFirstPersonArms(thirdPersonBody);
                }

                SyntyPlayerPrefabCreator.ConfigureFbxCharacterVisual(instance, characterModel, characterFbxPath);

                var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
                if (syntyVisual != null)
                {
                    SyntyAnimationSetup.EnsureHumanoidAvatarForVisual(syntyVisual);
                }

                SyntyAnimationSetup.WireMecanimComponents(instance, FirstPersonArmsCoverage.ArmsWithoutShoulders);
                SyntyPlayerPrefabCreator.RestoreActiveCharacterMeshesForPrefabSave(instance);
                PlayerPrefabOptimization.StripLocalFirstPersonPrefab(instance);
                SavePrefab(instance, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                "[PlayerCleanPrefabCreator] PlayerClean configured with FP arms without shoulders and weapon hand IK.");

            PlayerRemotePrefabCreator.CreatePlayerCleanRemotePrefab();
        }

        [MenuItem("Shooter Prototype/Create/Clean Player Prefab (from PlayerLocal)")]
        public static void CreateOrUpdateCleanPlayerPrefab()
        {
            CreateCleanPlayerPrefabInternal();
        }

        public static void CreateCleanPlayerPrefabInternal()
        {
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (sourcePrefab == null)
            {
                Debug.LogError($"[PlayerCleanPrefabCreator] Source prefab not found: {SourcePrefabPath}");
                return;
            }

            var instance = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
            if (instance == null)
            {
                Debug.LogError("[PlayerCleanPrefabCreator] Failed to instantiate source prefab.");
                return;
            }

            try
            {
                instance.name = "PlayerClean";
                StripProceduralVisuals(instance);
                PlayerPrefabOptimization.StripLocalFirstPersonPrefab(instance);
                StripHitDetection(instance);
                WireGameplayReferences(instance);
                SavePrefab(instance, TargetPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        public static void StripProceduralVisuals(GameObject playerRoot)
        {
            if (playerRoot == null)
            {
                return;
            }

            for (var i = 0; i < ProceduralLineObjectNames.Length; i++)
            {
                DestroyNamedObjects(playerRoot.transform, ProceduralLineObjectNames[i]);
            }

            for (var i = 0; i < ProceduralVisualObjectNames.Length; i++)
            {
                DestroyNamedObjects(playerRoot.transform, ProceduralVisualObjectNames[i]);
            }

            var lineRenderers = playerRoot.GetComponentsInChildren<LineRenderer>(true);
            for (var i = lineRenderers.Length - 1; i >= 0; i--)
            {
                var lineRenderer = lineRenderers[i];
                if (lineRenderer == null)
                {
                    continue;
                }

                Object.DestroyImmediate(lineRenderer.gameObject);
            }

            var threadArmRig = playerRoot.GetComponentInChildren<ThreadArmRig>(true);
            if (threadArmRig != null)
            {
                Object.DestroyImmediate(threadArmRig);
            }

            var locomotionRig = playerRoot.GetComponentInChildren<ProceduralLocomotionRig>(true);
            if (locomotionRig != null)
            {
                var locomotionSerialized = new SerializedObject(locomotionRig);
                locomotionSerialized.FindProperty("leftLegLine").objectReferenceValue = null;
                locomotionSerialized.FindProperty("rightLegLine").objectReferenceValue = null;
                locomotionSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var viewPresentation = playerRoot.GetComponent<PlayerViewPresentation>();
            if (viewPresentation != null)
            {
                var viewSerialized = new SerializedObject(viewPresentation);
                viewSerialized.FindProperty("armNameContains").stringValue = "Hand";
                viewSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var headMask = playerRoot.GetComponent<PlayerHeadMaskSelector>();
            if (headMask != null)
            {
                headMask.enabled = false;
            }
        }

        public static void StripHitDetection(GameObject playerRoot)
        {
            if (playerRoot == null)
            {
                return;
            }

            PlayerHitboxCleanup.RemoveLegacyLineHitboxes(playerRoot);

            var boneHitboxRig = playerRoot.GetComponent<PlayerBoneHitboxRig>();
            if (boneHitboxRig != null)
            {
                boneHitboxRig.RemoveHitboxes();
                Object.DestroyImmediate(boneHitboxRig);
            }
        }

        private static void WireGameplayReferences(GameObject playerRoot)
        {
            var fpsController = playerRoot.GetComponent<FpsCharacterController>();
            var locomotionRig = playerRoot.GetComponentInChildren<ProceduralLocomotionRig>(true);
            if (locomotionRig != null && fpsController != null)
            {
                var locomotionSerialized = new SerializedObject(locomotionRig);
                locomotionSerialized.FindProperty("fpsController").objectReferenceValue = fpsController;
                locomotionSerialized.ApplyModifiedPropertiesWithoutUndo();
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

                Object.DestroyImmediate(current.gameObject);
            }
        }

        private static void EnsureModelReadWriteEnabled(string modelAssetPath)
        {
            var importer = AssetImporter.GetAtPath(modelAssetPath) as ModelImporter;
            if (importer == null)
            {
                return;
            }

            if (importer.isReadable)
            {
                return;
            }

            importer.isReadable = true;
            importer.SaveAndReimport();
            Debug.Log($"[PlayerCleanPrefabCreator] Enabled Read/Write on {modelAssetPath}");
        }

        private static void SavePrefab(GameObject instance, string targetPrefabPath)
        {
            SyntyPlayerPrefabCreator.EnsurePlayerPrefabFolder();
            PrefabUtility.SaveAsPrefabAsset(instance, targetPrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(targetPrefabPath, ImportAssetOptions.ForceUpdate);

            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(targetPrefabPath);
            if (saved != null)
            {
                EditorGUIUtility.PingObject(saved);
                Selection.activeObject = saved;
            }

            Debug.Log($"[PlayerCleanPrefabCreator] Saved clean player prefab: {targetPrefabPath}");
        }
    }
}
#endif
