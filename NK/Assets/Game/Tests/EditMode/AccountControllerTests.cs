using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.Messaging;
using Naraka.Features.Account.Controller;
using Naraka.Features.Account.Model;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Naraka.P0.Tests
{
    public sealed class AccountControllerTests
    {
        [UnityTest]
        public IEnumerator SuccessfulLoginUpdatesAuthoritativeSessionAndPresentation() => UniTask.ToCoroutine(async () =>
        {
            var model = new AccountSessionModel();
            var events = new RecordingEventBus();
            var controller = new AccountController(
                new SuccessfulGateway(),
                model,
                events,
                ReadyStartup.Instance);

            await controller.LoginAsync(" player-one ", "correct-password", CancellationToken.None);

            Assert.That(model.IsAuthenticated, Is.True);
            Assert.That(model.Username, Is.EqualTo("player-one"));
            Assert.That(model.AccountId, Is.EqualTo(42));
            Assert.That(controller.Current.Phase, Is.EqualTo(AccountFlowPhase.Authenticated));
            Assert.That(controller.Current.AccountId, Is.EqualTo(42));
            Assert.That(events.LastAuthenticated.AccountId, Is.EqualTo(42));
        });

        [UnityTest]
        public IEnumerator InvalidCredentialsNeverReachGateway() => UniTask.ToCoroutine(async () =>
        {
            var gateway = new CountingGateway();
            var controller = new AccountController(
                gateway,
                new AccountSessionModel(),
                new RecordingEventBus(),
                ReadyStartup.Instance);

            await controller.LoginAsync("x", "short", CancellationToken.None);

            Assert.That(gateway.LoginCalls, Is.Zero);
            Assert.That(controller.Current.Phase, Is.EqualTo(AccountFlowPhase.Failed));
        });

        [UnityTest]
        public IEnumerator LoginCannotReachGatewayBeforeVersionCheckPasses() => UniTask.ToCoroutine(async () =>
        {
            var gateway = new CountingGateway();
            var controller = new AccountController(
                gateway,
                new AccountSessionModel(),
                new RecordingEventBus(),
                new BlockedStartup());

            await controller.LoginAsync("player-one", "correct-password", CancellationToken.None);

            Assert.That(gateway.LoginCalls, Is.Zero);
            Assert.That(controller.Current.Message, Does.Contain("版本检查"));
        });

        [Test]
        public void LobbyBecomesVisibleOnlyAfterControllerEntry()
        {
            var controller = new LobbyController(new LobbyModel());

            Assert.That(controller.Current.IsVisible, Is.False);
            controller.Enter("player-one", 42);

            Assert.That(controller.Current.IsVisible, Is.True);
            Assert.That(controller.Current.Username, Is.EqualTo("player-one"));
            Assert.That(controller.Current.AccountId, Is.EqualTo(42));
        }

        private sealed class SuccessfulGateway : IAccountGateway
        {
            public UniTask<AccountRegistrationResult> RegisterAsync(
                string username,
                string password,
                CancellationToken cancellationToken) =>
                UniTask.FromResult(new AccountRegistrationResult(AccountRegistrationStatus.Success));

            public UniTask<AccountLoginResult> LoginAsync(
                string username,
                string password,
                CancellationToken cancellationToken) =>
                UniTask.FromResult(new AccountLoginResult(AccountLoginStatus.Success, 42));
        }

        private sealed class CountingGateway : IAccountGateway
        {
            public int LoginCalls { get; private set; }

            public UniTask<AccountRegistrationResult> RegisterAsync(
                string username,
                string password,
                CancellationToken cancellationToken) =>
                UniTask.FromResult(new AccountRegistrationResult(AccountRegistrationStatus.Success));

            public UniTask<AccountLoginResult> LoginAsync(
                string username,
                string password,
                CancellationToken cancellationToken)
            {
                LoginCalls++;
                return UniTask.FromResult(new AccountLoginResult(AccountLoginStatus.Success, 42));
            }
        }

        private sealed class ReadyStartup : IStartupReadiness
        {
            public static ReadyStartup Instance { get; } = new ReadyStartup();

            public bool IsReady => true;

            public string BlockingReason => string.Empty;
        }

        private sealed class BlockedStartup : IStartupReadiness
        {
            public bool IsReady => false;

            public string BlockingReason => "版本检查尚未完成。";
        }

        private sealed class RecordingEventBus : IDomainEventBus
        {
            public AccountAuthenticatedEvent LastAuthenticated { get; private set; }

            public void Publish<TEvent>(TEvent domainEvent)
            {
                if (domainEvent is AccountAuthenticatedEvent authenticated)
                {
                    LastAuthenticated = authenticated;
                }
            }

            public IDisposable Subscribe<TEvent>(Action<TEvent> handler) =>
                throw new NotSupportedException();
        }
    }
}
