using Naraka.Features.Character.Model;
using NUnit.Framework;

namespace Naraka.P2.Tests
{
    /// <summary>
    /// 三段连招、输入缓存、连招重置与蓄力。
    ///
    /// 提交时机的约定：普通攻击在"鼠标左键释放且按住时长不足蓄力阈值"时提交，
    /// 蓄力在"按住达到阈值"时提交。这样一次按键只会产生其中一种结果。
    /// </summary>
    public sealed class PlayerComboTests
    {
        private const float Frame = 1f / 60f;

        private static PlayerCore NewCore() => new PlayerCore(PlayerTuning.CreateBaseline());

        [Test]
        public void ComboAdvancesInOrder()
        {
            var core = NewCore();
            var tuning = core.Tuning.Combo;

            ClickAttack(core);
            Assert.That(core.Action, Is.EqualTo(ActionState.AttackCombo1));

            AdvanceIntoComboWindow(core, tuning.Step1);
            ClickAttack(core);
            Assert.That(core.Action, Is.EqualTo(ActionState.AttackCombo2));

            AdvanceIntoComboWindow(core, tuning.Step2);
            ClickAttack(core);
            Assert.That(core.Action, Is.EqualTo(ActionState.AttackCombo3));
        }

        [Test]
        public void ThirdStepWrapsBackToTheFirst()
        {
            var core = NewCore();
            RunFullCombo(core);

            // 第三段结束后立刻再点一次：必须是第一段而不是第四段。
            ClickAttack(core);
            Assert.That(core.Action, Is.EqualTo(ActionState.AttackCombo1));
        }

        [Test]
        public void ASingleClickAdvancesExactlyOneStep()
        {
            var core = NewCore();
            ClickAttack(core);
            Assert.That(core.Action, Is.EqualTo(ActionState.AttackCombo1));

            // 不再点击，只让时间走完整段：不能自己跳到第二段。
            PlayerLocomotionTests.Hold(
                core, PlayerInputFrame.Idle, core.Tuning.Combo.Step1.RealDurationSeconds + 0.05f);

            Assert.That(core.Action, Is.EqualTo(ActionState.None));
            Assert.That(core.ComboStep, Is.EqualTo(1));
        }

        [Test]
        public void InputBufferedWithinTheWindowIsConsumed()
        {
            var core = NewCore();
            var step1 = core.Tuning.Combo.Step1;
            var buffer = core.Tuning.Combo.InputBufferSeconds;
            ClickAttack(core);
            Assert.That(core.Action, Is.EqualTo(ActionState.AttackCombo1));

            // 提前到连段窗口开启前 0.1 秒点击：还在 0.18 秒缓存内，窗口一开就该兑现。
            AdvanceToClipTime(core, step1, step1.ComboWindowStart - 0.2f);
            ClickAttack(core);
            Assert.That(core.Action, Is.EqualTo(ActionState.AttackCombo1), "窗口未开时不得提前推进。");

            AdvanceToClipTime(core, step1, step1.ComboWindowStart + 0.05f);

            Assert.That(core.Action, Is.EqualTo(ActionState.AttackCombo2),
                $"{buffer} 秒输入缓存内的点击必须在连段窗口开启时被消费。");
        }

        [Test]
        public void InputOlderThanTheBufferIsDropped()
        {
            var core = NewCore();
            var buffer = core.Tuning.Combo.InputBufferSeconds;
            Assert.That(buffer, Is.EqualTo(0.18f));

            var step1 = core.Tuning.Combo.Step1;
            ClickAttack(core);

            // 在连段窗口打开前很久点一次，缓存必须在窗口开启之前就过期。
            ClickAttack(core);
            Assert.That(
                step1.ClipTimeAt(core.ActionElapsedSeconds + buffer),
                Is.LessThan(step1.ComboWindowStart),
                "测试前提：这次点击的缓存必须早于连段窗口过期。");
            AdvanceToClipTime(core, step1, step1.ComboWindowStart + 0.05f);

            Assert.That(core.Action, Is.EqualTo(ActionState.AttackCombo1),
                "超过缓存时长的输入必须被丢弃，不能在窗口打开时兑现。");
        }

