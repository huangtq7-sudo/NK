using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Features.Lobby.Controller;

namespace Naraka.Features.Loadout.Controller
{
    /// <summary>
    /// 出战选择网关。与其他 P1 网关一样不接受 accountId：
    /// 操作对象由服务端的已认证连接会话决定。
    /// </summary>
    public interface ILoadoutGateway
    {
        UniTask<LobbyProfileResult> SetLoadoutAsync(
            string heroId,
            string weaponId,
            string petId,
            CancellationToken cancellationToken);
    }

    public interface ILoadoutController : IReadOnlyState<LoadoutPresentationState>
    {
        void OpenHeroPanel();

        void OpenWeaponPanel();

        void Close();

        void PreviewNextHero();

        void PreviewPreviousHero();

        void PreviewNextWeapon();

        void PreviewPreviousWeapon();

        void SelectSkill(string skillId);

        /// <summary>把当前预览的英雄设为出战英雄。已经出战时不发请求。</summary>
        void EquipPreviewHero();

        /// <summary>把当前预览的兵器设为出战兵器。已经装备时不发请求。</summary>
        void EquipPreviewWeapon();
    }

    /// <summary>
    /// 英雄与兵器界面的编排。
    ///
    /// 出战选择的权威副本保存在 <see cref="ILobbyController"/> 的账号资料里，这里<b>不</b>再存一份，
    /// 只通过只读 PresentationState 订阅它，写入则通过 <see cref="ILoadoutGateway"/> 交给服务端。
    /// 本控制器自己拥有的只有浏览游标与当前技能页签这类纯界面状态。
    /// </summary>
    public sealed class LoadoutController : IController, ILoadoutController, IDisposable
    {
        private readonly IGameConfigProvider _config;
        private readonly ILoadoutGateway _gateway;
        private readonly ILobbyController _lobby;
        private readonly IServerCapabilities _capabilities;
        private readonly ReactiveState<LoadoutPresentationState> _state;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly IDisposable _lobbySubscription;
        private bool _isSaving;

