using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Core.Application.Scenes;
using Naraka.Features.Expedition.Controller;
using Naraka.Features.Loading.Controller;
using Naraka.Features.Lobby.Controller;

namespace Naraka.Features.World.Controller
{
    public interface IWorldFlowController : IReadOnlyState<WorldPresentationState>
    {
        /// <summary>进入地图一（<see cref="WorldMapIds.Map01"/>）。与大厅"开始游戏"走同一条路径。</summary>
        UniTask EnterMap01Async(CancellationToken cancellationToken);

        /// <summary>
        /// 进入地图二（<see cref="WorldMapIds.Map02"/>）。
        /// 传送门只允许触发一次，重复请求直接被拒绝。
        /// </summary>
        UniTask EnterMap02Async(CancellationToken cancellationToken);

        /// <summary>死亡后异步返回地图一重生点。</summary>
        UniTask ReturnToMap01AfterDeathAsync(CancellationToken cancellationToken);

        /// <summary>幂等结算当前远征后返回Bootstrap大厅场景。</summary>
        UniTask ReturnToLobbyAsync(CancellationToken cancellationToken);

        /// <summary>场景入口 View 取走本次到达方式。取走后归零，避免重载场景重复播放出场动画。</summary>
        WorldArrival ConsumeArrival();
    }

    /// <summary>
    /// 地图之间的异步流转。
    ///
    /// 它同时实现 <see cref="ILobbySceneGateway"/>：大厅的"开始游戏"按钮因此不需要知道
    /// 加载界面、真实进度或出场动画的存在，只调用既有的网关接口。
    ///
    /// 这里是 MapId 与 Unity 场景名之间的**唯一**翻译点：对外、对状态、对未来的服务端
    /// 一律只用稳定 MapId；只有在把加载请求交给 <c>ILoadingController</c> 的那一行
    /// 才通过 <see cref="IWorldSceneCatalog"/> 换成场景名。
    /// 因此换美术素材（灰盒战斗场景换成正式战斗场景）不会改变任何业务状态值。
    ///
    /// 重复触发防护有两层：<see cref="WorldPresentationState.IsTransitioning"/> 对外可见，
    /// 内部 <c>_isTransitioning</c> 保证即使同一帧连续调用也只会创建一个切换任务。
    /// </summary>
    public sealed class WorldFlowController :
        IController, IWorldFlowController, ILobbySceneGateway, IDisposable
    {
        private readonly ILoadingController _loading;
        private readonly IWorldSceneCatalog _sceneCatalog;
        private readonly IExpeditionController _expedition;
        private readonly ReactiveState<WorldPresentationState> _state;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _isTransitioning;
        private bool _disposed;
        private WorldArrival _pendingArrival;

        public WorldFlowController(
            ILoadingController loading,
            IWorldSceneCatalog sceneCatalog,
            IExpeditionController expedition = null)
        {
            _loading = loading ?? throw new ArgumentNullException(nameof(loading));
            _sceneCatalog = sceneCatalog ?? throw new ArgumentNullException(nameof(sceneCatalog));
            _expedition = expedition;
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
        /// 大厅"开始游戏"的落点。大厅只转发一个它从不解释的标识符，
        /// 该标识符是 <see cref="WorldMapIds"/> 里的业务 MapId，由组合根注入。
        /// </summary>
        public UniTask LoadMapAsync(string mapId, CancellationToken cancellationToken) =>
            TransitionAsync(mapId, WorldArrival.LobbyToMap01, cancellationToken);

        public UniTask EnterMap01Async(CancellationToken cancellationToken) =>
            TransitionAsync(WorldMapIds.Map01, WorldArrival.LobbyToMap01, cancellationToken);

        public UniTask EnterMap02Async(CancellationToken cancellationToken) =>
            TransitionAsync(WorldMapIds.Map02, WorldArrival.Map01ToMap02, cancellationToken);

        public async UniTask ReturnToMap01AfterDeathAsync(CancellationToken cancellationToken)
        {
            if (_expedition != null)
            {
                var status = await _expedition.RecordDeathAsync(cancellationToken);

                // NotFound is decided client-side before any request leaves: there is no
                // active expedition, so there is no death to record against one. That is
                // not a refusal. Treating it as fatal left the player permanently dead,
                // which is the ordinary case today because starting an expedition is not
                // wired to the UI yet — enter the task map, die once, and the respawn
                // never happens.
                //
                // Every other non-success really is the authoritative server declining,
                // and those must still stop the respawn. Otherwise dropping the
                // connection would be a way to take a death that never gets recorded.
                if (status != ExpeditionOperationStatus.Success &&
                    status != ExpeditionOperationStatus.NotFound)
                {
                    throw new InvalidOperationException(ExpeditionOperationMessages.Describe(status));
                }
            }

            await TransitionAsync(WorldMapIds.Map01, WorldArrival.DeathToMap01, cancellationToken);
        }

        public async UniTask ReturnToLobbyAsync(CancellationToken cancellationToken)
        {
            if (_isTransitioning)
            {
                return;
            }

            if (_expedition == null)
            {
                throw new InvalidOperationException("远征控制器未注入，不能结算并返回大厅。");
            }

            _isTransitioning = true;
            SetState(new WorldPresentationState(Current.CurrentMapId, WorldArrival.None, true));
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           cancellationToken, _lifetime.Token))
                {
                    var status = await _expedition.SettleReturnToLobbyAsync(linked.Token);
                    if (status != ExpeditionOperationStatus.Success)
                    {
                        throw new InvalidOperationException(ExpeditionOperationMessages.Describe(status));
                    }

                    await _loading.LoadSceneAsync(WorldSceneNames.Boot, linked.Token);
                }

                _pendingArrival = WorldArrival.None;
                SetState(WorldPresentationState.Initial);
            }
            catch
            {
                SetState(new WorldPresentationState(Current.CurrentMapId, WorldArrival.None, false));
                throw;
            }
            finally
            {
                _isTransitioning = false;
            }
        }

        private async UniTask TransitionAsync(
            string mapId,
            WorldArrival arrival,
            CancellationToken cancellationToken)
        {
            if (_isTransitioning)
            {
                // 已有切换在途：绝不创建第二个场景切换任务。
                return;
            }

            // 先解析再置位。未登记的 MapId 必须在把界面切进"正在加载"之前就失败，
            // 否则加载界面会为一个根本不存在的目标亮起来。
            var sceneName = _sceneCatalog.ResolveSceneName(mapId);

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

                // 写进状态的是 MapId，不是刚刚加载的场景名。
                SetState(new WorldPresentationState(mapId, arrival, false));
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
