using System;
using System.Collections.Generic;
using MessagePipe;
using Naraka.Core.Application.Messaging;

namespace Naraka.Infrastructure.Messaging
{
    public sealed class MessagePipeDomainEventBus : IDomainEventBus, IDisposable
    {
        private readonly EventFactory _eventFactory;
        private readonly Dictionary<Type, IEventChannel> _channels =
            new Dictionary<Type, IEventChannel>();
        private readonly object _gate = new object();
        private bool _isDisposed;

        public MessagePipeDomainEventBus(EventFactory eventFactory)
        {
            _eventFactory = eventFactory ?? throw new ArgumentNullException(nameof(eventFactory));
        }

        public void Publish<TEvent>(TEvent domainEvent) => GetChannel<TEvent>().Publish(domainEvent);

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return GetChannel<TEvent>().Subscribe(handler);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                foreach (var channel in _channels.Values)
                {
                    channel.Dispose();
                }

                _channels.Clear();
            }
        }

        private EventChannel<TEvent> GetChannel<TEvent>()
        {
            lock (_gate)
            {
                if (_isDisposed)
                {
                    throw new ObjectDisposedException(nameof(MessagePipeDomainEventBus));
                }

                var eventType = typeof(TEvent);
                if (_channels.TryGetValue(eventType, out var existing))
                {
                    return (EventChannel<TEvent>)existing;
                }

                var created = new EventChannel<TEvent>(_eventFactory);
                _channels.Add(eventType, created);
                return created;
            }
        }

        private interface IEventChannel : IDisposable
        {
        }

        private sealed class EventChannel<TEvent> : IEventChannel
        {
            private readonly IDisposablePublisher<TEvent> _publisher;
            private readonly ISubscriber<TEvent> _subscriber;

            public EventChannel(EventFactory eventFactory)
            {
                (_publisher, _subscriber) = eventFactory.CreateEvent<TEvent>();
            }

            public void Publish(TEvent domainEvent) => _publisher.Publish(domainEvent);

            public IDisposable Subscribe(Action<TEvent> handler) => _subscriber.Subscribe(handler);

            public void Dispose() => _publisher.Dispose();
        }
    }
}
