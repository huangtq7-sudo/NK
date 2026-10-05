using Naraka.Features.Combat.Model;

namespace Naraka.Features.Character.Model
{
    /// <summary>一次伤害结算的结果。</summary>
    public readonly struct VitalsDamageResult
    {
        public VitalsDamageResult(float armorLost, float healthLost, bool died)
        {
            ArmorLost = armorLost;
            HealthLost = healthLost;
            Died = died;
        }

        public float ArmorLost { get; }

        public float HealthLost { get; }

        /// <summary>这一次伤害是否把生命打到 0。已经死亡时重复受伤不会再次返回 true。</summary>
        public bool Died { get; }

        public float Total => ArmorLost + HealthLost;
    }

    /// <summary>
    /// 生命、护甲与防御。
    ///
    /// 传进来的是<b>扣防御之前</b>的伤害，因为防御是受击方的属性：
    /// <c>AfterDefense = RawDamage × 100 / (100 + Defense)</c>。
    /// 随后伤害优先扣除护甲耐久，溢出部分再扣生命；护甲归零不销毁。
    /// 公式与吸收规则与怪物共用 <see cref="DamageFormula"/>，不存在第二份实现。
    /// </summary>
    public sealed class PlayerVitals
    {
        private readonly VitalsTuning _tuning;
        private float _health;
        private float _armor;

        public PlayerVitals(VitalsTuning tuning)
        {
            _tuning = tuning;
            _health = tuning.MaxHealth;
            _armor = tuning.MaxArmor;
        }

        public float Health => _health;

        public float MaxHealth => _tuning.MaxHealth;

        public float Armor => _armor;

        public float MaxArmor => _tuning.MaxArmor;

        public float Defense => _tuning.Defense;

        public bool IsDead => _health <= 0f;

        /// <summary>施加一次扣防御之前的伤害。</summary>
        public VitalsDamageResult ApplyRawDamage(float rawDamage)
        {
            // 已经死亡就不再扣血：死亡只允许发生一次，不能靠重复受击反复触发返回流程。
            if (rawDamage <= 0f || IsDead)
            {
                return default;
            }

            var afterDefense = DamageFormula.AfterDefense(rawDamage, _tuning.Defense);
            var absorption = ArmorAbsorption.Absorb(afterDefense, _armor, _health);
            _armor -= absorption.ArmorLost;
            if (_armor < 0f)
            {
                _armor = 0f;
            }

            _health -= absorption.HealthLost;
            if (_health < 0f)
            {
                _health = 0f;
            }

            return new VitalsDamageResult(absorption.ArmorLost, absorption.HealthLost, _health <= 0f);
        }

        /// <summary>重生：生命恢复 100%，护甲按配置比例恢复。</summary>
        public void Revive()
        {
            _health = _tuning.MaxHealth;
            _armor = _tuning.MaxArmor * _tuning.ReviveArmorRatio;
        }
    }
}
