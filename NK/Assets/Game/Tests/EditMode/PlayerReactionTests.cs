using Naraka.Features.Character.Model;
using NUnit.Framework;

namespace Naraka.P2.Tests
{
    /// <summary>
    /// F/V 冷却、受击硬直、霸体、死亡优先级与重生。
    /// </summary>
    public sealed class PlayerReactionTests
    {
        private const float Frame = 1f / 60f;

        private static PlayerCore NewCore() => new PlayerCore(PlayerTuning.CreateBaseline());

        [Test]
        public void SkillCooldownsMatchTheBaseline()
        {
            var tuning = PlayerTuning.CreateBaseline();

            Assert.That(tuning.SkillF.CooldownSeconds, Is.EqualTo(15f));
            Assert.That(tuning.SkillV.CooldownSeconds, Is.EqualTo(40f));
        }

        [Test]
        public void SkillFGoesOnCooldownAndIsRejectedUntilReady()
        {
            var core = NewCore();

            core.Tick(PlayerInputFrame.Idle.WithSkillF(true), Frame);
            Assert.That(core.Action, Is.EqualTo(ActionState.SkillF));
            Assert.That(core.SkillFCooldownRemaining, Is.EqualTo(15f).Within(0.05f));

            PlayerLocomotionTests.Hold(
                core, PlayerInputFrame.Idle, core.Tuning.SkillF.Action.RealDurationSeconds + 0.1f);
            Assert.That(core.Action, Is.EqualTo(ActionState.None));

            var output = core.Tick(PlayerInputFrame.Idle.WithSkillF(true), Frame);
            Assert.That(output.Rejection, Is.EqualTo(ActionRejection.SkillFOnCooldown));
            Assert.That(core.Action, Is.EqualTo(ActionState.None));
        }

        [Test]
        public void SkillVGoesOnCooldownAndBecomesReadyAgain()
        {
            var core = NewCore();
            core.Tick(PlayerInputFrame.Idle.WithSkillV(true), Frame);
            Assert.That(core.Action, Is.EqualTo(ActionState.SkillV));

            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, 41f);

            Assert.That(core.SkillVCooldownRemaining, Is.EqualTo(0f));
            core.Tick(PlayerInputFrame.Idle.WithSkillV(true), Frame);
            Assert.That(core.Action, Is.EqualTo(ActionState.SkillV));
        }

        [Test]
        public void SkillsBlockFreeMovementAndRotation()
        {
            var core = NewCore();
            core.Tick(PlayerInputFrame.Idle.WithSkillF(true), Frame);

            var output = core.Tick(PlayerInputFrame.Idle.WithMove(1f, 1f), Frame);

            Assert.That(output.TurnDegrees, Is.EqualTo(0f), "技能期间禁止自由旋转。");
            Assert.That(
                output.ForwardSpeed, Is.EqualTo(0f).Within(0.001f),
                "F 技能不是位移技能，坐标不变。");
        }

        [Test]
        public void StaminaDoesNotRegenerateWhileDashing()
        {
            var core = NewCore();
            PlayerStaminaTests.TapSprint(core);
            Assert.That(core.Action, Is.EqualTo(ActionState.Dash));
            var afterSpend = core.Stamina;

            // 冲刺动画实测 1.333 秒。途中恢复会让"两次冲刺耗尽体力"失效。
            PlayerLocomotionTests.Hold(
                core, PlayerInputFrame.Idle, core.Tuning.Dash.RealDurationSeconds * 0.9f);

            Assert.That(core.Stamina, Is.EqualTo(afterSpend).Within(0.001f),
                "冲刺进行中不得恢复体力，恢复窗口从动作结束才开始计时。");
        }

        [Test]
        public void SuperArmorBlocksHitStunButNotDamage()
        {
            var core = NewCore();
            core.Tick(PlayerInputFrame.Idle.WithSkillV(true), Frame);
            Assert.That((core.Flags & PlayerOverlayFlags.SuperArmor), Is.Not.EqualTo(PlayerOverlayFlags.None));

            var armorBefore = core.Armor;
            core.ApplyDamage(120f);

            Assert.That(core.Armor, Is.LessThan(armorBefore));
            Assert.That(core.Reaction, Is.EqualTo(ReactionState.None));
        }

