namespace Naraka.Features.Combat.Model
{
    /// <summary>
    /// 可受击目标的纯逻辑生命值。玩家自己的生命/护甲由 Character 模块的
    /// <c>PlayerVitals</c> 保管，这里只服务灰盒训练假人等战斗侧目标。
    /// </summary>
    public sealed class DamageableModel
    {
        private readonly float _maxHealth;
        private float _health;

        public DamageableModel(float maxHealth)
        {
            _maxHealth = maxHealth <= 0f ? 1f : maxHealth;
            _health = _maxHealth;
        }

        public float Health => _health;

        public float MaxHealth => _maxHealth;

        public float HealthRatio => _health / _maxHealth;

        public bool IsDead => _health <= 0f;

        /// <summary>返回本次真正扣掉的生命值。已经死亡时返回 0。</summary>
        public float ApplyDamage(float amount)
        {
            if (amount <= 0f || IsDead)
            {
                return 0f;
            }

            var lost = amount < _health ? amount : _health;
            _health -= lost;
            if (_health < 0f)
            {
                _health = 0f;
            }

            return lost;
        }

        public void Reset() => _health = _maxHealth;
    }
}
