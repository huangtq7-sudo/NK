using Naraka.EditorTools;
using Naraka.Features.Character.View;
using NUnit.Framework;
using UnityEditor;

namespace Naraka.Unity.EditMode.Tests
{
    /// <summary>
    /// 场景实际引用的是 <c>Settings/Player/PlayerTuning.asset</c>，不是代码里的基线。
    /// 这里读真实资产，确认它在<b>不改写资产文件</b>的前提下就让 V 成为"蓄势 + 0.2 秒可见冲刺"，
    /// 而 Move_F 仍是原来的连续冲刺。
    /// </summary>
    public sealed class PlayerTuningAssetDashTests
    {
        [Test]
        public void TheRealTuningAssetMakesSkillVAVisibleDashAndLeavesMoveFAlone()
        {
            var asset = AssetDatabase.LoadAssetAtPath<PlayerTuningAsset>(P2SceneSetup.PlayerTuningPath);
            Assert.That(asset, Is.Not.Null, $"找不到 {P2SceneSetup.PlayerTuningPath}。");

            var tuning = asset.ToTuning();
            var v = tuning.SkillV.Action;

            Assert.That(v.PathDash, Is.True, "V 必须沿校验过的路径冲刺。");
            Assert.That(v.ForwardDisplacement, Is.GreaterThan(0f));
            Assert.That(v.DisplacementStartClipSeconds, Is.EqualTo(0.333f).Within(0.0001f), "蓄势时机取自动画。");
            Assert.That(
                (v.DisplacementClipSeconds - v.DisplacementStartClipSeconds) / v.Playback.MainSpeed,
                Is.EqualTo(0.2f).Within(0.0001f), "冲刺时长 0.2 秒（真实时间）。");
            Assert.That(v.DisplacementClipSeconds, Is.LessThan(v.HitWindowStart), "冲完之后才开命中窗。");

            Assert.That(tuning.Dash.PathDash, Is.False, "Move_F 不得被连带改成路径冲刺。");
            Assert.That(tuning.Dash.ForwardDisplacement, Is.GreaterThan(0f));
        }
    }
}
