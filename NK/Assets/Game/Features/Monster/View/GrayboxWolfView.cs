using System;
using Naraka.Core.Application.Config;
using Naraka.Features.Combat.Controller;
using Naraka.Features.Combat.Model;
using Naraka.Features.Combat.View;
using Naraka.Features.Monster.Controller;
using Naraka.Features.Monster.Model;
using UnityEngine;
using UnityEngine.AI;
using VContainer;

namespace Naraka.Features.Monster.View
{
    /// <summary>
    /// 暮影妖狼的**灰盒** View。
    ///
    /// 正式狼模型（Polygonal Creatures Pack 里的 Polygonal Wolf）尚未导入，
    /// 因此这个对象刻意叫 GrayboxWolf：它是一个可替换的验证用表现层，
    /// **不是最终美术**。正式模型到位后替换掉子物体与 Animator 即可，
    /// 业务规则一行都不用动 —— 规则全在 Model 与 Controller 里。
    ///
    /// View 的职责只有三件：把场景里的距离/角度翻译成 <see cref="MonsterSenses"/>、
    /// 把帧输出投影成移动与命中窗、把表现（颜色、预警）画出来。
    /// 它不决定意图、伤害、冷却或阶段。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GrayboxWolfView : DamageReceiver, IExecutionTarget
    {
        [Tooltip("怪物配置 ID。数值全部来自 Config/Source/monsters.csv。")]
        [SerializeField] private string monsterId = "monster_wolf_duskshadow";

        [SerializeField] private MeleeHitbox hitbox;

        [Tooltip("命中盒相对本体的前向偏移由命中盒自己配置；这里只给技能用的表现根。")]
        [SerializeField] private Transform warningRoot;

        [SerializeField] private Renderer bodyRenderer;

        [SerializeField] private Color normalColor = new Color(0.36f, 0.38f, 0.45f);

        [SerializeField] private Color enragedColor = new Color(0.62f, 0.25f, 0.25f);

        [SerializeField] private Color executableColor = new Color(0.95f, 0.82f, 0.32f);

        [SerializeField] private Color deadColor = new Color(0.2f, 0.2f, 0.22f);

        [Tooltip("转向角速度（度/秒）。")]
        [SerializeField] private float turnDegreesPerSecond = 360f;

        [Tooltip("到达判定距离（世界单位）。")]
        [SerializeField] private float arriveDistance = 0.6f;

        [Tooltip("死亡动画播完后多久移除本体。0 表示保留尸体不移除。")]
        [SerializeField] private float despawnDelaySeconds = 3f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private IMonsterController _controller;
        private IGameConfigProvider _config;
        private IHitResolver _hitResolver;
        private IExecutionTargetRegistry _executionTargets;
        private ICombatTargetRegistry _combatTargets;
        private IMonsterRegistry _monsterRegistry;
        private MonsterWarningView _warning;
        private NavMeshAgent _agent;
        private CharacterController _motor;
        private MaterialPropertyBlock _block;

        private Vector3 _home;
        private Vector3 _patrolPoint;
        private bool _hasPatrolPoint;
        private bool _registeredAsExecutable;
        private float _despawnRemaining = -1f;
        private Color _appliedColor;
        private bool _hasAppliedColor;

        /// <summary>本体的怪物控制器。PlayMode 测试用它断言状态，而不是去读 Animator。</summary>
        public IMonsterController Controller => _controller;

        public MonsterPresentationState State =>
            _controller?.Current ?? MonsterPresentationState.Empty;

        public override bool IsAlive => _controller != null && !_controller.IsDead;

        public bool IsExecutable => _controller != null && _controller.IsExecutable;

        public Vector3 Position => transform.position;

        [Inject]
        public void Construct(
            IGameConfigProvider config,
            IHitResolver hitResolver,
            IExecutionTargetRegistry executionTargets = null,
            ICombatTargetRegistry combatTargets = null,
            IMonsterRegistry monsterRegistry = null)
        {
            _config = config;
            _hitResolver = hitResolver;
            _executionTargets = executionTargets;
            _combatTargets = combatTargets;
            _monsterRegistry = monsterRegistry;
        }

        private void Awake()
        {
            _home = transform.position;
            _block = new MaterialPropertyBlock();
            _agent = GetComponent<NavMeshAgent>();
            _motor = GetComponent<CharacterController>();
            if (hitbox == null)
            {
                hitbox = GetComponentInChildren<MeleeHitbox>();
            }

            if (bodyRenderer == null)
            {
                bodyRenderer = GetComponentInChildren<Renderer>();
            }

            if (warningRoot != null)
            {
                _warning = warningRoot.GetComponent<MonsterWarningView>();
            }

            _controller = new MonsterController(ResolveTuning());
            _monsterRegistry?.Register(_controller);
            hitbox?.Bind(_hitResolver);
            if (hitbox != null)
            {
                hitbox.Countered += OnAttackCountered;
            }

            ApplyColor(normalColor);
        }

        /// <summary>
        /// 取配置。配置缺失时**明确报错**并退回内置灰盒数值，
        /// 而不是安静地用一份编造的数值继续跑 —— 那会让"配置没生效"完全看不出来。
        /// </summary>
        private MonsterTuning ResolveTuning()
        {
            if (_config != null && _config.IsLoaded &&
                MonsterTuningFactory.TryCreate(_config.Catalog, monsterId, out var tuning))
            {
                return tuning;
            }

            Debug.LogError(
                $"GrayboxWolfView 未能从配置读取怪物 '{monsterId}'，" +
                "退回内置灰盒数值。请检查 Config/Source/monsters.csv 与生成物。",
                this);
            return MonsterTuning.CreateGrayboxWolf();
        }

        private void OnDestroy()
        {
            if (hitbox != null)
            {
                hitbox.Countered -= OnAttackCountered;
                hitbox.CloseWindow();
            }

            UnregisterExecutable();
            _monsterRegistry?.Unregister(_controller);
            (_controller as IDisposable)?.Dispose();
        }

        private void Update()
        {
            if (_controller == null || _controller.IsDisposed)
            {
                return;
            }

            var deltaSeconds = Time.deltaTime;
            var player = _combatTargets?.Player;
            var senses = BuildSenses(player);
            var output = _controller.Tick(in senses, deltaSeconds);

            // 一旦进入战斗就把 HUD 焦点指向自己。焦点只是展示，不影响任何判定。
            if (output.Intent != MonsterIntent.Patrol && output.Intent != MonsterIntent.Dormant)
            {
                _monsterRegistry?.Focus(_controller);
            }

            ApplyMovement(in output, player, deltaSeconds);
            ApplyHitWindow(in output);
            ApplyPresentation(in output);
            TickDespawn(deltaSeconds);
        }

        private MonsterSenses BuildSenses(ICombatTarget player)
        {
            if (player == null || player.Transform == null)
            {
                return new MonsterSenses(
                    false, false, float.MaxValue, 0f, HorizontalDistance(transform.position, _home));
            }

            var toPlayer = player.Transform.position - transform.position;
            toPlayer.y = 0f;
            var distance = toPlayer.magnitude;
            var angle = distance <= 0.0001f
                ? 0f
                : Vector3.SignedAngle(transform.forward, toPlayer, Vector3.up);
            return new MonsterSenses(
                true,
                player.IsAlive,
                distance,
                angle,
                HorizontalDistance(transform.position, _home));
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private void ApplyMovement(in MonsterFrameOutput output, ICombatTarget player, float deltaSeconds)
        {
            if (output.FaceTarget && player != null && player.Transform != null)
            {
                FaceTowards(player.Transform.position, deltaSeconds);
            }

            if (output.MoveSpeed <= 0f || output.MoveTarget == MonsterMoveTarget.None)
            {
                StopAgent();
                return;
            }

            var destination = ResolveDestination(output.MoveTarget, player);
            var toDestination = destination - transform.position;
            toDestination.y = 0f;
            if (toDestination.magnitude <= arriveDistance)
            {
                if (output.MoveTarget == MonsterMoveTarget.PatrolPoint)
                {
                    _hasPatrolPoint = false;
                    _controller.NotifyPatrolPointReached();
                }

                StopAgent();
                return;
            }

            if (!output.FaceTarget)
            {
                FaceTowards(destination, deltaSeconds);
            }

            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                // 有 NavMesh 就交给 Unity 2021.3 自带的导航，不引入第三方 AI 包。
                _agent.speed = output.MoveSpeed;
                _agent.isStopped = false;
                _agent.SetDestination(destination);
                return;
            }

            // 没有烘焙 NavMesh 时退回直线推进：灰盒地面是一块平板，
            // 直线足够验证追击与脱战，也不会因为缺少导航数据就整只怪不动。
            var step = toDestination.normalized * (output.MoveSpeed * deltaSeconds);
            if (_motor != null && _motor.enabled)
            {
                _motor.Move(step + (Physics.gravity * deltaSeconds * 0.1f));
            }
            else
            {
                transform.position += step;
            }
        }

        private Vector3 ResolveDestination(MonsterMoveTarget target, ICombatTarget player)
        {
            switch (target)
            {
                case MonsterMoveTarget.Player:
                    return player != null && player.Transform != null
                        ? player.Transform.position
                        : _home;

                case MonsterMoveTarget.Home:
                    return _home;

                case MonsterMoveTarget.PatrolPoint:
                    if (!_hasPatrolPoint)
                    {
                        _patrolPoint = PickPatrolPoint();
                        _hasPatrolPoint = true;
                    }

                    return _patrolPoint;

                default:
                    return transform.position;
            }
        }

        /// <summary>
        /// 在巡逻半径内挑一个点。半径来自配置，具体坐标属于几何问题，
        /// 因此由 View 决定 —— Model 不认识世界坐标。
        /// </summary>
        private Vector3 PickPatrolPoint()
        {
            var radius = _controller.Tuning.PatrolRadius;
            if (radius <= 0f)
            {
                return _home;
            }

            var angle = UnityEngine.Random.value * Mathf.PI * 2f;
            var distance = Mathf.Sqrt(UnityEngine.Random.value) * radius;
            var candidate = _home + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
            if (_agent != null && NavMesh.SamplePosition(candidate, out var hit, radius, NavMesh.AllAreas))
            {
                return hit.position;
            }

            return candidate;
        }

        private void FaceTowards(Vector3 worldPosition, float deltaSeconds)
        {
            var direction = worldPosition - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var target = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, target, turnDegreesPerSecond * deltaSeconds);
        }

