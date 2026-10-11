using Naraka.EditorTools;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using Naraka.Infrastructure.Camera;
using NUnit.Framework;
using UnityEditor;

namespace Naraka.Unity.EditMode.Tests
{
    /// <summary>
    /// 场景实际引用的是 <c>PlayerTuning.asset</c> 与 <c>ThirdPersonCameraSettings.asset</c>，不是代码基线。
    /// 2026-10-11 的手感调整必须在真实资产上生效，并与基线一致。
    /// </summary>
    public sealed class PlayerTuningAssetFeelTests
    {
        private const string CameraSettingsPath = "Assets/Game/Settings/Player/ThirdPersonCameraSettings.asset";

        private static PlayerTuning Real()
        {
            var asset = AssetDatabase.LoadAssetAtPath<PlayerTuningAsset>(P2SceneSetup.PlayerTuningPath);
            Assert.That(asset, Is.Not.Null, $"找不到 {P2SceneSetup.PlayerTuningPath}。");
            return asset.ToTuning();
        }

        [Test]
        public void TheRealAssetScalesWalkAndRunByOnePointThree()
        {
            var locomotion = Real().Locomotion;

            Assert.That(locomotion.SpeedScale, Is.EqualTo(1.3f).Within(0.0001f));
            Assert.That(locomotion.AnimationScale, Is.EqualTo(1.15f).Within(0.0001f));
            Assert.That(locomotion.WalkSpeed, Is.EqualTo(6.5f).Within(0.001f));
            Assert.That(locomotion.RunSpeed, Is.EqualTo(9.75f).Within(0.001f));
        }

        [Test]
        public void TheRealAssetGivesASixTenthsHardStunAndAFasterCancellableRest()
        {
            var reaction = Real().Reaction;

            Assert.That(reaction.HitStunLockSeconds, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(reaction.HitStunRecoverySpeed, Is.EqualTo(1.6f).Within(0.0001f));
            Assert.That(reaction.HitStunSeconds, Is.EqualTo(2.033f).Within(0.001f), "片段长度仍与动画一致。");
            Assert.That(reaction.HitStunRealSeconds, Is.EqualTo(1.496f).Within(0.002f));
        }

        [Test]
        public void TheRealAssetSlowsSkillFToThreeQuarters()
        {
            var real = Real().SkillF.Action;
            var baseline = PlayerTuning.CreateBaseline().SkillF.Action;

            Assert.That(real.Playback.MainSpeed, Is.EqualTo(1.125f).Within(0.0001f));
            Assert.That(real.Playback.RecoverySpeed, Is.EqualTo(1.8f).Within(0.0001f));
            Assert.That(real.RealDurationSeconds, Is.EqualTo(baseline.RealDurationSeconds).Within(0.001f));
            Assert.That(real.HitWindowStart, Is.EqualTo(baseline.HitWindowStart).Within(0.0001f));
        }

        [Test]
        public void TheRealCameraSettingsStartTheIntroAboveAndBehind()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ThirdPersonCameraSettings>(CameraSettingsPath);
            Assert.That(settings, Is.Not.Null, $"找不到 {CameraSettingsPath}。");

            Assert.That(settings.IntroPitch, Is.GreaterThan(settings.InitialPitch + 20f), "起点要明显更俯视。");
            Assert.That(settings.IntroDistance, Is.GreaterThan(settings.Distance), "起点要更远。");
            Assert.That(settings.IntroPitch, Is.LessThanOrEqualTo(settings.MaxPitch + 0.001f));

            // 2026-10-11 确认：白色、60% 起始不透明度、1.5 秒变透明。
            Assert.That(settings.IntroFadeColor.r, Is.EqualTo(1f));
            Assert.That(settings.IntroFadeColor.g, Is.EqualTo(1f));
            Assert.That(settings.IntroFadeColor.b, Is.EqualTo(1f));
            Assert.That(settings.IntroFadeStartAlpha, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(settings.IntroFadeSeconds, Is.EqualTo(1.5f).Within(0.0001f));
        }
    }
}
