using Naraka.Core.Application.Messaging;
using Naraka.Features.Account.Controller;
using Naraka.Infrastructure.Messaging;
using NUnit.Framework;
using VContainer;
using MessagePipe;

namespace Naraka.P0.Tests
{
    public sealed class MessagePipeIntegrationTests
    {
        [Test]
        public void DomainEventBusPublishesThroughMessagePipeAndReleasesSubscription()
        {
            var builder = new ContainerBuilder();
            builder.RegisterMessagePipe();
            builder.Register<MessagePipeDomainEventBus>(Lifetime.Singleton).As<IDomainEventBus>();
            using var container = builder.Build();
            var events = container.Resolve<IDomainEventBus>();
            var receivedAccountId = 0L;

            var subscription = events.Subscribe<AccountAuthenticatedEvent>(
                value => receivedAccountId = value.AccountId);
            events.Publish(new AccountAuthenticatedEvent("player-one", 42));

            Assert.That(receivedAccountId, Is.EqualTo(42));
            subscription.Dispose();
            events.Publish(new AccountAuthenticatedEvent("player-two", 84));
            Assert.That(receivedAccountId, Is.EqualTo(42));
        }
    }
}
