#if UNITY_EDITOR
using ShooterPrototype.Player;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    public static class Day5PlayerPrefabCreator
    {
        private const string PrefabFolderPath = "Assets/Prefabs/Player";
        private const string PrefabAssetPath = PrefabFolderPath + "/PlayerLocal.prefab";
        private const string Ak47PrefabPath = "Assets/Prefabs/AK-47/rifle_001.prefab";
        private const string BlackMaterialPath = PrefabFolderPath + "/Generated_BlackLine.mat";
        private const string WhiteMaterialPath = PrefabFolderPath + "/Generated_WhiteMask.mat";

        [MenuItem("Shooter Prototype/Create/Day5 Player Prefab")]
        public static void CreateOrUpdatePlayerPrefab()
        {
            GameObject root = null;
            try
            {
                EnsureFolders();
                EnsureWeaponAimPoint();

                root = new GameObject("PlayerLocal");
                root.layer = LayerMask.NameToLayer("Default");

                var characterController = root.AddComponent<CharacterController>();
                characterController.height = 1.8f;
                characterController.radius = 0.35f;
                characterController.center = new Vector3(0f, 0.9f, 0f);
                characterController.stepOffset = 0.3f;
                characterController.slopeLimit = 45f;

                var cameraPivot = new GameObject("CameraPivot").transform;
                cameraPivot.SetParent(root.transform, false);
                cameraPivot.localPosition = new Vector3(0f, 1.4f, 0f);

                var cameraObject = new GameObject("PlayerCamera");
                cameraObject.transform.SetParent(cameraPivot, false);
                cameraObject.transform.localPosition = Vector3.zero;
                cameraObject.transform.localRotation = Quaternion.identity;
                var camera = cameraObject.AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.nearClipPlane = 0.03f;

                var thirdPersonRoot = CreateThirdPersonBody(root.transform, out var weaponAnchor);

                var fpsController = root.AddComponent<FpsCharacterController>();
                var fpsSerialized = new SerializedObject(fpsController);
                fpsSerialized.FindProperty("cameraPivot").objectReferenceValue = cameraPivot;
                fpsSerialized.FindProperty("playerCamera").objectReferenceValue = camera;
                fpsSerialized.ApplyModifiedPropertiesWithoutUndo();

                var viewPresentation = root.AddComponent<PlayerViewPresentation>();
                var viewSerialized = new SerializedObject(viewPresentation);
                viewSerialized.FindProperty("firstPersonRoot").objectReferenceValue = null;
                viewSerialized.FindProperty("thirdPersonRoot").objectReferenceValue = thirdPersonRoot;
                viewSerialized.FindProperty("isLocalPlayer").boolValue = true;
                viewSerialized.FindProperty("useSingleModelForFirstPerson").boolValue = true;
                viewSerialized.FindProperty("armNameContains").stringValue = "Thread";
                viewSerialized.FindProperty("weaponNameContains").stringValue = "Weapon";
                viewSerialized.ApplyModifiedPropertiesWithoutUndo();

                var weaponMount = root.AddComponent<PlayerWeaponMount>();
                var weaponMountSerialized = new SerializedObject(weaponMount);
                weaponMountSerialized.FindProperty("weaponPrefab").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<GameObject>(Ak47PrefabPath);
                weaponMountSerialized.FindProperty("weaponParent").objectReferenceValue = weaponAnchor;
                weaponMountSerialized.FindProperty("cameraPivot").objectReferenceValue = cameraPivot;
                weaponMountSerialized.FindProperty("localPosition").vector3Value = Vector3.zero;
                weaponMountSerialized.FindProperty("localEulerAngles").vector3Value = Vector3.zero;
                weaponMountSerialized.FindProperty("localScale").vector3Value = new Vector3(0.35476f, 0.35476f, 0.35476f);
                weaponMountSerialized.FindProperty("hipAnchorLocalPosition").vector3Value = new Vector3(0.21f, 1.2f, 0.365f);
                weaponMountSerialized.FindProperty("hipAnchorLocalEuler").vector3Value = new Vector3(-0.752f, -3.043f, 4.539f);
                weaponMountSerialized.FindProperty("adsAnchorLocalPosition").vector3Value = new Vector3(0f, 1.26f, 0f);
                weaponMountSerialized.FindProperty("adsAnchorLocalEuler").vector3Value = new Vector3(0f, -1f, 0f);
                weaponMountSerialized.FindProperty("adsUseSightLock").boolValue = true;
                weaponMountSerialized.FindProperty("sightTargetName").stringValue = "AimPoint";
                weaponMountSerialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabAssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(PrefabAssetPath, ImportAssetOptions.ForceUpdate);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabAssetPath);
                if (prefab == null)
                {
                    Debug.LogError($"[Day5PlayerPrefabCreator] Failed to create player prefab at path: {PrefabAssetPath}");
                    return;
                }

                EditorGUIUtility.PingObject(prefab);
                Selection.activeObject = prefab;
                Debug.Log($"[Day5PlayerPrefabCreator] Player prefab created: {PrefabAssetPath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Day5PlayerPrefabCreator] Exception while creating prefab: {ex.Message}");
            }
            finally
            {
                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }

            if (!AssetDatabase.IsValidFolder(PrefabFolderPath))
            {
                AssetDatabase.CreateFolder("Assets/Prefabs", "Player");
            }
        }

        private static void EnsureWeaponAimPoint()
        {
            var weaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Ak47PrefabPath);
            if (weaponPrefab == null)
            {
                Debug.LogWarning($"[Day5PlayerPrefabCreator] Weapon prefab not found at {Ak47PrefabPath}");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(Ak47PrefabPath);
            if (root == null)
            {
                return;
            }

            try
            {
                var existing = FindChildByName(root.transform, "AimPoint");
                if (existing != null)
                {
                    return;
                }

                var aimPoint = new GameObject("AimPoint").transform;
                aimPoint.SetParent(root.transform, false);
                aimPoint.localPosition = new Vector3(0f, 0.02f, 0.42f);
                aimPoint.localRotation = Quaternion.identity;
                aimPoint.localScale = Vector3.one;
                PrefabUtility.SaveAsPrefabAsset(root, Ak47PrefabPath);
                Debug.Log("[Day5PlayerPrefabCreator] Added AimPoint to AK-47 prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static GameObject CreateThirdPersonBody(Transform parent, out Transform weaponAnchor)
        {
            var root = new GameObject("ThirdPersonBody");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.SetActive(false);
            weaponAnchor = null;
            var blackMaterial = GetOrCreateMaterial(BlackMaterialPath, Color.black);
            var whiteMaterial = GetOrCreateMaterial(WhiteMaterialPath, Color.white);

            var torsoBottom = new Vector3(0f, 0.76f, 0f);
            var torsoTop = new Vector3(0.02f, 1.34f, 0.015f);
            var sharedShoulder = new Vector3(0.02f, 1.26f, 0.015f);
            var leftFoot = new Vector3(-0.11f, 0.08f, 0f);
            var rightFoot = new Vector3(0.11f, 0.08f, 0f);
            var neckTop = new Vector3(0.035f, 1.56f, 0.04f);

            CreateCurvedLineSegment(
                root.transform,
                "TorsoLine",
                torsoBottom,
                new Vector3(0.03f, 1.08f, 0.045f),
                torsoTop,
                0.045f,
                blackMaterial,
                Color.black,
                10);
            CreateCurvedLineSegment(
                root.transform,
                "NeckLine",
                torsoTop,
                new Vector3(0.03f, 1.44f, 0.035f),
                neckTop,
                0.045f,
                blackMaterial,
                Color.black,
                8);
            var leftLegLine = CreateLineSegment(root.transform, "LeftLegLine", torsoBottom, leftFoot, 0.042f, blackMaterial, Color.black);
            var rightLegLine = CreateLineSegment(root.transform, "RightLegLine", torsoBottom, rightFoot, 0.042f, blackMaterial, Color.black);

            var shoulderAnchor = CreateAnchor(root.transform, "ShoulderAnchor", sharedShoulder);
            var hipAnchor = CreateAnchor(root.transform, "HipAnchor", torsoBottom);
            weaponAnchor = CreateAnchor(root.transform, "WeaponAnchor", new Vector3(0.21f, 1.2f, 0.365f));
            weaponAnchor.localRotation = Quaternion.Euler(-0.752f, -3.043f, 4.539f);
            var leftHandTarget = CreateAnchor(root.transform, "LeftHandTarget", new Vector3(-0.48f, 1.03f, 0.48f));
            var rightHandTarget = CreateAnchor(root.transform, "RightHandTarget", new Vector3(0.48f, 1.03f, 0.48f));
            var leftFootTarget = CreateAnchor(root.transform, "LeftFootTarget", leftFoot);
            var rightFootTarget = CreateAnchor(root.transform, "RightFootTarget", rightFoot);

            var armRig = root.AddComponent<ThreadArmRig>();
            armRig.Configure(shoulderAnchor, shoulderAnchor, leftHandTarget, rightHandTarget);

            var locomotionRig = root.AddComponent<ProceduralLocomotionRig>();
            var locomotionSerialized = new SerializedObject(locomotionRig);
            locomotionSerialized.FindProperty("fpsController").objectReferenceValue = root.GetComponentInParent<FpsCharacterController>();
            locomotionSerialized.FindProperty("rootTransform").objectReferenceValue = root.transform;
            locomotionSerialized.FindProperty("shoulderAnchor").objectReferenceValue = shoulderAnchor;
            locomotionSerialized.FindProperty("hipAnchor").objectReferenceValue = hipAnchor;
            locomotionSerialized.FindProperty("leftHandTarget").objectReferenceValue = leftHandTarget;
            locomotionSerialized.FindProperty("rightHandTarget").objectReferenceValue = rightHandTarget;
            locomotionSerialized.FindProperty("leftFootTarget").objectReferenceValue = leftFootTarget;
            locomotionSerialized.FindProperty("rightFootTarget").objectReferenceValue = rightFootTarget;
            locomotionSerialized.FindProperty("leftLegLine").objectReferenceValue = leftLegLine;
            locomotionSerialized.FindProperty("rightLegLine").objectReferenceValue = rightLegLine;
            locomotionSerialized.ApplyModifiedPropertiesWithoutUndo();

            var headMask = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            headMask.name = "WhiteMaskHead";
            headMask.transform.SetParent(root.transform, false);
            headMask.transform.localPosition = new Vector3(0f, 1.64f, 0f);
            headMask.transform.localScale = new Vector3(0.22f, 0.3f, 0.09f);
            SetRendererMaterial(headMask, whiteMaterial, Color.white);
            RemoveCollider(headMask);

            return root;
        }

        private static Transform CreateAnchor(Transform parent, string name, Vector3 localPosition)
        {
            var anchor = new GameObject(name).transform;
            anchor.SetParent(parent, false);
            anchor.localPosition = localPosition;
            anchor.localRotation = Quaternion.identity;
            return anchor;
        }

        private static Transform FindChildByName(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName))
            {
                return null;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var tr = all[i];
                if (tr != null && string.Equals(tr.name, targetName, System.StringComparison.Ordinal))
                {
                    return tr;
                }
            }

            return null;
        }

        private static LineRenderer CreateLineSegment(
            Transform parent,
            string name,
            Vector3 start,
            Vector3 end,
            float width,
            Material sharedMaterial,
            Color color)
        {
            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(parent, false);

            var line = lineObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCornerVertices = 8;
            line.numCapVertices = 8;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            line.startWidth = Mathf.Max(0.001f, width);
            line.endWidth = Mathf.Max(0.001f, width);
            line.startColor = color;
            line.endColor = color;
            line.SetPosition(0, start);
            line.SetPosition(1, end);

            if (sharedMaterial != null)
            {
                line.sharedMaterial = sharedMaterial;
            }

            return line;
        }

        private static LineRenderer CreateCurvedLineSegment(
            Transform parent,
            string name,
            Vector3 start,
            Vector3 control,
            Vector3 end,
            float width,
            Material sharedMaterial,
            Color color,
            int segments)
        {
            var line = CreateLineSegment(parent, name, start, end, width, sharedMaterial, color);
            var steps = Mathf.Max(2, segments);
            line.positionCount = steps + 1;

            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var p0 = Vector3.Lerp(start, control, t);
                var p1 = Vector3.Lerp(control, end, t);
                line.SetPosition(i, Vector3.Lerp(p0, p1, t));
            }

            return line;
        }

        private static void SetRendererMaterial(GameObject target, Material sharedMaterial, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            if (sharedMaterial != null)
            {
                renderer.sharedMaterial = sharedMaterial;
                return;
            }

            var fallback = FindSupportedShader();
            if (fallback == null)
            {
                return;
            }

            var material = new Material(fallback);
            ApplyMaterialColor(material, color);
            renderer.sharedMaterial = material;
        }

        private static Material GetOrCreateMaterial(string assetPath, Color color)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null)
            {
                return existing;
            }

            EnsureAssetFolder(Path.GetDirectoryName(assetPath));
            var shader = FindSupportedShader();
            if (shader == null)
            {
                return null;
            }

            var created = new Material(shader);
            ApplyMaterialColor(created, color);
            AssetDatabase.CreateAsset(created, assetPath);
            return created;
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            var normalized = folderPath.Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            var parent = Path.GetDirectoryName(normalized)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureAssetFolder(parent);
            }

            if (string.IsNullOrWhiteSpace(parent))
            {
                return;
            }

            var folderName = Path.GetFileName(normalized);
            if (!string.IsNullOrWhiteSpace(folderName))
            {
                AssetDatabase.CreateFolder(parent, folderName);
            }
        }

        private static Shader FindSupportedShader()
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                return shader;
            }

            return Shader.Find("Standard");
        }

        private static void ApplyMaterialColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static void RemoveCollider(GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }
        }
    }
}
#endif
