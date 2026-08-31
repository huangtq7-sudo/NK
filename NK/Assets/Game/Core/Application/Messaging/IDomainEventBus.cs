using System;

namespace Naraka.Core.Application.Messaging
{
    /// <summary>
    /// MessagePipe integration seam. Business modules depend on this interface, not a global broker.
    /// </summary>
    public interface IDomainEventBus
    {
        void Publish<TEvent>(TEvent domainEvent);

        IDisposable Subscribe<TEvent>(Action<TEvent> handler);
    }
}
