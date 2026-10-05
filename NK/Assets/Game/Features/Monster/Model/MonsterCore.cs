using Naraka.Features.AI.Model;
using Naraka.Features.Combat.Model;

namespace Naraka.Features.Monster.Model
{
    /// <summary>
    /// 怪物的纯逻辑核心：行为树选意图，HFSM 执行动作。
    ///
    /// **"当前动作"只有一份真相**，就是这里的 <see cref="Action"/>。
    /// 行为树写不了它：它只能把意图放进 <see cref="Intent"/>，
    /// 由本类在动作层空闲时决定要不要采纳。正在攻击的怪不会因为行为树
    /// 又选了一次追击就半途转身 —— 那正是"两处各存一份当前动作"会产生的缺陷。
    ///
    /// 不引用 UnityEngine：距离、角度由 <see cref="MonsterSenses"/> 送进来，
    /// 因此每条规则都能在 EditMode 里逐帧断言。
    /// </summary>
    public sealed class MonsterCore
    {
        private const float MaxDeltaSeconds = 0.1f;

        private readonly MonsterTuning _tuning;
        private readonly MonsterVitals _vitals;
        private readonly BehaviorTree<MonsterCore> _tree;
        private readonly float[] _skillCooldowns;
        private readonly float[] _skillSuppression;

        private MonsterActionState _action = MonsterActionState.Idle;
        private MonsterStateChangeReason _lastReason = MonsterStateChangeReason.Initial;
        private float _timeInAction;
        private MonsterIntent _intent = MonsterIntent.Patrol;
        private MonsterAnimation _animation = MonsterAnimation.Idle;
        private bool _animationRestarted;

        private MonsterSenses _senses;
        private MonsterAttackTuning _activeAttack;
        private int _activeSkillIndex = -1;
        private int _attackIdCounter;
        private int _currentAttackId;

        private float _normalAttackCooldown;
        private float _executeWindowRemaining;
        private float _patrolPauseRemaining;
        private bool _engaged;
        private bool _recovering;
        private bool _deathCompleted;

        public MonsterCore(MonsterTuning tuning)
        {
            _tuning = tuning;
            _vitals = new MonsterVitals(in tuning);
            _skillCooldowns = new float[tuning.SkillCount];
            _skillSuppression = new float[tuning.SkillCount];
            _tree = new BehaviorTree<MonsterCore>(BuildTree(), tuning.DecisionIntervalSeconds);
        }

        public MonsterTuning Tuning => _tuning;

        /// <summary>HFSM 的当前动作。这是唯一的"它正在干什么"。</summary>
        public MonsterActionState Action => _action;

        /// <summary>行为树最近一次选出的意图。它只是愿望，不是动作。</summary>
        public MonsterIntent Intent => _intent;

        public MonsterStateChangeReason LastReason => _lastReason;

        public float TimeInAction => _timeInAction;

        public float Health => _vitals.Health;

        public float MaxHealth => _vitals.MaxHealth;

        public float Armor => _vitals.Armor;

        public float MaxArmor => _vitals.MaxArmor;

        public float HealthRatio => _vitals.HealthRatio;

        public bool IsDead => _action == MonsterActionState.Death;

        /// <summary>生命降到阈值以下进入第二阶段。阶段只由生命比例决定。</summary>
        public MonsterPhase Phase =>
            _vitals.HealthRatio <= _tuning.PhaseHealthRatio ? MonsterPhase.Enraged : MonsterPhase.Normal;

        /// <summary>远离玩家，已经降低决策频率。</summary>
        public bool IsDormant => _intent == MonsterIntent.Dormant;

        /// <summary>当前是否处在被处决窗口内。</summary>
        public bool IsExecutable => _executeWindowRemaining > 0f && !IsDead;

        public float ExecuteWindowRemaining => _executeWindowRemaining;

        /// <summary>行为树已经决策过多少次。测试用它断言休眠确实降低了决策频率。</summary>
        public int DecisionCount => _tree.DecisionCount;

        public float DecisionIntervalSeconds => _tree.DecisionIntervalSeconds;

        /// <summary>死亡动画是否播完。只返回一次 true。</summary>
        public bool ConsumeDeathCompleted()
        {
            if (!_deathCompleted)
            {
                return false;
            }

            _deathCompleted = false;
            return true;
        }

