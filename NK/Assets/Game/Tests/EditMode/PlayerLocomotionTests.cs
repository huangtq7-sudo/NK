using System.Collections.Generic;
using Naraka.Features.Character.Model;
using NUnit.Framework;

namespace Naraka.P2.Tests
{
    /// <summary>
    /// Shift 点按/长按边界、Idle 5 秒待机动作与摄像机不重置计时。
    /// </summary>
    public sealed class PlayerLocomotionTests
    {
        private const float Frame = 1f / 60f;

        private static PlayerCore NewCore() => new PlayerCore(PlayerTuning.CreateBaseline());

        [Test]
        public void ShortShiftPressTriggersDashNotRun()
        {
            var core = NewCore();

            // 按住 0.10 秒（阈值 0.18）后释放。
            Hold(core, PlayerInputFrame.Idle.WithSprint(true).WithMove(0f, 1f), 0.10f);
            core.Tick(PlayerInputFrame.Idle.WithMove(0f, 1f), Frame);

            Assert.That(core.Action, Is.EqualTo(ActionState.Dash));
        }

        [Test]
        public void LongShiftHoldWithMovementEntersRun()
        {
            var core = NewCore();

            Hold(core, PlayerInputFrame.Idle.WithSprint(true).WithMove(0f, 1f), 0.30f);

            Assert.That(core.Action, Is.EqualTo(ActionState.None), "长按不应该触发冲刺。");
            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Run));
        }

        [Test]
        public void LongShiftHoldWithoutMovementDoesNotRunInPlace()
        {
            var core = NewCore();

            Hold(core, PlayerInputFrame.Idle.WithSprint(true), 0.50f);

            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Idle),
                "长按 Shift 但没有移动输入时不能原地播放跑步。");
        }

        [Test]
        public void HoldingShiftThroughTheDashDoesNotAutoSwitchToRun()
        {
            var core = NewCore();

            // 点按触发冲刺，然后立刻重新按住 Shift 并持续按到冲刺结束之前。
            Hold(core, PlayerInputFrame.Idle.WithSprint(true).WithMove(0f, 1f), 0.10f);
            core.Tick(PlayerInputFrame.Idle.WithMove(0f, 1f), Frame);
            Assert.That(core.Action, Is.EqualTo(ActionState.Dash));

            var held = PlayerInputFrame.Idle.WithSprint(true).WithMove(0f, 1f);
            Hold(core, held, 0.30f);

            Assert.That(core.Action, Is.EqualTo(ActionState.Dash),
                "冲刺期间不能因为继续按住 Shift 就切换到 Run。");
        }

        [Test]
        public void ReleasingShiftWhileMovingReturnsToWalkWithoutAStopAnimation()
        {
            var core = NewCore();
            Hold(core, PlayerInputFrame.Idle.WithSprint(true).WithMove(0f, 1f), 0.30f);
            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Run));

            core.Tick(PlayerInputFrame.Idle.WithMove(0f, 1f), Frame);

            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Walk));
        }

        [Test]
        public void ReleasingAllMovementFromRunPlaysStopRun()
        {
            var core = NewCore();
            Hold(core, PlayerInputFrame.Idle.WithSprint(true).WithMove(0f, 1f), 0.30f);

            core.Tick(PlayerInputFrame.Idle, Frame);

            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.StopRun));
        }

        [Test]
        public void ReleasingAllMovementFromWalkPlaysStopWalk()
        {
            var core = NewCore();
            Hold(core, PlayerInputFrame.Idle.WithMove(0f, 1f), 0.30f);
            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Walk));

            core.Tick(PlayerInputFrame.Idle, Frame);

            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.StopWalk));
        }

        [Test]
        public void RunTurnbackTriggersOnAFastDirectionReversal()
        {
            var core = NewCore();
            Hold(core, PlayerInputFrame.Idle.WithSprint(true).WithMove(0f, 1f), 0.40f);
            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Run));

            // 同一帧直接翻 180 度：这是"奔跑时突然反向"。
            core.Tick(PlayerInputFrame.Idle.WithSprint(true).WithMove(0f, -1f), Frame);

            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.RunTurnback));
        }

        [Test]
        public void SmallDirectionChangeDoesNotTriggerRunTurnback()
        {
            var core = NewCore();
            Hold(core, PlayerInputFrame.Idle.WithSprint(true).WithMove(0f, 1f), 0.40f);

            // 从 W 改成 W+D 只有 45 度，远小于 135 度阈值。
            core.Tick(PlayerInputFrame.Idle.WithSprint(true).WithMove(1f, 1f), Frame);

            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Run),
                "小角度转向只是普通转身，不该播反向动作。");
        }

        [Test]
        public void RunTurnbackEndsInTheNewRunDirection()
        {
            var core = NewCore();
            Hold(core, PlayerInputFrame.Idle.WithSprint(true).WithMove(0f, 1f), 0.40f);
            var reversed = PlayerInputFrame.Idle.WithSprint(true).WithMove(0f, -1f);
            core.Tick(reversed, Frame);
            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.RunTurnback));

            Hold(core, reversed, core.Tuning.Locomotion.RunTurnbackSeconds + 0.1f);

            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Run),
                "反向动作结束后回到奔跑。");
        }

        [Test]
        public void SlowDirectionChangeDoesNotTriggerRunTurnback()
        {
            var core = NewCore();
            Hold(core, PlayerInputFrame.Idle.WithSprint(true).WithMove(0f, 1f), 0.40f);

            // 中间松开足够久（超过允许时间窗），这属于重新起步而不是突然反向。
            Hold(core, PlayerInputFrame.Idle.WithSprint(true),
                core.Tuning.Locomotion.RunTurnbackInputWindowSeconds + 0.3f);
            core.Tick(PlayerInputFrame.Idle.WithSprint(true).WithMove(0f, -1f), Frame);

            Assert.That(core.Locomotion, Is.Not.EqualTo(LocomotionState.RunTurnback));
        }

        [Test]
        public void IdleVariationPlaysAfterTheConfiguredDelay()
        {
            var core = NewCore();
            var delay = core.Tuning.Idle.VariationDelaySeconds;

            // 2026-10-04：从 5 秒改为 2.4 秒。新 idle 片段（2.667 秒）烘焙的头发模拟
            // 不闭环，播满一轮就在循环处硬跳 41.9°；把延迟压到片段长度以内，
            // Idle 永远播不到那个边界。约束与余量见 PlayerTuning.CreateBaseline 的注释，
            // 以及 PlayerAnimationContractTests.IdleVariationStartsBeforeTheIdleClipWouldLoop。
            Assert.That(delay, Is.EqualTo(2.4f));

            Hold(core, PlayerInputFrame.Idle, delay - 0.1f);
            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Idle));

            Hold(core, PlayerInputFrame.Idle, 0.2f);

            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.IdleVariation));
        }

        [Test]
        public void IdleVariationReturnsToIdleAndCanPlayAgain()
        {
            var core = NewCore();
            var delay = core.Tuning.Idle.VariationDelaySeconds;

            Hold(core, PlayerInputFrame.Idle, delay + 0.1f);
            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.IdleVariation));

            Hold(core, PlayerInputFrame.Idle, core.Tuning.Idle.VariationDurationSeconds + 0.1f);
            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Idle));

            Hold(core, PlayerInputFrame.Idle, delay + 0.1f);
            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.IdleVariation),
                "回到 Idle 后必须重新计时，之后仍可再次播放。");
        }

        [Test]
        public void CameraMovementDoesNotResetTheIdleTimer()
        {
            var core = NewCore();

            // 全程只转摄像机，不做任何角色操作。
            Hold(core, PlayerInputFrame.Idle.WithCameraMoved(true),
                core.Tuning.Idle.VariationDelaySeconds + 0.1f);

            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.IdleVariation),
                "单纯转动摄像机不算角色操作，不应该重置待机计时。");
        }

        [Test]
        public void MovementResetsTheIdleTimer()
        {
            var core = NewCore();
            var delay = core.Tuning.Idle.VariationDelaySeconds;

            // 两段都差一点点到阈值：计时如果没有重置，两段加起来就会触发。
            Hold(core, PlayerInputFrame.Idle, delay - 0.1f);

            // 动一下再站住：计时必须从头开始。
            Hold(core, PlayerInputFrame.Idle.WithMove(0f, 1f), 0.2f);
            Hold(core, PlayerInputFrame.Idle, delay - 0.1f);

            Assert.That(core.Locomotion, Is.Not.EqualTo(LocomotionState.IdleVariation));
        }

        [Test]
        public void AttackResetsTheIdleTimer()
        {
            var core = NewCore();
            var delay = core.Tuning.Idle.VariationDelaySeconds;

            Hold(core, PlayerInputFrame.Idle, delay - 0.1f);

            PlayerComboTests.ClickAttack(core);
            Hold(core, PlayerInputFrame.Idle, delay - 0.1f);

            Assert.That(core.Locomotion, Is.Not.EqualTo(LocomotionState.IdleVariation));
        }

        [TestCase(0f, 1f, 0f, TestName = "W 朝镜头正前方")]
        [TestCase(0f, -1f, 180f, TestName = "S 朝镜头正后方")]
        [TestCase(-1f, 0f, -90f, TestName = "A 朝镜头左侧")]
        [TestCase(1f, 0f, 90f, TestName = "D 朝镜头右侧")]
        [TestCase(1f, 1f, 45f, TestName = "W+D 朝镜头右前")]
        public void EveryDirectionMovesRelativeToTheCamera(float x, float y, float expectedYaw)
        {
            var core = NewCore();
            // 镜头朝 0 度，角色也朝 0 度。
            var input = PlayerInputFrame.Idle.WithMove(x, y).WithCameraYaw(0f).WithFacingYaw(0f);

            Assert.That(
                input.DesiredWorldYaw, Is.EqualTo(expectedYaw).Within(0.01f),
                "目标朝向必须由摄像机朝向加输入方向决定。");

            var output = core.Tick(input, Frame);

            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Walk),
                "WASD 四个方向都应该进入行走，而不是只有 W。");
            Assert.That(output.ForwardSpeed, Is.GreaterThan(0f), "四个方向都要真的移动。");
            Assert.That(output.Animation, Is.EqualTo(PlayerAnimation.Walk),
                "四个方向都使用同一个前进动画。");
        }

        [Test]
        public void DesiredDirectionRotatesWithTheCamera()
        {
            // 镜头转到 90 度后按 W，目标朝向也应该是 90 度。
            var input = PlayerInputFrame.Idle.WithMove(0f, 1f).WithCameraYaw(90f).WithFacingYaw(0f);

            Assert.That(input.DesiredWorldYaw, Is.EqualTo(90f).Within(0.01f));
        }

        [Test]
        public void CharacterTurnsTowardTheDesiredDirectionAtTheConfiguredRate()
        {
            var core = NewCore();
            var rate = core.Tuning.Locomotion.TurnDegreesPerSecond;

            // 角色朝 0 度，按 D（目标 90 度）。单帧最多转 rate * dt。
            var output = core.Tick(
                PlayerInputFrame.Idle.WithMove(1f, 0f).WithCameraYaw(0f).WithFacingYaw(0f), Frame);

            Assert.That(output.TurnDegrees, Is.GreaterThan(0f), "必须朝目标方向转。");
            Assert.That(
                output.TurnDegrees, Is.EqualTo(rate * Frame).Within(0.01f),
                "单帧转向量必须受配置的转向角速度限制。");
        }

        [Test]
        public void TurnStopsOnceFacingMatchesTheDesiredDirection()
        {
            var core = NewCore();

            // 已经正对目标方向：不该再有任何转向量。
            var output = core.Tick(
                PlayerInputFrame.Idle.WithMove(0f, 1f).WithCameraYaw(0f).WithFacingYaw(0f), Frame);

            Assert.That(output.TurnDegrees, Is.EqualTo(0f).Within(0.001f));
            Assert.That(output.ForwardSpeed, Is.GreaterThan(0f));
        }

        [Test]
        public void TurnTakesTheShortestPath()
        {
            var core = NewCore();

            // 角色朝 350 度，目标 10 度：应该往 +20 度方向转，而不是绕 340 度。
            var output = core.Tick(
                PlayerInputFrame.Idle.WithMove(0f, 1f).WithCameraYaw(10f).WithFacingYaw(350f), Frame);

            Assert.That(output.TurnDegrees, Is.GreaterThan(0f), "必须走最短路径。");
        }

        [Test]
        public void NoTurnWithoutMovementInput()
        {
            var core = NewCore();

            // 只转镜头不按方向键：角色不该被镜头拖着转。
            var output = core.Tick(
                PlayerInputFrame.Idle.WithCameraYaw(120f).WithFacingYaw(0f).WithCameraMoved(true),
                Frame);

            Assert.That(output.TurnDegrees, Is.EqualTo(0f), "鼠标转动镜头不得旋转角色。");
            Assert.That(output.ForwardSpeed, Is.EqualTo(0f));
            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Idle));
        }

        [Test]
        public void AllDirectionsShareTheSameSpeed()
        {
            var speeds = new List<float>();
            foreach (var direction in new[]
                     {
                         new[] { 0f, 1f }, new[] { 0f, -1f }, new[] { -1f, 0f }, new[] { 1f, 0f }
                     })
            {
                var core = NewCore();
                var input = PlayerInputFrame.Idle
                    .WithMove(direction[0], direction[1])
                    .WithCameraYaw(0f)
                    .WithFacingYaw(0f);
                Hold(core, input, 0.2f);
                speeds.Add(core.Tick(input, Frame).ForwardSpeed);
            }

            foreach (var speed in speeds)
            {
                Assert.That(speed, Is.EqualTo(5.0f).Within(0.001f),
                    "四个方向共用同一个行走速度，没有后退减速。");
            }
        }

        [Test]
        public void DiagonalInputIsNormalisedInsteadOfBeingFaster()
        {
            var diagonal = PlayerInputFrame.Idle.WithMove(1f, 1f);

            Assert.That(
                diagonal.MoveMagnitude, Is.EqualTo(1f).Within(0.001f),
                "斜向输入的模长必须钳制到 1，否则斜着走会比直走快。");
        }

        [Test]
        public void StopAnimationTravelsTheDistanceTheAnimationWasAuthoredFor()
        {
            var core = NewCore();
            var tuning = core.Tuning.Locomotion;

            // 走起来再松手，累加停止动作期间走出的距离。
            var input = PlayerInputFrame.Idle.WithMove(0f, 1f).WithCameraYaw(0f).WithFacingYaw(0f);
            Hold(core, input, 0.5f);
            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Walk));

            var travelled = 0f;
            var guard = 0;
            while (core.Locomotion != LocomotionState.Idle && guard++ < 10000)
            {
                travelled += core.Tick(PlayerInputFrame.Idle, Frame).ForwardSpeed * Frame;
            }

            // 停止动画本身只前移 StopWalkDistance；线性衰减会走出好几倍的距离。
            Assert.That(
                travelled, Is.EqualTo(tuning.StopWalkDistance).Within(tuning.StopWalkDistance * 0.15f),
                $"停止走路走出 {travelled:0.00}，动画只前移 {tuning.StopWalkDistance:0.00}，脚会打滑。");
        }

        [Test]
        public void StopSpeedStartsAtTheEntrySpeedWithoutADiscontinuity()
        {
            var core = NewCore();
            var input = PlayerInputFrame.Idle.WithMove(0f, 1f).WithCameraYaw(0f).WithFacingYaw(0f);
            Hold(core, input, 0.5f);
            var walkSpeed = core.Tick(input, Frame).ForwardSpeed;

            var firstStopSpeed = core.Tick(PlayerInputFrame.Idle, Frame).ForwardSpeed;

            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.StopWalk));
            Assert.That(
                firstStopSpeed, Is.EqualTo(walkSpeed).Within(walkSpeed * 0.25f),
                "松手第一帧的速度必须接近行走速度，不能突然掉档。");
        }

        [Test]
        public void IdleVariationDurationMatchesTheMeasuredClip()
        {
            // 待机动作实测 8.0 秒（2026-10-04 的新动画；旧动画是 15.067 秒）。
            // 配置写成估计值会让它只播一小段就被砍掉。
            Assert.That(
                PlayerTuning.CreateBaseline().Idle.VariationDurationSeconds,
                Is.EqualTo(8f).Within(0.001f));
        }

        internal static void Hold(PlayerCore core, PlayerInputFrame input, float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                core.Tick(input, Frame);
                elapsed += Frame;
            }
        }
    }
}
