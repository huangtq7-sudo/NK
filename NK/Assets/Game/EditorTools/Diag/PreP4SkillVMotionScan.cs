#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Text;
using Naraka.Features.Character.View;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Naraka.EditorTools.Diag
{
    /// <summary>
    /// V 技能动画自身的冲刺节奏。
    ///
    /// 片段在根骨骼上烘焙了 17.837 米的水平位移（运行期被 RootMotionCanceller 抵消，坐标由代码驱动）。
    /// 也就是说"什么时候原地蓄势、什么时候起步、多快冲完"本来就编码在动画里。
    /// 这个工具把它逐帧量出来，让代码驱动的位移跟着动画的节奏走，而不是凭感觉填一个窗口。
    ///
    /// 只读测量，不改任何资产。
    /// </summary>
    public static class PreP4SkillVMotionScan
    {
        private const string ModelPath = "Assets/Game/Art/Characters/Changli/Changli_TPose.fbx";
        private const string ControllerPath = "Assets/Game/Settings/Player/NarakaPlayerAnimator.controller";
        private const string OutputPath = "artifacts/ci/unity/pre-p4-skill-v-motion-scan.txt";
        private const float Frame = 1f / 60f;

        [MenuItem("NARAKA/Diag/Pre-P4 Skill V Motion Scan")]
        public static void Run() => Write(Scan());

        /// <summary>批处理入口：<c>-executeMethod Naraka.EditorTools.Diag.PreP4SkillVMotionScan.RunBatch</c>。</summary>
        public static void RunBatch()
        {
            try
            {
                Write(Scan());
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static string Scan()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var tuningAsset = AssetDatabase.LoadAssetAtPath<PlayerTuningAsset>(P2SceneSetup.PlayerTuningPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (controller == null || tuningAsset == null || model == null)
            {
                throw new InvalidOperationException("缺少 Animator Controller、PlayerTuning 或角色模型。");
            }

            AnimationClip clip = null;
            foreach (var layer in controller.layers)
            {
                foreach (var child in layer.stateMachine.states)
                {
                    if (child.state.name == PlayerAnimatorProjector.StateNames.SkillV)
                    {
                        clip = child.state.motion as AnimationClip;
                    }
                }
            }

            if (clip == null)
            {
                throw new InvalidOperationException("Animator Controller 里没有 SkillV 状态或它没有片段。");
            }

            var v = tuningAsset.ToTuning().SkillV.Action;
            var sb = new StringBuilder();
            sb.AppendLine("V 技能动画冲刺节奏  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine(new string('=', 96));

            var instance = (GameObject)UnityEngine.Object.Instantiate(model);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Transform root = null;
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Root")
                    {
                        root = t;
                        break;
                    }
                }

                if (root == null)
                {
                    throw new InvalidOperationException("模型里找不到名为 Root 的骨骼。");
                }

                var frames = Mathf.CeilToInt(clip.length / Frame);
                var distance = new float[frames + 1];
                clip.SampleAnimation(instance, 0f);
                var start = root.position;
                for (var f = 0; f <= frames; f++)
                {
                    clip.SampleAnimation(instance, Mathf.Min(f * Frame, clip.length));
                    var d = root.position - start;
                    distance[f] = new Vector2(d.x, d.z).magnitude;
                }

                var total = distance[frames];
                var peak = 0f;
                for (var f = 0; f <= frames; f++)
                {
                    total = Mathf.Max(total, distance[f]);
                }

                var begin = FirstFrameAtOrAbove(distance, total * 0.02f);
                var finish = FirstFrameAtOrAbove(distance, total * 0.98f);
                var peakFrame = 0;
                for (var f = 1; f <= frames; f++)
                {
                    var speed = (distance[f] - distance[f - 1]) / Frame;
                    if (speed > peak)
                    {
                        peak = speed;
                        peakFrame = f;
                    }
                }

                var main = v.Playback.MainSpeed;
                sb.AppendLine();
                sb.AppendLine($"片段 {clip.name}  长度 {clip.length:F3}s（片段时间）  主体播放倍率 {main:F2}");
                sb.AppendLine($"根骨骼水平总位移 {total:F3} 米（当前配置 {v.ForwardDisplacement:F3} 米，" +
                              $"当前位移窗口 0 → {v.DisplacementClipSeconds:F3}s）");
                sb.AppendLine();
                sb.AppendLine("## 动画自身的节奏（片段时间 / 按主体倍率换算的真实时间）");
                sb.AppendLine();
                sb.AppendLine($"  原地蓄势：0 → {begin * Frame:F3}s        真实 {begin * Frame / main:F3}s");
                sb.AppendLine($"  冲刺    ：{begin * Frame:F3} → {finish * Frame:F3}s   真实 {(finish - begin) * Frame / main:F3}s");
                var dashClip = Mathf.Max(Frame, (finish - begin) * Frame);
                sb.AppendLine($"  冲刺平均速度 {total * 0.96f / dashClip * main:F1} 米/秒（真实时间）");
                sb.AppendLine($"  峰值速度    {peak * main:F1} 米/秒（真实时间），出现在片段 {peakFrame * Frame:F3}s");
                sb.AppendLine();
                sb.AppendLine("## 逐帧（每 2 帧一行，片段时间）");
                sb.AppendLine();
                sb.AppendLine($"  {"片段秒",-10}{"真实秒",-10}{"累计位移",-12}{"占比",-8}速度（米/秒，真实时间）");
                for (var f = 0; f <= Mathf.Min(frames, Mathf.CeilToInt(1.6f / Frame)); f += 2)
                {
                    var speed = f == 0 ? 0f : (distance[f] - distance[f - 1]) / Frame * main;
                    sb.AppendLine($"  {f * Frame,-10:F3}{f * Frame / main,-10:F3}{distance[f],-12:F3}" +
                                  $"{distance[f] / Mathf.Max(total, 1e-5f),-8:P0}{speed:F1}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            return sb.ToString();
        }

        private static int FirstFrameAtOrAbove(float[] values, float threshold)
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] >= threshold)
                {
                    return i;
                }
            }

            return values.Length - 1;
        }

        private static void Write(string report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? ".");
            File.WriteAllText(OutputPath, report, new UTF8Encoding(false));
            Debug.Log($"[V 技能冲刺节奏] 已写出 {OutputPath}\n{report}");
        }
    }
}
#endif