        /// <summary>
        /// 被反击之后进入处决窗口。窗口期内玩家的普通攻击会变成处决。
        /// 已经死亡的怪物不再进入任何窗口。
        /// </summary>
        public void BeginExecuteWindow(float seconds)
        {
            if (IsDead || seconds <= 0f)
            {
                return;
            }

            _executeWindowRemaining = seconds;
        }

        /// <summary>
        /// 吃到玩家一次攻击。霸体只免疫硬直，伤害、护甲扣减与死亡照常发生。
        /// 死亡之后拒绝一切后续伤害与动作。
        /// </summary>
        public MonsterDamageResult ApplyRawDamage(float rawDamage)
        {
            if (IsDead)
            {
                return default;
            }

            var result = _vitals.ApplyRawDamage(rawDamage);
            if (result.Total <= 0f)
            {
                return result;
            }

            _engaged = true;
            if (result.Died)
            {
                EnterDeath();
                return result;
            }

            // 霸体：只跳过硬直这一段，上面的扣血已经发生过了。
            if (!HasSuperArmor())
            {
                ChangeAction(MonsterActionState.HitStun, MonsterStateChangeReason.Damaged);
                SetAnimation(MonsterAnimation.HitStun, restart: true);
            }

            return result;
        }

        /// <summary>推进一帧。所有状态切换都发生在这里。</summary>
        public MonsterFrameOutput Tick(in MonsterSenses senses, float deltaSeconds)
        {
            if (deltaSeconds < 0f)
            {
                deltaSeconds = 0f;
            }
            else if (deltaSeconds > MaxDeltaSeconds)
            {
                deltaSeconds = MaxDeltaSeconds;
            }

            _senses = senses;
            _animationRestarted = false;
            TickTimers(deltaSeconds);

            if (_action == MonsterActionState.Death)
            {
                var before = _timeInAction;
                _timeInAction += deltaSeconds;
                if (before < _tuning.DeathSeconds && _timeInAction >= _tuning.DeathSeconds)
                {
                    _deathCompleted = true;
                }

                _intent = MonsterIntent.Dead;
                return BuildOutput(MonsterMoveTarget.None, 0f, false);
            }

            _timeInAction += deltaSeconds;

            if (_action == MonsterActionState.HitStun)
            {
                if (_timeInAction < _tuning.HitStunSeconds)
                {
                    return BuildOutput(MonsterMoveTarget.None, 0f, false);
                }

                ChangeAction(MonsterActionState.Idle, MonsterStateChangeReason.ActionCompleted);
                SetAnimation(MonsterAnimation.Idle);
            }

            // 攻击与技能在播完之前不接受任何新意图：动作层忙着，行为树说了不算。
            if (_action == MonsterActionState.Attack || _action == MonsterActionState.Skill)
            {
                if (_timeInAction < _activeAttack.DurationSeconds)
                {
                    return BuildOutput(MonsterMoveTarget.None, 0f, _activeAttack.IsWarningActive(_timeInAction));
                }

                FinishAttack();
            }

            // 决策频率跟着休眠状态走，这样"远离玩家就降低决策频率"是真的发生了。
            _tree.SetDecisionInterval(
                _intent == MonsterIntent.Dormant
                    ? _tuning.DormantDecisionIntervalSeconds
                    : _tuning.DecisionIntervalSeconds);
            _tree.Tick(this, deltaSeconds);

            return ExecuteIntent(deltaSeconds);
        }

        // ----------------------------------------------------------------- 行为树

