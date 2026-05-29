#if UNITY_EDITOR
using ShooterPrototype.Player;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    public static class SyntyAnimationSetup
    {
        private const string ControllerPath = "Assets/Prefabs/Player/SyntyLocomotion.controller";
        private const string RemoteControllerPath = "Assets/Prefabs/Player/SyntyRemoteLocomotion.controller";
        private const string LegsOnlyMaskPath = "Assets/Prefabs/Player/SyntyLocomotionLegsOnly.mask";
        private const string TorsoOnlyMaskPath = "Assets/Prefabs/Player/SyntyLocomotionTorsoOnly.mask";
        private const string ArmsOnlyMaskPath = "Assets/Prefabs/Player/SyntyLocomotionArmsOnly.mask";
        private const string BodyNoArmsMaskPath = "Assets/Prefabs/Player/SyntyLocomotionBodyNoArms.mask";
        private const string RemoteArmsIdleMaskPath = "Assets/Prefabs/Player/SyntyRemoteArmsIdle.mask";
        private const string RemoteForearmsOnlyMaskPath = "Assets/Prefabs/Player/SyntyRemoteForearmsOnly.mask";
        private const string UpperBodyOnlyMaskPath = "Assets/Prefabs/Player/SyntyLocomotionUpperBodyOnly.mask";
        private const string BlinkAnimationsRoot = "Assets/Blink/Art/Animations";
        private const string BlinkMovementFolder = "Assets/Blink/Art/Animations/Animations_Starter_Pack/Movement";
        private const string OpsiveAnimationsRoot = "Assets/Opsive";
        private const float WalkLocomotionSpeedThreshold = 0.38f;
        private const float SprintStateSpeedMin = 0.52f;
        private const string OpsiveTPoseFolder = "Assets/Opsive/OmniAnimation/Packs/CoreLocomotion/Animations/TPose";
        private const string OpsiveOriginalFolder = "Assets/Opsive/OmniAnimation/Packs/CoreLocomotion/Animations/Original";
        private const string LegacyAnimationsFolder = "Assets/Animations";
        private const string PlayerPrefabPath = PlayerCleanPrefabCreator.TargetPrefabPath;
        private const string PlayerRemotePrefabPath = PlayerRemotePrefabCreator.TargetPrefabPath;
        private const string BusinessMalePlayerPrefabPath = "Assets/Prefabs/Player/PlayerSyntyBusinessMale.prefab";
        private const string Ch18PlayerPrefabPath = "Assets/Prefabs/Player/PlayerCh18.prefab";

        [MenuItem("Shooter Prototype/Setup/Create Clean Player Base (no lines, no hitboxes)")]
        public static void SetupCleanPlayerBase()
        {
            PlayerCleanPrefabCreator.CreateCleanPlayerPrefabInternal();
            AssetDatabase.SaveAssets();
            Debug.Log("[SyntyAnimationSetup] PlayerClean base prefab created from PlayerLocal.");
        }

        [MenuItem("Shooter Prototype/Setup/Rebuild Ch18 From Clean Base")]
        public static void RebuildCh18FromCleanBase()
        {
            EnsureCleanPlayerBaseExists();
            var cleanTemplate = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCleanPrefabCreator.TargetPrefabPath);
            SyntyPlayerPrefabCreator.CreatePlayerPrefabFromFbx(
                cleanTemplate,
                Ch18PlayerPrefabPath,
                SyntyPlayerPrefabCreator.Ch18CharacterFbxAssetPath,
                "PlayerCh18");
            PrepareHumanoidRetargetingForPrefab(Ch18PlayerPrefabPath);
            ConfigurePlayerPrefab(Ch18PlayerPrefabPath, FirstPersonArmsCoverage.FullArms);
            AssetDatabase.SaveAssets();
            Debug.Log("[SyntyAnimationSetup] PlayerCh18 rebuilt from PlayerClean.");
        }

        [MenuItem("Shooter Prototype/Setup/Rebuild Business Male From Clean Base")]
        public static void RebuildBusinessMaleFromCleanBase()
        {
            EnsureCleanPlayerBaseExists();
            var cleanTemplate = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCleanPrefabCreator.TargetPrefabPath);
            var characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Synty/PolygonBattleRoyale/Prefabs/Characters/Character_BusinessMale_01.prefab");
            if (characterPrefab == null)
            {
                Debug.LogError("[SyntyAnimationSetup] Character_BusinessMale_01 prefab not found.");
                return;
            }

            SyntyPlayerPrefabCreator.CreatePlayerPrefabFromTemplate(
                cleanTemplate,
                BusinessMalePlayerPrefabPath,
                characterPrefab,
                "PlayerSyntyBusinessMale");
            PrepareHumanoidRetargetingForPrefab(BusinessMalePlayerPrefabPath);
            ConfigurePlayerPrefab(BusinessMalePlayerPrefabPath);
            TryAssignSpawnPrefab(BusinessMalePlayerPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[SyntyAnimationSetup] PlayerSyntyBusinessMale rebuilt from PlayerClean.");
        }

        private static void EnsureCleanPlayerBaseExists()
        {
            var clean = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCleanPrefabCreator.TargetPrefabPath);
            if (clean != null)
            {
                return;
            }

            PlayerCleanPrefabCreator.CreateCleanPlayerPrefabInternal();
        }

        [MenuItem("Shooter Prototype/Setup/Create Business Male Player Prefab (from Synty settings)")]
        public static void SetupBusinessMalePlayerPrefab()
        {
            SyntyPlayerPrefabCreator.CreateBusinessMalePlayerPrefab();
            PrepareHumanoidRetargetingForPrefab(BusinessMalePlayerPrefabPath);
            ConfigurePlayerPrefab(BusinessMalePlayerPrefabPath);
            TryAssignSpawnPrefab(BusinessMalePlayerPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[SyntyAnimationSetup] PlayerSyntyBusinessMale created and configured.");
        }

        [MenuItem("Shooter Prototype/Setup/Repair Player Synty Business Male Prefab")]
        public static void RepairPlayerSyntyBusinessMalePrefab()
        {
            SyntyPlayerPrefabCreator.CreateBusinessMalePlayerPrefab();
            ConfigurePlayerPrefab(BusinessMalePlayerPrefabPath);
            TryAssignSpawnPrefab(BusinessMalePlayerPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[SyntyAnimationSetup] PlayerSyntyBusinessMale repaired with split FP/TP body.");
        }

        [MenuItem("Shooter Prototype/Setup/Create Player Ch18 Prefab (Business Male settings)")]
        public static void SetupCh18PlayerPrefab()
        {
            SyntyPlayerPrefabCreator.CreateCh18PlayerPrefab();
            PrepareHumanoidRetargetingForPrefab(Ch18PlayerPrefabPath);
            ConfigurePlayerPrefab(Ch18PlayerPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[SyntyAnimationSetup] PlayerCh18 created and configured.");
        }

        [MenuItem("Shooter Prototype/Setup/Repair Player Ch18 Prefab")]
        public static void RepairPlayerCh18Prefab()
        {
            SyntyPlayerPrefabCreator.CreateCh18PlayerPrefab();
            PrepareHumanoidRetargetingForPrefab(Ch18PlayerPrefabPath);
            ConfigurePlayerPrefab(Ch18PlayerPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[SyntyAnimationSetup] PlayerCh18 repaired with split FP/TP body.");
        }

        [MenuItem("Shooter Prototype/Setup/Enable Split FP/TP Body (Business Male)")]
        public static void EnableSplitBodyOnBusinessMalePrefab()
        {
            var prefabPath = BusinessMalePlayerPrefabPath;
            var playerRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (playerRoot == null)
            {
                Debug.LogError($"[SyntyAnimationSetup] Missing prefab: {prefabPath}");
                return;
            }

            var instance = PrefabUtility.InstantiatePrefab(playerRoot) as GameObject;
            if (instance == null)
            {
                return;
            }

            try
            {
                SyntySplitBodyViewSetup.WireSplitBodyView(instance);
                var thirdPersonBody = FindChild(instance.transform, "ThirdPersonBody");
                var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
                var armsPresenter = instance.GetComponent<SyntyFirstPersonArmsPresenter>();
                if (armsPresenter != null && syntyVisual != null)
                {
                    armsPresenter.ResetForRebuild();
                    armsPresenter.Configure(
                        syntyVisual,
                        0.35f,
                        SyntyPlayerPrefabCreator.ResolvePrimaryCharacterMeshName(syntyVisual),
                        FirstPersonArmsCoverage.HandsAndForearms);
                }

                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[SyntyAnimationSetup] Split FP/TP body enabled on PlayerSyntyBusinessMale.");
        }

        [MenuItem("Shooter Prototype/Setup/Repair Player Synty Human Prefab")]
        public static void RepairPlayerSyntyHumanPrefab()
        {
            SyntyPlayerPrefabCreator.CreateOrUpdatePlayerPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log("[SyntyAnimationSetup] PlayerSyntyHuman repaired and Synty visual refreshed.");
        }

        [MenuItem("Shooter Prototype/Setup/Synty Animated Player (Full)")]
        public static void SetupAnimatedPlayer()
        {
            SyntyPlayerPrefabCreator.CreateOrUpdatePlayerPrefab();
            PrepareHumanoidRetargetingFromPlayerPrefab();

            var clips = FindLocomotionClips();
            if (clips.Idle == null)
            {
                Debug.LogWarning(
                    "[SyntyAnimationSetup] No animation clips found. Add Blink FBX files under Assets/Blink/Art/Animations, then run this menu again.");
            }
            else
            {
                CreateOrUpdateAnimatorController(clips);
                Debug.Log(
                    $"[SyntyAnimationSetup] Controller clips: Idle={NameOf(clips.Idle)}, Arms={NameOf(clips.ArmsIdle)}, Run8={NameOf(clips.RunForward)}, Sprint={NameOf(clips.Sprint)}");
            }

            ConfigurePlayerPrefab();
            TryAssignSpawnPrefab(PlayerPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[SyntyAnimationSetup] Synty animated player setup complete.");
        }

        [MenuItem("Shooter Prototype/Setup/Rebuild Animation Controller (Blink local + Opsive remote)")]
        public static void RebuildAnimationControllerOnly()
        {
            PrepareHumanoidRetargetingFromPlayerPrefab();

            var localClips = FindLocomotionClips();
            if (localClips.Idle == null)
            {
                Debug.LogError("[SyntyAnimationSetup] No local clips found under Assets/Blink/Art/Animations.");
                return;
            }

            var remoteClips = FindRemoteLocomotionClips();
            if (remoteClips.Idle == null)
            {
                Debug.LogError("[SyntyAnimationSetup] No remote clips found under Assets/Opsive.");
                return;
            }

            CreateOrUpdateAnimatorController(localClips);
            CreateOrUpdateRemoteAnimatorController(remoteClips);
            ConfigurePlayerPrefabIfExists(PlayerPrefabPath);
            ConfigurePlayerPrefabIfExists(PlayerRemotePrefabPath);
            TryAssignSpawnPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log("[SyntyAnimationSetup] Local controller rebuilt from Blink; remote controller rebuilt from Opsive (legs locomotion + torso/arms idle).");
        }

        private static void ConfigurePlayerPrefabIfExists(string playerPrefabPath)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefabPath) == null)
            {
                Debug.LogWarning($"[SyntyAnimationSetup] Skipping prefab wiring, missing: {playerPrefabPath}");
                return;
            }

            ConfigurePlayerPrefab(playerPrefabPath);
        }

        public static void CreateOrUpdateAnimatorController(LocomotionClips clips)
        {
            EnsureFolder("Assets/Prefabs/Player");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }

            EnsureParameters(controller);
            BuildLocomotionLayer(controller, clips);
            ApplyLegsOnlyAvatarMasks(controller, clips);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        public static void CreateOrUpdateRemoteAnimatorController(LocomotionClips clips)
        {
            EnsureFolder("Assets/Prefabs/Player");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(RemoteControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(RemoteControllerPath);
            }

            EnsureParameters(controller);
            BuildRemoteLocomotionLayer(controller, clips);
            ApplyLegsOnlyAvatarMasks(controller, clips);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        internal static RuntimeAnimatorController LoadRemoteAnimatorControllerAsset()
        {
            return AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(RemoteControllerPath);
        }

        private static LocomotionClips FindRemoteLocomotionClips()
        {
            var clips = FindLocomotionClipsFromOpsiveFolder();
            clips.ArmsIdle = LoadArmsIdleClip();
            return clips;
        }

        private static void ApplyRemoteLocomotionLayers(AnimatorController controller, LocomotionClips clips)
        {
            if (controller.layers.Length == 0)
            {
                return;
            }

            EnsureRemoteLocomotionAvatarMaskAssets();

            var bodyNoArmsMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(BodyNoArmsMaskPath);
            var armsClip = clips.ArmsIdle ?? LoadArmsIdleClip();
            var armsMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(RemoteArmsIdleMaskPath)
                ?? AssetDatabase.LoadAssetAtPath<AvatarMask>(ArmsOnlyMaskPath);
            if (armsClip == null || armsMask == null)
            {
                var fallbackLayers = controller.layers;
                fallbackLayers[0].name = "Full Body Locomotion";
                fallbackLayers[0].avatarMask = null;
                fallbackLayers[0].defaultWeight = 1f;
                fallbackLayers[0].blendingMode = AnimatorLayerBlendingMode.Override;
                fallbackLayers[0].syncedLayerIndex = -1;
                fallbackLayers[0].iKPass = false;
                controller.layers = new[] { fallbackLayers[0] };
                return;
            }

            EnsureAnimatorLayerCount(controller, 2);
            var layers = controller.layers;
            layers[0].name = "Full Body Locomotion";
            layers[0].avatarMask = bodyNoArmsMask;
            layers[0].defaultWeight = 1f;
            layers[0].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[0].syncedLayerIndex = -1;
            layers[0].iKPass = false;
            BuildOverrideIdleLayer(controller, ref layers, 1, armsMask, armsClip, "Arms Idle");
            controller.layers = layers;
        }

        private static void ApplyFullBodyLocomotionLayer(AnimatorController controller)
        {
            var layers = controller.layers;
            if (layers.Length == 0)
            {
                return;
            }

            layers[0].name = "Full Body Locomotion";
            layers[0].avatarMask = null;
            layers[0].defaultWeight = 1f;
            layers[0].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[0].syncedLayerIndex = -1;
            layers[0].iKPass = false;
            controller.layers = new[] { layers[0] };
        }

        public static void ConfigurePlayerPrefab()
        {
            ConfigurePlayerPrefab(PlayerPrefabPath);
        }

        public static void ConfigurePlayerPrefab(string playerPrefabPath)
        {
            ConfigurePlayerPrefab(playerPrefabPath, FirstPersonArmsCoverage.HandsAndForearms);
        }

        public static void ConfigurePlayerPrefab(string playerPrefabPath, FirstPersonArmsCoverage armsCoverage)
        {
            var playerRoot = AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefabPath);
            if (playerRoot == null)
            {
                Debug.LogWarning($"[SyntyAnimationSetup] Missing prefab: {playerPrefabPath}");
                return;
            }

            var instance = PrefabUtility.InstantiatePrefab(playerRoot) as GameObject;
            if (instance == null)
            {
                return;
            }

            try
            {
                var thirdPersonBody = FindChild(instance.transform, "ThirdPersonBody");
                if (thirdPersonBody != null)
                {
                    SyntyPlayerPrefabCreator.CleanupGeneratedFirstPersonArms(thirdPersonBody);
                }

                var armsPresenter = instance.GetComponent<SyntyFirstPersonArmsPresenter>();
                if (armsPresenter != null)
                {
                    armsPresenter.ResetForRebuild();
                }

                WireMecanimComponents(instance, armsCoverage);
                SyntyPlayerPrefabCreator.RestoreActiveCharacterMeshesForPrefabSave(instance);
                PrefabUtility.SaveAsPrefabAsset(instance, playerPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void PrepareHumanoidRetargetingFromPlayerPrefab()
        {
            PrepareHumanoidRetargetingForPrefab(PlayerPrefabPath);
        }

        private static void PrepareHumanoidRetargetingForPrefab(string playerPrefabPath)
        {
            var playerRoot = AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefabPath);
            if (playerRoot == null)
            {
                return;
            }

            var instance = PrefabUtility.InstantiatePrefab(playerRoot) as GameObject;
            if (instance == null)
            {
                return;
            }

            try
            {
                var thirdPersonBody = FindChild(instance.transform, "ThirdPersonBody");
                var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
                EnsureSyntyHumanoidAvatar(syntyVisual);
                ConfigureBlinkAnimationImports();
                ConfigureLegacyAnimationImports();
                ConfigureOpsiveAnimationImports();
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        internal static void WireMecanimComponents(
            GameObject playerRoot,
            FirstPersonArmsCoverage armsCoverage = FirstPersonArmsCoverage.HandsAndForearms)
        {
            var thirdPersonBody = FindChild(playerRoot.transform, "ThirdPersonBody");
            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (thirdPersonBody == null || syntyVisual == null)
            {
                Debug.LogError("[SyntyAnimationSetup] ThirdPersonBody/SyntyVisual missing. Run Create Synty Human Player Prefab first.");
                return;
            }

            SyntyPlayerPrefabCreator.ActivatePrimaryCharacterVariant(
                syntyVisual.gameObject,
                SyntyPlayerPrefabCreator.ResolvePrimaryCharacterMeshName(syntyVisual));

            var characterMeshName = SyntyPlayerPrefabCreator.ResolvePrimaryCharacterMeshName(syntyVisual);
            var fpsController = playerRoot.GetComponent<FpsCharacterController>();
            var locomotionRig = thirdPersonBody.GetComponent<ProceduralLocomotionRig>();
            var shoulder = FindChild(thirdPersonBody, "ShoulderAnchor");
            var hip = FindChild(thirdPersonBody, "HipAnchor");
            var leftHand = FindChild(thirdPersonBody, "LeftHandTarget");
            var rightHand = FindChild(thirdPersonBody, "RightHandTarget");
            var leftFoot = FindChild(thirdPersonBody, "LeftFootTarget");
            var rightFoot = FindChild(thirdPersonBody, "RightFootTarget");
            var head = FindChild(thirdPersonBody, "HeadTarget");

            var animator = syntyVisual.GetComponent<Animator>();
            if (animator == null)
            {
                animator = syntyVisual.gameObject.AddComponent<Animator>();
            }

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            ApplyHumanoidAvatarFromSynty(animator, syntyVisual);

            var driver = playerRoot.GetComponent<SyntyLocomotionDriver>();
            if (driver == null)
            {
                driver = playerRoot.AddComponent<SyntyLocomotionDriver>();
            }
            driver.Configure(animator, fpsController, locomotionRig);
            ApplyDefaultLocomotionPlaybackSpeeds(driver);

            var handBinder = playerRoot.GetComponent<SyntyWeaponHandBinder>();
            if (handBinder == null)
            {
                handBinder = playerRoot.AddComponent<SyntyWeaponHandBinder>();
            }
            handBinder.ConfigureFromVisualRoot(syntyVisual);
            var handBinderSerialized = new SerializedObject(handBinder);
            handBinderSerialized.FindProperty("onlyApplyInLocalFirstPerson").boolValue = true;
            handBinderSerialized.FindProperty("lockUpperBodyBeforeIk").boolValue = false;
            handBinderSerialized.FindProperty("resetArmPoseBeforeIk").boolValue = false;
            handBinderSerialized.FindProperty("applyIkBeforeRender").boolValue = false;
            handBinderSerialized.FindProperty("useDetachedShoulderAnchorsInFirstPerson").boolValue = true;
            handBinderSerialized.FindProperty("handIkWeight").floatValue = 1f;
            handBinderSerialized.ApplyModifiedPropertiesWithoutUndo();
            handBinder.SetHandIkEnabled(true);

            var boneSync = playerRoot.GetComponent<SyntyBoneAnchorSync>();
            if (boneSync == null)
            {
                boneSync = playerRoot.AddComponent<SyntyBoneAnchorSync>();
            }
            boneSync.Configure(syntyVisual, shoulder, hip, leftHand, rightHand, leftFoot, rightFoot, head);

            var armsPresenter = playerRoot.GetComponent<SyntyFirstPersonArmsPresenter>();
            if (armsPresenter == null)
            {
                armsPresenter = playerRoot.AddComponent<SyntyFirstPersonArmsPresenter>();
            }

            var armLocomotionGate = playerRoot.GetComponent<SyntyFirstPersonArmLocomotionGate>();
            if (armLocomotionGate == null)
            {
                armLocomotionGate = playerRoot.AddComponent<SyntyFirstPersonArmLocomotionGate>();
            }

            armLocomotionGate.Configure(animator);
            var armGateSerialized = new SerializedObject(armLocomotionGate);
            armGateSerialized.FindProperty("suppressArmLocomotionInFirstPerson").boolValue = true;
            armGateSerialized.FindProperty("disableSkeletonAnimatorInFirstPerson").boolValue = true;
            armGateSerialized.ApplyModifiedPropertiesWithoutUndo();
            armLocomotionGate.SetDisableSkeletonAnimatorInFirstPerson(true);

            var handAttachedWeaponMount = playerRoot.GetComponent<SyntyHandAttachedWeaponMount>();
            if (handAttachedWeaponMount == null)
            {
                handAttachedWeaponMount = playerRoot.AddComponent<SyntyHandAttachedWeaponMount>();
            }

            var weaponMount = playerRoot.GetComponent<PlayerWeaponMount>();
            handAttachedWeaponMount.Configure(syntyVisual, weaponMount);
            handAttachedWeaponMount.enabled = false;

            var binder = playerRoot.GetComponent<SyntyCharacterVisualBinder>();
            if (binder != null)
            {
                var binderSerialized = new SerializedObject(binder);
                binderSerialized.FindProperty("showSyntyMeshInFirstPerson").boolValue = false;
                binderSerialized.FindProperty("showArmsOnlyInFirstPerson").boolValue = false;
                binderSerialized.ApplyModifiedPropertiesWithoutUndo();
                binder.ApplyMecanimMode();
            }

            if (locomotionRig != null)
            {
                locomotionRig.SetProceduralVisualsEnabled(false);
            }

            var viewPresentation = playerRoot.GetComponent<PlayerViewPresentation>();
            if (viewPresentation != null)
            {
                var viewSerialized = new SerializedObject(viewPresentation);
                viewSerialized.FindProperty("armNameContains").stringValue = "Hand";
                viewSerialized.FindProperty("weaponNameContains").stringValue = "Weapon";
                viewSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var armsPresenterSerialized = new SerializedObject(armsPresenter);
            armsPresenterSerialized.FindProperty("armsCoverage").enumValueIndex = (int)armsCoverage;
            armsPresenterSerialized.ApplyModifiedPropertiesWithoutUndo();

            SyntySplitBodyViewSetup.WireSplitBodyView(playerRoot);

            armsPresenter.ResetForRebuild();
            armsPresenter.Configure(
                syntyVisual,
                0.35f,
                characterMeshName,
                armsCoverage);
        }

        private static void ApplyDefaultLocomotionPlaybackSpeeds(SyntyLocomotionDriver driver)
        {
            if (driver == null)
            {
                return;
            }

            var serialized = new SerializedObject(driver);
            serialized.FindProperty("walkAnimationSpeed").floatValue = 0.62f;
            serialized.FindProperty("sprintAnimationSpeed").floatValue = 1f;
            serialized.FindProperty("crouchAnimationSpeed").floatValue = 0.68f;
            serialized.FindProperty("animatorSpeedSmoothTime").floatValue = 0.1f;
            serialized.FindProperty("networkMoveSmoothTime").floatValue = 0.12f;
            serialized.FindProperty("networkStopSmoothTime").floatValue = 0.05f;
            serialized.FindProperty("moveSmoothTime").floatValue = 0.08f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static LocomotionClips FindLocomotionClips()
        {
            var fromBlink = FindLocomotionClipsFromBlinkFolder();
            if (fromBlink.Idle != null)
            {
                return fromBlink;
            }

            var fromLegacy = FindLocomotionClipsFromAnimationsFolder();
            if (fromLegacy.Idle != null)
            {
                return fromLegacy;
            }

            var searchRoots = new List<string> { BlinkAnimationsRoot, LegacyAnimationsFolder, "Assets/Synty", "Assets" };
            var clipGuids = AssetDatabase.FindAssets("t:AnimationClip", searchRoots.ToArray());
            var clips = new List<AnimationClip>();
            for (var i = 0; i < clipGuids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(clipGuids[i]);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip != null && !clip.name.StartsWith("__preview__"))
                {
                    clips.Add(clip);
                }
            }

            return new LocomotionClips
            {
                Idle = FindClip(clips, "Idle", "A_Idle"),
                Walk = FindClip(clips, "ForwardWalking", "Forward_Walk", "Walk_Fwd", "A_Walk", "Walk"),
                WalkLeft = FindClip(clips, "LeftWalking", "Left_Walk", "Walk_Left"),
                WalkRight = FindClip(clips, "RightWalking", "Right_Walk", "Walk_Right"),
                Run = FindClip(clips, "Run", "A_Run", "Sprint"),
                Jump = FindClip(clips, "Jump", "A_Jump"),
                Fall = FindClip(clips, "Fall", "A_Fall", "InAir"),
                Land = FindClip(clips, "Land", "A_Land"),
                CrouchIdle = FindClip(clips, "CrouchIdle", "Crouch_Idle"),
                CrouchWalk = FindClip(clips, "CrouchForwardWalking", "CrouchForward", "Crouch_Walk", "CrouchWalk")
            };
        }

        private static LocomotionClips FindLocomotionClipsFromBlinkFolder()
        {
            if (!AssetDatabase.IsValidFolder(BlinkMovementFolder))
            {
                return new LocomotionClips();
            }

            return new LocomotionClips
            {
                Idle = LoadClipFromFbx($"{BlinkMovementFolder}/Idle.fbx"),
                WalkLeft = LoadClipFromFbx($"{BlinkMovementFolder}/StrafeLeft.fbx"),
                WalkRight = LoadClipFromFbx($"{BlinkMovementFolder}/StrafeRight.fbx"),
                WalkForward = LoadClipFromFbx($"{BlinkMovementFolder}/RunForward.fbx"),
                WalkBackward = LoadClipFromFbx($"{BlinkMovementFolder}/RunBackward.fbx"),
                Walk = LoadClipFromFbx($"{BlinkMovementFolder}/RunForward.fbx"),
                RunForward = LoadClipFromFbx($"{BlinkMovementFolder}/RunForward.fbx"),
                RunBackward = LoadClipFromFbx($"{BlinkMovementFolder}/RunBackward.fbx"),
                RunLeft = LoadClipFromFbx($"{BlinkMovementFolder}/RunLeft.fbx"),
                RunRight = LoadClipFromFbx($"{BlinkMovementFolder}/RunRight.fbx"),
                RunBackwardLeft = LoadClipFromFbx($"{BlinkMovementFolder}/RunBackwardLeft.fbx"),
                RunBackwardRight = LoadClipFromFbx($"{BlinkMovementFolder}/RunBackwardRight.fbx"),
                Run = LoadClipFromFbx($"{BlinkMovementFolder}/RunForward.fbx"),
                Sprint = LoadClipFromFbx($"{BlinkMovementFolder}/Sprint.fbx"),
                Jump = LoadClipFromFbx($"{BlinkMovementFolder}/Jumps.fbx"),
                JumpWhileRunning = LoadClipFromFbx($"{BlinkMovementFolder}/JumpWhileRunning.fbx"),
                Fall = LoadClipFromFbx($"{BlinkMovementFolder}/FallingLoop.fbx")
            };
        }

        private static LocomotionClips FindLocomotionClipsFromAnimationsFolder()
        {
            if (!AssetDatabase.IsValidFolder(LegacyAnimationsFolder))
            {
                return new LocomotionClips();
            }

            return new LocomotionClips
            {
                Idle = LoadClipFromFbx($"{LegacyAnimationsFolder}/Idle.fbx"),
                WalkForward = LoadClipFromFbx($"{LegacyAnimationsFolder}/ForwardWalking.fbx"),
                WalkLeft = LoadClipFromFbx($"{LegacyAnimationsFolder}/LeftWalking.fbx"),
                WalkRight = LoadClipFromFbx($"{LegacyAnimationsFolder}/RightWalking.fbx"),
                Walk = LoadClipFromFbx($"{LegacyAnimationsFolder}/ForwardWalking.fbx"),
                Run = LoadClipFromFbx($"{LegacyAnimationsFolder}/Run.fbx"),
                Jump = LoadClipFromFbx($"{LegacyAnimationsFolder}/Jump.fbx"),
                CrouchIdle = LoadClipFromFbx($"{LegacyAnimationsFolder}/CrouchIdle.fbx"),
                CrouchWalk = LoadClipFromFbx($"{LegacyAnimationsFolder}/CrouchForwardWalking.fbx"),
                CrouchWalkRight = LoadClipFromFbx($"{LegacyAnimationsFolder}/CrouchRightWalking.fbx"),
                ArmsIdle = LoadClipFromFbx($"{LegacyAnimationsFolder}/ArmsIdle.fbx")
            };
        }

        private static AnimationClip LoadArmsIdleClip()
        {
            return LoadClipFromFbx($"{LegacyAnimationsFolder}/ArmsIdle.fbx");
        }

        private static string ResolveOpsiveLocomotionFolder()
        {
            if (AssetDatabase.IsValidFolder(OpsiveTPoseFolder))
            {
                return OpsiveTPoseFolder;
            }

            if (AssetDatabase.IsValidFolder(OpsiveOriginalFolder))
            {
                return OpsiveOriginalFolder;
            }

            return OpsiveTPoseFolder;
        }

        private static AnimationClip LoadOpsiveClip(string fileName)
        {
            return LoadClipFromFbx($"{ResolveOpsiveLocomotionFolder()}/{fileName}.fbx");
        }

        private static LocomotionClips FindLocomotionClipsFromOpsiveFolder()
        {
            var folder = ResolveOpsiveLocomotionFolder();
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return new LocomotionClips();
            }

            return new LocomotionClips
            {
                Idle = LoadOpsiveClip("Idle"),
                WalkForward = LoadOpsiveClip("WalkForward"),
                WalkBackward = LoadOpsiveClip("WalkBackward"),
                WalkLeft = LoadOpsiveClip("WalkStrafeLeft"),
                WalkRight = LoadOpsiveClip("WalkStrafeRight"),
                WalkForwardDiagonalLeft = LoadOpsiveClip("WalkForwardDiagonalLeft"),
                WalkForwardDiagonalRight = LoadOpsiveClip("WalkForwardDiagonalRight"),
                WalkBackwardDiagonalLeft = LoadOpsiveClip("WalkBackwardDiagonalLeft"),
                WalkBackwardDiagonalRight = LoadOpsiveClip("WalkBackwardDiagonalRight"),
                Walk = LoadOpsiveClip("WalkForward"),
                RunForward = LoadOpsiveClip("RunForward"),
                RunBackward = LoadOpsiveClip("RunBackward"),
                RunLeft = LoadOpsiveClip("RunStrafeLeft"),
                RunRight = LoadOpsiveClip("RunStrafeRight"),
                RunForwardDiagonalLeft = LoadOpsiveClip("RunForwardDiagonalLeft"),
                RunForwardDiagonalRight = LoadOpsiveClip("RunForwardDiagonalRight"),
                RunBackwardLeft = LoadOpsiveClip("RunBackwardDiagonalLeft"),
                RunBackwardRight = LoadOpsiveClip("RunBackwardDiagonalRight"),
                Run = LoadOpsiveClip("RunForward"),
                Sprint = LoadOpsiveClip("SprintForward"),
                SprintDiagonalLeft = LoadOpsiveClip("SprintForwardDiagonalLeft"),
                SprintDiagonalRight = LoadOpsiveClip("SprintForwardDiagonalRight"),
                Jump = LoadOpsiveClip("IdleJump"),
                WalkJumpLeft = LoadOpsiveClip("WalkJumpLeft"),
                WalkJumpRight = LoadOpsiveClip("WalkJumpRight"),
                JumpWhileRunning = LoadOpsiveClip("RunJumpLeft"),
                JumpRunRight = LoadOpsiveClip("RunJumpRight"),
                CrouchIdle = LoadOpsiveClip("CrouchIdle"),
                CrouchWalk = LoadOpsiveClip("CrouchWalkForward"),
                CrouchWalkBackward = LoadOpsiveClip("CrouchWalkBackward"),
                CrouchWalkLeft = LoadOpsiveClip("CrouchWalkStrafeLeft"),
                CrouchWalkRight = LoadOpsiveClip("CrouchWalkStrafeRight"),
                CrouchWalkForwardDiagonalLeft = LoadOpsiveClip("CrouchWalkForwardDiagonalLeft"),
                CrouchWalkForwardDiagonalRight = LoadOpsiveClip("CrouchWalkForwardDiagonalRight"),
                CrouchWalkBackwardDiagonalLeft = LoadOpsiveClip("CrouchWalkBackwardDiagonalLeft"),
                CrouchWalkBackwardDiagonalRight = LoadOpsiveClip("CrouchWalkBackwardDiagonalRight"),
                CrouchRun = LoadOpsiveClip("CrouchRunForward"),
                CrouchRunForward = LoadOpsiveClip("CrouchRunForward"),
                CrouchRunBackward = LoadOpsiveClip("CrouchRunBackward"),
                CrouchRunLeft = LoadOpsiveClip("CrouchRunStrafeLeft"),
                CrouchRunRight = LoadOpsiveClip("CrouchRunStrafeRight"),
                CrouchRunForwardDiagonalLeft = LoadOpsiveClip("CrouchRunForwardDiagonalLeft"),
                CrouchRunForwardDiagonalRight = LoadOpsiveClip("CrouchRunForwardDiagonalRight"),
                CrouchRunBackwardDiagonalLeft = LoadOpsiveClip("CrouchRunBackwardDiagonalLeft"),
                CrouchRunBackwardDiagonalRight = LoadOpsiveClip("CrouchRunBackwardDiagonalRight")
            };
        }

        private static void ConfigureOpsiveAnimationImports()
        {
            if (!AssetDatabase.IsValidFolder(OpsiveAnimationsRoot))
            {
                return;
            }

            var fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { OpsiveAnimationsRoot });
            for (var i = 0; i < fbxGuids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(fbxGuids[i]);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ConfigureBlinkFbxImport(path);
            }
        }

        private static AnimationClip LoadClipFromFbx(string fbxAssetPath)
        {
            if (string.IsNullOrWhiteSpace(fbxAssetPath))
            {
                return null;
            }

            var assets = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath);
            if (assets == null || assets.Length == 0)
            {
                return null;
            }

            AnimationClip fallback = null;
            for (var i = 0; i < assets.Length; i++)
            {
                if (assets[i] is not AnimationClip clip || clip.name.StartsWith("__preview__"))
                {
                    continue;
                }

                fallback = clip;
                var fbxName = Path.GetFileNameWithoutExtension(fbxAssetPath);
                if (string.Equals(clip.name, fbxName, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(clip.name, "mixamo.com", System.StringComparison.OrdinalIgnoreCase))
                {
                    return clip;
                }
            }

            return fallback;
        }

        private static Avatar EnsureSyntyHumanoidAvatar(Transform syntyVisual)
        {
            var modelPath = ResolveSyntyModelAssetPath(syntyVisual);
            if (string.IsNullOrEmpty(modelPath))
            {
                Debug.LogWarning(
                    "[SyntyAnimationSetup] Could not locate Synty model FBX. " +
                    "Open the character mesh Rig tab and set Animation Type to Humanoid.");
                return null;
            }

            if (!modelPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            {
                if (syntyVisual != null)
                {
                    var existingAnimator = syntyVisual.GetComponent<Animator>();
                    if (existingAnimator != null &&
                        existingAnimator.avatar != null &&
                        existingAnimator.avatar.isValid)
                    {
                        return existingAnimator.avatar;
                    }
                }

                Debug.LogWarning(
                    $"[SyntyAnimationSetup] Synty source is not an FBX ({modelPath}). " +
                    "Select the Synty character mesh FBX, set Rig to Humanoid, then re-run setup.");
                return null;
            }

            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[SyntyAnimationSetup] No ModelImporter for {modelPath}");
                return LoadAvatarFromAsset(modelPath);
            }

            var needsReimport = false;
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                needsReimport = true;
            }

            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                needsReimport = true;
            }

            if (needsReimport)
            {
                importer.SaveAndReimport();
            }

            var avatar = LoadAvatarFromAsset(modelPath);
            if (avatar == null || !avatar.isValid)
            {
                Debug.LogError(
                    $"[SyntyAnimationSetup] Humanoid avatar invalid for {modelPath}. " +
                    "Configure Avatar Mapping manually (Hips, Shoulder_L, Elbow_L, Hand_L, etc.).");
            }

            return avatar;
        }

        private static void ConfigureBlinkAnimationImports()
        {
            if (!AssetDatabase.IsValidFolder(BlinkAnimationsRoot))
            {
                return;
            }

            var fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { BlinkAnimationsRoot });
            for (var i = 0; i < fbxGuids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(fbxGuids[i]);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ConfigureBlinkFbxImport(path);
            }
        }

        private static void ConfigureLegacyAnimationImports()
        {
            if (!AssetDatabase.IsValidFolder(LegacyAnimationsFolder))
            {
                return;
            }

            var fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { LegacyAnimationsFolder });
            for (var i = 0; i < fbxGuids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(fbxGuids[i]);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ConfigureBlinkFbxImport(path);
            }
        }

        private static void ConfigureBlinkFbxImport(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                return;
            }

            var needsReimport = false;
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                needsReimport = true;
            }

            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel ||
                importer.sourceAvatar != null)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.sourceAvatar = null;
                needsReimport = true;
            }

            if (needsReimport)
            {
                importer.SaveAndReimport();
            }
        }

        internal static void EnsureHumanoidAvatarForVisual(Transform syntyVisual)
        {
            if (syntyVisual == null)
            {
                return;
            }

            var animator = syntyVisual.GetComponent<Animator>();
            if (animator == null)
            {
                animator = syntyVisual.gameObject.AddComponent<Animator>();
            }

            ApplyHumanoidAvatarFromSynty(animator, syntyVisual);
        }

        private static void ApplyHumanoidAvatarFromSynty(Animator animator, Transform syntyVisual)
        {
            if (animator == null)
            {
                return;
            }

            var avatar = EnsureSyntyHumanoidAvatar(syntyVisual);
            if (avatar == null || !avatar.isValid)
            {
                return;
            }

            animator.avatar = avatar;
            Debug.Log("[SyntyAnimationSetup] Assigned Synty Humanoid avatar (not Mixamo).");
        }

        private static string ResolveSyntyModelAssetPath(Transform syntyVisual)
        {
            if (syntyVisual != null)
            {
                var skinnedMeshes = syntyVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (var i = 0; i < skinnedMeshes.Length; i++)
                {
                    var mesh = skinnedMeshes[i].sharedMesh;
                    if (mesh == null)
                    {
                        continue;
                    }

                    var meshPath = AssetDatabase.GetAssetPath(mesh);
                    if (!string.IsNullOrEmpty(meshPath))
                    {
                        return meshPath;
                    }
                }

                var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(syntyVisual.gameObject);
                if (!string.IsNullOrEmpty(prefabPath) &&
                    prefabPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                {
                    return prefabPath;
                }
            }

            return FindDefaultSyntyModelPath();
        }

        private static string FindDefaultSyntyModelPath()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Synty"))
            {
                return null;
            }

            var guids = AssetDatabase.FindAssets("Character_BusinessMale t:Model", new[] { "Assets/Synty" });
            if (guids.Length == 0)
            {
                guids = AssetDatabase.FindAssets("Character_ t:Model", new[] { "Assets/Synty" });
            }

            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }

            return null;
        }

        private static Avatar LoadAvatarFromAsset(string assetPath)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (var i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Avatar avatar && avatar.isValid)
                {
                    return avatar;
                }
            }

            return null;
        }

        private static string NameOf(Object asset)
        {
            return asset != null ? asset.name : "missing";
        }

        private static AnimationClip FindClip(List<AnimationClip> clips, params string[] tokens)
        {
            for (var t = 0; t < tokens.Length; t++)
            {
                var token = tokens[t];
                var match = clips.FirstOrDefault(c =>
                    c.name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static void EnsureParameters(AnimatorController controller)
        {
            EnsureParameter(controller, "Speed", AnimatorControllerParameterType.Float);
            EnsureParameter(controller, "MoveX", AnimatorControllerParameterType.Float);
            EnsureParameter(controller, "MoveY", AnimatorControllerParameterType.Float);
            EnsureParameter(controller, "Grounded", AnimatorControllerParameterType.Bool);
            EnsureParameter(controller, "Sprinting", AnimatorControllerParameterType.Bool);
            EnsureParameter(controller, "Crouching", AnimatorControllerParameterType.Bool);
            EnsureParameter(controller, "JumpState", AnimatorControllerParameterType.Int);
            EnsureParameter(controller, "LookPitch", AnimatorControllerParameterType.Float);
        }

        private static void EnsureParameter(
            AnimatorController controller,
            string name,
            AnimatorControllerParameterType type)
        {
            for (var i = 0; i < controller.parameters.Length; i++)
            {
                if (controller.parameters[i].name == name)
                {
                    return;
                }
            }

            controller.AddParameter(name, type);
        }

        private static void BuildLocomotionLayer(AnimatorController controller, LocomotionClips clips)
        {
            const float transitionDuration = 0.18f;

            var layer = controller.layers[0];
            var stateMachine = layer.stateMachine;
            stateMachine.states = new ChildAnimatorState[0];
            stateMachine.anyStateTransitions = new AnimatorStateTransition[0];

            var idle = clips.Idle ?? clips.RunForward ?? clips.Run;
            var walkMotion = BuildWalkMotion(controller, clips, clips.WalkForward ?? clips.Walk ?? idle);
            var sprintClip = clips.Sprint ?? clips.RunForward ?? clips.Run ?? idle;

            var blendTree = BuildIdleWalkLocomotionBlend(controller, idle, walkMotion);

            var locomotionState = stateMachine.AddState("Locomotion", new Vector3(300f, 0f, 0f));
            locomotionState.motion = blendTree;
            stateMachine.defaultState = locomotionState;

            if (sprintClip != null)
            {
                var sprintState = stateMachine.AddState("Sprint", new Vector3(520f, -120f, 0f));
                sprintState.motion = sprintClip;
                var toSprint = locomotionState.AddTransition(sprintState);
                toSprint.AddCondition(AnimatorConditionMode.If, 0f, "Sprinting");
                toSprint.AddCondition(AnimatorConditionMode.Greater, SprintStateSpeedMin, "Speed");
                toSprint.hasExitTime = false;
                toSprint.duration = transitionDuration;

                var fromSprint = sprintState.AddTransition(locomotionState);
                fromSprint.AddCondition(AnimatorConditionMode.IfNot, 0f, "Sprinting");
                fromSprint.hasExitTime = false;
                fromSprint.duration = transitionDuration;

                var fromSprintSlow = sprintState.AddTransition(locomotionState);
                fromSprintSlow.AddCondition(AnimatorConditionMode.Less, 0.42f, "Speed");
                fromSprintSlow.hasExitTime = false;
                fromSprintSlow.duration = transitionDuration;
            }

            var jumpClip = clips.Jump;
            var jumpRunClip = clips.JumpWhileRunning ?? clips.Jump;
            if (jumpClip != null || jumpRunClip != null)
            {
                var jumpState = stateMachine.AddState("Jump", new Vector3(520f, -80f, 0f));
                jumpState.motion = jumpClip ?? jumpRunClip;

                if (jumpRunClip != null && jumpRunClip != jumpClip)
                {
                    var jumpRunState = stateMachine.AddState("JumpRun", new Vector3(520f, -200f, 0f));
                    jumpRunState.motion = jumpRunClip;
                    var toJumpRun = stateMachine.AddAnyStateTransition(jumpRunState);
                    toJumpRun.AddCondition(AnimatorConditionMode.Equals, 1f, "JumpState");
                    toJumpRun.AddCondition(AnimatorConditionMode.Greater, SprintStateSpeedMin, "Speed");
                    toJumpRun.hasExitTime = false;
                    toJumpRun.duration = transitionDuration;
                    toJumpRun.canTransitionToSelf = false;

                    var jumpRunBack = jumpRunState.AddTransition(locomotionState);
                    jumpRunBack.AddCondition(AnimatorConditionMode.Equals, 0f, "JumpState");
                    jumpRunBack.hasExitTime = false;
                    jumpRunBack.duration = transitionDuration;
                }

                if (jumpClip != null)
                {
                    var jumpTransition = stateMachine.AddAnyStateTransition(jumpState);
                    jumpTransition.AddCondition(AnimatorConditionMode.Equals, 1f, "JumpState");
                    if (jumpRunClip != null && jumpRunClip != jumpClip)
                    {
                        jumpTransition.AddCondition(AnimatorConditionMode.Less, SprintStateSpeedMin, "Speed");
                    }

                    jumpTransition.hasExitTime = false;
                    jumpTransition.duration = transitionDuration;
                    jumpTransition.canTransitionToSelf = false;

                    var jumpBack = jumpState.AddTransition(locomotionState);
                    jumpBack.AddCondition(AnimatorConditionMode.Equals, 0f, "JumpState");
                    jumpBack.hasExitTime = false;
                    jumpBack.duration = transitionDuration;
                }
            }

            if (clips.Fall != null)
            {
                var fallState = stateMachine.AddState("Fall", new Vector3(520f, 120f, 0f));
                fallState.motion = clips.Fall;
                var toFall = stateMachine.AddAnyStateTransition(fallState);
                toFall.AddCondition(AnimatorConditionMode.Equals, 2f, "JumpState");
                toFall.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
                toFall.hasExitTime = false;
                toFall.duration = transitionDuration;
                toFall.canTransitionToSelf = true;

                var fallBack = fallState.AddTransition(locomotionState);
                fallBack.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
                fallBack.hasExitTime = false;
                fallBack.duration = transitionDuration;
            }

            if (clips.CrouchIdle != null || clips.CrouchWalk != null)
            {
                var crouchMotion = BuildCrouchLocomotionMotion(controller, clips);
                if (crouchMotion != null)
                {
                    var crouchState = stateMachine.AddState("Crouch", new Vector3(520f, 80f, 0f));
                    crouchState.motion = crouchMotion;
                    var toCrouch = locomotionState.AddTransition(crouchState);
                    toCrouch.AddCondition(AnimatorConditionMode.If, 0f, "Crouching");
                    toCrouch.hasExitTime = false;
                    toCrouch.duration = transitionDuration;

                    var fromCrouch = crouchState.AddTransition(locomotionState);
                    fromCrouch.AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouching");
                    fromCrouch.hasExitTime = false;
                    fromCrouch.duration = transitionDuration;
                }
            }
        }

        private static void BuildRemoteLocomotionLayer(AnimatorController controller, LocomotionClips clips)
        {
            const float transitionDuration = 0.18f;
            const float walkJumpSpeedMin = 0.2f;
            const float sprintJumpSpeedMin = SprintStateSpeedMin;

            var layer = controller.layers[0];
            var stateMachine = layer.stateMachine;
            stateMachine.states = new ChildAnimatorState[0];
            stateMachine.anyStateTransitions = new AnimatorStateTransition[0];

            var idle = clips.Idle ?? clips.RunForward ?? clips.Run;
            var walkMotion = BuildWalkMotion(controller, clips, clips.WalkForward ?? clips.Walk ?? idle);
            var sprintMotion = BuildSprintDirectionMotion(controller, clips)
                ?? clips.Sprint
                ?? clips.RunForward
                ?? clips.Run
                ?? idle;

            var blendTree = BuildIdleWalkLocomotionBlend(controller, idle, walkMotion);

            var locomotionState = stateMachine.AddState("Locomotion", new Vector3(300f, 0f, 0f));
            locomotionState.motion = blendTree;
            stateMachine.defaultState = locomotionState;

            if (sprintMotion != null)
            {
                var sprintState = stateMachine.AddState("Sprint", new Vector3(520f, -120f, 0f));
                sprintState.motion = sprintMotion;
                var toSprint = locomotionState.AddTransition(sprintState);
                toSprint.AddCondition(AnimatorConditionMode.If, 0f, "Sprinting");
                toSprint.AddCondition(AnimatorConditionMode.Greater, SprintStateSpeedMin, "Speed");
                toSprint.hasExitTime = false;
                toSprint.duration = transitionDuration;

                var fromSprint = sprintState.AddTransition(locomotionState);
                fromSprint.AddCondition(AnimatorConditionMode.IfNot, 0f, "Sprinting");
                fromSprint.hasExitTime = false;
                fromSprint.duration = transitionDuration;

                var fromSprintSlow = sprintState.AddTransition(locomotionState);
                fromSprintSlow.AddCondition(AnimatorConditionMode.Less, 0.42f, "Speed");
                fromSprintSlow.hasExitTime = false;
                fromSprintSlow.duration = transitionDuration;
            }

            var jumpClip = clips.Jump;
            var walkJumpMotion = BuildWalkJumpMotion(controller, clips);
            var jumpRunLeftClip = clips.JumpWhileRunning ?? clips.Jump;
            var jumpRunRightClip = clips.JumpRunRight ?? jumpRunLeftClip;

            if (jumpRunRightClip != null && jumpRunRightClip != jumpRunLeftClip)
            {
                var jumpRunRightState = stateMachine.AddState("JumpRunRight", new Vector3(760f, -200f, 0f));
                jumpRunRightState.motion = jumpRunRightClip;
                var toJumpRunRight = stateMachine.AddAnyStateTransition(jumpRunRightState);
                toJumpRunRight.AddCondition(AnimatorConditionMode.Equals, 1f, "JumpState");
                toJumpRunRight.AddCondition(AnimatorConditionMode.Greater, sprintJumpSpeedMin - 0.01f, "Speed");
                toJumpRunRight.AddCondition(AnimatorConditionMode.Greater, 0.05f, "MoveX");
                toJumpRunRight.hasExitTime = false;
                toJumpRunRight.duration = transitionDuration;
                toJumpRunRight.canTransitionToSelf = false;

                var jumpRunRightBack = jumpRunRightState.AddTransition(locomotionState);
                jumpRunRightBack.AddCondition(AnimatorConditionMode.Equals, 0f, "JumpState");
                jumpRunRightBack.hasExitTime = false;
                jumpRunRightBack.duration = transitionDuration;
            }

            if (jumpRunLeftClip != null)
            {
                var jumpRunState = stateMachine.AddState("JumpRun", new Vector3(520f, -200f, 0f));
                jumpRunState.motion = jumpRunLeftClip;
                var toJumpRun = stateMachine.AddAnyStateTransition(jumpRunState);
                toJumpRun.AddCondition(AnimatorConditionMode.Equals, 1f, "JumpState");
                toJumpRun.AddCondition(AnimatorConditionMode.Greater, sprintJumpSpeedMin - 0.01f, "Speed");
                if (jumpRunRightClip != null && jumpRunRightClip != jumpRunLeftClip)
                {
                    toJumpRun.AddCondition(AnimatorConditionMode.Less, 0.06f, "MoveX");
                }

                toJumpRun.hasExitTime = false;
                toJumpRun.duration = transitionDuration;
                toJumpRun.canTransitionToSelf = false;

                var jumpRunBack = jumpRunState.AddTransition(locomotionState);
                jumpRunBack.AddCondition(AnimatorConditionMode.Equals, 0f, "JumpState");
                jumpRunBack.hasExitTime = false;
                jumpRunBack.duration = transitionDuration;
            }

            if (walkJumpMotion != null)
            {
                var jumpWalkState = stateMachine.AddState("JumpWalk", new Vector3(520f, -140f, 0f));
                jumpWalkState.motion = walkJumpMotion;
                var toJumpWalk = stateMachine.AddAnyStateTransition(jumpWalkState);
                toJumpWalk.AddCondition(AnimatorConditionMode.Equals, 1f, "JumpState");
                toJumpWalk.AddCondition(AnimatorConditionMode.Greater, walkJumpSpeedMin - 0.01f, "Speed");
                toJumpWalk.AddCondition(AnimatorConditionMode.Less, sprintJumpSpeedMin, "Speed");
                toJumpWalk.hasExitTime = false;
                toJumpWalk.duration = transitionDuration;
                toJumpWalk.canTransitionToSelf = false;

                var jumpWalkBack = jumpWalkState.AddTransition(locomotionState);
                jumpWalkBack.AddCondition(AnimatorConditionMode.Equals, 0f, "JumpState");
                jumpWalkBack.hasExitTime = false;
                jumpWalkBack.duration = transitionDuration;
            }

            if (jumpClip != null)
            {
                var jumpState = stateMachine.AddState("Jump", new Vector3(520f, -80f, 0f));
                jumpState.motion = jumpClip;

                var jumpTransition = stateMachine.AddAnyStateTransition(jumpState);
                jumpTransition.AddCondition(AnimatorConditionMode.Equals, 1f, "JumpState");
                if (walkJumpMotion != null || jumpRunLeftClip != null)
                {
                    jumpTransition.AddCondition(AnimatorConditionMode.Less, walkJumpSpeedMin, "Speed");
                }

                jumpTransition.hasExitTime = false;
                jumpTransition.duration = transitionDuration;
                jumpTransition.canTransitionToSelf = false;

                var jumpBack = jumpState.AddTransition(locomotionState);
                jumpBack.AddCondition(AnimatorConditionMode.Equals, 0f, "JumpState");
                jumpBack.hasExitTime = false;
                jumpBack.duration = transitionDuration;
            }

            if (clips.Fall != null)
            {
                var fallState = stateMachine.AddState("Fall", new Vector3(520f, 120f, 0f));
                fallState.motion = clips.Fall;
                var toFall = stateMachine.AddAnyStateTransition(fallState);
                toFall.AddCondition(AnimatorConditionMode.Equals, 2f, "JumpState");
                toFall.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
                toFall.hasExitTime = false;
                toFall.duration = transitionDuration;
                toFall.canTransitionToSelf = true;

                var fallBack = fallState.AddTransition(locomotionState);
                fallBack.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
                fallBack.hasExitTime = false;
                fallBack.duration = transitionDuration;
            }

            if (clips.CrouchIdle != null || clips.CrouchWalk != null)
            {
                var crouchMotion = BuildCrouchLocomotionMotion(controller, clips);
                if (crouchMotion != null)
                {
                    var crouchState = stateMachine.AddState("Crouch", new Vector3(520f, 80f, 0f));
                    crouchState.motion = crouchMotion;
                    var toCrouch = locomotionState.AddTransition(crouchState);
                    toCrouch.AddCondition(AnimatorConditionMode.If, 0f, "Crouching");
                    toCrouch.hasExitTime = false;
                    toCrouch.duration = transitionDuration;

                    var fromCrouch = crouchState.AddTransition(locomotionState);
                    fromCrouch.AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouching");
                    fromCrouch.hasExitTime = false;
                    fromCrouch.duration = transitionDuration;
                }
            }
        }

        private static void ApplyLegsOnlyAvatarMasks(AnimatorController controller, LocomotionClips clips)
        {
            EnsureRemoteLocomotionAvatarMaskAssets();

            var legsMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(LegsOnlyMaskPath);
            var torsoMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(TorsoOnlyMaskPath);
            var armsMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(ArmsOnlyMaskPath);
            if (legsMask == null || torsoMask == null || armsMask == null)
            {
                Debug.LogWarning("[SyntyAnimationSetup] Locomotion avatar masks are missing.");
                return;
            }

            EnsureAnimatorLayerCount(controller, 3);

            var layers = controller.layers;
            layers[0].name = "Legs Locomotion";
            layers[0].avatarMask = legsMask;
            layers[0].defaultWeight = 1f;
            layers[0].blendingMode = AnimatorLayerBlendingMode.Override;

            var torsoClip = clips.Idle ?? clips.Walk ?? clips.Run;
            var armsClip = clips.ArmsIdle ?? LoadArmsIdleClip() ?? torsoClip;
            BuildTorsoOverrideLayer(controller, ref layers, 1, torsoMask, torsoClip, clips.CrouchIdle, "Torso");
            BuildOverrideIdleLayer(controller, ref layers, 2, armsMask, armsClip, "Arms Idle");

            controller.layers = layers;
        }

        private static void EnsureAnimatorLayerCount(AnimatorController controller, int targetCount)
        {
            var layers = controller.layers;
            while (layers.Length < targetCount)
            {
                var stateMachine = new AnimatorStateMachine
                {
                    name = $"Layer {layers.Length}",
                    hideFlags = HideFlags.HideInHierarchy
                };
                AssetDatabase.AddObjectToAsset(stateMachine, controller);

                var expanded = new AnimatorControllerLayer[layers.Length + 1];
                for (var i = 0; i < layers.Length; i++)
                {
                    expanded[i] = layers[i];
                }

                expanded[layers.Length] = new AnimatorControllerLayer
                {
                    name = $"Layer {layers.Length}",
                    defaultWeight = 1f,
                    blendingMode = AnimatorLayerBlendingMode.Override,
                    syncedLayerIndex = -1,
                    iKPass = false,
                    stateMachine = stateMachine
                };
                layers = expanded;
            }

            controller.layers = layers;
        }

        private static void BuildOverrideIdleLayer(
            AnimatorController controller,
            ref AnimatorControllerLayer[] layers,
            int layerIndex,
            AvatarMask mask,
            AnimationClip idleClip,
            string layerName)
        {
            if (layerIndex < 0 || layerIndex >= layers.Length)
            {
                return;
            }

            layers[layerIndex].name = layerName;
            layers[layerIndex].avatarMask = mask;
            layers[layerIndex].defaultWeight = 1f;
            layers[layerIndex].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[layerIndex].syncedLayerIndex = -1;
            layers[layerIndex].iKPass = false;

            var stateMachine = layers[layerIndex].stateMachine;
            if (stateMachine == null)
            {
                stateMachine = new AnimatorStateMachine
                {
                    name = layerName,
                    hideFlags = HideFlags.HideInHierarchy
                };
                AssetDatabase.AddObjectToAsset(stateMachine, controller);
                layers[layerIndex].stateMachine = stateMachine;
            }

            stateMachine.states = new ChildAnimatorState[0];
            stateMachine.anyStateTransitions = new AnimatorStateTransition[0];
            if (idleClip == null)
            {
                return;
            }

            var idleState = stateMachine.AddState("Idle", new Vector3(300f, layerIndex * 80f, 0f));
            idleState.motion = idleClip;
            stateMachine.defaultState = idleState;
        }

        private static void BuildTorsoOverrideLayer(
            AnimatorController controller,
            ref AnimatorControllerLayer[] layers,
            int layerIndex,
            AvatarMask mask,
            AnimationClip standingIdle,
            AnimationClip crouchIdle,
            string layerName)
        {
            if (crouchIdle == null || standingIdle == null)
            {
                BuildOverrideIdleLayer(controller, ref layers, layerIndex, mask, standingIdle ?? crouchIdle, layerName);
                return;
            }

            const float transitionDuration = 0.16f;

            if (layerIndex < 0 || layerIndex >= layers.Length)
            {
                return;
            }

            layers[layerIndex].name = layerName;
            layers[layerIndex].avatarMask = mask;
            layers[layerIndex].defaultWeight = 1f;
            layers[layerIndex].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[layerIndex].syncedLayerIndex = -1;
            layers[layerIndex].iKPass = false;

            var stateMachine = layers[layerIndex].stateMachine;
            if (stateMachine == null)
            {
                stateMachine = new AnimatorStateMachine
                {
                    name = layerName,
                    hideFlags = HideFlags.HideInHierarchy
                };
                AssetDatabase.AddObjectToAsset(stateMachine, controller);
                layers[layerIndex].stateMachine = stateMachine;
            }

            stateMachine.states = new ChildAnimatorState[0];
            stateMachine.anyStateTransitions = new AnimatorStateTransition[0];

            var standState = stateMachine.AddState("StandIdle", new Vector3(280f, layerIndex * 80f, 0f));
            standState.motion = standingIdle;
            var crouchState = stateMachine.AddState("CrouchIdle", new Vector3(520f, layerIndex * 80f, 0f));
            crouchState.motion = crouchIdle;
            stateMachine.defaultState = standState;

            var toCrouch = standState.AddTransition(crouchState);
            toCrouch.AddCondition(AnimatorConditionMode.If, 0f, "Crouching");
            toCrouch.hasExitTime = false;
            toCrouch.duration = transitionDuration;

            var fromCrouch = crouchState.AddTransition(standState);
            fromCrouch.AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouching");
            fromCrouch.hasExitTime = false;
            fromCrouch.duration = transitionDuration;
        }

        private static void EnsureRemoteLocomotionAvatarMaskAssets()
        {
            EnsureFolder("Assets/Prefabs/Player");
            EnsureAvatarMaskAsset(LegsOnlyMaskPath, ApplyLegsOnlyMaskSettings);
            EnsureAvatarMaskAsset(TorsoOnlyMaskPath, ApplyTorsoOnlyMaskSettings);
            EnsureAvatarMaskAsset(ArmsOnlyMaskPath, ApplyArmsOnlyMaskSettings);
            EnsureAvatarMaskAsset(BodyNoArmsMaskPath, ApplyBodyNoArmsMaskSettings);
            EnsureAvatarMaskAsset(RemoteArmsIdleMaskPath, ApplyRemoteArmsIdleMaskSettings);
            EnsureAvatarMaskAsset(UpperBodyOnlyMaskPath, ApplyUpperBodyOnlyMaskSettings);
            EnsureAvatarMaskAsset(RemoteForearmsOnlyMaskPath, ApplyRemoteForearmsOnlyMaskSettings);
        }

        private static void EnsureAvatarMaskAsset(string path, System.Action<AvatarMask> applySettings)
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (mask == null)
            {
                mask = new AvatarMask();
                applySettings(mask);
                AssetDatabase.CreateAsset(mask, path);
                return;
            }

            applySettings(mask);
            EditorUtility.SetDirty(mask);
        }

        private static void ApplyLegsOnlyMaskSettings(AvatarMask mask)
        {
            for (var i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                mask.SetHumanoidBodyPartActive(part, IsLegBodyPart(part));
            }
        }

        private static void ApplyUpperBodyOnlyMaskSettings(AvatarMask mask)
        {
            for (var i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                mask.SetHumanoidBodyPartActive(part, !IsLegBodyPart(part));
            }
        }

        private static void ApplyTorsoOnlyMaskSettings(AvatarMask mask)
        {
            for (var i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                mask.SetHumanoidBodyPartActive(part, IsTorsoBodyPart(part));
            }
        }

        private static void ApplyArmsOnlyMaskSettings(AvatarMask mask)
        {
            mask.transformCount = 0;
            for (var i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                mask.SetHumanoidBodyPartActive(part, IsArmBodyPart(part));
            }
        }

        private static void ApplyBodyNoArmsMaskSettings(AvatarMask mask)
        {
            mask.transformCount = 0;
            for (var i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                mask.SetHumanoidBodyPartActive(part, !IsArmBodyPart(part));
            }
        }

        private static void ApplyRemoteArmsIdleMaskSettings(AvatarMask mask)
        {
            mask.transformCount = 0;
            for (var i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            }

            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);

            var shoulderPaths = CollectRemoteShoulderMaskTransformPaths();
            if (shoulderPaths.Count == 0)
            {
                return;
            }

            mask.transformCount = shoulderPaths.Count;
            for (var i = 0; i < shoulderPaths.Count; i++)
            {
                mask.SetTransformPath(i, shoulderPaths[i]);
                mask.SetTransformActive(i, true);
            }
        }

        private static List<string> CollectRemoteShoulderMaskTransformPaths()
        {
            var result = new List<string>();
            var avatarRoot = ResolveReferenceSyntyAvatarRoot(out var instanceRoot);
            if (avatarRoot == null)
            {
                return result;
            }

            try
            {
                var animator = avatarRoot.GetComponent<Animator>();
                if (animator == null || !animator.isHuman)
                {
                    return result;
                }

                var shoulderBones = new[]
                {
                    HumanBodyBones.LeftShoulder,
                    HumanBodyBones.RightShoulder,
                    HumanBodyBones.LeftUpperArm,
                    HumanBodyBones.RightUpperArm
                };

                for (var i = 0; i < shoulderBones.Length; i++)
                {
                    var bone = animator.GetBoneTransform(shoulderBones[i]);
                    if (bone == null)
                    {
                        continue;
                    }

                    AddForearmTransformPath(avatarRoot, bone, result);
                }

                result.Sort(System.StringComparer.Ordinal);
                return result;
            }
            finally
            {
                if (instanceRoot != null)
                {
                    Object.DestroyImmediate(instanceRoot);
                }
            }
        }

        private static readonly string[] RemoteForearmMaskIncludeTokens =
        {
            "Elbow",
            "LowerArm",
            "ForeArm",
            "Hand",
            "Finger",
            "Thumb",
            "Index",
            "Middle",
            "Ring",
            "Pinky",
            "Little"
        };

        private static readonly string[] RemoteForearmMaskExcludeTokens =
        {
            "Clavicle",
            "Shoulder",
            "UpperArm"
        };

        private static void ApplyRemoteForearmsOnlyMaskSettings(AvatarMask mask)
        {
            for (var i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            }

            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);

            var paths = CollectRemoteForearmMaskTransformPaths();
            if (paths.Count == 0)
            {
                mask.transformCount = 0;
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
                Debug.LogWarning(
                    "[SyntyAnimationSetup] Forearm avatar mask fell back to full arm humanoid parts. " +
                    "Re-run Rebuild Animation Controller after PlayerCleanRemote is available.");
                return;
            }

            mask.transformCount = paths.Count;
            for (var i = 0; i < paths.Count; i++)
            {
                mask.SetTransformPath(i, paths[i]);
                mask.SetTransformActive(i, true);
            }
        }

        private static List<string> CollectRemoteForearmMaskTransformPaths()
        {
            var result = new List<string>();
            var avatarRoot = ResolveReferenceSyntyAvatarRoot(out var instanceRoot);
            if (avatarRoot == null)
            {
                return result;
            }

            try
            {
                var animator = avatarRoot.GetComponent<Animator>();
                if (animator != null && animator.isHuman)
                {
                    CollectHumanoidForearmPaths(avatarRoot, animator, result);
                }

                if (result.Count == 0)
                {
                    CollectNameBasedForearmPaths(avatarRoot, result);
                }

                result.Sort(System.StringComparer.Ordinal);
                return result;
            }
            finally
            {
                if (instanceRoot != null)
                {
                    Object.DestroyImmediate(instanceRoot);
                }
            }
        }

        private static void CollectHumanoidForearmPaths(Transform avatarRoot, Animator animator, List<string> result)
        {
            var rootBones = new[]
            {
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.LeftHand,
                HumanBodyBones.RightHand
            };

            for (var i = 0; i < rootBones.Length; i++)
            {
                var bone = animator.GetBoneTransform(rootBones[i]);
                if (bone == null)
                {
                    continue;
                }

                AddForearmTransformPath(avatarRoot, bone, result);
                AddForearmDescendantPaths(avatarRoot, bone, result);
            }
        }

        private static void CollectNameBasedForearmPaths(Transform avatarRoot, List<string> result)
        {
            var allTransforms = avatarRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var bone = allTransforms[i];
                if (bone == null || bone == avatarRoot)
                {
                    continue;
                }

                if (!ShouldIncludeRemoteForearmMaskBone(bone.name))
                {
                    continue;
                }

                AddForearmTransformPath(avatarRoot, bone, result);
            }
        }

        private static void AddForearmDescendantPaths(Transform avatarRoot, Transform rootBone, List<string> result)
        {
            var children = rootBone.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child == null || child == rootBone)
                {
                    continue;
                }

                if (ShouldExcludeRemoteForearmMaskPath(child.name))
                {
                    continue;
                }

                AddForearmTransformPath(avatarRoot, child, result);
            }
        }

        private static void AddForearmTransformPath(Transform avatarRoot, Transform bone, List<string> result)
        {
            if (ShouldExcludeRemoteForearmMaskPath(bone.name))
            {
                return;
            }

            var path = AnimationUtility.CalculateTransformPath(bone, avatarRoot);
            if (string.IsNullOrEmpty(path) || result.Contains(path))
            {
                return;
            }

            result.Add(path);
        }

        private static bool ShouldExcludeRemoteForearmMaskPath(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return true;
            }

            return boneName.IndexOf("Weapon", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("Target", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("Anchor", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("Mag", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Transform ResolveReferenceSyntyAvatarRoot(out GameObject instanceRoot)
        {
            instanceRoot = null;
            var playerRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRemotePrefabPath);
            if (playerRoot == null)
            {
                playerRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            }

            if (playerRoot == null)
            {
                return null;
            }

            var instance = PrefabUtility.InstantiatePrefab(playerRoot) as GameObject;
            if (instance == null)
            {
                return null;
            }

            instanceRoot = instance;
            var thirdPersonBody = FindChild(instance.transform, "ThirdPersonBody");
            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (syntyVisual != null)
            {
                var animator = syntyVisual.GetComponent<Animator>();
                if (animator != null)
                {
                    var avatar = EnsureSyntyHumanoidAvatar(syntyVisual);
                    if (avatar != null && avatar.isValid)
                    {
                        animator.avatar = avatar;
                    }
                }
            }

            return syntyVisual;
        }

        private static bool ShouldIncludeRemoteForearmMaskBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return false;
            }

            for (var i = 0; i < RemoteForearmMaskExcludeTokens.Length; i++)
            {
                if (boneName.IndexOf(RemoteForearmMaskExcludeTokens[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            for (var i = 0; i < RemoteForearmMaskIncludeTokens.Length; i++)
            {
                if (boneName.IndexOf(RemoteForearmMaskIncludeTokens[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTorsoBodyPart(AvatarMaskBodyPart part)
        {
            return part == AvatarMaskBodyPart.Root ||
                   part == AvatarMaskBodyPart.Body ||
                   part == AvatarMaskBodyPart.Head;
        }

        private static bool IsArmBodyPart(AvatarMaskBodyPart part)
        {
            return part == AvatarMaskBodyPart.LeftArm ||
                   part == AvatarMaskBodyPart.RightArm ||
                   part == AvatarMaskBodyPart.LeftFingers ||
                   part == AvatarMaskBodyPart.RightFingers;
        }

        private static bool IsLegBodyPart(AvatarMaskBodyPart part)
        {
            // Root/hips must stay on the upper-body idle layer or run clips tilt the torso forward.
            return part == AvatarMaskBodyPart.LeftLeg ||
                   part == AvatarMaskBodyPart.RightLeg;
        }

        private static BlendTree BuildIdleWalkLocomotionBlend(
            AnimatorController controller,
            Motion idle,
            Motion walkMotion)
        {
            var blendTree = new BlendTree
            {
                name = "LocomotionBlend",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false
            };
            Add1DChild(blendTree, idle, 0f);
            Add1DChild(blendTree, walkMotion ?? idle, WalkLocomotionSpeedThreshold);
            AssetDatabase.AddObjectToAsset(blendTree, controller);
            return blendTree;
        }

        private static Motion BuildWalkMotion(AnimatorController controller, LocomotionClips clips, AnimationClip walkForward)
        {
            walkForward ??= clips.Walk ?? clips.RunForward ?? clips.Idle;
            var walkBackward = clips.WalkBackward ?? clips.RunBackward;
            var walkLeft = clips.WalkLeft;
            var walkRight = clips.WalkRight;
            if (walkForward == null)
            {
                return null;
            }

            if (walkLeft == null && walkRight == null && walkBackward == null)
            {
                return walkForward;
            }

            var walkTree = new BlendTree
            {
                name = "WalkDirectionBlend",
                blendType = BlendTreeType.SimpleDirectional2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveY",
                useAutomaticThresholds = false
            };

            Add2DChild(walkTree, walkForward, new Vector2(0f, 1f));
            if (walkBackward != null)
            {
                Add2DChild(walkTree, walkBackward, new Vector2(0f, -1f));
            }
            else
            {
                Add2DChild(walkTree, walkForward, new Vector2(0f, -1f), timeScale: -1f);
            }

            if (walkLeft != null)
            {
                Add2DChild(walkTree, walkLeft, new Vector2(-1f, 0f));
            }

            if (walkRight != null)
            {
                Add2DChild(walkTree, walkRight, new Vector2(1f, 0f));
            }
            else if (walkLeft != null)
            {
                Add2DChild(walkTree, walkLeft, new Vector2(1f, 0f), mirror: true);
            }

            if (clips.WalkForwardDiagonalLeft != null)
            {
                Add2DChild(walkTree, clips.WalkForwardDiagonalLeft, new Vector2(-1f, 1f));
            }

            if (clips.WalkForwardDiagonalRight != null)
            {
                Add2DChild(walkTree, clips.WalkForwardDiagonalRight, new Vector2(1f, 1f));
            }

            if (clips.WalkBackwardDiagonalLeft != null)
            {
                Add2DChild(walkTree, clips.WalkBackwardDiagonalLeft, new Vector2(-1f, -1f));
            }

            if (clips.WalkBackwardDiagonalRight != null)
            {
                Add2DChild(walkTree, clips.WalkBackwardDiagonalRight, new Vector2(1f, -1f));
            }

            AssetDatabase.AddObjectToAsset(walkTree, controller);
            return walkTree;
        }

        private static Motion BuildRunDirectionMotion(AnimatorController controller, LocomotionClips clips)
        {
            var forward = clips.RunForward ?? clips.Run;
            if (forward == null)
            {
                return null;
            }

            var backward = clips.RunBackward;
            var left = clips.RunLeft;
            var right = clips.RunRight;
            var backLeft = clips.RunBackwardLeft;
            var backRight = clips.RunBackwardRight;
            if (backward == null && left == null && right == null)
            {
                return forward;
            }

            var runTree = new BlendTree
            {
                name = "RunDirectionBlend",
                blendType = BlendTreeType.SimpleDirectional2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveY",
                useAutomaticThresholds = false
            };

            Add2DChild(runTree, forward, new Vector2(0f, 1f));
            if (backward != null)
            {
                Add2DChild(runTree, backward, new Vector2(0f, -1f));
            }
            else
            {
                Add2DChild(runTree, forward, new Vector2(0f, -1f), timeScale: -1f);
            }

            if (left != null)
            {
                Add2DChild(runTree, left, new Vector2(-1f, 0f));
            }

            if (right != null)
            {
                Add2DChild(runTree, right, new Vector2(1f, 0f));
            }

            if (backLeft != null)
            {
                Add2DChild(runTree, backLeft, new Vector2(-1f, -1f));
            }

            if (backRight != null)
            {
                Add2DChild(runTree, backRight, new Vector2(1f, -1f));
            }

            if (clips.RunForwardDiagonalLeft != null)
            {
                Add2DChild(runTree, clips.RunForwardDiagonalLeft, new Vector2(-1f, 1f));
            }

            if (clips.RunForwardDiagonalRight != null)
            {
                Add2DChild(runTree, clips.RunForwardDiagonalRight, new Vector2(1f, 1f));
            }

            AssetDatabase.AddObjectToAsset(runTree, controller);
            return runTree;
        }

        private static Motion BuildSprintDirectionMotion(AnimatorController controller, LocomotionClips clips)
        {
            var forward = clips.Sprint ?? clips.RunForward ?? clips.Run;
            if (forward == null)
            {
                return null;
            }

            var hasDirectionalSprint = clips.SprintDiagonalLeft != null || clips.SprintDiagonalRight != null;
            var hasRunStrafe = clips.RunLeft != null || clips.RunRight != null || clips.RunBackward != null;
            if (!hasDirectionalSprint && !hasRunStrafe)
            {
                return forward;
            }

            var sprintTree = new BlendTree
            {
                name = "SprintDirectionBlend",
                blendType = BlendTreeType.SimpleDirectional2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveY",
                useAutomaticThresholds = false
            };

            Add2DChild(sprintTree, forward, new Vector2(0f, 1f));
            AddDirectionalChild(
                sprintTree,
                clips.SprintDiagonalLeft,
                clips.RunForwardDiagonalLeft,
                new Vector2(-1f, 1f));
            AddDirectionalChild(
                sprintTree,
                clips.SprintDiagonalRight,
                clips.RunForwardDiagonalRight,
                new Vector2(1f, 1f));

            if (clips.RunLeft != null)
            {
                Add2DChild(sprintTree, clips.RunLeft, new Vector2(-1f, 0f));
            }

            if (clips.RunRight != null)
            {
                Add2DChild(sprintTree, clips.RunRight, new Vector2(1f, 0f));
            }

            if (clips.RunBackward != null)
            {
                Add2DChild(sprintTree, clips.RunBackward, new Vector2(0f, -1f));
            }
            else if (forward != null)
            {
                Add2DChild(sprintTree, forward, new Vector2(0f, -1f), timeScale: -1f);
            }

            AssetDatabase.AddObjectToAsset(sprintTree, controller);
            return sprintTree;
        }

        private static Motion BuildWalkJumpMotion(AnimatorController controller, LocomotionClips clips)
        {
            var left = clips.WalkJumpLeft;
            var right = clips.WalkJumpRight;
            if (left == null && right == null)
            {
                return null;
            }

            if (left != null && right == null)
            {
                return left;
            }

            if (right != null && left == null)
            {
                return right;
            }

            var jumpTree = new BlendTree
            {
                name = "WalkJumpBlend",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "MoveX",
                useAutomaticThresholds = false
            };
            Add1DChild(jumpTree, left, -1f);
            Add1DChild(jumpTree, right, 1f);
            AssetDatabase.AddObjectToAsset(jumpTree, controller);
            return jumpTree;
        }

        private static Motion BuildCrouchWalkDirectionMotion(AnimatorController controller, LocomotionClips clips)
        {
            var crouchForward = clips.CrouchWalk;
            var crouchBackward = clips.CrouchWalkBackward;
            var crouchLeft = clips.CrouchWalkLeft;
            var crouchRight = clips.CrouchWalkRight;
            if (crouchForward == null)
            {
                return null;
            }

            if (crouchLeft == null && crouchRight == null && crouchBackward == null &&
                clips.CrouchWalkForwardDiagonalLeft == null && clips.CrouchWalkForwardDiagonalRight == null &&
                clips.CrouchWalkBackwardDiagonalLeft == null && clips.CrouchWalkBackwardDiagonalRight == null)
            {
                return crouchForward;
            }

            var crouchTree = new BlendTree
            {
                name = "CrouchWalkDirectionBlend",
                blendType = BlendTreeType.SimpleDirectional2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveY",
                useAutomaticThresholds = false
            };

            Add2DChild(crouchTree, crouchForward, new Vector2(0f, 1f));
            if (crouchBackward != null)
            {
                Add2DChild(crouchTree, crouchBackward, new Vector2(0f, -1f));
            }
            else
            {
                Add2DChild(crouchTree, crouchForward, new Vector2(0f, -1f), timeScale: -1f);
            }

            if (crouchLeft != null)
            {
                Add2DChild(crouchTree, crouchLeft, new Vector2(-1f, 0f));
            }

            if (crouchRight != null)
            {
                Add2DChild(crouchTree, crouchRight, new Vector2(1f, 0f));
            }
            else if (crouchLeft != null)
            {
                Add2DChild(crouchTree, crouchLeft, new Vector2(1f, 0f), mirror: true);
            }

            if (clips.CrouchWalkForwardDiagonalLeft != null)
            {
                Add2DChild(crouchTree, clips.CrouchWalkForwardDiagonalLeft, new Vector2(-1f, 1f));
            }

            if (clips.CrouchWalkForwardDiagonalRight != null)
            {
                Add2DChild(crouchTree, clips.CrouchWalkForwardDiagonalRight, new Vector2(1f, 1f));
            }

            if (clips.CrouchWalkBackwardDiagonalLeft != null)
            {
                Add2DChild(crouchTree, clips.CrouchWalkBackwardDiagonalLeft, new Vector2(-1f, -1f));
            }

            if (clips.CrouchWalkBackwardDiagonalRight != null)
            {
                Add2DChild(crouchTree, clips.CrouchWalkBackwardDiagonalRight, new Vector2(1f, -1f));
            }

            AssetDatabase.AddObjectToAsset(crouchTree, controller);
            return crouchTree;
        }

        private static Motion BuildCrouchRunDirectionMotion(AnimatorController controller, LocomotionClips clips)
        {
            var forward = clips.CrouchRunForward ?? clips.CrouchRun;
            if (forward == null)
            {
                return null;
            }

            var backward = clips.CrouchRunBackward;
            var left = clips.CrouchRunLeft;
            var right = clips.CrouchRunRight;
            if (backward == null && left == null && right == null &&
                clips.CrouchRunForwardDiagonalLeft == null && clips.CrouchRunForwardDiagonalRight == null &&
                clips.CrouchRunBackwardDiagonalLeft == null && clips.CrouchRunBackwardDiagonalRight == null)
            {
                return forward;
            }

            var runTree = new BlendTree
            {
                name = "CrouchRunDirectionBlend",
                blendType = BlendTreeType.SimpleDirectional2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveY",
                useAutomaticThresholds = false
            };

            Add2DChild(runTree, forward, new Vector2(0f, 1f));
            if (backward != null)
            {
                Add2DChild(runTree, backward, new Vector2(0f, -1f));
            }
            else
            {
                Add2DChild(runTree, forward, new Vector2(0f, -1f), timeScale: -1f);
            }

            if (left != null)
            {
                Add2DChild(runTree, left, new Vector2(-1f, 0f));
            }

            if (right != null)
            {
                Add2DChild(runTree, right, new Vector2(1f, 0f));
            }

            if (clips.CrouchRunForwardDiagonalLeft != null)
            {
                Add2DChild(runTree, clips.CrouchRunForwardDiagonalLeft, new Vector2(-1f, 1f));
            }

            if (clips.CrouchRunForwardDiagonalRight != null)
            {
                Add2DChild(runTree, clips.CrouchRunForwardDiagonalRight, new Vector2(1f, 1f));
            }

            if (clips.CrouchRunBackwardDiagonalLeft != null)
            {
                Add2DChild(runTree, clips.CrouchRunBackwardDiagonalLeft, new Vector2(-1f, -1f));
            }

            if (clips.CrouchRunBackwardDiagonalRight != null)
            {
                Add2DChild(runTree, clips.CrouchRunBackwardDiagonalRight, new Vector2(1f, -1f));
            }

            AssetDatabase.AddObjectToAsset(runTree, controller);
            return runTree;
        }

        private static Motion BuildExtendedCrouchLocomotionMotion(AnimatorController controller, LocomotionClips clips)
        {
            var crouchIdle = clips.CrouchIdle;
            var walkDirectional = BuildCrouchWalkDirectionMotion(controller, clips);
            var runDirectional = BuildCrouchRunDirectionMotion(controller, clips);
            if (runDirectional == null)
            {
                return null;
            }

            if (crouchIdle == null && walkDirectional == null)
            {
                return runDirectional;
            }

            var speedTree = new BlendTree
            {
                name = "CrouchLocomotionExtendedBlend",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false
            };

            if (crouchIdle != null)
            {
                Add1DChild(speedTree, crouchIdle, 0f);
            }
            else if (walkDirectional != null)
            {
                Add1DChild(speedTree, walkDirectional, 0f);
            }
            else
            {
                Add1DChild(speedTree, runDirectional, 0f);
            }

            if (walkDirectional != null)
            {
                Add1DChild(speedTree, walkDirectional, 0.38f);
            }

            Add1DChild(speedTree, runDirectional, 0.72f);
            AssetDatabase.AddObjectToAsset(speedTree, controller);
            return speedTree;
        }

        private static Motion BuildCrouchLocomotionMotion(AnimatorController controller, LocomotionClips clips)
        {
            var crouchIdle = clips.CrouchIdle;
            var directional = BuildCrouchWalkDirectionMotion(controller, clips)
                ?? clips.CrouchWalk;

            if (crouchIdle == null)
            {
                return directional;
            }

            if (directional == null)
            {
                return crouchIdle;
            }

            var speedTree = new BlendTree
            {
                name = "CrouchLocomotionBlend",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false
            };
            Add1DChild(speedTree, crouchIdle, 0f);
            Add1DChild(speedTree, directional, 0.45f);
            AssetDatabase.AddObjectToAsset(speedTree, controller);
            return speedTree;
        }

        private static void AddDirectionalChild(
            BlendTree tree,
            Motion primary,
            Motion fallback,
            Vector2 position,
            float timeScale = 1f,
            bool mirror = false)
        {
            var motion = primary ?? fallback;
            if (motion == null)
            {
                return;
            }

            Add2DChild(tree, motion, position, mirror, timeScale);
        }

        private static void Add1DChild(BlendTree tree, Motion motion, float threshold)
        {
            tree.AddChild(motion, threshold);
        }

        private static void Add2DChild(
            BlendTree tree,
            Motion motion,
            Vector2 position,
            bool mirror = false,
            float timeScale = 1f)
        {
            if (motion == null)
            {
                return;
            }

            var children = tree.children;
            System.Array.Resize(ref children, children.Length + 1);
            children[children.Length - 1] = new ChildMotion
            {
                motion = motion,
                position = position,
                mirror = mirror,
                timeScale = timeScale
            };
            tree.children = children;
        }

        private static void TryAssignSpawnPrefab()
        {
            TryAssignSpawnPrefab(PlayerPrefabPath);
        }

        private static void TryAssignSpawnPrefab(string playerPrefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefabPath);
            if (prefab == null)
            {
                return;
            }

            var remotePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRemotePrefabCreator.TargetPrefabPath);

            var spawnManagers = Object.FindObjectsByType<PlayerSpawnManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < spawnManagers.Length; i++)
            {
                var manager = spawnManagers[i];
                if (manager == null)
                {
                    continue;
                }

                var serialized = new SerializedObject(manager);
                serialized.FindProperty("playerPrefab").objectReferenceValue = prefab;
                serialized.FindProperty("remotePlayerPrefab").objectReferenceValue =
                    remotePrefab != null ? remotePrefab : prefab;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(manager);
            }
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

        private static Transform FindChild(Transform root, string childName)
        {
            if (root == null)
            {
                return null;
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

        public sealed class LocomotionClips
        {
            public AnimationClip Idle;
            public AnimationClip Walk;
            public AnimationClip WalkForward;
            public AnimationClip WalkBackward;
            public AnimationClip WalkLeft;
            public AnimationClip WalkRight;
            public AnimationClip WalkForwardDiagonalLeft;
            public AnimationClip WalkForwardDiagonalRight;
            public AnimationClip WalkBackwardDiagonalLeft;
            public AnimationClip WalkBackwardDiagonalRight;
            public AnimationClip Run;
            public AnimationClip RunForward;
            public AnimationClip RunBackward;
            public AnimationClip RunLeft;
            public AnimationClip RunRight;
            public AnimationClip RunForwardDiagonalLeft;
            public AnimationClip RunForwardDiagonalRight;
            public AnimationClip RunBackwardLeft;
            public AnimationClip RunBackwardRight;
            public AnimationClip Sprint;
            public AnimationClip SprintDiagonalLeft;
            public AnimationClip SprintDiagonalRight;
            public AnimationClip Jump;
            public AnimationClip WalkJumpLeft;
            public AnimationClip WalkJumpRight;
            public AnimationClip JumpWhileRunning;
            public AnimationClip JumpRunRight;
            public AnimationClip Fall;
            public AnimationClip Land;
            public AnimationClip CrouchIdle;
            public AnimationClip CrouchWalk;
            public AnimationClip CrouchWalkBackward;
            public AnimationClip CrouchWalkLeft;
            public AnimationClip CrouchWalkRight;
            public AnimationClip CrouchWalkForwardDiagonalLeft;
            public AnimationClip CrouchWalkForwardDiagonalRight;
            public AnimationClip CrouchWalkBackwardDiagonalLeft;
            public AnimationClip CrouchWalkBackwardDiagonalRight;
            public AnimationClip CrouchRun;
            public AnimationClip CrouchRunForward;
            public AnimationClip CrouchRunBackward;
            public AnimationClip CrouchRunLeft;
            public AnimationClip CrouchRunRight;
            public AnimationClip CrouchRunForwardDiagonalLeft;
            public AnimationClip CrouchRunForwardDiagonalRight;
            public AnimationClip CrouchRunBackwardDiagonalLeft;
            public AnimationClip CrouchRunBackwardDiagonalRight;
            public AnimationClip ArmsIdle;
        }
    }
}
#endif
