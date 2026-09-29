using Naraka.Features.Character.Model.Hfsm;

namespace Naraka.Features.Character.Model
{
    /// <summary>
    /// 玩家分层状态机的核心。它是纯逻辑：不引用 UnityEngine，不知道 Animator、
    /// CharacterController、摄像机或场景的存在，因此可以在 EditMode 里逐帧驱动并断言。
    ///
    /// 三层状态互相独立，转换规则集中在这里仲裁，优先级固定为：
    /// Death → HitStun → 强制场景/出场状态 → Action → Locomotion。
    /// </summary>
    public sealed class PlayerCore
    {
        private const float MaxDeltaSeconds = 0.1f;

        private readonly PlayerTuning _tuning;
        private readonly StateMachine<LocomotionState> _locomotion;
        private readonly StateMachine<ActionState> _action;
        private readonly StateMachine<ReactionState> _reaction;
        private readonly StaminaModel _stamina;
        private readonly ComboModel _combo;
        private readonly IdleVariationTimer _idleTimer;
        private readonly SprintGestureModel _sprint;
        private readonly SkillCooldown _skillF;
        private readonly SkillCooldown _skillV;
        private readonly PlayerVitals _vitals;

        private PlayerAnimation _animation = PlayerAnimation.Idle;
        private bool _animationRestarted;
        private ActionRejection _rejection;

        private bool _externalInputLock;
        private bool _loading;
        private bool _grounded = true;
        private float _spawnProtectionRemaining;

        private bool _attackHeldPrevious;
        private float _attackHeldSeconds;
        private bool _chargeConsumedThisHold;

        private float _lastMoveYaw;
        private bool _hasLastMoveYaw;
        private float _noMoveSeconds;
        private bool _reversalPending;
        private bool _chargeReady;

        private float _stopEntrySpeed;
        private int _attackIdCounter;
        private int _currentAttackId;
        private bool _deathCompleted;

        public PlayerCore(PlayerTuning tuning)
        {
            _tuning = tuning;
            _locomotion = new StateMachine<LocomotionState>(LocomotionState.Idle);
            _action = new StateMachine<ActionState>(ActionState.None);
            _reaction = new StateMachine<ReactionState>(ReactionState.None);
            _stamina = new StaminaModel(tuning.Stamina);
            _combo = new ComboModel(tuning.Combo);
            _idleTimer = new IdleVariationTimer(tuning.Idle);
            _sprint = new SprintGestureModel(tuning.SprintTapMaxSeconds);
            _skillF = new SkillCooldown(tuning.SkillF.CooldownSeconds);
            _skillV = new SkillCooldown(tuning.SkillV.CooldownSeconds);
            _vitals = new PlayerVitals(tuning.Vitals);

            _locomotion.Configure(
                LocomotionState.Idle,
                onEnter: () => _idleTimer.Reset());
            _action.Configure(
                ActionState.None,
                onEnter: () => _currentAttackId = 0);
        }

        public PlayerTuning Tuning => _tuning;

        public LocomotionState Locomotion => _locomotion.Current;

        public ActionState Action => _action.Current;

        public ReactionState Reaction => _reaction.Current;

        public float ActionElapsedSeconds => _action.TimeInState;

        public float Stamina => _stamina.Current;

        public float MaxStamina => _stamina.Max;

        public float Health => _vitals.Health;

        public float MaxHealth => _vitals.MaxHealth;

        public float Armor => _vitals.Armor;

        public float MaxArmor => _vitals.MaxArmor;

        public float SkillFCooldownRemaining => _skillF.Remaining;

        public float SkillVCooldownRemaining => _skillV.Remaining;

        public float SkillFCooldownSeconds => _skillF.CooldownSeconds;

        public float SkillVCooldownSeconds => _skillV.CooldownSeconds;

        public int ComboStep => _combo.CompletedStep;

        public float IdleSeconds => _idleTimer.IdleSeconds;

