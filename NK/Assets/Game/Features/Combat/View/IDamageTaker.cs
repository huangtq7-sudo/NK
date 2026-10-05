using Naraka.Features.Combat.Controller;
using Naraka.Features.Combat.Model;

namespace Naraka.Features.Combat.View
{
    /// <summary>
    /// 可以吃到一次命中的场景对象。玩家与怪物都实现它，
    /// 因此同一套命中盒既能打怪也能打玩家，不需要两份扫描实现。
    ///
    /// 它只是 View 层的转发入口：防御、护甲、霸体、重生保护、反击与死亡
    /// 全部由各自的 Controller/Model 判定，实现类不得自己算伤害。
    /// </summary>
    public interface IDamageTaker
    {
        /// <summary>目标稳定标识。使用 GetInstanceID，绝不使用场景对象列表下标。</summary>
        int TargetId { get; }

        Faction Faction { get; }

        bool IsAlive { get; }

        /// <summary>施加一次命中并返回真正发生的护甲/生命变化。</summary>
        DamageApplication TakeDamage(in HitRequest request);
    }
}
