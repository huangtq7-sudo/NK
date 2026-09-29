using System;
using Naraka.Features.Combat.Model;

namespace Naraka.Features.Combat.Controller
{
    /// <summary>已经发生的命中事实。跨模块订阅方通过 IDomainEventBus 收到它。</summary>
    public readonly struct HitLandedEvent
    {
        public HitLandedEvent(HitId hitId, int targetId, float damage, bool killed)
        {
            HitId = hitId;
            TargetId = targetId;
            Damage = damage;
            Killed = killed;
        }

        public HitId HitId { get; }

        public int TargetId { get; }

        public float Damage { get; }

        public bool Killed { get; }
    }

    /// <summary>目标侧真正发生的扣血结果。</summary>
    public readonly struct DamageApplication
    {
        public DamageApplication(float healthLost, bool killed)
        {
            HealthLost = healthLost;
            Killed = killed;
        }

        public float HealthLost { get; }

        public bool Killed { get; }
    }

    /// <summary>把伤害施加到目标上。由目标自己实现，命中层只负责校验与去重。</summary>
    public delegate DamageApplication ApplyDamageDelegate(float amount);

    /// <summary>
    /// 灰盒命中结算。View 侧的命中盒只负责"扫到了谁"，能不能算、算多少、算几次由这里决定。
    /// Animator Event 可以通知表现时间点，但绝不能替代这一层。
    /// </summary>
    public interface IHitResolver
    {
        HitResolution Resolve(HitRequest request, ApplyDamageDelegate applyDamage);

        void ReleaseAttack(HitId hitId);

        void Clear();
    }

    public sealed class HitResolver : IHitResolver
    {
        private readonly HitRegistry _registry = new HitRegistry();

        public event Action<HitLandedEvent> HitLanded;

        /// <summary>
        /// 校验并结算一次命中。<paramref name="applyDamage"/> 由调用方提供，
        /// 返回真正扣掉的生命值；返回 0 视为目标已经死亡。
        /// </summary>
        public HitResolution Resolve(HitRequest request, ApplyDamageDelegate applyDamage)
        {
            if (applyDamage == null)
            {
                throw new ArgumentNullException(nameof(applyDamage));
            }

            if (!request.HitId.IsValid)
            {
                return HitResolution.Rejected(HitRejection.InvalidHitId);
            }

            if (request.Damage <= 0f)
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

            var applied = applyDamage(request.Damage);
            if (applied.HealthLost <= 0f)
            {
                // 一点血都没扣掉就不算命中。目标可能刚刚被同一帧的另一次攻击打死，
                // 此时哪怕它报告 Killed 也不该再产生一次命中事件。
                return HitResolution.Rejected(HitRejection.TargetAlreadyDead);
            }

            var resolution = new HitResolution(
                true, HitRejection.None, 0f, applied.HealthLost, applied.Killed);
            HitLanded?.Invoke(new HitLandedEvent(
                request.HitId, request.TargetId, applied.HealthLost, applied.Killed));
            return resolution;
        }

        public void ReleaseAttack(HitId hitId) => _registry.Release(hitId);

        public void Clear() => _registry.Clear();
    }
}