        public bool IsDead => _reaction.Current == ReactionState.Death;

        public PlayerOverlayFlags Flags => BuildFlags();

        /// <summary>死亡动画是否播完。只返回一次 true，避免重复触发返回地图一。</summary>
        public bool ConsumeDeathCompleted()
        {
            if (!_deathCompleted)
            {
                return false;
            }

            _deathCompleted = false;
            return true;
        }

        /// <summary>加载、设置界面等外部原因锁定输入。</summary>
        public void SetInputLocked(bool locked) => _externalInputLock = locked;

        public void SetLoading(bool loading) => _loading = loading;

        public void SetGrounded(bool grounded) => _grounded = grounded;

        /// <summary>进入强制出场状态。期间输入锁定，普通状态无法打断。</summary>
        public void BeginSpawn(ActionState spawnState)
        {
            if (spawnState != ActionState.SpawnLobbyToMap01 &&
                spawnState != ActionState.SpawnMap01ToMap02)
            {
                return;
            }

            _reaction.TryChangeTo(ReactionState.None, StateChangeReason.SceneSpawn);
            _locomotion.TryChangeTo(LocomotionState.Idle, StateChangeReason.SceneSpawn);
            _combo.Reset();
            _combo.ClearBuffer();
            _sprint.Reset();
            _idleTimer.Reset();
            _attackHeldPrevious = false;
            _attackHeldSeconds = 0f;
            _chargeConsumedThisHold = false;
            _action.Restart(spawnState, StateChangeReason.SceneSpawn);
        }

        /// <summary>
        /// 重生。生命 100%、护甲 50%、体力回满、F/V 冷却清零、获得重生保护。
        /// </summary>
        public void Respawn()
        {
            _vitals.Revive();
            _stamina.Reset();
            _skillF.Reset();
            _skillV.Reset();
            _combo.Reset();
            _combo.ClearBuffer();
            _sprint.Reset();
            _idleTimer.Reset();
            _deathCompleted = false;
            _attackHeldPrevious = false;
            _attackHeldSeconds = 0f;
            _chargeConsumedThisHold = false;
            _lastMoveYaw = 0f;
            _hasLastMoveYaw = false;
            _noMoveSeconds = 0f;
            _reversalPending = false;
            _spawnProtectionRemaining = _tuning.Reaction.SpawnProtectionSeconds;
            _reaction.Restart(ReactionState.None, StateChangeReason.Respawn);
            _action.Restart(ActionState.None, StateChangeReason.Respawn);
            _locomotion.Restart(LocomotionState.Idle, StateChangeReason.Respawn);
        }

        /// <summary>
        /// 受到伤害。霸体只免硬直，不免伤害与死亡；重生保护期间直接免伤。
        /// </summary>
        public VitalsDamageResult ApplyDamage(float amount)
        {
            if (IsDead || _spawnProtectionRemaining > 0f)
            {
                return default;
            }

            var result = _vitals.ApplyDamage(amount);
            if (result.ArmorLost <= 0f && result.HealthLost <= 0f)
            {
                return result;
            }

            _stamina.NotifyDamaged();
            _idleTimer.Reset();

            if (result.Died)
            {
                EnterDeath();
                return result;
            }

            if ((BuildFlags() & PlayerOverlayFlags.SuperArmor) == 0)
            {
                EnterHitStun();
            }

            return result;
        }

