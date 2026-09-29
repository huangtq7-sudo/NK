using System;
using System.Collections.Generic;

namespace Naraka.Features.Character.Model.Hfsm
{
    /// <summary>
    /// 状态切换的原因。每次切换都必须给出原因，避免出现"不知道为什么会跳过去"的转换。
    /// </summary>
    public enum StateChangeReason
    {
        Initial = 0,
        MoveInput = 1,
        MoveInputReleased = 2,
        SprintTap = 3,
        SprintHold = 4,
        DirectionReversed = 5,
        AttackInput = 6,
        ChargeThreshold = 7,
        SkillInput = 8,
        ActionCompleted = 9,
        Damaged = 10,
        Died = 11,
        SceneSpawn = 12,
        InputLocked = 13,
        IdleTimeout = 14,
        Respawn = 15
    }

    /// <summary>
    /// 一次已经发生的状态切换。测试与调试显示都只读这个记录，不去猜 Animator。
    /// </summary>
    public readonly struct StateChange<TState>
        where TState : struct, Enum
    {
        public StateChange(TState from, TState to, StateChangeReason reason)
        {
            From = from;
            To = to;
            Reason = reason;
        }

        public TState From { get; }

        public TState To { get; }

        public StateChangeReason Reason { get; }
    }

    /// <summary>
    /// 项目自有的轻量状态机层。分层状态机由三台这样的机器（Locomotion/Action/Reaction）
    /// 加一个集中的优先级仲裁组成，因此这里只负责单层的生命周期，不负责跨层规则。
    ///
    /// 约束：
    /// - 不引用 UnityEngine，可在 EditMode 中作为纯逻辑测试；
    /// - 状态标识是枚举，运行期不做字符串查找也不做反射；
    /// - 每个状态有明确的 Enter / Tick / Exit，切换必须带原因。
    /// </summary>
    public sealed class StateMachine<TState>
        where TState : struct, Enum
    {
        private readonly Dictionary<TState, Handlers> _handlers = new Dictionary<TState, Handlers>();
        private StateChange<TState> _lastChange;

        public StateMachine(TState initial)
        {
            Current = initial;
            _lastChange = new StateChange<TState>(initial, initial, StateChangeReason.Initial);
        }

        public TState Current { get; private set; }

        /// <summary>当前状态已经持续的秒数。进入新状态时归零。</summary>
        public float TimeInState { get; private set; }

        /// <summary>本层最近一次切换。没有发生过切换时 From 与 To 相同。</summary>
        public StateChange<TState> LastChange => _lastChange;

        public void Configure(
            TState state,
            Action onEnter = null,
            Action<float> onTick = null,
            Action onExit = null)
        {
            _handlers[state] = new Handlers(onEnter, onTick, onExit);
        }

        /// <summary>
        /// 切换到目标状态。已经处于目标状态时不重入，返回 false —— 这样"每帧都想进 Idle"
        /// 不会把 Idle 的无操作计时反复清零。需要强制重入的场合使用 <see cref="Restart"/>。
        /// </summary>
        public bool TryChangeTo(TState next, StateChangeReason reason)
        {
            if (EqualityComparer<TState>.Default.Equals(Current, next))
            {
                return false;
            }

            Restart(next, reason);
            return true;
        }

        /// <summary>强制重新进入状态，即使目标与当前相同。用于同一状态需要重播表现的场合。</summary>
        public void Restart(TState next, StateChangeReason reason)
        {
            var previous = Current;
            if (_handlers.TryGetValue(previous, out var previousHandlers))
            {
                previousHandlers.OnExit?.Invoke();
            }

            Current = next;
            TimeInState = 0f;
            _lastChange = new StateChange<TState>(previous, next, reason);

            if (_handlers.TryGetValue(next, out var nextHandlers))
            {
                nextHandlers.OnEnter?.Invoke();
            }
        }

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            }

            TimeInState += deltaSeconds;
            if (_handlers.TryGetValue(Current, out var handlers))
            {
                handlers.OnTick?.Invoke(deltaSeconds);
            }
        }

        private readonly struct Handlers
        {
            public Handlers(Action onEnter, Action<float> onTick, Action onExit)
            {
                OnEnter = onEnter;
                OnTick = onTick;
                OnExit = onExit;
            }

            public Action OnEnter { get; }

            public Action<float> OnTick { get; }

            public Action OnExit { get; }
        }
    }
}
