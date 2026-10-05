namespace Naraka.Features.Combat.Model
{
    /// <summary>一次伤害在目标身上真正发生的变化。</summary>
    public readonly struct DamageableResult
    {
        public DamageableResult(float armorLost, float healthLost, bool died)
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
    /// 可受击目标的纯逻辑生命值、护甲与防御。
    ///
    /// 玩家自己的生命/护甲由 Character 模块的 <c>PlayerVitals</c> 保管，怪物由
    /// <c>MonsterVitals</c> 保管；这里服务灰盒训练假人与反击训练靶这类战斗侧目标。
    /// 三者共用 <see cref="DamageFormula"/> 与 <see cref="ArmorAbsorption"/>，
    /// 因此"先扣护甲、溢出扣生命"只有一份实现。
    /// </summary>
    public sealed class DamageableModel
    {
        private readonly float _maxHealth;
        private readonly float _maxArmor;
        private readonly float _defense;
        private float _health;
        private float _armor;

        public DamageableModel(float maxHealth, float maxArmor = 0f, float defense = 0f)
        {
            _maxHealth = maxHealth <= 0f ? 1f : maxHealth;
            _maxArmor = maxArmor < 0f ? 0f : maxArmor;
            _defense = defense < 0f ? 0f : defense;
            _health = _maxHealth;
            _armor = _maxArmor;
        }

        public float Health => _health;

        public float MaxHealth => _maxHealth;

        public float Armor => _armor;

        public float MaxArmor => _maxArmor;

        public float Defense => _defense;

        public float HealthRatio => _health / _maxHealth;

        public bool IsDead => _health <= 0f;

        /// <summary>
        /// 施加一次<b>扣防御之前</b>的伤害。返回护甲与生命各自真正减少的量。
        /// 已经死亡时什么都不做：死亡只允许发生一次。
        /// </summary>
        public DamageableResult ApplyRawDamage(float rawDamage)
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

            return new DamageableResult(absorption.ArmorLost, absorption.HealthLost, _health <= 0f);
        }

        public void Reset()
        {
            _health = _maxHealth;
            _armor = _maxArmor;
        }
    }
}