        /// <summary>推进一帧。所有状态切换都发生在这里，外部不直接写状态。</summary>
        public PlayerFrameOutput Tick(PlayerInputFrame input, float deltaSeconds)
        {
            if (deltaSeconds < 0f)
            {
                deltaSeconds = 0f;
            }
            else if (deltaSeconds > MaxDeltaSeconds)
            {
                // 单帧过长时钳制，避免一次卡顿把连招窗口整段跳过。
                deltaSeconds = MaxDeltaSeconds;
            }

            _animationRestarted = false;
            _rejection = ActionRejection.None;

            TickTimers(deltaSeconds);

            if (TickDeath(deltaSeconds))
            {
                return BuildOutput(0f, 0f);
            }

            var locked = _externalInputLock || _loading;
            var effective = locked ? PlayerInputFrame.Idle : input;

            if (TickHitStun(deltaSeconds))
            {
                TrackAttackHold(PlayerInputFrame.Idle, deltaSeconds);
                TrackMoveDirection(PlayerInputFrame.Idle, deltaSeconds);
                return BuildOutput(0f, 0f);
            }

            TrackAttackHold(effective, deltaSeconds);
            TrackMoveDirection(effective, deltaSeconds);

            var sprintGesture = _sprint.Tick(effective.SprintHeld, deltaSeconds);
            var attackInProgress = IsAttackAction(_action.Current);
            _combo.Tick(deltaSeconds, attackInProgress);

            if (TickForcedSpawn(deltaSeconds, out var spawnOutput))
            {
                return spawnOutput;
            }

            if (locked)
            {
                _rejection = ActionRejection.InputLocked;
            }

            TickActionLayer(effective, sprintGesture, deltaSeconds, locked);

            if (_action.Current != ActionState.None)
            {
                // 动作期间禁止自由移动与自由旋转，只有配置好的动作位移生效。
                ApplyActionAnimation(_action.Current);
                return BuildOutput(ActionForwardSpeed(_action.Current), 0f);
            }

            return TickLocomotionLayer(effective, deltaSeconds, locked);
        }

        private void TickTimers(float deltaSeconds)
        {
            // 冲刺进行中不恢复体力。规则是"Move_F 结束 0.75 秒后开始恢复"，
            // 所以恢复窗口的起点是动作结束，而不是动作开始。
            // 冲刺动画实测 1.333 秒，如果途中就恢复，一次冲刺能自己回来 6.7 点体力，
            // "连续两次冲刺耗尽体力"这条设计就不成立了。
            if (_action.Current != ActionState.Dash)
            {
                _stamina.Tick(deltaSeconds);
            }

            _skillF.Tick(deltaSeconds);
            _skillV.Tick(deltaSeconds);
            if (_spawnProtectionRemaining > 0f)
            {
                _spawnProtectionRemaining -= deltaSeconds;
                if (_spawnProtectionRemaining < 0f)
                {
                    _spawnProtectionRemaining = 0f;
                }
            }
        }

        private bool TickDeath(float deltaSeconds)
        {
            if (_reaction.Current != ReactionState.Death)
            {
                return false;
            }

            var before = _reaction.TimeInState;
            _reaction.Tick(deltaSeconds);
            if (before < _tuning.Reaction.DeathSeconds &&
                _reaction.TimeInState >= _tuning.Reaction.DeathSeconds)
            {
                _deathCompleted = true;
            }

            SetAnimation(PlayerAnimation.Death);
            return true;
        }

        private bool TickHitStun(float deltaSeconds)
        {
            if (_reaction.Current != ReactionState.HitStun)
            {
                return false;
            }

            _reaction.Tick(deltaSeconds);
            if (_reaction.TimeInState < _tuning.Reaction.HitStunSeconds)
            {
                SetAnimation(PlayerAnimation.HitStun);
                return true;
            }

            _reaction.TryChangeTo(ReactionState.None, StateChangeReason.ActionCompleted);
            return false;
        }

        private bool TickForcedSpawn(float deltaSeconds, out PlayerFrameOutput output)
        {
            var current = _action.Current;
            if (current != ActionState.SpawnLobbyToMap01 && current != ActionState.SpawnMap01ToMap02)
            {
                output = default;
                return false;
            }

            _action.Tick(deltaSeconds);
            var spawn = current == ActionState.SpawnLobbyToMap01
                ? _tuning.Reaction.SpawnLobbyToMap01
                : _tuning.Reaction.SpawnMap01ToMap02;

            if (_action.TimeInState >= spawn.RealDurationSeconds)
            {
                _action.TryChangeTo(ActionState.None, StateChangeReason.ActionCompleted);
                _idleTimer.Reset();
                output = default;
                return false;
            }

            SetAnimation(current == ActionState.SpawnLobbyToMap01
                ? PlayerAnimation.SpawnBurstLobbyToMap01
                : PlayerAnimation.SpawnBurstMap01ToMap02);
            output = BuildOutput(0f, 0f);
            return true;
        }

