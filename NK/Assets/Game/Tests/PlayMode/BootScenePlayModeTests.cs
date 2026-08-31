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
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var accountView = Object.FindObjectOfType<AccountView>();
            var configVersionView = Object.FindObjectOfType<ConfigVersionView>();
            var lobbyView = Object.FindObjectOfType<LobbyView>();
            Assert.That(accountView, Is.Not.Null);
            Assert.That(configVersionView, Is.Not.Null);
            Assert.That(lobbyView, Is.Not.Null);

            var root = accountView.GetComponent<UIDocument>().rootVisualElement;
            Assert.That(root.Q<VisualElement>("AccountPanel"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("ConfigVersionOverlay"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("LobbyPanel"), Is.Not.Null);
        }
    }
}
