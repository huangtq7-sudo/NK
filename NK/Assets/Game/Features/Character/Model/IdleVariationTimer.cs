namespace Naraka.Features.Character.Model
{
    /// <summary>
    /// 长时间待机计时。连续无角色操作达到 <see cref="IdleTuning.VariationDelaySeconds"/> 秒后
    /// 播放一次待机动作，播完回到默认 Idle 并重新计时。
    ///
    /// 只有"角色操作"才重置计时：移动、攻击、技能、冲刺、受击、死亡与场景切换。
    /// 单纯转动摄像机不算角色操作，因此摄像机输入根本不会传到这里。
    /// </summary>
    public sealed class IdleVariationTimer
    {
        private readonly IdleTuning _tuning;
        private float _idleSeconds;

        public IdleVariationTimer(IdleTuning tuning)
        {
            _tuning = tuning;
        }

        public float IdleSeconds => _idleSeconds;

        public float DelaySeconds => _tuning.VariationDelaySeconds;

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds > 0f)
            {
                _idleSeconds += deltaSeconds;
            }
        }

        public bool ShouldPlayVariation => _idleSeconds >= _tuning.VariationDelaySeconds;

        /// <summary>发生了角色操作，重新开始计时。</summary>
        public void Reset()
        {
            _idleSeconds = 0f;
        }
    }
}
