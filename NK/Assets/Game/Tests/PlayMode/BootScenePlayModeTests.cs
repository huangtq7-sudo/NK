using System.Collections;
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
