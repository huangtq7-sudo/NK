using System;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Features.Character.Model;

namespace Naraka.Features.Character.Controller
{
    /// <summary>
    /// 玩家状态编排。它是 View 与纯逻辑核心之间唯一的业务协调层：
    /// View 只提供输入意图并消费帧输出，绝不自己决定状态、伤害或冷却。
    /// </summary>
    public interface IPlayerController : IReadOnlyState<PlayerPresentationState>
    {
        /// <summary>推进一帧并返回本帧的位移、朝向、动画与命中窗。</summary>
        PlayerFrameOutput Tick(PlayerInputFrame input, float deltaSeconds);

        void BeginSpawn(ActionState spawnState);

        void SetInputLocked(bool locked);

        void SetLoading(bool loading);

        void SetGrounded(bool grounded);

        VitalsDamageResult ApplyDamage(float amount);

        void Respawn();

        /// <summary>死亡动画播完的一次性信号。消费后不会再次返回 true。</summary>
        bool ConsumeDeathCompleted();

        /// <summary>
        /// 控制器是否已经释放。持久化组合根被销毁（例如退出应用）之后，
        /// 仍然活着的角色 View 必须据此停止推进，而不是继续写一个已释放的状态源。
        /// </summary>
        bool IsDisposed { get; }

        PlayerTuning Tuning { get; }
    }

    public sealed class PlayerController : IController, IPlayerController, IDisposable
    {
        private readonly PlayerCore _core;
        private readonly ReactiveState<PlayerPresentationState> _state;
        private ActionRejection _lastRejection;
        private PlayerPresentationState _lastPublished;
        private bool _disposed;

        public PlayerController(PlayerTuning tuning)
        {
            _core = new PlayerCore(tuning);
            _lastPublished = PlayerPresentationState.FromCore(_core, ActionRejection.None);
            _state = new ReactiveState<PlayerPresentationState>(_lastPublished);
        }

        public PlayerTuning Tuning => _core.Tuning;

        public bool IsDisposed => _disposed;

        public PlayerPresentationState Current => _disposed ? _lastPublished : _state.Current;

        public PlayerFrameOutput Tick(PlayerInputFrame input, float deltaSeconds)
        {
            if (_disposed)
            {
                return default;
            }

            var output = _core.Tick(input, deltaSeconds);

            // 拒绝原因是"最近一次"，不是"本帧"：本帧没有新的拒绝时保留上一次，
            // 否则 HUD 会在下一帧立刻丢掉刚刚要显示的"体力不足"。
            if (output.Rejection != ActionRejection.None)
            {
                _lastRejection = output.Rejection;
            }

            Publish();
            return output;
        }

        public void BeginSpawn(ActionState spawnState)
        {
            if (_disposed)
            {
                return;
            }

            _core.BeginSpawn(spawnState);
            Publish();
        }

        public void SetInputLocked(bool locked)
        {
            _core.SetInputLocked(locked);
            Publish();
        }

        public void SetLoading(bool loading)
        {
            _core.SetLoading(loading);
            Publish();
        }

        public void SetGrounded(bool grounded) => _core.SetGrounded(grounded);

        public VitalsDamageResult ApplyDamage(float amount)
        {
            if (_disposed)
            {
                return default;
            }

            var result = _core.ApplyDamage(amount);
            Publish();
            return result;
        }

        public void Respawn()
        {
            if (_disposed)
            {
                return;
            }

            _lastRejection = ActionRejection.None;
            _core.Respawn();
            Publish();
        }

        public bool ConsumeDeathCompleted() => _core.ConsumeDeathCompleted();

        public IDisposable Subscribe(IObserver<PlayerPresentationState> observer) =>
            _state.Subscribe(observer);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _state.Dispose();
        }

        private void Publish()
        {
            if (_disposed)
            {
                return;
            }

            _lastPublished = PlayerPresentationState.FromCore(_core, _lastRejection);
            _state.Set(_lastPublished);
        }
    }
}
