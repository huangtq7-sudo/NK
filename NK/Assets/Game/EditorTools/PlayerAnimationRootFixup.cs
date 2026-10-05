using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Naraka.EditorTools
{
    /// <summary>
    /// 把玩家动画 `Root` 节点上的导出约定差异在导入期抹平。
    ///
    /// ## 问题
    ///
    /// 2026-10-04 换入的新动画集（FBX 7700）在 `Root` 节点上多写了两样东西，
    /// 而旧动画集（FBX 7500）与正式角色模型都没有：
    ///
    /// | | 旧动画 / 正式模型 | 新动画 |
    /// | --- | --- | --- |
    /// | `Root` 的 `Lcl Rotation` | 缺省（0） | **(+90, 0, 0)** |
    /// | `Root` 的 `Lcl Scaling` | 缺省（1） | **0.3937 = 1/2.54** |
    /// | `Root` 的 `PreRotation` | (−90, 0, 0) | (−90, 0, 0)（相同） |
    ///
    /// `PreRotation` 是 3ds Max（Z 轴朝上）导出 FBX 时的标准补偿，每个文件都有，
    /// Unity 会把它折进 `Root` 的静止 `localRotation`。正式模型的 `Root` 静止姿态
    /// 因此是 `Rx(−90)`、缩放 1。新片段把 `Root` 驱动到 `Rx(−90)·Rx(+90) = 0`，
    /// 于是整具骨架相对静止姿态**绕世界 X 轴转了 +90°**
    /// —— 角色躺在地上、头朝前；同时整体缩到 39.37%。
    ///
    /// 实测（`NARAKA/Diag/P2.2 Animation Axis Diag`）：17 个新片段全部带 0.3937 缩放，
    /// 其中 15 个的 Root 旋转偏差恰好是 +90° 且全程恒定；例外是 `Walk_F`（+6.87°）
    /// 与 `Attack01`（−3.14°），正是用户唯一没有报告"贴在地上"的两个动作。
    /// 对照组旧片段 `AM_Skill01` 的偏差是 0、缩放 1.0。
    ///
    /// ## 为什么在导入期修，而不是运行期
    ///
    /// 运行期（`RootMotionCanceller`）分不清当前播的是新片段还是旧片段：
    /// 同一个补偿量加给旧片段就会把旧片段转错 90°，而交叉淡入期间两者混在一起，
    /// 补偿量根本没有定义。导入期是逐片段的，每个片段按**自己文件里的静止姿态**算补偿，
    /// 因此旧片段算出来是单位四元数，天然不受影响。
    ///
    /// ## 补偿量怎么来
    ///
    /// 不是写死 90°，而是 <c>正式模型的静止姿态 × 该文件静止姿态的逆</c>：
    /// 它把这个文件里的站立姿态映射到正式模型的站立姿态，两边约定一致时就是单位四元数。
    /// 左乘常量四元数在 R⁴ 上是线性映射，所以关键帧的切线可以用同一个乘法变换，
    /// 曲线形状完全保留（不需要重新平滑，也不会引入抖动）。
    ///
    /// 位移通道同样要修，而且**必须修**：它不是垃圾数据。
    /// 蓄力（`Attack10`）的腾空就烘焙在这条通道上 —— 还原到正确约定之后是
    /// 「升到 +5.23、再落回 +0.06，同时前进 8.47」，一条完整的跳跃弧线。
    /// 轴向错位会把"向上"读成"向前"、"向前"读成"向下"，于是腾空消失、角色往地里沉。
    /// 还原之后冲刺是 (0,0,12.276)、V 技能是 (0,0,17.838)，与配置里的位移值分毫不差，
    /// 这反过来证明还原是对的。
    /// </summary>
    public sealed class PlayerAnimationRootFixup : AssetPostprocessor
    {
        /// <summary>只处理玩家动画目录，不碰角色模型与任何第三方资源。</summary>
        private const string AnimationFolder = "Assets/Game/Player_Animation/";

        private const string RootBoneName = "Root";

        /// <summary>
        /// 正式角色模型 `Changli_TPose.fbx` 的 `Root` 静止姿态，来自所有 FBX 都带的
        /// `PreRotation(−90, 0, 0)`。实测 `localEuler = (270, 0, 0)`。
        /// `PlayerAnimationContractTests` 会断言模型确实还是这个姿态，
        /// 所以模型哪天被重新导出，测试会先报警，而不是悄悄错过去。
        /// </summary>
        public static readonly Quaternion ExpectedRootRotation = Quaternion.Euler(-90f, 0f, 0f);

        /// <summary>姿态差小于这个角度就认为两边约定一致，不做任何改动。</summary>
        public const float RotationToleranceDegrees = 0.1f;

        /// <summary>缩放比例偏离 1 小于这个量就认为两边约定一致。</summary>
        public const float ScaleTolerance = 0.001f;

        /// <summary>
        /// 根节点垂直位移的净变化容许量（单位）。
        ///
        /// 这是一条项目级不变量：游戏里没有跳跃，角色由 `CharacterController` 恒定贴地，
        /// 水平移动全部由代码驱动。所以片段播完之后根节点**必须回到起始高度** ——
        /// 净变化不为 0 只可能是导出残留。实测 `Attack01` 的净变化是 −3.167，
        /// 单调下沉且不回来；留着它就意味着第一段普攻会把角色按进地里 3 个单位。
        ///
        /// 容许量取 0.1：蓄力的腾空弧线净变化是 +0.062（升到 +5.227 再落回），
        /// 属于"去了又回"的合法弧线，必须保留。
        /// </summary>
        public const float VerticalDriftTolerance = 0.1f;

        /// <summary>
        /// 改了本类的逻辑就要让 Unity 重新导入受影响的动画。
        /// Unity 只在资产本身或导入设置变化时重新导入，脚本改动不算；
        /// 这个版本号一变，所有经过本后处理器的资产都会重新导入一次。
        /// 以后调整修正逻辑时必须同时加 1，否则工程里留着的还是旧结果。
        /// </summary>
        public override uint GetVersion() => 3;

        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (!assetPath.Replace('\\', '/').StartsWith(AnimationFolder, StringComparison.Ordinal))
            {
                return;
            }

            var rootBone = FindChild(root.transform, RootBoneName);
            if (rootBone == null)
            {
                return;
            }

            var fileRotation = rootBone.localRotation;
            var fileScale = rootBone.localScale;

            var rotationOff =
                Quaternion.Angle(fileRotation, ExpectedRootRotation) > RotationToleranceDegrees;
            var scaleOff =
                Mathf.Abs(fileScale.x - 1f) > ScaleTolerance ||
                Mathf.Abs(fileScale.y - 1f) > ScaleTolerance ||
                Mathf.Abs(fileScale.z - 1f) > ScaleTolerance;

            if (!rotationOff && !scaleOff)
            {
                // 旧动画集走这条路：文件约定与正式模型一致，不改一个字节。
                return;
            }

            var changed = new List<string>(2);

            if (rotationOff)
            {
                var correction = ExpectedRootRotation * Quaternion.Inverse(fileRotation);
                if (RotateRootCurves(clip, correction))
                {
                    changed.Add(
                        $"旋转 ×{Quaternion.Angle(Quaternion.identity, correction):F2}°" +
                        $"（文件静止姿态 {Euler(fileRotation)} → 模型静止姿态 {Euler(ExpectedRootRotation)}）");
                }
            }

            if (scaleOff)
            {
                if (ScaleRootCurves(clip, fileScale))
                {
                    changed.Add($"缩放 {fileScale.x:F4} → 1");
                }
            }

            // 位移通道和旋转、缩放是同一套错位，必须用同一个补偿量一起还原，
            // 否则"向上"会被读成"向前"。
            var positionCorrection = ExpectedRootRotation * Quaternion.Inverse(fileRotation);
            var positionFactor = Mathf.Abs(fileScale.x) < 1e-6f ? 1f : 1f / fileScale.x;
            if (MoveRootCurves(clip, positionCorrection, positionFactor))
            {
                changed.Add($"位移 ×{positionFactor:F3} 并旋转 " +
                            $"{Quaternion.Angle(Quaternion.identity, positionCorrection):F2}°");
            }

            if (changed.Count > 0)
            {
                Debug.Log(
                    $"[动画 Root 修正] {System.IO.Path.GetFileName(assetPath)} / {clip.name}：" +
                    string.Join("，", changed));
            }
        }

        /// <summary>把 `Root` 的旋转曲线整条左乘一个常量四元数。</summary>
        private static bool RotateRootCurves(AnimationClip clip, Quaternion correction)
        {
            var bindings = new EditorCurveBinding[4];
            var curves = new AnimationCurve[4];
            var found = 0;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != RootBoneName)
                {
                    continue;
                }

                var axis = AxisIndex(binding.propertyName, "m_LocalRotation.");
                if (axis < 0)
                {
                    continue;
                }

                bindings[axis] = binding;
                curves[axis] = AnimationUtility.GetEditorCurve(clip, binding);
                found++;
            }

            if (found != 4)
            {
                if (found > 0)
                {
                    Debug.LogWarning(
                        $"[动画 Root 修正] {clip.name} 的 Root 旋转曲线只有 {found}/4 条，" +
                        "可能是欧拉角曲线。已跳过，请检查导入设置里的 Resample Curves。");
                }

                return false;
            }

            var keyCount = curves[0].length;
            for (var axis = 1; axis < 4; axis++)
            {
                if (curves[axis].length != keyCount)
                {
                    Debug.LogWarning(
                        $"[动画 Root 修正] {clip.name} 的四条 Root 旋转曲线关键帧数量不一致，已跳过。");
                    return false;
                }
            }

            var keys = new Keyframe[4][];
            for (var axis = 0; axis < 4; axis++)
            {
                keys[axis] = curves[axis].keys;
            }

            for (var i = 0; i < keyCount; i++)
            {
                // 左乘常量四元数在 R⁴ 上是线性映射，因此值与切线都能用同一个乘法变换，
                // 曲线形状逐帧保留，不需要重新计算切线。
                var value = correction * new Quaternion(
                    keys[0][i].value, keys[1][i].value, keys[2][i].value, keys[3][i].value);
                var inT = correction * new Quaternion(
                    keys[0][i].inTangent, keys[1][i].inTangent,
                    keys[2][i].inTangent, keys[3][i].inTangent);
                var outT = correction * new Quaternion(
                    keys[0][i].outTangent, keys[1][i].outTangent,
                    keys[2][i].outTangent, keys[3][i].outTangent);

                for (var axis = 0; axis < 4; axis++)
                {
                    keys[axis][i].value = value[axis];
                    keys[axis][i].inTangent = inT[axis];
                    keys[axis][i].outTangent = outT[axis];
                }
            }

            for (var axis = 0; axis < 4; axis++)
            {
                curves[axis].keys = keys[axis];
                AnimationUtility.SetEditorCurve(clip, bindings[axis], curves[axis]);
            }

            return true;
        }

        /// <summary>
        /// 把 `Root` 的位移曲线旋转并缩放回正式模型的约定。
        ///
        /// 位移存在父节点空间里，所以补偿是「先转到模型的朝向，再除掉文件的缩放」。
        /// 旋转加等比缩放仍然是线性映射，因此值与切线用同一套变换，曲线形状保留。
        ///
        /// 三条轴必须一起算：缺哪条就按静止值（0）补一条，否则换轴之后那条轴的数据会丢。
        /// </summary>
        private static bool MoveRootCurves(AnimationClip clip, Quaternion correction, float factor)
        {
            var bindings = new EditorCurveBinding[3];
            var curves = new AnimationCurve[3];
            var found = 0;
            var template = -1;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != RootBoneName)
                {
                    continue;
                }

                var axis = AxisIndex(binding.propertyName, "m_LocalPosition.");
                if (axis < 0 || axis > 2)
                {
                    continue;
                }

                bindings[axis] = binding;
                curves[axis] = AnimationUtility.GetEditorCurve(clip, binding);
                template = axis;
                found++;
            }

            if (found == 0)
            {
                // Idle 这类片段根本不动根节点，没有位移曲线，不需要改。
                return false;
            }

            var keyCount = curves[template].length;
            for (var axis = 0; axis < 3; axis++)
            {
                if (curves[axis] == null)
                {
                    // 缺失的轴等价于一条恒为静止值的曲线。换轴之后它会接到别的轴上，
                    // 所以必须显式补出来。
                    var filled = new Keyframe[keyCount];
                    for (var i = 0; i < keyCount; i++)
                    {
                        filled[i] = new Keyframe(curves[template].keys[i].time, 0f);
                    }

                    curves[axis] = new AnimationCurve(filled);
                    bindings[axis] = new EditorCurveBinding
                    {
                        path = RootBoneName,
                        type = typeof(Transform),
                        propertyName = "m_LocalPosition." + "xyz"[axis]
                    };
                    continue;
                }

                if (curves[axis].length != keyCount)
                {
                    Debug.LogWarning(
                        $"[动画 Root 修正] {clip.name} 的三条 Root 位移曲线关键帧数量不一致，已跳过。");
                    return false;
                }
            }

            var keys = new Keyframe[3][];
            for (var axis = 0; axis < 3; axis++)
            {
                keys[axis] = curves[axis].keys;
            }

            for (var i = 0; i < keyCount; i++)
            {
                var value = correction * new Vector3(
                    keys[0][i].value, keys[1][i].value, keys[2][i].value) * factor;
                var inT = correction * new Vector3(
                    keys[0][i].inTangent, keys[1][i].inTangent, keys[2][i].inTangent) * factor;
                var outT = correction * new Vector3(
                    keys[0][i].outTangent, keys[1][i].outTangent, keys[2][i].outTangent) * factor;

                for (var axis = 0; axis < 3; axis++)
                {
                    keys[axis][i].value = value[axis];
                    keys[axis][i].inTangent = inT[axis];
                    keys[axis][i].outTangent = outT[axis];
                }
            }

            // 垂直净变化不为 0 的通道是导出残留，直接压平（见 VerticalDriftTolerance）。
            // 压到首帧值而不是压到 0：这样片段开头没有突变，交叉淡入不会被带出一跳。
            var verticalDrift = keys[1][keyCount - 1].value - keys[1][0].value;
            if (Mathf.Abs(verticalDrift) > VerticalDriftTolerance)
            {
                var hold = keys[1][0].value;
                for (var i = 0; i < keyCount; i++)
                {
                    keys[1][i].value = hold;
                    keys[1][i].inTangent = 0f;
                    keys[1][i].outTangent = 0f;
                }

                Debug.Log(
                    $"[动画 Root 修正] {clip.name} 的根节点垂直位移净变化 {verticalDrift:F3} " +
                    "不为 0，判定为导出残留并压平（游戏里角色恒定贴地，净变化只能是 0）。");
            }

            for (var axis = 0; axis < 3; axis++)
            {
                curves[axis].keys = keys[axis];
                AnimationUtility.SetEditorCurve(clip, bindings[axis], curves[axis]);
            }

            return true;
        }

        /// <summary>把 `Root` 的缩放曲线按比例拉回 1。</summary>
        private static bool ScaleRootCurves(AnimationClip clip, Vector3 fileScale)
        {
            var any = false;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != RootBoneName)
                {
                    continue;
                }

                var axis = AxisIndex(binding.propertyName, "m_LocalScale.");
                if (axis < 0)
                {
                    continue;
                }

                var from = axis == 0 ? fileScale.x : axis == 1 ? fileScale.y : fileScale.z;
                if (Mathf.Abs(from) < 1e-6f)
                {
                    continue;
                }

                var factor = 1f / from;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                var keys = curve.keys;
                for (var i = 0; i < keys.Length; i++)
                {
                    // 乘一个标量同样是线性映射，切线一起缩放。
                    keys[i].value *= factor;
                    keys[i].inTangent *= factor;
                    keys[i].outTangent *= factor;
                }

                curve.keys = keys;
                AnimationUtility.SetEditorCurve(clip, binding, curve);
                any = true;
            }

            return any;
        }

        /// <summary>`m_LocalRotation.z` → 2，没匹配上返回 −1。w 排在 x/y/z 之后。</summary>
        private static int AxisIndex(string propertyName, string prefix)
        {
            if (!propertyName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return -1;
            }

            switch (propertyName.Substring(prefix.Length))
            {
                case "x": return 0;
                case "y": return 1;
                case "z": return 2;
                case "w": return 3;
                default: return -1;
            }
        }

        private static Transform FindChild(Transform parent, string name)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static string Euler(Quaternion q)
        {
            var e = q.eulerAngles;
            float Wrap(float a) => a > 180f ? a - 360f : a;
            return $"({Wrap(e.x):F2},{Wrap(e.y):F2},{Wrap(e.z):F2})";
        }
    }
}
