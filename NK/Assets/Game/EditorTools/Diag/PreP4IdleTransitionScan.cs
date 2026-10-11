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
    /// 动作结束切回 Idle 时，姿态到底在哪里不连续。
    ///
    /// 用户报告"所有动画结束后回 Idle 都有卡帧"。候选原因不止一个，而且彼此会被误认成对方：
    /// 头发/裙摆从运动中被拉回 idle 首帧的静止姿态、根骨骼带着净旋转被淡入拧回、
    /// 片段比业务时长短而停在末帧、片段比业务时长长而被中途截断……
    /// 这个工具不预设是哪一个，只把每个"回 Idle"的来源逐一量出来：
    ///
    /// 1. 业务在哪个片段时间点把它交还给 Idle，片段在那之前是否已经播完（停在末帧多久），
    ///    或者还没播完就被截断；
    /// 2. 交接那一刻的姿态与 Idle 首帧差多少 —— 头发/飘带、身体、根骨骼分开统计；
    /// 3. 交接前后的角速度：来源最后一帧在动多快，Idle 开头在动多快。
    ///
    /// 状态与片段的对应直接从实际的 Animator Controller 读，业务时长直接从场景实际引用的
    /// PlayerTuning.asset 读，不用代码里的基线值。只读测量，不改任何资产。
    /// </summary>
    public static class PreP4IdleTransitionScan
    {
        private const string ModelPath = "Assets/Game/Art/Characters/Changli/Changli_TPose.fbx";
        private const string ControllerPath = "Assets/Game/Settings/Player/NarakaPlayerAnimator.controller";
        private const string OutputPath = "artifacts/ci/unity/pre-p4-idle-transition-scan.txt";
        private const float Frame = 1f / 60f;

        [MenuItem("NARAKA/Diag/Pre-P4 Idle Transition Scan")]
        public static void Run() => Write(Scan());

        /// <summary>批处理入口：<c>-executeMethod Naraka.EditorTools.Diag.PreP4IdleTransitionScan.RunBatch</c>。</summary>
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
            var sb = new StringBuilder();
            sb.AppendLine("回 Idle 交接点姿态扫描  " +
                          DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine(new string('=', 120));

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var tuningAsset = AssetDatabase.LoadAssetAtPath<PlayerTuningAsset>(P2SceneSetup.PlayerTuningPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (controller == null || tuningAsset == null || model == null)
            {
                throw new InvalidOperationException(
                    $"缺少必要资产：controller={controller != null} tuning={tuningAsset != null} model={model != null}");
            }

            var clips = ClipsByState(controller);
            if (!clips.TryGetValue(PlayerAnimatorProjector.StateNames.Idle, out var idle))
            {
                throw new InvalidOperationException("Animator Controller 里没有 Idle 状态。");
            }

            var tuning = tuningAsset.ToTuning();
            var instance = (GameObject)UnityEngine.Object.Instantiate(model);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var bones = instance.GetComponentsInChildren<Transform>(true);
                var groups = Classify(bones);
                _visible = Visible(instance, bones);

                var idleStart = Capture(instance, bones, idle, 0f);
                var idleNext = Capture(instance, bones, idle, Frame);
                var idleStartSpeed = GroupMax(bones, groups, idleStart, idleNext, 1f / Frame);

                sb.AppendLine();
                sb.AppendLine($"Idle 片段 {idle.name}  长度 {idle.length:F3}s  循环 {idle.isLooping}");
                sb.AppendLine($"骨骼 {bones.Length} 个，其中驱动可见网格的：头发与飘带 {Count(groups, Group.Hair)}，" +
                              $"身体 {Count(groups, Group.Body)}，武器 {Count(groups, Group.Weapon)}，" +
                              $"根骨骼 {Count(groups, Group.Root)}（以下只统计这些可见骨骼）");
                sb.AppendLine($"Idle 开头角速度（°/s）：头发 {idleStartSpeed[(int)Group.Hair].Value,7:F1}  " +
                              $"身体 {idleStartSpeed[(int)Group.Body].Value,7:F1}  " +
                              $"武器 {idleStartSpeed[(int)Group.Weapon].Value,7:F1}  " +
                              $"根 {idleStartSpeed[(int)Group.Root].Value,7:F1}");

                sb.AppendLine();
                sb.AppendLine("## 1. 每个来源交还 Idle 的那一刻");
                sb.AppendLine();
                sb.AppendLine("  交接点  = 业务把它交还 Idle 时的片段时间（不超过片段实际长度）");
                sb.AppendLine("  末帧停留 = 片段先播完、停在最后一帧等业务结束的真实秒数（>0 就是一段静止）");
                sb.AppendLine("  截断    = 片段还剩多少没播完就被切走（>0 就是从动作中途拉回 Idle）");
                sb.AppendLine("  姿态差  = 交接时的姿态与 Idle 首帧的最大骨骼角度差，0.1 秒淡入要抹平的量");
                sb.AppendLine("  末速度  = 来源最后一帧的最大角速度（已乘结尾播放倍率），与 Idle 开头角速度对照");
                sb.AppendLine();
                sb.AppendLine(
                    $"  {"来源状态",-24}{"长度",7}{"配置",7}{"倍率",6}{"末帧停留",9}{"截断",7}" +
                    $"{"头发差°",9}{"身体差°",9}{"武器差°",9}{"根旋转°",9}{"根高差",8}" +
                    $"{"头发末速",10}{"身体末速",10}{"武器末速",10}  身体最大差骨骼 / 武器最大差骨骼");
                sb.AppendLine("  " + new string('-', 170));

                foreach (var source in Sources(tuning))
                {
                    if (!clips.TryGetValue(source.State, out var clip))
                    {
                        sb.AppendLine($"  {source.State,-24}（Controller 里没有这个状态）");
                        continue;
                    }

                    var handoff = Mathf.Min(source.ClipSeconds, clip.length);
                    var hold = Mathf.Max(0f, source.ClipSeconds - clip.length) / source.EndSpeed;
                    var cut = Mathf.Max(0f, clip.length - source.ClipSeconds);

                    var end = Capture(instance, bones, clip, handoff);
                    var beforeEnd = Capture(instance, bones, clip, Mathf.Max(0f, handoff - Frame));
                    var gap = GroupMax(bones, groups, end, idleStart, 1f);
                    var endSpeed = GroupMax(bones, groups, beforeEnd, end, source.EndSpeed / Frame);

                    var rootIndex = Array.IndexOf(groups, Group.Root);
                    var rootHeight = rootIndex >= 0
                        ? SampleRootHeight(instance, bones[rootIndex], clip, handoff) -
                          SampleRootHeight(instance, bones[rootIndex], idle, 0f)
                        : 0f;

                    sb.AppendLine(
                        $"  {source.State,-24}{clip.length,7:F3}{source.ClipSeconds,7:F3}" +
                        $"{source.EndSpeed,6:F2}{hold,9:F3}{cut,7:F3}" +
                        $"{gap[(int)Group.Hair].Value,9:F2}{gap[(int)Group.Body].Value,9:F2}" +
                        $"{gap[(int)Group.Weapon].Value,9:F2}" +
                        $"{gap[(int)Group.Root].Value,9:F2}{rootHeight,8:F3}" +
                        $"{endSpeed[(int)Group.Hair].Value,10:F1}{endSpeed[(int)Group.Body].Value,10:F1}" +
                        $"{endSpeed[(int)Group.Weapon].Value,10:F1}" +
                        $"  {gap[(int)Group.Body].Bone} / {gap[(int)Group.Weapon].Bone}");
                }

                sb.AppendLine();
                sb.AppendLine("## 2. 读法");
                sb.AppendLine();
                sb.AppendLine("  头发差远大于身体差、根旋转很小 → 主要是头发模拟不连续（导出端问题，代码只能减轻）。");
                sb.AppendLine("  根旋转明显           → 该片段结束时带着净旋转，淡入时被拧回。");
                sb.AppendLine("  末帧停留 > 一帧        → 片段比业务时长短，角色先静止再切 Idle。");
                sb.AppendLine("  截断 > 一帧            → 片段比业务时长长，动作中途被拉回 Idle。");
                sb.AppendLine("  末速度远大于 Idle 开头 → 交接时运动突然停住，倍率断崖会放大这种感觉。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 每个来源交还 Idle 那一刻与 Idle 首帧的最大姿态差（度），只统计驱动可见网格的骨骼。
        /// 回归测试与本报告用的是同一套测量，结论不会各说各话。
        /// </summary>
        public static IReadOnlyList<(string State, float HairGap, float BodyGap)> MeasureReturnGaps()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var tuningAsset = AssetDatabase.LoadAssetAtPath<PlayerTuningAsset>(P2SceneSetup.PlayerTuningPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (controller == null || tuningAsset == null || model == null)
            {
                throw new InvalidOperationException("缺少 Animator Controller、PlayerTuning 或角色模型。");
            }

            var clips = ClipsByState(controller);
            var idle = clips[PlayerAnimatorProjector.StateNames.Idle];
            var result = new List<(string, float, float)>();
            var instance = (GameObject)UnityEngine.Object.Instantiate(model);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var bones = instance.GetComponentsInChildren<Transform>(true);
                var groups = Classify(bones);
                _visible = Visible(instance, bones);
                var idleStart = Capture(instance, bones, idle, 0f);

                foreach (var source in Sources(tuningAsset.ToTuning()))
                {
                    if (!clips.TryGetValue(source.State, out var clip))
                    {
                        continue;
                    }

                    var end = Capture(instance, bones, clip, Mathf.Min(source.ClipSeconds, clip.length));
                    var gap = GroupMax(bones, groups, end, idleStart, 1f);
                    result.Add((source.State, gap[(int)Group.Hair].Value, gap[(int)Group.Body].Value));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            return result;
        }

        private static IEnumerable<Source> Sources(PlayerTuning tuning)
        {
            yield return Timed(PlayerAnimatorProjector.StateNames.AttackCombo1, tuning.Combo.Step1);
            yield return Timed(PlayerAnimatorProjector.StateNames.AttackCombo2, tuning.Combo.Step2);
            yield return Timed(PlayerAnimatorProjector.StateNames.AttackCombo3, tuning.Combo.Step3);
            yield return Timed(PlayerAnimatorProjector.StateNames.Charge, tuning.Charge.Action);
            yield return Timed(PlayerAnimatorProjector.StateNames.SkillF, tuning.SkillF.Action);
            yield return Timed(PlayerAnimatorProjector.StateNames.SkillV, tuning.SkillV.Action);
            yield return Timed(PlayerAnimatorProjector.StateNames.Dash, tuning.Dash);
            yield return new Source(PlayerAnimatorProjector.StateNames.StopWalk, tuning.Locomotion.StopWalkSeconds, 1f);
            yield return new Source(PlayerAnimatorProjector.StateNames.StopRun, tuning.Locomotion.StopRunSeconds, 1f);
            // 不操作时受击整段播完，结尾那段按硬直后的倍率播放。
            yield return new Source(
                PlayerAnimatorProjector.StateNames.HitStun, tuning.Reaction.HitStunSeconds,
                tuning.Reaction.HitStunRecoverySpeed);
            yield return new Source(
                PlayerAnimatorProjector.StateNames.IdleVariation, tuning.Idle.VariationDurationSeconds, 1f);
            yield return Timed(PlayerAnimatorProjector.StateNames.SpawnBurstLobbyToMap01, tuning.Reaction.SpawnLobbyToMap01);
            yield return Timed(PlayerAnimatorProjector.StateNames.SpawnBurstMap01ToMap02, tuning.Reaction.SpawnMap01ToMap02);
        }

        private static Source Timed(string state, TimedActionTuning action) =>
            new Source(state, action.ClipSeconds, action.Playback.RecoverySpeed);

        private static Dictionary<string, AnimationClip> ClipsByState(AnimatorController controller)
        {
            var result = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (var layer in controller.layers)
            {
                foreach (var child in layer.stateMachine.states)
                {
                    if (child.state.motion is AnimationClip clip)
                    {
                        result[child.state.name] = clip;
                    }
                }
            }

            return result;
        }

        private enum Group
        {
            Hair = 0,
            Body = 1,
            Root = 2,
            Weapon = 3
        }

        private const int GroupCount = 4;

        private static Group[] Classify(Transform[] bones)
        {
            var groups = new Group[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                var n = bones[i].name;
                groups[i] = n == "Root"
                    ? Group.Root
                    : n.StartsWith("Bone_Hair", StringComparison.Ordinal) ||
                      n.StartsWith("Bone_Piao", StringComparison.Ordinal) ||
                      n.StartsWith("Bangs", StringComparison.Ordinal)
                        ? Group.Hair
                        : n.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0
                            ? Group.Weapon
                            : Group.Body;
            }

            return groups;
        }

        private static bool[] _visible = Array.Empty<bool>();

        /// <summary>
        /// 哪些骨骼真的会被看见：被任何蒙皮网格引用，或者下面直接挂着网格（刚性挂件，例如武器）。
        /// </summary>
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

            foreach (var mesh in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                for (var t = mesh.transform; t != null; t = t.parent)
                {
                    used.Add(t);
                }
            }

            var visible = new bool[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                visible[i] = used.Contains(bones[i]);
            }

            return visible;
        }

        private static int Count(Group[] groups, Group group)
        {
            var count = 0;
            for (var i = 0; i < groups.Length; i++)
            {
                if (groups[i] == group && _visible[i])
                {
                    count++;
                }
            }

            return count;
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

        private static float SampleRootHeight(GameObject instance, Transform root, AnimationClip clip, float time)
        {
            clip.SampleAnimation(instance, time);
            return root.localPosition.y;
        }

        /// <summary>每组里两帧之间角度差最大的那根骨骼，乘以 <paramref name="scale"/>（换算成角速度时用）。</summary>
        private static (float Value, string Bone)[] GroupMax(
            Transform[] bones, Group[] groups, Quaternion[] a, Quaternion[] b, float scale)
        {
            var result = new (float Value, string Bone)[GroupCount];
            for (var g = 0; g < result.Length; g++)
            {
                result[g] = (0f, "-");
            }

            for (var i = 0; i < bones.Length; i++)
            {
                // 只看真的驱动着可见网格的骨骼：不可见的辅助骨骼转得再多，玩家也看不到。
                if (!_visible[i])
                {
                    continue;
                }

                var value = Quaternion.Angle(a[i], b[i]) * scale;
                var g = (int)groups[i];
                if (value > result[g].Value)
                {
                    result[g] = (value, bones[i].name);
                }
            }

            return result;
        }

        private static string Trim(string value, int max) =>
            value.Length <= max ? value : value.Substring(0, max);

        private static void Write(string report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? ".");
            File.WriteAllText(OutputPath, report, new UTF8Encoding(false));
            Debug.Log($"[回 Idle 交接点扫描] 已写出 {OutputPath}\n{report}");
        }

        private readonly struct Source
        {
            public Source(string state, float clipSeconds, float endSpeed)
            {
                State = state;
                ClipSeconds = clipSeconds;
                EndSpeed = endSpeed <= 0f ? 1f : endSpeed;
            }

            public string State { get; }

            /// <summary>业务交还 Idle 时的片段时间。</summary>
            public float ClipSeconds { get; }

            /// <summary>片段结尾的播放倍率（后摇倍率；移动停止、受击与待机动作为 1）。</summary>
            public float EndSpeed { get; }
        }
    }
}
#endif