        /// <summary>
        /// 建树。节点在构造时建好一次，决策时只调用委托，
        /// 因此 5–10Hz 的决策不会产生任何分配。
        /// </summary>
        private BehaviorNode<MonsterCore> BuildTree() => new SelectorNode<MonsterCore>(
            "Root",
            new SequenceNode<MonsterCore>(
                "Dead",
                new ConditionNode<MonsterCore>("IsDead", core => core.IsDead),
                new ActionNode<MonsterCore>("SetDead", core => core.Choose(MonsterIntent.Dead))),
            new SequenceNode<MonsterCore>(
                "Dormant",
                new ConditionNode<MonsterCore>("TooFar", core => core.IsBeyondDormantDistance()),
                new ActionNode<MonsterCore>("SetDormant", core => core.Choose(MonsterIntent.Dormant))),
            new SequenceNode<MonsterCore>(
                "Leash",
                new ConditionNode<MonsterCore>("Engaged", core => core._engaged),
                new ConditionNode<MonsterCore>("LostTarget", core => core.HasLostTarget()),
                new ActionNode<MonsterCore>("SetRecover", core => core.Recover())),
            new SequenceNode<MonsterCore>(
                "Skill",
                new ConditionNode<MonsterCore>("CanEngage", core => core.CanEngage()),
                new ConditionNode<MonsterCore>("HasUsableSkill", core => core.SelectSkill() >= 0),
                new ActionNode<MonsterCore>("SetSkill", core => core.Choose(MonsterIntent.SelectSkill))),
            new SequenceNode<MonsterCore>(
                "NormalAttack",
                new ConditionNode<MonsterCore>("CanEngage", core => core.CanEngage()),
                new ConditionNode<MonsterCore>("InAttackRange", core => core.IsInNormalAttackRange()),
                new ActionNode<MonsterCore>("SetAttack", core => core.Choose(MonsterIntent.NormalAttack))),
            new SequenceNode<MonsterCore>(
                "Chase",
                new ConditionNode<MonsterCore>("CanEngage", core => core.CanEngage()),
                new ActionNode<MonsterCore>("SetChase", core => core.Chase())),
            // 脱战之后要一直往回走，直到回到出生点附近才恢复巡逻。
            // 否则"脱战"只存在一个决策周期，下一拍就变成在原地巡逻了。
            new SequenceNode<MonsterCore>(
                "ReturnHome",
                new ConditionNode<MonsterCore>("Recovering", core => core._recovering),
                new ConditionNode<MonsterCore>("AwayFromHome", core => core.IsAwayFromHome()),
                new ActionNode<MonsterCore>("KeepRecovering", core => core.Choose(MonsterIntent.Recover))),
            new ActionNode<MonsterCore>("Patrol", core => core.Choose(MonsterIntent.Patrol)));

        private BehaviorStatus Choose(MonsterIntent intent)
        {
            _intent = intent;
            return BehaviorStatus.Success;
        }

        private BehaviorStatus Chase()
        {
            // 第一次发现玩家先给一拍 Perceive，让"发现"这件事在状态上看得见，
            // 而不是从巡逻直接瞬移成追击。
            if (!_engaged)
            {
                _engaged = true;
                _recovering = false;
                return Choose(MonsterIntent.Perceive);
            }

            _recovering = false;

            return Choose(MonsterIntent.Chase);
        }

        private BehaviorStatus Recover()
        {
            _engaged = false;
            _recovering = true;
            return Choose(MonsterIntent.Recover);
        }

        /// <summary>离出生点还有多远才算"还没回到家"。用巡逻半径当阈值。</summary>
        private bool IsAwayFromHome() => _senses.DistanceFromHome > _tuning.PatrolRadius;

        /// <summary>
        /// 休眠只针对"玩家在场但很远"。完全没有玩家时应该回去巡逻，
        /// 而不是原地睡着 —— 巡逻才是怪物的默认状态。
        /// </summary>
        private bool IsBeyondDormantDistance() =>
            _senses.HasLivingTarget && _senses.DistanceToTarget > _tuning.DormantDistance;

        private bool HasLostTarget() =>
            !_senses.HasLivingTarget || _senses.DistanceToTarget > _tuning.ChaseRadius;

        /// <summary>能不能进入战斗：已经交战、或者玩家进了感知圈。</summary>
        private bool CanEngage()
        {
            if (!_senses.HasLivingTarget)
            {
                return false;
            }

            return _engaged || _senses.DistanceToTarget <= _tuning.PerceptionRadius;
        }

        private bool IsInNormalAttackRange() =>
            _normalAttackCooldown <= 0f && _senses.DistanceToTarget <= _tuning.AttackRange;

        /// <summary>
        /// 选技能。必须同时满足距离、冷却、阶段与最近使用抑制，
        /// 任何一条不满足就跳过这个技能。返回 -1 表示没有可用技能。
        /// </summary>
        public int SelectSkill()
        {
            var ratio = _vitals.HealthRatio;
            for (var i = 0; i < _tuning.SkillCount; i++)
            {
                var skill = _tuning.Skill(i);
                if (!skill.IsValid)
                {
                    continue;
                }

                if (_skillCooldowns[i] > 0f || _skillSuppression[i] > 0f)
                {
                    continue;
                }

                if (!skill.IsUnlockedAt(ratio))
                {
                    continue;
                }

                if (!skill.IsInRange(_senses.DistanceToTarget))
                {
                    continue;
                }

                return i;
            }

            return -1;
        }

