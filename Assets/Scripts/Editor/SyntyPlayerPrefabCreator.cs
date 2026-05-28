#if UNITY_EDITOR
using ShooterPrototype.Player;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    public static class SyntyPlayerPrefabCreator
    {
        private const string SourcePrefabPath = PlayerCleanPrefabCreator.TargetPrefabPath;
        private const string LegacySourcePrefabPath = "Assets/Prefabs/Player/PlayerLocal.prefab";
        private const string SettingsTemplatePrefabPath = "Assets/Prefabs/Player/PlayerSyntyHuman.prefab";
        private const string TargetPrefabPath = "Assets/Prefabs/Player/PlayerSyntyHuman.prefab";
        private const string BusinessMaleTargetPrefabPath = "Assets/Prefabs/Player/PlayerSyntyBusinessMale.prefab";
        private const string Ch18CharacterFbxPath = "Assets/Characters/Ch18_nonPBR.fbx";
        private const string Ch18TargetPrefabPath = "Assets/Prefabs/Player/PlayerCh18.prefab";
        internal const string Ch18CharacterFbxAssetPath = Ch18CharacterFbxPath;
        private const string SyntyCharactersFolder = "Assets/Synty/PolygonBattleRoyale/Prefabs/Characters";
        private const string DefaultSyntyCharacterPath =
            "Assets/Synty/PolygonBattleRoyale/Prefabs/Characters/Character_BusinessMale_01.prefab";
        private const float TargetCharacterHeightMeters = 1.58f;

        [MenuItem("Shooter Prototype/Create/Synty Human Player Prefab")]
        public static void CreateOrUpdatePlayerPrefab()
        {
            CreatePlayerPrefabFromTemplate(
                ResolveTemplatePrefabPath(TargetPrefabPath),
                TargetPrefabPath,
                ResolveSyntyCharacterPrefab(DefaultSyntyCharacterPath),
                "PlayerSyntyHuman");
        }

        [MenuItem("Shooter Prototype/Create/Player Synty Business Male Prefab")]
        public static void CreateBusinessMalePlayerPrefab()
        {
            var characterPrefab = ResolveSyntyCharacterPrefab(DefaultSyntyCharacterPath);
            if (characterPrefab == null)
            {
                Debug.LogError(
                    $"[SyntyPlayerPrefabCreator] Character prefab not found: {DefaultSyntyCharacterPath}");
                return;
            }

            CreatePlayerPrefabFromTemplate(
                ResolveTemplatePrefabPath(SettingsTemplatePrefabPath),
                BusinessMaleTargetPrefabPath,
                characterPrefab,
                "PlayerSyntyBusinessMale");
        }

        [MenuItem("Shooter Prototype/Create/Player Ch18 Prefab (Business Male settings)")]
        public static void CreateCh18PlayerPrefab()
        {
            CreatePlayerPrefabFromFbx(
                ResolveTemplatePrefabPath(BusinessMaleTargetPrefabPath),
                Ch18TargetPrefabPath,
                Ch18CharacterFbxPath,
                "PlayerCh18");
        }

        public static void CreatePlayerPrefabFromFbx(
            GameObject templatePrefab,
            string targetPrefabPath,
            string characterFbxPath,
            string instanceName)
        {
            if (templatePrefab == null)
            {
                Debug.LogError("[SyntyPlayerPrefabCreator] Template prefab is missing.");
                return;
            }

            var characterModel = AssetDatabase.LoadAssetAtPath<GameObject>(characterFbxPath);
            if (characterModel == null)
            {
                Debug.LogError($"[SyntyPlayerPrefabCreator] Character FBX not found: {characterFbxPath}");
                return;
            }

            var instance = PrefabUtility.InstantiatePrefab(templatePrefab) as GameObject;
            if (instance == null)
            {
                Debug.LogError("[SyntyPlayerPrefabCreator] Failed to instantiate template prefab.");
                return;
            }

            try
            {
                instance.name = instanceName;
                CleanupPlayerGeneratedContent(instance);
                ConfigureFbxCharacterVisual(instance, characterModel, characterFbxPath);
                SavePrefab(instance, targetPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        public static void CreatePlayerPrefabFromTemplate(
            GameObject templatePrefab,
            string targetPrefabPath,
            GameObject syntyCharacterPrefab,
            string instanceName)
        {
            if (templatePrefab == null)
            {
                Debug.LogError("[SyntyPlayerPrefabCreator] Template prefab is missing.");
                return;
            }

            if (syntyCharacterPrefab == null)
            {
                Debug.LogError("[SyntyPlayerPrefabCreator] Synty character prefab is missing.");
                return;
            }

            var instance = PrefabUtility.InstantiatePrefab(templatePrefab) as GameObject;
            if (instance == null)
            {
                Debug.LogError("[SyntyPlayerPrefabCreator] Failed to instantiate template prefab.");
                return;
            }

            try
            {
                instance.name = instanceName;
                CleanupPlayerGeneratedContent(instance);
                ConfigureHumanVisual(instance, syntyCharacterPrefab);
                SavePrefab(instance, targetPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static GameObject ResolveTemplatePrefabPath(string preferredTemplatePath)
        {
            var preferred = AssetDatabase.LoadAssetAtPath<GameObject>(preferredTemplatePath);
            if (preferred != null)
            {
                return preferred;
            }

            var clean = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (clean != null)
            {
                return clean;
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(LegacySourcePrefabPath);
        }

        internal static void EnsurePlayerPrefabFolder()
        {
            EnsureFolder("Assets/Prefabs/Player");
        }

        private static void ConfigureHumanVisual(GameObject playerRoot, GameObject syntyCharacterPrefab)
        {
            var thirdPersonBody = FindChildRecursive(playerRoot.transform, "ThirdPersonBody");
            if (thirdPersonBody == null)
            {
                Debug.LogError("[SyntyPlayerPrefabCreator] ThirdPersonBody not found on source prefab.");
                return;
            }

            var existingVisual = thirdPersonBody.Find("SyntyVisual");
            if (existingVisual != null)
            {
                Object.DestroyImmediate(existingVisual.gameObject);
            }

            CleanupGeneratedFirstPersonArms(thirdPersonBody);

            var armsPresenter = playerRoot.GetComponent<SyntyFirstPersonArmsPresenter>();
            if (armsPresenter != null)
            {
                armsPresenter.ResetForRebuild();
            }

            var syntyInstance = PrefabUtility.InstantiatePrefab(syntyCharacterPrefab, thirdPersonBody) as GameObject;
            if (syntyInstance == null)
            {
                Debug.LogError("[SyntyPlayerPrefabCreator] Failed to instantiate Synty character.");
                return;
            }

            syntyInstance.name = "SyntyVisual";
            syntyInstance.transform.localPosition = Vector3.zero;
            syntyInstance.transform.localRotation = Quaternion.identity;
            syntyInstance.transform.localScale = Vector3.one;
            ActivatePrimaryCharacterVariant(syntyInstance, syntyCharacterPrefab.name);

            var binder = playerRoot.GetComponent<SyntyCharacterVisualBinder>();
            if (binder == null)
            {
                binder = playerRoot.AddComponent<SyntyCharacterVisualBinder>();
            }

            var alignment = ComputeAlignment(syntyInstance.transform);
            binder.Configure(
                syntyInstance.transform,
                alignment.positionOffset,
                alignment.eulerOffset,
                alignment.uniformScale);

            var binderSerialized = new SerializedObject(binder);
            binderSerialized.FindProperty("showSyntyMeshInFirstPerson").boolValue = false;
            binderSerialized.FindProperty("showArmsOnlyInFirstPerson").boolValue = false;
            binderSerialized.ApplyModifiedPropertiesWithoutUndo();
            binder.ApplyMecanimMode();

            SyntyAnimationSetup.WireMecanimComponents(playerRoot);

            var viewPresentation = playerRoot.GetComponent<PlayerViewPresentation>();
            if (viewPresentation != null)
            {
                var viewSerialized = new SerializedObject(viewPresentation);
                viewSerialized.FindProperty("armNameContains").stringValue = "Hand";
                viewSerialized.FindProperty("weaponNameContains").stringValue = "Weapon";
                viewSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var headMask = playerRoot.GetComponent<PlayerHeadMaskSelector>();
            if (headMask != null)
            {
                headMask.enabled = false;
            }

            Debug.Log(
                $"[SyntyPlayerPrefabCreator] Configured {playerRoot.name} with Synty prefab '{syntyCharacterPrefab.name}'.");
            RestoreActiveCharacterMeshesForPrefabSave(playerRoot);
        }

        public static void ConfigureFbxCharacterVisual(
            GameObject playerRoot,
            GameObject characterModel,
            string characterFbxPath)
        {
            var thirdPersonBody = FindChildRecursive(playerRoot.transform, "ThirdPersonBody");
            if (thirdPersonBody == null)
            {
                Debug.LogError("[SyntyPlayerPrefabCreator] ThirdPersonBody not found on source prefab.");
                return;
            }

            var existingVisual = thirdPersonBody.Find("SyntyVisual");
            if (existingVisual != null)
            {
                Object.DestroyImmediate(existingVisual.gameObject);
            }

            CleanupGeneratedFirstPersonArms(thirdPersonBody);

            var armsPresenter = playerRoot.GetComponent<SyntyFirstPersonArmsPresenter>();
            if (armsPresenter != null)
            {
                armsPresenter.ResetForRebuild();
            }

            var characterInstance = PrefabUtility.InstantiatePrefab(characterModel, thirdPersonBody) as GameObject;
            if (characterInstance == null)
            {
                characterInstance = Object.Instantiate(characterModel, thirdPersonBody);
            }

            if (characterInstance == null)
            {
                Debug.LogError("[SyntyPlayerPrefabCreator] Failed to instantiate character FBX.");
                return;
            }

            characterInstance.name = "SyntyVisual";
            characterInstance.transform.localPosition = Vector3.zero;
            characterInstance.transform.localRotation = Quaternion.identity;
            characterInstance.transform.localScale = Vector3.one;

            var primaryMeshName = ResolvePrimaryCharacterMeshName(characterInstance.transform, characterFbxPath);
            ActivatePrimaryCharacterVariant(characterInstance, primaryMeshName);

            var binder = playerRoot.GetComponent<SyntyCharacterVisualBinder>();
            if (binder == null)
            {
                binder = playerRoot.AddComponent<SyntyCharacterVisualBinder>();
            }

            var alignment = ComputeAlignment(characterInstance.transform);
            binder.Configure(
                characterInstance.transform,
                alignment.positionOffset,
                alignment.eulerOffset,
                alignment.uniformScale);

            var binderSerialized = new SerializedObject(binder);
            binderSerialized.FindProperty("showSyntyMeshInFirstPerson").boolValue = false;
            binderSerialized.FindProperty("showArmsOnlyInFirstPerson").boolValue = false;
            binderSerialized.ApplyModifiedPropertiesWithoutUndo();
            binder.ApplyMecanimMode();

            SyntyAnimationSetup.WireMecanimComponents(playerRoot);

            var viewPresentation = playerRoot.GetComponent<PlayerViewPresentation>();
            if (viewPresentation != null)
            {
                var viewSerialized = new SerializedObject(viewPresentation);
                viewSerialized.FindProperty("armNameContains").stringValue = "Hand";
                viewSerialized.FindProperty("weaponNameContains").stringValue = "Weapon";
                viewSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var headMask = playerRoot.GetComponent<PlayerHeadMaskSelector>();
            if (headMask != null)
            {
                headMask.enabled = false;
            }

            Debug.Log(
                $"[SyntyPlayerPrefabCreator] Configured {playerRoot.name} with FBX '{characterFbxPath}' (mesh '{primaryMeshName}').");
            RestoreActiveCharacterMeshesForPrefabSave(playerRoot);
        }

        internal static void RestoreActiveCharacterMeshesForPrefabSave(GameObject playerRoot)
        {
            var thirdPersonBody = FindChildRecursive(playerRoot.transform, "ThirdPersonBody");
            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (syntyVisual == null)
            {
                return;
            }

            ActivatePrimaryCharacterVariant(
                syntyVisual.gameObject,
                ResolvePrimaryCharacterMeshName(syntyVisual));
        }

        public static void CleanupPlayerGeneratedContent(GameObject playerRoot)
        {
            if (playerRoot == null)
            {
                return;
            }

            var thirdPersonBody = FindChildRecursive(playerRoot.transform, "ThirdPersonBody");
            CleanupGeneratedFirstPersonArms(thirdPersonBody);

            var armsPresenter = playerRoot.GetComponent<SyntyFirstPersonArmsPresenter>();
            if (armsPresenter != null)
            {
                armsPresenter.ResetForRebuild();
            }
        }

        public static void CleanupGeneratedFirstPersonArms(Transform thirdPersonBody)
        {
            if (thirdPersonBody == null)
            {
                return;
            }

            CleanupGeneratedFirstPersonArmsUnder(thirdPersonBody);

            var playerRoot = thirdPersonBody.root;
            var cameraPivot = playerRoot != null ? playerRoot.Find("CameraPivot") : null;
            var firstPersonView = cameraPivot != null ? cameraPivot.Find("FirstPersonView") : null;
            if (firstPersonView != null)
            {
                CleanupGeneratedFirstPersonArmsUnder(firstPersonView);
            }
        }

        private static void CleanupGeneratedFirstPersonArmsUnder(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var generatedRoot = root.Find("GeneratedFirstPersonArms");
            if (generatedRoot != null)
            {
                Object.DestroyImmediate(generatedRoot.gameObject);
            }

            var children = root.GetComponentsInChildren<Transform>(true);
            for (var i = children.Length - 1; i >= 0; i--)
            {
                var child = children[i];
                if (child == null || child == root)
                {
                    continue;
                }

                if (child.name.EndsWith("_FirstPersonArms", System.StringComparison.Ordinal))
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        private static GameObject ResolveSyntyCharacterPrefab(string preferredCharacterPath)
        {
            var preferred = AssetDatabase.LoadAssetAtPath<GameObject>(preferredCharacterPath);
            if (preferred != null)
            {
                return preferred;
            }

            if (!AssetDatabase.IsValidFolder(SyntyCharactersFolder))
            {
                return null;
            }

            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { SyntyCharactersFolder });
            var candidates = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                    path.StartsWith(SyntyCharactersFolder) &&
                    !path.Contains("/Attachments/") &&
                    Path.GetFileName(path).StartsWith("Character_"))
                .OrderBy(path => path)
                .ToArray();

            if (candidates.Length == 0)
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(candidates[0]);
        }

        internal static string ResolvePrimaryCharacterMeshName(Transform visualRoot, string sourceAssetPath = null)
        {
            var syntyName = ResolveSyntyCharacterName(visualRoot);
            if (!string.IsNullOrEmpty(syntyName))
            {
                return syntyName;
            }

            if (!string.IsNullOrEmpty(sourceAssetPath))
            {
                var fileName = Path.GetFileNameWithoutExtension(sourceAssetPath);
                var byFileName = FindSkinnedMeshByObjectName(visualRoot, fileName);
                if (byFileName != null)
                {
                    return byFileName.gameObject.name;
                }

                var shortName = fileName.Split('_')[0];
                if (!string.Equals(shortName, fileName, System.StringComparison.Ordinal))
                {
                    byFileName = FindSkinnedMeshByObjectName(visualRoot, shortName);
                    if (byFileName != null)
                    {
                        return byFileName.gameObject.name;
                    }
                }
            }

            var best = FindBestBodySkinnedMesh(visualRoot);
            return best != null ? best.gameObject.name : string.Empty;
        }

        internal static string ResolveSyntyCharacterName(Transform syntyVisual)
        {
            if (syntyVisual == null)
            {
                return string.Empty;
            }

            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(syntyVisual.gameObject);
            if (!string.IsNullOrEmpty(prefabPath))
            {
                var fileName = Path.GetFileNameWithoutExtension(prefabPath);
                if (fileName.StartsWith("Character_", System.StringComparison.Ordinal))
                {
                    return fileName;
                }
            }

            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(syntyVisual.gameObject);
            if (source != null)
            {
                var sourceName = source.name;
                if (sourceName.StartsWith("Character_", System.StringComparison.Ordinal))
                {
                    return sourceName;
                }
            }

            return string.Empty;
        }

        internal static void ActivatePrimaryCharacterVariant(GameObject syntyRoot, string characterPrefabName)
        {
            var skinnedMeshes = syntyRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinnedMeshes.Length == 0)
            {
                return;
            }

            var characterKey = string.IsNullOrWhiteSpace(characterPrefabName)
                ? string.Empty
                : characterPrefabName.Replace(".prefab", string.Empty);

            foreach (var skinnedMesh in skinnedMeshes)
            {
                if (skinnedMesh == null)
                {
                    continue;
                }

                var meshObject = skinnedMesh.gameObject;
                var meshObjectName = meshObject.name;
                if (meshObjectName.EndsWith("_FirstPersonArms", System.StringComparison.Ordinal))
                {
                    meshObject.SetActive(false);
                    continue;
                }

                if (meshObjectName.StartsWith("SM_Char_Attach", System.StringComparison.Ordinal))
                {
                    meshObject.SetActive(false);
                    continue;
                }

                if (!meshObjectName.StartsWith("Character_", System.StringComparison.Ordinal) &&
                    !meshObjectName.StartsWith("Ch", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var shouldEnable = string.IsNullOrEmpty(characterKey) ||
                                   string.Equals(meshObjectName, characterKey, System.StringComparison.Ordinal) ||
                                   (!characterKey.StartsWith("Character_", System.StringComparison.Ordinal) &&
                                    meshObjectName.StartsWith(characterKey, System.StringComparison.Ordinal));
                meshObject.SetActive(shouldEnable);
                if (shouldEnable)
                {
                    skinnedMesh.enabled = true;
                }
            }
        }

        private static (Vector3 positionOffset, Vector3 eulerOffset, float uniformScale) ComputeAlignment(
            Transform syntyRoot)
        {
            var renderers = syntyRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return (Vector3.zero, Vector3.zero, 1f);
            }

            var minLocal = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var maxLocal = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (var i = 0; i < renderers.Length; i++)
            {
                EncapsulateLocalBounds(renderers[i].bounds, syntyRoot, ref minLocal, ref maxLocal);
            }

            var targetHeight = TargetCharacterHeightMeters;
            var currentHeight = Mathf.Max(0.01f, maxLocal.y - minLocal.y);
            var scale = targetHeight / currentHeight;
            var feetOffsetY = -minLocal.y * scale;

            return (new Vector3(0f, feetOffsetY, 0f), Vector3.zero, scale);
        }

        private static void EncapsulateLocalBounds(
            Bounds worldBounds,
            Transform root,
            ref Vector3 minLocal,
            ref Vector3 maxLocal)
        {
            var center = worldBounds.center;
            var extents = worldBounds.extents;
            var corners = new[]
            {
                center + new Vector3(extents.x, extents.y, extents.z),
                center + new Vector3(extents.x, extents.y, -extents.z),
                center + new Vector3(extents.x, -extents.y, extents.z),
                center + new Vector3(extents.x, -extents.y, -extents.z),
                center + new Vector3(-extents.x, extents.y, extents.z),
                center + new Vector3(-extents.x, extents.y, -extents.z),
                center + new Vector3(-extents.x, -extents.y, extents.z),
                center + new Vector3(-extents.x, -extents.y, -extents.z)
            };

            for (var i = 0; i < corners.Length; i++)
            {
                var local = root.InverseTransformPoint(corners[i]);
                minLocal = Vector3.Min(minLocal, local);
                maxLocal = Vector3.Max(maxLocal, local);
            }
        }

        private static void SavePrefab(GameObject instance, string targetPrefabPath)
        {
            EnsureFolder("Assets/Prefabs/Player");
            PrefabUtility.SaveAsPrefabAsset(instance, targetPrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(targetPrefabPath, ImportAssetOptions.ForceUpdate);

            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(targetPrefabPath);
            if (saved != null)
            {
                EditorGUIUtility.PingObject(saved);
                Selection.activeObject = saved;
            }

            Debug.Log($"[SyntyPlayerPrefabCreator] Saved prefab: {targetPrefabPath}");
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            var parts = folderPath.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static SkinnedMeshRenderer FindSkinnedMeshByObjectName(Transform visualRoot, string objectName)
        {
            if (visualRoot == null || string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            var skinnedMeshes = visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (var i = 0; i < skinnedMeshes.Length; i++)
            {
                var skinnedMesh = skinnedMeshes[i];
                if (skinnedMesh != null &&
                    string.Equals(skinnedMesh.gameObject.name, objectName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return skinnedMesh;
                }
            }

            return null;
        }

        private static SkinnedMeshRenderer FindBestBodySkinnedMesh(Transform visualRoot)
        {
            if (visualRoot == null)
            {
                return null;
            }

            var skinnedMeshes = visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer best = null;
            var bestScore = int.MinValue;
            for (var i = 0; i < skinnedMeshes.Length; i++)
            {
                var skinnedMesh = skinnedMeshes[i];
                if (skinnedMesh == null ||
                    skinnedMesh.sharedMesh == null ||
                    skinnedMesh.gameObject.name.EndsWith("_FirstPersonArms", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var objectName = skinnedMesh.gameObject.name;
                if (objectName.StartsWith("SM_Char_Attach", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var score = skinnedMesh.sharedMesh.vertexCount;
                if (objectName.StartsWith("Ch", System.StringComparison.Ordinal) ||
                    objectName.StartsWith("Character_", System.StringComparison.Ordinal))
                {
                    score += 100000;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = skinnedMesh;
                }
            }

            return best;
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            if (string.Equals(root.name, childName, System.StringComparison.Ordinal))
            {
                return root;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var current = all[i];
                if (current != null && string.Equals(current.name, childName, System.StringComparison.Ordinal))
                {
                    return current;
                }
            }

            return null;
        }
    }
}
#endif
