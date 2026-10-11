using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Naraka.Boot;
using Naraka.Core.Application.Scenes;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using Naraka.Features.World.Controller;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using Cysharp.Threading.Tasks;

namespace Naraka.P2.PlayMode.Tests
{
    /// <summary>
    /// 回 Idle 卡帧的运行期测量（诊断，不是验收测试）。
    ///
    /// 在正式任务场景里逐一触发每一类动作，逐帧记录到 CSV，并对每一次"交还 Idle"
    /// 给出交接窗口内外的峰值对照。它回答离线扫描回答不了的问题：
    /// 交接时有没有真实的帧时间尖峰或 GC，Animator 的倍率与过渡实际怎么走，
    /// 姿态跳变是不是真的在那 0.1 秒里发生。
    ///
    /// 标成 <see cref="ExplicitAttribute"/>：常规全量测试不跑它，只在指名运行时执行，
    /// 不断言任何东西，只写出 <c>artifacts/ci/unity/pre-p4-idle-transition-runtime/</c>。
    /// </summary>
    [Explicit("诊断：只在指名运行时执行。")]
    public sealed class IdleTransitionRecorderTests
    {
        private const float SceneLoadTimeoutSeconds = 30f;
        private const string OutputDirectory = "artifacts/ci/unity/pre-p4-idle-transition-runtime";

        /// <summary>交接窗口：交接前 12 帧到交接后 18 帧（60 帧下约 0.2 秒与 0.3 秒）。</summary>
        private const int WindowBefore = 12;
        private const int WindowAfter = 18;

        private InputTestFixture _input;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private int _previousFrameRate;

        [SetUp]
        public void SetUp()
        {
            DestroyPersistentRoot();
            _input = new InputTestFixture();
            _input.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();

            // 批处理下不限帧率，一帧可能只有一毫秒；按游戏实际目标帧率跑，节奏才有代表性。
            _previousFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = 60;
        }

        [TearDown]
        public void TearDown()
        {
            Application.targetFrameRate = _previousFrameRate;
            DestroyPersistentRoot();
            _input?.TearDown();
            _input = null;
        }

        [UnityTest]
        [Timeout(1200000)]
        public IEnumerator RecordEveryReturnToIdle()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return null;
            Assert.That(AppRootLifetimeScope.Instance, Is.Not.Null);
            yield return null;

            var world = AppRootLifetimeScope.Instance.Container.Resolve<IWorldFlowController>();
            var entering = world.EnterMap01Async(default).Preserve();

            // 出场动画在进入场景那一刻就开始播，所以记录器要在角色一出现就挂上。
            PlayerCharacterView player = null;
            var deadline = Time.realtimeSinceStartup + SceneLoadTimeoutSeconds;
            while (player == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                player = UnityEngine.Object.FindObjectOfType<PlayerCharacterView>();
            }

            Assert.That(player, Is.Not.Null, "地图一里没有出现玩家。");
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(WorldSceneNames.Map01Task));

            var sampler = player.gameObject.AddComponent<IdleTransitionSampler>();
            var segments = new List<Segment>();

            yield return Record(sampler, segments, "出场（大厅→地图一）", null);

            yield return Record(sampler, segments, "普攻第一段", Click());
            yield return Record(sampler, segments, "普攻第二段", ClickUntil(player, PlayerAnimation.AttackCombo2));
            yield return Record(sampler, segments, "普攻第三段", ClickUntil(player, PlayerAnimation.AttackCombo3));
            yield return Record(sampler, segments, "蓄力", Hold(_mouse.leftButton, 1.2f));
            yield return Record(sampler, segments, "F 技能", Hold(_keyboard[Key.F], 0.1f));
            yield return Record(sampler, segments, "V 技能", Hold(_keyboard[Key.V], 0.1f));
            yield return Record(sampler, segments, "冲刺", Hold(_keyboard[Key.LeftShift], 0.08f));
            yield return Record(sampler, segments, "走路停止", Hold(_keyboard[Key.W], 1.0f));
            yield return Record(sampler, segments, "奔跑停止", HoldTogether(_keyboard[Key.LeftShift], _keyboard[Key.W], 1.6f));
            yield return Record(sampler, segments, "受击", Hit(player));
            yield return Record(sampler, segments, "待机动作", WaitSeconds(
                player.Tuning.Idle.VariationDelaySeconds + player.Tuning.Idle.VariationDurationSeconds + 1.5f));

