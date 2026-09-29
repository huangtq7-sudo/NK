namespace Naraka.Features.Character.Model
{
    /// <summary>
    /// 体力规则。本轮确认基线：上限 20、Move_F 消耗 10、每秒恢复 5、
    /// 动作结束 0.75 秒后开始恢复、受击后额外暂停 0.5 秒。
    ///
    /// 体力永远被钳制在 [0, Max]：客户端没有任何路径能把它写成负数或超过上限。
    /// </summary>
    public sealed class StaminaModel
    {
        private readonly StaminaTuning _tuning;
        private float _current;
        private float _regenBlockedSeconds;

        public StaminaModel(StaminaTuning tuning)
        {
            _tuning = tuning;
            _current = tuning.Max;
        }

        public float Current => _current;

        public float Max => _tuning.Max;

        /// <summary>恢复还要等多久。为 0 表示正在恢复。</summary>
        public float RegenBlockedSeconds => _regenBlockedSeconds;

        public bool CanSpendDash => _current >= _tuning.DashCost;

        /// <summary>
        /// 扣除冲刺体力。体力不足时返回 false 且不扣除任何数值 ——
        /// 拒绝与扣除是同一个原子判断，不存在"先扣再回滚"的中间态。
        /// </summary>
        public bool TrySpendDash()
        {
            if (!CanSpendDash)
            {
                return false;
            }

            _current = Clamp(_current - _tuning.DashCost);
            return true;
        }

        /// <summary>冲刺结束。从这一刻起等 RegenDelaySeconds 才开始恢复。</summary>
        public void NotifyDashEnded()
        {
            _regenBlockedSeconds = _tuning.RegenDelaySeconds;
        }

        /// <summary>
        /// 受击。恢复延迟额外累加 HitRegenPauseSeconds，
        /// 而不是覆盖为它 —— 冲刺刚结束就被打不该反而提早恢复。
        /// </summary>
        public void NotifyDamaged()
        {
            _regenBlockedSeconds += _tuning.HitRegenPauseSeconds;
        }

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            if (_regenBlockedSeconds > 0f)
            {
                _regenBlockedSeconds -= deltaSeconds;
                if (_regenBlockedSeconds > 0f)
                {
                    return;
                }

                // 延迟在本帧内用完，剩余时间照常恢复，不整帧丢弃。
                deltaSeconds = -_regenBlockedSeconds;
                _regenBlockedSeconds = 0f;
            }

            _current = Clamp(_current + (_tuning.RegenPerSecond * deltaSeconds));
        }

        /// <summary>重生：体力恢复至最大值，恢复延迟清零。</summary>
        public void Reset()
        {
            _current = _tuning.Max;
            _regenBlockedSeconds = 0f;
        }

        private float Clamp(float value) =>
            value < 0f ? 0f : value > _tuning.Max ? _tuning.Max : value;
    }
}
