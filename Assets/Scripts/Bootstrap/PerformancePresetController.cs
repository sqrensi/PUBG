using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using QualityShadowResolution = UnityEngine.ShadowResolution;
using QualityShadowsMode = UnityEngine.ShadowQuality;

namespace ShooterPrototype.Bootstrap
{
    [DefaultExecutionOrder(-100)]
    public sealed class PerformancePresetController : MonoBehaviour
    {
        private const string PrefKey = "client_max_performance";

        [Header("Mode")]
        [SerializeField] private bool maxPerformanceEnabled;

        [Header("Max performance — QualitySettings")]
        [SerializeField] private int maxPerformanceAntiAliasing = 0;
        [SerializeField] private QualityShadowResolution maxPerformanceShadowResolution = QualityShadowResolution.Low;
        [SerializeField] private float maxPerformanceShadowDistance = 0f;
        [SerializeField] private int maxPerformanceShadowCascades = 1;
        [SerializeField] private int maxPerformancePixelLightCount = 0;
        [SerializeField] private float maxPerformanceLodBias = 0.25f;
        [SerializeField] private int maxPerformanceGlobalTextureMipmapLimit = 2;
        [SerializeField] private int maxPerformanceMaximumLodLevel = 2;
        [SerializeField] private QualityShadowsMode maxPerformanceShadowsMode = QualityShadowsMode.Disable;

        [Header("Max performance — URP")]
        [SerializeField] private float maxPerformanceRenderScale = 0.65f;
        [SerializeField] private int maxPerformanceUrpMsaa = 1;
        [SerializeField] private bool maxPerformanceSupportsHdr = false;
        [SerializeField] private bool maxPerformanceRequireDepthTexture = false;
        [SerializeField] private bool maxPerformanceRequireOpaqueTexture = false;
        [SerializeField] private float maxPerformanceUrpShadowDistance = 0f;
        [SerializeField] private int maxPerformanceMainLightShadowResolution = 256;
        [SerializeField] private int maxPerformanceShadowCascadeCount = 1;

        [Header("Visual quality (when max performance is off)")]
        [SerializeField] private int visualQualityAntiAliasing = 4;
        [SerializeField] private QualityShadowResolution visualQualityShadowResolution = QualityShadowResolution.VeryHigh;
        [SerializeField] private float visualQualityShadowDistance = 120f;
        [SerializeField] private int visualQualityShadowCascades = 4;
        [SerializeField] private int visualQualityPixelLightCount = 4;
        [SerializeField] private float visualQualityLodBias = 2f;
        [SerializeField] private int visualQualityUrpMsaa = 4;
        [SerializeField] private float visualQualityRenderScale = 1f;
        [SerializeField] private int visualQualityMainLightShadowResolution = 2048;
        [SerializeField] private int visualQualityShadowCascadeCount = 4;
        [SerializeField] private bool visualQualityRequireDepthTexture = true;
        [SerializeField] private bool visualQualityRequireOpaqueTexture = true;
        [SerializeField] private bool visualQualitySupportsHdr = true;

        private Volume[] cachedVolumes;
        private float[] cachedVolumeWeights;
        private bool hasCachedVolumeWeights;
        private bool hasCapturedProjectUrp;
        private UrpPresetState projectUrpState;
        private QualityShadowsMode projectShadowsMode;
        private int projectMaximumLodLevel;

        public bool MaxPerformanceEnabled => maxPerformanceEnabled;

        private struct UrpPresetState
        {
            public float RenderScale;
            public int MsaaSampleCount;
            public bool SupportsHdr;
            public bool RequireDepthTexture;
            public bool RequireOpaqueTexture;
            public float ShadowDistance;
            public int MainLightShadowResolution;
            public int ShadowCascadeCount;
        }

        private void Awake()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            projectShadowsMode = QualitySettings.shadows;
            projectMaximumLodLevel = QualitySettings.maximumLODLevel;
            CaptureProjectUrpIfNeeded();

            if (PlayerPrefs.HasKey(PrefKey))
            {
                maxPerformanceEnabled = PlayerPrefs.GetInt(PrefKey, 0) == 1;
            }

