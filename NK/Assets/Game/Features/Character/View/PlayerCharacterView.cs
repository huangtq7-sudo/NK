using System;
using Naraka.Core.Application.MVC;
using Naraka.Features.Character.Controller;
using Naraka.Features.Character.Model;
using Naraka.Features.Combat.Controller;
using Naraka.Features.Combat.Model;
using Naraka.Features.Combat.View;
using UnityEngine;
using VContainer;

namespace Naraka.Features.Character.View
{
    /// <summary>
    /// 玩家角色 View。
    ///
    /// 职责严格限定在 View 该做的事：采样输入、把状态机输出投影到
    /// CharacterController 与 Animator、把命中窗交给命中盒。
    /// 它不决定任何状态、伤害、冷却或体力，也不读取 Animator 作为状态来源。
    /// </summary>
    [RequireComponent(typeof(CharacterControllerMotor))]
    public sealed class PlayerCharacterView :
        MonoBehaviour,
        IView<PlayerPresentationState>,
        IObserver<PlayerPresentationState>,
        IDamageTaker,
        ICombatTarget
    {
        [SerializeField] private PlayerAnimatorProjector animatorProjector;
        [SerializeField] private MeleeHitbox hitbox;
        [SerializeField] private Faction faction = Faction.Player;

        [Tooltip("搜索可处决目标的半径（世界单位）。")]
        [SerializeField] private float executeSearchRadius = 4f;

        private IPlayerController _controller;
        private IPlayerInputSource _input;
        private ICameraOrientation _camera;
        private IHitResolver _hitResolver;
        private IExecutionTargetRegistry _executionTargets;
        private ICombatTargetRegistry _combatTargets;
        private CharacterControllerMotor _motor;
        private IDisposable _subscription;
        private PlayerPresentationState _state;

        /// <summary>死亡动画播完的一次性通知。场景入口 View 订阅它来触发返回地图一。</summary>
        public event Action DeathSequenceCompleted;

        /// <summary>反击成功的一次性通知，参数是被反击者的标识。</summary>
        public event Action<int> CounterSucceeded;

        public PlayerPresentationState State => _state;

        /// <summary>当前生效的玩家配置快照。PlayMode 测试与调试显示用它对照实际表现。</summary>
        public PlayerTuning Tuning =>
            _controller != null ? _controller.Tuning : PlayerTuning.CreateBaseline();

        public int TargetId => gameObject.GetInstanceID();

        public Faction Faction => faction;

        public bool IsAlive => _controller == null || _controller.Current.IsAlive;

        Transform ICombatTarget.Transform => transform;

        /// <summary>反击成功后目标应该开多久的处决窗口。规则属于玩家配置。</summary>
        public float CounterExecuteWindowSeconds => Tuning.Counter.ExecuteWindowSeconds;

        [Inject]
        public void Construct(
            IPlayerController controller,
            IPlayerInputSource input,
            IHitResolver hitResolver,
            ICameraOrientation camera = null,
            IExecutionTargetRegistry executionTargets = null,
            ICombatTargetRegistry combatTargets = null)
        {
            _controller = controller;
            _input = input;
            _hitResolver = hitResolver;
            _camera = camera;
            _executionTargets = executionTargets;
            _combatTargets = combatTargets;
        }

        private void Awake()
        {
            _motor = GetComponent<CharacterControllerMotor>();
            if (animatorProjector == null)
            {
                animatorProjector = GetComponentInChildren<PlayerAnimatorProjector>();
            }

            if (hitbox == null)
            {
                hitbox = GetComponentInChildren<MeleeHitbox>();
            }

            if (_controller == null)
            {
                Debug.LogError("PlayerCharacterView 未注入 IPlayerController。", this);
                enabled = false;
                return;
            }

            // 在 Awake 就订阅并取一次当前状态，不能等到 Start。
            // VContainer 的 Instantiate 会在激活之前完成注入，因此这里已经拿得到控制器；
            // 而生成玩家的 WorldSceneEntry 就在同一帧里读 State —— 如果订阅留在 Start，
            // 它会读到默认值（Action = None），出场动画的等待会立刻被判定为"已结束"。
            _state = _controller.Current;
            _subscription = _controller.Subscribe(this);
            hitbox?.Bind(_hitResolver);
            _combatTargets?.SetPlayer(this);
        }

        private void Update()
        {
            if (_controller == null)
            {
                return;
            }

            // 持久化组合根被销毁后角色可能还活着一帧到几帧。继续推进一个已释放的
            // 控制器只会抛 ObjectDisposedException，因此这里直接停掉自己。
            if (_controller.IsDisposed)
            {
                enabled = false;
                return;
            }

            var deltaSeconds = Time.deltaTime;
            _controller.SetGrounded(_motor.IsGrounded);

            // 移动是相机相对的：把镜头朝向和角色当前朝向一并交给状态机，
            // 它据此算出该转多少、往哪走。缺少相机时退回角色自身朝向，
            // 移动变成角色相对，仍然可以操作。
            var facingYaw = transform.eulerAngles.y;
            var input = (_input?.Sample() ?? PlayerInputFrame.Idle)
                .WithCameraYaw(_camera?.Yaw ?? facingYaw)
                .WithFacingYaw(facingYaw)
                .WithExecutableTarget(HasExecutableTarget());
            var output = _controller.Tick(input, deltaSeconds);

            _motor.Apply(
                output.TurnDegrees, output.ForwardSpeed, _controller.Tuning.Locomotion, deltaSeconds);
            animatorProjector?.Apply(output.Animation, output.AnimationRestarted, output.AnimationSpeed);

            if (hitbox != null)
            {
                hitbox.SetWindow(output.AttackId, output.AttackDamage, output.AttackRadius);
            }

            // 输入锁定由业务状态决定，Action Map 只是它的投影。
            if (_input != null)
            {
                var shouldEnable = !output.IsInputLocked;
                if (_input.IsPlayerInputEnabled != shouldEnable)
                {
                    _input.SetPlayerInputEnabled(shouldEnable);
                }
            }

            if (_controller.ConsumeCounterSuccess(out var counteredId))
            {
                CounterSucceeded?.Invoke(counteredId);
            }

            if (_controller.ConsumeDeathCompleted())
            {
                DeathSequenceCompleted?.Invoke();
            }
        }

        /// <summary>
        /// 附近有没有处在处决窗口里的目标。
        ///
        /// 登记表为空时整段跳过：绝大多数时间场上没有任何可处决目标，
        /// 这条判断不该变成一次每帧的物理查询。
        /// </summary>
        private bool HasExecutableTarget()
        {
            if (_executionTargets == null || _executionTargets.Count == 0)
            {
                return false;
            }

            return _executionTargets.TryFindExecutable(transform.position, executeSearchRadius, out _);
        }

        /// <summary>把角色放到出生点。CharacterController 必须先停用再改位置。</summary>
        public void TeleportTo(Vector3 position, Quaternion rotation) =>
            _motor.Teleport(position, rotation);

        /// <summary>播放出场动画并在结束后恢复输入。业务状态由控制器推进，这里只发起。</summary>
        public void BeginSpawn(ActionState spawnState) => _controller?.BeginSpawn(spawnState);

        public void Respawn() => _controller?.Respawn();

        public void SetLoading(bool loading) => _controller?.SetLoading(loading);

        /// <summary>
        /// 调试入口：施加一次没有颜色、不可反击的伤害。
        /// 参数是<b>扣防御之前</b>的原始伤害。
        /// </summary>
        public VitalsDamageResult ApplyDebugRawDamage(float rawDamage) =>
            _controller == null ? default : _controller.ApplyDamage(rawDamage);

        public DamageApplication TakeDamage(in HitRequest request)
        {
            if (_controller == null)
            {
                return default;
            }

            var attack = new IncomingAttack(
                request.RawDamage, request.ColorTag, request.Counterable, request.HitId.OwnerId);
            var result = _controller.ApplyIncomingAttack(in attack);
            if (result.Countered)
            {
                return DamageApplication.AsCountered();
            }

            return new DamageApplication(
                result.Damage.ArmorLost, result.Damage.HealthLost, result.Damage.Died);
        }

        public void Render(PlayerPresentationState state) => _state = state;

        public void OnNext(PlayerPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }

        private void OnDestroy()
        {
            _subscription?.Dispose();
            hitbox?.CloseWindow();
            _combatTargets?.ClearPlayer(this);
        }
    }
}
