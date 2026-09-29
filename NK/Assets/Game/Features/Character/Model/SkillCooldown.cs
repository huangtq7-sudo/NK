namespace Naraka.Features.Character.Model
{
    /// <summary>
    /// 技能冷却。冷却完全由业务状态推进，绝不读取 Animator 的播放进度。
    /// </summary>
    public sealed class SkillCooldown
    {
        private readonly float _cooldownSeconds;
        private float _remaining;

        public SkillCooldown(float cooldownSeconds)
        {
            _cooldownSeconds = cooldownSeconds;
        }

        public float CooldownSeconds => _cooldownSeconds;

        public float Remaining => _remaining;

        public bool IsReady => _remaining <= 0f;

        /// <summary>0（刚释放）到 1（已就绪）的归一化进度，供 HUD 画冷却圈。</summary>
        public float NormalizedReady =>
            _cooldownSeconds <= 0f ? 1f : 1f - (_remaining / _cooldownSeconds);

        public bool TryUse()
        {
            if (!IsReady)
            {
                return false;
            }

            _remaining = _cooldownSeconds;
            return true;
        }

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || _remaining <= 0f)
            {
                return;
            }

            _remaining -= deltaSeconds;
            if (_remaining < 0f)
            {
                _remaining = 0f;
            }
        }

        /// <summary>重生：冷却清零。</summary>
        public void Reset()
        {
            _remaining = 0f;
        }
    }
}