        public LoadoutController(
            IGameConfigProvider config,
            ILoadoutGateway gateway,
            ILobbyController lobby,
            IServerCapabilities capabilities)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));

            _state = new ReactiveState<LoadoutPresentationState>(new LoadoutPresentationState(
                LoadoutPanel.None, false, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, 1, false, string.Empty));

            _lobbySubscription = _lobby.Subscribe(new LobbyStateObserver(this));
            ApplyLobbyState(_lobby.Current);
        }

        public LoadoutPresentationState Current => _state.Current;

        public IReadOnlyList<HeroConfig> Heroes =>
            _config.IsLoaded ? _config.Catalog.HeroesInDisplayOrder : Array.Empty<HeroConfig>();

        public IReadOnlyList<WeaponConfig> Weapons =>
            _config.IsLoaded ? _config.Catalog.WeaponsInDisplayOrder : Array.Empty<WeaponConfig>();

        // 「哪个面板是打开的」只有一个权威来源：大厅的 OpenModal。
        // 装备界面若自己再存一份开关状态，两处就会在关闭动画、Esc 与切换入口时不同步。
        public void OpenHeroPanel() => _lobby.RequestFeature(LobbyFeature.Hero);

        public void OpenWeaponPanel() => _lobby.RequestFeature(LobbyFeature.Weapon);

        public void Close() => _lobby.CloseFeature();

        public void PreviewNextHero() => StepHero(1);

        public void PreviewPreviousHero() => StepHero(-1);

        public void PreviewNextWeapon() => StepWeapon(1);

        public void PreviewPreviousWeapon() => StepWeapon(-1);

        public void SelectSkill(string skillId)
        {
            if (string.IsNullOrEmpty(skillId) || Current.SelectedSkillId == skillId)
            {
                return;
            }

            _state.Set(Current.WithSelectedSkill(skillId));
        }

        public void EquipPreviewHero()
        {
            if (Current.IsPreviewHeroEquipped)
            {
                return;
            }

            SaveAsync(Current.PreviewHeroId, Current.EquippedWeaponId, Current.EquippedPetId).Forget();
        }

        public void EquipPreviewWeapon()
        {
            if (Current.IsPreviewWeaponEquipped)
            {
                return;
            }

            SaveAsync(Current.EquippedHeroId, Current.PreviewWeaponId, Current.EquippedPetId).Forget();
        }

        public IDisposable Subscribe(IObserver<LoadoutPresentationState> observer) =>
            _state.Subscribe(observer);

        public void Dispose()
        {
            _lobbySubscription?.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
            _state.Dispose();
        }

        private void StepHero(int delta)
        {
            var heroes = Heroes;
            if (heroes.Count == 0)
            {
                return;
            }

            var next = heroes[Step(IndexOfHero(Current.PreviewHeroId), delta, heroes.Count)].HeroId;
            _state.Set(Current.WithPreviewHero(next, FirstSkillOf(next)));
        }

        private void StepWeapon(int delta)
        {
            var weapons = Weapons;
            if (weapons.Count == 0)
            {
                return;
            }

            var next = weapons[Step(IndexOfWeapon(Current.PreviewWeaponId), delta, weapons.Count)].WeaponId;
            _state.Set(Current.WithPreviewWeapon(next));
        }

        /// <summary>环形步进。列表两端相连，玩家不会在第一个英雄处点不动上一个。</summary>
        private static int Step(int current, int delta, int count)
        {
            var index = current < 0 ? 0 : current;
            return ((index + delta) % count + count) % count;
        }

        private int IndexOfHero(string heroId)
        {
            var heroes = Heroes;
            for (var i = 0; i < heroes.Count; i++)
            {
                if (string.Equals(heroes[i].HeroId, heroId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private int IndexOfWeapon(string weaponId)
        {
            var weapons = Weapons;
            for (var i = 0; i < weapons.Count; i++)
            {
                if (string.Equals(weapons[i].WeaponId, weaponId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private string FirstSkillOf(string heroId)
        {
            if (!_config.IsLoaded)
            {
                return string.Empty;
            }

            var skills = _config.Catalog.GetHeroSkills(heroId);
            return skills.Count > 0 ? skills[0].SkillId : string.Empty;
        }

        private async UniTaskVoid SaveAsync(string heroId, string weaponId, string petId)
        {
            if (_isSaving)
            {
                return;
            }

            // 能力检查放在就绪检查之前：服务器没有部署该功能时，
            // 玩家应该看到"服务器功能尚未升级"，而不是一次什么都没发生的点击。
            if (!_capabilities.Has(NarakaServerCapabilities.Loadout))
            {
                _state.Set(Current.WithStatusMessage(
                    LobbyOperationMessages.Describe(LobbyOperationStatus.ServerCapabilityMissing)));
                return;
            }

            if (!Current.IsReady ||
                string.IsNullOrEmpty(heroId) || string.IsNullOrEmpty(weaponId) || string.IsNullOrEmpty(petId))
            {
                return;
            }

            _isSaving = true;
            _state.Set(Current.WithSaving(true).WithStatusMessage(string.Empty));
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           CancellationToken.None, _lifetime.Token))
                {
                    var result = await _gateway.SetLoadoutAsync(heroId, weaponId, petId, linked.Token);
                    if (result.IsSuccess)
                    {
                        // 出战选择的权威副本在大厅资料里；这里只等待大厅刷新后回流的状态，
                        // 不自行改写"已出战"，避免界面比服务端先一步显示成功。
                        _state.Set(Current.WithSaving(false).WithStatusMessage(string.Empty));
                        _lobby.LoadProfileAsync(linked.Token).Forget();
                    }
                    else
                    {
                        _state.Set(Current
                            .WithSaving(false)
                            .WithStatusMessage(LobbyOperationMessages.Describe(result.Status)));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _state.Set(Current.WithSaving(false));
            }
            catch (Exception)
            {
                _state.Set(Current
                    .WithSaving(false)
                    .WithStatusMessage(LobbyOperationMessages.Describe(LobbyOperationStatus.TransportFailure)));
            }
            finally
            {
                _isSaving = false;
            }
        }

        /// <summary>
        /// 把大厅账号资料投影成装备状态。第一次拿到资料时把浏览游标对齐到出战对象；
        /// 之后玩家自己翻页的位置不会被后续刷新打断。
        /// </summary>
        private void ApplyLobbyState(LobbyPresentationState lobby)
        {
            var panel = ToPanel(lobby);
            var next = Current.WithPanel(panel);

            if (!lobby.HasProfile)
            {
                _state.Set(next.WithEquipped(false, string.Empty, string.Empty, string.Empty, 1));
                return;
            }

            var profile = lobby.Profile;
            next = next.WithEquipped(
                _config.IsLoaded,
                profile.SelectedHeroId,
                profile.SelectedWeaponId,
                profile.SelectedPetId,
                1);

            // 浏览游标只在第一次拿到资料时对齐到出战对象；
            // 之后玩家自己翻到的位置不会被后续刷新打断。
            if (next.PreviewHeroId.Length == 0)
            {
                next = next.WithPreviewHero(profile.SelectedHeroId, FirstSkillOf(profile.SelectedHeroId));
            }

            if (next.PreviewWeaponId.Length == 0)
            {
                next = next.WithPreviewWeapon(profile.SelectedWeaponId);
            }

            _state.Set(next);
        }

        private static LoadoutPanel ToPanel(LobbyPresentationState lobby)
        {
            if (!lobby.IsFeatureOpen)
            {
                return LoadoutPanel.None;
            }

            switch (lobby.OpenFeature)
            {
                case LobbyFeature.Hero:
                    return LoadoutPanel.Hero;
                case LobbyFeature.Weapon:
                    return LoadoutPanel.Weapon;
                default:
                    return LoadoutPanel.None;
            }
        }

        /// <summary>
        /// 只读订阅大厅状态。用具名类而不是匿名 lambda，
        /// 以便 <see cref="Dispose"/> 能确定地解除订阅。
        /// </summary>
        private sealed class LobbyStateObserver : IObserver<LobbyPresentationState>
        {
            private readonly LoadoutController _owner;

            public LobbyStateObserver(LoadoutController owner) => _owner = owner;

            public void OnNext(LobbyPresentationState value) => _owner.ApplyLobbyState(value);

            public void OnError(Exception error)
            {
            }

            public void OnCompleted()
            {
            }
        }
    }
}