            CacheVolumeWeightsIfNeeded();
            ApplyCurrentPreset();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void OnValidate()
        {
            if (!Application.isPlaying || Application.isBatchMode)
            {
                return;
            }

            ApplyCurrentPreset();
        }

        public void SetMaxPerformanceEnabled(bool enabled)
        {
            maxPerformanceEnabled = enabled;
            PlayerPrefs.SetInt(PrefKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
            ApplyCurrentPreset();
        }

        public void ToggleMaxPerformance()
        {
            SetMaxPerformanceEnabled(!maxPerformanceEnabled);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Application.isBatchMode)
            {
                return;
            }

            hasCachedVolumeWeights = false;
            CacheVolumeWeightsIfNeeded();
            ApplyVolumeAndCameraSettings(maxPerformanceEnabled);
        }

        private void ApplyCurrentPreset()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            if (maxPerformanceEnabled)
            {
                ApplyMaxPerformancePreset();
            }
            else
            {
                ApplyVisualQualityPreset();
            }

            ApplyVolumeAndCameraSettings(maxPerformanceEnabled);
        }

        private void CaptureProjectUrpIfNeeded()
        {
            if (hasCapturedProjectUrp)
            {
                return;
            }

            var urp = GetActiveUrpAsset();
            if (urp == null)
            {
                return;
            }

            projectUrpState = ReadUrpState(urp);
            hasCapturedProjectUrp = true;
        }

        private void ApplyMaxPerformancePreset()
        {
            QualitySettings.antiAliasing = maxPerformanceAntiAliasing;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
            QualitySettings.globalTextureMipmapLimit = maxPerformanceGlobalTextureMipmapLimit;
            QualitySettings.maximumLODLevel = maxPerformanceMaximumLodLevel;
            QualitySettings.streamingMipmapsActive = false;
            QualitySettings.shadowResolution = maxPerformanceShadowResolution;
            QualitySettings.shadowDistance = maxPerformanceShadowDistance;
            QualitySettings.shadowCascades = maxPerformanceShadowCascades;
            QualitySettings.pixelLightCount = maxPerformancePixelLightCount;
            QualitySettings.lodBias = maxPerformanceLodBias;
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.softParticles = false;
            QualitySettings.shadows = maxPerformanceShadowsMode;
            QualitySettings.skinWeights = SkinWeights.OneBone;
            QualitySettings.particleRaycastBudget = 4;
            QualitySettings.vSyncCount = 0;

            ApplyUrpSettings(new UrpPresetState
            {
                RenderScale = maxPerformanceRenderScale,
                MsaaSampleCount = maxPerformanceUrpMsaa,
                SupportsHdr = maxPerformanceSupportsHdr,
                RequireDepthTexture = maxPerformanceRequireDepthTexture,
                RequireOpaqueTexture = maxPerformanceRequireOpaqueTexture,
                ShadowDistance = maxPerformanceUrpShadowDistance,
                MainLightShadowResolution = maxPerformanceMainLightShadowResolution,
                ShadowCascadeCount = maxPerformanceShadowCascadeCount
            });
        }

        private void ApplyVisualQualityPreset()
        {
            QualitySettings.antiAliasing = visualQualityAntiAliasing;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.globalTextureMipmapLimit = 0;
            QualitySettings.maximumLODLevel = projectMaximumLodLevel;
            QualitySettings.streamingMipmapsActive = false;
            QualitySettings.shadowResolution = visualQualityShadowResolution;
            QualitySettings.shadowDistance = visualQualityShadowDistance;
            QualitySettings.shadowCascades = visualQualityShadowCascades;
            QualitySettings.pixelLightCount = visualQualityPixelLightCount;
            QualitySettings.lodBias = visualQualityLodBias;
            QualitySettings.shadows = projectShadowsMode;
            QualitySettings.skinWeights = SkinWeights.FourBones;
            QualitySettings.vSyncCount = 0;

            ApplyUrpSettings(new UrpPresetState
            {
                RenderScale = visualQualityRenderScale,
                MsaaSampleCount = visualQualityUrpMsaa,
                SupportsHdr = visualQualitySupportsHdr,
                RequireDepthTexture = visualQualityRequireDepthTexture,
                RequireOpaqueTexture = visualQualityRequireOpaqueTexture,
                ShadowDistance = hasCapturedProjectUrp ? projectUrpState.ShadowDistance : visualQualityShadowDistance,
                MainLightShadowResolution = visualQualityMainLightShadowResolution,
                ShadowCascadeCount = visualQualityShadowCascadeCount
            });
        }

