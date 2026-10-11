using System.Linq;
using System.Reflection;
using Naraka.Features.Lobby.View;
using NUnit.Framework;

namespace Naraka.P0.Tests
{
    /// <summary>
    /// 大厅只显示静态背景 <c>Lobby_Animate.png</c>。
    ///
    /// 这条测试检查的是<b>类型</b>，不是运行结果，原因值得记下来：
    /// 大厅背景视频（<c>*.mp4</c>）被 .gitignore 排除，干净检出与 CI 里那个引用本来就是丢失的，
    /// 视频路径因 <c>backgroundClip == null</c> 直接跳过 —— 任何"进大厅看背景"的运行期测试
    /// 在 CI 上都会通过，只有素材齐全的那台机器能看到视频盖住静态图。
    /// 只有检查 View 自身还会不会去建视频，才能在每个环境里都暴露这个问题。
    /// </summary>
    public sealed class LobbyStaticBackgroundTests
    {
        private static readonly string[] VideoPathTypes =
        {
            "UnityEngine.Video.VideoClip",
            "UnityEngine.Video.VideoPlayer",
            "UnityEngine.RenderTexture"
        };

        [Test]
        public void LobbyViewHasNoVideoBackgroundPath()
        {
            var offending = typeof(LobbyView)
                .GetFields(BindingFlags.Instance | BindingFlags.Static |
                           BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => VideoPathTypes.Contains(field.FieldType.FullName))
                .Select(field => $"{field.Name} : {field.FieldType.Name}")
                .ToArray();

            Assert.That(
                offending, Is.Empty,
                "LobbyView 不得再持有视频片段、视频播放器或视频用的 RenderTexture —— " +
                "大厅背景由 LobbyMain.uxml / LobbyMain.uss 里的 Lobby_Animate.png 决定，" +
                "运行期任何对 LobbyBackground 的覆盖都会盖住它。");
        }
    }
}
