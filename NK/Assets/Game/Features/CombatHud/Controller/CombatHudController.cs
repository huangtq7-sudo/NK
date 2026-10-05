using System;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Features.Character.Controller;
using Naraka.Features.Character.Model;
using Naraka.Features.Combat.Controller;
using Naraka.Features.Combat.Model;
using Naraka.Features.Monster.Controller;
using Naraka.Features.Monster.Model;

namespace Naraka.Features.CombatHud.Controller
{
    /// <summary>
    /// 战斗 HUD 的编排层。
    ///
    /// 它把玩家状态、当前目标状态和命中事实合成一份只读状态，
    /// 因此 HUD View 只需要订阅一个东西。View 不认识 PlayerCore、MonsterCore
    /// 或场景里的任何对象。
    /// </summary>
    public interface ICombatHudController : IReadOnlyState<CombatHudPresentationState>
    {
        /// <summary>玩家挨打了。表现用，不改任何业务状态。</summary>
        event Action<CombatFeedback> PlayerDamaged;

        /// <summary>怪物挨打了。表现用，不改任何业务状态。</summary>
        event Action<CombatFeedback> MonsterDamaged;

        /// <summary>按当前数据重算一次。View 每帧调用，值没变时不会重复通知。</summary>
        void Refresh();
    }

    /// <inheritdoc cref="ICombatHudController" />
    public sealed class CombatHudController : IController, ICombatHudController, IDisposable
    {
        private readonly IPlayerController _player;
        private readonly IMonsterRegistry _monsters;
        private readonly IHitResolver _hitResolver;
        private readonly ReactiveState<CombatHudPresentationState> _state;
        private CombatHudPresentationState _lastPublished;
        private bool _disposed;

        public CombatHudController(
            IPlayerController player,
            IMonsterRegistry monsters,
            IHitResolver hitResolver)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _monsters = monsters ?? throw new ArgumentNullException(nameof(monsters));
            _hitResolver = hitResolver;
            _lastPublished = Build();
            _state = new ReactiveState<CombatHudPresentationState>(_lastPublished);

            if (_hitResolver != null)
            {
                _hitResolver.HitLanded += OnHitLanded;
            }
        }

        public event Action<CombatFeedback> PlayerDamaged;

        public event Action<CombatFeedback> MonsterDamaged;

        public CombatHudPresentationState Current => _disposed ? _lastPublished : _state.Current;

        public void Refresh()
        {
            if (_disposed)
            {
                return;
            }

            var next = Build();
            if (next.Equals(_lastPublished))
            {
                // 值没变就不发布。HUD 每帧刷新，重复通知只会让所有绑定白跑一遍。
                return;
            }

            _lastPublished = next;
            _state.Set(next);
        }

        public IDisposable Subscribe(IObserver<CombatHudPresentationState> observer) =>
            _state.Subscribe(observer);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_hitResolver != null)
            {
                _hitResolver.HitLanded -= OnHitLanded;
            }

            _state.Dispose();
        }

        private void OnHitLanded(HitLandedEvent hit)
        {
            if (_disposed)
            {
                return;
            }

            var feedback = new CombatFeedback(
                hit.TargetId, hit.ArmorLost, hit.HealthLost, hit.Killed);

            // 阵营决定这是"我挨打"还是"我打中了"。HUD 不需要再去比对象。
            if (hit.TargetFaction == Faction.Player)
            {
                PlayerDamaged?.Invoke(feedback);
            }
            else
            {
                MonsterDamaged?.Invoke(feedback);
            }
        }

        private CombatHudPresentationState Build()
        {
            var player = _player.Current;
            var focused = _monsters.Focused;
            var hasTarget = focused != null && !focused.IsDisposed;
            var target = hasTarget ? focused.Current : MonsterPresentationState.Empty;

            return new CombatHudPresentationState(
                player.Health,
                player.MaxHealth,
                player.Armor,
                player.MaxArmor,
                player.Stamina,
                player.MaxStamina,
                player.SkillFCooldownRemaining,
                player.SkillFCooldownSeconds,
                player.SkillVCooldownRemaining,
                player.SkillVCooldownSeconds,
                player.Rejection,
                player.IsAlive,
                player.Flags,
                hasTarget,
                target.DisplayName,
                target.Health,
                target.MaxHealth,
                target.Armor,
                target.MaxArmor,
                target.IsAlive,
                target.Phase,
                target.WarningColorTag,
                target.ExecuteWindowRemaining);
        }
    }
}
