using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerHeadMaskSelector : MonoBehaviour
    {
        private const string MasksFolderPath = "Assets/Prefabs/tensentmask/Masks";

        [Header("Head Mount")]
        [SerializeField] private Transform headCenter;
        [SerializeField] private string headCenterName = "HeadCenter";

        [Header("Spawn")]
        [SerializeField] private bool applyOnEnable = true;

        [Header("Mask Prefabs")]
        [SerializeField] private List<GameObject> maskPrefabs = new List<GameObject>();

#if UNITY_EDITOR
        [Header("Editor")]
        [SerializeField] private bool autoPopulateMasksFromFolder = true;
#endif

        private GameObject spawnedMaskInstance;

        private void OnEnable()
        {
            if (!Application.isPlaying || !applyOnEnable)
            {
                return;
            }

            ApplyRandomMask();
        }

        public void ApplyRandomMask()
        {
            if (!TryResolveHeadCenter(out var target))
            {
                return;
            }

            var validMasks = CollectValidMasks();
            if (validMasks.Count == 0)
            {
                return;
            }

            var randomIndex = Random.Range(0, validMasks.Count);
            ApplyMaskByIndex(target, validMasks[randomIndex]);
        }

        public void ApplyMaskByKey(string key)
        {
            if (!TryResolveHeadCenter(out var target))
            {
                return;
            }

            var validMasks = CollectValidMasks();
            if (validMasks.Count == 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                ApplyMaskByIndex(target, validMasks[Random.Range(0, validMasks.Count)]);
                return;
            }

            var hash = ComputeStableHash(key.Trim());
            var index = Mathf.Abs(hash) % validMasks.Count;
            ApplyMaskByIndex(target, validMasks[index]);
        }

        private void ApplyMaskByIndex(Transform target, GameObject prefab)
        {
            if (target == null || prefab == null)
            {
                return;
            }

            if (spawnedMaskInstance != null)
            {
                Destroy(spawnedMaskInstance);
                spawnedMaskInstance = null;
            }

            spawnedMaskInstance = Instantiate(prefab, target, false);
            spawnedMaskInstance.name = prefab.name;
            spawnedMaskInstance.transform.localPosition = Vector3.zero;
            spawnedMaskInstance.transform.localRotation = Quaternion.identity;

            // Re-apply local/remote visibility rules because this mesh is spawned after Awake.
            var viewPresentation = GetComponent<PlayerViewPresentation>();
            viewPresentation?.RefreshViewMode();
        }

        private bool TryResolveHeadCenter(out Transform target)
        {
            if (headCenter != null)
            {
                target = headCenter;
                return true;
            }

            var all = GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var current = all[i];
                if (current == null)
                {
                    continue;
                }

                if (string.Equals(current.name, headCenterName, System.StringComparison.Ordinal))
                {
                    headCenter = current;
                    target = headCenter;
                    return true;
                }
            }

            target = null;
            return false;
        }

        private List<GameObject> CollectValidMasks()
        {
            var valid = new List<GameObject>();
            for (var i = 0; i < maskPrefabs.Count; i++)
            {
                var prefab = maskPrefabs[i];
                if (prefab != null)
                {
                    valid.Add(prefab);
                }
            }

            return valid;
        }

        private static int ComputeStableHash(string text)
        {
            unchecked
            {
                var hash = (int)2166136261;
                for (var i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 16777619;
                }

                return hash;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (headCenter == null)
            {
                TryResolveHeadCenter(out _);
            }

            if (autoPopulateMasksFromFolder)
            {
                PopulateMasksFromFolder();
            }
        }

        private void PopulateMasksFromFolder()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { MasksFolderPath });
            var loaded = new List<GameObject>();
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    loaded.Add(prefab);
                }
            }

            maskPrefabs = loaded;
        }
#endif
    }
}
