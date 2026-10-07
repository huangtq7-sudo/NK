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
using Unity.Profiling.LowLevel.Unsafe;
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
    /// **这份数据的边界必须说清楚**：Editor PlayMode 只用于回归，
    /// 正式门禁必须运行在 1920×1080、真实 GPU 的 Development Build 独立播放器中。
    /// 因此：
    ///
    /// - **GC 分配**是可信的，而且用"战斗相对空闲基线的增量"表达 ——
    ///   这样 Editor 自身的固定开销会被两边相减抵消掉，剩下的就是战斗代码本身的分配。
    ///   这正是"战斗稳定态 0B GC/frame"真正要回答的问题。
    /// - **主线程与渲染线程**使用 ProfilerRecorder 采样。
    /// - **GPU 与总帧时**在独立播放器中使用 FrameTimingManager 采样。
    /// - 无图形 Editor 环境取不到可信渲染/GPU 值时，报告仍如实写"未验证"。
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
        /// 权威架构预算是 0 B/frame，此处不再用旧的 512 B 容差放宽。
        /// </summary>
        private const long CombatAllocationBudgetBytes = 0L;

        private const double TotalFrameBudgetMs = 16.67;
        private const double MainThreadBudgetMs = 8.0;
        private const double RenderThreadBudgetMs = 8.0;
        private const double GpuFrameBudgetMs = 14.0;

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

            // Development Build 里会编译 IMGUI 调试面板，它每帧格式化字符串并走
            // GUILayout，既不属于正式 HUD，也不会进入 Release Player。性能门禁必须关掉它，
            // 否则采到的是调试字符串/GUILayout 开销，而不是战斗代码。
            foreach (var overlay in UnityEngine.Object.FindObjectsOfType<PlayerDebugOverlay>())
            {
                overlay.enabled = false;
            }

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

            if (!Application.isEditor)
            {
                Assert.That(Debug.isDebugBuild, Is.True, "正式性能门禁必须在 Development Build 中运行。");
                Assert.That(Screen.width, Is.EqualTo(1920), "正式性能门禁必须使用 1920×1080。");
                Assert.That(Screen.height, Is.EqualTo(1080), "正式性能门禁必须使用 1920×1080。");
                Assert.That(
                    SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null),
                    "正式性能门禁不能在 Null Device/-nographics 下运行。");
                Assert.That(combat.RenderValid, Is.True, "独立播放器未取到可信的渲染线程帧时。");
                Assert.That(combat.GpuValid, Is.True, "独立播放器未取到可信的 GPU 帧时。");
                Assert.That(combat.TotalValid, Is.True, "独立播放器未取到可信的总帧时。");
                Assert.That(combat.P95MainMs, Is.LessThanOrEqualTo(MainThreadBudgetMs));
                Assert.That(combat.P95RenderMs, Is.LessThanOrEqualTo(RenderThreadBudgetMs));
                Assert.That(combat.P95GpuMs, Is.LessThanOrEqualTo(GpuFrameBudgetMs));
                Assert.That(combat.P95TotalMs, Is.LessThanOrEqualTo(TotalFrameBudgetMs));
            }
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
                double p95MainMs,
                double maxMainMs,
                double medianRenderMs,
                double p95RenderMs,
                double maxRenderMs,
                double medianGpuMs,
                double p95GpuMs,
                double maxGpuMs,
                double medianTotalMs,
                double p95TotalMs,
                double maxTotalMs,
                bool renderValid,
                bool gpuValid,
                bool totalValid)
            {
                Label = label;
                Frames = frames;
                MedianGcBytes = medianGcBytes;
                MaxGcBytes = maxGcBytes;
                TotalGcBytes = totalGcBytes;
                MedianMainMs = medianMainMs;
                P95MainMs = p95MainMs;
                MaxMainMs = maxMainMs;
                MedianRenderMs = medianRenderMs;
                P95RenderMs = p95RenderMs;
                MaxRenderMs = maxRenderMs;
                MedianGpuMs = medianGpuMs;
                P95GpuMs = p95GpuMs;
                MaxGpuMs = maxGpuMs;
                MedianTotalMs = medianTotalMs;
                P95TotalMs = p95TotalMs;
                MaxTotalMs = maxTotalMs;
                RenderValid = renderValid;
                GpuValid = gpuValid;
                TotalValid = totalValid;
            }

            public string Label { get; }

            public int Frames { get; }

            public long MedianGcBytes { get; }

            public long MaxGcBytes { get; }

            public long TotalGcBytes { get; }

            public double MedianMainMs { get; }

            public double P95MainMs { get; }

            public double MaxMainMs { get; }

            public double MedianRenderMs { get; }

            public double P95RenderMs { get; }

            public double MaxRenderMs { get; }

            public double MedianGpuMs { get; }

            public double P95GpuMs { get; }

            public double MaxGpuMs { get; }

            public double MedianTotalMs { get; }

            public double P95TotalMs { get; }

            public double MaxTotalMs { get; }

            public bool RenderValid { get; }

            public bool GpuValid { get; }

            public bool TotalValid { get; }
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
            var render = StartAvailableRecorder("Render Thread", ProfilerCategory.Render);
            var gpuRecorder = StartAvailableRecorder("GPU Frame Time", ProfilerCategory.Render);

            // 1080p 独立播放器通常在 8 秒内跑 600–1500 帧。预留足够容量，
            // 避免 List 扩容本身污染 GC Allocated In Frame 采样。
            const int initialCapacity = 2048;
            var gcSamples = new List<long>(initialCapacity);
            var mainSamples = new List<long>(initialCapacity);
            var renderSamples = new List<long>(initialCapacity);
            var gpuSamples = new List<long>(initialCapacity);
            var totalSamples = new List<long>(initialCapacity);
            var latestTiming = new FrameTiming[1];

            try
            {
                // 第一帧丢掉：Recorder 刚启动时的值还没有意义。
                yield return null;

                var start = Time.realtimeSinceStartup;
                for (var i = 0; i < SampleFrames ||
                                Time.realtimeSinceStartup - start < MinSampleSeconds; i++)
                {
                    onFrame?.Invoke();

                    // 请求 Unity 在本帧结束时捕获 CPU/GPU timing，下一帧读取。
                    FrameTimingManager.CaptureFrameTimings();
                    yield return null;

                    if (gc.Valid)
                    {
                        gcSamples.Add(gc.LastValue);
                    }

                    if (main.Valid)
                    {
                        mainSamples.Add(main.LastValue);
                    }

                    if (render.Valid && render.LastValue > 0L)
                    {
                        renderSamples.Add(render.LastValue);
                    }

                    totalSamples.Add((long)Math.Round(Time.unscaledDeltaTime * 1_000_000_000.0));

                    if (FrameTimingManager.GetLatestTimings(1, latestTiming) > 0 &&
                        latestTiming[0].gpuFrameTime > 0.0)
                    {
                        gpuSamples.Add((long)Math.Round(latestTiming[0].gpuFrameTime * 1_000_000.0));
                    }
                    else if (gpuRecorder.Valid && gpuRecorder.LastValue > 0L)
                    {
                        gpuSamples.Add(gpuRecorder.LastValue);
                    }
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
                    NanosToMs(Percentile95(mainSamples)),
                    NanosToMs(Max(mainSamples)),
                    NanosToMs(Median(renderSamples)),
                    NanosToMs(Percentile95(renderSamples)),
                    NanosToMs(Max(renderSamples)),
                    NanosToMs(Median(gpuSamples)),
                    NanosToMs(Percentile95(gpuSamples)),
                    NanosToMs(Max(gpuSamples)),
                    NanosToMs(Median(totalSamples)),
                    NanosToMs(Percentile95(totalSamples)),
                    NanosToMs(Max(totalSamples)),
                    renderSamples.Count > 0,
                    gpuSamples.Count > 0,
                    totalSamples.Count > 0));
            }
            finally
            {
                gc.Dispose();
                main.Dispose();
                render.Dispose();
                gpuRecorder.Dispose();
            }
        }

        /// <summary>
        /// Recorder 的分类在 Unity 版本间有差异（例如 Render Thread 在某些版本归 Internal）。
        /// 先从当前运行时暴露的 handle 中按名字找，只在找不到时用分类回退，
        /// 避免因为分类猜错而得到一串虚假的 0。
        /// </summary>
        private static ProfilerRecorder StartAvailableRecorder(
            string metricName, ProfilerCategory fallbackCategory)
        {
            var handles = new List<ProfilerRecorderHandle>(256);
            ProfilerRecorderHandle.GetAvailable(handles);
            for (var i = 0; i < handles.Count; i++)
            {
                var description = ProfilerRecorderHandle.GetDescription(handles[i]);
                if (string.Equals(description.Name, metricName, StringComparison.Ordinal))
                {
                    return new ProfilerRecorder(
                        handles[i], 1,
                        ProfilerRecorderOptions.StartImmediately |
                        ProfilerRecorderOptions.SumAllSamplesInFrame);
                }
            }

            return ProfilerRecorder.StartNew(fallbackCategory, metricName);
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

        private static long Percentile95(List<long> values)
        {
            if (values.Count == 0)
            {
                return 0L;
            }

            values.Sort();
            var index = (int)Math.Ceiling(values.Count * 0.95) - 1;
            return values[Mathf.Clamp(index, 0, values.Count - 1)];
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
            sb.AppendLine($"  {"阶段",-12}{"帧数",-7}{"GC中位数",-13}{"主线程P95",-14}" +
                          $"{"渲染线程P95",-14}{"GPU P95",-13}{"总帧P95",-13}");
            sb.AppendLine("  " + new string('-', 86));
            foreach (var sample in new[] { baseline, combat })
            {
                sb.AppendLine(
                    $"  {sample.Label,-12}{sample.Frames,-7}{sample.MedianGcBytes + " B",-13}" +
                    $"{FormatMetric(sample.P95MainMs, true),-14}" +
                    $"{FormatMetric(sample.P95RenderMs, sample.RenderValid),-14}" +
                    $"{FormatMetric(sample.P95GpuMs, sample.GpuValid),-13}" +
                    $"{FormatMetric(sample.P95TotalMs, sample.TotalValid),-13}");
            }

            sb.AppendLine();
            sb.AppendLine("  战斗稳定态中位数 / P95 / 峰值：");
            sb.AppendLine($"    主线程  {FormatTriple(combat.MedianMainMs, combat.P95MainMs, combat.MaxMainMs, true)}");
            sb.AppendLine($"    渲染线程{FormatTriple(combat.MedianRenderMs, combat.P95RenderMs, combat.MaxRenderMs, combat.RenderValid)}");
            sb.AppendLine($"    GPU     {FormatTriple(combat.MedianGpuMs, combat.P95GpuMs, combat.MaxGpuMs, combat.GpuValid)}");
            sb.AppendLine($"    总帧时   {FormatTriple(combat.MedianTotalMs, combat.P95TotalMs, combat.MaxTotalMs, combat.TotalValid)}");

            var delta = combat.MedianGcBytes - baseline.MedianGcBytes;
            sb.AppendLine();
            sb.AppendLine($"  **战斗相对空闲基线的每帧分配增量：{delta} B**" +
                          $"（预算 ≤ {CombatAllocationBudgetBytes} B）");
            sb.AppendLine();
            sb.AppendLine("  用增量而不是绝对值：Test Runner/Profiler 有固定每帧分配，" +
                          "空闲与战斗的中位数差才是本门禁关心的稳定态增量。");

            sb.AppendLine();
            sb.AppendLine("## 阶段预算对照（`NARAKA_技术架构.md` §15）");
            sb.AppendLine();
            sb.AppendLine($"  {"指标",-22}{"目标",-14}{"本次",-26}结论");
            sb.AppendLine("  " + new string('-', 86));
            sb.AppendLine($"  {"战斗稳定态 GC/帧",-22}{"0 B 新增",-14}" +
                          $"{delta + " B（相对基线）",-26}" +
                          $"{(delta <= CombatAllocationBudgetBytes ? "通过" : "**不通过**")}");
            sb.AppendLine($"  {"主线程帧时",-22}{"≤ 8 ms",-14}" +
                          $"{FormatMetric(combat.P95MainMs, true) + "（P95）",-26}" +
                          $"{GateConclusion(combat.P95MainMs, MainThreadBudgetMs, !Application.isEditor)}");
            sb.AppendLine($"  {"渲染线程帧时",-22}{"≤ 8 ms",-14}" +
                          $"{FormatMetric(combat.P95RenderMs, combat.RenderValid) + "（P95）",-26}" +
                          $"{GateConclusion(combat.P95RenderMs, RenderThreadBudgetMs, !Application.isEditor && combat.RenderValid)}");
            sb.AppendLine($"  {"GPU 帧时",-22}{"≤ 14 ms",-14}" +
                          $"{FormatMetric(combat.P95GpuMs, combat.GpuValid) + "（P95）",-26}" +
                          $"{GateConclusion(combat.P95GpuMs, GpuFrameBudgetMs, !Application.isEditor && combat.GpuValid)}");
            sb.AppendLine($"  {"总帧预算",-22}{"≤ 16.67 ms",-14}" +
                          $"{FormatMetric(combat.P95TotalMs, combat.TotalValid) + "（P95）",-26}" +
                          $"{GateConclusion(combat.P95TotalMs, TotalFrameBudgetMs, !Application.isEditor && combat.TotalValid)}");

            sb.AppendLine();
            sb.AppendLine("## 验收边界");
            sb.AppendLine();
            sb.AppendLine(Application.isEditor
                ? "  当前是 Editor 回归数据；渲染/GPU/总帧不得用于关闭正式门禁。"
                : "  当前是 1920×1080 Development Build 独立播放器数据；P95 用于稳定态门禁，峰值保留用于定位偶发尖峰。");

            var outputPath = ResolveOutputPath();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[性能采集] 已写出 {outputPath}\n{sb}");
        }

        private static string FormatMetric(double value, bool valid) =>
            valid ? value.ToString("F3", CultureInfo.InvariantCulture) + " ms" : "**未验证**";

        private static string FormatTriple(double median, double p95, double max, bool valid) =>
            valid
                ? $"{median.ToString("F3", CultureInfo.InvariantCulture)} / " +
                  $"{p95.ToString("F3", CultureInfo.InvariantCulture)} / " +
                  $"{max.ToString("F3", CultureInfo.InvariantCulture)} ms"
                : "**未验证**";

        private static string GateConclusion(double value, double budget, bool formalEnvironment)
        {
            if (!formalEnvironment)
            {
                return "未在正式独立播放器环境验证";
            }

            return value <= budget ? "通过" : "**不通过**";
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