        private void StopAgent()
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
            }
        }

        private void ApplyHitWindow(in MonsterFrameOutput output)
        {
            if (hitbox == null)
            {
                return;
            }

            hitbox.SetWindow(
                output.AttackId,
                output.RawDamage,
                output.AttackRadius,
                output.ColorTag,
                output.Counterable,
                output.AttackConeDegrees);
        }

        private void ApplyPresentation(in MonsterFrameOutput output)
        {
            _warning?.SetWarning(output.IsWarningActive, output.ColorTag);

            if (!IsAlive)
            {
                ApplyColor(deadColor);
                UnregisterExecutable();
                return;
            }

            if (output.IsInExecuteWindow)
            {
                RegisterExecutable();
                ApplyColor(executableColor);
                return;
            }

            UnregisterExecutable();
            ApplyColor(output.Phase == MonsterPhase.Enraged ? enragedColor : normalColor);
        }

        private void TickDespawn(float deltaSeconds)
        {
            if (_controller.ConsumeDeathCompleted() && despawnDelaySeconds > 0f)
            {
                _despawnRemaining = despawnDelaySeconds;
            }

            if (_despawnRemaining <= 0f)
            {
                return;
            }

            _despawnRemaining -= deltaSeconds;
            if (_despawnRemaining <= 0f)
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 自己这一刀被玩家反击掉了。结果是命中结算同步返回的，
        /// 因此不需要任何全局广播，也不会因为查询时机差一帧而漏掉。
        /// </summary>
        private void OnAttackCountered(int counteredBy)
        {
            var seconds = _combatTargets?.Player?.CounterExecuteWindowSeconds ?? 0f;
            if (seconds <= 0f)
            {
                return;
            }

            _controller?.BeginExecuteWindow(seconds);
            hitbox?.CloseWindow();
        }

        public override DamageApplication TakeDamage(in HitRequest request)
        {
            if (_controller == null)
            {
                return default;
            }

            var result = _controller.ApplyRawDamage(request.RawDamage);
            if (result.Total <= 0f)
            {
                return default;
            }

            // 挨打也算交战：玩家先手偷袭时 HUD 也应该立刻显示这只怪的血条。
            _monsterRegistry?.Focus(_controller);
            RaiseDamageDisplayed(result.Total);
            return new DamageApplication(result.ArmorLost, result.HealthLost, result.Died);
        }

        private void RegisterExecutable()
        {
            if (_registeredAsExecutable || _executionTargets == null)
            {
                return;
            }

            _executionTargets.Register(this);
            _registeredAsExecutable = true;
        }

        private void UnregisterExecutable()
        {
            if (!_registeredAsExecutable || _executionTargets == null)
            {
                return;
            }

            _executionTargets.Unregister(this);
            _registeredAsExecutable = false;
        }

        private void ApplyColor(Color color)
        {
            if (bodyRenderer == null || _block == null)
            {
                return;
            }

            if (_hasAppliedColor && _appliedColor == color)
            {
                return;
            }

            _appliedColor = color;
            _hasAppliedColor = true;

            // MaterialPropertyBlock 不生成材质实例，切场景也不会泄漏材质。
            bodyRenderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            bodyRenderer.SetPropertyBlock(_block);
        }
    }
}
