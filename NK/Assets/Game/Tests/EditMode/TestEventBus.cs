using System;
using System.Collections.Generic;
using Naraka.Core.Application.Messaging;
using Naraka.Core.Application.RedDot;

namespace Naraka.P0.Tests
{
    /// <summary>
    /// 测试用事件总线。
    ///
    /// 它同时做两件事：真的把事件送到订阅者（红点控制器需要），并记录发布过的红点来源事件
    /// （业务控制器的测试需要断言"声明了什么"）。因此一个替身就够，不必再造两个。
    /// </summary>
    internal sealed class TestEventBus : IDomainEventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();

        /// <summary>按发布顺序记录的红点来源事件。</summary>
        public List<RedDotSourceChanged> RedDotSources { get; } = new List<RedDotSourceChanged>();

        public void Publish<TEvent>(TEvent domainEvent)
        {
            if (domainEvent is RedDotSourceChanged source)
            {
                RedDotSources.Add(source);
            }

            if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            {
                return;
            }

            // 复制一份再回调：订阅者在处理事件时取消订阅不应破坏本次派发。
            foreach (var handler in handlers.ToArray())
            {
                ((Action<TEvent>)handler)(domainEvent);
            }
        }

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            {
                handlers = new List<Delegate>();
                _handlers[typeof(TEvent)] = handlers;
            }

            handlers.Add(handler);
            return new Subscription(() => handlers.Remove(handler));
        }

        /// <summary>某个路径最后一次被声明的内容状态。从未声明过时返回 null。</summary>
        public bool? LastStateOf(string path)
        {
            for (var i = RedDotSources.Count - 1; i >= 0; i--)
            {
                if (string.Equals(RedDotSources[i].Path, path, StringComparison.Ordinal))
                {
                    return RedDotSources[i].HasContent;
                }
            }

            return null;
        }

        private sealed class Subscription : IDisposable
        {
            private Action _dispose;

            public Subscription(Action dispose) => _dispose = dispose;

            public void Dispose()
            {
                _dispose?.Invoke();
                _dispose = null;
            }
        }
    }
}
