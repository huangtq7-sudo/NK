using System;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Features.Combat.Model;
using Naraka.Features.Monster.Model;

namespace Naraka.Features.Monster.Controller
{
    /// <summary>
    /// 一只怪物的编排入口。View 只提供感知数据并消费帧输出，
    /// 绝不自己决定意图、伤害、冷却或阶段。
    /// </summary>
    public interface IMonsterController : IReadOnlyState<MonsterPresentationState>
    {
        MonsterTuning Tuning { get; }

        MonsterFrameOutput Tick(in MonsterSenses senses, float deltaSeconds);

        /// <summary>吃到一次<b>扣防御之前</b>的伤害。</summary>
        MonsterDamageResult ApplyRawDamage(float rawDamage);

        /// <summary>被玩家反击之后进入处决窗口。</summary>
        void BeginExecuteWindow(float seconds);

        /// <summary>巡逻点已到达，开始停顿。</summary>
        void NotifyPatrolPointReached();

        /// <summary>死亡动画播完的一次性信号。</summary>
        bool ConsumeDeathCompleted();

        bool IsDead { get; }

        bool IsExecutable { get; }

        bool IsDisposed { get; }
    }

    /// <inheritdoc cref="IMonsterController" />
    public sealed class MonsterController : IController, IMonsterController, IDisposable
    {
        private readonly MonsterCore _core;
        private readonly ReactiveState<MonsterPresentationState> _state;
        private MonsterPresentationState _lastPublished;
        private AttackColorTag _warningColorTag;
        private bool _disposed;

        public MonsterController(MonsterTuning tuning)
        {
            _core = new MonsterCore(tuning);
            _lastPublished = MonsterPresentationState.FromCore(_core, AttackColorTag.None);
            _state = new ReactiveState<MonsterPresentationState>(_lastPublished);
        }

        public MonsterTuning Tuning => _core.Tuning;

        public bool IsDead => _core.IsDead;

        public bool IsExecutable => _core.IsExecutable;

        public bool IsDisposed => _disposed;

        public MonsterPresentationState Current => _disposed ? _lastPublished : _state.Current;

        public MonsterFrameOutput Tick(in MonsterSenses senses, float deltaSeconds)
        {
            if (_disposed)
            {
                return default;
            }

            var output = _core.Tick(in senses, deltaSeconds);

            // 预警颜色直接取本帧输出：它跟着动作时间轴走，
            // HUD 因此不需要自己算"现在是不是预警段"。
            _warningColorTag = output.IsWarningActive ? output.ColorTag : AttackColorTag.None;
            Publish(output);
            return output;
        }

        public MonsterDamageResult ApplyRawDamage(float rawDamage)
        {
            if (_disposed)
            {
                return default;
            }

            var result = _core.ApplyRawDamage(rawDamage);
            Publish();
            return result;
        }

        public void BeginExecuteWindow(float seconds)
        {
            if (_disposed)
            {
                return;
            }

            _core.BeginExecuteWindow(seconds);
            Publish();
        }

        public void NotifyPatrolPointReached() => _core.NotifyPatrolPointReached();

        public bool ConsumeDeathCompleted() => !_disposed && _core.ConsumeDeathCompleted();

        public IDisposable Subscribe(IObserver<MonsterPresentationState> observer) =>
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

        private void Publish(in MonsterFrameOutput output)
        {
            if (_disposed)
            {
                return;
            }

            _lastPublished = new MonsterPresentationState(
                _core.Tuning.MonsterId,
                _core.Tuning.DisplayName,
                !_core.IsDead,
                _core.Health,
                _core.MaxHealth,
                _core.Armor,
                _core.MaxArmor,
                _core.Phase,
                _core.Action,
                _core.Intent,
                output.Flags,
                _warningColorTag,
                _core.ExecuteWindowRemaining);
            _state.Set(_lastPublished);
        }

        private void Publish()
        {
            if (_disposed)
            {
                return;
            }

            _lastPublished = MonsterPresentationState.FromCore(_core, _warningColorTag);
            _state.Set(_lastPublished);
        }
    }
}