        // ------------------------------------------------------------ 意图 → 动作

        private MonsterFrameOutput ExecuteIntent(float deltaSeconds)
        {
            switch (_intent)
            {
                case MonsterIntent.Dead:
                    return BuildOutput(MonsterMoveTarget.None, 0f, false);

                case MonsterIntent.Dormant:
                    GoIdle();
                    return BuildOutput(MonsterMoveTarget.None, 0f, false);

                case MonsterIntent.Perceive:
                    GoIdle();
                    return BuildOutput(MonsterMoveTarget.None, 0f, true);

                case MonsterIntent.Chase:
                    ChangeAction(MonsterActionState.Move, MonsterStateChangeReason.IntentSelected);
                    SetAnimation(MonsterAnimation.Run);
                    return BuildOutput(MonsterMoveTarget.Player, _tuning.ChaseSpeed, true);

                case MonsterIntent.Recover:
                    if (!IsAwayFromHome())
                    {
                        // 已经到家了，不用再走。下一次决策会回到巡逻。
                        _recovering = false;
                        GoIdle();
                        return BuildOutput(MonsterMoveTarget.None, 0f, false);
                    }

                    ChangeAction(MonsterActionState.Move, MonsterStateChangeReason.TargetLost);
                    SetAnimation(MonsterAnimation.Walk);
                    return BuildOutput(MonsterMoveTarget.Home, _tuning.PatrolSpeed, false);

                case MonsterIntent.NormalAttack:
                    BeginAttack(_tuning.NormalAttack, -1);
                    return BuildOutput(MonsterMoveTarget.None, 0f, false);

                case MonsterIntent.SelectSkill:
                    var index = SelectSkill();
                    if (index < 0)
                    {
                        // 决策到执行之间条件可能已经变了（例如刚好走出射程）。
                        // 这时回到追击，而不是空放一个技能。
                        ChangeAction(MonsterActionState.Move, MonsterStateChangeReason.IntentSelected);
                        SetAnimation(MonsterAnimation.Run);
                        return BuildOutput(MonsterMoveTarget.Player, _tuning.ChaseSpeed, true);
                    }

                    BeginAttack(_tuning.Skill(index), index);
                    return BuildOutput(
                        MonsterMoveTarget.None, 0f, _activeAttack.IsWarningActive(_timeInAction));

                default:
                    return TickPatrol(deltaSeconds);
            }
        }

        /// <summary>
        /// 巡逻：走一段、停一会儿。巡逻点的坐标由 View 在巡逻半径内挑，
        /// 因为那需要地形与导航信息；这里只决定"走还是停、走多快"。
        /// </summary>
        private MonsterFrameOutput TickPatrol(float deltaSeconds)
        {
            if (_patrolPauseRemaining > 0f)
            {
                GoIdle();
                return BuildOutput(MonsterMoveTarget.None, 0f, false);
            }

            if (_action != MonsterActionState.Move)
            {
                ChangeAction(MonsterActionState.Move, MonsterStateChangeReason.IntentSelected);
                SetAnimation(MonsterAnimation.Walk);
            }

            return BuildOutput(MonsterMoveTarget.PatrolPoint, _tuning.PatrolSpeed, false);
        }

        /// <summary>巡逻点已经走到，开始停顿。由 View 在到达时调用。</summary>
        public void NotifyPatrolPointReached() => _patrolPauseRemaining = _tuning.PatrolPauseSeconds;

        private void GoIdle()
        {
            if (_action == MonsterActionState.Idle)
            {
                return;
            }

            ChangeAction(MonsterActionState.Idle, MonsterStateChangeReason.IntentSelected);
            SetAnimation(MonsterAnimation.Idle);
        }

        private void BeginAttack(in MonsterAttackTuning attack, int skillIndex)
        {
            // 出手就算交战。否则一只"一上来就在攻击距离内"的怪永远不会被标成交战状态，
            // 脱战判定也就永远不会生效。
            _engaged = true;
            _recovering = false;
            _activeAttack = attack;
            _activeSkillIndex = skillIndex;
            _attackIdCounter++;
            _currentAttackId = _attackIdCounter;
            ChangeAction(
                skillIndex >= 0 ? MonsterActionState.Skill : MonsterActionState.Attack,
                MonsterStateChangeReason.IntentSelected);
            SetAnimation(
                skillIndex >= 0 ? MonsterAnimation.Skill : MonsterAnimation.Attack, restart: true);
        }

