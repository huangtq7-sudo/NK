using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using Naraka.Boot;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using Naraka.Features.Monster.Model;
using Naraka.Features.Monster.View;
using Naraka.Features.World.Controller;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Naraka.P2.PlayMode.Tests
{
    /// <summary>
    /// P2 战斗稳定态的性能采集。
    ///
    /// 这不是"跑一遍看看顺不顺"，而是按 `NARAKA_开发规范.md` §16 的要求
    /// 给出可复现的 Profiler 数据。它刻意放在 Tests 里：
    /// 采集代码不得进入正式业务运行路径，也不得改变玩法。
    ///
    /// **这份数据的边界必须说清楚**：它在 Editor 的 PlayMode 里采，不是
    /// Development Build 连 Profiler。因此：
    ///
    /// - **GC 分配**是可信的，而且用"战斗相对空闲基线的增量"表达 ——
    ///   这样 Editor 自身的固定开销会被两边相减抵消掉，剩下的就是战斗代码本身的分配。
    ///   这正是"战斗稳定态 0B GC/frame"真正要回答的问题。
    /// - **主线程帧时**只能当上限参考：Editor 的开销比播放器大，这个数偏悲观。
    /// - **渲染线程与 GPU 帧时**在批处理/无图形模式下取不到可信值，
    ///   取不到就写"未验证"，绝不推测，也绝不填 0。
    ///
    /// 要拿正式的渲染线程与 GPU 数据，必须由人把 Profiler 连到 Development Build 上，
    /// 这一步自动化做不了。
    /// </summary>
    public sealed class P2PerformanceProfileTests
    {
        private const string OutputRelativePath = "artifacts/p2-performance-profile.txt";
        private const float SceneLoadTimeoutSeconds = 30f;

        /// <summary>预热时长。首次场景加载、Shader 编译与资源初始化都要落在这段里。</summary>
        private const float WarmupSeconds = 10f;

        /// <summary>每个阶段的采样帧数。</summary>
        private const int SampleFrames = 300;

        /// <summary>
        /// 每个阶段的最短采样时长（秒）。
        ///
        /// 只定帧数不够：无图形模式下 300 帧只有两三秒，
        /// 而普攻冷却就有 2 秒、吐息冷却 12 秒 —— 第一版采集因此只覆盖到吐息、
        /// 漏掉了普攻。而本阶段要证明的是"巡逻、追击、普攻、吐息与受击整个循环"
        /// 都不产生每帧垃圾，所以必须让采样窗口长到真的跨过这些动作。
        /// </summary>
        private const float MinSampleSeconds = 8f;

        /// <summary>
        /// 战斗相对空闲基线允许多出多少每帧分配（字节）。
        ///
        /// 取 512：一次 LINQ、一次装箱或一个临时数组都远超这个量，
        /// 因此它足以抓出"战斗路径里有每帧垃圾"这件事，
        /// 又不会被 Editor 自身的抖动误判。
        /// </summary>
        private const long CombatAllocationBudgetBytes = 512L;

        [SetUp]
        public void SetUp() => DestroyPersistentRoot();

        [TearDown]
        public void TearDown() => DestroyPersistentRoot();

        private static void DestroyPersistentRoot()
        {
            foreach (var root in UnityEngine.Object.FindObjectsOfType<AppRootLifetimeScope>())
            {
                UnityEngine.Object.DestroyImmediate(root.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator CombatSteadyStateAddsNoPerFrameGarbage()
        {
            yield return EnterMap02();

            var player = UnityEngine.Object.FindObjectOfType<PlayerCharacterView>();
            var wolf = UnityEngine.Object.FindObjectOfType<DuskshadowWolfView>();
            Assert.That(player, Is.Not.Null);
            Assert.That(wolf, Is.Not.Null);

            yield return WaitUntil(() => player.State.Action == ActionState.None, 15f, "spawn-burst");

            // ---------------- 预热：首次加载、Shader 编译与资源初始化都落在这里
            var wolfHome = wolf.transform.position;
            player.TeleportTo(wolfHome + new Vector3(0f, 0f, 200f), Quaternion.identity);

            var warmupStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - warmupStart < WarmupSeconds)
            {
                yield return null;
            }

            var warmupActual = Time.realtimeSinceStartup - warmupStart;

            // ---------------- 阶段 A：空闲基线（狼休眠，玩家不动）
            Sample baseline = default;
            yield return Measure(result => baseline = result, "空闲基线");

            // ---------------- 阶段 B：战斗稳定态
            // 打到半血以下，这样采样期间同时覆盖普攻与红色吐息两条路径。
            var tuning = wolf.Controller.Tuning;
            var raw = (tuning.MaxArmor + (tuning.MaxHealth * 0.55f)) * (100f + tuning.Defense) / 100f;
            wolf.Controller.ApplyRawDamage(raw);

            player.TeleportTo(
                wolfHome + new Vector3(0f, 0f, tuning.AttackRange - 0.4f), Quaternion.identity);
            yield return WaitUntil(
                () => wolf.Controller.Current.Intent != MonsterIntent.Patrol &&
                      wolf.Controller.Current.Intent != MonsterIntent.Dormant,
                12f, "engage");

            Sample combat = default;
            var coverage = new Coverage();
            var combatStart = Time.realtimeSinceStartup;
            var hitApplied = false;
            yield return Measure(
                result => combat = result,
                "战斗稳定态",
                onFrame: () =>
                {
                    var state = wolf.Controller.Current;
                    switch (state.Action)
                    {
                        case MonsterActionState.Attack:
                            coverage.NormalAttack = true;
                            break;
                        case MonsterActionState.Skill:
                            coverage.Skill = true;
                            break;
                        case MonsterActionState.HitStun:
                            coverage.HitStun = true;
                            break;
                        case MonsterActionState.Move:
                            coverage.Chase = true;
                            break;
                    }

                    // 采样中段敲一下，把受击硬直这条路径也带进来。
                    //
                    // 按**真实时间**而不是帧序号判断：无图形模式下帧率高达两千多，
                    // 第一版按"第 150 帧"敲，那才 0.05 秒，狼正好在放吐息 ——
                    // 霸体期间不进硬直，于是这一下白敲、受击路径没被覆盖。
                    // 现在改成 3 秒后敲，并且敲不中就下一帧再试。
                    if (!hitApplied &&
                        Time.realtimeSinceStartup - combatStart >= 3f &&
                        state.Action != MonsterActionState.Skill &&
                        !state.HasSuperArmor)
                    {
                        wolf.Controller.ApplyRawDamage(60f);
                        hitApplied = true;
                    }
                });

            WriteReport(baseline, combat, warmupActual, coverage);

            // 采样期间必须真的在打，否则"战斗稳定态"这个标题是假的。
            Assert.That(
                coverage.NormalAttack || coverage.Skill, Is.True,
                "采样期间怪物没有出手，这段数据不能称为战斗稳定态。");

            var delta = combat.MedianGcBytes - baseline.MedianGcBytes;
            Assert.That(
                delta, Is.LessThanOrEqualTo(CombatAllocationBudgetBytes),
                $"战斗相对空闲基线每帧多分配 {delta} 字节（基线 {baseline.MedianGcBytes}、" +
                $"战斗 {combat.MedianGcBytes}），说明战斗路径里有每帧垃圾。" +
                $"完整数据见 {ResolveOutputPath()}。");
        }

        // ------------------------------------------------------------------ 采样

        private readonly struct Sample
        {
            public Sample(
                string label,
                int frames,
                long medianGcBytes,
                long maxGcBytes,
                long totalGcBytes,
                double medianMainMs,
                double maxMainMs,
                double medianRenderMs,
                double medianGpuMs,
                bool renderValid,
                bool gpuValid)
            {
                Label = label;
                Frames = frames;
                MedianGcBytes = medianGcBytes;
                MaxGcBytes = maxGcBytes;
                TotalGcBytes = totalGcBytes;
                MedianMainMs = medianMainMs;
                MaxMainMs = maxMainMs;
                MedianRenderMs = medianRenderMs;
                MedianGpuMs = medianGpuMs;
                RenderValid = renderValid;
                GpuValid = gpuValid;
            }

            public string Label { get; }

            public int Frames { get; }

            public long MedianGcBytes { get; }

            public long MaxGcBytes { get; }

            public long TotalGcBytes { get; }

            public double MedianMainMs { get; }

            public double MaxMainMs { get; }

            public double MedianRenderMs { get; }

            public double MedianGpuMs { get; }

            public bool RenderValid { get; }

            public bool GpuValid { get; }
        }

        /// <summary>这段采样实际覆盖到了哪些动作路径。报告里要写实话，不能笼统说"战斗"。</summary>
        private sealed class Coverage
        {
            public bool Chase;
            public bool NormalAttack;
            public bool Skill;
            public bool HitStun;

            public string Describe() =>
                $"追击 {Mark(Chase)}、普攻 {Mark(NormalAttack)}、" +
                $"吐息 {Mark(Skill)}、受击 {Mark(HitStun)}";

            private static string Mark(bool value) => value ? "有" : "无";
        }

        private static IEnumerator Measure(
            Action<Sample> onDone, string label, Action onFrame = null)
        {
            var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
            var render = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Render Thread");
            var gpu = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time");

            var gcSamples = new List<long>(SampleFrames);
            var mainSamples = new List<long>(SampleFrames);
            var renderSamples = new List<long>(SampleFrames);
            var gpuSamples = new List<long>(SampleFrames);

            try
            {
                // 第一帧丢掉：Recorder 刚启动时的值还没有意义。
                yield return null;

                var start = Time.realtimeSinceStartup;
                for (var i = 0; i < SampleFrames ||
                                Time.realtimeSinceStartup - start < MinSampleSeconds; i++)
                {
                    onFrame?.Invoke();

                    if (gc.Valid)
                    {
                        gcSamples.Add(gc.LastValue);
                    }

                    if (main.Valid)
                    {
                        mainSamples.Add(main.LastValue);
                    }

                    if (render.Valid)
                    {
                        renderSamples.Add(render.LastValue);
                    }

                    if (gpu.Valid)
                    {
                        gpuSamples.Add(gpu.LastValue);
                    }

                    yield return null;
                }

                var totalGc = 0L;
                foreach (var value in gcSamples)
                {
                    totalGc += value;
                }

                onDone(new Sample(
                    label,
                    gcSamples.Count,
                    Median(gcSamples),
                    Max(gcSamples),
                    totalGc,
                    NanosToMs(Median(mainSamples)),
                    NanosToMs(Max(mainSamples)),
                    NanosToMs(Median(renderSamples)),
                    NanosToMs(Median(gpuSamples)),
                    renderSamples.Count > 0,
                    gpuSamples.Count > 0));
            }
            finally
            {
                gc.Dispose();
                main.Dispose();
                render.Dispose();
                gpu.Dispose();
            }
        }

        private static long Median(List<long> values)
        {
            if (values.Count == 0)
            {
                return 0L;
            }

            values.Sort();
            return values[values.Count / 2];
        }

        private static long Max(List<long> values)
        {
            var max = 0L;
            foreach (var value in values)
            {
                if (value > max)
                {
                    max = value;
                }
            }

            return max;
        }

        private static double NanosToMs(long nanoseconds) => nanoseconds / 1_000_000.0;

        // ------------------------------------------------------------------ 报告

        private static void WriteReport(
            Sample baseline, Sample combat, float warmupSeconds, Coverage coverage)
        {
            var sb = new StringBuilder();
            sb.AppendLine("NARAKA P2 战斗稳定态性能采集  " +
                          DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine(new string('=', 92));
            sb.AppendLine();
            sb.AppendLine("## 采集环境");
            sb.AppendLine();
            sb.AppendLine($"  Unity 版本            {Application.unityVersion}");
            sb.AppendLine($"  操作系统              {SystemInfo.operatingSystem}");
            sb.AppendLine($"  CPU                   {SystemInfo.processorType}" +
                          $"（{SystemInfo.processorCount} 逻辑核）");
            sb.AppendLine($"  GPU                   {SystemInfo.graphicsDeviceName}" +
                          $"（{SystemInfo.graphicsDeviceType}）");
            sb.AppendLine($"  内存                  {SystemInfo.systemMemorySize} MB");
            sb.AppendLine($"  分辨率                {Screen.width}×{Screen.height}");
            sb.AppendLine($"  运行容器              " +
                          $"{(Application.isEditor ? "Editor PlayMode（不是 Development Build）" : "独立播放器")}");
            sb.AppendLine($"  Development Build     " +
                          $"{(Application.isEditor ? "不适用（当前为 Editor PlayMode）" : Debug.isDebugBuild.ToString())}");
            sb.AppendLine("  Deep Profile          关闭（本自动化从不开启）");
            sb.AppendLine($"  预热时长              {warmupSeconds:F2} 秒（要求 ≥ {WarmupSeconds:F0}）");
            sb.AppendLine($"  每阶段采样            ≥ {SampleFrames} 帧且 ≥ {MinSampleSeconds:F0} 秒" +
                          $"（实际基线 {baseline.Frames} 帧、战斗 {combat.Frames} 帧）");
            sb.AppendLine($"  采样覆盖的动作路径    {coverage.Describe()}");
            sb.AppendLine();
            sb.AppendLine("  场景 Map02_Combat，1 名玩家 + 1 只正式暮影妖狼。");
            sb.AppendLine("  战斗阶段先把狼打到半血以下，因此同时覆盖普攻与红色吐息两条路径。");

            sb.AppendLine();
            sb.AppendLine("## 测量结果");
            sb.AppendLine();
            sb.AppendLine($"  {"阶段",-14}{"帧数",-7}{"GC/帧中位数",-15}{"GC/帧峰值",-13}" +
                          $"{"主线程中位数",-15}{"主线程峰值",-13}");
            sb.AppendLine("  " + new string('-', 80));
            foreach (var sample in new[] { baseline, combat })
            {
                sb.AppendLine(
                    $"  {sample.Label,-14}{sample.Frames,-7}{sample.MedianGcBytes + " B",-15}" +
                    $"{sample.MaxGcBytes + " B",-13}" +
                    $"{sample.MedianMainMs.ToString("F3") + " ms",-15}" +
                    $"{sample.MaxMainMs.ToString("F3") + " ms",-13}");
            }

            var delta = combat.MedianGcBytes - baseline.MedianGcBytes;
            sb.AppendLine();
            sb.AppendLine($"  **战斗相对空闲基线的每帧分配增量：{delta} B**" +
                          $"（预算 ≤ {CombatAllocationBudgetBytes} B）");
            sb.AppendLine();
            sb.AppendLine("  用增量而不是绝对值：Editor 自身有固定的每帧分配，" +
                          "两边相减之后剩下的才是战斗代码本身的分配。");

            sb.AppendLine();
            sb.AppendLine("## 阶段预算对照（`NARAKA_技术架构.md` §15）");
            sb.AppendLine();
            sb.AppendLine($"  {"指标",-22}{"目标",-14}{"本次",-26}结论");
            sb.AppendLine("  " + new string('-', 86));
            sb.AppendLine($"  {"战斗稳定态 GC/帧",-22}{"0 B 新增",-14}" +
                          $"{delta + " B（相对基线）",-26}" +
                          $"{(delta <= CombatAllocationBudgetBytes ? "通过" : "**不通过**")}");
            sb.AppendLine($"  {"主线程帧时",-22}{"≤ 8 ms",-14}" +
                          $"{combat.MedianMainMs.ToString("F3") + " ms（中位数）",-26}" +
                          "仅作上限参考：Editor 开销大于播放器，这个数偏悲观");
            sb.AppendLine($"  {"渲染线程帧时",-22}{"≤ 8 ms",-14}" +
                          $"{(combat.RenderValid ? combat.MedianRenderMs.ToString("F3") + " ms" : "**未验证**"),-26}" +
                          $"{(combat.RenderValid ? "仅参考" : "取不到可信值，不推测")}");
            sb.AppendLine($"  {"GPU 帧时",-22}{"≤ 14 ms",-14}" +
                          $"{(combat.GpuValid ? combat.MedianGpuMs.ToString("F3") + " ms" : "**未验证**"),-26}" +
                          $"{(combat.GpuValid ? "仅参考" : "取不到可信值，不推测")}");
            sb.AppendLine($"  {"总帧预算",-22}{"≤ 16.67 ms",-14}{"**未验证**",-26}" +
                          "需要把 Profiler 连到 Development Build，自动化做不到");

            sb.AppendLine();
            sb.AppendLine("## 仍需人工完成");
            sb.AppendLine();
            sb.AppendLine("  1920×1080 下的渲染线程、GPU 帧时与总帧预算必须由人把 Unity Profiler");
            sb.AppendLine("  连到已构建的 Development Build 上采集（Deep Profile 保持关闭、");
            sb.AppendLine("  预热与采样分开、至少 300 帧稳定战斗）。本文件给出的 GC 结论不受此影响。");

            var outputPath = ResolveOutputPath();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[性能采集] 已写出 {outputPath}\n{sb}");
        }

        private static string ResolveOutputPath()
        {
            var projectDirectory = Directory.GetParent(Application.dataPath)?.FullName;
            var repositoryDirectory = projectDirectory != null
                ? Directory.GetParent(projectDirectory)?.FullName
                : null;

            Assert.That(repositoryDirectory, Is.Not.Null.And.Not.Empty,
                "无法从 Unity Application.dataPath 定位仓库根目录。");
            return Path.Combine(repositoryDirectory, OutputRelativePath);
        }

        // ------------------------------------------------------------------ 场景

        private IEnumerator EnterMap02()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return null;
            Assert.That(AppRootLifetimeScope.Instance, Is.Not.Null, "Bootstrap 场景缺少持久化 App Root。");
            yield return null;

            var world = AppRootLifetimeScope.Instance.Container.Resolve<IWorldFlowController>();
            var task = world.EnterMap02Async(default).Preserve();
            var deadline = Time.realtimeSinceStartup + SceneLoadTimeoutSeconds;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(task.Status.IsCompleted(), Is.True, "进入地图二没有在超时前完成。");
            yield return WaitUntil(
                () => UnityEngine.Object.FindObjectOfType<PlayerCharacterView>() != null &&
                      UnityEngine.Object.FindObjectOfType<DuskshadowWolfView>() != null,
                10f, "map02-actors");
        }

        private static IEnumerator WaitUntil(
            Func<bool> condition, float timeoutSeconds, string label)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                condition(), Is.True,
                $"等待条件在 {timeoutSeconds} 秒内没有满足（{label}）。");
        }
    }
}