        [Test]
        public void ComboResetsAfterEightHundredMilliseconds()
        {
            var core = NewCore();
            Assert.That(core.Tuning.Combo.ResetSeconds, Is.EqualTo(0.8f));

            ClickAttack(core);
            PlayerLocomotionTests.Hold(
                core, PlayerInputFrame.Idle, core.Tuning.Combo.Step1.RealDurationSeconds + 0.05f);
            Assert.That(core.ComboStep, Is.EqualTo(1));

            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, 0.85f);
            Assert.That(core.ComboStep, Is.EqualTo(0));

            ClickAttack(core);
            Assert.That(core.Action, Is.EqualTo(ActionState.AttackCombo1));
        }

        [Test]
        public void HoldingTwoSecondsTriggersTheChargeAttack()
        {
            var core = NewCore();
            Assert.That(core.Tuning.Charge.HoldSeconds, Is.EqualTo(0.75f));

            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle.WithAttack(true), 0.85f);

            Assert.That(core.Action, Is.EqualTo(ActionState.Charge));
        }

        [Test]
        public void AShortClickNeverTriggersTheCharge()
        {
            var core = NewCore();

            ClickAttack(core);

            Assert.That(core.Action, Is.EqualTo(ActionState.AttackCombo1));
            Assert.That(core.Action, Is.Not.EqualTo(ActionState.Charge));
        }

        [Test]
        public void ReleasingAfterTheChargeThresholdDoesNotAlsoQueueANormalAttack()
        {
            var core = NewCore();
            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle.WithAttack(true), 0.85f);
            Assert.That(core.Action, Is.EqualTo(ActionState.Charge));

            // 松手，然后等蓄力动作播完：不能紧接着又冒出一段普通攻击。
            core.Tick(PlayerInputFrame.Idle, Frame);
            PlayerLocomotionTests.Hold(
                core, PlayerInputFrame.Idle, core.Tuning.Charge.Action.RealDurationSeconds + 0.2f);

            Assert.That(core.Action, Is.EqualTo(ActionState.None),
                "同一次按键不能既打普通攻击又打蓄力。");
        }

        [Test]
        public void ChargeCarriesSuperArmorButNotInvulnerability()
        {
            var core = NewCore();
            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle.WithAttack(true), 0.85f);
            Assert.That(core.Action, Is.EqualTo(ActionState.Charge));
            Assert.That((core.Flags & PlayerOverlayFlags.SuperArmor), Is.Not.EqualTo(PlayerOverlayFlags.None));

            var before = core.Health + core.Armor;
            core.ApplyDamage(200f);

            Assert.That(core.Health + core.Armor, Is.LessThan(before), "霸体不免伤害。");
            Assert.That(core.Reaction, Is.EqualTo(ReactionState.None), "霸体免硬直。");
            Assert.That(core.Action, Is.EqualTo(ActionState.Charge), "霸体期间蓄力不被打断。");
        }

        [Test]
        public void MovementIsBlockedWhileCharging()
        {
            var core = NewCore();
            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle.WithAttack(true), 0.85f);

            var output = core.Tick(
                PlayerInputFrame.Idle.WithAttack(true).WithMove(1f, 1f), Frame);

            Assert.That(output.TurnDegrees, Is.EqualTo(0f), "蓄力期间禁止自由旋转。");
            Assert.That(
                output.ForwardSpeed, Is.EqualTo(0f).Within(0.001f),
                "蓄力是攻击，攻击不改变坐标，因此位移为 0。");
        }

        [Test]
        public void AttackWindowExposesAStableAttackIdPerSwing()
        {
            var core = NewCore();
            ClickAttack(core);
            var step1 = core.Tuning.Combo.Step1;

            var ids = new System.Collections.Generic.HashSet<int>();
            var elapsed = 0f;
            while (elapsed < step1.RealDurationSeconds)
            {
                var output = core.Tick(PlayerInputFrame.Idle, Frame);
                if (output.HitWindowOpen)
                {
                    ids.Add(output.AttackId);
                }

                elapsed += Frame;
            }

            Assert.That(ids.Count, Is.EqualTo(1), "同一次挥击在整个命中窗内必须是同一个 AttackId。");
            Assert.That(ids.Contains(0), Is.False);
        }

        [Test]
        public void EachComboStepGetsADistinctAttackId()
        {
            var core = NewCore();
            ClickAttack(core);
            var first = FirstAttackIdOf(core, ActionState.AttackCombo1);
            AdvanceIntoComboWindow(core, core.Tuning.Combo.Step1);
            ClickAttack(core);
            var second = FirstAttackIdOf(core, ActionState.AttackCombo2);

            Assert.That(second, Is.Not.EqualTo(first));
        }

        [Test]
        public void AttacksDoNotChangeTheCharacterPosition()
        {
            var tuning = PlayerTuning.CreateBaseline();

            // 攻击前后坐标必须不变，因此三段连招、蓄力与 F 技能的位移都是 0。
            Assert.That(tuning.Combo.Step1.ForwardDisplacement, Is.EqualTo(0f));
            Assert.That(tuning.Combo.Step2.ForwardDisplacement, Is.EqualTo(0f));
            Assert.That(tuning.Combo.Step3.ForwardDisplacement, Is.EqualTo(0f));
            Assert.That(tuning.Charge.Action.ForwardDisplacement, Is.EqualTo(0f));
            Assert.That(tuning.SkillF.Action.ForwardDisplacement, Is.EqualTo(0f));

            // 前摇与后摇仍然可配置。
            Assert.That(tuning.Combo.Step1.HitWindowStart, Is.GreaterThan(0f), "前摇可配置。");
            Assert.That(
                tuning.Combo.Step1.ClipSeconds - tuning.Combo.Step1.HitWindowEnd,
                Is.GreaterThan(0f),
                "后摇可配置。");
        }

        [Test]
        public void AFullComboChainAccumulatesNoDisplacement()
        {
            var core = NewCore();
            var travelled = 0f;
            var guard = 0;

            ClickAttack(core);
            while (core.Action != ActionState.None && guard++ < 20000)
            {
                travelled += core.Tick(PlayerInputFrame.Idle, Frame).ForwardSpeed * Frame;
            }

            Assert.That(travelled, Is.EqualTo(0f).Within(0.0001f), "攻击不得改变坐标。");
        }

        [Test]
        public void DashAndSkillVAreInstantBursts()
        {
            var tuning = PlayerTuning.CreateBaseline();

            foreach (var (name, action) in new[]
                     {
                         ("Move_F 冲刺", tuning.Dash),
                         ("V 技能", tuning.SkillV.Action)
                     })
            {
                Assert.That(action.ForwardDisplacement, Is.Not.EqualTo(0f), $"{name} 必须真的改变位置。");
                Assert.That(
                    action.DisplacementClipSeconds, Is.GreaterThan(0f),
                    $"{name} 必须有位移窗口，否则位移会铺满整段动作变成缓慢滑行。");
                Assert.That(
                    action.DisplacementClipSeconds, Is.LessThan(action.ClipSeconds * 0.4f),
                    $"{name} 的位移窗口必须明显短于整段动作，才是瞬间冲刺。");

                // 位移结束之后速度必须归零，不能继续缓慢移动。
                var afterWindowReal = action.RealDurationSeconds * 0.9f;
                Assert.That(
                    action.ForwardSpeedAt(afterWindowReal), Is.EqualTo(0f),
                    $"{name} 的位移窗口之后不得继续移动。");
            }
        }

        [Test]
        public void RecoveryPlaysFasterThanTheMainPhase()
        {
            var tuning = PlayerTuning.CreateBaseline();

            foreach (var action in new[]
                     {
                         tuning.Combo.Step1, tuning.Combo.Step2, tuning.Combo.Step3,
                         tuning.SkillF.Action, tuning.SkillV.Action,
                         tuning.Reaction.SpawnMap01ToMap02
                     })
            {
                Assert.That(
                    action.Playback.RecoverySpeed, Is.GreaterThan(action.Playback.MainSpeed),
                    "后摇必须比主体快，否则收招拖沓。");
            }
        }

        [Test]
        public void PlaybackSpeedShortensTheActionWithoutMovingTheWindows()
        {
            var slow = new TimedActionTuning(
                new ActionPlayback(3f, 1f, 1f, 1.5f), 0.5f, 1.5f, 1f, 3f, 0f, 0f, 10f);
            var fast = new TimedActionTuning(
                new ActionPlayback(3f, 2f, 4f, 1.5f), 0.5f, 1.5f, 1f, 3f, 0f, 0f, 10f);

            Assert.That(slow.RealDurationSeconds, Is.EqualTo(3f).Within(0.001f));
            // 主体 1.5/2 = 0.75 秒，后摇 1.5/4 = 0.375 秒。
            Assert.That(fast.RealDurationSeconds, Is.EqualTo(1.125f).Within(0.001f));

            // 窗口是片段时间，因此两者的命中窗完全一样。
            Assert.That(fast.HitWindowStart, Is.EqualTo(slow.HitWindowStart));
            Assert.That(fast.HitWindowEnd, Is.EqualTo(slow.HitWindowEnd));

            // 快的那一个在更早的真实时间到达同一个片段时间点。
            Assert.That(fast.ClipTimeAt(0.25f), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(slow.ClipTimeAt(0.25f), Is.EqualTo(0.25f).Within(0.001f));
        }

        [Test]
        public void DisplacementTotalIsIndependentOfPlaybackSpeed()
        {
            foreach (var speed in new[] { 1f, 1.6f, 3f })
            {
                var action = new TimedActionTuning(
                    new ActionPlayback(2f, speed), 0f, 0f, 2f, 2f, 10f, 0.5f, 0f);

                var travelled = 0f;
                var elapsed = 0f;
                const float step = 1f / 480f;
                while (elapsed < action.RealDurationSeconds)
                {
                    travelled += action.ForwardSpeedAt(elapsed) * step;
                    elapsed += step;
                }

                Assert.That(
                    travelled, Is.EqualTo(10f).Within(0.2f),
                    $"播放速度 {speed} 下总位移必须仍然是 10。");
            }
        }

        [Test]
        public void AnimationSpeedSwitchesToRecoveryAfterTheHitWindow()
        {
            var core = NewCore();
            var step1 = core.Tuning.Combo.Step1;
            ClickAttack(core);

            var main = core.Tick(PlayerInputFrame.Idle, Frame).AnimationSpeed;
            Assert.That(main, Is.EqualTo(step1.Playback.MainSpeed).Within(0.001f));

            AdvanceToClipTime(core, step1, step1.HitWindowEnd + 0.05f);
            var recovery = core.Tick(PlayerInputFrame.Idle, Frame).AnimationSpeed;

            Assert.That(
                recovery, Is.EqualTo(step1.Playback.RecoverySpeed).Within(0.001f),
                "命中窗结束之后必须切到后摇速度。");
        }

        /// <summary>模拟一次短按：按下一帧后立刻释放。</summary>
        internal static void ClickAttack(PlayerCore core)
        {
            core.Tick(PlayerInputFrame.Idle.WithAttack(true), Frame);
            core.Tick(PlayerInputFrame.Idle, Frame);
        }

        /// <summary>推进到当前动作的指定**片段时间**。窗口都是片段时间，不是真实时间。</summary>
        private static void AdvanceToClipTime(PlayerCore core, TimedActionTuning step, float clipTime)
        {
            var guard = 0;
            while (step.ClipTimeAt(core.ActionElapsedSeconds) < clipTime && guard++ < 20000)
            {
                core.Tick(PlayerInputFrame.Idle, Frame);
            }
        }

        private static void AdvanceIntoComboWindow(PlayerCore core, TimedActionTuning step)
        {
            var target = (step.ComboWindowStart + step.ComboWindowEnd) * 0.5f;
            AdvanceToClipTime(core, step, target);
        }

        private static void RunFullCombo(PlayerCore core)
        {
            var tuning = core.Tuning.Combo;
            ClickAttack(core);
            AdvanceIntoComboWindow(core, tuning.Step1);
            ClickAttack(core);
            AdvanceIntoComboWindow(core, tuning.Step2);
            ClickAttack(core);
            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, tuning.Step3.RealDurationSeconds + 0.05f);
        }

        private static int FirstAttackIdOf(PlayerCore core, ActionState expected)
        {
            Assert.That(core.Action, Is.EqualTo(expected));
            while (true)
            {
                var output = core.Tick(PlayerInputFrame.Idle, Frame);
                if (output.HitWindowOpen)
                {
                    return output.AttackId;
                }

                if (core.Action != expected)
                {
                    Assert.Fail($"{expected} 结束前没有出现命中窗。");
                }
            }
        }
    }
}
