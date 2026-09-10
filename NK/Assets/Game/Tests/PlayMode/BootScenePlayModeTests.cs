using System.Collections;
using Naraka.Features.Account.View;
using Naraka.Features.Bootstrap.View;
using Naraka.Features.Lobby.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Naraka.P0.PlayMode.Tests
{
    public sealed class BootScenePlayModeTests
    {
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
            Assert.That(root.Q<VisualElement>("LoadingScreen"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("FeatureScreen"), Is.Not.Null);

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
            Assert.That(root.Q<VisualElement>("LoadingBarFill"), Is.Not.Null);
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

        private static IEnumerator LoadBootScene()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return null;
        }
    }
}
