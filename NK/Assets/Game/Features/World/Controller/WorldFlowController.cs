using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Features.Loading.Controller;
using Naraka.Features.Lobby.Controller;

namespace Naraka.Features.World.Controller
{
    public interface IWorldFlowController : IReadOnlyState<WorldPresentationState>
    {
        /// <summary>从大厅进入地图一。与大厅"开始游戏"走同一条路径。</summary>
        UniTask EnterMap01Async(CancellationToken cancellationToken);

        /// <summary>进入地图二。传送门只允许触发一次，重复请求直接被拒绝。</summary>
        UniTask EnterMap02Async(CancellationToken cancellationToken);

        /// <summary>死亡后异步返回地图一重生点。</summary>
        UniTask ReturnToMap01AfterDeathAsync(CancellationToken cancellationToken);

        /// <summary>场景入口 View 取走本次到达方式。取走后归零，避免重载场景重复播放出场动画。</summary>
        WorldArrival ConsumeArrival();
    }

    /// <summary>
    /// 地图之间的异步流转。
    ///
    /// 它同时实现 <see cref="ILobbySceneGateway"/>：大厅的"开始游戏"按钮因此不需要知道
    /// 加载界面、真实进度或出场动画的存在，只调用既有的网关接口。
    ///
    /// 重复触发防护有两层：<see cref="WorldPresentationState.IsTransitioning"/> 对外可见，
    /// 内部 <c>_isTransitioning</c> 保证即使同一帧连续调用也只会创建一个切换任务。
    /// </summary>
    public sealed class WorldFlowController :
        IController, IWorldFlowController, ILobbySceneGateway, IDisposable
    {
        private readonly ILoadingController _loading;
        private readonly ReactiveState<WorldPresentationState> _state;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _isTransitioning;
        private bool _disposed;
        private WorldArrival _pendingArrival;

        public WorldFlowController(ILoadingController loading)
        {
            _loading = loading ?? throw new ArgumentNullException(nameof(loading));
            _state = new ReactiveState<WorldPresentationState>(WorldPresentationState.Initial);
        }

        public WorldPresentationState Current => _state.Current;

        public WorldArrival ConsumeArrival()
        {
            var arrival = _pendingArrival;
            _pendingArrival = WorldArrival.None;
            if (arrival != WorldArrival.None)
            {
                SetState(new WorldPresentationState(
                    Current.CurrentMapId, WorldArrival.None, Current.IsTransitioning));
            }

            return arrival;
        }

        /// <summary>
        /// 大厅"开始游戏"的落点。场景名由组合根注入大厅控制器，这里只负责实际流转。
        /// </summary>
        public UniTask LoadMapAsync(string sceneName, CancellationToken cancellationToken) =>
            TransitionAsync(sceneName, WorldArrival.LobbyToMap01, cancellationToken);

        public UniTask EnterMap01Async(CancellationToken cancellationToken) =>
            TransitionAsync(WorldMapIds.Map01Task, WorldArrival.LobbyToMap01, cancellationToken);

        public UniTask EnterMap02Async(CancellationToken cancellationToken) =>
            TransitionAsync(
                WorldMapIds.Map02CombatGraybox, WorldArrival.Map01ToMap02, cancellationToken);

        public UniTask ReturnToMap01AfterDeathAsync(CancellationToken cancellationToken) =>
            TransitionAsync(WorldMapIds.Map01Task, WorldArrival.DeathToMap01, cancellationToken);

        private async UniTask TransitionAsync(
            string sceneName,
            WorldArrival arrival,
            CancellationToken cancellationToken)
        {
            if (_isTransitioning)
            {
                // 已有切换在途：绝不创建第二个场景切换任务。
                return;
            }

            _isTransitioning = true;
            _pendingArrival = arrival;
            SetState(new WorldPresentationState(Current.CurrentMapId, arrival, true));
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           cancellationToken, _lifetime.Token))
                {
                    await _loading.LoadSceneAsync(sceneName, linked.Token);
                }

                SetState(new WorldPresentationState(sceneName, arrival, false));
            }
            catch (OperationCanceledException)
            {
                _pendingArrival = WorldArrival.None;
                SetState(new WorldPresentationState(Current.CurrentMapId, WorldArrival.None, false));
                throw;
            }
            catch
            {
                // 失败时把到达状态清掉并恢复可操作：下一次点击必须能重新发起加载。
                _pendingArrival = WorldArrival.None;
                SetState(new WorldPresentationState(Current.CurrentMapId, WorldArrival.None, false));
                throw;
            }
            finally
            {
                _isTransitioning = false;
            }
        }

        public IDisposable Subscribe(IObserver<WorldPresentationState> observer) =>
            _state.Subscribe(observer);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            // 先标记再释放：取消会让在途的 TransitionAsync 走进 catch，
            // 那里不能再往已经释放的 ReactiveState 写值。
            _disposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _state.Dispose();
        }

        /// <summary>
        /// 释放之后不再写状态。容器在场景切换途中被销毁是正常情况（例如应用退出），
        /// 此时继续回调一个已经释放的状态源只会抛 ObjectDisposedException。
        /// </summary>
        private void SetState(WorldPresentationState state)
        {
            if (_disposed)
            {
                return;
            }

            _state.Set(state);
        }
    }
}
