using Naraka.Features.Character.Model;
using UnityEngine;

namespace Naraka.Features.Character.View
{
    /// <summary>
    /// 角色运动适配层。<see cref="CharacterController.Move"/> 只在这里被调用一次，
    /// 因此不存在多个状态同时写位移的情况。
    ///
    /// 玩家主移动不使用动态 Rigidbody，本阶段也没有跳跃。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class CharacterControllerMotor : MonoBehaviour
    {
        private CharacterController _controller;
        private float _verticalVelocity;

        public bool IsGrounded => _controller != null && _controller.isGrounded;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        /// <summary>
        /// 施加本帧的朝向变化与位移。
        /// </summary>
        /// <param name="turnDegrees">朝向变化量（度）。</param>
        /// <param name="forwardSpeed">沿朝向的速度（米/秒），负值后退。</param>
        /// <param name="tuning">重力与贴地参数。</param>
        /// <param name="deltaSeconds">帧时长。</param>
        public void Apply(
            float turnDegrees,
            float forwardSpeed,
            LocomotionTuning tuning,
            float deltaSeconds)
        {
            if (_controller == null || deltaSeconds <= 0f)
            {
                return;
            }

            if (turnDegrees != 0f)
            {
                transform.Rotate(0f, turnDegrees, 0f, Space.Self);
            }

            if (_controller.isGrounded)
            {
                // 着地时保持一个很小的向下速度：不清零会持续累积，清成 0 又会在下坡时脱离地面。
                _verticalVelocity = -tuning.GroundSnapSpeed;
            }
            else
            {
                _verticalVelocity -= tuning.Gravity * deltaSeconds;
            }

            var motion = transform.forward * (forwardSpeed * deltaSeconds);
            motion.y += _verticalVelocity * deltaSeconds;
            _controller.Move(motion);
        }

        /// <summary>传送到新位置。必须先关掉 CharacterController，否则它会把位置改回去。</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (_controller == null)
            {
                _controller = GetComponent<CharacterController>();
            }

            var wasEnabled = _controller.enabled;
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = wasEnabled;
            _verticalVelocity = 0f;
        }
    }
}
