#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Naraka.Features.Monster.Model;
using UnityEditor;
using UnityEngine;

namespace Naraka.EditorTools.Diag
{
    /// <summary>
    /// 正式暮影妖狼资源的导入核查。
    ///
    /// 第三方资源刚进工程时最容易出的问题是"看起来导进来了，其实有一半是坏的"：
    /// URP 下的粉色材质、贴图丢失、动画曲线绑到了别的骨架、带 Root Motion 的位移动画、
    /// 以及顺手把演示场景一起导进来。这些都不能靠肉眼在 Inspector 里翻，
    /// 因此这里一次性量出来并写成报告。
    ///
    /// 只读：不改任何资产，不改导入设置。
    /// </summary>
    public static class DuskshadowWolfAssetAudit
    {
        private const string OutputPath = "artifacts/duskshadow-wolf-asset-audit.txt";

        // 资源地址与动画映射只有一份，见 DuskshadowWolfAssets：
        // 核查、装配与测试共用同一张表，否则三边会各自漂移。
        private const string WolfRoot = DuskshadowWolfAssets.ThirdPartyRoot;
        private const string ModelPath = DuskshadowWolfAssets.ModelPath;
        private const string ThirdPartyMaterialPath = DuskshadowWolfAssets.ThirdPartyMaterialPath;
        private const string BaseTexturePath = DuskshadowWolfAssets.BaseTexturePath;
        private const string GlowTexturePath = DuskshadowWolfAssets.GlowTexturePath;

        private static (MonsterAnimation Animation, string Fbx)[] AnimationToFbx =>
            DuskshadowWolfAssets.AnimationToFbx;

        private static string AnimationFbxPath(string fbx) =>
            DuskshadowWolfAssets.AnimationFbxPath(fbx);

