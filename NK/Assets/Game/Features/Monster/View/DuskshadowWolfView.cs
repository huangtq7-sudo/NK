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
    /// 暮影妖狼的表现层。
    ///
    /// 2026-10-05 起它承载**正式模型**（Polygonal Creatures Pack 的 Polygonal Wolf，
    /// Black 外观）。此前它叫 `GrayboxWolfView`，接的是一个方块替身；
    /// 类名随正式模型接入一并改掉，因为继续叫 Graybox 会让人以为美术还没进来。
    /// 方块替身仍然保留为 `GrayboxWolf.prefab`，但只作为**开发回退资产**，
    /// 正式场景不引用它。
    ///
    /// ADR-0018 当时写的是"换成正式模型时它一行都不用改"。实际换的时候
    /// 确实没有改动任何业务路径，只补了两件纯表现的事：
    /// 把 <c>MonsterFrameOutput.Animation</c> 投影到 Animator（动画真相本来就在 Model 层），
    /// 以及把颜色反馈从单个 Renderer 改成**全部**身体 Renderer
    /// （正式模型可能不只一个 Renderer，只染第一个会出现"身子变红、尾巴没变"）。
    ///
    /// View 的职责仍然只有四件：把场景里的距离/角度翻译成 <see cref="MonsterSenses"/>、
    /// 把帧输出投影成移动与命中窗、把当前动作投影到 Animator、把表现（颜色、预警）画出来。
    /// 它不决定意图、伤害、冷却、阶段或死亡。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DuskshadowWolfView : DamageReceiver, IExecutionTarget
    {
        [Tooltip("怪物配置 ID。数值全部来自 Config/Source/monsters.csv。")]
        [SerializeField] private string monsterId = "monster_wolf_duskshadow";

        [SerializeField] private MeleeHitbox hitbox;

        [Tooltip("命中盒相对本体的前向偏移由命中盒自己配置；这里只给技能用的表现根。")]
        [SerializeField] private Transform warningRoot;

        [Tooltip("身体 Renderer。正式模型可能有多个，颜色反馈必须作用在全部之上。" +
                 "留空时自动收集子物体里除预警面片之外的全部 Renderer。")]
        [SerializeField] private Renderer[] bodyRenderers;

        [Tooltip("动画投影器。留空时自动在本物体与子物体里查找。")]
        [SerializeField] private MonsterAnimatorProjector animatorProjector;

        [SerializeField] private Color normalColor = new Color(0.36f, 0.38f, 0.45f);

        [SerializeField] private Color enragedColor = new Color(0.62f, 0.25f, 0.25f);

        [SerializeField] private Color executableColor = new Color(0.95f, 0.82f, 0.32f);

        [SerializeField] private Color deadColor = new Color(0.2f, 0.2f, 0.22f);

        [Tooltip("转向角速度（度/秒）。")]
        [SerializeField] private float turnDegreesPerSecond = 360f;

        [Tooltip("到达判定距离（世界单位）。用于巡逻点与回家。")]
        [SerializeField] private float arriveDistance = 0.6f;

        [Tooltip("追击玩家时停在攻击距离之内多远（世界单位）。" +
                 "停止距离 = 配置的攻击距离 − 这个余量；攻击距离本身是配置值，这里只读不改。")]
        [SerializeField] private float chaseStopMargin = 0.8f;

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
            if (_agent != null)
            {
                // `updateRotation` 在 Unity 2021.3 里**不是序列化字段**
                // （Prefab 的 YAML 里没有 m_UpdateRotation），所以它只能在运行期关。
                // 转向由本 View 按配置角速度处理，交给 Agent 会和朝向投影打架。
                // Prefab 上序列化的 `angularSpeed = 0` 是第二道保险：
                // 即使这一行哪天被删掉，Agent 也转不动。
                _agent.updateRotation = false;
            }

            _motor = GetComponent<CharacterController>();
            if (hitbox == null)
            {
                hitbox = GetComponentInChildren<MeleeHitbox>();
            }

            if (warningRoot != null)
            {
                _warning = warningRoot.GetComponent<MonsterWarningView>();
            }

            if (animatorProjector == null)
            {
                animatorProjector = GetComponentInChildren<MonsterAnimatorProjector>(true);
            }

            if (bodyRenderers == null || bodyRenderers.Length == 0)
            {
                bodyRenderers = CollectBodyRenderers();
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
                $"DuskshadowWolfView 未能从配置读取怪物 '{monsterId}'，" +
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

            // 动画真相在 Model 层：`MonsterFrameOutput.Animation` 由 HFSM 产出，
            // 这里只负责把它交给 Animator，不做任何"现在该播什么"的判断。
            animatorProjector?.Apply(output.Animation, output.AnimationRestarted);

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
                HorizontalDistance(transform.position, _home),
                distance <= StopDistanceFor(MonsterMoveTarget.Player));
        }

        /// <summary>
        /// 收集身体 Renderer：子物体里除预警面片之外的全部 Renderer。
        ///
        /// 预警面片必须排除 —— 它是技能预警的红色提示，被身体颜色染一遍就看不出预警了。
        /// 只在 Awake 调一次，不在每帧收集。
        /// </summary>
        private Renderer[] CollectBodyRenderers()
        {
            var all = GetComponentsInChildren<Renderer>(true);
            var warningRenderer = _warning != null
                ? _warning.GetComponent<Renderer>()
                : warningRoot != null
                    ? warningRoot.GetComponent<Renderer>()
                    : null;

            var kept = 0;
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i] != warningRenderer)
                {
                    kept++;
                }
            }

            var result = new Renderer[kept];
            var next = 0;
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i] != warningRenderer)
                {
                    result[next++] = all[i];
                }
            }

            return result;
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
            var stopDistance = StopDistanceFor(output.MoveTarget);
            if (toDestination.magnitude <= stopDistance)
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
                // 停止距离也交给 Agent：它会自己减速收尾，而不是跑满速再被上面那段硬停。
                _agent.speed = output.MoveSpeed;
                _agent.stoppingDistance = stopDistance;
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

        /// <summary>
        /// 这一类目标要停在多远。
        ///
        /// 追玩家时必须停在**攻击距离以内一点**，而不是停在玩家身上。
        /// 2026-10-05 用户报告"怪物攻击角色后、仍在攻击范围内时会处于追击状态推着角色移动"，
        /// 根因不在碰撞体，而在这里：
        ///
        /// - 普攻冷却是 2 秒。冷却期间行为树的 `NormalAttack` 分支不成立
        ///   （`IsInNormalAttackRange` 同时要求"冷却结束"与"在攻击距离内"），
        ///   于是落到下一个分支 `Chase`；
        /// - `Chase` 的目标是**玩家的坐标本身**，而原来的到达判定用的是
        ///   <see cref="arriveDistance"/>（0.6），比两边胶囊半径之和（0.33 + 0.32 ≈ 0.65）
        ///   还小 —— 所以永远判定不出"到达"，狼会一直往玩家身上顶；
        /// - 玩家每帧都在用 `CharacterController.Move` 落重力，于是被挤开。
        ///
        /// 停在 `攻击距离 − chaseStopMargin` 上同时满足两件事：仍然在普攻距离之内
        /// （冷却一结束下一拍就能出手），又离接触足够远（2.4 对 0.65）。
        ///
        /// 攻击距离、感知距离与冷却都是已验收配置，这里**只读不改** ——
        /// 停多远属于几何，按 <see cref="MonsterMoveTarget"/> 的分工就该由 View 决定。
        /// </summary>
        private float StopDistanceFor(MonsterMoveTarget target)
        {
            if (target != MonsterMoveTarget.Player || _controller == null)
            {
                return arriveDistance;
            }

            return Mathf.Max(arriveDistance, _controller.Tuning.AttackRange - chaseStopMargin);
        }

        /// <summary>追击玩家时的停止距离。测试与调试用。</summary>
        public float ChaseStopDistance => StopDistanceFor(MonsterMoveTarget.Player);

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

        /// <summary>
        /// 把颜色反馈写到**全部**身体 Renderer 上。
        ///
        /// 正式模型不保证只有一个 Renderer，只染第一个会出现"身子变红、别的部位没变"。
        /// 用 <see cref="MaterialPropertyBlock"/> 而不是改材质：不生成材质实例，
        /// 切场景也不会泄漏材质，而且共享材质不会被一只狼的状态污染到另一只。
        /// 颜色没变时整段跳过，因此稳定态下这里不产生任何写入。
        /// </summary>
        private void ApplyColor(Color color)
        {
            if (bodyRenderers == null || bodyRenderers.Length == 0 || _block == null)
            {
                return;
            }

            if (_hasAppliedColor && _appliedColor == color)
            {
                return;
            }

            _appliedColor = color;
            _hasAppliedColor = true;

            for (var i = 0; i < bodyRenderers.Length; i++)
            {
                var renderer = bodyRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, color);
                _block.SetColor(ColorId, color);
                renderer.SetPropertyBlock(_block);
            }
        }

        /// <summary>身体 Renderer 数量。测试用它断言颜色反馈覆盖了整只狼。</summary>
        public int BodyRendererCount => bodyRenderers?.Length ?? 0;

        /// <summary>当前投影到 Animator 的动画。PlayMode 测试断言它，而不是去读 Animator 内部。</summary>
        public MonsterAnimation CurrentAnimation =>
            animatorProjector != null ? animatorProjector.CurrentAnimation : MonsterAnimation.None;
    }
}
