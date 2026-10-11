using System.Linq;
using System.Reflection;
using Naraka.EditorTools;
using NUnit.Framework;

namespace Naraka.Unity.EditMode.Tests
{
    /// <summary>
    /// 装配工具不得把大厅背景视频接回去。
    ///
    /// 只在 Inspector 里清空 <c>backgroundClip</c> 不够：<c>NARAKA/Setup/Apply P0 Project Settings</c>
    /// 每次运行都会重新指派视频，还会顺手改视频的导入设置并重导入。
    /// 这里不运行装配工具本身 —— 它会改写 Bootstrap 场景 —— 而是检查它还有没有那条路径。
    /// </summary>
    public sealed class LobbyBackgroundSetupTests
    {
        private const BindingFlags AllStatic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void ProjectSetupDoesNotConfigureALobbyVideo()
        {
            var videoProducers = typeof(P0ProjectSetup)
                .GetMethods(AllStatic)
                .Where(method => method.ReturnType.FullName == "UnityEngine.Video.VideoClip")
                .Select(method => method.Name)
                .ToArray();

            Assert.That(
                videoProducers, Is.Empty,
                "P0ProjectSetup 不得再产出大厅背景视频片段。");
        }

        [Test]
        public void ProjectSetupHoldsNoVideoAssetPath()
        {
            var videoPaths = typeof(P0ProjectSetup)
                .GetFields(AllStatic)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue())
                .Where(value => value.EndsWith(".mp4", System.StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.That(
                videoPaths, Is.Empty,
                "P0ProjectSetup 不得再引用视频素材路径。视频文件本身保留在工程里，只是不再接线。");
        }
    }
}
