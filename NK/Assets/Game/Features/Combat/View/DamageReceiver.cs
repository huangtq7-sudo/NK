using System;
using Naraka.Features.Combat.Controller;
using Naraka.Features.Combat.Model;
using UnityEngine;

namespace Naraka.Features.Combat.View
{
    /// <summary>
    /// 受击盒。挂在可受击目标上，命中盒扫到它之后由 Controller 层决定能否结算。
    /// 它自己不计算伤害，也不决定阵营规则。
    /// </summary>
    public abstract class DamageReceiver : MonoBehaviour
    {
        [SerializeField] private Faction faction = Faction.Enemy;

        /// <summary>目标稳定标识。使用 GetInstanceID，绝不使用场景对象列表下标。</summary>
        public int TargetId => gameObject.GetInstanceID();

        public Faction Faction => faction;

        public abstract bool IsAlive { get; }

        /// <summary>施加伤害并返回真正发生的扣血结果。</summary>
        public abstract DamageApplication Apply(float amount);

        /// <summary>命中已被判定生效时的表现回调。</summary>
        public event Action<float> DamageDisplayed;

        protected void RaiseDamageDisplayed(float amount) => DamageDisplayed?.Invoke(amount);
    }
}
