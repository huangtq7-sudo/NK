using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using NUnit.Framework;

namespace Naraka.Unity.EditMode.Tests
{
    /// <summary>
    /// 卡顿帧里 Animator 与业务状态机推进同样多的片段时间。
    /// 否则进地图头几帧一卡，出场动作就被 Animator 一口气播完（"角色突然出现"）。
    /// </summary>
    public sealed class PlayerAnimatorStepTests
    {
        [TestCase(1f / 144f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 30f)]
        [TestCase(0.1f)]
        public void OrdinaryFramesPlayAtFullSpeed(float frame)
        {
            Assert.That(PlayerAnimatorProjector.BusinessStepRatio(frame), Is.EqualTo(1f));
        }

        [TestCase(0.2f)]
        [TestCase(0.25f)]
        [TestCase(1f / 3f)]
        public void AStalledFrameOnlyAdvancesAsFarAsTheStateMachine(float frame)
        {
            var advanced = frame * PlayerAnimatorProjector.BusinessStepRatio(frame);

            Assert.That(advanced, Is.EqualTo(PlayerCore.MaxDeltaSeconds).Within(1e-5f));
        }

        [Test]
        public void APausedFrameDoesNotDivideByZero()
        {
            Assert.That(PlayerAnimatorProjector.BusinessStepRatio(0f), Is.EqualTo(1f));
        }
    }
}
