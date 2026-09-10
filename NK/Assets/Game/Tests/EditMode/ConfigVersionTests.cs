using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Bootstrap;
using Naraka.Features.Bootstrap.Controller;
using Naraka.Features.Bootstrap.Model;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Naraka.P0.Tests
{
    public sealed class ConfigVersionTests
    {
        [Test]
        public void ModelAcceptsMatchingConfigClientAndProtocolVersions()
        {
            var model = new ConfigVersionModel("0.1", "p1-config-1", "LegacyNetworkV1");

            var result = model.Evaluate(new ConfigVersionManifest(
                "p1-config-1",
                "0.1",
                "0.2",
                "LegacyNetworkV1"));

            Assert.That(result.Status, Is.EqualTo(ConfigCompatibilityStatus.Compatible));
        }

        [TestCase("p0-config-1", "0.1", "0.2", "LegacyNetworkV1", ConfigCompatibilityStatus.ConfigVersionMismatch)]
        [TestCase("p1-config-1", "0.2", "0.3", "LegacyNetworkV1", ConfigCompatibilityStatus.ClientVersionTooOld)]
        [TestCase("p1-config-1", "0.1", "0.2", "LegacyNetworkV2", ConfigCompatibilityStatus.ProtocolVersionMismatch)]
        public void ModelRejectsIncompatibleManifest(
            string configVersion,
            string minimumClientVersion,
            string maximumClientVersion,
            string protocolVersion,
            ConfigCompatibilityStatus expected)
        {
            var model = new ConfigVersionModel("0.1", "p1-config-1", "LegacyNetworkV1");

            var result = model.Evaluate(new ConfigVersionManifest(
                configVersion,
                minimumClientVersion,
                maximumClientVersion,
                protocolVersion));

            Assert.That(result.Status, Is.EqualTo(expected));
        }

        [UnityTest]
        public IEnumerator ControllerOpensStartupGateOnlyAfterCompatibleResponse() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new ConfigVersionController(
                    new CompatibleGateway(),
                    new ConfigVersionModel("0.1", "p1-config-1", "LegacyNetworkV1"),
                    new ServerCapabilityRegistry());

                Assert.That(controller.IsReady, Is.False);
                await controller.CheckAsync(CancellationToken.None);

                Assert.That(controller.IsReady, Is.True);
                Assert.That(controller.Current.Phase, Is.EqualTo(ConfigVersionPhase.Ready));
            });

        private sealed class CompatibleGateway : IConfigVersionGateway
        {
            public UniTask<ConfigVersionManifest> GetRequiredVersionAsync(
                CancellationToken cancellationToken) =>
                UniTask.FromResult(new ConfigVersionManifest(
                    "p1-config-1",
                    "0.1",
                    "0.1",
                    "LegacyNetworkV1"));
        }
    }
}
