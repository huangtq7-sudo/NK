using System;

namespace Naraka.Features.AI.Model
{
    /// <summary>一个节点这次 Tick 的结果。</summary>
    public enum BehaviorStatus
    {
        /// <summary>条件不成立或动作无法执行。</summary>
        Failure = 0,

        /// <summary>动作已经选定。</summary>
        Success = 1,

        /// <summary>动作仍在进行，本次决策到此为止。</summary>
        Running = 2
    }

    /// <summary>
    /// 行为树节点。
    ///
    /// 它对上下文类型是泛型的，因此这一层不认识怪物、玩家或任何 Unity 类型 ——
    /// 具体的黑板由使用方提供。节点在构造时建好一次，
    /// 运行期只调用 <see cref="Tick"/>，**不在决策时创建任何节点或集合**。
    /// </summary>
    public abstract class BehaviorNode<TContext>
    {
        /// <summary>节点名。只用于调试显示与测试断言，不参与任何判定。</summary>
        public string Name { get; protected set; } = string.Empty;

        public abstract BehaviorStatus Tick(TContext context);
    }

    /// <summary>
    /// 选择节点：按顺序尝试子节点，第一个不是 Failure 的结果就是本节点的结果。
    /// 优先级完全由子节点的顺序表达，没有隐藏的权重。
    /// </summary>
    public sealed class SelectorNode<TContext> : BehaviorNode<TContext>
    {
        private readonly BehaviorNode<TContext>[] _children;

        public SelectorNode(string name, params BehaviorNode<TContext>[] children)
        {
            Name = name ?? string.Empty;
            _children = children ?? Array.Empty<BehaviorNode<TContext>>();
        }

        public override BehaviorStatus Tick(TContext context)
        {
            for (var i = 0; i < _children.Length; i++)
            {
                var status = _children[i].Tick(context);
                if (status != BehaviorStatus.Failure)
                {
                    return status;
                }
            }

            return BehaviorStatus.Failure;
        }
    }

    /// <summary>
    /// 序列节点：全部子节点都成功才算成功，任何一个失败立即返回失败。
    /// 用来表达"条件成立 → 才执行动作"。
    /// </summary>
    public sealed class SequenceNode<TContext> : BehaviorNode<TContext>
    {
        private readonly BehaviorNode<TContext>[] _children;

        public SequenceNode(string name, params BehaviorNode<TContext>[] children)
        {
            Name = name ?? string.Empty;
            _children = children ?? Array.Empty<BehaviorNode<TContext>>();
        }

        public override BehaviorStatus Tick(TContext context)
        {
            for (var i = 0; i < _children.Length; i++)
            {
                var status = _children[i].Tick(context);
                if (status != BehaviorStatus.Success)
                {
                    return status;
                }
            }

            return BehaviorStatus.Success;
        }
    }

    /// <summary>条件节点：委托返回真为成功，假为失败。它从不产生 Running。</summary>
    public sealed class ConditionNode<TContext> : BehaviorNode<TContext>
    {
        private readonly Func<TContext, bool> _predicate;

        public ConditionNode(string name, Func<TContext, bool> predicate)
        {
            Name = name ?? string.Empty;
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        public override BehaviorStatus Tick(TContext context) =>
            _predicate(context) ? BehaviorStatus.Success : BehaviorStatus.Failure;
    }

    /// <summary>
    /// 动作节点：委托直接返回状态。行为树只在这里"选出意图"，
    /// 真正执行由使用方的状态机负责 —— 两边不能同时保存"当前动作"。
    /// </summary>
    public sealed class ActionNode<TContext> : BehaviorNode<TContext>
    {
        private readonly Func<TContext, BehaviorStatus> _action;

        public ActionNode(string name, Func<TContext, BehaviorStatus> action)
        {
            Name = name ?? string.Empty;
            _action = action ?? throw new ArgumentNullException(nameof(action));
        }

        public override BehaviorStatus Tick(TContext context) => _action(context);
    }

    /// <summary>
    /// 行为树本体。它按固定频率决策，而不是每帧决策：
    /// 每帧跑一次行为树既没有意义（怪物的意图不会一帧一变），也浪费预算。
    /// </summary>
    public sealed class BehaviorTree<TContext>
    {
        private readonly BehaviorNode<TContext> _root;
        private float _sinceLastDecision;

        public BehaviorTree(BehaviorNode<TContext> root, float decisionIntervalSeconds)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            DecisionIntervalSeconds = decisionIntervalSeconds <= 0f ? 0.2f : decisionIntervalSeconds;
            // 第一次 Tick 立刻决策：刚生成的怪物不该先发呆一个决策周期。
            _sinceLastDecision = DecisionIntervalSeconds;
        }

        /// <summary>两次决策之间的间隔。5–10Hz 对应 0.2–0.1 秒。</summary>
        public float DecisionIntervalSeconds { get; private set; }

        /// <summary>本树已经决策过多少次。测试用它断言决策频率，而不是去数帧。</summary>
        public int DecisionCount { get; private set; }

        public BehaviorStatus LastStatus { get; private set; } = BehaviorStatus.Failure;

        /// <summary>
        /// 改变决策间隔。休眠的怪物把间隔拉长，因此"远离玩家就降低决策频率"
        /// 是一条真实生效的规则，而不是注释里的承诺。
        /// </summary>
        public void SetDecisionInterval(float seconds)
        {
            if (seconds <= 0f || Math.Abs(seconds - DecisionIntervalSeconds) < 0.0001f)
            {
                return;
            }

            DecisionIntervalSeconds = seconds;
        }

        /// <summary>推进时间；到达决策间隔时跑一次树。返回本帧是否真的决策了。</summary>
        public bool Tick(TContext context, float deltaSeconds)
        {
            if (deltaSeconds > 0f)
            {
                _sinceLastDecision += deltaSeconds;
            }

            if (_sinceLastDecision < DecisionIntervalSeconds)
            {
                return false;
            }

            _sinceLastDecision = 0f;
            DecisionCount++;
            LastStatus = _root.Tick(context);
            return true;
        }

        /// <summary>强制下一次 Tick 立刻决策。用于生成、复活或阶段切换。</summary>
        public void RequestImmediateDecision() => _sinceLastDecision = DecisionIntervalSeconds;
    }
}
