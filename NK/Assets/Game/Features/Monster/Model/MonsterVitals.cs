using Naraka.Features.Combat.Model;

namespace Naraka.Features.Monster.Model
{
    /// <summary>一次伤害在怪物身上真正发生的变化。</summary>
    public readonly struct MonsterDamageResult
    {
        public MonsterDamageResult(float armorLost, float healthLost, bool died)
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
    /// 怪物的生命、护甲与防御。与玩家共用
    /// <see cref="DamageFormula"/> 和 <see cref="ArmorAbsorption"/>：
    /// 玩家打怪与怪打玩家必须是同一条公式，否则数值表根本没法对。
    /// </summary>
    public sealed class MonsterVitals
    {
        private readonly float _maxHealth;
        private readonly float _maxArmor;
        private readonly float _defense;
        private float _health;
        private float _armor;

        public MonsterVitals(in MonsterTuning tuning)
        {
            _maxHealth = tuning.MaxHealth;
            _maxArmor = tuning.MaxArmor;
            _defense = tuning.Defense;
            _health = _maxHealth;
            _armor = _maxArmor;
        }

        public float Health => _health;

        public float MaxHealth => _maxHealth;

        public float Armor => _armor;

        public float MaxArmor => _maxArmor;

        public float Defense => _defense;

        public float HealthRatio => _maxHealth <= 0f ? 0f : _health / _maxHealth;

        public bool IsDead => _health <= 0f;

        /// <summary>施加一次扣防御之前的伤害。霸体不在这里判定：它只影响硬直。</summary>
        public MonsterDamageResult ApplyRawDamage(float rawDamage)
        {
            if (rawDamage <= 0f || IsDead)
            {
                return default;
            }

            var afterDefense = DamageFormula.AfterDefense(rawDamage, _defense);
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

            return new MonsterDamageResult(absorption.ArmorLost, absorption.HealthLost, _health <= 0f);
        }

        public void Reset()
        {
            _health = _maxHealth;
            _armor = _maxArmor;
        }
    }
}