            // 持续移动输入：动作结束时直接接走路，而不是回 Idle。确认这条路径同样连贯、且移动没被吞掉。
            yield return Record(sampler, segments, "普攻第一段（按住 W）", WithMoveHeld(Click()), expectIdle: false);
            yield return Record(sampler, segments, "冲刺（按住 W）", WithMoveHeld(Hold(_keyboard[Key.LeftShift], 0.08f)), expectIdle: false);

            WriteSummary(segments);
            UnityEngine.Object.Destroy(sampler);

            while (!entering.Status.IsCompleted())
            {
                yield return null;
            }
        }

        // ------------------------------------------------------------------ 动作驱动

        private IEnumerator Click()
        {
            _input.Press(_mouse.leftButton);
            yield return null;
            yield return null;
            _input.Release(_mouse.leftButton);
        }

        private IEnumerator ClickUntil(PlayerCharacterView player, PlayerAnimation wanted)
        {
            var projector = player.GetComponentInChildren<PlayerAnimatorProjector>();
            var deadline = Time.realtimeSinceStartup + 10f;
            while (projector.CurrentAnimation != wanted && Time.realtimeSinceStartup < deadline)
            {
                yield return Click();
                var pause = Time.realtimeSinceStartup + 0.25f;
                while (Time.realtimeSinceStartup < pause && projector.CurrentAnimation != wanted)
                {
                    yield return null;
                }
            }
        }

        private IEnumerator Hold(UnityEngine.InputSystem.Controls.ButtonControl button, float seconds)
        {
            _input.Press(button);
            yield return null;
            yield return null;
            yield return WaitSeconds(seconds);
            _input.Release(button);
        }

        private IEnumerator HoldTogether(
            UnityEngine.InputSystem.Controls.ButtonControl first,
            UnityEngine.InputSystem.Controls.ButtonControl second,
            float seconds)
        {
            // 同一设备的两次按键事件不能排在同一帧：测试夹具用设备"当前"状态生成第二个事件，
            // 第一个事件还没处理时，第二个事件会把它覆盖掉 —— 第一次测量里 W 就是这样被卡住的。
            _input.Press(first);
            yield return null;
            _input.Press(second);
            yield return null;
            yield return null;
            yield return WaitSeconds(seconds);
            _input.Release(second);
            yield return null;
            _input.Release(first);
            yield return null;
        }

        private IEnumerator WithMoveHeld(IEnumerator action)
        {
            _input.Press(_keyboard[Key.W]);
            yield return null;
            yield return action;
            yield return WaitSeconds(6f);
            _input.Release(_keyboard[Key.W]);
            yield return null;
        }

        private static IEnumerator Hit(PlayerCharacterView player)
        {
            player.ApplyDebugRawDamage(60f);
            yield return null;
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
        }

        // ------------------------------------------------------------------ 记录与切分

        private static IEnumerator Record(
            IdleTransitionSampler sampler,
            List<Segment> segments,
            string label,
            IEnumerator action,
            bool expectIdle = true)
        {
            sampler.Begin();
            if (action != null)
            {
                yield return action;
            }

            // 等到交还 Idle（或在移动输入下交还走路）之后再多记 1 秒。
            var deadline = Time.realtimeSinceStartup + 20f;
            var settledAt = -1f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                var settled = expectIdle
                    ? sampler.CurrentAnimation == PlayerAnimation.Idle
                    : sampler.CurrentAnimation == PlayerAnimation.Walk || sampler.CurrentAnimation == PlayerAnimation.Run;
                if (settled && sampler.HasLeftStartAnimation)
                {
                    if (settledAt < 0f)
                    {
                        settledAt = Time.realtimeSinceStartup;
                    }
                    else if (Time.realtimeSinceStartup - settledAt > 1f)
                    {
                        break;
                    }
                }
            }

            var frames = sampler.End();
            var segment = Segment.From(label, frames, expectIdle);
            segments.Add(segment);
            WriteCsv(segment, segments.Count);

            // 两段之间留出静止时间，让下一段从稳定的 Idle 开始。
            yield return WaitSeconds(1.2f);
        }

        private static void WriteCsv(Segment segment, int index)
        {
            Directory.CreateDirectory(OutputDirectory);
            var sb = new StringBuilder();
            sb.AppendLine("frame,time,dt,gcGen0,animation,state,next,inTransition,stateTime,transitionTime," +
                          "animatorSpeed,hairDegPerSec,bodyDegPerSec,rootDegPerSec,rootHeight,rootYaw,handoff");
            for (var i = 0; i < segment.Frames.Count; i++)
            {
                var f = segment.Frames[i];
                sb.Append(i).Append(',')
                    .Append(F(f.Time)).Append(',').Append(F(f.DeltaTime)).Append(',')
                    .Append(f.GcGen0).Append(',').Append(f.Animation).Append(',')
                    .Append(f.State).Append(',').Append(f.Next).Append(',')
                    .Append(f.InTransition ? 1 : 0).Append(',')
                    .Append(F(f.StateTime)).Append(',').Append(F(f.TransitionTime)).Append(',')
                    .Append(F(f.AnimatorSpeed)).Append(',')
                    .Append(F(f.HairSpeed)).Append(',').Append(F(f.BodySpeed)).Append(',').Append(F(f.RootSpeed)).Append(',')
                    .Append(F(f.RootHeight)).Append(',').Append(F(f.RootYaw)).Append(',')
                    .Append(i == segment.Handoff ? 1 : 0).AppendLine();
            }

            var safe = new string(segment.Label.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            File.WriteAllText(
                Path.Combine(OutputDirectory, $"{index:00}-{safe}.csv"), sb.ToString(), new UTF8Encoding(false));
        }

        private static void WriteSummary(List<Segment> segments)
        {
            var sb = new StringBuilder();
            sb.AppendLine("回 Idle 运行期测量  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine(new string('=', 140));
            sb.AppendLine();
            sb.AppendLine("每一行是一次交接。角速度单位 °/s，取各组骨骼的逐帧最大值。");
            sb.AppendLine("  交接前 = 交接窗口之前 30 帧的峰值（动作收招本身有多快）");
            sb.AppendLine("  窗口内 = 交接前 12 帧到交接后 18 帧的峰值（0.1 秒淡入就在这里）");
            sb.AppendLine("  交接后 = 窗口之后 30 帧的峰值（Idle 稳定之后的正常摆动）");
            sb.AppendLine("  窗口内明显高于两侧 → 交接本身造成了跳变；三者相近 → 不是交接造成的。");
            sb.AppendLine("  帧时 = 窗口内最长一帧 / 全段中位帧（毫秒）；GC = 窗口内 0 代回收次数。");
            sb.AppendLine();
            sb.AppendLine(
                $"{"段落",-22}{"交接来源",-24}{"头发 前/窗/后",-24}{"身体 前/窗/后",-24}{"根 前/窗/后",-22}" +
                $"{"倍率 前→后",-14}{"帧时 窗/中位",-16}{"GC",4}");
            sb.AppendLine(new string('-', 150));

            foreach (var s in segments)
            {
                if (s.Handoff < 0)
                {
                    sb.AppendLine($"{s.Label,-22}（没有观察到交接，记录 {s.Frames.Count} 帧）");
                    continue;
                }

                sb.AppendLine(
                    $"{s.Label,-22}{s.Source,-24}" +
                    $"{Triple(s, f => f.HairSpeed),-24}{Triple(s, f => f.BodySpeed),-24}{Triple(s, f => f.RootSpeed),-22}" +
                    $"{s.SpeedBefore,5:F2}→{s.SpeedAfter,-7:F2}" +
                    $"{s.MaxWindowDeltaMs,6:F1}/{s.MedianDeltaMs,-9:F1}{s.WindowGc,4}");
            }

            Directory.CreateDirectory(OutputDirectory);
            var path = Path.Combine(OutputDirectory, "summary.txt");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[回 Idle 运行期测量] 已写出 {path}\n{sb}");
        }

        private static string Triple(Segment s, Func<SampleFrame, float> pick) =>
            $"{s.Peak(pick, Phase.Before),6:F0}/{s.Peak(pick, Phase.Window),6:F0}/{s.Peak(pick, Phase.After),6:F0}";

        private static string F(float value) => value.ToString("0.#####", CultureInfo.InvariantCulture);

        private static void DestroyPersistentRoot()
        {
            foreach (var root in UnityEngine.Object.FindObjectsOfType<AppRootLifetimeScope>())
            {
                UnityEngine.Object.DestroyImmediate(root.gameObject);
            }
        }

        private enum Phase
        {
            Before,
            Window,
            After
        }

        private sealed class Segment
        {
            public string Label { get; private set; }
            public List<SampleFrame> Frames { get; private set; }
            public int Handoff { get; private set; } = -1;
            public string Source { get; private set; } = "-";
            public float SpeedBefore { get; private set; }
            public float SpeedAfter { get; private set; }
            public float MaxWindowDeltaMs { get; private set; }
            public float MedianDeltaMs { get; private set; }
            public int WindowGc { get; private set; }

            public static Segment From(string label, List<SampleFrame> frames, bool expectIdle)
            {
                var segment = new Segment { Label = label, Frames = frames };

                // 交接 = 投影的动画最后一次从"别的动画"变成 Idle（移动输入下是变成 Walk/Run）的那一帧。
                for (var i = frames.Count - 1; i > 0; i--)
                {
                    var now = frames[i].Animation;
                    var before = frames[i - 1].Animation;
                    var arrived = expectIdle
                        ? now == PlayerAnimation.Idle
                        : now == PlayerAnimation.Walk || now == PlayerAnimation.Run;
                    if (arrived && before != now)
                    {
                        segment.Handoff = i;
                        segment.Source = before.ToString();
                        segment.SpeedBefore = frames[i - 1].AnimatorSpeed;
                        segment.SpeedAfter = frames[i].AnimatorSpeed;
                        break;
                    }
                }

                if (frames.Count > 0)
                {
                    var deltas = frames.Select(f => f.DeltaTime).OrderBy(d => d).ToList();
                    segment.MedianDeltaMs = deltas[deltas.Count / 2] * 1000f;
                }

                if (segment.Handoff >= 0)
                {
                    var (from, to) = segment.Range(Phase.Window);
                    for (var i = from; i < to; i++)
                    {
                        segment.MaxWindowDeltaMs = Mathf.Max(segment.MaxWindowDeltaMs, frames[i].DeltaTime * 1000f);
                    }

                    segment.WindowGc = frames[to - 1].GcGen0 - frames[from].GcGen0;
                }

                return segment;
            }

            public float Peak(Func<SampleFrame, float> pick, Phase phase)
            {
                var (from, to) = Range(phase);
                var peak = 0f;
                for (var i = from; i < to; i++)
                {
                    peak = Mathf.Max(peak, pick(Frames[i]));
                }

                return peak;
            }

            private (int From, int To) Range(Phase phase)
            {
                var windowFrom = Mathf.Max(0, Handoff - WindowBefore);
                var windowTo = Mathf.Min(Frames.Count, Handoff + WindowAfter);
                return phase switch
                {
                    Phase.Before => (Mathf.Max(0, windowFrom - 30), windowFrom),
                    Phase.After => (windowTo, Mathf.Min(Frames.Count, windowTo + 30)),
                    _ => (windowFrom, windowTo)
                };
            }
        }
    }
}
