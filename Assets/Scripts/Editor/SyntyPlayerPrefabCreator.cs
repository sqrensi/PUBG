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
        private const string SourcePrefabPath = "Assets/Prefabs/Player/PlayerLocal.prefab";
        private const string TargetPrefabPath = "Assets/Prefabs/Player/PlayerSyntyHuman.prefab";
        private const string SyntyCharactersFolder = "Assets/Synty/PolygonBattleRoyale/Prefabs/Characters";
        private const string DefaultSyntyCharacterPath =
            "Assets/Synty/PolygonBattleRoyale/Prefabs/Characters/Character_MilitaryMale_01.prefab";

        [MenuItem("Shooter Prototype/Create/Synty Human Player Prefab")]
        public static void CreateOrUpdatePlayerPrefab()
        {
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (sourcePrefab == null)
            {
                Debug.LogError($"[SyntyPlayerPrefabCreator] Source prefab not found: {SourcePrefabPath}");
                return;
            }

            var syntyCharacterPrefab = ResolveSyntyCharacterPrefab();
            if (syntyCharacterPrefab == null)
            {
                Debug.LogError(
                    $"[SyntyPlayerPrefabCreator] No Synty character prefab found under {SyntyCharactersFolder}");
                return;
            }

            var instance = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
            if (instance == null)
            {
                Debug.LogError("[SyntyPlayerPrefabCreator] Failed to instantiate source prefab.");
                return;
            }

            try
            {
                instance.name = "PlayerSyntyHuman";
                ConfigureHumanVisual(instance, syntyCharacterPrefab);
                SaveTargetPrefab(instance);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
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
            ActivatePrimaryCharacterVariant(syntyInstance);

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

            var viewPresentation = playerRoot.GetComponent<PlayerViewPresentation>();
            if (viewPresentation != null)
            {
                var viewSerialized = new SerializedObject(viewPresentation);
                viewSerialized.FindProperty("armNameContains").stringValue = "Thread";
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
        }

        private static GameObject ResolveSyntyCharacterPrefab()
        {
            var defaultPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultSyntyCharacterPath);
            if (defaultPrefab != null)
            {
                return defaultPrefab;
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

        private static void ActivatePrimaryCharacterVariant(GameObject syntyRoot)
        {
            var skinnedMeshes = syntyRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinnedMeshes.Length == 0)
            {
                return;
            }

            var primary = skinnedMeshes
                .OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.vertexCount : 0)
                .First();
            var primaryRoot = primary.transform;
            while (primaryRoot.parent != null && primaryRoot.parent != syntyRoot.transform)
            {
                primaryRoot = primaryRoot.parent;
            }

            foreach (var skinnedMesh in skinnedMeshes)
            {
                var branchRoot = skinnedMesh.transform;
                while (branchRoot.parent != null && branchRoot.parent != syntyRoot.transform)
                {
                    branchRoot = branchRoot.parent;
                }

                var shouldEnable = branchRoot == primaryRoot;
                branchRoot.gameObject.SetActive(shouldEnable);
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

            var targetHeight = 1.72f;
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

        private static void SaveTargetPrefab(GameObject instance)
        {
            EnsureFolder("Assets/Prefabs/Player");

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(TargetPrefabPath);
            if (existing != null)
            {
                PrefabUtility.SaveAsPrefabAsset(instance, TargetPrefabPath);
            }
            else
            {
                PrefabUtility.SaveAsPrefabAsset(instance, TargetPrefabPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(TargetPrefabPath, ImportAssetOptions.ForceUpdate);

            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(TargetPrefabPath);
            if (saved != null)
            {
                EditorGUIUtility.PingObject(saved);
                Selection.activeObject = saved;
            }

            Debug.Log($"[SyntyPlayerPrefabCreator] Saved prefab: {TargetPrefabPath}");
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
