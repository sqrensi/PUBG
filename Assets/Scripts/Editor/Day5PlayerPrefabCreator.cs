#if UNITY_EDITOR
using ShooterPrototype.Player;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    public static class Day5PlayerPrefabCreator
    {
        private const string PrefabFolderPath = "Assets/Prefabs/Player";
        private const string PrefabAssetPath = PrefabFolderPath + "/PlayerLocal.prefab";

        [MenuItem("Shooter Prototype/Create/Day5 Player Prefab")]
        public static void CreateOrUpdatePlayerPrefab()
        {
            GameObject root = null;
            try
            {
                EnsureFolders();

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
                cameraPivot.localPosition = new Vector3(0f, 1.6f, 0f);

                var cameraObject = new GameObject("PlayerCamera");
                cameraObject.transform.SetParent(cameraPivot, false);
                cameraObject.transform.localPosition = Vector3.zero;
                cameraObject.transform.localRotation = Quaternion.identity;
                var camera = cameraObject.AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.nearClipPlane = 0.03f;

                var firstPersonRoot = CreateFirstPersonHands(cameraPivot);
                var thirdPersonRoot = CreateThirdPersonBody(root.transform);

                var fpsController = root.AddComponent<FpsCharacterController>();
                var fpsSerialized = new SerializedObject(fpsController);
                fpsSerialized.FindProperty("cameraPivot").objectReferenceValue = cameraPivot;
                fpsSerialized.FindProperty("playerCamera").objectReferenceValue = camera;
                fpsSerialized.ApplyModifiedPropertiesWithoutUndo();

                var viewPresentation = root.AddComponent<PlayerViewPresentation>();
                var viewSerialized = new SerializedObject(viewPresentation);
                viewSerialized.FindProperty("firstPersonRoot").objectReferenceValue = firstPersonRoot;
                viewSerialized.FindProperty("thirdPersonRoot").objectReferenceValue = thirdPersonRoot;
                viewSerialized.FindProperty("isLocalPlayer").boolValue = true;
                viewSerialized.ApplyModifiedPropertiesWithoutUndo();

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

        private static GameObject CreateFirstPersonHands(Transform parent)
        {
            var root = new GameObject("FirstPersonHands");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, -0.35f, 0.35f);

            CreateHandCube(root.transform, "LeftHand", new Vector3(-0.12f, 0f, 0f));
            CreateHandCube(root.transform, "RightHand", new Vector3(0.12f, 0f, 0f));
            return root;
        }

        private static void CreateHandCube(Transform parent, string name, Vector3 localPos)
        {
            var hand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hand.name = name;
            hand.transform.SetParent(parent, false);
            hand.transform.localPosition = localPos;
            hand.transform.localScale = new Vector3(0.12f, 0.12f, 0.3f);
            PaintRenderer(hand, new Color(0.85f, 0.77f, 0.68f));
            RemoveCollider(hand);
        }

        private static GameObject CreateThirdPersonBody(Transform parent)
        {
            var root = new GameObject("ThirdPersonBody");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.SetActive(false);

            var torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
            torso.name = "Torso";
            torso.transform.SetParent(root.transform, false);
            torso.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            torso.transform.localScale = new Vector3(0.35f, 0.65f, 0.2f);
            PaintRenderer(torso, new Color(0.1f, 0.12f, 0.15f));
            RemoveCollider(torso);

            var headMask = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            headMask.name = "WhiteMaskHead";
            headMask.transform.SetParent(root.transform, false);
            headMask.transform.localPosition = new Vector3(0f, 1.52f, 0f);
            headMask.transform.localScale = new Vector3(0.26f, 0.26f, 0.26f);
            PaintRenderer(headMask, Color.white);
            RemoveCollider(headMask);

            CreateLimb(root.transform, "LeftLeg", new Vector3(-0.1f, 0.45f, 0f), Quaternion.identity);
            CreateLimb(root.transform, "RightLeg", new Vector3(0.1f, 0.45f, 0f), Quaternion.identity);
            CreateLimb(root.transform, "LeftArm", new Vector3(-0.27f, 1.07f, 0f), Quaternion.Euler(0f, 0f, 15f));
            CreateLimb(root.transform, "RightArm", new Vector3(0.27f, 1.07f, 0f), Quaternion.Euler(0f, 0f, -15f));

            return root;
        }

        private static void CreateLimb(Transform parent, string name, Vector3 localPos, Quaternion localRot)
        {
            var limb = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            limb.name = name;
            limb.transform.SetParent(parent, false);
            limb.transform.localPosition = localPos;
            limb.transform.localRotation = localRot;
            limb.transform.localScale = new Vector3(0.06f, 0.35f, 0.06f);
            PaintRenderer(limb, new Color(0.11f, 0.11f, 0.12f));
            RemoveCollider(limb);
        }

        private static void PaintRenderer(GameObject target, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                if (shader == null)
                {
                    return;
                }

                renderer.sharedMaterial = new Material(shader) { color = color };
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
