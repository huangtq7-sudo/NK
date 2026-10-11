#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Naraka.EditorTools.Diag
{
    /// <summary>
    /// 手感问题的离线测量：走跑的动画自身步速、受击动画在哪里已经"演完"、F 技能实际节奏、
    /// 出场动画片段里都有些什么曲线。
    ///
    /// 用户报告"受击罚站太久""F 太快""走跑太慢""出场看不到"。每一条都有不止一种可能，
    /// 这里不预设结论，只把片段本身量出来，让改动落在动画给出的节奏上。
    ///
    /// 只读测量，不改任何资产。
    /// </summary>
    public static class PreP4FeelScan
    {
        private const string ModelPath = "Assets/Game/Art/Characters/Changli/Changli_TPose.fbx";
        private const string ControllerPath = "Assets/Game/Settings/Player/NarakaPlayerAnimator.controller";
        private const string OutputPath = "artifacts/ci/unity/pre-p4-feel-scan.txt";
        private const float Frame = 1f / 60f;

        [MenuItem("NARAKA/Diag/Pre-P4 Feel Scan")]
        public static void Run() => Write(Scan());

        /// <summary>批处理入口：<c>-executeMethod Naraka.EditorTools.Diag.PreP4FeelScan.RunBatch</c>。</summary>
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

            var clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (var layer in controller.layers)
            {
                foreach (var child in layer.stateMachine.states)
                {
                    if (child.state.motion is AnimationClip clip)
                    {
                        clips[child.state.name] = clip;
                    }
                }
            }

            var tuning = tuningAsset.ToTuning();
            var sb = new StringBuilder();
            sb.AppendLine("手感问题离线测量  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine(new string('=', 100));

            var instance = (GameObject)UnityEngine.Object.Instantiate(model);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var bones = instance.GetComponentsInChildren<Transform>(true);
                var visible = Visible(instance, bones);
                var root = Find(bones, "Root");
                var feet = FindFeet(bones);

                sb.AppendLine();
                sb.AppendLine("## 1. 走 / 跑：动画自身步速与当前移动速度");
                sb.AppendLine();
                sb.AppendLine($"  脚骨骼：{string.Join(", ", feet.ConvertAll(f => f.name))}");
                Locomotion(sb, instance, root, feet, clips, PlayerAnimatorProjector.StateNames.Walk,
                    tuning.Locomotion.WalkSpeed);
                Locomotion(sb, instance, root, feet, clips, PlayerAnimatorProjector.StateNames.Run,
                    tuning.Locomotion.RunSpeed);

                sb.AppendLine();
                sb.AppendLine("## 2. 受击：动作在哪里已经演完");
                sb.AppendLine();
                if (clips.TryGetValue(PlayerAnimatorProjector.StateNames.HitStun, out var hit) &&
                    clips.TryGetValue(PlayerAnimatorProjector.StateNames.Idle, out var idle))
                {
                    Profile(sb, instance, bones, visible, root, hit, idle, tuning.Reaction.HitStunSeconds, 1f);
                }

                sb.AppendLine();
                sb.AppendLine("## 3. F 技能当前节奏");
                sb.AppendLine();
                Timed(sb, clips, PlayerAnimatorProjector.StateNames.SkillF, tuning.SkillF.Action);
                if (clips.TryGetValue(PlayerAnimatorProjector.StateNames.SkillF, out var skillF) &&
                    clips.TryGetValue(PlayerAnimatorProjector.StateNames.Idle, out var idleForF))
                {
                    Profile(sb, instance, bones, visible, root, skillF, idleForF,
                        tuning.SkillF.Action.ClipSeconds, tuning.SkillF.Action.Playback.MainSpeed);
                }

                sb.AppendLine();
                sb.AppendLine("## 4. 出场动画片段");
                sb.AppendLine();
                Timed(sb, clips, PlayerAnimatorProjector.StateNames.SpawnBurstLobbyToMap01, tuning.Reaction.SpawnLobbyToMap01);
                Timed(sb, clips, PlayerAnimatorProjector.StateNames.SpawnBurstMap01ToMap02, tuning.Reaction.SpawnMap01ToMap02);
                Bindings(sb, clips, PlayerAnimatorProjector.StateNames.SpawnBurstLobbyToMap01);
                Bindings(sb, clips, PlayerAnimatorProjector.StateNames.SpawnBurstMap01ToMap02);
                Scale(sb, instance, bones, visible, clips, PlayerAnimatorProjector.StateNames.SpawnBurstLobbyToMap01);
                Scale(sb, instance, bones, visible, clips, PlayerAnimatorProjector.StateNames.SpawnBurstMap01ToMap02);
                Scale(sb, instance, bones, visible, clips, PlayerAnimatorProjector.StateNames.Idle);
                if (clips.TryGetValue(PlayerAnimatorProjector.StateNames.SpawnBurstLobbyToMap01, out var burst02) &&
                    clips.TryGetValue(PlayerAnimatorProjector.StateNames.Idle, out var idleForBurst))
                {
                    Profile(sb, instance, bones, visible, root, burst02, idleForBurst,
                        tuning.Reaction.SpawnLobbyToMap01.ClipSeconds,
                        tuning.Reaction.SpawnLobbyToMap01.Playback.MainSpeed);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 原地循环动画的"脚下速度"：支撑脚（较低的那只）相对根部向后滑的水平速度。
        /// 角色以这个速度前进时脚才是踩实的；移动速度比它快，脚就在地上往后拖。
        /// </summary>
        private static void Locomotion(
            StringBuilder sb, GameObject instance, Transform root, List<Transform> feet,
            Dictionary<string, AnimationClip> clips, string state, float configured)
        {
            if (!clips.TryGetValue(state, out var clip) || feet.Count < 2)
            {
                sb.AppendLine($"  {state}：缺少片段或脚骨骼");
                return;
            }

            var frames = Mathf.Max(2, Mathf.RoundToInt(clip.length / Frame));
            clip.SampleAnimation(instance, 0f);
            var rootStart = root != null ? root.position : Vector3.zero;
            var speeds = new List<float>();
            var previous = new Vector3[feet.Count];
            var previousRoot = rootStart;
            for (var i = 0; i < feet.Count; i++)
            {
                previous[i] = feet[i].position;
            }

            for (var f = 1; f <= frames; f++)
            {
                clip.SampleAnimation(instance, Mathf.Min(f * Frame, clip.length));
                var low = 0;
                for (var i = 1; i < feet.Count; i++)
                {
                    if (feet[i].position.y < feet[low].position.y)
                    {
                        low = i;
                    }
                }

                var delta = feet[low].position - previous[low];
                // 片段若带根位移，支撑脚相对根部的后滑才是步速；原地片段根部不动，两者相同。
                var rootNow = root != null ? root.position : Vector3.zero;
                var rootDelta = rootNow - previousRoot;
                previousRoot = rootNow;
                speeds.Add(new Vector2(delta.x - rootDelta.x, delta.z - rootDelta.z).magnitude / Frame);
                for (var i = 0; i < feet.Count; i++)
                {
                    previous[i] = feet[i].position;
                }
            }

            clip.SampleAnimation(instance, clip.length);
            var rootTravel = root != null ? root.position - rootStart : Vector3.zero;
            speeds.Sort();
            var median = speeds[speeds.Count / 2];
            sb.AppendLine(
                $"  {state,-6} 片段 {clip.name}  长度 {clip.length:F3}s  循环 {clip.isLooping}  " +
                $"根骨骼一圈水平位移 {new Vector2(rootTravel.x, rootTravel.z).magnitude:F3} 米");
            sb.AppendLine(
                $"         支撑脚后滑速度（中位数）{median:F2} 米/秒   当前移动速度 {configured:F2} 米/秒   " +
                $"比值 {configured / Mathf.Max(0.01f, median):F2}");
        }

        /// <summary>
        /// 逐 0.05 秒：可见身体骨骼的最大角速度（动作还在不在演）、与 Idle 首帧的姿态差、根高度。
        /// </summary>
        private static void Profile(
            StringBuilder sb, GameObject instance, Transform[] bones, bool[] visible, Transform root,
            AnimationClip clip, AnimationClip idle, float configuredClipSeconds, float speed)
        {
            var idlePose = Capture(instance, bones, idle, 0f);
            sb.AppendLine(
                $"  片段 {clip.name}  长度 {clip.length:F3}s  当前业务时长（片段时间）{configuredClipSeconds:F3}s  " +
                $"播放倍率 {speed:F2}");
            sb.AppendLine($"  {"片段秒",-8}{"真实秒",-8}{"身体角速度°/s",-16}{"离Idle°",-10}{"根高",-8}");
            var step = 0.05f;
            var settled = -1f;
            Quaternion[] before = Capture(instance, bones, clip, 0f);
            for (var t = step; t <= clip.length + 1e-4f; t += step)
            {
                var pose = Capture(instance, bones, clip, Mathf.Min(t, clip.length));
                var motion = MaxBody(bones, visible, before, pose) / step;
                var gap = MaxBody(bones, visible, pose, idlePose);
                clip.SampleAnimation(instance, Mathf.Min(t, clip.length));
                var height = root != null ? root.position.y : 0f;
                sb.AppendLine($"  {t,-8:F2}{t / speed,-8:F2}{motion,-16:F1}{gap,-10:F1}{height,-8:F3}");
                if (settled < 0f && motion < 60f && t > 0.2f)
                {
                    settled = t;
                }
                else if (motion >= 60f)
                {
                    settled = -1f;
                }

                before = pose;
            }

            sb.AppendLine(settled >= 0f
                ? $"  身体角速度最后一次降到 60°/s 以下并保持到结尾：片段 {settled:F2}s"
                : "  身体到结尾仍在动（角速度 ≥ 60°/s）");
        }

        /// <summary>
        /// 片段带缩放曲线时，角色在某段时间里可能被缩到看不见（"凭空出现"式的出场就是这么做的）。
        /// 逐 0.1 秒给出可见骨骼世界缩放的最小 / 中位 / 最大值。
        /// </summary>
        private static void Scale(
            StringBuilder sb, GameObject instance, Transform[] bones, bool[] visible,
            Dictionary<string, AnimationClip> clips, string state)
        {
            if (!clips.TryGetValue(state, out var clip))
            {
                return;
            }

            sb.AppendLine($"  {state} 可见骨骼世界缩放（片段秒：最小 / 中位 / 最大）");
            var values = new List<float>();
            for (var t = 0f; t <= clip.length + 1e-4f; t += 0.1f)
            {
                clip.SampleAnimation(instance, Mathf.Min(t, clip.length));
                values.Clear();
                for (var i = 0; i < bones.Length; i++)
                {
                    if (visible[i])
                    {
                        values.Add(bones[i].lossyScale.magnitude / Mathf.Sqrt(3f));
                    }
                }

                values.Sort();
                sb.AppendLine($"    {t,5:F1}s  {values[0],7:F3} / {values[values.Count / 2],7:F3} / {values[values.Count - 1],7:F3}");
            }
        }

        private static void Timed(
            StringBuilder sb, Dictionary<string, AnimationClip> clips, string state, TimedActionTuning action)
        {
            clips.TryGetValue(state, out var clip);
            var p = action.Playback;
            sb.AppendLine(
                $"  {state,-24} 片段 {(clip != null ? clip.name : "-")} 长度 {(clip != null ? clip.length : 0f):F3}s  " +
                $"业务片段长 {action.ClipSeconds:F3}s  主体倍率 {p.MainSpeed:F2} 后摇起点 {p.RecoveryStartSeconds:F3}s " +
                $"后摇倍率 {p.RecoverySpeed:F2}  真实时长 {action.RealDurationSeconds:F3}s  " +
                $"命中窗（片段）{action.HitWindowStart:F3}–{action.HitWindowEnd:F3}s");
        }

        private static void Bindings(StringBuilder sb, Dictionary<string, AnimationClip> clips, string state)
        {
            if (!clips.TryGetValue(state, out var clip))
            {
                return;
            }

            var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var key = binding.type.Name + "." + Bucket(binding.propertyName);
                kinds[key] = kinds.TryGetValue(key, out var n) ? n + 1 : 1;
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var key = "[对象引用] " + binding.type.Name + "." + binding.propertyName;
                kinds[key] = kinds.TryGetValue(key, out var n) ? n + 1 : 1;
            }

            var events = AnimationUtility.GetAnimationEvents(clip);
            sb.AppendLine($"  {state} 曲线种类：");
            foreach (var pair in kinds)
            {
                sb.AppendLine($"    {pair.Key,-40} × {pair.Value}");
            }

            sb.AppendLine($"    动画事件 {events.Length} 个");
        }

        private static string Bucket(string property)
        {
            var dot = property.IndexOf('.');
            return dot > 0 ? property.Substring(0, dot) : property;
        }

        private static float MaxBody(Transform[] bones, bool[] visible, Quaternion[] a, Quaternion[] b)
        {
            var max = 0f;
            for (var i = 0; i < bones.Length; i++)
            {
                var n = bones[i].name;
                if (!visible[i] || n == "Root" ||
                    n.StartsWith("Bone_Hair", StringComparison.Ordinal) ||
                    n.StartsWith("Bone_Piao", StringComparison.Ordinal) ||
                    n.StartsWith("Bangs", StringComparison.Ordinal) ||
                    n.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                max = Mathf.Max(max, Quaternion.Angle(a[i], b[i]));
            }

            return max;
        }

        private static Quaternion[] Capture(GameObject instance, Transform[] bones, AnimationClip clip, float time)
        {
            clip.SampleAnimation(instance, time);
            var pose = new Quaternion[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                pose[i] = bones[i].localRotation;
            }

            return pose;
        }

        private static Transform Find(Transform[] bones, string name)
        {
            foreach (var bone in bones)
            {
                if (bone.name == name)
                {
                    return bone;
                }
            }

            return null;
        }

        private static List<Transform> FindFeet(Transform[] bones)
        {
            var result = new List<Transform>();
            foreach (var bone in bones)
            {
                var n = bone.name;
                if ((n.EndsWith("Foot", StringComparison.OrdinalIgnoreCase) ||
                     n.IndexOf("_Foot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     n.IndexOf(" Foot", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    n.IndexOf("Twist", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    result.Add(bone);
                }
            }

            if (result.Count >= 2)
            {
                return result;
            }

            result.Clear();
            foreach (var bone in bones)
            {
                if (bone.name.IndexOf("Toe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    bone.name.IndexOf("Ankle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    bone.name.IndexOf("Calf", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(bone);
                }
            }

            return result;
        }

        private static bool[] Visible(GameObject instance, Transform[] bones)
        {
            var used = new HashSet<Transform>();
            foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (var bone in skin.bones)
                {
                    if (bone != null)
                    {
                        used.Add(bone);
                    }
                }
            }

            var visible = new bool[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                visible[i] = used.Contains(bones[i]);
            }

            return visible;
        }

        private static void Write(string report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? ".");
            File.WriteAllText(OutputPath, report, new UTF8Encoding(false));
            Debug.Log($"[手感测量] 已写出 {OutputPath}\n{report}");
        }
    }
}
#endif