        [Test]
        public void SuperArmorDoesNotPreventDeath()
        {
            var core = NewCore();
            core.Tick(PlayerInputFrame.Idle.WithSkillV(true), Frame);

            core.ApplyDamage(Lethal(core));

            Assert.That(core.Reaction, Is.EqualTo(ReactionState.Death));
        }

        [Test]
        public void TakingDamageWithoutSuperArmorEntersHitStun()
        {
            var core = NewCore();
            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle.WithMove(0f, 1f), 0.2f);

            core.ApplyDamage(50f);

            Assert.That(core.Reaction, Is.EqualTo(ReactionState.HitStun));
        }

        [Test]
        public void HitStunPausesStaminaRegenerationForAnExtraHalfSecond()
        {
            var core = NewCore();
            PlayerStaminaTests.DoDash(core);
            var afterDash = core.Stamina;

            core.ApplyDamage(10f);
            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, 0.75f);

            Assert.That(core.Stamina, Is.EqualTo(afterDash).Within(0.05f),
                "受击后恢复必须额外暂停 0.5 秒。");
        }

        [Test]
        public void HitStunCannotInterruptDeath()
        {
            var core = NewCore();
            core.ApplyDamage(Lethal(core));
            Assert.That(core.Reaction, Is.EqualTo(ReactionState.Death));

            core.ApplyDamage(50f);

            Assert.That(core.Reaction, Is.EqualTo(ReactionState.Death));
        }

        [Test]
        public void DeathCannotBeInterruptedByOrdinaryStates()
        {
            var core = NewCore();
            core.ApplyDamage(Lethal(core));

            // 死亡期间持续输入移动、攻击与技能，全部不得生效。
            PlayerLocomotionTests.Hold(
                core,
                PlayerInputFrame.Idle.WithMove(0f, 1f).WithSprint(true).WithSkillF(true),
                0.5f);

            Assert.That(core.Reaction, Is.EqualTo(ReactionState.Death));
            Assert.That(core.Action, Is.EqualTo(ActionState.None));
            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Idle));
        }

        [Test]
        public void DeathLocksInputAndOutputsZeroMotion()
        {
            var core = NewCore();
            core.ApplyDamage(Lethal(core));

            var output = core.Tick(PlayerInputFrame.Idle.WithMove(1f, 1f), Frame);

            Assert.That(output.IsInputLocked, Is.True);
            Assert.That(output.ForwardSpeed, Is.EqualTo(0f));
            Assert.That(output.TurnDegrees, Is.EqualTo(0f));
            Assert.That(output.Animation, Is.EqualTo(PlayerAnimation.Death));
        }

        [Test]
        public void DeathCompletionSignalsExactlyOnce()
        {
            var core = NewCore();
            core.ApplyDamage(Lethal(core));

            var signals = 0;
            var elapsed = 0f;
            while (elapsed < core.Tuning.Reaction.DeathSeconds + 1.0f)
            {
                core.Tick(PlayerInputFrame.Idle, Frame);
                if (core.ConsumeDeathCompleted())
                {
                    signals++;
                }

                elapsed += Frame;
            }

            Assert.That(signals, Is.EqualTo(1), "死亡返回只能被触发一次。");
        }

        [Test]
        public void RespawnRestoresTheConfirmedBaseline()
        {
            var core = NewCore();
            core.Tick(PlayerInputFrame.Idle.WithSkillF(true), Frame);
            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, 2.0f);
            core.ApplyDamage(Lethal(core));
            Assert.That(core.Reaction, Is.EqualTo(ReactionState.Death));

            core.Respawn();

            Assert.That(core.Health, Is.EqualTo(core.MaxHealth), "生命恢复 100%。");
            Assert.That(core.Armor, Is.EqualTo(core.MaxArmor * 0.5f), "护甲恢复 50%。");
            Assert.That(core.Stamina, Is.EqualTo(20f), "体力恢复至最大值 20。");
            Assert.That(core.SkillFCooldownRemaining, Is.EqualTo(0f), "F 冷却清零。");
            Assert.That(core.SkillVCooldownRemaining, Is.EqualTo(0f), "V 冷却清零。");
            Assert.That(core.Reaction, Is.EqualTo(ReactionState.None));
            Assert.That(
                (core.Flags & PlayerOverlayFlags.SpawnProtection),
                Is.Not.EqualTo(PlayerOverlayFlags.None),
                "重生获得 3 秒保护。");
        }

        [Test]
        public void SpawnProtectionExpiresAfterThreeSeconds()
        {
            var core = NewCore();
            core.Respawn();
            Assert.That(core.Tuning.Reaction.SpawnProtectionSeconds, Is.EqualTo(3f));

            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, 3.1f);

            Assert.That((core.Flags & PlayerOverlayFlags.SpawnProtection), Is.EqualTo(PlayerOverlayFlags.None));
        }

        [Test]
        public void SpawnAnimationLocksInputUntilItFinishes()
        {
            var core = NewCore();
            core.BeginSpawn(ActionState.SpawnLobbyToMap01);

            var output = core.Tick(PlayerInputFrame.Idle.WithMove(0f, 1f), Frame);
            Assert.That(output.IsInputLocked, Is.True);
            Assert.That(output.Animation, Is.EqualTo(PlayerAnimation.SpawnBurstLobbyToMap01));

            PlayerLocomotionTests.Hold(
                core, PlayerInputFrame.Idle, core.Tuning.Reaction.SpawnLobbyToMap01.RealDurationSeconds + 0.1f);

            Assert.That(core.Action, Is.EqualTo(ActionState.None));
            Assert.That(core.Locomotion, Is.EqualTo(LocomotionState.Idle));
            Assert.That(core.Tick(PlayerInputFrame.Idle, Frame).IsInputLocked, Is.False);
        }

        [Test]
        public void SpawnDurationsMatchTheMeasuredBurstClips()
        {
            // Burst02 实测 5.5 秒、Burst01 实测 7.233 秒。
            // 配置写成 2 秒会让出场动画播到 36% / 28% 就被切断。
            var reaction = PlayerTuning.CreateBaseline().Reaction;

            Assert.That(reaction.SpawnLobbyToMap01.ClipSeconds, Is.EqualTo(5.5f).Within(0.001f));
            Assert.That(reaction.SpawnMap01ToMap02.ClipSeconds, Is.EqualTo(7.233f).Within(0.001f));

            // 进入战斗场景的出场动画整体放快，所以真实时长明显短于片段长度。
            Assert.That(
                reaction.SpawnMap01ToMap02.RealDurationSeconds,
                Is.LessThan(reaction.SpawnMap01ToMap02.ClipSeconds * 0.75f),
                "Burst01 必须被加速播放。");
        }

        [Test]
        public void SpawnStateLastsTheWholeClipBeforeReturningToIdle()
        {
            var core = NewCore();
            var duration = core.Tuning.Reaction.SpawnLobbyToMap01.RealDurationSeconds;
            core.BeginSpawn(ActionState.SpawnLobbyToMap01);

            // 走到片段长度的 90%：出场动作必须还在播。
            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, duration * 0.9f);
            Assert.That(
                core.Action, Is.EqualTo(ActionState.SpawnLobbyToMap01),
                "出场动画必须完整播放，不能提前结束。");

            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, duration * 0.15f);
            Assert.That(core.Action, Is.EqualTo(ActionState.None));
        }

        [Test]
        public void LockedInputRejectsActionsAndStopsMotion()
        {
            var core = NewCore();
            core.SetInputLocked(true);

            var output = core.Tick(
                PlayerInputFrame.Idle.WithMove(0f, 1f).WithSkillF(true).WithSprint(true), Frame);

            Assert.That(output.Rejection, Is.EqualTo(ActionRejection.InputLocked));
            Assert.That(output.ForwardSpeed, Is.EqualTo(0f));
            Assert.That(core.Action, Is.EqualTo(ActionState.None));
            Assert.That(core.SkillFCooldownRemaining, Is.EqualTo(0f), "锁定期间不得偷偷消耗冷却。");
        }

        /// <summary>
        /// 一次必定致命的**原始**伤害。
        ///
        /// 原始伤害要先过防御再扣护甲，因此"刚好等于生命加护甲"是打不死的：
        /// 防御 80 时它只剩 55.6%。测试要表达的是"打死"，不是某个具体数字。
        /// </summary>
        private static float Lethal(PlayerCore core) =>
            (core.MaxHealth + core.MaxArmor) * (100f + core.Defense) / 100f;
    }
}
