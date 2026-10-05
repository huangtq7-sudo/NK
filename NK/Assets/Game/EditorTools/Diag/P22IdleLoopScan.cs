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
    /// idle 循环缝的成因定位。
    ///
    /// 修掉裁剪范围之后，走路与奔跑的首末帧差已经降到 0.29° / 0.00°，
    /// 但 idle 还有 41.93°，而且最大差全在 `Bone_Hair*` / `Bone_Piao*` 上。
    /// 这个工具回答两个问题：
    ///
    /// 1. **是整具骨架不闭环，还是只有头发/飘带不闭环？**
    ///    把骨骼分成「头发与飘带」与「身体」两组分别统计。
    ///    如果身体闭环、只有头发不闭环，那就是烘焙头发模拟时没有做循环，
    ///    属于导出端问题，引擎里没有无损的修法。
    /// 2. **有没有某个更短的结束帧能让它闭环？**
    ///    逐帧扫描 pose(0) 与 pose(t) 的差，列出最接近的若干个。
    ///    如果存在这样的帧，裁到那里就是一个可测量的折中方案；
    ///    如果曲线是单调漂移，那就只能重新导出。
    ///
    /// 只读测量，不改任何资产。
    /// </summary>
    public static class P22IdleLoopScan
    {
        private const string ModelPath = "Assets/Game/Art/Characters/Changli/Changli_TPose.fbx";
        private const string OutputPath = "artifacts/p22-idle-loop-scan.txt";
        private const string IdleFbx = "Assets/Game/Player_Animation/AM_Stand1_Action03.fbx";

        [MenuItem("NARAKA/Diag/P2.2 Idle Loop Scan")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("idle 循环缝成因定位  " +
                          DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine(new string('=', 92));

            var clip = LoadClip(IdleFbx);
            if (clip == null)
            {
                Debug.LogError($"找不到 {IdleFbx} 的动画片段。");
                return;
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var instance = (GameObject)UnityEngine.Object.Instantiate(model);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var bones = instance.GetComponentsInChildren<Transform>(true);

                var isHair = new bool[bones.Length];
                var hairCount = 0;
                for (var i = 0; i < bones.Length; i++)
                {
                    var n = bones[i].name;
                    isHair[i] = n.StartsWith("Bone_Hair", StringComparison.Ordinal) ||
                                n.StartsWith("Bone_Piao", StringComparison.Ordinal) ||
                                n.StartsWith("Bangs", StringComparison.Ordinal);
                    if (isHair[i])
                    {
                        hairCount++;
                    }
                }

                sb.AppendLine();
                sb.AppendLine($"片段 {clip.name}  长度 {clip.length:F3}s  " +
                              $"骨骼 {bones.Length} 个（头发与飘带 {hairCount} 个，" +
                              $"身体 {bones.Length - hairCount} 个）");

                var first = Capture(instance, bones, clip, 0f);
                var last = Capture(instance, bones, clip, clip.length);

                sb.AppendLine();
                sb.AppendLine("## 1. 首末帧差：头发/飘带 与 身体 分开看");
                sb.AppendLine();
                var hair = Max(bones, isHair, true, first, last);
                var body = Max(bones, isHair, false, first, last);
                sb.AppendLine($"  头发与飘带：最大 {hair.Angle,7:F3}°   （{hair.Bone}）");
                sb.AppendLine($"  身体      ：最大 {body.Angle,7:F3}°   （{body.Bone}）");
                sb.AppendLine();
                sb.AppendLine(body.Angle < 5f
                    ? "  → 身体是闭环的，只有头发与飘带不闭环：烘焙头发模拟时没有做成循环。"
                    : "  → 身体本身也不闭环，这个片段整体就不是一个循环动作。");

                sb.AppendLine();
                sb.AppendLine("## 2. 逐帧扫描：有没有更短的结束点能闭环");
                sb.AppendLine();
                sb.AppendLine("   把结束点从第 20 帧扫到末尾，看 pose(0) 与 pose(t) 差多少。");
                sb.AppendLine("   差值小于 5° 就算可用的循环点。");
                sb.AppendLine();

                var frames = Mathf.RoundToInt(clip.length * 60f);
                var scan = new List<(int Frame, float Angle, float HairAngle)>();
                for (var f = 20; f <= frames; f++)
                {
                    var pose = Capture(instance, bones, clip, f / 60f);
                    scan.Add((f, Max(bones, isHair, false, first, pose).Angle,
                        Max(bones, isHair, true, first, pose).Angle));
                }

                scan.Sort((a, b) =>
                    Mathf.Max(a.Angle, a.HairAngle).CompareTo(Mathf.Max(b.Angle, b.HairAngle)));

                sb.AppendLine($"   {"结束帧",-10}{"秒",-10}{"身体最大差",-14}头发最大差");
                sb.AppendLine("   " + new string('-', 56));
                for (var i = 0; i < Mathf.Min(10, scan.Count); i++)
                {
                    var (f, angle, hairAngle) = scan[i];
                    sb.AppendLine($"   {f,-10}{f / 60f,-10:F3}{angle,-14:F3}{hairAngle:F3}");
                }

                var best = scan.Count > 0 ? scan[0] : (0, 0f, 0f);
                sb.AppendLine();
                sb.AppendLine(Mathf.Max(best.Item2, best.Item3) < 5f
                    ? $"   → 存在可用的循环点：第 {best.Item1} 帧（{best.Item1 / 60f:F3}s）。"
                    : "   → 没有任何结束点能让头发闭环，差值最小也有 " +
                      $"{Mathf.Max(best.Item2, best.Item3):F2}°：必须重新导出。");

                sb.AppendLine();
                sb.AppendLine("## 3. 头发漂移是单调的还是来回的");
                sb.AppendLine();
                sb.AppendLine("   每 10 帧取一次，看头发相对首帧的偏差怎么走。");
                sb.AppendLine();
                for (var f = 0; f <= frames; f += 10)
                {
                    var pose = Capture(instance, bones, clip, f / 60f);
                    var h = Max(bones, isHair, true, first, pose).Angle;
                    var b = Max(bones, isHair, false, first, pose).Angle;
                    sb.AppendLine($"   第 {f,3} 帧（{f / 60f:F3}s）  头发 {h,7:F2}°   身体 {b,7:F2}°");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? ".");
            File.WriteAllText(OutputPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[idle 循环扫描] 已写出 {OutputPath}\n{sb}");
        }

        private static Quaternion[] Capture(
            GameObject instance, Transform[] bones, AnimationClip clip, float time)
        {
            clip.SampleAnimation(instance, time);
            var pose = new Quaternion[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                pose[i] = bones[i].localRotation;
            }

            return pose;
        }

        private static (float Angle, string Bone) Max(
            Transform[] bones, bool[] isHair, bool wantHair, Quaternion[] a, Quaternion[] b)
        {
            var max = 0f;
            var bone = "-";
            for (var i = 0; i < bones.Length; i++)
            {
                if (isHair[i] != wantHair)
                {
                    continue;
                }

                var angle = Quaternion.Angle(a[i], b[i]);
                if (angle > max)
                {
                    max = angle;
                    bone = bones[i].name;
                }
            }

            return (max, bone);
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
    }
}
#endif
