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
    }

    /// <summary>
    /// 生命与护甲。伤害优先扣除护甲耐久，溢出部分再扣生命；护甲归零不销毁。
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

        public bool IsDead => _health <= 0f;

        public VitalsDamageResult ApplyDamage(float amount)
        {
            // 已经死亡就不再扣血：死亡只允许发生一次，不能靠重复受击反复触发返回流程。
            if (amount <= 0f || IsDead)
            {
                return default;
            }

            var armorLost = amount < _armor ? amount : _armor;
            _armor -= armorLost;
            var overflow = amount - armorLost;
            var healthLost = overflow < _health ? overflow : _health;
            _health -= healthLost;
            if (_health < 0f)
            {
                _health = 0f;
            }

            return new VitalsDamageResult(armorLost, healthLost, _health <= 0f);
        }

        /// <summary>重生：生命恢复 100%，护甲按配置比例恢复。</summary>
        public void Revive()
        {
            _health = _tuning.MaxHealth;
            _armor = _tuning.MaxArmor * _tuning.ReviveArmorRatio;
        }
    }
}