        [MenuItem("NARAKA/Diag/Duskshadow Wolf Asset Audit")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("正式暮影妖狼资源导入核查  " +
                          DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine(new string('=', 104));

            ReportImportedFiles(sb);
            var modelPaths = ReportModel(sb);
            ReportAnimations(sb, modelPaths);
            ReportPosedGeometry(sb);
            ReportMaterials(sb);
            ReportDemoContent(sb);

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? ".");
            File.WriteAllText(OutputPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[狼资源核查] 已写出 {OutputPath}\n{sb}");
        }

        // ------------------------------------------------------------- 1. 落盘清单

        private static void ReportImportedFiles(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("## 1. 第三方目录里实际有哪些资产");
            sb.AppendLine();

            if (!AssetDatabase.IsValidFolder(WolfRoot))
            {
                sb.AppendLine($"  **{WolfRoot} 不存在** —— 正式资源尚未导入。");
                return;
            }

            var guids = AssetDatabase.FindAssets(string.Empty, new[] { WolfRoot });
            var paths = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();

            sb.AppendLine($"  {"路径",-62}{"类型",-26}GUID");
            sb.AppendLine("  " + new string('-', 118));
            foreach (var path in paths)
            {
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                sb.AppendLine($"  {path.Substring(WolfRoot.Length + 1),-62}" +
                              $"{(type == null ? "<无法识别>" : type.Name),-26}" +
                              $"{AssetDatabase.AssetPathToGUID(path)}");
            }
        }

        // ------------------------------------------------------------- 2. 模型

        private static HashSet<string> ReportModel(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("## 2. 模型本体");
            sb.AppendLine();

            var known = new HashSet<string>(StringComparer.Ordinal);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                sb.AppendLine($"  **找不到 {ModelPath}**");
                return known;
            }

            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            sb.AppendLine($"  路径            {ModelPath}");
            sb.AppendLine($"  animationType   {importer.animationType}（2 = Generic）");
            sb.AppendLine($"  avatarSetup     {importer.avatarSetup}");
            sb.AppendLine($"  importMaterials {importer.materialImportMode}");
            sb.AppendLine($"  useFileScale    {importer.useFileScale}，globalScale {importer.globalScale}");
            sb.AppendLine($"  rootMotionBone  " +
                          $"{(string.IsNullOrEmpty(importer.motionNodeName) ? "(空，没有 Root Motion 节点)" : importer.motionNodeName)}");

            var instance = (GameObject)UnityEngine.Object.Instantiate(model);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var transforms = instance.GetComponentsInChildren<Transform>(true);
                CollectPaths(instance.transform, instance.transform, known);

                var skinned = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var meshRenderers = instance.GetComponentsInChildren<MeshRenderer>(true);
                var animators = instance.GetComponentsInChildren<Animator>(true);
                var colliders = instance.GetComponentsInChildren<Collider>(true);

                sb.AppendLine();
                sb.AppendLine($"  Transform 数量           {transforms.Length}");
                sb.AppendLine($"  SkinnedMeshRenderer     {skinned.Length}");
                sb.AppendLine($"  MeshRenderer            {meshRenderers.Length}");
                sb.AppendLine($"  Animator                {animators.Length}" +
                              (animators.Length > 0
                                  ? $"（applyRootMotion={animators[0].applyRootMotion}，" +
                                    $"avatar={(animators[0].avatar == null ? "无" : animators[0].avatar.name)}）"
                                  : string.Empty));
                sb.AppendLine($"  Collider                {colliders.Length}" +
                              (colliders.Length == 0
                                  ? "（第三方模型自带 0 个碰撞体，因此命中与阻挡只能用项目自有的）"
                                  : "（**注意：有第三方碰撞体**）"));

                if (skinned.Length > 0)
                {
                    var bounds = skinned[0].bounds;
                    for (var i = 1; i < skinned.Length; i++)
                    {
                        bounds.Encapsulate(skinned[i].bounds);
                    }

                    sb.AppendLine();
                    sb.AppendLine($"  世界包围盒 center {V(bounds.center)}  size {V(bounds.size)}");
                    sb.AppendLine($"  体长/体宽/体高    Z {bounds.size.z:F3} / X {bounds.size.x:F3} / Y {bounds.size.y:F3}");
                    sb.AppendLine($"  脚底高度          {bounds.min.y:F3}" +
                                  (Mathf.Abs(bounds.min.y) < 0.05f
                                      ? "（≈0，模型原点在脚底，可直接贴地放置）"
                                      : "（**不在 0，放置时需要补偿**）"));
                    sb.AppendLine($"  朝向判断          " +
                                  (bounds.size.z >= bounds.size.x
                                      ? "体长沿 Z 轴（四足兽的正常朝向，Unity 正 Z 为前）"
                                      : "**体长沿 X 轴，模型可能需要绕 Y 旋转 90°**"));
                    sb.AppendLine($"  第 0 个蒙皮骨骼数  {skinned[0].bones?.Length ?? 0}");
                }

                sb.AppendLine();
                sb.AppendLine($"  骨骼路径总数（用于动画曲线匹配）{known.Count}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            return known;
        }

        // ------------------------------------------------------------- 3. 动画

        private static void ReportAnimations(StringBuilder sb, HashSet<string> modelPaths)
        {
            sb.AppendLine();
            sb.AppendLine("## 3. 七个状态需要的动画");
            sb.AppendLine();
            sb.AppendLine("  曲线路径必须全部落在模型的骨骼路径集合里，否则动画绑到的是另一套骨架。");
            sb.AppendLine("  位移通道如果有净变化，说明这条动画会自己推世界坐标（必须用 WO Root 版本）。");
            sb.AppendLine();
            sb.AppendLine($"  {"动作",-10}{"片段名",-30}{"长度",-9}{"帧率",-7}{"循环",-7}" +
                          $"{"曲线",-7}{"对不上",-8}{"根位移净变化",-26}");
            sb.AppendLine("  " + new string('-', 112));

            foreach (var (animation, fbx) in AnimationToFbx)
            {
                var path = AnimationFbxPath(fbx);
                var clip = LoadClip(path);
                if (clip == null)
                {
                    sb.AppendLine($"  {animation,-10}**找不到 {path}**");
                    continue;
                }

                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                var loop = importer != null && importer.clipAnimations != null &&
                           importer.clipAnimations.Length > 0
                    ? importer.clipAnimations[0].loopTime
                    : importer != null && importer.defaultClipAnimations.Length > 0 &&
                      importer.defaultClipAnimations[0].loopTime;

                var bindings = AnimationUtility.GetCurveBindings(clip);
                var unmatched = new List<string>();
                var rootDelta = Vector3.zero;
                foreach (var binding in bindings)
                {
                    if (!string.IsNullOrEmpty(binding.path) && !modelPaths.Contains(binding.path) &&
                        !unmatched.Contains(binding.path))
                    {
                        unmatched.Add(binding.path);
                    }

                    if (!binding.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // 根位移：没有层级前缀（或只有一段）的那个节点就是动画的根。
                    if (binding.path.IndexOf('/') >= 0)
                    {
                        continue;
                    }

                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    var value = curve.Evaluate(clip.length) - curve.Evaluate(0f);
                    switch (binding.propertyName)
                    {
                        case "m_LocalPosition.x":
                            rootDelta.x = value;
                            break;
                        case "m_LocalPosition.y":
                            rootDelta.y = value;
                            break;
                        case "m_LocalPosition.z":
                            rootDelta.z = value;
                            break;
                    }
                }

                sb.AppendLine(
                    $"  {animation,-10}{clip.name,-30}{clip.length,-9:F3}{clip.frameRate,-7:F0}" +
                    $"{(loop ? "是" : "否"),-7}{bindings.Length,-7}{unmatched.Count,-8}{V(rootDelta)}");

                foreach (var path2 in unmatched.Take(5))
                {
                    sb.AppendLine($"      对不上的路径：{path2}");
                }
            }
        }

        // ------------------------------------------------------------- 3.5 实际姿态几何

        /// <summary>
        /// 把 Idle 采样到模型上再量一次几何。
        ///
        /// 绑定姿态的包围盒不能直接用来摆碰撞体：蒙皮网格的 bounds 是作者烘焙的，
        /// 四足兽的绑定姿态经常是腿摊开或躺平的。真正要摆的是"游戏里站着的那个样子"。
        /// 同时列出骨骼清单与头部骨骼位置，咬击判定盒才能摆在嘴前面而不是靠猜。
        /// </summary>
        private static void ReportPosedGeometry(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("## 3.5 Idle 姿态下的实际几何（摆碰撞体与判定盒用）");
            sb.AppendLine();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var idle = LoadClip(AnimationFbxPath("Polygonal Wolf@Idle"));
            if (model == null || idle == null)
            {
                sb.AppendLine("  缺少模型或 Idle 片段，跳过。");
                return;
            }

            var instance = (GameObject)UnityEngine.Object.Instantiate(model);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                idle.SampleAnimation(instance, 0f);

                var bones = instance.GetComponentsInChildren<Transform>(true);
                var box = new Bounds(instance.transform.position, Vector3.zero);
                foreach (var bone in bones)
                {
                    box.Encapsulate(bone.position);
                }

                sb.AppendLine($"  骨骼位置包围盒   center {V(box.center)}  size {V(box.size)}");
                sb.AppendLine($"  最低骨骼 Y       {box.min.y:F3}");
                sb.AppendLine($"  最高骨骼 Y       {box.max.y:F3}");

                var skinned = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (skinned.Length > 0)
                {
                    // 蒙皮网格在 Idle 姿态下的真实世界包围盒。
                    var mesh = new Mesh();
                    skinned[0].BakeMesh(mesh);
                    var local = mesh.bounds;
                    UnityEngine.Object.DestroyImmediate(mesh);
                    sb.AppendLine($"  烘焙网格包围盒   center {V(local.center)}  size {V(local.size)}");
                    sb.AppendLine($"  脚底高度         {local.min.y:F3}");
                    sb.AppendLine($"  体长 Z / 体宽 X / 体高 Y   " +
                                  $"{local.size.z:F3} / {local.size.x:F3} / {local.size.y:F3}");
                }

                sb.AppendLine();
                sb.AppendLine("  头部相关骨骼（咬击判定盒的参考点）：");
                var found = false;
                foreach (var bone in bones)
                {
                    var n = bone.name;
                    if (n.IndexOf("Head", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Jaw", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Mouth", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Neck", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    found = true;
                    sb.AppendLine($"      {n,-28}世界位置 {V(bone.position)}");
                }

                if (!found)
                {
                    sb.AppendLine("      （没有名字带 Head/Jaw/Mouth/Neck 的骨骼）");
                }

                sb.AppendLine();
                sb.AppendLine("  全部骨骼名（共 " + (bones.Length - 1) + " 个，不含模型根）：");
                var line = new StringBuilder("     ");
                foreach (var bone in bones)
                {
                    if (bone == instance.transform)
                    {
                        continue;
                    }

                    if (line.Length > 96)
                    {
                        sb.AppendLine(line.ToString());
                        line = new StringBuilder("     ");
                    }

                    line.Append(' ').Append(bone.name);
                }

                if (line.Length > 5)
                {
                    sb.AppendLine(line.ToString());
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        // ------------------------------------------------------------- 4. 材质与贴图

        private static void ReportMaterials(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("## 4. 材质与贴图");
            sb.AppendLine();

            var material = AssetDatabase.LoadAssetAtPath<Material>(ThirdPartyMaterialPath);
            if (material == null)
            {
                sb.AppendLine($"  **找不到 {ThirdPartyMaterialPath}**");
            }
            else
            {
                var shader = material.shader;
                var shaderName = shader == null ? "<丢失>" : shader.name;
                var urpSafe = shaderName.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal) ||
                              shaderName.StartsWith("Shader Graphs/", StringComparison.Ordinal);
                sb.AppendLine($"  第三方材质  {ThirdPartyMaterialPath}");
                sb.AppendLine($"  Shader      {shaderName}");
                sb.AppendLine($"  URP 兼容    {(urpSafe ? "是" : "**否 —— URP 下会显示为粉色，必须建项目自有的 URP 适配材质**")}");
                sb.AppendLine($"  _MainTex    {TextureName(material, "_MainTex")}");
                sb.AppendLine($"  _EmissionMap{TextureName(material, "_EmissionMap")}");
            }

            sb.AppendLine();
            foreach (var path in new[] { BaseTexturePath, GlowTexturePath })
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                sb.AppendLine(texture == null
                    ? $"  贴图 **找不到 {path}**"
                    : $"  贴图 {path.Substring(WolfRoot.Length + 1),-42}{texture.width}×{texture.height}");
            }
        }

        private static string TextureName(Material material, string property)
        {
            if (!material.HasProperty(property))
            {
                return "(该 Shader 没有这个属性)";
            }

            var texture = material.GetTexture(property);
            return texture == null ? "(未设置)" : texture.name;
        }

        // ------------------------------------------------------------- 5. 演示内容

        private static void ReportDemoContent(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("## 5. 演示内容是否被误导入");
            sb.AppendLine();

            var scenes = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.IndexOf("Polygonal", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();
            sb.AppendLine(scenes.Length == 0
                ? "  第三方演示场景：工程里 0 个 ✓"
                : $"  第三方演示场景：**{scenes.Length} 个被导入**");
            foreach (var scene in scenes)
            {
                sb.AppendLine($"      {scene}");
            }

            var controllers = AssetDatabase.FindAssets("t:AnimatorController", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.StartsWith("Assets/Polygonal", StringComparison.Ordinal))
                .ToArray();
            sb.AppendLine(controllers.Length == 0
                ? "  第三方演示 Animator：工程里 0 个 ✓"
                : $"  第三方演示 Animator：**{controllers.Length} 个被导入**");

            var otherCreatures = AssetDatabase.IsValidFolder("Assets/Polygonal Creatures Pack")
                ? AssetDatabase.GetSubFolders("Assets/Polygonal Creatures Pack")
                : Array.Empty<string>();
            sb.AppendLine($"  Polygonal Creatures Pack 下的子目录：{otherCreatures.Length} 个");
            foreach (var folder in otherCreatures)
            {
                sb.AppendLine($"      {folder}");
            }

            sb.AppendLine();
            sb.AppendLine("  Build Settings 里的场景：");
            foreach (var scene in EditorBuildSettings.scenes)
            {
                sb.AppendLine($"      [{(scene.enabled ? "启用" : "停用")}] {scene.path}");
            }
        }

        // ------------------------------------------------------------- 工具

        private static AnimationClip LoadClip(string assetPath)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (asset is AnimationClip clip &&
                    !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    return clip;
                }
            }

            return null;
        }

        private static void CollectPaths(Transform root, Transform current, ISet<string> into)
        {
            if (current != root)
            {
                into.Add(AnimationUtility.CalculateTransformPath(current, root));
            }

            for (var i = 0; i < current.childCount; i++)
            {
                CollectPaths(root, current.GetChild(i), into);
            }
        }

        private static string V(Vector3 v) => $"({v.x,7:F3},{v.y,7:F3},{v.z,7:F3})";
    }
}
#endif