        private void TickActionLayer(
            PlayerInputFrame input,
            SprintGesture sprintGesture,
            float deltaSeconds,
            bool locked)
        {
            var current = _action.Current;
            if (current != ActionState.None)
            {
                // 先推进动作时间，再判断连段窗口与结束，这样窗口边界与配置数值一致。
                _action.Tick(deltaSeconds);
                var elapsed = _action.TimeInState;
                var currentTuning = ActionTuning(current);

                // 窗口都是片段时间，因此改播放速度不会让窗口跑到动作之外。
                var clipTime = currentTuning.ClipTimeAt(elapsed);

                // 攻击动作在连段窗口内消费缓存输入，直接推进到下一段。
                if (IsComboAction(current) &&
                    _combo.HasBufferedInput &&
                    currentTuning.IsComboWindowOpen(clipTime))
                {
                    var step = _combo.TryAdvance();
                    if (step > 0)
                    {
                        StartAttack(StepToAction(step), StateChangeReason.AttackInput);
                        return;
                    }
                }

                if (elapsed >= currentTuning.RealDurationSeconds)
                {
                    if (IsComboAction(current))
                    {
                        _combo.NotifyStepFinished();
                    }

                    _action.TryChangeTo(ActionState.None, StateChangeReason.ActionCompleted);
                    if (current == ActionState.Dash)
                    {
                        _stamina.NotifyDashEnded();
                    }
                }
                else
                {
                    return;
                }
            }

            if (locked)
            {
                return;
            }

            // 技能优先于普通攻击：玩家按了 F/V 就是要放技能，不该被连招吃掉。
            if (input.SkillFPressed && TryStartSkill(ActionState.SkillF))
            {
                return;
            }

            if (input.SkillVPressed && TryStartSkill(ActionState.SkillV))
            {
                return;
            }

            if (_chargeReady)
            {
                _chargeReady = false;
                StartAttack(ActionState.Charge, StateChangeReason.ChargeThreshold);
                return;
            }

            if (_combo.HasBufferedInput)
            {
                var step = _combo.TryAdvance();
                if (step > 0)
                {
                    StartAttack(StepToAction(step), StateChangeReason.AttackInput);
                    return;
                }
            }

            if (sprintGesture == SprintGesture.Tap)
            {
                TryStartDash();
            }
        }

        private bool TryStartSkill(ActionState skill)
        {
            var cooldown = skill == ActionState.SkillF ? _skillF : _skillV;
            if (!cooldown.TryUse())
            {
                _rejection = skill == ActionState.SkillF
                    ? ActionRejection.SkillFOnCooldown
                    : ActionRejection.SkillVOnCooldown;
                return false;
            }

            StartAttack(skill, StateChangeReason.SkillInput);
            return true;
        }

        private bool TryStartDash()
        {
            if (!_stamina.TrySpendDash())
            {
                _rejection = ActionRejection.InsufficientStamina;
                return false;
            }

            _idleTimer.Reset();
            _combo.ClearBuffer();
            _action.Restart(ActionState.Dash, StateChangeReason.SprintTap);
            SetAnimation(PlayerAnimation.Dash, restart: true);
            return true;
        }

        private void StartAttack(ActionState action, StateChangeReason reason)
        {
            _idleTimer.Reset();
            _attackIdCounter++;
            _currentAttackId = _attackIdCounter;
            _action.Restart(action, reason);
            ApplyActionAnimation(action, restart: true);
        }

