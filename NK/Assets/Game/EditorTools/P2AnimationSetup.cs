using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Naraka.EditorTools
{
    /// <summary>
    /// 玩家动画装配。
    ///
    /// 两件事：
    /// 1. 只给确实需要循环的 5 个片段打开 loopTime，其余动画的 .meta 一个字都不改；
    /// 2. 生成一个"每个动画各占一个 State、没有任何过渡条件、没有任何参数"的
    ///    Animator Controller。业务状态机用缓存好的 State Hash 直接 CrossFade，
    ///    因此 Animator 永远只是投影，不可能成为状态真相。
    ///
    /// 动画 FBX 里即使带模型和材质，这里也只取其中的 AnimationClip：
    /// 不实例化重复模型，也不让动画 FBX 的材质覆盖正式角色材质。
    /// </summary>
    public static class P2AnimationSetup
    {
        public const string AnimationRoot = "Assets/Game/Player_Animation";
        public const string CharacterFbxPath = "Assets/Game/Art/Characters/Changli/Changli_TPose.fbx";
        public const string ControllerPath = "Assets/Game/Settings/Player/NarakaPlayerAnimator.controller";

        /// <summary>
        /// 状态名 → 动画 FBX 文件名。这就是本阶段确认的动画映射表。
        /// </summary>
        public static readonly (string State, string Fbx)[] StateToFbx =
        {
            (PlayerAnimatorProjector.StateNames.Idle, "AM_Stand1_Action03"),
            (PlayerAnimatorProjector.StateNames.IdleVariation, "AM_Stand1_Action03_SEQ1"),
            (PlayerAnimatorProjector.StateNames.Walk, "Walk_F"),
            (PlayerAnimatorProjector.StateNames.Run, "Run_F"),
            (PlayerAnimatorProjector.StateNames.RunTurnback, "Run_Turnback"),
            (PlayerAnimatorProjector.StateNames.StopWalk, "Stop_Walk_R"),
            (PlayerAnimatorProjector.StateNames.StopRun, "Stop_Run_R"),
            (PlayerAnimatorProjector.StateNames.Dash, "Move_F"),
            (PlayerAnimatorProjector.StateNames.AttackCombo1, "Attack01"),
            (PlayerAnimatorProjector.StateNames.AttackCombo2, "AM_Summon"),
            (PlayerAnimatorProjector.StateNames.AttackCombo3, "Attack02"),
            (PlayerAnimatorProjector.StateNames.Charge, "Attack10"),
            (PlayerAnimatorProjector.StateNames.SkillF, "AM_Skill01"),
            (PlayerAnimatorProjector.StateNames.SkillV, "Attack04_1"),
            (PlayerAnimatorProjector.StateNames.HitStun, "Behit_B_L"),
            (PlayerAnimatorProjector.StateNames.Death, "AM_Death"),
            (PlayerAnimatorProjector.StateNames.SpawnBurstLobbyToMap01, "Burst02"),
            (PlayerAnimatorProjector.StateNames.SpawnBurstMap01ToMap02, "Burst01")
        };

        /// <summary>需要循环的动画。其余动画都只播一次。</summary>
        public static readonly string[] LoopingFbx =
        {
            "AM_Stand1_Action03",
            "Walk_F",
            "Run_F"
        };

        /// <summary>
        /// 曾经被本工具改过导入设置、但现在已经不被任何状态引用的动画。
        /// 把它们恢复成默认，让改过的 `.meta` 数量保持最小。
        /// </summary>
        public static readonly string[] UnusedFbx =
        {
            "Walk_B",
            "Run_B"
        };

        [MenuItem("NARAKA/Setup/Rebuild Player Animator")]
        public static void Rebuild()
        {
            var report = new StringBuilder();
            report.AppendLine("NARAKA P2 动画装配：");
            ApplyImportSettings(report);
            ResetUnusedClipSettings(report);
            SyncTuningFromClips(P2SceneSetup.PlayerTuningPath, report);
            BuildController(report);
            ValidateClipPaths(report);
            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// 统一动画导入设置。这是本阶段对动画 `.meta` 的全部修改，逐项记录在日志里。
        /// 已经正确的文件不再写入，因此重复执行不会产生新的资产改动。
        ///
        /// 只改一项：循环动画的 `loopTime`。
        ///
        /// 这里**刻意不设置** `motionNodeName`。这些动画在根骨骼上烘焙了水平位移，
        /// 看上去应该用 Unity 的 Root Motion 提取把它抽掉，但实测行不通：
        /// 正式角色模型与全部动画都是 Generic 且 `avatarSetup = NoAvatar`，
        /// 没有 Avatar 就没有 Root Motion 节点，设了这个字段也是空转。
        /// 要走那条路必须给角色生成 Avatar 并重新导入全部动画，
        /// 那是对用户美术资源的大改动。因此根位移改由
        /// <see cref="Naraka.Features.Character.View.RootMotionCanceller"/> 在运行期抵消。
        /// </summary>
        public static void ApplyImportSettings(StringBuilder report)
        {
            var looping = new HashSet<string>(LoopingFbx, StringComparer.Ordinal);
            foreach (var (_, fbx) in StateToFbx)
            {
                var path = $"{AnimationRoot}/{fbx}.fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    report.AppendLine($"  [导入] 缺少动画文件：{path}");
                    continue;
                }

                var wantLoop = looping.Contains(fbx);
                var existing = importer.clipAnimations;
                var hasStaleMotionNode = !string.IsNullOrEmpty(importer.motionNodeName);

                if (!wantLoop)
                {
                    // 非循环动画不需要任何改动：保持默认的空 clipAnimations。
                    if ((existing == null || existing.Length == 0) && !hasStaleMotionNode)
                    {
                        continue;
                    }

                    importer.clipAnimations = new ModelImporterClipAnimation[0];
                    importer.motionNodeName = string.Empty;
                    importer.SaveAndReimport();
                    report.AppendLine($"  [导入] {fbx}.fbx.meta 恢复默认设置（非循环动画无需改动）。");
                    continue;
                }

                var clips = existing != null && existing.Length > 0
                    ? existing
                    : importer.defaultClipAnimations;
                if (clips == null || clips.Length == 0)
                {
                    report.AppendLine($"  [导入] {fbx} 没有可用的 Take，跳过。");
                    continue;
                }

                if (clips.All(clip => clip.loopTime) && !hasStaleMotionNode &&
                    existing != null && existing.Length == clips.Length)
                {
                    continue;
                }

                for (var i = 0; i < clips.Length; i++)
                {
                    clips[i].loopTime = true;
                }

                importer.motionNodeName = string.Empty;
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                report.AppendLine($"  [导入] {fbx}.fbx.meta 设置 loopTime = true（唯一改动项）。");
            }
        }

        /// <summary>
        /// 把不再使用的动画恢复成默认导入设置，让改过的 `.meta` 数量保持最小。
        /// 只清空由本工具写入的项，不改任何其他导入设置。
        /// </summary>
        public static void ResetUnusedClipSettings(StringBuilder report)
        {
            foreach (var fbx in UnusedFbx)
            {
                var path = $"{AnimationRoot}/{fbx}.fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    continue;
                }

                var existing = importer.clipAnimations;
                if ((existing == null || existing.Length == 0) &&
                    string.IsNullOrEmpty(importer.motionNodeName))
                {
                    continue;
                }

                importer.clipAnimations = new ModelImporterClipAnimation[0];
                importer.motionNodeName = string.Empty;
                importer.SaveAndReimport();
                report.AppendLine($"  [导入] {fbx}.fbx.meta 恢复默认设置（该动画已不再被引用）。");
            }
        }

        /// <summary>
        /// 把每个动作的**片段长度**从实际 AnimationClip 同步进 <c>PlayerTuningAsset</c>。
        ///
        /// 为什么必须有这一步：初版把每个动作时长都写成了估计值，与真实片段差得很远
        /// （待机动作 3 秒 vs 实际 15.07 秒、Burst02 2 秒 vs 实际 5.5 秒）。
        /// 状态机按配置时长结束动作，于是所有比配置长的动画都被拦腰截断。
        /// 片段长度这种东西只有一个真相，就是片段本身，不该由人去猜。
        ///
        /// 这个工具**只管片段长度**。播放速度、命中窗、连段窗与动作位移都是设计值，
        /// 工具一律不碰 —— 例如三段攻击刻意把位移设成 0（攻击不改变坐标），
        /// 如果工具去同步"动画烘焙了多少位移"就会把这个设计意图冲掉。
        ///
        /// 已经一致的字段不再写入，因此重复执行不会改动资产。
        /// </summary>
        public static void SyncTuningFromClips(string tuningAssetPath, StringBuilder report)
        {
            var asset = AssetDatabase.LoadAssetAtPath<PlayerTuningAsset>(tuningAssetPath);
            if (asset == null)
            {
                report.AppendLine($"  [时长] 找不到 {tuningAssetPath}，跳过同步。");
                return;
            }

            var serialized = new SerializedObject(asset);
            var changes = 0;

            foreach (var (state, fbx) in StateToFbx)
            {
                var clip = LoadClip($"{AnimationRoot}/{fbx}.fbx");
                if (clip == null)
                {
                    continue;
                }

                var length = clip.length;
                switch (state)
                {
                    case PlayerAnimatorProjector.StateNames.IdleVariation:
                        changes += SetFloat(serialized, "idleVariationDurationSeconds", length, fbx, report);
                        break;
                    case PlayerAnimatorProjector.StateNames.StopWalk:
                        changes += SetFloat(serialized, "stopWalkSeconds", length, fbx, report);
                        changes += SetFloat(
                            serialized, "stopWalkDistance", Abs(MeasureForwardDisplacement(clip)), fbx, report);
                        break;
                    case PlayerAnimatorProjector.StateNames.StopRun:
                        changes += SetFloat(serialized, "stopRunSeconds", length, fbx, report);
                        changes += SetFloat(
                            serialized, "stopRunDistance", Abs(MeasureForwardDisplacement(clip)), fbx, report);
                        break;
                    case PlayerAnimatorProjector.StateNames.RunTurnback:
                        changes += SetFloat(serialized, "runTurnbackSeconds", length, fbx, report);
                        break;
                    case PlayerAnimatorProjector.StateNames.HitStun:
                        changes += SetFloat(serialized, "hitStunSeconds", length, fbx, report);
                        break;
                    case PlayerAnimatorProjector.StateNames.Death:
                        changes += SetFloat(serialized, "deathSeconds", length, fbx, report);
                        break;
                    case PlayerAnimatorProjector.StateNames.Dash:
                        changes += SetClipSeconds(serialized, "dash", length, fbx, report);
                        break;
                    case PlayerAnimatorProjector.StateNames.AttackCombo1:
                        changes += SetClipSeconds(serialized, "combo1", length, fbx, report);
                        break;
                    case PlayerAnimatorProjector.StateNames.AttackCombo2:
                        changes += SetClipSeconds(serialized, "combo2", length, fbx, report);
                        break;
                    case PlayerAnimatorProjector.StateNames.AttackCombo3:
                        changes += SetClipSeconds(serialized, "combo3", length, fbx, report);
                        break;
                    case PlayerAnimatorProjector.StateNames.Charge:
                        changes += SetClipSeconds(serialized, "charge", length, fbx, report);
                        break;
                    case PlayerAnimatorProjector.StateNames.SkillF:
                        changes += SetClipSeconds(serialized, "skillF", length, fbx, report);
                        break;
                    case PlayerAnimatorProjector.StateNames.SkillV:
                        changes += SetClipSeconds(serialized, "skillV", length, fbx, report);
                        break;
                    case PlayerAnimatorProjector.StateNames.SpawnBurstLobbyToMap01:
                        changes += SetClipSeconds(serialized, "spawnLobbyToMap01", length, fbx, report);
                        break;
                    case PlayerAnimatorProjector.StateNames.SpawnBurstMap01ToMap02:
                        changes += SetClipSeconds(serialized, "spawnMap01ToMap02", length, fbx, report);
                        break;
                }
            }

            if (changes == 0)
            {
                report.AppendLine("  [时长] 全部片段长度已与动画一致，未改动。");
                return;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            report.AppendLine($"  [时长] 共同步 {changes} 项。");
        }

        /// <summary>
        /// 片段在根骨骼上烘焙的水平位移（沿本地 Z）。
        /// 只用于停止动作的减速距离 —— 那是"脚不打滑"需要的物理量，不是设计值。
        /// </summary>
        private static float MeasureForwardDisplacement(AnimationClip clip)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var rootPath = bindings
                .Where(b => b.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal))
                .Select(b => b.path)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x.Count(c => c == '/'))
                .FirstOrDefault();
            if (string.IsNullOrEmpty(rootPath))
            {
                return 0f;
            }

            foreach (var binding in bindings)
            {
                if (!string.Equals(binding.path, rootPath, StringComparison.Ordinal) ||
                    !string.Equals(binding.propertyName, "m_LocalPosition.z", StringComparison.Ordinal))
                {
                    continue;
                }

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0)
                {
                    return 0f;
                }

                return curve.Evaluate(clip.length) - curve.Evaluate(0f);
            }

            return 0f;
        }

        private static float Abs(float value) => value < 0f ? -value : value;

        /// <summary>
        /// 写入一个动作条目的片段长度。窗口与速度一律不动 —— 它们是设计值。
        /// </summary>
        private static int SetClipSeconds(
            SerializedObject serialized,
            string field,
            float length,
            string fbx,
            StringBuilder report)
        {
            var entry = serialized.FindProperty(field);
            if (entry == null)
            {
                report.AppendLine($"  [时长] PlayerTuningAsset 缺少字段 {field}。");
                return 0;
            }

            var clipSeconds = entry.FindPropertyRelative("clipSeconds");
            if (clipSeconds == null)
            {
                report.AppendLine($"  [时长] {field} 缺少 clipSeconds。");
                return 0;
            }

            if (Approximately(clipSeconds.floatValue, length))
            {
                return 0;
            }

            report.AppendLine(
                $"  [时长] {fbx} → {field}.clipSeconds {clipSeconds.floatValue:0.###} → {length:0.###}");
            clipSeconds.floatValue = length;
            return 1;
        }

        private static int SetFloat(
            SerializedObject serialized, string field, float value, string fbx, StringBuilder report)
        {
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                report.AppendLine($"  [时长] PlayerTuningAsset 缺少字段 {field}。");
                return 0;
            }

            if (Approximately(property.floatValue, value))
            {
                return 0;
            }

            report.AppendLine($"  [时长] {fbx} → {field} {property.floatValue:0.###} → {value:0.###}");
            property.floatValue = value;
            return 1;
        }

        private static bool Approximately(float a, float b) => Abs(a - b) < 0.0005f;

        /// <summary>
        /// 生成 Animator Controller。
        ///
        /// 刻意不建任何 Transition、Trigger 或 Parameter：状态转换的真相在业务状态机里，
        /// Animator 只有一层平铺的 State。这样它既不可能自己跳状态，也不可能被反查成状态来源。
        /// </summary>
        public static AnimatorController BuildController(StringBuilder report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ControllerPath) ?? string.Empty);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
                report.AppendLine($"  [Animator] 新建 {ControllerPath}");
            }

            // 新建的 Controller 通常自带 Base Layer，但如果上一次运行在保存之前中断，
            // 磁盘上可能留下一个层数为 0 的资产。这里补一层，而不是让工具再崩一次。
            if (controller.layers == null || controller.layers.Length == 0)
            {
                controller.AddLayer("Base Layer");
                report.AppendLine("  [Animator] 补齐缺失的 Base Layer。");
            }

            var layer = controller.layers[0];
            var machine = layer.stateMachine;
            if (machine == null)
            {
                machine = new AnimatorStateMachine
                {
                    name = layer.name,
                    hideFlags = HideFlags.HideInHierarchy
                };
                AssetDatabase.AddObjectToAsset(machine, controller);
                var layers = controller.layers;
                layers[0].stateMachine = machine;
                controller.layers = layers;
            }

            var existing = machine.states.ToDictionary(s => s.state.name, s => s.state);
            var missing = new List<string>();
            var column = 0;

            foreach (var (stateName, fbx) in StateToFbx)
            {
                var clip = LoadClip($"{AnimationRoot}/{fbx}.fbx");
                if (clip == null)
                {
                    missing.Add(fbx);
                    continue;
                }

                if (!existing.TryGetValue(stateName, out var state))
                {
                    state = machine.AddState(
                        stateName,
                        new Vector3(260f + (column % 2 * 260f), 60f + (column / 2 * 70f), 0f));
                    existing[stateName] = state;
                }

                state.motion = clip;
                state.writeDefaultValues = false;
                column++;
            }

            // 删除已经不在映射表里的孤儿 State。投影层按名字缓存 Hash，
            // 一个没人能寻址的 State 留在图里只会让人误以为它还在被使用。
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (stateName, _) in StateToFbx)
            {
                expected.Add(stateName);
            }

            // 先收集再删除：RemoveState 会销毁对象，边遍历 machine.states 边删会拿到失效引用。
            var orphans = new List<string>();
            foreach (var child in machine.states)
            {
                if (!expected.Contains(child.state.name))
                {
                    orphans.Add(child.state.name);
                }
            }

            foreach (var orphan in orphans)
            {
                foreach (var child in machine.states)
                {
                    if (!string.Equals(child.state.name, orphan, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    machine.RemoveState(child.state);
                    break;
                }

                existing.Remove(orphan);
                report.AppendLine($"  [Animator] 移除孤儿 State：{orphan}");
            }

            // 默认状态固定为 Idle，这样 Animator 初始化时不会先播一个随机动作。
            if (existing.TryGetValue(PlayerAnimatorProjector.StateNames.Idle, out var idle))
            {
                machine.defaultState = idle;
            }

            // 清掉可能残留的过渡：Animator 不做任何自己的状态判断。
            foreach (var state in machine.states)
            {
                var transitions = state.state.transitions;
                for (var i = transitions.Length - 1; i >= 0; i--)
                {
                    state.state.RemoveTransition(transitions[i]);
                }
            }

            foreach (var transition in machine.anyStateTransitions)
            {
                machine.RemoveAnyStateTransition(transition);
            }

            EditorUtility.SetDirty(controller);
            report.AppendLine(
                $"  [Animator] {machine.states.Length} 个 State，0 个 Parameter，0 条 Transition。");
            if (missing.Count > 0)
            {
                report.AppendLine($"  [Animator] 缺少动画：{string.Join("、", missing)}");
            }

            return controller;
        }

        /// <summary>
        /// 校验动画曲线路径能否在正式角色模型上找到对应 Transform。
        /// 找不到就直接报出来，而不是等运行时表现为"角色一动不动"。
        /// </summary>
        public static void ValidateClipPaths(StringBuilder report)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterFbxPath);
            if (model == null)
            {
                report.AppendLine($"  [校验] 找不到角色模型：{CharacterFbxPath}");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                var known = new HashSet<string>(StringComparer.Ordinal);
                CollectPaths(instance.transform, instance.transform, known);

                var problems = 0;
                foreach (var (_, fbx) in StateToFbx)
                {
                    var clip = LoadClip($"{AnimationRoot}/{fbx}.fbx");
                    if (clip == null)
                    {
                        continue;
                    }

                    var unmatched = AnimationUtility.GetCurveBindings(clip)
                        .Select(binding => binding.path)
                        .Distinct(StringComparer.Ordinal)
                        .Where(path => !string.IsNullOrEmpty(path) && !known.Contains(path))
                        .Take(3)
                        .ToArray();
                    if (unmatched.Length == 0)
                    {
                        continue;
                    }

                    problems++;
                    report.AppendLine($"  [校验] {fbx} 有无法匹配的曲线路径：{string.Join("、", unmatched)}");
                }

                report.AppendLine(problems == 0
                    ? $"  [校验] {StateToFbx.Length} 个动画的曲线路径全部匹配正式角色模型。"
                    : $"  [校验] {problems} 个动画存在路径不匹配。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// 取出一个 FBX 里的 AnimationClip。动画 FBX 可能同时包含网格与材质，
        /// 这里只取 AnimationClip，并跳过编辑器自动生成的 __preview__ 片段。
        /// </summary>
        public static AnimationClip LoadClip(string assetPath)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (assets == null)
            {
                return null;
            }

            foreach (var asset in assets)
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
    }
}
