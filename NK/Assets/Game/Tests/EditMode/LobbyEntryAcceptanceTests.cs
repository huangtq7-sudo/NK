using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using NUnit.Framework;

namespace Naraka.P0.Tests
{
    /// <summary>
    /// P1.9 大厅联合验收。
    ///
    /// 这些测试覆盖的是"整体"而不是某个模块：十个入口都能开、旧云端下一个未知协议都不发、
    /// 重新登录后每个模块都会重新向服务端取数据而不是留着上一个账号的残影。
    /// </summary>
    public sealed class LobbyEntryAcceptanceTests
    {
        /// <summary>大厅的全部功能入口。新增入口时这里会先失败，提醒补齐验收。</summary>
        private static readonly LobbyFeature[] AllFeatures =
        {
            LobbyFeature.Hero,
            LobbyFeature.Weapon,
            LobbyFeature.Forge,
            LobbyFeature.Shop,
            LobbyFeature.Inventory,
            LobbyFeature.CheckIn,
            LobbyFeature.Draw,
            LobbyFeature.AccountLevelReward,
            LobbyFeature.Friends,
            LobbyFeature.Chat
        };

        private static LobbyController Create(IServerCapabilities capabilities = null)
        {
            LobbyAccountSnapshot.TryCreate(3, 1000, 1000, 1000, out var summary);
            var lobby = new LobbyController(
                new LobbyModel(),
                new FakeLobbySceneGateway(),
                "Map1",
                new FakeLobbyAccountGateway { Result = LobbyAccountSummaryResult.Success(summary) },
                new FakeLobbyProfileGateway(),
                capabilities ?? LobbyTestCapabilities.Full());

            lobby.Enter("player-one", 42);
            return lobby;
        }

        [Test]
        public void TheLobbyExposesExactlyTenFeatureEntries()
        {
            Assert.That(Enum.GetValues(typeof(LobbyFeature)).Length, Is.EqualTo(AllFeatures.Length));
        }

        [Test]
        public void EveryEntryOpensItsOwnPanelWithoutAnyPlaceholderText()
        {
            var lobby = Create();

            foreach (var feature in AllFeatures)
            {
                lobby.RequestFeature(feature);

                Assert.That(lobby.Current.IsFeatureOpen, Is.True, feature.ToString());
                Assert.That(lobby.Current.OpenFeature, Is.EqualTo(feature));
                // 每个入口都有真实模块了，大厅不应该再叠一句占位说明。
                Assert.That(lobby.Current.StatusMessage, Is.Empty, feature.ToString());
                lobby.CloseFeature();
            }
        }

        [Test]
        public void EveryEntryIsGatedInCompatibilityMode()
        {
            var lobby = Create(LobbyTestCapabilities.LegacyCloud());

            foreach (var feature in AllFeatures)
            {
                lobby.RequestFeature(feature);

                Assert.That(lobby.Current.StatusMessage, Does.Contain("服务器功能尚未升级"), feature.ToString());
                lobby.CloseFeature();
            }
        }

        [Test]
        public void EveryFeatureMapsToARegisteredCapability()
        {
            // 入口如果映射到一个没登记的能力字符串，兼容门控就会静默失效。
            var lobby = Create(LobbyTestCapabilities.LegacyCloud());

            foreach (var feature in AllFeatures)
            {
                lobby.RequestFeature(feature);
                Assert.That(lobby.Current.StatusMessage, Is.Not.Empty, feature.ToString());
                lobby.CloseFeature();
            }

            Assert.That(NarakaServerCapabilities.Full.Length, Is.EqualTo(12));
        }

        [Test]
        public void OnlyOneModalCanBeOpenAtATime()
        {
            var lobby = Create();

            lobby.RequestFeature(LobbyFeature.Inventory);
            lobby.RequestFeature(LobbyFeature.Draw);

            Assert.That(lobby.Current.OpenFeature, Is.EqualTo(LobbyFeature.Draw));
        }

        [Test]
        public void ClosingAFeatureClearsOnlyTheFeatureMessage()
        {
            var lobby = Create(LobbyTestCapabilities.LegacyCloud());
            lobby.RequestFeature(LobbyFeature.Draw);
            Assert.That(lobby.Current.StatusMessage, Is.Not.Empty);

            lobby.CloseFeature();

            Assert.That(lobby.Current.IsFeatureOpen, Is.False);
            Assert.That(lobby.Current.StatusMessage, Is.Empty);
        }

        [Test]
        public void EnteringTheLobbyAgainReloadsTheAccountSummary()
        {
            LobbyAccountSnapshot.TryCreate(3, 1000, 1000, 1000, out var summary);
            var accounts = new FakeLobbyAccountGateway
            {
                Result = LobbyAccountSummaryResult.Success(summary)
            };
            var lobby = new LobbyController(
                new LobbyModel(),
                new FakeLobbySceneGateway(),
                "Map1",
                accounts,
                new FakeLobbyProfileGateway(),
                LobbyTestCapabilities.Full());

            lobby.Enter("player-one", 42);
            var afterFirst = accounts.CallCount;
            lobby.Enter("player-two", 43);

            // 重新登录必须重新取数：留着上一个账号的余额是资产显示错误。
            Assert.That(accounts.CallCount, Is.GreaterThan(afterFirst));
            Assert.That(lobby.Current.AccountId, Is.EqualTo(43));
        }

        [Test]
        public void ReEnteringClosesAnyOpenPanel()
        {
            var lobby = Create();
            lobby.RequestFeature(LobbyFeature.Shop);

            lobby.Enter("player-two", 43);

            Assert.That(lobby.Current.IsFeatureOpen, Is.False);
        }

        [Test]
        public void StartingTheGameOnlyTouchesTheSceneGateway()
        {
            // P1 只负责"请求进入 Map1"这个边界，不包含任何战斗或结算。
            var scene = new FakeLobbySceneGateway();
            LobbyAccountSnapshot.TryCreate(3, 1000, 1000, 1000, out var summary);
            var lobby = new LobbyController(
                new LobbyModel(),
                scene,
                "Map1",
                new FakeLobbyAccountGateway { Result = LobbyAccountSummaryResult.Success(summary) },
                new FakeLobbyProfileGateway(),
                LobbyTestCapabilities.Full());
            lobby.Enter("player-one", 42);

            lobby.StartGameAsync(CancellationToken.None).Forget();

            Assert.That(scene.LoadCount, Is.EqualTo(1));
            Assert.That(scene.LastSceneName, Is.EqualTo("Map1"));
        }
    }
}
