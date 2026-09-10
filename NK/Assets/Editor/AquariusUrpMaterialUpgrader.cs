using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

internal static class AquariusUrpMaterialUpgrader
{
    private const string Root = "Assets/Aquarius Fantasy - High Elves";
    private const string UrpLitName = "Universal Render Pipeline/Lit";
    private const string FoliageName = "Aquarius Fantasy/URP/Foliage";
    private const string WaterName = "Aquarius Fantasy/URP/Water";
    private const string WaterfallName = "Aquarius Fantasy/URP/Waterfall";

    [InitializeOnLoadMethod]
    private static void ScheduleAutomaticUpgrade()
    {
        EditorApplication.delayCall += UpgradeIfNeeded;
    }

    [MenuItem("Tools/Aquarius Fantasy/Upgrade Materials to URP")]
    private static void UpgradeFromMenu()
    {
        UpgradeMaterials(false);
    }

    private static void UpgradeIfNeeded()
    {
        if (!(GraphicsSettings.renderPipelineAsset is UniversalRenderPipelineAsset))
            return;

        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Root }))
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (material != null && IsLegacyAquariusShader(material.shader))
            {
                UpgradeMaterials(true);
                return;
            }
        }

        EnsureDepthTexture();
    }

    private static bool IsLegacyAquariusShader(Shader shader)
    {
        if (shader == null)
            return true;

        string path = AssetDatabase.GetAssetPath(shader).Replace('\\', '/');
        if (path.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
            return !shader.name.StartsWith("Aquarius Fantasy/URP/", StringComparison.Ordinal);

        // Built-in shaders have no AssetDatabase path. Everything in this asset pack
        // that uses one of them should be migrated, except the supported skybox shader.
        if (string.IsNullOrEmpty(path))
            return !shader.name.StartsWith("Skybox/", StringComparison.Ordinal);

        return shader.name == "Standard" ||
               shader.name == "Standard (Specular setup)" ||
               shader.name.StartsWith("Legacy Shaders/", StringComparison.Ordinal);
    }

    private static void UpgradeMaterials(bool automatic)
    {
        Shader urpLit = Shader.Find(UrpLitName);
        Shader foliage = Shader.Find(FoliageName);
        Shader water = Shader.Find(WaterName);
        Shader waterfall = Shader.Find(WaterfallName);

        if (urpLit == null || foliage == null || water == null || waterfall == null)
        {
            if (!automatic)
                Debug.LogError("Aquarius URP upgrade could not find one or more target shaders. Wait for shader import to finish, then run Tools > Aquarius Fantasy > Upgrade Materials to URP.");
            return;
        }

        int converted = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || !IsLegacyAquariusShader(material.shader))
                    continue;

                string shaderPath = material.shader == null ? string.Empty : AssetDatabase.GetAssetPath(material.shader).Replace('\\', '/');

                if (shaderPath.EndsWith("Leaves Foliage.shadergraph", StringComparison.OrdinalIgnoreCase) ||
                    shaderPath.EndsWith("Leaves_Gradient_Windy.shader", StringComparison.OrdinalIgnoreCase))
                {
                    material.shader = foliage;
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    material.EnableKeyword("_ALPHATEST_ON");
                }
                else if (shaderPath.EndsWith("Water Shader.shadergraph", StringComparison.OrdinalIgnoreCase))
                {
                    material.shader = water;
                    material.renderQueue = (int)RenderQueue.Transparent;
                    material.SetOverrideTag("RenderType", "Transparent");
                }
                else if (shaderPath.EndsWith("Waterfall Realistic A.shadergraph", StringComparison.OrdinalIgnoreCase))
                {
                    material.shader = waterfall;
                    material.renderQueue = (int)RenderQueue.Transparent;
                    material.SetOverrideTag("RenderType", "Transparent");
                }
                else if (material.shader != null && material.shader.name.StartsWith("Skybox/", StringComparison.Ordinal))
                {
                    continue;
                }
                else
                {
                    UpgradeToUrpLit(material, urpLit, shaderPath);
                }

                EditorUtility.SetDirty(material);
                converted++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        EnsureDepthTexture();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Aquarius Fantasy URP upgrade complete: {converted} materials converted{(automatic ? " automatically" : string.Empty)}.");
    }

    private static void UpgradeToUrpLit(Material material, Shader urpLit, string oldShaderPath)
    {
        bool graphMaterial = oldShaderPath.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase);
        Texture albedo = FirstTexture(material, graphMaterial ? "_Albedo" : "_MainTex", "_MainTex", "_Albedo");
        Texture normal = FirstTexture(material, graphMaterial ? "_Normal" : "_BumpMap", "_BumpMap", "_Normal");
        Texture metallicMap = FirstTexture(material, graphMaterial ? "_Metallic" : "_MetallicGlossMap", "_MetallicGlossMap");
        Texture occlusion = FirstTexture(material, "_AO_1", "_OcclusionMap");
        Texture emission = FirstTexture(material, "_EmissionMap");

        Vector2 albedoScale = TextureScale(material, graphMaterial ? "_Albedo" : "_MainTex");
        Vector2 albedoOffset = TextureOffset(material, graphMaterial ? "_Albedo" : "_MainTex");
        Color baseColor = FirstColor(material, Color.white, "_Albedo_Color", "_Color");
        Color emissionColor = FirstColor(material, Color.black, "_EmissionColor");
        float metallic = FirstFloat(material, graphMaterial ? 0f : 0f, graphMaterial ? "_Metallic_Strength" : "_Metallic");
        float smoothness = FirstFloat(material, 0.35f, "_Smoothness", "_Glossiness");
        float normalScale = FirstFloat(material, 1f, graphMaterial ? "_Normal_Strength" : "_BumpScale", "_BumpScale");
        float occlusionStrength = FirstFloat(material, 1f, "_OcclusionStrength", "_AO");
        float oldMode = FirstFloat(material, 0f, "_Mode");
        bool transparent = material.renderQueue >= (int)RenderQueue.Transparent || oldMode >= 2f;

        material.shader = urpLit;
        SetTexture(material, "_BaseMap", albedo, albedoScale, albedoOffset);
        SetTexture(material, "_BumpMap", normal, Vector2.one, Vector2.zero);
        SetTexture(material, "_MetallicGlossMap", metallicMap, Vector2.one, Vector2.zero);
        SetTexture(material, "_OcclusionMap", occlusion, Vector2.one, Vector2.zero);
        SetTexture(material, "_EmissionMap", emission, Vector2.one, Vector2.zero);

        SetColor(material, "_BaseColor", baseColor);
        SetColor(material, "_EmissionColor", emissionColor);
        SetFloat(material, "_Metallic", Mathf.Clamp01(metallic));
        SetFloat(material, "_Smoothness", Mathf.Clamp01(smoothness));
        SetFloat(material, "_BumpScale", normalScale);
        SetFloat(material, "_OcclusionStrength", Mathf.Clamp01(occlusionStrength));
        SetFloat(material, "_WorkflowMode", 1f);

        SetKeyword(material, "_NORMALMAP", normal != null);
        SetKeyword(material, "_METALLICSPECGLOSSMAP", metallicMap != null);
        SetKeyword(material, "_OCCLUSIONMAP", occlusion != null);
        SetKeyword(material, "_EMISSION", emission != null || emissionColor.maxColorComponent > 0.001f);
        SetKeyword(material, "_SPECULAR_SETUP", false);

        if (transparent)
        {
            SetFloat(material, "_Surface", 1f);
            SetFloat(material, "_Blend", 0f);
            SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetFloat(material, "_ZWrite", 0f);
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
            SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", true);
        }
        else
        {
            SetFloat(material, "_Surface", 0f);
            SetFloat(material, "_Blend", 0f);
            SetFloat(material, "_SrcBlend", (float)BlendMode.One);
            SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
            SetFloat(material, "_ZWrite", 1f);
            material.renderQueue = -1;
            material.SetOverrideTag("RenderType", "Opaque");
            SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", false);
        }
    }

    private static void EnsureDepthTexture()
    {
        if (GraphicsSettings.renderPipelineAsset is UniversalRenderPipelineAsset urp && !urp.supportsCameraDepthTexture)
        {
            urp.supportsCameraDepthTexture = true;
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();
            Debug.Log("Aquarius Fantasy URP upgrade enabled Depth Texture on the active URP asset.");
        }
    }

    private static Texture FirstTexture(Material material, params string[] names)
    {
        foreach (string name in names)
        {
            if (!material.HasProperty(name))
                continue;
            try
            {
                Texture texture = material.GetTexture(name);
                if (texture != null)
                    return texture;
            }
            catch (ArgumentException)
            {
                // A legacy material can contain a float and texture with the same serialized name.
            }
        }
        return null;
    }

    private static Vector2 TextureScale(Material material, string name)
    {
        try { return material.HasProperty(name) ? material.GetTextureScale(name) : Vector2.one; }
        catch (ArgumentException) { return Vector2.one; }
    }

    private static Vector2 TextureOffset(Material material, string name)
    {
        try { return material.HasProperty(name) ? material.GetTextureOffset(name) : Vector2.zero; }
        catch (ArgumentException) { return Vector2.zero; }
    }

    private static Color FirstColor(Material material, Color fallback, params string[] names)
    {
        foreach (string name in names)
        {
            if (material.HasProperty(name))
            {
                try { return material.GetColor(name); }
                catch (ArgumentException) { }
            }
        }
        return fallback;
    }

    private static float FirstFloat(Material material, float fallback, params string[] names)
    {
        foreach (string name in names)
        {
            if (material.HasProperty(name))
            {
                try { return material.GetFloat(name); }
                catch (ArgumentException) { }
            }
        }
        return fallback;
    }

    private static void SetTexture(Material material, string name, Texture texture, Vector2 scale, Vector2 offset)
    {
        if (!material.HasProperty(name))
            return;
        material.SetTexture(name, texture);
        material.SetTextureScale(name, scale);
        material.SetTextureOffset(name, offset);
    }

    private static void SetColor(Material material, string name, Color value)
    {
        if (material.HasProperty(name))
            material.SetColor(name, value);
    }

    private static void SetFloat(Material material, string name, float value)
    {
        if (material.HasProperty(name))
            material.SetFloat(name, value);
    }

    private static void SetKeyword(Material material, string keyword, bool enabled)
    {
        if (enabled)
            material.EnableKeyword(keyword);
        else
            material.DisableKeyword(keyword);
    }
}