        /// <summary>
        /// 移动层。移动是**相机相对**的：把输入向量放到摄像机平面上得到目标世界朝向，
        /// 角色朝那个方向转，然后沿自己的朝向前进。
        ///
        /// 因此 WASD 四个方向都会移动，动画始终是前进；没有后退，也没有原地转身。
        /// </summary>
        private PlayerFrameOutput TickLocomotionLayer(
            PlayerInputFrame input,
            float deltaSeconds,
            bool locked)
        {
            var deadzone = _tuning.Locomotion.MoveInputDeadzone;
            var magnitude = input.MoveMagnitude;
            var hasMove = !locked && magnitude > deadzone;

            if (hasMove)
            {
                _idleTimer.Reset();
            }

            // 转向：朝目标方向转，每帧最多转 TurnDegreesPerSecond * dt。
            // 没有移动输入时不转 —— 松开按键角色应该停在当前朝向，而不是继续回正。
            var turnDegrees = 0f;
            if (hasMove)
            {
                var delta = PlayerInputFrame.DeltaAngle(input.FacingYaw, input.DesiredWorldYaw);
                var maxStep = _tuning.Locomotion.TurnDegreesPerSecond * deltaSeconds;
                turnDegrees = delta > maxStep ? maxStep : delta < -maxStep ? -maxStep : delta;
            }

            var running = _sprint.IsHolding && hasMove;
            var current = _locomotion.Current;

            if (current == LocomotionState.RunTurnback)
            {
                _locomotion.Tick(deltaSeconds);
                if (_locomotion.TimeInState < _tuning.Locomotion.RunTurnbackSeconds)
                {
                    SetAnimation(PlayerAnimation.RunTurnback);
                    return BuildOutput(0f, turnDegrees);
                }

                _reversalPending = false;
                current = ResolveGroundState(hasMove, running);
                _locomotion.TryChangeTo(current, StateChangeReason.ActionCompleted);
            }
            else if (_reversalPending && running && current == LocomotionState.Run)
            {
                _reversalPending = false;
                _locomotion.Restart(LocomotionState.RunTurnback, StateChangeReason.DirectionReversed);
                SetAnimation(PlayerAnimation.RunTurnback, restart: true);
                return BuildOutput(0f, turnDegrees);
            }
            else
            {
                _reversalPending = false;
                var next = ResolveGroundState(hasMove, running);
                if (next != current)
                {
                    if (next == LocomotionState.StopWalk || next == LocomotionState.StopRun)
                    {
                        _stopEntrySpeed = LocomotionSpeed(current);
                    }

                    _locomotion.TryChangeTo(
                        next,
                        hasMove ? StateChangeReason.MoveInput : StateChangeReason.MoveInputReleased);
                }

                _locomotion.Tick(deltaSeconds);
                current = _locomotion.Current;
            }

            if (current == LocomotionState.Idle)
            {
                _idleTimer.Tick(deltaSeconds);
                if (_idleTimer.ShouldPlayVariation)
                {
                    _idleTimer.Reset();
                    _locomotion.Restart(LocomotionState.IdleVariation, StateChangeReason.IdleTimeout);
                    SetAnimation(PlayerAnimation.IdleVariation, restart: true);
                    return BuildOutput(0f, turnDegrees);
                }
            }

            SetAnimation(ToAnimation(current));

            // 速度按摇杆量缩放。键盘输入的模长恒为 1，因此键盘操作始终是满速。
            var speed = LocomotionSpeed(current);
            if (hasMove && (current == LocomotionState.Walk || current == LocomotionState.Run))
            {
                speed *= magnitude;
            }

            return BuildOutput(speed, turnDegrees);
        }

