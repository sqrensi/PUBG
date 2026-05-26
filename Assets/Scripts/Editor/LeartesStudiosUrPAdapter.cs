#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShooterPrototype.EditorTools
{
    public static class LeartesStudiosUrPAdapter
    {
        private const string RootFolder = "Assets/LeartesStudios";
        private const string TerrainLitPath = "Assets/LeartesStudios/BanditsValley/Art/Terrain/TerrainLit.mat";

        [MenuItem("Shooter Prototype/Leartes Studios/Adapt Materials To URP Lit")]
        public static void AdaptMaterialsMenu()
        {
            var converted = AdaptAllMaterials();
            Debug.Log($"LeartesStudios URP adaptation finished. Converted {converted} material(s).");
        }

        public static void AdaptMaterialsBatch()
        {
            AdaptAllMaterials();
            EditorApplication.Exit(0);
        }

        private static int AdaptAllMaterials()
        {
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            var urpTerrainLit = Shader.Find("Universal Render Pipeline/Terrain/Lit");

            if (urpLit == null)
            {
                Debug.LogError("Universal Render Pipeline/Lit shader was not found. Is URP installed?");
                return 0;
            }

            var converted = 0;
            var materialGuids = AssetDatabase.FindAssets("t:Material", new[] { RootFolder });

            foreach (var guid in materialGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                    continue;

                if (path == TerrainLitPath && urpTerrainLit != null)
                {
                    if (ConvertTerrainLit(material, urpTerrainLit))
                    {
                        EditorUtility.SetDirty(material);
                        converted++;
                    }

                    continue;
                }

                if (ShouldConvertToUrPLit(material))
                {
                    ConvertHdrpLitToUrPLit(material, urpLit);
                    EditorUtility.SetDirty(material);
                    converted++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return converted;
        }

        private static bool ShouldConvertToUrPLit(Material material)
        {
            if (material.shader == null)
                return true;

            var shaderName = material.shader.name;
            return shaderName.Contains("HDRP/Lit")
                || shaderName.Contains("Hidden/HDRP")
                || shaderName.Contains("High Definition Render Pipeline");
        }

        private static void ConvertHdrpLitToUrPLit(Material material, Shader urpLit)
        {
            var baseMap = FirstTexture(material, "_BaseColorMap", "_MainTex", "_BaseMap");
            var bumpMap = FirstTexture(material, "_NormalMap", "_BumpMap");
            var maskMap = FirstTexture(material, "_MaskMap", "_MetallicGlossMap");
            var baseColor = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
            var metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0f;
            var smoothness = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : 0.5f;
            var cutoff = material.HasProperty("_AlphaCutoff")
                ? material.GetFloat("_AlphaCutoff")
                : material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : 0.5f;
            var alphaClip = material.IsKeywordEnabled("_ALPHATEST_ON")
                || material.IsKeywordEnabled("_BUILTIN_ALPHATEST_ON")
                || (material.HasProperty("_AlphaCutoffEnable") && material.GetFloat("_AlphaCutoffEnable") > 0.5f);

            material.shader = urpLit;
            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);

            if (baseMap != null)
                material.SetTexture("_BaseMap", baseMap);

            if (bumpMap != null)
            {
                material.SetTexture("_BumpMap", bumpMap);
                material.EnableKeyword("_NORMALMAP");
            }

            if (maskMap != null)
                material.SetTexture("_MetallicGlossMap", maskMap);

            if (alphaClip)
            {
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", cutoff);
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                material.SetFloat("_AlphaClip", 0f);
                material.DisableKeyword("_ALPHATEST_ON");
                material.renderQueue = -1;
            }

            CleanupHdrpKeywords(material);
        }

        private static bool ConvertTerrainLit(Material material, Shader urpTerrainLit)
        {
            if (material.shader == urpTerrainLit)
                return false;

            material.shader = urpTerrainLit;
            return true;
        }

        private static Texture FirstTexture(Material material, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName))
                {
                    var texture = material.GetTexture(propertyName);
                    if (texture != null)
                        return texture;
                }
            }

            return null;
        }

        private static void CleanupHdrpKeywords(Material material)
        {
            var keywordsToDisable = new List<string>();
            foreach (var keyword in material.shaderKeywords)
            {
                if (keyword.StartsWith("_DISABLE_")
                    || keyword.Contains("SSR")
                    || keyword.Contains("BUILTIN_")
                    || keyword.Contains("NORMALMAP_TANGENT"))
                {
                    keywordsToDisable.Add(keyword);
                }
            }

            foreach (var keyword in keywordsToDisable)
                material.DisableKeyword(keyword);
        }
    }
}
#endif
