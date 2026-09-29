using Naraka.Features.Character.Controller;
using Naraka.Features.Character.Model;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Naraka.Infrastructure.Input
{
    /// <summary>
    /// Input System 适配器。它把 Action Map 的读数翻译成 <see cref="PlayerInputFrame"/>，
    /// 业务层因此完全不知道键位、设备与重映射的存在。
    ///
    /// 这里不使用旧的 <c>Input.GetKey</c>/<c>Input.GetAxis</c>：正式玩家输入只走 Input System。
    /// 调试动作表只在编辑器与开发构建中启用，正式发布不会激活它。
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class NarakaPlayerInputProvider : MonoBehaviour, IPlayerInputSource
    {
        public const string PlayerMapName = "Player";
        public const string DebugMapName = "Debug";

        [SerializeField] private InputActionAsset actions;

        private InputActionAsset _runtimeActions;
        private InputActionMap _playerMap;
        private InputActionMap _debugMap;
        private InputAction _move;
        private InputAction _cameraLook;
        private InputAction _sprint;
        private InputAction _attack;
        private InputAction _skillF;
        private InputAction _skillV;
        private InputAction _debugHit;
        private InputAction _debugDeath;
        private InputAction _debugRevive;
        private bool _resolved;

        public bool IsPlayerInputEnabled => _playerMap != null && _playerMap.enabled;

        /// <summary>本帧鼠标位移。只喂给摄像机，绝不进入角色状态机。</summary>
        public Vector2 CameraLook =>
            _cameraLook == null ? Vector2.zero : _cameraLook.ReadValue<Vector2>();

        public bool DebugHitPressed => _debugHit != null && _debugHit.WasPressedThisFrame();

        public bool DebugDeathPressed => _debugDeath != null && _debugDeath.WasPressedThisFrame();

        public bool DebugRevivePressed => _debugRevive != null && _debugRevive.WasPressedThisFrame();

        private void Awake()
        {
            Resolve();
        }

        private void OnEnable()
        {
            Resolve();
            _playerMap?.Enable();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugMap?.Enable();
#endif
        }

        private void OnDisable()
        {
            _playerMap?.Disable();
            _debugMap?.Disable();
        }

        private void OnDestroy()
        {
            if (_runtimeActions == null)
            {
                return;
            }

            _runtimeActions.Disable();
            Destroy(_runtimeActions);
            _runtimeActions = null;
        }

        public PlayerInputFrame Sample()
        {
            if (!_resolved || _playerMap == null || !_playerMap.enabled)
            {
                // 输入禁用时返回空输入，而不是最后一次读数：否则锁输入的瞬间角色会保持前冲。
                return PlayerInputFrame.Idle;
            }

            var move = _move.ReadValue<Vector2>();
            var look = _cameraLook.ReadValue<Vector2>();

            // 只提供原始输入向量。摄像机朝向与角色朝向由 View 补齐，
            // 因为"往哪走"需要这三样凑在一起才有意义，而输入层只认识键位。
            return new PlayerInputFrame(
                moveX: move.x,
                moveY: move.y,
                cameraYaw: 0f,
                facingYaw: 0f,
                sprintHeld: _sprint.IsPressed(),
                attackHeld: _attack.IsPressed(),
                skillFPressed: _skillF.WasPressedThisFrame(),
                skillVPressed: _skillV.WasPressedThisFrame(),
                cameraMoved: look.sqrMagnitude > 0f);
        }

        public void SetPlayerInputEnabled(bool enabled)
        {
            Resolve();
            if (_playerMap == null)
            {
                return;
            }

            if (enabled)
            {
                _playerMap.Enable();
            }
            else
            {
                _playerMap.Disable();
            }
        }

        private void Resolve()
        {
            if (_resolved)
            {
                return;
            }

            if (actions == null)
            {
                Debug.LogError(
                    "NarakaPlayerInputProvider 未绑定 NarakaPlayerControls.inputactions；" +
                    "请运行菜单 NARAKA/Setup/Apply P2 Scene Setup。",
                    this);
                return;
            }

            // 用一份运行期副本，而不是直接使用资产本体。
            // InputActionAsset 会把"是否已启用"和绑定解析结果缓存在资产实例上，
            // 同一份资产被先后两个 provider 使用时，后者会继承前者的解析结果，
            // 于是它读不到自己启动之后才出现的设备。副本让每个实例各自解析一次。
            _runtimeActions = Instantiate(actions);
            _runtimeActions.name = actions.name;
            _playerMap = _runtimeActions.FindActionMap(PlayerMapName, throwIfNotFound: false);
            _debugMap = _runtimeActions.FindActionMap(DebugMapName, throwIfNotFound: false);
            if (_playerMap == null)
            {
                Debug.LogError($"输入资产缺少 '{PlayerMapName}' Action Map。", this);
                return;
            }

            // Action 引用只解析一次并缓存，运行期不做任何按名字的字符串查找。
            _move = _playerMap.FindAction("Move", throwIfNotFound: false);
            _cameraLook = _playerMap.FindAction("CameraLook", throwIfNotFound: false);
            _sprint = _playerMap.FindAction("SprintOrDash", throwIfNotFound: false);
            _attack = _playerMap.FindAction("Attack", throwIfNotFound: false);
            _skillF = _playerMap.FindAction("SkillF", throwIfNotFound: false);
            _skillV = _playerMap.FindAction("SkillV", throwIfNotFound: false);
            if (_move == null || _cameraLook == null || _sprint == null ||
                _attack == null || _skillF == null || _skillV == null)
            {
                Debug.LogError($"'{PlayerMapName}' Action Map 缺少必需的 Action。", this);
                return;
            }

            if (_debugMap != null)
            {
                _debugHit = _debugMap.FindAction("TriggerHit", throwIfNotFound: false);
                _debugDeath = _debugMap.FindAction("TriggerDeath", throwIfNotFound: false);
                _debugRevive = _debugMap.FindAction("TriggerRevive", throwIfNotFound: false);
            }

            _resolved = true;
        }
    }
}
