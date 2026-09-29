namespace Naraka.Features.Character.Model
{
    /// <summary>
    /// 三段连招计数与输入缓存。
    ///
    /// 输入提交时机：按本轮确认，普通攻击在"鼠标左键释放且按住时长不足蓄力阈值"时提交一次。
    /// 这样"一次点击只能推进一段"，也不会出现既打出普通第一段又进入蓄力的双触发。
    /// </summary>
    public sealed class ComboModel
    {
        private readonly ComboTuning _tuning;
        private float _bufferRemaining;
        private float _idleSinceLastAttack;

        public ComboModel(ComboTuning tuning)
        {
            _tuning = tuning;
        }

        /// <summary>已经打出的段数：0 表示尚未起手，1/2/3 对应三段。</summary>
        public int CompletedStep { get; private set; }

        public bool HasBufferedInput => _bufferRemaining > 0f;

        /// <summary>缓存一次攻击输入。重复按只刷新缓存时长，不会累积成多段。</summary>
        public void BufferAttack()
        {
            _bufferRemaining = _tuning.InputBufferSeconds;
        }

        /// <summary>
        /// 推进缓存与重置计时。<paramref name="attackInProgress"/> 为真时不累计重置计时：
        /// 第三段本身就比 0.8 秒长，动作播放期间计时会让连招在自己播放途中被重置。
        /// </summary>
        public void Tick(float deltaSeconds, bool attackInProgress)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            if (_bufferRemaining > 0f)
            {
                _bufferRemaining -= deltaSeconds;
                if (_bufferRemaining < 0f)
                {
                    _bufferRemaining = 0f;
                }
            }

            if (attackInProgress || CompletedStep <= 0)
            {
                return;
            }

            _idleSinceLastAttack += deltaSeconds;
            if (_idleSinceLastAttack >= _tuning.ResetSeconds)
            {
                Reset();
            }
        }

        /// <summary>
        /// 消费一次缓存输入并推进一段。返回新的段号；没有缓存输入时返回 0 且不推进。
        /// </summary>
        public int TryAdvance()
        {
            if (_bufferRemaining <= 0f)
            {
                return 0;
            }

            // 一次缓存只能兑换一段：消费后立刻清空，帧率变化不会让同一次输入被重复消费。
            _bufferRemaining = 0f;
            _idleSinceLastAttack = 0f;
            CompletedStep = CompletedStep >= 3 ? 1 : CompletedStep + 1;
            return CompletedStep;
        }

        /// <summary>某一段动作播完。第三段结束后直接回到第一段。</summary>
        public void NotifyStepFinished()
        {
            _idleSinceLastAttack = 0f;
            if (CompletedStep >= 3)
            {
                Reset();
            }
        }

        public void Reset()
        {
            CompletedStep = 0;
            _idleSinceLastAttack = 0f;
        }

        /// <summary>清空缓存但保留段数。用于受击、死亡等打断场合。</summary>
        public void ClearBuffer()
        {
            _bufferRemaining = 0f;
        }

        public TimedActionTuning CurrentAction => _tuning.Step(CompletedStep <= 0 ? 1 : CompletedStep);
    }
}
