using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class NarakaBlenderImportPostprocessor : AssetPostprocessor
{
    private const string ImportRoot = "Assets/Aquarius Fantasy - High Elves/Demo Scenes/High Elves Sanctuary/Imported Characters";
    private const string ShaderName = "Naraka/URP/Imported Character";
    private static bool s_Scheduled;

    [Serializable]
    private sealed class ImportManifest
    {
        public int version;
        public string model_name;
        public string source_blend;
        public string fbx_asset_path;
        public MaterialDefinition[] materials;
        public AnimationDefinition[] animations;
        public ArmatureDefinition[] armatures;
        public ImageError[] image_errors;
    }

    [Serializable]
    private sealed class MaterialDefinition
    {
        public string name;
        public float[] base_color;
        public float metallic;
        public float roughness;
        public float emission_strength;
        public string blend_method;
        public bool double_sided;
        public bool uses_alpha;
        public bool ignore_base_texture_color;
        public bool normal_swap_rg;
        public bool normal_invert_r;
        public TextureDefinition[] textures;
    }

    [Serializable]
    private sealed class TextureDefinition
    {
        public string role;
        public string image;
        public string asset_path;
        public string colorspace;
        public string uv_map;
        public string channel;
    }

    [Serializable]
    private sealed class AnimationDefinition
    {
        public string name;
        public float frame_start;
        public float frame_end;
        public string[] slots;
    }

    [Serializable]
    private sealed class ArmatureDefinition
    {
        public string name;
        public int bones;
        public string[] bone_names;
    }

    [Serializable]
    private sealed class ImageError
    {
        public string image;
        public string error;
    }

    private void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(ImportRoot, StringComparison.OrdinalIgnoreCase))
            return;

        var importer = (ModelImporter)assetImporter;
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.bakeAxisConversion = true;
        importer.importBlendShapes = true;
        importer.importVisibility = true;
        importer.importCameras = true;
        importer.importLights = true;
        importer.importConstraints = true;
        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.resampleCurves = true;
        importer.optimizeGameObjects = false;
        importer.preserveHierarchy = true;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.Import;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
        importer.materialSearch = ModelImporterMaterialSearch.Local;
    }

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(ImportRoot, StringComparison.OrdinalIgnoreCase))
            return;

        string name = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
        var importer = (TextureImporter)assetImporter;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.maxTextureSize = 4096;

        bool normal = name.EndsWith("_n") || name.EndsWith("_nx") || name.Contains("normal") || name.Contains("法线");
        bool data = name.Contains("mrav") || name.Contains("mask") || name.EndsWith("_tr") || name.EndsWith("_tx") ||
                    name.Contains("rough") || name.Contains("糙度") || name.Contains("skin");
        if (normal)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
        }
        else if (data)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
        }
    }

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (s_Scheduled)
            return;

        bool relevant = importedAssets.Any(IsRelevantAsset) || movedAssets.Any(IsRelevantAsset);
        if (!relevant)
            return;

        s_Scheduled = true;
        EditorApplication.delayCall += () =>
        {
            s_Scheduled = false;
            SetupAllImportedCharacters();
        };
    }

    private static bool IsRelevantAsset(string path)
    {
        return path.StartsWith(ImportRoot, StringComparison.OrdinalIgnoreCase) &&
               (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".unity.json", StringComparison.OrdinalIgnoreCase));
    }

    [MenuItem("Tools/Naraka/Setup Imported Blender Characters")]
    public static void SetupAllImportedCharacters()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"Naraka Blender import: shader '{ShaderName}' was not found.");
            return;
        }

        string[] manifests = AssetDatabase.FindAssets("t:TextAsset", new[] { ImportRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".unity.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        int models = 0;
        int materials = 0;
        foreach (string manifestPath in manifests)
        {
            TextAsset textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(manifestPath);
            if (textAsset == null)
                continue;
            ImportManifest manifest = JsonUtility.FromJson<ImportManifest>(textAsset.text);
            if (manifest == null || string.IsNullOrEmpty(manifest.fbx_asset_path))
                continue;
            ConfigureManifestTextures(manifest);
            materials += SetupModel(manifest, shader);
            models++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Naraka Blender import complete: {models} models and {materials} materials configured.");
    }

    [MenuItem("Tools/Naraka/Validate Imported Blender Characters")]
    public static void ValidateImportedCharacters()
    {
        string[] manifestPaths = AssetDatabase.FindAssets("t:TextAsset", new[] { ImportRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".unity.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        int failures = 0;

        foreach (string manifestPath in manifestPaths)
        {
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(manifestPath);
            ImportManifest manifest = source == null ? null : JsonUtility.FromJson<ImportManifest>(source.text);
            if (manifest == null)
            {
                Debug.LogError($"Naraka Blender import validation: unreadable manifest {manifestPath}");
                failures++;
                continue;
            }

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(manifest.fbx_asset_path);
            ModelImporter importer = AssetImporter.GetAtPath(manifest.fbx_asset_path) as ModelImporter;
            if (model == null || importer == null)
            {
                Debug.LogError($"Naraka Blender import validation: model not imported: {manifest.fbx_asset_path}");
                failures++;
                continue;
            }

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            int meshCount = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length +
                            model.GetComponentsInChildren<MeshFilter>(true).Length;
            int missingMaterials = renderers.Sum(renderer => renderer.sharedMaterials.Count(material => material == null));
            int mappedMaterials = importer.GetExternalObjectMap().Count;
            int expectedMaterials = manifest.materials?.Length ?? 0;
            int textureAssets = 0;
            int missingTextureAssets = 0;
            foreach (TextureDefinition texture in (manifest.materials ?? Array.Empty<MaterialDefinition>())
                         .SelectMany(material => material.textures ?? Array.Empty<TextureDefinition>()))
            {
                if (string.IsNullOrEmpty(texture.asset_path))
                    continue;
                textureAssets++;
                if (AssetDatabase.LoadAssetAtPath<Texture>(texture.asset_path) == null)
                    missingTextureAssets++;
            }

            var armatureNames = new HashSet<string>(
                (manifest.armatures ?? Array.Empty<ArmatureDefinition>()).Select(armature => armature.name),
                StringComparer.Ordinal);
            Transform[] collapsedArmatures = model.GetComponentsInChildren<Transform>(true)
                .Where(transform => armatureNames.Contains(transform.name))
                .Where(transform =>
                {
                    Vector3 scale = transform.lossyScale;
                    return Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)) < 0.1f;
                })
                .ToArray();

            if (missingMaterials > 0 || mappedMaterials < expectedMaterials || missingTextureAssets > 0 || collapsedArmatures.Length > 0)
            {
                Debug.LogError(
                    $"Naraka Blender import validation: {manifest.model_name} has {missingMaterials} null materials and " +
                    $"{mappedMaterials}/{expectedMaterials} external material mappings, {missingTextureAssets}/{textureAssets} " +
                    $"missing texture assets, and {collapsedArmatures.Length} collapsed armatures.");
                failures++;
            }

            int expectedAnimations = manifest.animations?.Length ?? 0;
            int animationFiles = 0;
            int animationClips = 0;
            if (expectedAnimations > 0)
            {
                string modelDirectory = Path.GetDirectoryName(manifest.fbx_asset_path)?.Replace('\\', '/');
                string animationDirectory = modelDirectory + "/Animations";
                string[] animationPaths = AssetDatabase.FindAssets("t:Model", new[] { animationDirectory })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(path => path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                animationFiles = animationPaths.Length;
                animationClips = animationPaths.Sum(path => AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .Count(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase)));
                if (animationFiles < expectedAnimations || animationClips < expectedAnimations)
                {
                    Debug.LogError(
                        $"Naraka Blender import validation: {manifest.model_name} has {animationFiles}/{expectedAnimations} " +
                        $"animation FBXs and {animationClips}/{expectedAnimations} imported clips.");
                    failures++;
                }
            }

            Debug.Log(
                $"Naraka Blender import validation: {manifest.model_name}, meshes={meshCount}, renderers={renderers.Length}, " +
                $"materials={mappedMaterials}/{expectedMaterials}, textures={textureAssets - missingTextureAssets}/{textureAssets}, " +
                $"collapsedArmatures={collapsedArmatures.Length}, animationFiles={animationFiles}, clips={animationClips}.");
        }

        if (failures > 0)
            throw new InvalidOperationException($"Naraka Blender import validation failed with {failures} issue(s).");
        Debug.Log($"Naraka Blender import validation passed for {manifestPaths.Length} models.");
    }

    private static int SetupModel(ImportManifest manifest, Shader shader)
    {
        string modelDirectory = Path.GetDirectoryName(manifest.fbx_asset_path)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(modelDirectory))
            return 0;

        string materialDirectory = modelDirectory + "/Materials";
        EnsureAssetFolder(materialDirectory);
        var materialAssets = new Dictionary<string, Material>(StringComparer.Ordinal);

        foreach (MaterialDefinition definition in manifest.materials ?? Array.Empty<MaterialDefinition>())
        {
            string safeName = SanitizeFileName(definition.name);
            string materialPath = materialDirectory + "/" + safeName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader) { name = definition.name };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                material.shader = shader;
            }

            ConfigureMaterial(material, definition);
            EditorUtility.SetDirty(material);
            materialAssets[definition.name] = material;
        }

        var importer = AssetImporter.GetAtPath(manifest.fbx_asset_path) as ModelImporter;
        if (importer == null)
            return materialAssets.Count;

        Dictionary<AssetImporter.SourceAssetIdentifier, UnityEngine.Object> existing = importer.GetExternalObjectMap();
        bool changed = false;
        foreach (KeyValuePair<string, Material> pair in materialAssets)
        {
            var identifier = new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key);
            if (!existing.TryGetValue(identifier, out UnityEngine.Object current) || current != pair.Value)
            {
                importer.AddRemap(identifier, pair.Value);
                changed = true;
            }
        }

        if (changed)
            importer.SaveAndReimport();
        return materialAssets.Count;
    }

    private static void ConfigureManifestTextures(ImportManifest manifest)
    {
        foreach (TextureDefinition definition in (manifest.materials ?? Array.Empty<MaterialDefinition>())
                     .SelectMany(material => material.textures ?? Array.Empty<TextureDefinition>())
                     .Where(texture => !string.IsNullOrEmpty(texture.asset_path))
                     .GroupBy(texture => texture.asset_path, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            TextureImporter importer = AssetImporter.GetAtPath(definition.asset_path) as TextureImporter;
            if (importer == null)
                continue;

            string role = (definition.role ?? string.Empty).ToLowerInvariant();
            bool normal = role.StartsWith("normal", StringComparison.Ordinal);
            bool data = normal || role.StartsWith("mrav", StringComparison.Ordinal) ||
                        role.StartsWith("roughness", StringComparison.Ordinal) ||
                        role.StartsWith("mask", StringComparison.Ordinal) ||
                        role.StartsWith("skin_detail", StringComparison.Ordinal) ||
                        role.StartsWith("detail_mrav", StringComparison.Ordinal);
            bool changed = false;
            TextureImporterType desiredType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != desiredType)
            {
                importer.textureType = desiredType;
                changed = true;
            }
            bool desiredSrgb = !data;
            if (importer.sRGBTexture != desiredSrgb)
            {
                importer.sRGBTexture = desiredSrgb;
                changed = true;
            }
            if (changed)
                importer.SaveAndReimport();
        }
    }

    private static void ConfigureMaterial(Material material, MaterialDefinition definition)
    {
        TextureDefinition bakedBaseTexture = FindTexture(definition, "baked_base");
        bool hasBakedBase = bakedBaseTexture != null;
        TextureDefinition baseTexture = bakedBaseTexture ?? FindTexture(definition, "base") ?? FindTexture(definition, "color_detail");
        TextureDefinition normalTexture = FindTexture(definition, "normal");
        TextureDefinition packedMravTexture = FindTexture(definition, "mrav");
        TextureDefinition roughnessTexture = FindTexture(definition, "roughness");
        TextureDefinition mravTexture = packedMravTexture ?? roughnessTexture;
        TextureDefinition emissionTexture = FindTexture(definition, "emission");
        TextureDefinition detailTexture = FindTexture(definition, "detail_base") ??
                                          FindTexture(definition, "color_detail") ??
                                          FindTexture(definition, "base_2") ??
                                          FindTexture(definition, "color_detail_2");
        if (detailTexture != null && baseTexture != null && detailTexture.asset_path == baseTexture.asset_path)
            detailTexture = FindTexture(definition, "color_detail_2");
        TextureDefinition detailMravTexture = FindTexture(definition, "detail_mrav") ?? FindTexture(definition, "mrav_2");
        TextureDefinition skinTexture = FindTexture(definition, "skin_detail");
        TextureDefinition maskTexture = FindTexture(definition, "mask");

        // The baked texture already contains Blender's complete color graph.
        // Applying source detail/skin nodes a second time would double-tint it.
        if (hasBakedBase)
        {
            detailTexture = null;
            skinTexture = null;
        }

        SetTexture(material, "_BaseMap", baseTexture);
        SetTexture(material, "_NormalMap", normalTexture);
        SetTexture(material, "_MRAVMap", mravTexture);
        SetTexture(material, "_EmissionMap", emissionTexture);
        SetTexture(material, "_DetailMap", detailTexture);
        SetTexture(material, "_DetailMRAVMap", detailMravTexture);
        SetTexture(material, "_SkinMap", skinTexture);
        SetTexture(material, "_MaskMap", maskTexture);
        material.SetFloat("_UseNormal", normalTexture != null ? 1f : 0f);
        material.SetFloat("_UseMRAV", mravTexture != null ? 1f : 0f);
        material.SetFloat("_MRAVMode", packedMravTexture == null && roughnessTexture != null ? 1f : 0f);
        bool useEmission = emissionTexture != null && definition.emission_strength > 0.0001f;
        material.SetFloat("_UseEmission", useEmission ? 1f : 0f);
        material.SetFloat("_UseDetail", detailTexture != null ? 1f : 0f);
        material.SetFloat("_UseDetailMRAV", detailMravTexture != null ? 1f : 0f);
        material.SetFloat("_UseSkin", skinTexture != null ? 1f : 0f);
        material.SetFloat("_UseMask", maskTexture != null ? 1f : 0f);
        material.SetFloat("_MaskChannel", maskTexture != null &&
                          string.Equals(maskTexture.channel, "a", StringComparison.OrdinalIgnoreCase) ? 1f : 0f);
        material.SetFloat("_UseBaseRGB", definition.ignore_base_texture_color ? 0f : 1f);
        material.SetFloat("_NormalSwapRG", definition.normal_swap_rg ? 1f : 0f);
        material.SetFloat("_NormalInvertR", definition.normal_invert_r ? 1f : 0f);
        material.SetFloat("_DetailStrength", 1f);
        float metallicScale = packedMravTexture != null ? 1f : Mathf.Clamp01(definition.metallic);
        float roughnessScale = mravTexture != null ? 1f : Mathf.Clamp01(definition.roughness);
        material.SetFloat("_MetallicScale", metallicScale);
        material.SetFloat("_RoughnessScale", roughnessScale);
        material.SetFloat("_OcclusionStrength", 1f);
        material.SetFloat("_NormalScale", 1f);
        material.SetFloat("_Cull", definition.double_sided ? (float)CullMode.Off : (float)CullMode.Back);
        material.doubleSidedGI = definition.double_sided;

        Color baseColor = Color.white;
        if (definition.base_color != null && definition.base_color.Length >= 4)
            baseColor = new Color(definition.base_color[0], definition.base_color[1], definition.base_color[2], definition.base_color[3]);
        // Blender's material.diffuse_color is only the viewport display color.
        // It is not a tint when the shader's Base Color input is texture-driven.
        if (baseTexture != null && !definition.ignore_base_texture_color)
            baseColor = new Color(1f, 1f, 1f, baseColor.a);
        material.SetColor("_BaseColor", baseColor);
        float emissionStrength = Mathf.Max(0f, definition.emission_strength);
        material.SetColor("_EmissionColor", new Color(emissionStrength, emissionStrength, emissionStrength, 1f));

        string lowerName = (definition.name ?? string.Empty).ToLowerInvariant();
        bool cornea = lowerName.Contains("cornea") || lowerName.Contains("glass") || lowerName.Contains("透明");
        bool hasCutoutTexture = definition.textures?.Any(texture =>
        {
            string imageName = (texture.image ?? string.Empty).ToLowerInvariant();
            return imageName.Contains("lash") || imageName.Contains("eyelash") || imageName.Contains("睫毛") ||
                   imageName.Contains("hair") || imageName.Contains("头发");
        }) == true;
        bool cutout = lowerName.Contains("lash") || lowerName.Contains("睫毛") || lowerName.Contains("hair") ||
                      lowerName.Contains("头发") || hasCutoutTexture || maskTexture != null ||
                      (!hasBakedBase && definition.uses_alpha);
        bool transparent = !hasBakedBase &&
                           (cornea || string.Equals(definition.blend_method, "BLENDED", StringComparison.OrdinalIgnoreCase));
        if (!transparent)
        {
            baseColor.a = 1f;
            material.SetColor("_BaseColor", baseColor);
        }

        if (cutout)
        {
            material.SetFloat("_AlphaClip", 1f);
            float cutoff = lowerName == "mat_22" || lowerName == "mat_25" ? 0.12f :
                           lowerName == "mat_2" || lowerName == "mat_21" ? 0.25f : 0.35f;
            material.SetFloat("_Cutoff", cutoff);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }
        else
        {
            material.SetFloat("_AlphaClip", 0f);
            material.DisableKeyword("_ALPHATEST_ON");
        }

        if (transparent)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            if (cornea)
            {
                baseColor.a = Mathf.Min(baseColor.a, 0.28f);
                material.SetColor("_BaseColor", baseColor);
                material.SetFloat("_RoughnessScale", 0.08f);
            }
        }
        else
        {
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetFloat("_ZWrite", 1f);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (!cutout)
            {
                material.SetOverrideTag("RenderType", "Opaque");
                material.renderQueue = -1;
            }
        }
    }

    private static TextureDefinition FindTexture(MaterialDefinition definition, string role)
    {
        return definition.textures?.FirstOrDefault(texture =>
            string.Equals(texture.role, role, StringComparison.OrdinalIgnoreCase));
    }

    private static void SetTexture(Material material, string property, TextureDefinition definition)
    {
        Texture texture = definition == null ? null : AssetDatabase.LoadAssetAtPath<Texture>(definition.asset_path);
        material.SetTexture(property, texture);
    }

    private static void EnsureAssetFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int index = 1; index < parts.Length; index++)
        {
            string next = current + "/" + parts[index];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[index]);
            current = next;
        }
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "Material" : value.Trim();
    }
}
