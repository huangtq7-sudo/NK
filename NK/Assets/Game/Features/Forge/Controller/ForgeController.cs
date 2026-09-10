using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.Messaging;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Core.Application.RedDot;
using Naraka.Features.Lobby.Controller;

namespace Naraka.Features.Forge.Controller
{
    /// <summary>一把武器的账号内唯一实例。武器不进入仓库，因此没有数量。</summary>
    public readonly struct AccountWeaponSnapshot
    {
        public AccountWeaponSnapshot(string weaponId, int level, long proficiency, long killCount)
        {
            WeaponId = weaponId ?? string.Empty;
            Level = level;
            Proficiency = proficiency;
            KillCount = killCount;
        }

        public string WeaponId { get; }

        public int Level { get; }

        public long Proficiency { get; }

        public long KillCount { get; }
    }

    /// <summary>锻造界面用到的材料持有量。</summary>
    public readonly struct ForgeMaterialSnapshot
    {
        public ForgeMaterialSnapshot(string itemId, long quantity)
        {
            ItemId = itemId ?? string.Empty;
            Quantity = quantity;
        }

        public string ItemId { get; }

        public long Quantity { get; }
    }

    public readonly struct ForgeResult
    {
        private ForgeResult(
            LobbyOperationStatus status,
            IReadOnlyList<AccountWeaponSnapshot> weapons,
            IReadOnlyList<ForgeMaterialSnapshot> materials,
            long copper,
            long silk,
            long gold)
        {
            Status = status;
            Weapons = weapons;
            Materials = materials;
            Copper = copper;
            Silk = silk;
            Gold = gold;
        }

        public LobbyOperationStatus Status { get; }

        public IReadOnlyList<AccountWeaponSnapshot> Weapons { get; }

        public IReadOnlyList<ForgeMaterialSnapshot> Materials { get; }

        public long Copper { get; }

        public long Silk { get; }

        public long Gold { get; }

        public bool IsSuccess => Status == LobbyOperationStatus.Success && Weapons != null;

        public static ForgeResult Success(
            IReadOnlyList<AccountWeaponSnapshot> weapons,
            IReadOnlyList<ForgeMaterialSnapshot> materials,
            long copper,
            long silk,
            long gold) =>
            new ForgeResult(LobbyOperationStatus.Success, weapons, materials, copper, silk, gold);

        public static ForgeResult Failed(LobbyOperationStatus status) =>
            new ForgeResult(status, null, null, 0, 0, 0);
    }

    /// <summary>
    /// 锻造网关。配方与"下一级预览"都来自两端共享的配置，
    /// 网络上只传武器等级、材料持有量与余额这类账号事实。
    /// </summary>
    public interface IForgeGateway
    {
        UniTask<ForgeResult> RequestForgeAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 强化一级。<paramref name="expectedLevel"/> 是界面上显示的等级，
        /// 与服务端不符时服务端会拒绝，从而不会按陈旧数据消耗材料。
        /// </summary>
        UniTask<ForgeResult> UpgradeAsync(
            string weaponId,
            int expectedLevel,
            string requestId,
            CancellationToken cancellationToken);
    }

    public interface IForgeController : IReadOnlyState<ForgePresentationState>
    {
        void Close();

        void PreviewNextWeapon();

        void PreviewPreviousWeapon();

        void Upgrade();

        UniTask ReloadAsync(CancellationToken cancellationToken);

        /// <summary>当前预览武器的下一级配方。已达最高等级时为 null。</summary>
        ForgeRecipeConfig CurrentRecipe { get; }

        /// <summary>当前等级的攻击力。用于"68 → 75"这类预览的左侧数值。</summary>
        WeaponLevelConfig CurrentLevelStats { get; }

        /// <summary>下一级的攻击力。已达最高等级时为 null。</summary>
        WeaponLevelConfig NextLevelStats { get; }

        /// <summary>材料是否齐备。配置里没有随机失败，因此这就是"能否强化"的全部条件。</summary>
        bool CanAffordUpgrade { get; }
    }

    /// <summary>
    /// 锻造界面的编排。
    ///
    /// 玩法基线规定"材料足够时强化必定成功"，因此这里既不显示成功率，也不做任何随机判定；
    /// 界面显示的"必定成功"与服务端行为是同一件事。
    /// </summary>
    public sealed class ForgeController : IController, IForgeController, IDisposable
    {
        private readonly IGameConfigProvider _config;
        private readonly IForgeGateway _gateway;
        private readonly ILobbyController _lobby;
        private readonly IServerCapabilities _capabilities;
        private readonly IDomainEventBus _bus;
        private readonly ReactiveState<ForgePresentationState> _state;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly IDisposable _lobbySubscription;
        private bool _isLoading;
        private bool _isUpgrading;
        private bool _wasOpen;

