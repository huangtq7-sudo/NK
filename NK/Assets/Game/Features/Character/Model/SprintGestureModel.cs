namespace Naraka.Features.Character.Model
{
    /// <summary>Shift 手势的判定结果。</summary>
    public enum SprintGesture
    {
        None = 0,

        /// <summary>点按：按下后不足阈值就释放，触发 Move_F 快速冲刺。</summary>
        Tap = 1,

        /// <summary>长按达到阈值，只在跨过阈值的那一帧上报一次。</summary>
        Hold = 2
    }

    /// <summary>
    /// Shift 点按与长按的边界判定。
    ///
    /// 规则：
    /// - 按下不足阈值就释放 → Tap，触发 Move_F；
    /// - 按住达到阈值 → Hold（只上报一次），之后 <see cref="IsHolding"/> 保持为真；
    /// - 达到阈值后再释放不算点按，因此长按不会在松手时额外触发一次冲刺。
    ///
    /// 是否真的进入 Run 由状态机判断：长按但没有 W/S 输入时不能原地播放跑步，
    /// 这条规则属于状态仲裁，不属于手势判定，所以不写在这里。
    /// </summary>
    public sealed class SprintGestureModel
    {
        private readonly float _tapMaxSeconds;
        private bool _pressed;
        private bool _holdReported;
        private float _heldSeconds;

        public SprintGestureModel(float tapMaxSeconds)
        {
            _tapMaxSeconds = tapMaxSeconds;
        }

        public float HeldSeconds => _heldSeconds;

        /// <summary>长按已经成立且仍在按住。松开 Shift 后立刻变为 false。</summary>
        public bool IsHolding => _pressed && _holdReported;

        /// <summary>推进一帧手势判定。<paramref name="pressed"/> 是本帧 Shift 的按下状态。</summary>
        public SprintGesture Tick(bool pressed, float deltaSeconds)
        {
            if (!_pressed)
            {
                if (!pressed)
                {
                    return SprintGesture.None;
                }

                _pressed = true;
                _holdReported = false;
                _heldSeconds = 0f;
                return SprintGesture.None;
            }

            if (!pressed)
            {
                _pressed = false;
                var wasTap = !_holdReported && _heldSeconds < _tapMaxSeconds;
                _heldSeconds = 0f;
                _holdReported = false;
                return wasTap ? SprintGesture.Tap : SprintGesture.None;
            }

            _heldSeconds += deltaSeconds;
            if (_holdReported || _heldSeconds < _tapMaxSeconds)
            {
                return SprintGesture.None;
            }

            _holdReported = true;
            return SprintGesture.Hold;
        }

        /// <summary>场景切换与重生时清空手势，避免把上一条命的按键状态带过来。</summary>
        public void Reset()
        {
            _pressed = false;
            _holdReported = false;
            _heldSeconds = 0f;
        }
    }
}