        private LocomotionState ResolveGroundState(bool hasMove, bool running)
        {
            var current = _locomotion.Current;
            if (hasMove)
            {
                return running ? LocomotionState.Run : LocomotionState.Walk;
            }

            switch (current)
            {
                case LocomotionState.Run:
                    return LocomotionState.StopRun;
                case LocomotionState.Walk:
                    return LocomotionState.StopWalk;
                case LocomotionState.StopRun:
                    return _locomotion.TimeInState >= _tuning.Locomotion.StopRunSeconds
                        ? LocomotionState.Idle
                        : LocomotionState.StopRun;
                case LocomotionState.StopWalk:
                    return _locomotion.TimeInState >= _tuning.Locomotion.StopWalkSeconds
                        ? LocomotionState.Idle
                        : LocomotionState.StopWalk;
                case LocomotionState.IdleVariation:
                    return _locomotion.TimeInState >= _tuning.Idle.VariationDurationSeconds
                        ? LocomotionState.Idle
                        : LocomotionState.IdleVariation;
                default:
                    return LocomotionState.Idle;
            }
        }

        private void TrackAttackHold(PlayerInputFrame input, float deltaSeconds)
        {
            var held = input.AttackHeld;
            if (held && !_attackHeldPrevious)
            {
                _attackHeldSeconds = 0f;
                _chargeConsumedThisHold = false;
            }
            else if (held)
            {
                _attackHeldSeconds += deltaSeconds;
                if (!_chargeConsumedThisHold && _attackHeldSeconds >= _tuning.Charge.HoldSeconds)
                {
                    // 按满阈值：本次按住兑换成蓄力，释放时不会再补一次普通攻击。
                    _chargeConsumedThisHold = true;
                    _chargeReady = true;
                    _combo.ClearBuffer();
                }
            }
            else if (_attackHeldPrevious)
            {
                if (!_chargeConsumedThisHold && _attackHeldSeconds < _tuning.Charge.HoldSeconds)
                {
                    _combo.BufferAttack();
                }

                _attackHeldSeconds = 0f;
                _chargeConsumedThisHold = false;
            }

            _attackHeldPrevious = held;
        }

        /// <summary>
        /// 跟踪移动方向，用于奔跑反向判定。
        ///
        /// "反向"= 新的目标世界朝向与上一次相差超过阈值角度，且中间没有停太久。
        /// 松开方向键停一会儿再按反方向属于重新起步，不播反向动作。
        /// </summary>
        private void TrackMoveDirection(PlayerInputFrame input, float deltaSeconds)
        {
            var deadzone = _tuning.Locomotion.MoveInputDeadzone;
            if (input.MoveMagnitude > deadzone)
            {
                var desired = input.DesiredWorldYaw;
                if (_hasLastMoveYaw &&
                    _noMoveSeconds <= _tuning.Locomotion.RunTurnbackInputWindowSeconds)
                {
                    var swing = PlayerInputFrame.DeltaAngle(_lastMoveYaw, desired);
                    if (swing < 0f)
                    {
                        swing = -swing;
                    }

                    if (swing >= _tuning.Locomotion.RunTurnbackAngleThresholdDegrees)
                    {
                        _reversalPending = true;
                    }
                }

                _lastMoveYaw = desired;
                _hasLastMoveYaw = true;
                _noMoveSeconds = 0f;
                return;
            }

            _noMoveSeconds += deltaSeconds;
            if (_noMoveSeconds > _tuning.Locomotion.RunTurnbackInputWindowSeconds)
            {
                _hasLastMoveYaw = false;
            }
        }

        private void EnterDeath()
        {
            _combo.Reset();
            _combo.ClearBuffer();
            _sprint.Reset();
            _deathCompleted = false;
            _action.Restart(ActionState.None, StateChangeReason.Died);
            _locomotion.Restart(LocomotionState.Idle, StateChangeReason.Died);
            _reaction.Restart(ReactionState.Death, StateChangeReason.Died);
            SetAnimation(PlayerAnimation.Death, restart: true);
        }

        private void EnterHitStun()
        {
            _combo.ClearBuffer();
            _action.Restart(ActionState.None, StateChangeReason.Damaged);
            _locomotion.Restart(LocomotionState.Idle, StateChangeReason.Damaged);
            _reaction.Restart(ReactionState.HitStun, StateChangeReason.Damaged);
            SetAnimation(PlayerAnimation.HitStun, restart: true);
        }