        public ForgeController(
            IGameConfigProvider config,
            IForgeGateway gateway,
            ILobbyController lobby,
            IServerCapabilities capabilities,
            IDomainEventBus bus)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));

            _state = new ReactiveState<ForgePresentationState>(ForgePresentationState.Initial);
            _lobbySubscription = _lobby.Subscribe(new LobbyStateObserver(this));
            ApplyLobbyState(_lobby.Current);
        }

        public ForgePresentationState Current => _state.Current;

        public ForgeRecipeConfig CurrentRecipe
        {
            get
            {
                var state = Current;
                if (!_config.IsLoaded || state.PreviewWeaponId.Length == 0)
                {
                    return null;
                }

                return _config.Catalog.TryGetForgeRecipe(
                    state.PreviewWeaponId, state.LevelOf(state.PreviewWeaponId), out var recipe)
                    ? recipe
                    : null;
            }
        }

        public WeaponLevelConfig CurrentLevelStats => LevelStats(Current.LevelOf(Current.PreviewWeaponId));

        public WeaponLevelConfig NextLevelStats => LevelStats(Current.LevelOf(Current.PreviewWeaponId) + 1);

        public bool CanAffordUpgrade
        {
            get
            {
                var recipe = CurrentRecipe;
                if (recipe == null)
                {
                    return false;
                }

                var state = Current;
                if (state.BalanceOf(recipe.CurrencyId) < recipe.CurrencyAmount)
                {
                    return false;
                }

                return HasMaterial(state, recipe.Material1ItemId, recipe.Material1Amount) &&
                       HasMaterial(state, recipe.Material2ItemId, recipe.Material2Amount);
            }
        }

        public void Close() => _lobby.CloseFeature();

        public void PreviewNextWeapon() => StepWeapon(1);

        public void PreviewPreviousWeapon() => StepWeapon(-1);

        public void Upgrade()
        {
            var state = Current;
            if (state.PreviewWeaponId.Length == 0 || state.IsBusy || CurrentRecipe == null)
            {
                return;
            }

            UpgradeAsync(state.PreviewWeaponId, state.LevelOf(state.PreviewWeaponId)).Forget();
        }

        public async UniTask ReloadAsync(CancellationToken cancellationToken)
        {
            if (_isLoading)
            {
                return;
            }

            if (!_capabilities.Has(NarakaServerCapabilities.Forge))
            {
                _state.Set(Current.WithStatusMessage(
                    LobbyOperationMessages.Describe(LobbyOperationStatus.ServerCapabilityMissing)));
                return;
            }

            _isLoading = true;
            _state.Set(Current.WithLoading(true).WithStatusMessage(string.Empty));
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           cancellationToken, _lifetime.Token))
                {
                    Apply(await _gateway.RequestForgeAsync(linked.Token));
                }
            }
            catch (OperationCanceledException)
            {
                _state.Set(Current.WithLoading(false));
            }
            catch (Exception)
            {
                _state.Set(Current
                    .WithLoading(false)
                    .WithStatusMessage(LobbyOperationMessages.Describe(LobbyOperationStatus.TransportFailure)));
            }
            finally
            {
                _isLoading = false;
            }
        }

        public IDisposable Subscribe(IObserver<ForgePresentationState> observer) => _state.Subscribe(observer);

        public void Dispose()
        {
            _lobbySubscription?.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
            _state.Dispose();
        }

        private static bool HasMaterial(ForgePresentationState state, string itemId, int amount) =>
            string.IsNullOrEmpty(itemId) || amount <= 0 || state.MaterialOf(itemId) >= amount;

        private WeaponLevelConfig LevelStats(int level)
        {
            var weaponId = Current.PreviewWeaponId;
            if (!_config.IsLoaded || weaponId.Length == 0)
            {
                return null;
            }

            return _config.Catalog.TryGetWeaponLevel(weaponId, level, out var stats) ? stats : null;
        }

        private void StepWeapon(int delta)
        {
            if (!_config.IsLoaded)
            {
                return;
            }

            var weapons = _config.Catalog.WeaponsInDisplayOrder;
            if (weapons.Count == 0)
            {
                return;
            }

            var index = -1;
            for (var i = 0; i < weapons.Count; i++)
            {
                if (string.Equals(weapons[i].WeaponId, Current.PreviewWeaponId, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            // 环形步进：列表两端相连，玩家不会在第一把武器处点不动上一把。
            var next = ((index < 0 ? 0 : index) + delta) % weapons.Count;
            if (next < 0)
            {
                next += weapons.Count;
            }

            _state.Set(Current.WithPreviewWeapon(weapons[next].WeaponId).WithStatusMessage(string.Empty));
        }

        private async UniTaskVoid UpgradeAsync(string weaponId, int expectedLevel)
        {
            if (_isUpgrading || !Current.IsOpen)
            {
                return;
            }

            if (!_capabilities.Has(NarakaServerCapabilities.Forge))
            {
                _state.Set(Current.WithStatusMessage(
                    LobbyOperationMessages.Describe(LobbyOperationStatus.ServerCapabilityMissing)));
                return;
            }

            _isUpgrading = true;
            _state.Set(Current.WithBusy(true).WithStatusMessage(string.Empty));
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           CancellationToken.None, _lifetime.Token))
                {
                    var result = await _gateway.UpgradeAsync(
                        weaponId, expectedLevel, Guid.NewGuid().ToString("N"), linked.Token);
                    Apply(result);

                    if (result.IsSuccess)
                    {
                        // 强化消耗了货币与材料；大厅余额必须跟着刷新。
                        _lobby.LoadAccountSummaryAsync(linked.Token).Forget();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _state.Set(Current.WithBusy(false));
            }
            catch (Exception)
            {
                _state.Set(Current
                    .WithBusy(false)
                    .WithStatusMessage(LobbyOperationMessages.Describe(LobbyOperationStatus.TransportFailure)));
            }
            finally
            {
                _isUpgrading = false;
            }
        }

        private void Apply(ForgeResult result)
        {
            if (result.IsSuccess)
            {
                var next = Current.WithServerState(
                    result.Weapons, result.Materials, result.Copper, result.Silk, result.Gold);

                // 第一次拿到数据时把浏览游标对齐到当前出战武器。
                if (next.PreviewWeaponId.Length == 0 && result.Weapons.Count > 0)
                {
                    next = next.WithPreviewWeapon(result.Weapons[0].WeaponId);
                }

                _state.Set(next.WithStatusMessage(string.Empty));
                PublishUpgradeAvailability(result);
                return;
            }

            // 失败只更新提示，绝不覆盖上一次成功的等级与材料。
            _state.Set(Current
                .WithLoading(false)
                .WithBusy(false)
                .WithStatusMessage(LobbyOperationMessages.Describe(result.Status)));
        }

        /// <summary>
        /// 声明"有武器现在就能强化"。
        ///
        /// 逐把武器看一遍，而不只看当前浏览的那一把：否则玩家切换浏览目标时
        /// 入口红点会跟着闪烁。材料与余额均来自服务端返回的快照。
        /// </summary>
        private void PublishUpgradeAvailability(ForgeResult result)
        {
            var state = Current;
            var hasUpgrade = false;
            if (_config.IsLoaded)
            {
                foreach (var weapon in result.Weapons)
                {
                    if (!_config.Catalog.TryGetForgeRecipe(
                            weapon.WeaponId, weapon.Level, out var recipe) || recipe == null)
                    {
                        continue;
                    }

                    if (state.BalanceOf(recipe.CurrencyId) >= recipe.CurrencyAmount &&
                        HasMaterial(state, recipe.Material1ItemId, recipe.Material1Amount) &&
                        HasMaterial(state, recipe.Material2ItemId, recipe.Material2Amount))
                    {
                        hasUpgrade = true;
                        break;
                    }
                }
            }

            _bus.Publish(new RedDotSourceChanged(RedDotPath.ForgeUpgradeAvailable, hasUpgrade));
        }

        private void ApplyLobbyState(LobbyPresentationState lobby)
        {
            var isOpen = lobby.IsFeatureOpen && lobby.OpenFeature == LobbyFeature.Forge;
            if (isOpen == _wasOpen)
            {
                return;
            }

            _wasOpen = isOpen;
            _state.Set(Current.WithOpen(isOpen));
            if (isOpen)
            {
                ReloadAsync(CancellationToken.None).Forget();
            }
        }

        private sealed class LobbyStateObserver : IObserver<LobbyPresentationState>
        {
            private readonly ForgeController _owner;

            public LobbyStateObserver(ForgeController owner) => _owner = owner;

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
