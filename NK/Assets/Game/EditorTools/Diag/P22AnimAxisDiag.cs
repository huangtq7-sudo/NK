#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Naraka.EditorTools.Diag
{
    /// <summary>
    /// 2026-10-04 动画换版后的轴向诊断。
    ///
    /// 用户报告 idle / run / 待机动作 / V技能 / 冲刺 / 第二段 / 第三段 / 受击
    /// "贴在地上，方向轴不对"，并且走路与 idle 卡顿。这个工具只做测量，不改任何资产：
    ///
    /// 1. 正式模型 `Root` 与 `Bip001` 的静止姿态；
    /// 2. 每个动画状态在 `Root` 上的位移/旋转/缩放曲线（存在性与首末值）；
    /// 3. 把片段采样到正式模型上，用**骨骼世界坐标包围盒**判断站立还是躺平
    ///    （站立时 Y 跨度≈身高、Z 跨度很小；躺平时反过来），顺带看整体尺寸有没有缩小；
    /// 4. 三个循环片段的首末帧姿态差，用来定位卡顿。
    /// </summary>
    public static class P22AnimAxisDiag
    {
        private const string ModelPath = "Assets/Game/Art/Characters/Changli/Changli_TPose.fbx";
        private const string AnimationRoot = "Assets/Game/Player_Animation";
        private const string OutputPath = "artifacts/p22-anim-axis-diag.txt";

        /// <summary>（状态名, 动画文件名, 是否本轮换成了新版）。</summary>
        private static readonly (string State, string Fbx, bool IsNew)[] Clips =
        {
            ("Idle", "AM_Stand1_Action03", true),
            ("IdleVariation", "AM_Stand1_Action03_SEQ1", true),
            ("Walk", "Walk_F", true),
            ("Run", "Run_F", true),
            ("RunTurnback", "Run_Turnback", true),
            ("StopWalk", "Stop_Walk_R", true),
            ("StopRun", "Stop_Run_R", true),
            ("Dash", "Move_F", true),
            ("AttackCombo1", "Attack01", true),
            ("AttackCombo2", "AM_Summon", true),
            ("AttackCombo3", "Attack02", true),
            ("Charge", "Attack10", true),
            ("SkillF", "AM_Skill01", false),
            ("SkillV", "Attack04_1", true),
            ("HitStun", "Behit_B_L", true),
            ("Death", "AM_Death", true),
            ("SpawnBurstLobbyToMap01", "Burst02", true),
            ("SpawnBurstMap01ToMap02", "Burst01", true)
        };

        private static readonly string[] LoopingClips = { "Walk_F", "Run_F", "AM_Stand1_Action03" };

        [MenuItem("NARAKA/Diag/P2.2 Animation Axis Diag")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("NARAKA P2.2 动画轴向诊断  " +
                          DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine(new string('=', 100));

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"找不到正式角色模型 {ModelPath}");
                return;
            }

            var instance = (GameObject)UnityEngine.Object.Instantiate(model);
            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                ReportModelRest(sb, instance);
                ReportClips(sb, instance);
                ReportLoopSeams(sb, instance);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? ".");
            File.WriteAllText(OutputPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[轴向诊断] 已写出 {OutputPath}\n{sb}");
        }

        // ------------------------------------------------------------------ 模型静止姿态

        private static void ReportModelRest(StringBuilder sb, GameObject instance)
        {
            sb.AppendLine();
            sb.AppendLine("## 1. 正式模型的静止姿态（动画曲线写入的目标）");
            foreach (var name in new[] { "Root", "Bip001" })
            {
                var t = FindDeep(instance.transform, name);
                if (t == null)
                {
                    sb.AppendLine($"  {name,-10} <找不到>");
                    continue;
                }

                sb.AppendLine($"  {name,-10} localPos={V(t.localPosition)}  " +
                              $"localEuler={V(t.localRotation.eulerAngles)}  localScale={V(t.localScale)}");
            }

            var rootBone = FindDeep(instance.transform, "Root");
            sb.AppendLine($"  骨骼包围盒（T 姿势）size={V(BoneBounds(instance.transform).size)}" +
                          $"  → {Posture(rootBone)}");
        }

        // ------------------------------------------------------------------ 每个片段

        private static void ReportClips(StringBuilder sb, GameObject instance)
        {
            sb.AppendLine();
            sb.AppendLine("## 2. 每个动画状态在 Root 上的曲线，以及采样后的实际姿态");
            sb.AppendLine();
            sb.AppendLine("  列含义：rot/pos/scl = Root 上是否存在该通道的曲线；");
            sb.AppendLine("          ΔRootEuler  = 采样姿态相对模型静止姿态的 Root 旋转偏差（度）；");
            sb.AppendLine("          姿态         = `Root` 的局部 +Z（角色头顶方向）与世界 +Y 的夹角。");
            sb.AppendLine();

            var root = FindDeep(instance.transform, "Root");
            var restRot = root.localRotation;
            var restScale = root.localScale;

            sb.AppendLine($"  {"状态",-24}{"片段",-26}{"新",-4}{"长度",-8}" +
                          $"{"rot",-5}{"pos",-5}{"scl",-5}{"ΔRootEuler",-26}{"RootScale",-22}姿态");
            sb.AppendLine("  " + new string('-', 150));

            foreach (var (state, fbx, isNew) in Clips)
            {
                var clip = LoadClip(fbx);
                if (clip == null)
                {
                    sb.AppendLine($"  {state,-24}{fbx,-26}{(isNew ? "新" : "旧"),-4}<找不到片段>");
                    continue;
                }

                var hasRot = HasCurve(clip, "Root", "m_LocalRotation") ||
                             HasCurve(clip, "Root", "localEulerAnglesRaw");
                var hasPos = HasCurve(clip, "Root", "m_LocalPosition");
                var hasScl = HasCurve(clip, "Root", "m_LocalScale");

                // 采样到片段中点：首帧可能恰好是过渡帧，中点更有代表性。
                clip.SampleAnimation(instance, clip.length * 0.5f);
                var delta = (Quaternion.Inverse(restRot) * root.localRotation).eulerAngles;
                var scale = root.localScale;

                sb.AppendLine(
                    $"  {state,-24}{fbx,-26}{(isNew ? "新" : "旧"),-4}{clip.length,-8:F3}" +
                    $"{(hasRot ? "有" : "—"),-5}{(hasPos ? "有" : "—"),-5}{(hasScl ? "有" : "—"),-5}" +
                    $"{Signed(delta),-26}{Ratio(scale, restScale),-22}{Posture(root)}");
            }
        }

        // ------------------------------------------------------------------ 循环缝

        private static void ReportLoopSeams(StringBuilder sb, GameObject instance)
        {
            sb.AppendLine();
            sb.AppendLine("## 3. 循环片段的首末帧姿态差（卡顿定位）");
            sb.AppendLine();
            sb.AppendLine("  `loopTime` 打开时 Unity 把时间取模，pose(length) 的下一帧就是 pose(0)。");
            sb.AppendLine("  两者差得越多，每个循环就越明显地跳一下。");

            var bones = instance.GetComponentsInChildren<Transform>(true);
            foreach (var fbx in LoopingClips)
            {
                var clip = LoadClip(fbx);
                if (clip == null)
                {
                    continue;
                }

                var first = SamplePose(instance, bones, clip, 0f);
                var last = SamplePose(instance, bones, clip, clip.length);
                var oneFrameBefore = SamplePose(
                    instance, bones, clip, Mathf.Max(0f, clip.length - 1f / 60f));

                var seam = Compare(bones, first, last);
                var step = Compare(bones, oneFrameBefore, last);

                sb.AppendLine();
                sb.AppendLine($"  ### {fbx}  长度 {clip.length:F3}s  " +
                              $"（帧率 60 → {Mathf.RoundToInt(clip.length * 60f)} 帧）");
                sb.AppendLine($"    首↔末 最大骨骼旋转差 {seam.MaxAngle:F3}°（骨骼 {seam.MaxAngleBone}）" +
                              $"，超过 1° 的骨骼 {seam.Above1}/{bones.Length}，超过 5° 的 {seam.Above5}");
                sb.AppendLine($"    末帧前一帧↔末帧 最大旋转差 {step.MaxAngle:F3}°（单帧正常步进量，用作对照）");
                sb.AppendLine($"    首↔末 比值 = {(step.MaxAngle > 0.0001f ? seam.MaxAngle / step.MaxAngle : 0f):F1} 倍单帧步进" +
                              "（≈1 表示无缝，远大于 1 表示有跳变）");

                foreach (var line in seam.Top)
                {
                    sb.AppendLine($"      {line}");
                }
            }
        }

        private static Pose[] SamplePose(
            GameObject instance, Transform[] bones, AnimationClip clip, float time)
        {
            clip.SampleAnimation(instance, time);
            var pose = new Pose[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                pose[i] = new Pose(bones[i].localPosition, bones[i].localRotation);
            }

            return pose;
        }

        private static SeamResult Compare(Transform[] bones, Pose[] a, Pose[] b)
        {
            var result = new SeamResult { Top = new List<string>() };
            var ranked = new List<(float Angle, string Name)>();
            for (var i = 0; i < bones.Length; i++)
            {
                var angle = Quaternion.Angle(a[i].rotation, b[i].rotation);
                if (angle > 1f)
                {
                    result.Above1++;
                }

                if (angle > 5f)
                {
                    result.Above5++;
                }

                if (angle > result.MaxAngle)
                {
                    result.MaxAngle = angle;
                    result.MaxAngleBone = bones[i].name;
                }

                ranked.Add((angle, bones[i].name));
            }

            ranked.Sort((x, y) => y.Angle.CompareTo(x.Angle));
            for (var i = 0; i < Mathf.Min(8, ranked.Count); i++)
            {
                if (ranked[i].Angle < 0.5f)
                {
                    break;
                }

                result.Top.Add($"{ranked[i].Angle,8:F3}°  {ranked[i].Name}");
            }

            return result;
        }

        private struct SeamResult
        {
            public float MaxAngle;
            public string MaxAngleBone;
            public int Above1;
            public int Above5;
            public List<string> Top;
        }

        // ------------------------------------------------------------------ 工具

        private static Bounds BoneBounds(Transform root)
        {
            var bounds = new Bounds(root.position, Vector3.zero);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                bounds.Encapsulate(t.position);
            }

            return bounds;
        }

        /// <summary>
        /// 角色是否站着。判据取 `Root` 的局部 +Z 与世界 +Y 的夹角：
        /// `Root` 的局部 +Z 就是角色头顶方向（静止姿态 Rx(−90) 把它映射成世界 +Y）。
        ///
        /// 刻意不用骨骼包围盒：包围盒会被未参与动画的道具节点和根位移通道一起撑大，
        /// 位移大的动作（蓄力、V 技能）会被误判成躺平。
        /// </summary>
        private static string Posture(Transform root)
        {
            var tilt = Vector3.Angle(root.forward, Vector3.up);
            return tilt < 5f ? $"站立（偏 {tilt:F1}°）" : $"**偏离竖直 {tilt:F1}°**";
        }

        private static bool HasCurve(AnimationClip clip, string path, string propertyPrefix)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path == path &&
                    binding.propertyName.StartsWith(propertyPrefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static AnimationClip LoadClip(string fbx)
        {
            var path = $"{AnimationRoot}/{fbx}.fbx";
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    return clip;
                }
            }

            return null;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (var t in parent.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
        }

        private static string V(Vector3 v) =>
            $"({v.x,7:F3},{v.y,7:F3},{v.z,7:F3})";

        private static string Signed(Vector3 euler)
        {
            float Wrap(float a) => a > 180f ? a - 360f : a;
            return $"({Wrap(euler.x),7:F2},{Wrap(euler.y),7:F2},{Wrap(euler.z),7:F2})";
        }

        private static string Ratio(Vector3 actual, Vector3 rest)
        {
            var r = rest.x == 0f ? 0f : actual.x / rest.x;
            return $"{V(actual)} ×{r:F4}";
        }
    }
}
#endif
