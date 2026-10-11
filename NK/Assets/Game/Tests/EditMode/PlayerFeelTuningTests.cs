using Naraka.Features.Character.Model;
using NUnit.Framework;

namespace Naraka.P2.Tests
{
    /// <summary>
    /// 2026-10-11 用户确认的三项手感调整：受击不再"罚站"、F 放慢到 75%、走跑移动等比例提高到 1.3 倍、动画加速到 1.15 倍。
    /// </summary>
    public sealed class PlayerFeelTuningTests
    {
        private const float Frame = 1f / 60f;

        private static PlayerCore NewCore() => new PlayerCore(PlayerTuning.CreateBaseline());

        // ------------------------------------------------------------------ 受击

        [Test]
        public void HitStunOnlyLocksInputForTheHardStun()
        {
            var core = NewCore();
            core.ApplyDamage(50f);
            var lockSeconds = core.Tuning.Reaction.HitStunLockSeconds;
            Assert.That(lockSeconds, Is.EqualTo(0.6f));

            var move = PlayerInputFrame.Idle.WithMove(0f, 1f);
            var elapsed = 0f;
            while (elapsed + Frame < lockSeconds - 0.01f)
            {
                var output = core.Tick(move, Frame);
                elapsed += Frame;
                Assert.That(core.Reaction, Is.EqualTo(ReactionState.HitStun), $"{elapsed:0.000}s 硬直还没结束。");
                Assert.That(output.IsInputLocked, Is.True, "硬直段必须锁输入。");
                Assert.That(output.ForwardSpeed, Is.EqualTo(0f), "硬直段输入不得生效。");
                Assert.That(output.AnimationSpeed, Is.EqualTo(1f), "硬直段受击动画按原速播放。");
            }
        }

        [Test]
        public void AfterTheHardStunTheRestPlaysFasterAndUnlocked()
        {
            var core = NewCore();
            core.ApplyDamage(50f);
            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, core.Tuning.Reaction.HitStunLockSeconds + 0.05f);

            var output = core.Tick(PlayerInputFrame.Idle, Frame);

            Assert.That(core.Reaction, Is.EqualTo(ReactionState.HitStun), "不操作时剩余受击动作继续播。");
            Assert.That(output.IsInputLocked, Is.False, "硬直之后输入必须是开着的，否则收不到打断它的操作。");
            Assert.That(output.AnimationSpeed, Is.EqualTo(1.6f));
            Assert.That(output.Animation, Is.EqualTo(PlayerAnimation.HitStun));
        }

