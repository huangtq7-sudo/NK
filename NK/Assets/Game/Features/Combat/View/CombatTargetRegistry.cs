using UnityEngine;

namespace Naraka.Features.Combat.View
{
    /// <summary>
    /// 战斗中的一个参与者，供其他参与者定位。
    ///
    /// 怪物需要知道玩家在哪，但 <c>FindObjectOfType</c> 找不到接口，
    /// 而让怪物模块直接引用玩家 View 会把两个 View 绑死。
    /// 因此玩家在生成时把自己登记进来，怪物只认识这个接口。
    /// </summary>
    public interface ICombatTarget
    {
        int TargetId { get; }

        Transform Transform { get; }

        bool IsAlive { get; }

        /// <summary>
        /// 被这个参与者反击成功之后，攻击者应该进入多久的处决窗口。
        ///
        /// 规则属于反击方（玩家）的配置，因此由它给出而不是由怪物自己编一个时长。
        /// </summary>
        float CounterExecuteWindowSeconds { get; }
    }

    /// <summary>
    /// 战斗参与者登记表。本阶段只有一个玩家，因此只登记玩家一侧；
    /// 后续要做多人或宠物时在同一接口上扩展，不需要改怪物模块。
    /// </summary>
    public interface ICombatTargetRegistry
    {
        /// <summary>当前玩家。没有玩家时为 null。</summary>
        ICombatTarget Player { get; }

        void SetPlayer(ICombatTarget player);

        /// <summary>只有当前登记的那个玩家才能注销自己，避免旧实例把新实例清掉。</summary>
        void ClearPlayer(ICombatTarget player);
    }

    /// <inheritdoc />
    public sealed class CombatTargetRegistry : ICombatTargetRegistry
    {
        public ICombatTarget Player { get; private set; }

        public void SetPlayer(ICombatTarget player) => Player = player;

        public void ClearPlayer(ICombatTarget player)
        {
            if (Player == player)
            {
                Player = null;
            }
        }
    }
}
