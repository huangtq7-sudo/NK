using Cinemachine;
using Naraka.Features.Character.Controller;
using Naraka.Infrastructure.Input;
using UnityEngine;

namespace Naraka.Infrastructure.Camera
{
    /// <summary>
    /// 第三人称镜头装配。
    ///
    /// 结构：一个独立于角色的轴心对象跟随角色胸口位置，鼠标只旋转这个轴心；
    /// Cinemachine 虚拟相机 Follow + LookAt 这个轴心，Transposer 以 LockToTarget
    /// 把相机放在轴心正后方，HardLookAt 保证轴心永远在画面中心。
    ///
    /// 由此得到三条硬性保证：
    /// - 角色永远是环绕中心：相机看的是挂在角色身上的轴心，而不是一个自由点；
    /// - 鼠标不直接旋转角色：轴心不是角色的子物体，旋转它不会改变角色朝向；
    /// - 角色用 A/D 转身时相机不被拖着转：轴心只取角色的位置，不取它的旋转。
    ///
    /// 遮挡由 <see cref="CinemachineCollider"/> 处理：它只缩短相机到轴心的距离，
    /// 不移动轴心，因此穿墙时中心点仍然在角色身上。
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class ThirdPersonCameraRig : MonoBehaviour, ICameraOrientation
    {
        [SerializeField] private ThirdPersonCameraSettings settings;
        [SerializeField] private CinemachineVirtualCamera virtualCamera;
        [SerializeField] private Transform pivot;
        [SerializeField] private NarakaPlayerInputProvider input;

        [Tooltip("战斗场景里锁定并隐藏鼠标指针。关掉它可以在编辑器里自由点击，但镜头会因为指针离开画面而停转。")]
        [SerializeField] private bool lockCursorDuringGameplay = true;

        private Transform _target;
        private float _yaw;
        private float _pitch;
        private Vector3 _followVelocity;
        private bool _lookEnabled = true;
        private bool _cursorLocked;

        public Transform Target => _target;

        /// <summary>摄像机当前水平朝向（度）。角色的移动方向以它为基准。</summary>
        public float Yaw => _yaw;

        public float Pitch => _pitch;

        /// <summary>
        /// 绑定到新的角色。场景加载与重生后必须调用，
        /// 否则虚拟相机会继续持有已销毁的 Transform。
        /// </summary>
        public void Rebind(Transform target)
        {
            _target = target;
            if (pivot == null)
            {
                return;
            }

            if (target != null)
            {
                pivot.position = PivotPosition(target);
                // 首次绑定时让镜头从角色背后起步，而不是从一个与角色无关的初始方位。
                _yaw = target.eulerAngles.y;
                pivot.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            if (virtualCamera != null)
            {
                // 没有角色时不抢相机：大厅仍由场景里的 Main Camera 自己决定取景。
                virtualCamera.gameObject.SetActive(target != null);

                // 让虚拟相机立刻落到新位置，避免从上一条命的位置拉一条长镜头过来。
                virtualCamera.PreviousStateIsValid = false;
                virtualCamera.OnTargetObjectWarped(pivot, Vector3.zero);
            }

            _followVelocity = Vector3.zero;
            ApplyCursorState();
        }

        /// <summary>出场动画与死亡期间可以停掉视角输入，但相机仍然跟随角色。</summary>
        public void SetLookEnabled(bool enabled)
        {
            _lookEnabled = enabled;
            ApplyCursorState();
        }

        private void Awake()
        {
            if (settings != null)
            {
                _yaw = settings.InitialYaw;
                _pitch = settings.InitialPitch;
            }

            ApplySettings();
        }

        private void LateUpdate()
        {
            if (settings == null || pivot == null)
            {
                return;
            }

            if (_target == null)
            {
                // 目标已销毁：停在原地等待 Rebind，不要抛异常也不要漂走。
                return;
            }

            if (_lookEnabled && input != null)
            {
                var look = input.CameraLook;
                _yaw += look.x * settings.YawSensitivity;
                var pitchDelta = look.y * settings.PitchSensitivity;
                _pitch += settings.InvertPitch ? pitchDelta : -pitchDelta;
                _pitch = Mathf.Clamp(_pitch, settings.MinPitch, settings.MaxPitch);
                if (_yaw > 360f)
                {
                    _yaw -= 360f;
                }
                else if (_yaw < -360f)
                {
                    _yaw += 360f;
                }
            }

            var desired = PivotPosition(_target);
            ApplyCursorState();
            pivot.position = settings.FollowDamping <= 0f
                ? desired
                : Vector3.SmoothDamp(
                    pivot.position, desired, ref _followVelocity, settings.FollowDamping);

            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            pivot.rotation = settings.RotationDamping <= 0f
                ? rotation
                : Quaternion.Slerp(
                    pivot.rotation,
                    rotation,
                    1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, settings.RotationDamping)));
        }

        private Vector3 PivotPosition(Transform target) =>
            target.position + (Vector3.up * settings.PivotHeight);

        /// <summary>
        /// 只有"已经绑定角色且视角可用"时才锁指针。大厅没有角色，指针必须保持可用，
        /// 否则玩家点不到任何按钮。
        /// </summary>
        private void ApplyCursorState()
        {
            if (!lockCursorDuringGameplay)
            {
                return;
            }

            var shouldLock = _target != null && _lookEnabled;
            if (shouldLock == _cursorLocked)
            {
                return;
            }

            _cursorLocked = shouldLock;
            Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !shouldLock;
        }

        private void ReleaseCursor()
        {
            if (!_cursorLocked)
            {
                return;
            }

            _cursorLocked = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDisable() => ReleaseCursor();

        private void OnDestroy() => ReleaseCursor();

        /// <summary>把配置写进 Cinemachine 组件。Editor 装配工具也调用它，保证两边一致。</summary>
        public void ApplySettings()
        {
            if (settings == null || virtualCamera == null)
            {
                return;
            }

            var transposer = virtualCamera.GetCinemachineComponent<CinemachineTransposer>();
            if (transposer != null)
            {
                transposer.m_BindingMode = CinemachineTransposer.BindingMode.LockToTarget;
                transposer.m_FollowOffset = new Vector3(0f, 0f, -settings.Distance);
                transposer.m_XDamping = 0f;
                transposer.m_YDamping = 0f;
                transposer.m_ZDamping = 0f;
                transposer.m_AngularDampingMode = CinemachineTransposer.AngularDampingMode.Euler;
                transposer.m_PitchDamping = 0f;
                transposer.m_YawDamping = 0f;
                transposer.m_RollDamping = 0f;
            }

            var collider = virtualCamera.GetComponent<CinemachineCollider>();
            if (collider != null)
            {
                collider.m_CollideAgainst = settings.ObstacleLayers;
                collider.m_MinimumDistanceFromTarget = settings.MinObstacleDistance;
                collider.m_CameraRadius = settings.CameraRadius;
                collider.m_Damping = settings.ObstacleDamping;
                collider.m_AvoidObstacles = true;
                collider.m_Strategy = CinemachineCollider.ResolutionStrategy.PullCameraForward;
            }
        }

#if UNITY_EDITOR
        private void OnValidate() => ApplySettings();
#endif
    }
}
