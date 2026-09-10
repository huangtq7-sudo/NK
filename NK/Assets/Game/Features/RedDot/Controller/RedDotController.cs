using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.Messaging;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Core.Application.RedDot;

namespace Naraka.Features.RedDot.Controller
{
    /// <summary>服务端持久化的一条红点版本记录。</summary>
    public readonly struct RedDotStateRecord
    {
        public RedDotStateRecord(string path, long version, long seenVersion)
        {
            Path = path ?? string.Empty;
            Version = version;
            SeenVersion = seenVersion;
        }

        public string Path { get; }

        public long Version { get; }

        public long SeenVersion { get; }
    }

    /// <summary>
    /// 红点持久化网关。
    ///
    /// "看过没看过"必须跨登录保留，否则每次重登所有红点都会重新亮起。
    /// 服务端未提供该能力时（旧云端），网关可以缺省为空实现，红点退化为本次会话内有效。
    /// </summary>
    public interface IRedDotGateway
    {
        UniTask<IReadOnlyList<RedDotStateRecord>> LoadAsync(CancellationToken cancellationToken);

        UniTask SaveSeenAsync(string path, long seenVersion, CancellationToken cancellationToken);
    }

    public interface IRedDotController : IReadOnlyState<RedDotPresentationState>
    {
        /// <summary>玩家看过某个节点。会向服务端持久化 SeenVersion。</summary>
        void MarkSeen(string path);

        UniTask ReloadAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 红点聚合。
    ///
    /// 这是一个<b>独立模块</b>：它不认识背包、锻造或社交，只认识路径与版本号。
    /// 业务模块通过 MessagePipe 发布 <see cref="RedDotSourceChanged"/> 声明自己有没有内容，
    /// 这里负责把叶子的变化沿路径向上聚合，并把结果作为只读状态交给界面。
    ///
    /// 因此不存在"某个按钮永远挂着红点"的情况：叶子没有内容时，祖先会被重算并熄灭。
    /// </summary>
    public sealed class RedDotController : IController, IRedDotController, IDisposable
    {
        private readonly IDomainEventBus _bus;
        private readonly IRedDotGateway _gateway;
        private readonly IServerCapabilities _capabilities;
        private readonly RedDotTree _tree = new RedDotTree();
        private readonly ReactiveState<RedDotPresentationState> _state;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        private bool _isLoading;

        public RedDotController(
            IDomainEventBus bus,
            IRedDotGateway gateway,
            IServerCapabilities capabilities)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));

            _state = new ReactiveState<RedDotPresentationState>(RedDotPresentationState.Initial);

            _subscriptions.Add(_bus.Subscribe<RedDotSourceChanged>(OnSourceChanged));
            _subscriptions.Add(_bus.Subscribe<RedDotSeen>(OnSeen));
            _subscriptions.Add(_bus.Subscribe<RedDotSourceRemoved>(OnSourceRemoved));
        }

        public RedDotPresentationState Current => _state.Current;

        public void MarkSeen(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var seenVersion = _tree.Get(path).Version;
            _tree.MarkSeen(path);
            Publish();
            PersistAsync(path, seenVersion).Forget();
        }

        public async UniTask ReloadAsync(CancellationToken cancellationToken)
        {
            if (_isLoading || !_capabilities.Has(NarakaServerCapabilities.RedDot))
            {
                return;
            }

            _isLoading = true;
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           cancellationToken, _lifetime.Token))
                {
                    var records = await _gateway.LoadAsync(linked.Token);
                    foreach (var record in records)
                    {
                        _tree.Restore(record.Path, record.Version, record.SeenVersion);
                    }

                    Publish();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // 读不到历史"看过"记录时红点只是可能多亮一次，不影响任何资产，
                // 因此这里不打断大厅，也不显示错误。
            }
            finally
            {
                _isLoading = false;
            }
        }

        public IDisposable Subscribe(IObserver<RedDotPresentationState> observer) => _state.Subscribe(observer);

        /// <summary>切换账号时清空。红点属于账号，不属于客户端进程。</summary>
        public void Reset()
        {
            _tree.Reset();
            Publish();
        }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription?.Dispose();
            }

            _subscriptions.Clear();
            _lifetime.Cancel();
            _lifetime.Dispose();
            _state.Dispose();
        }

        private void OnSourceChanged(RedDotSourceChanged message)
        {
            if (_tree.SetLeaf(message.Path, message.HasContent))
            {
                Publish();
            }
        }

        private void OnSeen(RedDotSeen message) => MarkSeen(message.Path);

        private void OnSourceRemoved(RedDotSourceRemoved message)
        {
            _tree.Remove(message.Path);
            Publish();
        }

        private void Publish()
        {
            var active = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in _tree.Paths)
            {
                if (_tree.IsActive(path))
                {
                    active.Add(path);
                }
            }

            _state.Set(new RedDotPresentationState(_tree.Revision, active));
        }

        private async UniTaskVoid PersistAsync(string path, long seenVersion)
        {
            if (!_capabilities.Has(NarakaServerCapabilities.RedDot))
            {
                return;
            }

            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           CancellationToken.None, _lifetime.Token))
                {
                    await _gateway.SaveSeenAsync(path, seenVersion, linked.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // 持久化失败只会让红点下次登录重新亮一次，不改变任何账号资产。
            }
        }
    }
}