        private static UrpPresetState ReadUrpState(UniversalRenderPipelineAsset urp)
        {
            return new UrpPresetState
            {
                RenderScale = urp.renderScale,
                MsaaSampleCount = urp.msaaSampleCount,
                SupportsHdr = urp.supportsHDR,
                RequireDepthTexture = urp.supportsCameraDepthTexture,
                RequireOpaqueTexture = urp.supportsCameraOpaqueTexture,
                ShadowDistance = urp.shadowDistance,
                MainLightShadowResolution = urp.mainLightShadowmapResolution,
                ShadowCascadeCount = urp.shadowCascadeCount
            };
        }

        private static void ApplyUrpSettings(UrpPresetState state)
        {
            var urp = GetActiveUrpAsset();
            if (urp == null)
            {
                return;
            }

            urp.renderScale = Mathf.Clamp(state.RenderScale, 0.5f, 1f);
            urp.msaaSampleCount = Mathf.Clamp(state.MsaaSampleCount, 1, 8);
            urp.supportsHDR = state.SupportsHdr;
            urp.supportsCameraDepthTexture = state.RequireDepthTexture;
            urp.supportsCameraOpaqueTexture = state.RequireOpaqueTexture;
            urp.shadowDistance = Mathf.Max(0f, state.ShadowDistance);
            urp.mainLightShadowmapResolution = Mathf.Clamp(state.MainLightShadowResolution, 256, 4096);
            urp.shadowCascadeCount = Mathf.Clamp(state.ShadowCascadeCount, 1, 4);
        }

        private void ApplyVolumeAndCameraSettings(bool maxPerformance)
        {
            if (maxPerformance)
            {
                CacheVolumeWeightsIfNeeded();
                if (cachedVolumes != null)
                {
                    for (var i = 0; i < cachedVolumes.Length; i++)
                    {
                        var volume = cachedVolumes[i];
                        if (volume != null)
                        {
                            volume.weight = 0f;
                        }
                    }
                }
            }
            else
            {
                RestoreVolumeWeights();
            }

            ApplyCameraRenderSettings(maxPerformance);
        }

        private static void ApplyCameraRenderSettings(bool maxPerformance)
        {
            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < cameras.Length; i++)
            {
                var camera = cameras[i];
                if (camera == null || !camera.enabled)
                {
                    continue;
                }

                var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
                if (cameraData == null)
                {
                    continue;
                }

                cameraData.renderPostProcessing = !maxPerformance;
                cameraData.renderShadows = !maxPerformance;
                cameraData.antialiasing = maxPerformance
                    ? AntialiasingMode.None
                    : AntialiasingMode.FastApproximateAntialiasing;
                cameraData.antialiasingQuality = maxPerformance
                    ? AntialiasingQuality.Low
                    : AntialiasingQuality.High;
            }
        }

        private void CacheVolumeWeightsIfNeeded()
        {
            if (hasCachedVolumeWeights)
            {
                return;
            }

            cachedVolumes = FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            cachedVolumeWeights = new float[cachedVolumes.Length];
            for (var i = 0; i < cachedVolumes.Length; i++)
            {
                cachedVolumeWeights[i] = cachedVolumes[i] != null ? cachedVolumes[i].weight : 1f;
            }

            hasCachedVolumeWeights = cachedVolumes.Length > 0;
        }

        private void RestoreVolumeWeights()
        {
            if (cachedVolumes == null || cachedVolumeWeights == null)
            {
                return;
            }

            for (var i = 0; i < cachedVolumes.Length; i++)
            {
                var volume = cachedVolumes[i];
                if (volume != null)
                {
                    volume.weight = cachedVolumeWeights[i];
                }
            }
        }

        private static UniversalRenderPipelineAsset GetActiveUrpAsset()
        {
            if (QualitySettings.renderPipeline is UniversalRenderPipelineAsset qualityPipeline)
            {
                return qualityPipeline;
            }

            return GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        }
    }
}
