using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShooterPrototype.Player
{
    public sealed class Day5RuntimeSetup : MonoBehaviour
    {
        private const string RuntimeRootName = "RuntimeDay5Root";
        private const string LocalPlayerName = "RuntimeLocalPlayer";
        private const string RemoteDummyName = "RuntimeRemoteDummy";

        [SerializeField] private bool enabledRuntimeSetup = true;
        [SerializeField] private bool spawnRemoteDummy = true;
        [SerializeField] private string gameSceneName = "Game";

        public void Configure(string sceneName, bool enabledSetup, bool remoteDummy)
        {
            gameSceneName = string.IsNullOrWhiteSpace(sceneName) ? "Game" : sceneName;
            enabledRuntimeSetup = enabledSetup;
            spawnRemoteDummy = remoteDummy;
        }

        public void HandleSceneLoaded(Scene scene)
        {
            if (!enabledRuntimeSetup || scene.name != gameSceneName)
            {
                return;
            }

            EnsureRuntimeSetup();
        }

        private void EnsureRuntimeSetup()
        {
            if (GameObject.Find(LocalPlayerName) != null)
            {
                return;
            }

            var root = new GameObject(RuntimeRootName);
            CreateGround(root.transform);
            CreateLocalPlayer(root.transform);

            if (spawnRemoteDummy)
            {
                CreateRemoteDummy(root.transform);
            }
        }

        private static void CreateGround(Transform parent)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "RuntimeGround";
            ground.transform.SetParent(parent, false);
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(4f, 1f, 4f);

            var renderer = ground.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = new Color(0.17f, 0.2f, 0.22f);
            }
        }

        private static void CreateLocalPlayer(Transform parent)
        {
            var player = new GameObject(LocalPlayerName);
            player.transform.SetParent(parent, false);
            player.transform.position = new Vector3(0f, 1f, -4f);

            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);

            var cameraPivot = new GameObject("CameraPivot").transform;
            cameraPivot.SetParent(player.transform, false);
            cameraPivot.localPosition = new Vector3(0f, 1.6f, 0f);

            var cameraObject = new GameObject("PlayerCamera");
            cameraObject.transform.SetParent(cameraPivot, false);
            cameraObject.transform.localPosition = Vector3.zero;
            cameraObject.transform.localRotation = Quaternion.identity;
            cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";

            var firstPersonHands = BuildFirstPersonHands(cameraPivot);
            var thirdPersonBody = BuildThirdPersonBody(player.transform, true);

            var presenter = player.AddComponent<PlayerViewPresentation>();
            var fpsController = player.AddComponent<FpsCharacterController>();

            presenter.ConfigureRoots(firstPersonHands, thirdPersonBody);
            presenter.Configure(true);

            fpsController.Configure(cameraPivot, cameraObject.GetComponent<Camera>());
        }

        private static void CreateRemoteDummy(Transform parent)
        {
            var dummy = new GameObject(RemoteDummyName);
            dummy.transform.SetParent(parent, false);
            dummy.transform.position = new Vector3(2.6f, 1f, 0f);

            var bodyRoot = BuildThirdPersonBody(dummy.transform, false);
            var presenter = dummy.AddComponent<PlayerViewPresentation>();
            presenter.ConfigureRoots(null, bodyRoot);
            presenter.Configure(false);
        }

        private static GameObject BuildFirstPersonHands(Transform cameraPivot)
        {
            var handsRoot = new GameObject("FirstPersonHands");
            handsRoot.transform.SetParent(cameraPivot, false);
            handsRoot.transform.localPosition = new Vector3(0f, -0.35f, 0.35f);

            var leftHand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftHand.name = "LeftHand";
            leftHand.transform.SetParent(handsRoot.transform, false);
            leftHand.transform.localPosition = new Vector3(-0.12f, 0f, 0f);
            leftHand.transform.localScale = new Vector3(0.12f, 0.12f, 0.3f);

            var rightHand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightHand.name = "RightHand";
            rightHand.transform.SetParent(handsRoot.transform, false);
            rightHand.transform.localPosition = new Vector3(0.12f, 0f, 0f);
            rightHand.transform.localScale = new Vector3(0.12f, 0.12f, 0.3f);

            return handsRoot;
        }

        private static GameObject BuildThirdPersonBody(Transform parent, bool localHiddenBody)
        {
            var root = new GameObject("ThirdPersonBody");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;

            var torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
            torso.name = "Torso";
            torso.transform.SetParent(root.transform, false);
            torso.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            torso.transform.localScale = new Vector3(0.35f, 0.65f, 0.2f);

            var headMask = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            headMask.name = "WhiteMaskHead";
            headMask.transform.SetParent(root.transform, false);
            headMask.transform.localPosition = new Vector3(0f, 1.52f, 0f);
            headMask.transform.localScale = new Vector3(0.26f, 0.26f, 0.26f);

            var leftLeg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            leftLeg.name = "LeftLeg";
            leftLeg.transform.SetParent(root.transform, false);
            leftLeg.transform.localPosition = new Vector3(-0.1f, 0.45f, 0f);
            leftLeg.transform.localScale = new Vector3(0.07f, 0.45f, 0.07f);

            var rightLeg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rightLeg.name = "RightLeg";
            rightLeg.transform.SetParent(root.transform, false);
            rightLeg.transform.localPosition = new Vector3(0.1f, 0.45f, 0f);
            rightLeg.transform.localScale = new Vector3(0.07f, 0.45f, 0.07f);

            var leftArm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            leftArm.name = "LeftArm";
            leftArm.transform.SetParent(root.transform, false);
            leftArm.transform.localPosition = new Vector3(-0.27f, 1.07f, 0f);
            leftArm.transform.localRotation = Quaternion.Euler(0f, 0f, 15f);
            leftArm.transform.localScale = new Vector3(0.05f, 0.33f, 0.05f);

            var rightArm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rightArm.name = "RightArm";
            rightArm.transform.SetParent(root.transform, false);
            rightArm.transform.localPosition = new Vector3(0.27f, 1.07f, 0f);
            rightArm.transform.localRotation = Quaternion.Euler(0f, 0f, -15f);
            rightArm.transform.localScale = new Vector3(0.05f, 0.33f, 0.05f);

            PaintRenderer(headMask, Color.white);
            PaintRenderer(torso, new Color(0.1f, 0.12f, 0.15f));
            PaintRenderer(leftLeg, new Color(0.07f, 0.08f, 0.1f));
            PaintRenderer(rightLeg, new Color(0.07f, 0.08f, 0.1f));
            PaintRenderer(leftArm, new Color(0.11f, 0.11f, 0.12f));
            PaintRenderer(rightArm, new Color(0.11f, 0.11f, 0.12f));

            if (localHiddenBody)
            {
                root.SetActive(false);
            }

            return root;
        }

        private static void PaintRenderer(GameObject target, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
            }
        }
    }
}
