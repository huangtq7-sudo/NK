using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Features.Account.Controller;
using Naraka.Infrastructure.Network;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Naraka.P0.Tests
{
    public sealed class LegacyClientLiveSmokeTests
    {
        [UnityTest]
        public IEnumerator RegisterAndLoginAgainstConfiguredHost() => UniTask.ToCoroutine(async () =>
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable("NARAKA_RUN_LEGACY_CLIENT_SMOKE"),
                    "1",
                    StringComparison.Ordinal))
            {
                Assert.Ignore("Set NARAKA_RUN_LEGACY_CLIENT_SMOKE=1 to run the live Host/MySQL smoke.");
            }

            var username = Environment.GetEnvironmentVariable("NARAKA_SMOKE_USERNAME");
            var password = Environment.GetEnvironmentVariable("NARAKA_SMOKE_PASSWORD");
            Assert.That(username, Is.Not.Null.And.Not.Empty);
            Assert.That(password, Is.Not.Null.And.Not.Empty);

            using var adapter = new LegacyNetworkAdapter("127.0.0.1", 8011);
            var registration = await adapter.RegisterAsync(username, password, CancellationToken.None);
            Assert.That(
                registration.Status,
                Is.EqualTo(AccountRegistrationStatus.Success).Or.EqualTo(AccountRegistrationStatus.AlreadyExists));

            var login = await adapter.LoginAsync(username, password, CancellationToken.None);
            Assert.That(login.Status, Is.EqualTo(AccountLoginStatus.Success));
            Assert.That(login.AccountId, Is.GreaterThan(0));
        });
    }
}
