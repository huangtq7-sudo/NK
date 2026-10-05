using System;
using Naraka.Features.Combat.Model;

namespace Naraka.Features.Combat.Controller
{
    /// <summary>已经发生的命中事实。跨模块订阅方通过 IDomainEventBus 收到它。</summary>
    public readonly struct HitLandedEvent
    {
        public HitLandedEvent(
            HitId hitId,
            int targetId,
            Faction targetFaction,
            float armorLost,
            float healthLost,
            bool killed)
        {
            HitId = hitId;
            TargetId = targetId;
            TargetFaction = targetFaction;
            ArmorLost = armorLost;
            HealthLost = healthLost;
            Killed = killed;
        }

        public HitId HitId { get; }

        public int TargetId { get; }

        public Faction TargetFaction { get; }

        public float ArmorLost { get; }

        public float HealthLost { get; }

        /// <summary>本次命中实际移除的总量，护甲与生命之和。</summary>
        public float Damage => ArmorLost + HealthLost;

        public bool Killed { get; }
    }

    /// <summary>目标侧真正发生的扣血结果。</summary>
    public readonly struct DamageApplication
    {
        public DamageApplication(float armorLost, float healthLost, bool killed, bool countered = false)
        {
            ArmorLost = armorLost;
            HealthLost = healthLost;
            Killed = killed;
            Countered = countered;
        }

        public float ArmorLost { get; }

        public float HealthLost { get; }

        public bool Killed { get; }

        /// <summary>这一刀被目标反击掉了，不产生任何伤害。</summary>
        public bool Countered { get; }

        /// <summary>实际移除的总量，护甲与生命之和。</summary>
        public float Total => ArmorLost + HealthLost;

        /// <summary>被反击。</summary>
        public static DamageApplication AsCountered() => new DamageApplication(0f, 0f, false, true);
    }

    /// <summary>
    /// 把伤害施加到目标上。由目标自己实现：防御、护甲、霸体、重生保护与反击
    /// 都是受击方的属性，命中层只负责校验、去重与转发。
    /// </summary>
    public delegate DamageApplication ApplyDamageDelegate(in HitRequest request);

    /// <summary>
    /// 灰盒命中结算。View 侧的命中盒只负责"扫到了谁"，能不能算、算多少、算几次由这里决定。
    /// Animator Event 可以通知表现时间点，但绝不能替代这一层。
    /// </summary>
    public interface IHitResolver
    {
        /// <summary>
        /// 已经生效的命中。HUD 与音效订阅它来做受击反馈；
        /// 它是"已经发生的事实"，订阅者不得据此再改任何业务状态。
        /// </summary>
        event Action<HitLandedEvent> HitLanded;

        HitResolution Resolve(in HitRequest request, ApplyDamageDelegate applyDamage);

        void ReleaseAttack(HitId hitId);

        void Clear();
    }

    public sealed class HitResolver : IHitResolver
    {
        private readonly HitRegistry _registry = new HitRegistry();

        public event Action<HitLandedEvent> HitLanded;

        /// <summary>
        /// 校验并结算一次命中。<paramref name="applyDamage"/> 由调用方提供，
        /// 返回目标身上真正发生的护甲/生命变化。
        /// </summary>
        public HitResolution Resolve(in HitRequest request, ApplyDamageDelegate applyDamage)
        {
            if (applyDamage == null)
            {
                throw new ArgumentNullException(nameof(applyDamage));
            }

            if (!request.HitId.IsValid)
            {
                return HitResolution.Rejected(HitRejection.InvalidHitId);
            }

            if (request.RawDamage <= 0f)
            {
                return HitResolution.Rejected(HitRejection.NonPositiveDamage);
            }

            if (request.Attacker == request.TargetFaction)
            {
                return HitResolution.Rejected(HitRejection.SameFaction);
            }

            // 先登记再扣血：登记失败说明这一刀已经打过这个目标，绝不能重复结算。
            if (!_registry.TryRegister(request.HitId, request.TargetId))
            {
                return HitResolution.Rejected(HitRejection.AlreadyHitByThisAttack);
            }

            var applied = applyDamage(in request);
            if (applied.Countered)
            {
                // 被反击的一刀仍然占着去重记录：它已经"打出去"了，不该再扫一次。
                return HitResolution.Rejected(HitRejection.Countered);
            }

            if (applied.ArmorLost <= 0f && applied.HealthLost <= 0f)
            {
                // 护甲和生命都没有扣掉就不算命中。目标可能刚刚被同一帧的另一次攻击打死，
                // 也可能处在重生保护里；此时哪怕它报告 Killed 也不该再产生一次命中事件。
                return HitResolution.Rejected(HitRejection.TargetAlreadyDead);
            }

            var resolution = new HitResolution(
                true, HitRejection.None, applied.ArmorLost, applied.HealthLost, applied.Killed);
            HitLanded?.Invoke(new HitLandedEvent(
                request.HitId,
                request.TargetId,
                request.TargetFaction,
                applied.ArmorLost,
                applied.HealthLost,
                applied.Killed));
            return resolution;
        }

        public void ReleaseAttack(HitId hitId) => _registry.Release(hitId);

        public void Clear() => _registry.Clear();
    }
}