        private float LocomotionSpeed(LocomotionState state) => state switch
        {
            LocomotionState.Walk => _tuning.Locomotion.WalkSpeed,
            LocomotionState.Run => _tuning.Locomotion.RunSpeed,
            LocomotionState.StopWalk => DecayingStopSpeed(
                _tuning.Locomotion.StopWalkSeconds, _tuning.Locomotion.StopWalkDistance),
            LocomotionState.StopRun => DecayingStopSpeed(
                _tuning.Locomotion.StopRunSeconds, _tuning.Locomotion.StopRunDistance),
            _ => 0f
        };

        /// <summary>
        /// 停止动作期间的减速曲线。
        ///
        /// 用指数衰减而不是线性衰减，时间常数取 <c>距离 / 入口速度</c>：
        /// 这样速度从入口速度连续下降（松键瞬间不会突然掉档），
        /// 总位移又收敛到停止动画烘焙的距离，脚不会在地上滑一大段。
        ///
        /// 线性衰减做不到这一点：停止动画实测有 1.3–1.5 秒，
        /// 从 5 米每秒线性减速会走出 3.7 米，而动画本身只前移 1.4 米。
        /// </summary>
        private float DecayingStopSpeed(float duration, float distance)
        {
            if (duration <= 0f || _stopEntrySpeed <= 0f)
            {
                return 0f;
            }

            if (distance <= 0f)
            {
                // 没有配置距离时退回线性衰减，至少不会硬停。
                var remaining = 1f - (_locomotion.TimeInState / duration);
                return remaining <= 0f ? 0f : _stopEntrySpeed * remaining;
            }

            var tau = distance / _stopEntrySpeed;
            if (tau <= 0f)
            {
                return 0f;
            }

            return _stopEntrySpeed * (float)System.Math.Exp(-_locomotion.TimeInState / tau);
        }

        /// <summary>
        /// 动作位移速度。位移在配置的片段时间窗口内完成，因此冲刺与位移技能是
        /// 一次爆发，而不是铺满整段动作的缓慢滑行。
        /// </summary>
        private float ActionForwardSpeed(ActionState action) =>
            ActionTuning(action).ForwardSpeedAt(_action.TimeInState);

        private TimedActionTuning ActionTuning(ActionState action) => action switch
        {
            ActionState.AttackCombo1 => _tuning.Combo.Step1,
            ActionState.AttackCombo2 => _tuning.Combo.Step2,
            ActionState.AttackCombo3 => _tuning.Combo.Step3,
            ActionState.Charge => _tuning.Charge.Action,
            ActionState.SkillF => _tuning.SkillF.Action,
            ActionState.SkillV => _tuning.SkillV.Action,
            ActionState.Dash => _tuning.Dash,
            ActionState.SpawnLobbyToMap01 => _tuning.Reaction.SpawnLobbyToMap01,
            ActionState.SpawnMap01ToMap02 => _tuning.Reaction.SpawnMap01ToMap02,
            _ => default
        };

        private static bool IsComboAction(ActionState action) =>
            action == ActionState.AttackCombo1 ||
            action == ActionState.AttackCombo2 ||
            action == ActionState.AttackCombo3;

        private static bool IsAttackAction(ActionState action) =>
            IsComboAction(action) ||
            action == ActionState.Charge ||
            action == ActionState.SkillF ||
            action == ActionState.SkillV;

        private static ActionState StepToAction(int step) => step switch
        {
            1 => ActionState.AttackCombo1,
            2 => ActionState.AttackCombo2,
            _ => ActionState.AttackCombo3
        };

