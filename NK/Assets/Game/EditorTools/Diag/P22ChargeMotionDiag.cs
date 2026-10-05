#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Naraka.EditorTools.Diag
{
    /// <summary>
    /// 蓄力（`Attack10`）到底有没有"飞上天空"。
    ///
    /// 用户报告蓄力动画在原地旋转、没有飞起来。要分清三件事：
    ///
    /// 1. **片段里有没有腾空**：把片段采样到正式模型上，看最低骨骼（脚）的世界高度
    ///    有没有抬离起始值。腾空一定表现为脚离地。
    /// 2. **腾空挂在哪条通道上**：如果挂在 `Root` 的位移通道上，
    ///    那么 <c>RootMotionCanceller</c> 会把它整条锁掉，游戏里就看不见；
    ///    如果挂在 `Bip001` 及以下，抵消器碰不到它，游戏里应该看得见。
    /// 3. **是腾空还是前冲**：分别看垂直与水平分量。
    ///
    /// 因此每个片段都量两遍：
    /// - 「原始」＝直接采样，`Root` 位移生效（＝美术在 Max 里看到的样子）；
    /// - 「游戏里」＝把 `Root.localPosition` 锁回静止值后再量（＝抵消器之后的样子）。
    ///
    /// 两者之差就是抵消器拿掉的东西。只读测量，不改任何资产。
    /// </summary>
    public static class P22ChargeMotionDiag
    {
        private const string ModelPath = "Assets/Game/Art/Characters/Changli/Changli_TPose.fbx";
        private const string OutputPath = "artifacts/p22-charge-motion-diag.txt";

        /// <summary>（状态, 片段）。蓄力是主角，另外两个位移动作作参照。</summary>
        private static readonly (string State, string Fbx)[] Subjects =
        {
            ("蓄力", "Attack10"),
            ("冲刺", "Move_F"),
            ("V 技能", "Attack04_1"),
            ("F 技能（旧动画，对照）", "AM_Skill01")
        };

        [MenuItem("NARAKA/Diag/P2.2 Charge Motion Diag")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("蓄力与位移动作的腾空测量  " +
                          DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine(new string('=', 104));

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var instance = (GameObject)UnityEngine.Object.Instantiate(model);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var bones = instance.GetComponentsInChildren<Transform>(true);
                var root = FindDeep(instance.transform, "Root");
                var pelvis = FindDeep(instance.transform, "Bip001");
                var restRootPosition = root.localPosition;

                sb.AppendLine();
                sb.AppendLine("## 1. 总览：片段里有多少腾空与水平位移");
                sb.AppendLine();
                sb.AppendLine("   脚高 = 全部骨骼里世界 Y 最小的那个，相对片段首帧的变化量。");
                sb.AppendLine("   「原始」是 Root 位移生效时的样子，「游戏里」是抵消器锁住 Root 位移之后的样子。");
                sb.AppendLine();
                sb.AppendLine($"   {"状态",-26}{"片段",-14}{"版本",-6}" +
                              $"{"脚最高抬起",-13}{"骨盆最高抬起",-15}{"水平行程",-12}Root 位移净变化");
                sb.AppendLine("   " + new string('-', 104));

                foreach (var (state, fbx) in Subjects)
                {
                    var clip = LoadClip($"Assets/Game/Player_Animation/{fbx}.fbx");
                    if (clip == null)
                    {
                        sb.AppendLine($"   {state,-26}{fbx,-14}<找不到片段>");
                        continue;
                    }

                    foreach (var cancel in new[] { false, true })
                    {
                        var m = Measure(instance, bones, root, pelvis, clip, restRootPosition, cancel);
                        sb.AppendLine(
                            $"   {(cancel ? string.Empty : state),-26}{(cancel ? string.Empty : fbx),-14}" +
                            $"{(cancel ? "游戏里" : "原始"),-6}" +
                            $"{m.FootRise,-13:F3}{m.PelvisRise,-15:F3}{m.Horizontal,-12:F3}{V(m.RootDelta)}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("## 2. 蓄力逐帧：Root 位移通道与身体高度");
                sb.AppendLine();
                sb.AppendLine("   每 0.1 秒一行。看 Root 位移是「去了又回」（腾空弧线）");
                sb.AppendLine("   还是「一路单向」（行进位移）。");
                sb.AppendLine();

                var charge = LoadClip("Assets/Game/Player_Animation/Attack10.fbx");
                if (charge != null)
                {
                    sb.AppendLine($"   {"时间",-9}{"Root.x",-10}{"Root.y",-10}{"Root.z",-10}" +
                                  $"{"脚高(原始)",-13}{"脚高(游戏里)",-14}骨盆高(游戏里)");
                    sb.AppendLine("   " + new string('-', 86));

                    var baseFootRaw = 0f;
                    var baseFootGame = 0f;
                    var basePelvis = 0f;
                    for (var t = 0f; t <= charge.length + 0.0001f; t += 0.1f)
                    {
                        var time = Mathf.Min(t, charge.length);

                        charge.SampleAnimation(instance, time);
                        var rootPos = root.localPosition;
                        var footRaw = LowestY(bones);

                        root.localPosition = restRootPosition;
                        var footGame = LowestY(bones);
                        var pelvisGame = pelvis.position.y;

                        if (t == 0f)
                        {
                            baseFootRaw = footRaw;
                            baseFootGame = footGame;
                            basePelvis = pelvisGame;
                        }

                        sb.AppendLine(
                            $"   {time,-9:F2}{rootPos.x,-10:F3}{rootPos.y,-10:F3}{rootPos.z,-10:F3}" +
                            $"{footRaw - baseFootRaw,-13:F3}{footGame - baseFootGame,-14:F3}" +
                            $"{pelvisGame - basePelvis:F3}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("## 3. 全部 18 个动画状态的 Root 位移通道（修正之后）");
                sb.AppendLine();
                sb.AppendLine("   这一节决定抵消器能不能只锁水平、保留垂直：");
                sb.AppendLine("   只有在**每个片段的垂直净变化都≈0** 的前提下保留垂直才安全，");
                sb.AppendLine("   否则净值不为 0 的片段会把角色留在地里或空中。");
                sb.AppendLine();
                sb.AppendLine($"   {"状态",-24}{"片段",-26}{"垂直净变化",-13}{"垂直最高",-11}" +
                              $"{"垂直最低",-11}{"水平净行程",-13}结论");
                sb.AppendLine("   " + new string('-', 112));

                foreach (var (state, fbx) in Naraka.EditorTools.P2AnimationSetup.StateToFbx)
                {
                    var clip = LoadClip($"Assets/Game/Player_Animation/{fbx}.fbx");
                    if (clip == null)
                    {
                        sb.AppendLine($"   {state,-24}{fbx,-26}<找不到片段>");
                        continue;
                    }

                    var samples = Mathf.Max(2, Mathf.RoundToInt(clip.length * 60f));
                    var first = Vector3.zero;
                    var last = Vector3.zero;
                    var maxY = float.MinValue;
                    var minY = float.MaxValue;
                    for (var i = 0; i <= samples; i++)
                    {
                        clip.SampleAnimation(instance, clip.length * i / samples);
                        var pos = root.localPosition - restRootPosition;
                        if (i == 0)
                        {
                            first = pos;
                        }

                        last = pos;
                        maxY = Mathf.Max(maxY, pos.y);
                        minY = Mathf.Min(minY, pos.y);
                    }

                    var net = last - first;
                    var horizontal = new Vector2(net.x, net.z).magnitude;
                    var verdict = Mathf.Abs(net.y) < 0.1f
                        ? (maxY - minY > 0.5f ? "腾空弧线，落回原高度" : "无垂直位移")
                        : "**垂直净变化不为 0**";

                    sb.AppendLine(
                        $"   {state,-24}{fbx,-26}{net.y,-13:F3}{maxY,-11:F3}" +
                        $"{minY,-11:F3}{horizontal,-13:F3}{verdict}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? ".");
            File.WriteAllText(OutputPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[蓄力腾空测量] 已写出 {OutputPath}\n{sb}");
        }

        private readonly struct Result
        {
            public Result(float footRise, float pelvisRise, float horizontal, Vector3 rootDelta)
            {
                FootRise = footRise;
                PelvisRise = pelvisRise;
                Horizontal = horizontal;
                RootDelta = rootDelta;
            }

            public float FootRise { get; }

            public float PelvisRise { get; }

            public float Horizontal { get; }

            public Vector3 RootDelta { get; }
        }

        private static Result Measure(
            GameObject instance, Transform[] bones, Transform root, Transform pelvis,
            AnimationClip clip, Vector3 restRootPosition, bool cancelRootTranslation)
        {
            var samples = Mathf.Max(2, Mathf.RoundToInt(clip.length * 60f));
            var footRise = 0f;
            var pelvisRise = 0f;
            var horizontal = 0f;
            var baseFoot = 0f;
            var basePelvis = 0f;
            var basePlanar = Vector2.zero;
            var firstRootPosition = Vector3.zero;
            var lastRootPosition = Vector3.zero;

            for (var i = 0; i <= samples; i++)
            {
                var time = clip.length * i / samples;
                clip.SampleAnimation(instance, time);

                if (i == 0)
                {
                    firstRootPosition = root.localPosition;
                }

                lastRootPosition = root.localPosition;

                if (cancelRootTranslation)
                {
                    root.localPosition = restRootPosition;
                }

                var foot = LowestY(bones);
                var pelvisY = pelvis.position.y;
                var planar = new Vector2(pelvis.position.x, pelvis.position.z);

                if (i == 0)
                {
                    baseFoot = foot;
                    basePelvis = pelvisY;
                    basePlanar = planar;
                }

                footRise = Mathf.Max(footRise, foot - baseFoot);
                pelvisRise = Mathf.Max(pelvisRise, pelvisY - basePelvis);
                horizontal = Mathf.Max(horizontal, (planar - basePlanar).magnitude);
            }

            return new Result(footRise, pelvisRise, horizontal, lastRootPosition - firstRootPosition);
        }

        private static float LowestY(Transform[] bones)
        {
            var min = float.MaxValue;
            foreach (var bone in bones)
            {
                min = Mathf.Min(min, bone.position.y);
            }

            return min;
        }

        private static AnimationClip LoadClip(string path)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is AnimationClip clip &&
                    !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
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

        private static string V(Vector3 v) => $"({v.x,7:F3},{v.y,7:F3},{v.z,7:F3})";
    }
}
#endif