        private void FinishAttack()
        {
            if (_activeSkillIndex >= 0 && _activeSkillIndex < _skillCooldowns.Length)
            {
                _skillCooldowns[_activeSkillIndex] = _activeAttack.CooldownSeconds;
                _skillSuppression[_activeSkillIndex] = _activeAttack.RecentUseSuppressionSeconds;
            }
            else
            {
                _normalAttackCooldown = _activeAttack.CooldownSeconds;
            }

            _activeSkillIndex = -1;
            _activeAttack = default;
            _currentAttackId = 0;
            ChangeAction(MonsterActionState.Idle, MonsterStateChangeReason.ActionCompleted);
            SetAnimation(MonsterAnimation.Idle);
            _tree.RequestImmediateDecision();
        }

        private void EnterDeath()
        {
            _activeSkillIndex = -1;
            _activeAttack = default;
            _currentAttackId = 0;
            _executeWindowRemaining = 0f;
            _intent = MonsterIntent.Dead;
            _deathCompleted = false;
            ChangeAction(MonsterActionState.Death, MonsterStateChangeReason.Died);
            SetAnimation(MonsterAnimation.Death, restart: true);
        }

        private void TickTimers(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            _normalAttackCooldown = Decay(_normalAttackCooldown, deltaSeconds);
            _executeWindowRemaining = Decay(_executeWindowRemaining, deltaSeconds);
            _patrolPauseRemaining = Decay(_patrolPauseRemaining, deltaSeconds);
            for (var i = 0; i < _skillCooldowns.Length; i++)
            {
                _skillCooldowns[i] = Decay(_skillCooldowns[i], deltaSeconds);
                _skillSuppression[i] = Decay(_skillSuppression[i], deltaSeconds);
            }
        }

        private static float Decay(float remaining, float deltaSeconds)
        {
            if (remaining <= 0f)
            {
                return 0f;
            }

            remaining -= deltaSeconds;
            return remaining < 0f ? 0f : remaining;
        }

        private void ChangeAction(MonsterActionState next, MonsterStateChangeReason reason)
        {
            if (_action == next)
            {
                return;
            }

            _action = next;
            _lastReason = reason;
            _timeInAction = 0f;
        }

        /// <summary>霸体：技能过程中免疫普通硬直。伤害、护甲与死亡不受影响。</summary>
        private bool HasSuperArmor() => _action == MonsterActionState.Skill;

        private void SetAnimation(MonsterAnimation animation, bool restart = false)
        {
            if (restart || _animation != animation)
            {
                _animationRestarted = true;
            }

            _animation = animation;
        }

        private MonsterOverlayFlags BuildFlags(bool warningActive)
        {
            var flags = MonsterOverlayFlags.None;
            if (HasSuperArmor())
            {
                flags |= MonsterOverlayFlags.SuperArmor;
            }

            if (_intent == MonsterIntent.Dormant)
            {
                flags |= MonsterOverlayFlags.Dormant;
            }

            if (IsExecutable)
            {
                flags |= MonsterOverlayFlags.ExecuteWindow;
            }

            if (warningActive)
            {
                flags |= MonsterOverlayFlags.Warning;
            }

            return flags;
        }

        private MonsterFrameOutput BuildOutput(
            MonsterMoveTarget moveTarget,
            float moveSpeed,
            bool faceTarget)
        {
            var attacking = _action == MonsterActionState.Attack || _action == MonsterActionState.Skill;
            var hitWindowOpen = attacking && _activeAttack.IsHitWindowOpen(_timeInAction);
            var warningActive = attacking && _activeAttack.IsWarningActive(_timeInAction);
            var rawDamage = attacking
                ? DamageFormula.Raw(_tuning.Attack, _activeAttack.DamageMultiplier)
                : 0f;

            return new MonsterFrameOutput(
                _intent,
                _action,
                Phase,
                BuildFlags(warningActive),
                _animation,
                _animationRestarted,
                moveTarget,
                moveSpeed,
                faceTarget,
                hitWindowOpen ? _currentAttackId : 0,
                hitWindowOpen,
                rawDamage,
                attacking ? _activeAttack.MaxRange : 0f,
                attacking ? _activeAttack.ConeAngleDegrees : 0f,
                attacking ? _activeAttack.ColorTag : AttackColorTag.None,
                attacking && _activeAttack.Counterable);
        }
    }
}
