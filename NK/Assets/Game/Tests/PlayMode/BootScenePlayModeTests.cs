using System.Collections;
using System.Linq;
using System.Reflection;
using Naraka.Boot;
using Naraka.Core.Application.Scenes;
using Naraka.Features.Expedition.Model;
using Naraka.Features.Account.View;
using Naraka.Features.Bootstrap.View;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.View;
using Naraka.Features.World.Controller;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.Video;
using VContainer;

namespace Naraka.P0.PlayMode.Tests
{
    public sealed class BootScenePlayModeTests
    {
        /// <summary>
        /// 持久化 App Root 会活过场景切换，也会活过测试。它的输入提供者启用了
        /// 共享的 InputActionAsset，留给下一个测试夹具就会让虚拟设备读不到任何输入。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            foreach (var root in Object.FindObjectsOfType<Naraka.Boot.AppRootLifetimeScope>())
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator BootSceneBuildsAccountAndLobbyViews()
        {
            yield return LoadBootScene();

            var accountView = Object.FindObjectOfType<AccountView>();
            var configVersionView = Object.FindObjectOfType<ConfigVersionView>();
            var lobbyView = Object.FindObjectOfType<LobbyView>();
            Assert.That(accountView, Is.Not.Null);
            Assert.That(configVersionView, Is.Not.Null);
            Assert.That(lobbyView, Is.Not.Null);

            var root = accountView.GetComponent<UIDocument>().rootVisualElement;
            Assert.That(root.Q<VisualElement>("AccountPanel"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("ConfigVersionOverlay"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("LobbyScreen"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("AppearanceScreen"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("FeatureScreen"), Is.Not.Null);

            // 加载界面已迁移到持久化 App Root 的独立 UIDocument：
            // 它必须活过场景切换，因此不能再挂在随 Bootstrap 场景销毁的 P0ClientShell 上。
            var loadingView = Object.FindObjectOfType<Naraka.Features.Loading.View.LoadingView>();
            Assert.That(loadingView, Is.Not.Null);
            var loadingRoot = loadingView.GetComponent<UIDocument>().rootVisualElement;
            Assert.That(loadingRoot.Q<VisualElement>("LoadingScreen"), Is.Not.Null);
            Assert.That(loadingRoot.Q<VisualElement>("LoadingBarFill"), Is.Not.Null);

            // P1.1-A：等级与三种货币 Label 必须存在，且在服务端数据到达前保持占位。
            Assert.That(root.Q<Label>("PlayerLevelLabel"), Is.Not.Null);
            Assert.That(root.Q<Label>("CopperCurrencyAmount"), Is.Not.Null);
            Assert.That(root.Q<Label>("SilkCurrencyAmount"), Is.Not.Null);
            Assert.That(root.Q<Label>("GoldCurrencyAmount"), Is.Not.Null);

            // 用户已完成的大厅入口不得因 P1 接线而消失。
            Assert.That(root.Q<Button>("PlayerAvatarButton"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("PlayerAvatarFrame"), Is.Not.Null);
            foreach (var entry in new[]
                     {
                         "HeroButton", "WeaponButton", "ForgeButton", "ShopButton", "InventoryButton",
                         "CheckInButton", "DrawButton", "AccountLevelRewardButton", "FriendsButton", "ChatButton"
                     })
            {
                Assert.That(root.Q<Button>(entry), Is.Not.Null, entry + " 缺失。");
            }
            Assert.That(root.Q<Button>("PlayerAvatarButton"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator LoginScreenExposesStableElementNames()
        {
            yield return LoadBootScene();

            var root = Object.FindObjectOfType<AccountView>().GetComponent<UIDocument>().rootVisualElement;
            var password = root.Q<TextField>("PasswordField");
            Assert.That(root.Q<VisualElement>("AccountScreen"), Is.Not.Null);
            Assert.That(root.Q<TextField>("UsernameField"), Is.Not.Null);
            Assert.That(password, Is.Not.Null);
            Assert.That(root.Q<Button>("RegisterButton"), Is.Not.Null);
            Assert.That(root.Q<Button>("LoginButton"), Is.Not.Null);
            Assert.That(root.Q<Label>("AccountStatusLabel"), Is.Not.Null);
            Assert.That(password.isPasswordField, Is.True, "密码输入框必须默认遮挡。");
        }

        [UnityTest]
        public IEnumerator ConfigVersionOverlayStaysAboveLoginScreen()
        {
            yield return LoadBootScene();

            var root = Object.FindObjectOfType<AccountView>().GetComponent<UIDocument>().rootVisualElement;
            var screen = root.Q<VisualElement>("AccountScreen");
            var overlay = root.Q<VisualElement>("ConfigVersionOverlay");
            Assert.That(screen, Is.Not.Null);
            Assert.That(overlay, Is.Not.Null);
            Assert.That(overlay.parent, Is.SameAs(screen.parent));
            Assert.That(
                overlay.parent.IndexOf(overlay),
                Is.GreaterThan(overlay.parent.IndexOf(screen)),
                "版本预检覆盖层必须位于登录界面之上。");
        }

        [UnityTest]
        public IEnumerator StartGameButtonLoadsTheFormalTaskSceneThroughTheRealLobbyPath()
        {
            yield return LoadBootScene();

            var appRoot = AppRootLifetimeScope.Instance;
            Assert.That(appRoot, Is.Not.Null, "Bootstrap 场景缺少持久化 App Root。");
            var expeditionModel = appRoot.Container.Resolve<ExpeditionModel>();
            Assert.That(
                ExpeditionSnapshot.TryCreate(
                    "playmode-expedition", "map-01", 1700000000000,
                    ExpeditionState.Active, System.Array.Empty<ExpeditionAsset>(), 0,
                    out var snapshot),
                Is.True);
            expeditionModel.ApplyActive(snapshot);

            var gameScope = Object.FindObjectOfType<GameLifetimeScope>();
            Assert.That(gameScope, Is.Not.Null, "Bootstrap 场景缺少 GameLifetimeScope。");
            var lobby = gameScope.Container.Resolve<LobbyController>();
            lobby.Enter("playmode-player", 42);
            yield return null;

            var lobbyView = Object.FindObjectOfType<LobbyView>();
            Assert.That(lobbyView, Is.Not.Null);
            var startButton = lobbyView.GetComponent<UIDocument>()
                .rootVisualElement.Q<Button>("StartGameButton");
            Assert.That(startButton, Is.Not.Null);
            Assert.That(startButton.enabledSelf, Is.True);

            InvokeButton(startButton);

            var deadline = Time.realtimeSinceStartup + 30f;
            while (SceneManager.GetActiveScene().name != WorldSceneNames.Map01Task &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(WorldSceneNames.Map01Task),
                "大厅开始游戏按钮必须经过远征控制器与加载流程进入正式任务场景。");
        }

        [UnityTest]
        public IEnumerator LobbyShowsTheStaticBackgroundAcrossReentry()
        {
            // 两轮：首次进入，以及从地图返回时 Bootstrap 场景被整体重载后的再次进入。
            //
            // 在 CI 上这条测试即使不修也会通过：背景视频被 .gitignore 排除，引用本来就是丢失的。
            // 它守的是素材齐全的机器上的真实表现；在任何环境都能暴露问题的是
            // LobbyStaticBackgroundTests.LobbyViewHasNoVideoBackgroundPath。
            for (var pass = 0; pass < 2; pass++)
            {
                yield return LoadBootScene();

                var gameScope = Object.FindObjectOfType<GameLifetimeScope>();
                Assert.That(gameScope, Is.Not.Null, $"第 {pass + 1} 次进入：Bootstrap 场景缺少 GameLifetimeScope。");
                gameScope.Container.Resolve<LobbyController>().Enter("playmode-player", 42);

                // 旧实现要等视频 Prepare 完成才覆盖背景，留足帧数让它有机会发生。
                for (var frame = 0; frame < 30; frame++)
                {
                    yield return null;
                }

                var lobbyView = Object.FindObjectOfType<LobbyView>();
                Assert.That(lobbyView, Is.Not.Null);
                var background = lobbyView.GetComponent<UIDocument>()
                    .rootVisualElement.Q<VisualElement>("LobbyBackground");
                Assert.That(background, Is.Not.Null, "LobbyMain.uxml 缺少 LobbyBackground。");

                Assert.That(
                    Object.FindObjectsOfType<VideoPlayer>(true), Is.Empty,
                    $"第 {pass + 1} 次进入大厅时不应创建任何视频播放器。");
                Assert.That(
                    Resources.FindObjectsOfTypeAll<RenderTexture>()
                        .Where(texture => texture.name == "LobbyBackgroundVideo"),
                    Is.Empty,
                    $"第 {pass + 1} 次进入大厅后不应残留视频用的 RenderTexture。");

                // 读最终生效的样式（USS、UXML 与内联合并后的结果），不读 style：
                // style 只反映 C# 代码写入的内联值，UXML 里声明的背景不会出现在那里。
                var image = background.resolvedStyle.backgroundImage;
                Assert.That(
                    image.renderTexture, Is.Null,
                    "大厅背景被运行期的 RenderTexture 覆盖了，静态图不会显示。");
                Assert.That(image.texture, Is.Not.Null, "大厅背景没有贴图。");
                Assert.That(image.texture.name, Is.EqualTo("Lobby_Animate"));
            }
        }

        private static void InvokeButton(Button button)
        {
            // Button.clicked 存在于 Clickable 操纵器中；通过它的实际 Invoke 路径触发，
            // 可以覆盖 LobbyView 对 StartGameButton 的真实绑定，又不依赖桌面鼠标位置。
            var invoke = typeof(Clickable).GetMethod(
                "Invoke", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(invoke, Is.Not.Null, "当前 UI Toolkit 找不到 Clickable.Invoke。");
            invoke.Invoke(button.clickable, new object[] { null });
        }

        private static IEnumerator LoadBootScene()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return null;
        }
    }
}
