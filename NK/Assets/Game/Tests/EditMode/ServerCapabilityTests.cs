using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Features.Bootstrap.Controller;
using Naraka.Features.Bootstrap.Model;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Naraka.P0.Tests
{
    /// <summary>
    /// 旧云端兼容性。
    ///
    /// P1.1-A兼容性与P1正式门禁。
    /// 匹配P0门禁的旧客户端仍能安全使用没有能力字段的旧响应；P1客户端则要求
    /// P1门禁与明确能力集合，避免向旧Host发送它不认识的协议。
    /// </summary>
    public sealed class ServerCapabilityTests
    {
        [Test]
        public void UnresolvedRegistryBlocksEveryCapability()
        {
            var registry = new ServerCapabilityRegistry();

            Assert.That(registry.IsResolved, Is.False);
            Assert.That(registry.Has(NarakaServerCapabilities.LobbyAccountSummary), Is.False);
            Assert.That(registry.Has(NarakaServerCapabilities.Shop), Is.False);
        }

        [Test]
        public void MissingCapabilityFieldFallsBackToCompatibilityMode()
        {
            var registry = new ServerCapabilityRegistry();

            registry.Resolve(null);

            Assert.That(registry.IsCompatibilityMode, Is.True);
            Assert.That(registry.IsResolved, Is.True);
            // 旧云端确实实现了账号概要协议，所以大厅余额仍然要能读出来。
            Assert.That(registry.Has(NarakaServerCapabilities.LobbyAccountSummary), Is.True);
            Assert.That(registry.Has(NarakaServerCapabilities.Shop), Is.False);
            Assert.That(registry.Has(NarakaServerCapabilities.Social), Is.False);
        }

        [Test]
        public void EmptyCapabilityArrayIsTreatedAsMissingField()
        {
            var registry = new ServerCapabilityRegistry();

            registry.Resolve(new string[0]);

            Assert.That(registry.IsCompatibilityMode, Is.True);
            Assert.That(registry.Has(NarakaServerCapabilities.LobbyAccountSummary), Is.True);
        }

        [Test]
        public void DeclaredCapabilitiesReplaceTheCompatibilitySet()
        {
            var registry = new ServerCapabilityRegistry();

            registry.Resolve(NarakaServerCapabilities.Full);

            Assert.That(registry.IsCompatibilityMode, Is.False);
            Assert.That(registry.Has(NarakaServerCapabilities.Shop), Is.True);
            Assert.That(registry.Has(NarakaServerCapabilities.Social), Is.True);
        }

        [Test]
        public void ResetClearsCapabilitiesSoAnotherServerCannotInheritThem()
        {
            var registry = new ServerCapabilityRegistry();
            registry.Resolve(NarakaServerCapabilities.Full);

            registry.Reset();

            Assert.That(registry.IsResolved, Is.False);
            Assert.That(registry.Has(NarakaServerCapabilities.Shop), Is.False);
        }

        [UnityTest]
        public IEnumerator MatchingLegacyManifestWithoutCapabilitiesStillPassesVersionCheck() =>
            UniTask.ToCoroutine(async () =>
            {
                var registry = new ServerCapabilityRegistry();
                var controller = new ConfigVersionController(
                    new LegacyCloudGateway(),
                    new ConfigVersionModel("0.1", "p0-config-1", "LegacyNetworkV1"),
                    registry);

                await controller.CheckAsync(CancellationToken.None);

                Assert.That(controller.IsReady, Is.True, "旧云端必须仍然可以通过版本预检并登录。");
                Assert.That(registry.IsCompatibilityMode, Is.True);
                Assert.That(registry.Has(NarakaServerCapabilities.LobbyAccountSummary), Is.True);
            });

        [UnityTest]
        public IEnumerator P1ClientRejectsTheLegacyP0ConfigGate() =>
            UniTask.ToCoroutine(async () =>
            {
                var registry = new ServerCapabilityRegistry();
                var controller = new ConfigVersionController(
                    new LegacyCloudGateway(),
                    new ConfigVersionModel("0.1", "p1-config-1", "LegacyNetworkV1"),
                    registry);

                await controller.CheckAsync(CancellationToken.None);

                Assert.That(controller.IsReady, Is.False);
                Assert.That(controller.Current.Phase, Is.EqualTo(ConfigVersionPhase.Blocked));
                Assert.That(registry.IsResolved, Is.False);
            });

        [UnityTest]
        public IEnumerator UpgradedManifestEnablesFullCapabilities() =>
            UniTask.ToCoroutine(async () =>
            {
                var registry = new ServerCapabilityRegistry();
                var controller = new ConfigVersionController(
                    new UpgradedHostGateway(),
                    new ConfigVersionModel("0.1", "p1-config-1", "LegacyNetworkV1"),
                    registry);

                await controller.CheckAsync(CancellationToken.None);

                Assert.That(controller.IsReady, Is.True);
                Assert.That(registry.IsCompatibilityMode, Is.False);
                Assert.That(registry.Has(NarakaServerCapabilities.Forge), Is.True);
            });

        [UnityTest]
        public IEnumerator IncompatibleManifestLeavesCapabilitiesUnresolved() =>
            UniTask.ToCoroutine(async () =>
            {
                var registry = new ServerCapabilityRegistry();
                var controller = new ConfigVersionController(
                    new MismatchedConfigGateway(),
                    new ConfigVersionModel("0.1", "p1-config-1", "LegacyNetworkV1"),
                    registry);

                await controller.CheckAsync(CancellationToken.None);

                Assert.That(controller.IsReady, Is.False);
                Assert.That(registry.IsResolved, Is.False);
            });

        [Test]
        public void CompatibilityModeShowsUpgradeNoticeInsteadOfSendingUnknownProtocols()
        {
            var gateway = SuccessfulAccountGateway();
            var controller = new LobbyController(
                new LobbyModel(),
                new FakeLobbySceneGateway(),
                "Map1",
                gateway,
                new FakeLobbyProfileGateway(),
                LobbyTestCapabilities.LegacyCloud());
            controller.Enter("player-one", 42);

            controller.RequestFeature(LobbyFeature.Shop);

            Assert.That(controller.Current.IsFeatureOpen, Is.True);
            Assert.That(controller.Current.StatusMessage, Does.Contain("服务器功能尚未升级"));
            // 只允许账号概要这一个已部署协议离开客户端。
            Assert.That(gateway.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void FullCapabilitiesShowTheModuleMessageInsteadOfTheUpgradeNotice()
        {
            var controller = new LobbyController(
                new LobbyModel(),
                new FakeLobbySceneGateway(),
                "Map1",
                SuccessfulAccountGateway(),
                new FakeLobbyProfileGateway(),
                LobbyTestCapabilities.Full());
            controller.Enter("player-one", 42);

            controller.RequestFeature(LobbyFeature.Shop);

            Assert.That(controller.Current.StatusMessage, Does.Not.Contain("服务器功能尚未升级"));
        }

        [Test]
        public void EveryLobbyEntryIsGatedInCompatibilityMode()
        {
            var controller = new LobbyController(
                new LobbyModel(),
                new FakeLobbySceneGateway(),
                "Map1",
                SuccessfulAccountGateway(),
                new FakeLobbyProfileGateway(),
                LobbyTestCapabilities.LegacyCloud());
            controller.Enter("player-one", 42);

            // 遍历全部入口，避免将来新增入口时漏掉能力门禁。
            foreach (LobbyFeature feature in System.Enum.GetValues(typeof(LobbyFeature)))
            {
                controller.CloseFeature();
                controller.RequestFeature(feature);
                Assert.That(
                    controller.Current.StatusMessage,
                    Does.Contain("服务器功能尚未升级"),
                    $"入口 {feature} 在兼容模式下没有被拦住。");
            }
        }

        private static FakeLobbyAccountGateway SuccessfulAccountGateway()
        {
            LobbyAccountSnapshot.TryCreate(1, 1000, 1000, 1000, out var snapshot);
            return new FakeLobbyAccountGateway
            {
                Result = LobbyAccountSummaryResult.Success(snapshot)
            };
        }

        /// <summary>旧云端 Host：响应里没有 serverCapabilities 字段。</summary>
        private sealed class LegacyCloudGateway : IConfigVersionGateway
        {
            public UniTask<ConfigVersionManifest> GetRequiredVersionAsync(CancellationToken cancellationToken) =>
                UniTask.FromResult(new ConfigVersionManifest(
                    "p0-config-1", "0.1", "0.1", "LegacyNetworkV1"));
        }

        private sealed class UpgradedHostGateway : IConfigVersionGateway
        {
            public UniTask<ConfigVersionManifest> GetRequiredVersionAsync(CancellationToken cancellationToken) =>
                UniTask.FromResult(new ConfigVersionManifest(
                    "p1-config-1", "0.1", "0.1", "LegacyNetworkV1", NarakaServerCapabilities.Full));
        }

        private sealed class MismatchedConfigGateway : IConfigVersionGateway
        {
            public UniTask<ConfigVersionManifest> GetRequiredVersionAsync(CancellationToken cancellationToken) =>
                UniTask.FromResult(new ConfigVersionManifest(
                    "another-config", "0.1", "0.1", "LegacyNetworkV1", NarakaServerCapabilities.Full));
        }
    }
}