        private static PlayerAnimation ToAnimation(LocomotionState state) => state switch
        {
            LocomotionState.Idle => PlayerAnimation.Idle,
            LocomotionState.IdleVariation => PlayerAnimation.IdleVariation,
            LocomotionState.Walk => PlayerAnimation.Walk,
            LocomotionState.Run => PlayerAnimation.Run,
            LocomotionState.RunTurnback => PlayerAnimation.RunTurnback,
            LocomotionState.StopWalk => PlayerAnimation.StopWalk,
            _ => PlayerAnimation.StopRun
        };

        private void ApplyActionAnimation(ActionState action, bool restart = false)
        {
            var animation = action switch
            {
                ActionState.Dash => PlayerAnimation.Dash,
                ActionState.AttackCombo1 => PlayerAnimation.AttackCombo1,
                ActionState.AttackCombo2 => PlayerAnimation.AttackCombo2,
                ActionState.AttackCombo3 => PlayerAnimation.AttackCombo3,
                ActionState.Charge => PlayerAnimation.Charge,
                ActionState.SkillF => PlayerAnimation.SkillF,
                ActionState.SkillV => PlayerAnimation.SkillV,
                ActionState.SpawnLobbyToMap01 => PlayerAnimation.SpawnBurstLobbyToMap01,
                ActionState.SpawnMap01ToMap02 => PlayerAnimation.SpawnBurstMap01ToMap02,
                _ => PlayerAnimation.Idle
            };
            SetAnimation(animation, restart);
        }

        private void SetAnimation(PlayerAnimation animation, bool restart = false)
        {
            if (restart || _animation != animation)
            {
                _animationRestarted = true;
            }

            _animation = animation;
        }

        private PlayerOverlayFlags BuildFlags()
        {
            var flags = PlayerOverlayFlags.None;
            if (_externalInputLock || _loading || IsDead ||
                _reaction.Current == ReactionState.HitStun ||
                _action.Current == ActionState.SpawnLobbyToMap01 ||
                _action.Current == ActionState.SpawnMap01ToMap02)
            {
                flags |= PlayerOverlayFlags.InputLocked;
            }

            if (_loading)
            {
                flags |= PlayerOverlayFlags.Loading;
            }

            // 蓝色霸体：第三段普通攻击、蓄力与英雄技能。只免硬直，不免伤害与死亡。
            if (_action.Current == ActionState.AttackCombo3 ||
                _action.Current == ActionState.Charge ||
                _action.Current == ActionState.SkillF ||
                _action.Current == ActionState.SkillV)
            {
                flags |= PlayerOverlayFlags.SuperArmor;
            }

            if (_spawnProtectionRemaining > 0f)
            {
                flags |= PlayerOverlayFlags.SpawnProtection;
            }

            if (_grounded)
            {
                flags |= PlayerOverlayFlags.Grounded;
            }

            return flags;
        }

        private PlayerFrameOutput BuildOutput(float forwardSpeed, float turnDegrees)
        {
            var action = _action.Current;
            var hitWindowOpen = false;
            var damage = 0f;
            var radius = 0f;
            var animationSpeed = 1f;
            if (action != ActionState.None)
            {
                var tuning = ActionTuning(action);
                var clipTime = tuning.ClipTimeAt(_action.TimeInState);
                animationSpeed = tuning.SpeedAt(clipTime);
                if (IsAttackAction(action))
                {
                    hitWindowOpen = tuning.IsHitWindowOpen(clipTime);
                    damage = tuning.Damage;
                    radius = action == ActionState.SkillF
                        ? _tuning.SkillF.Radius
                        : action == ActionState.SkillV
                            ? _tuning.SkillV.Radius
                            : 0f;
                }
            }

            return new PlayerFrameOutput(
                _locomotion.Current,
                action,
                _reaction.Current,
                BuildFlags(),
                _animation,
                _animationRestarted,
                forwardSpeed,
                turnDegrees,
                hitWindowOpen,
                hitWindowOpen ? _currentAttackId : 0,
                damage,
                radius,
                animationSpeed,
                _rejection);
        }
    }
}
