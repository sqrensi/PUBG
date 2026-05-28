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
        private const string BodyNoArmsMaskPath = "Assets/Prefabs/Player/SyntyLocomotionBodyNoArms.mask";
        private const string ArmsOnlyMaskPath = "Assets/Prefabs/Player/SyntyLocomotionArmsOnly.mask";
        private const string PlayerPrefabPath = PlayerCleanPrefabCreator.TargetPrefabPath;
        private const string PlayerRemotePrefabPath = PlayerRemotePrefabCreator.TargetPrefabPath;
        private const string BusinessMalePlayerPrefabPath = "Assets/Prefabs/Player/PlayerSyntyBusinessMale.prefab";
        private const string Ch18PlayerPrefabPath = "Assets/Prefabs/Player/PlayerCh18.prefab";
        private const string AnimationsFolder = "Assets/Animations";
        private const string MixamoSourceFbxPath = "Assets/Animations/Idle.fbx";

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
                    "[SyntyAnimationSetup] No animation clips found. Add Mixamo/Synty FBX files to Assets/Animations, then run this menu again.");
            }
            else
            {
                CreateOrUpdateAnimatorController(clips);
                Debug.Log(
                    $"[SyntyAnimationSetup] Controller clips: Idle={NameOf(clips.Idle)}, Walk={NameOf(clips.Walk)}, Run={NameOf(clips.Run)}, Jump={NameOf(clips.Jump)}, Crouch={NameOf(clips.CrouchIdle)}");
            }

            ConfigurePlayerPrefab();
            TryAssignSpawnPrefab(PlayerPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[SyntyAnimationSetup] Synty animated player setup complete.");
        }

        [MenuItem("Shooter Prototype/Setup/Rebuild Animation Controller (Assets/Animations)")]
        public static void RebuildAnimationControllerOnly()
        {
            PrepareHumanoidRetargetingFromPlayerPrefab();

            var clips = FindLocomotionClips();
            if (clips.Idle == null)
            {
                Debug.LogError("[SyntyAnimationSetup] No clips found in Assets/Animations.");
                return;
            }

            CreateOrUpdateAnimatorController(clips);
            ConfigurePlayerPrefabIfExists(PlayerPrefabPath);
            ConfigurePlayerPrefabIfExists(PlayerRemotePrefabPath);
            TryAssignSpawnPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log("[SyntyAnimationSetup] Animation controller rebuilt from Assets/Animations.");
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
            ApplyLocomotionAvatarMasks(controller);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
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
                ConfigureMixamoAnimationImports();
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
            serialized.FindProperty("walkAnimationSpeed").floatValue = 0.65f;
            serialized.FindProperty("runAnimationSpeed").floatValue = 0.72f;
            serialized.FindProperty("crouchAnimationSpeed").floatValue = 0.6f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static LocomotionClips FindLocomotionClips()
        {
            var fromProjectFolder = FindLocomotionClipsFromAnimationsFolder();
            if (fromProjectFolder.Idle != null)
            {
                return fromProjectFolder;
            }

            var searchRoots = new List<string> { AnimationsFolder, "Assets/Synty", "Assets" };
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
                Run = FindClip(clips, "Run", "A_Run", "Sprint"),
                Jump = FindClip(clips, "Jump", "A_Jump"),
                Fall = FindClip(clips, "Fall", "A_Fall", "InAir"),
                Land = FindClip(clips, "Land", "A_Land"),
                CrouchIdle = FindClip(clips, "CrouchIdle", "Crouch_Idle"),
                CrouchWalk = FindClip(clips, "CrouchForwardWalking", "CrouchForward", "Crouch_Walk", "CrouchWalk")
            };
        }

        private static LocomotionClips FindLocomotionClipsFromAnimationsFolder()
        {
            return new LocomotionClips
            {
                Idle = LoadClipFromFbx($"{AnimationsFolder}/Idle.fbx"),
                WalkForward = LoadClipFromFbx($"{AnimationsFolder}/ForwardWalking.fbx"),
                WalkLeft = LoadClipFromFbx($"{AnimationsFolder}/LeftWalking.fbx"),
                Walk = LoadClipFromFbx($"{AnimationsFolder}/ForwardWalking.fbx"),
                Run = LoadClipFromFbx($"{AnimationsFolder}/Run.fbx"),
                Jump = LoadClipFromFbx($"{AnimationsFolder}/Jump.fbx"),
                CrouchIdle = LoadClipFromFbx($"{AnimationsFolder}/CrouchIdle.fbx"),
                CrouchWalk = LoadClipFromFbx($"{AnimationsFolder}/CrouchForwardWalking.fbx"),
                CrouchWalkRight = LoadClipFromFbx($"{AnimationsFolder}/CrouchRightWalking.fbx")
            };
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

        private static void ConfigureMixamoAnimationImports()
        {
            if (!AssetDatabase.IsValidFolder(AnimationsFolder))
            {
                return;
            }

            EnsureMixamoSourceAvatar(MixamoSourceFbxPath);

            var fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { AnimationsFolder });
            for (var i = 0; i < fbxGuids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(fbxGuids[i]);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ConfigureMixamoFbxImport(path);
            }
        }

        private static Avatar EnsureMixamoSourceAvatar(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                return LoadAvatarFromAsset(fbxPath);
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

            return LoadAvatarFromAsset(fbxPath);
        }

        private static void ConfigureMixamoFbxImport(string fbxPath)
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

            // Each Mixamo FBX has its own skeleton. CopyFromOther fails when optional
            // bones such as LeftEye exist in the source avatar but not in a clip file.
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
            var layer = controller.layers[0];
            var stateMachine = layer.stateMachine;
            stateMachine.states = new ChildAnimatorState[0];
            stateMachine.anyStateTransitions = new AnimatorStateTransition[0];

            var idle = clips.Idle ?? clips.Walk ?? clips.Run;
            var walkForward = clips.WalkForward ?? clips.Walk ?? idle;
            var run = clips.Run ?? walkForward;

            var walkMotion = BuildWalkMotion(controller, clips, walkForward);
            var blendTree = new BlendTree
            {
                name = "LocomotionBlend",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false
            };
            Add1DChild(blendTree, idle, 0f);
            Add1DChild(blendTree, walkMotion, 0.45f);
            Add1DChild(blendTree, run, 1f);
            AssetDatabase.AddObjectToAsset(blendTree, controller);

            var locomotionState = stateMachine.AddState("Locomotion", new Vector3(300f, 0f, 0f));
            locomotionState.motion = blendTree;
            stateMachine.defaultState = locomotionState;

            if (clips.Jump != null)
            {
                var jumpState = stateMachine.AddState("Jump", new Vector3(520f, -80f, 0f));
                jumpState.motion = clips.Jump;
                var jumpTransition = stateMachine.AddAnyStateTransition(jumpState);
                jumpTransition.AddCondition(AnimatorConditionMode.Equals, 1f, "JumpState");
                jumpTransition.hasExitTime = false;
                jumpTransition.duration = 0.05f;
                jumpTransition.canTransitionToSelf = false;

                var jumpBack = jumpState.AddTransition(locomotionState);
                jumpBack.hasExitTime = true;
                jumpBack.exitTime = 0.85f;
                jumpBack.duration = 0.12f;
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
                    toCrouch.duration = 0.1f;

                    var fromCrouch = crouchState.AddTransition(locomotionState);
                    fromCrouch.AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouching");
                    fromCrouch.hasExitTime = false;
                    fromCrouch.duration = 0.1f;
                }
            }
        }

        private static void ApplyLocomotionAvatarMasks(AnimatorController controller)
        {
            EnsureLocomotionAvatarMaskAssets();

            var bodyMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(BodyNoArmsMaskPath);
            var armsMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(ArmsOnlyMaskPath);
            if (bodyMask == null || armsMask == null)
            {
                Debug.LogWarning("[SyntyAnimationSetup] Locomotion avatar masks are missing.");
                return;
            }

            var layers = controller.layers;
            if (layers.Length == 0)
            {
                return;
            }

            layers[0].name = "Base Locomotion";
            layers[0].avatarMask = bodyMask;
            layers[0].defaultWeight = 1f;

            if (layers.Length < 2)
            {
                var armsLayer = new AnimatorControllerLayer
                {
                    name = "Arms Locomotion",
                    avatarMask = armsMask,
                    defaultWeight = 1f,
                    blendingMode = AnimatorLayerBlendingMode.Override,
                    syncedLayerIndex = 0,
                    iKPass = false,
                    stateMachine = new AnimatorStateMachine
                    {
                        name = "Arms Locomotion",
                        hideFlags = HideFlags.HideInHierarchy
                    }
                };
                AssetDatabase.AddObjectToAsset(armsLayer.stateMachine, controller);

                var expandedLayers = new AnimatorControllerLayer[layers.Length + 1];
                for (var i = 0; i < layers.Length; i++)
                {
                    expandedLayers[i] = layers[i];
                }

                expandedLayers[layers.Length] = armsLayer;
                layers = expandedLayers;
            }
            else
            {
                layers[1].name = "Arms Locomotion";
                layers[1].avatarMask = armsMask;
                layers[1].defaultWeight = 1f;
                layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
                layers[1].syncedLayerIndex = 0;
                layers[1].iKPass = false;
            }

            controller.layers = layers;
        }

        private static void EnsureLocomotionAvatarMaskAssets()
        {
            EnsureFolder("Assets/Prefabs/Player");

            var bodyMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(BodyNoArmsMaskPath);
            if (bodyMask == null)
            {
                bodyMask = CreateHumanoidLocomotionMask(includeArms: false);
                AssetDatabase.CreateAsset(bodyMask, BodyNoArmsMaskPath);
            }

            var armsMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(ArmsOnlyMaskPath);
            if (armsMask == null)
            {
                armsMask = CreateHumanoidLocomotionMask(includeArms: true);
                AssetDatabase.CreateAsset(armsMask, ArmsOnlyMaskPath);
            }
        }

        private static AvatarMask CreateHumanoidLocomotionMask(bool includeArms)
        {
            var mask = new AvatarMask();
            for (var i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                var isArmPart = part == AvatarMaskBodyPart.LeftArm ||
                                part == AvatarMaskBodyPart.RightArm ||
                                part == AvatarMaskBodyPart.LeftFingers ||
                                part == AvatarMaskBodyPart.RightFingers;
                mask.SetHumanoidBodyPartActive(part, includeArms ? isArmPart : !isArmPart);
            }

            return mask;
        }

        private static Motion BuildWalkMotion(AnimatorController controller, LocomotionClips clips, AnimationClip walkForward)
        {
            walkForward ??= clips.Walk ?? clips.Idle;
            var walkLeft = clips.WalkLeft;
            if (walkForward == null || walkLeft == null)
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
            Add2DChild(walkTree, walkForward, new Vector2(0f, -1f), timeScale: -1f);
            Add2DChild(walkTree, walkLeft, new Vector2(-1f, 0f));
            Add2DChild(walkTree, walkLeft, new Vector2(1f, 0f), mirror: true);

            AssetDatabase.AddObjectToAsset(walkTree, controller);
            return walkTree;
        }

        private static Motion BuildCrouchLocomotionMotion(AnimatorController controller, LocomotionClips clips)
        {
            var crouchIdle = clips.CrouchIdle;
            var crouchForward = clips.CrouchWalk;
            var crouchRight = clips.CrouchWalkRight;
            Motion directional = null;

            if (crouchForward != null && crouchRight != null)
            {
                var crouchTree = new BlendTree
                {
                    name = "CrouchDirectionBlend",
                    blendType = BlendTreeType.SimpleDirectional2D,
                    blendParameter = "MoveX",
                    blendParameterY = "MoveY",
                    useAutomaticThresholds = false
                };

                Add2DChild(crouchTree, crouchForward, new Vector2(0f, 1f));
                Add2DChild(crouchTree, crouchForward, new Vector2(0f, -1f), timeScale: -1f);
                Add2DChild(crouchTree, crouchRight, new Vector2(1f, 0f));
                Add2DChild(crouchTree, crouchRight, new Vector2(-1f, 0f), mirror: true);
                AssetDatabase.AddObjectToAsset(crouchTree, controller);
                directional = crouchTree;
            }
            else if (crouchForward != null)
            {
                directional = crouchForward;
            }

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
            public AnimationClip Run;
            public AnimationClip Jump;
            public AnimationClip Fall;
            public AnimationClip Land;
            public AnimationClip CrouchIdle;
            public AnimationClip CrouchWalk;
            public AnimationClip CrouchWalkRight;
        }
    }
}
#endif