        [Test]
        public void WithoutInputTheHitReactionEndsInAboutOneAndAHalfSeconds()
        {
            var core = NewCore();
            core.ApplyDamage(50f);
            var reaction = core.Tuning.Reaction;

            // 0.6 + (2.033 − 0.6) / 1.6 = 1.496 秒，原来整整 2.033 秒都锁着。
            Assert.That(reaction.HitStunRealSeconds, Is.EqualTo(1.496f).Within(0.001f));

            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, reaction.HitStunRealSeconds - 0.05f);
            Assert.That(core.Reaction, Is.EqualTo(ReactionState.HitStun));

            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, 0.1f);
            Assert.That(core.Reaction, Is.EqualTo(ReactionState.None));
            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Idle));
        }

        [Test]
        public void MovingAfterTheHardStunBreaksOutOnTheSameFrame()
        {
            var core = NewCore();
            core.ApplyDamage(50f);
            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, core.Tuning.Reaction.HitStunLockSeconds + 0.02f);

            var output = core.Tick(PlayerInputFrame.Idle.WithMove(0f, 1f), Frame);

            Assert.That(core.Reaction, Is.EqualTo(ReactionState.None));
            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Walk), "按下方向的那一帧就开始走，不吞一帧。");
            Assert.That(output.ForwardSpeed, Is.GreaterThan(0f));
        }

        [Test]
        public void AttackingAfterTheHardStunBreaksOutOnTheSameFrame()
        {
            var core = NewCore();
            core.ApplyDamage(50f);
            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, core.Tuning.Reaction.HitStunLockSeconds + 0.02f);

            core.Tick(PlayerInputFrame.Idle.WithAttack(true), Frame);
            core.Tick(PlayerInputFrame.Idle, Frame);

            Assert.That(core.Reaction, Is.EqualTo(ReactionState.None));
            Assert.That(core.Action, Is.EqualTo(ActionState.AttackCombo1));
        }

        [TestCase("F")]
        [TestCase("V")]
        [TestCase("Dash")]
        public void SkillsAndDashAlsoBreakOutAfterTheHardStun(string which)
        {
            var core = NewCore();
            core.ApplyDamage(50f);
            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, core.Tuning.Reaction.HitStunLockSeconds + 0.02f);

            switch (which)
            {
                case "F":
                    core.Tick(PlayerInputFrame.Idle.WithSkillF(true), Frame);
                    Assert.That(core.Action, Is.EqualTo(ActionState.SkillF));
                    break;
                case "V":
                    core.Tick(PlayerInputFrame.Idle.WithSkillV(true), Frame);
                    Assert.That(core.Action, Is.EqualTo(ActionState.SkillV));
                    break;
                default:
                    core.Tick(PlayerInputFrame.Idle.WithSprint(true).WithMove(0f, 1f), Frame);
                    core.Tick(PlayerInputFrame.Idle.WithMove(0f, 1f), Frame);
                    Assert.That(core.Action, Is.EqualTo(ActionState.Dash));
                    break;
            }

            Assert.That(core.Reaction, Is.EqualTo(ReactionState.None));
        }

        [Test]
        public void TurningTheCameraDoesNotCountAsBreakingOut()
        {
            var core = NewCore();
            core.ApplyDamage(50f);
            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, core.Tuning.Reaction.HitStunLockSeconds + 0.02f);

            core.Tick(PlayerInputFrame.Idle.WithCameraMoved(true).WithCameraYaw(90f), Frame);

            Assert.That(core.Reaction, Is.EqualTo(ReactionState.HitStun));
        }

        [Test]
        public void BeingHitAgainAfterTheHardStunLocksAgain()
        {
            var core = NewCore();
            core.ApplyDamage(50f);
            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, core.Tuning.Reaction.HitStunLockSeconds + 0.1f);

            core.ApplyDamage(50f);
            var output = core.Tick(PlayerInputFrame.Idle.WithMove(0f, 1f), Frame);

            Assert.That(core.Reaction, Is.EqualTo(ReactionState.HitStun));
            Assert.That(output.IsInputLocked, Is.True);
            Assert.That(output.Animation, Is.EqualTo(PlayerAnimation.HitStun));
            Assert.That(output.ForwardSpeed, Is.EqualTo(0f));
        }

        [Test]
        public void MoveInputHeldThroughTheHardStunTakesOverTheMomentItEnds()
        {
            var core = NewCore();
            core.ApplyDamage(50f);
            var move = PlayerInputFrame.Idle.WithMove(0f, 1f);
            var lockSeconds = core.Tuning.Reaction.HitStunLockSeconds;

            PlayerLocomotionTests.Hold(core, move, lockSeconds + (2f * Frame));

            Assert.That(core.Reaction, Is.EqualTo(ReactionState.None));
            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Walk));
        }

        // ------------------------------------------------------------------ F

        [Test]
        public void SkillFPlaysAtThreeQuartersOfItsFormerSpeed()
        {
            var f = PlayerTuning.CreateBaseline().SkillF.Action;

            Assert.That(f.Playback.MainSpeed, Is.EqualTo(1.125f));
            Assert.That(f.Playback.RecoverySpeed, Is.EqualTo(1.8f));
            Assert.That(f.RealDurationSeconds, Is.EqualTo(3.169f).Within(0.002f), "原来 2.377 秒。");
            Assert.That(f.HitWindowStart / f.Playback.MainSpeed, Is.EqualTo(0.954f).Within(0.002f),
                "命中起点按真实时间从 0.715 秒推迟到 0.954 秒。");
            Assert.That(f.ClipSeconds, Is.EqualTo(4.6f), "片段与窗口（片段时间）不变。");
            Assert.That(f.HitWindowStart, Is.EqualTo(1.073f));
            Assert.That(f.HitWindowEnd, Is.EqualTo(1.840f));
        }

        [Test]
        public void SkillFReallyTakesTheLongerTime()
        {
            var core = NewCore();
            core.Tick(PlayerInputFrame.Idle.WithSkillF(true), Frame);

            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, 3.1f);
            Assert.That(core.Action, Is.EqualTo(ActionState.SkillF), "3.1 秒时 F 还没放完。");

            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, 0.2f);
            Assert.That(core.Action, Is.EqualTo(ActionState.None));
        }

        // ------------------------------------------------------------------ 走跑

        [Test]
        public void WalkAndRunAreScaledTogether()
        {
            var locomotion = PlayerTuning.CreateBaseline().Locomotion;

            Assert.That(locomotion.SpeedScale, Is.EqualTo(1.3f));
            Assert.That(locomotion.AnimationScale, Is.EqualTo(1.15f), "动画倍率与移动倍率分开。");
            Assert.That(locomotion.WalkSpeed, Is.EqualTo(6.5f).Within(0.001f));
            Assert.That(locomotion.RunSpeed, Is.EqualTo(9.75f).Within(0.001f));
            Assert.That(locomotion.RunSpeed / locomotion.WalkSpeed, Is.EqualTo(7.5f / 5f).Within(0.001f),
                "等比例：走跑速度之比不变。");
        }

        [Test]
        public void WalkingMovesAtTheScaledSpeedAndPlaysTheAnimationFaster()
        {
            var core = NewCore();
            var input = PlayerInputFrame.Idle.WithMove(0f, 1f).WithCameraYaw(0f).WithFacingYaw(0f);
            PlayerLocomotionTests.Hold(core, input, 0.5f);

            var output = core.Tick(input, Frame);

            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Walk));
            Assert.That(output.ForwardSpeed, Is.EqualTo(6.5f).Within(0.01f));
            Assert.That(output.AnimationSpeed, Is.EqualTo(1.15f), "动画只加速到 1.15 倍，不跟着移动倍率加到 1.3。");
        }

        [Test]
        public void RunningMovesAtTheScaledSpeedAndPlaysTheAnimationFaster()
        {
            var core = NewCore();
            var input = PlayerInputFrame.Idle.WithSprint(true).WithMove(0f, 1f).WithCameraYaw(0f).WithFacingYaw(0f);
            PlayerLocomotionTests.Hold(core, input, 0.5f);

            var output = core.Tick(input, Frame);

            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Run));
            Assert.That(output.ForwardSpeed, Is.EqualTo(9.75f).Within(0.01f));
            Assert.That(output.AnimationSpeed, Is.EqualTo(1.15f));
        }

        [Test]
        public void IdleAndStoppingKeepTheirOwnAnimationSpeed()
        {
            var core = NewCore();
            Assert.That(core.Tick(PlayerInputFrame.Idle, Frame).AnimationSpeed, Is.EqualTo(1f));

            var input = PlayerInputFrame.Idle.WithMove(0f, 1f).WithCameraYaw(0f).WithFacingYaw(0f);
            PlayerLocomotionTests.Hold(core, input, 0.5f);
            var stop = core.Tick(PlayerInputFrame.Idle, Frame);

            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.StopWalk));
            Assert.That(stop.AnimationSpeed, Is.EqualTo(1f), "停止动作按自己烘焙的距离收住，不跟着加速。");
        }
    }
}
