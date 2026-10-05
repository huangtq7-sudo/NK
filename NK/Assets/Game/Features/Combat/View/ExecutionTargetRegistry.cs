using System.Collections.Generic;
using UnityEngine;

namespace Naraka.Features.Combat.View
{
    /// <summary>
    /// 一个可以被处决的目标。反击成功之后目标进入处决窗口，窗口期内它是可处决的。
    /// </summary>
    public interface IExecutionTarget
    {
        int TargetId { get; }

        /// <summary>当前是否处在处决窗口里。</summary>
        bool IsExecutable { get; }

        Vector3 Position { get; }
    }

    /// <summary>
    /// 处决目标登记表。
    ///
    /// 玩家状态机需要知道"附近有没有可处决的目标"，但它不认识场景对象；
    /// 而每帧做一次物理查询只为了回答这个问题又太浪费。因此由目标自己在
    /// 进入/离开处决窗口时登记，玩家只在登记表非空时做一次距离比较。
    ///
    /// 它不持有任何业务规则：处决窗口时长、伤害倍率与无敌都在 Model 层。
    /// </summary>
    public interface IExecutionTargetRegistry
    {
        void Register(IExecutionTarget target);

        void Unregister(IExecutionTarget target);

        /// <summary>登记表里当前有多少目标。为 0 时调用方可以整段跳过查询。</summary>
        int Count { get; }

        bool TryFindExecutable(Vector3 origin, float radius, out IExecutionTarget target);
    }

    /// <inheritdoc />
    public sealed class ExecutionTargetRegistry : IExecutionTargetRegistry
    {
        private readonly List<IExecutionTarget> _targets = new List<IExecutionTarget>(8);

        public int Count => _targets.Count;

        public void Register(IExecutionTarget target)
        {
            if (target == null || _targets.Contains(target))
            {
                return;
            }

            _targets.Add(target);
        }

        public void Unregister(IExecutionTarget target)
        {
            if (target == null)
            {
                return;
            }

            _targets.Remove(target);
        }

        public bool TryFindExecutable(Vector3 origin, float radius, out IExecutionTarget target)
        {
            target = null;
            if (_targets.Count == 0 || radius <= 0f)
            {
                return false;
            }

            var bestSquared = radius * radius;
            // 手写循环而不是 LINQ：这条查询可能每帧执行，不能产生迭代器分配。
            for (var i = 0; i < _targets.Count; i++)
            {
                var candidate = _targets[i];
                if (candidate == null || !candidate.IsExecutable)
                {
                    continue;
                }

                var squared = (candidate.Position - origin).sqrMagnitude;
                if (squared > bestSquared)
                {
                    continue;
                }

                bestSquared = squared;
                target = candidate;
            }

            return target != null;
        }
    }
}
