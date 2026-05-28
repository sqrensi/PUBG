using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    [DefaultExecutionOrder(250)]
    public sealed class SyntyFirstPersonArmsPresenter : MonoBehaviour
    {
        [SerializeField] private Transform syntyVisualRoot;
        [SerializeField] private SkinnedMeshRenderer sourceRenderer;
        [SerializeField] private string primaryCharacterMeshName;
        [SerializeField] private FirstPersonArmsCoverage armsCoverage = FirstPersonArmsCoverage.ArmsWithoutShoulders;
        [SerializeField] private float minArmBoneWeight = 0.35f;

        private readonly List<SkinnedMeshRenderer> firstPersonArmsRenderers = new List<SkinnedMeshRenderer>();
        private readonly List<SkinnedMeshRenderer> sourceRenderers = new List<SkinnedMeshRenderer>();
        private readonly List<Mesh> generatedMeshes = new List<Mesh>();
        private bool built;

        public bool HasFirstPersonArms => firstPersonArmsRenderers.Count > 0;

        public void Configure(Transform visualRoot, float armBoneWeightThreshold)
        {
            Configure(visualRoot, armBoneWeightThreshold, primaryCharacterMeshName);
        }

        public void Configure(Transform visualRoot, float armBoneWeightThreshold, string characterMeshName)
        {
            Configure(visualRoot, armBoneWeightThreshold, characterMeshName, armsCoverage);
        }

        public void Configure(
            Transform visualRoot,
            float armBoneWeightThreshold,
            string characterMeshName,
            FirstPersonArmsCoverage coverage)
        {
            syntyVisualRoot = visualRoot;
            minArmBoneWeight = Mathf.Clamp01(armBoneWeightThreshold);
            armsCoverage = coverage;
            if (!string.IsNullOrWhiteSpace(characterMeshName))
            {
                primaryCharacterMeshName = characterMeshName;
            }

            ResetForRebuild();
            BuildIfNeeded();
        }

        public void ResetForRebuild()
        {
            DestroyGeneratedArmObjects();
            for (var i = 0; i < generatedMeshes.Count; i++)
            {
                if (generatedMeshes[i] != null)
                {
                    DestroyObject(generatedMeshes[i]);
                }
            }

            generatedMeshes.Clear();
            firstPersonArmsRenderers.Clear();
            sourceRenderers.Clear();
            sourceRenderer = null;
            built = false;
        }

        private void DestroyGeneratedArmObjects()
        {
            for (var i = firstPersonArmsRenderers.Count - 1; i >= 0; i--)
            {
                var armsRenderer = firstPersonArmsRenderers[i];
                if (armsRenderer == null)
                {
                    continue;
                }

                DestroyObject(armsRenderer.gameObject);
            }

            var thirdPersonBody = transform.Find("ThirdPersonBody");
            if (thirdPersonBody != null)
            {
                var generatedRoot = thirdPersonBody.Find("GeneratedFirstPersonArms");
                if (generatedRoot != null)
                {
                    DestroyObject(generatedRoot.gameObject);
                }
            }

            var cameraPivot = transform.Find("CameraPivot");
            var firstPersonView = cameraPivot != null ? cameraPivot.Find("FirstPersonView") : null;
            if (firstPersonView != null)
            {
                var generatedRoot = firstPersonView.Find("GeneratedFirstPersonArms");
                if (generatedRoot != null)
                {
                    DestroyObject(generatedRoot.gameObject);
                }
            }

            var searchRoot = syntyVisualRoot != null ? syntyVisualRoot.root : transform;
            var transforms = searchRoot.GetComponentsInChildren<Transform>(true);
            for (var i = transforms.Length - 1; i >= 0; i--)
            {
                var current = transforms[i];
                if (current == null ||
                    current.name.IndexOf("_FirstPersonArms", System.StringComparison.Ordinal) < 0)
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

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(target);
                return;
            }
#endif
            Object.Destroy(target);
        }

        public bool IsSourceBodyRenderer(Renderer renderer)
        {
            return renderer is SkinnedMeshRenderer skinned && sourceRenderers.Contains(skinned);
        }

        public bool IsFirstPersonArmsRenderer(Renderer renderer)
        {
            return renderer is SkinnedMeshRenderer skinned && firstPersonArmsRenderers.Contains(skinned);
        }

        public void ApplyFirstPersonVisibility(bool localFirstPersonView)
        {
            BuildIfNeeded();

            for (var i = 0; i < sourceRenderers.Count; i++)
            {
                var source = sourceRenderers[i];
                if (source == null)
                {
                    continue;
                }

                source.enabled = !localFirstPersonView || !HasFirstPersonArms;
            }

            for (var i = 0; i < firstPersonArmsRenderers.Count; i++)
            {
                var arms = firstPersonArmsRenderers[i];
                if (arms == null)
                {
                    continue;
                }

                arms.enabled = localFirstPersonView;
            }
        }

        private void Awake()
        {
            BuildIfNeeded();
        }

        private void OnDestroy()
        {
            for (var i = 0; i < generatedMeshes.Count; i++)
            {
                if (generatedMeshes[i] != null)
                {
                    Destroy(generatedMeshes[i]);
                }
            }
        }

        private void BuildIfNeeded()
        {
            if (built)
            {
                return;
            }

            built = true;
            firstPersonArmsRenderers.Clear();
            sourceRenderers.Clear();

            if (syntyVisualRoot == null)
            {
                return;
            }

            var preferredSourceName = ResolvePreferredSourceMeshName();
            ActivatePrimaryCharacterByName(preferredSourceName);

            var primarySource = FindPrimaryCharacterSource(preferredSourceName);
            if (primarySource == null)
            {
                return;
            }

            RemoveStaleArmsRenderers(primarySource.gameObject.name);

            if (TryAdoptExistingArmsRenderer(primarySource))
            {
                return;
            }

            var armsMesh = SyntyFirstPersonArmsMeshBuilder.ExtractArmsMesh(primarySource, minArmBoneWeight, armsCoverage);
            if (armsMesh == null)
            {
                return;
            }

            generatedMeshes.Add(armsMesh);
            sourceRenderers.Add(primarySource);

            var armsRenderer = GetOrCreateArmsRenderer(primarySource.gameObject.name);
            armsRenderer.sharedMesh = armsMesh;
            armsRenderer.sharedMaterials = primarySource.sharedMaterials;
            armsRenderer.bones = primarySource.bones;
            armsRenderer.rootBone = primarySource.rootBone;
            armsRenderer.updateWhenOffscreen = true;
            armsRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            armsRenderer.enabled = false;
            if (!firstPersonArmsRenderers.Contains(armsRenderer))
            {
                firstPersonArmsRenderers.Add(armsRenderer);
            }

            if (sourceRenderer == null)
            {
                sourceRenderer = primarySource;
            }
        }

        private SkinnedMeshRenderer FindPrimaryCharacterSource(string preferredSourceName)
        {
            if (syntyVisualRoot == null)
            {
                return null;
            }

            var skinnedMeshes = syntyVisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (!string.IsNullOrEmpty(preferredSourceName))
            {
                for (var i = 0; i < skinnedMeshes.Length; i++)
                {
                    var source = skinnedMeshes[i];
                    if (!IsCharacterBodyRenderer(source))
                    {
                        continue;
                    }

                    if (string.Equals(source.gameObject.name, preferredSourceName, System.StringComparison.Ordinal))
                    {
                        return source;
                    }
                }

                return null;
            }

            SkinnedMeshRenderer bestFallback = null;
            var bestFallbackScore = int.MinValue;
            for (var i = 0; i < skinnedMeshes.Length; i++)
            {
                var source = skinnedMeshes[i];
                if (!IsCharacterBodyRenderer(source))
                {
                    continue;
                }

                var score = 0;
                if (source.gameObject.activeInHierarchy)
                {
                    score += 1000;
                }

                if (source.enabled)
                {
                    score += 500;
                }

                score += source.sharedMesh != null ? source.sharedMesh.vertexCount : 0;
                if (score > bestFallbackScore)
                {
                    bestFallbackScore = score;
                    bestFallback = source;
                }
            }

            return bestFallback;
        }

        private string ResolvePreferredSourceMeshName()
        {
            if (!string.IsNullOrWhiteSpace(primaryCharacterMeshName))
            {
                return primaryCharacterMeshName;
            }

            if (sourceRenderer != null && IsCharacterBodyRenderer(sourceRenderer))
            {
                return sourceRenderer.gameObject.name;
            }

            return string.Empty;
        }

        private void ActivatePrimaryCharacterByName(string characterMeshName)
        {
            if (string.IsNullOrWhiteSpace(characterMeshName) || syntyVisualRoot == null)
            {
                return;
            }

            var skinnedMeshes = syntyVisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (var i = 0; i < skinnedMeshes.Length; i++)
            {
                var source = skinnedMeshes[i];
                if (!IsCharacterBodyRenderer(source))
                {
                    continue;
                }

                var isPrimary = string.Equals(source.gameObject.name, characterMeshName, System.StringComparison.Ordinal);
                source.gameObject.SetActive(isPrimary);
                if (isPrimary)
                {
                    source.enabled = true;
                }
            }
        }

        private void RemoveStaleArmsRenderers(string expectedSourceMeshName)
        {
            var expectedArmsName = ResolveArmsObjectName(expectedSourceMeshName, armsCoverage);
            var armsContainer = GetOrCreateArmsContainer();
            for (var i = armsContainer.childCount - 1; i >= 0; i--)
            {
                var child = armsContainer.GetChild(i);
                if (child == null || string.Equals(child.name, expectedArmsName, System.StringComparison.Ordinal))
                {
                    continue;
                }

                DestroyObject(child.gameObject);
            }
        }

        private static bool IsFirstPersonArmsObjectName(string objectName)
        {
            return !string.IsNullOrEmpty(objectName) &&
                   objectName.IndexOf("_FirstPersonArms", System.StringComparison.Ordinal) >= 0;
        }

        public static string ResolveArmsObjectName(string sourceMeshName, FirstPersonArmsCoverage coverage)
        {
            if (string.IsNullOrWhiteSpace(sourceMeshName))
            {
                return "FirstPersonArms";
            }

            switch (coverage)
            {
                case FirstPersonArmsCoverage.ArmsWithoutShoulders:
                    return sourceMeshName + "_FirstPersonArms_NoShoulders";
                case FirstPersonArmsCoverage.HandsOnly:
                    return sourceMeshName + "_FirstPersonArms_Hands";
                case FirstPersonArmsCoverage.FullArms:
                    return sourceMeshName + "_FirstPersonArms";
                default:
                    return sourceMeshName + "_FirstPersonArms_Forearms";
            }
        }

        private static bool IsCharacterBodyRenderer(SkinnedMeshRenderer source)
        {
            if (source == null || source.sharedMesh == null)
            {
                return false;
            }

            var objectName = source.gameObject.name;
            if (IsFirstPersonArmsObjectName(objectName))
            {
                return false;
            }

            if (objectName.StartsWith("SM_Char_Attach", System.StringComparison.Ordinal))
            {
                return false;
            }

            if (objectName.StartsWith("Character_", System.StringComparison.Ordinal))
            {
                return true;
            }

            return objectName.StartsWith("Ch", System.StringComparison.Ordinal);
        }

        private Transform GetOrCreateArmsContainer()
        {
            var preferredParent = ResolveFirstPersonArmsParent();
            var existing = preferredParent.Find("GeneratedFirstPersonArms");
            if (existing != null)
            {
                return existing;
            }

            var container = new GameObject("GeneratedFirstPersonArms");
            container.transform.SetParent(preferredParent, false);
            container.transform.localPosition = Vector3.zero;
            container.transform.localRotation = Quaternion.identity;
            container.transform.localScale = Vector3.one;
            return container.transform;
        }

        private Transform ResolveFirstPersonArmsParent()
        {
            var viewPresentation = GetComponent<PlayerViewPresentation>();
            if (viewPresentation != null &&
                !viewPresentation.UsesSingleModelForFirstPerson &&
                viewPresentation.FirstPersonViewRoot != null)
            {
                return viewPresentation.FirstPersonViewRoot.transform;
            }

            var cameraPivot = transform.Find("CameraPivot");
            if (cameraPivot != null)
            {
                var firstPersonView = cameraPivot.Find("FirstPersonView");
                if (firstPersonView != null)
                {
                    return firstPersonView;
                }
            }

            var thirdPersonBody = transform.Find("ThirdPersonBody");
            return thirdPersonBody != null ? thirdPersonBody : transform;
        }

        private bool TryAdoptExistingArmsRenderer(SkinnedMeshRenderer primarySource)
        {
            if (primarySource == null)
            {
                return false;
            }

            var expectedArmsName = ResolveArmsObjectName(primarySource.gameObject.name, armsCoverage);
            var armsContainer = GetOrCreateArmsContainer();
            var existingTransform = armsContainer.Find(expectedArmsName);
            if (existingTransform == null ||
                !existingTransform.TryGetComponent(out SkinnedMeshRenderer existingRenderer) ||
                existingRenderer.sharedMesh == null)
            {
                return false;
            }

            sourceRenderers.Add(primarySource);
            if (!firstPersonArmsRenderers.Contains(existingRenderer))
            {
                firstPersonArmsRenderers.Add(existingRenderer);
            }

            if (sourceRenderer == null)
            {
                sourceRenderer = primarySource;
            }

            return true;
        }

        private SkinnedMeshRenderer GetOrCreateArmsRenderer(string sourceMeshName)
        {
            var armsName = ResolveArmsObjectName(sourceMeshName, armsCoverage);
            var armsContainer = GetOrCreateArmsContainer();
            var existingTransform = armsContainer.Find(armsName);
            if (existingTransform != null &&
                existingTransform.TryGetComponent(out SkinnedMeshRenderer existingRenderer))
            {
                return existingRenderer;
            }

            var armsObject = new GameObject(armsName);
            armsObject.transform.SetParent(armsContainer, false);
            armsObject.transform.localPosition = Vector3.zero;
            armsObject.transform.localRotation = Quaternion.identity;
            armsObject.transform.localScale = Vector3.one;
            return armsObject.AddComponent<SkinnedMeshRenderer>();
        }
    }
}
