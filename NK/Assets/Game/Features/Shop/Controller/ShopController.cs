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

namespace Naraka.Features.Shop.Controller
{
    /// <summary>某个商品的账号累计购买量。限购上限来自配置。</summary>
    public readonly struct ShopPurchaseCount
    {
        public ShopPurchaseCount(string productId, long purchasedTotal)
        {
            ProductId = productId ?? string.Empty;
            PurchasedTotal = purchasedTotal;
        }

        public string ProductId { get; }

        public long PurchasedTotal { get; }
    }

    public readonly struct ShopResult
    {
        private ShopResult(
            LobbyOperationStatus status,
            IReadOnlyList<ShopPurchaseCount> purchases,
            long copper,
            long silk,
            long gold)
        {
            Status = status;
            Purchases = purchases;
            Copper = copper;
            Silk = silk;
            Gold = gold;
        }

        public LobbyOperationStatus Status { get; }

        public IReadOnlyList<ShopPurchaseCount> Purchases { get; }

        public long Copper { get; }

        public long Silk { get; }

        public long Gold { get; }

        public bool IsSuccess => Status == LobbyOperationStatus.Success && Purchases != null;

        public static ShopResult Success(
            IReadOnlyList<ShopPurchaseCount> purchases, long copper, long silk, long gold) =>
            new ShopResult(LobbyOperationStatus.Success, purchases, copper, silk, gold);

        public static ShopResult Failed(LobbyOperationStatus status) =>
            new ShopResult(status, null, 0, 0, 0);
    }

    /// <summary>
    /// 商店网关。
    ///
    /// 商品目录本身<b>不</b>走网络：价格、分类与限购都在两端共享的生成配置里，
    /// 网络上只传"这个账号买过多少"和余额这类账号事实。
    /// </summary>
    public interface IShopGateway
    {
        UniTask<ShopResult> RequestShopAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 购买。总价由服务端按配置计算，这里只提交商品 ID 与数量；
        /// 同一个 OrderId 重复提交只会扣一次费。
        /// </summary>
        UniTask<ShopResult> PurchaseAsync(
            string productId,
            int quantity,
            string orderId,
            CancellationToken cancellationToken);
    }

    public interface IShopController : IReadOnlyState<ShopPresentationState>
    {
        void Close();

        void SelectCategory(ShopCategory category);

        void SelectProduct(string productId);

        /// <summary>打开购买弹窗。数量从 1 起。</summary>
        void OpenPurchaseDialog();

        void ChangePurchaseQuantity(int delta);

        void SetPurchaseQuantity(int quantity);

        void CancelPurchase();

        void ConfirmPurchase();

        UniTask ReloadAsync(CancellationToken cancellationToken);

        /// <summary>按当前分类过滤后的商品。界面直接绘制这个列表。</summary>
        IReadOnlyList<ShopProductConfig> VisibleProducts { get; }

        /// <summary>当前选中商品的剩余可购数量。0 表示不限购或已售罄，由 IsSoldOut 区分。</summary>
        long RemainingPurchaseLimit { get; }
    }

    /// <summary>
    /// 商店界面的编排。
    ///
    /// 价格与限购只用于<b>显示</b>：真正的扣费、限购判定与发货都在服务端完成，
    /// 因此这里算出的总价永远不会成为账号资产变化的依据。
    /// </summary>
    public sealed class ShopController : IController, IShopController, IDisposable
    {
        private readonly IGameConfigProvider _config;
        private readonly IShopGateway _gateway;
        private readonly ILobbyController _lobby;
        private readonly IServerCapabilities _capabilities;
        private readonly ReactiveState<ShopPresentationState> _state;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly IDisposable _lobbySubscription;
        private readonly List<ShopProductConfig> _visible = new List<ShopProductConfig>();
        private bool _isLoading;
        private bool _isPurchasing;
        private bool _wasOpen;

        public ShopController(
            IGameConfigProvider config,
            IShopGateway gateway,
            ILobbyController lobby,
            IServerCapabilities capabilities)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));

            _state = new ReactiveState<ShopPresentationState>(ShopPresentationState.Initial);
            _lobbySubscription = _lobby.Subscribe(new LobbyStateObserver(this));
            ApplyLobbyState(_lobby.Current);
        }

        public ShopPresentationState Current => _state.Current;

        public IReadOnlyList<ShopProductConfig> VisibleProducts
        {
            get
            {
                _visible.Clear();
                if (!_config.IsLoaded)
                {
                    return _visible;
                }

                foreach (var product in _config.Catalog.ShopProductsInDisplayOrder)
                {
                    if (product.IsAvailable && MatchesCategory(product, Current.Category))
                    {
                        _visible.Add(product);
                    }
                }

                return _visible;
            }
        }

        public long RemainingPurchaseLimit
        {
            get
            {
                var state = Current;
                if (state.SelectedProductId.Length == 0 || !_config.IsLoaded ||
                    !_config.Catalog.TryGetShopProduct(state.SelectedProductId, out var product) ||
                    product.PurchaseLimit <= 0)
                {
                    return 0;
                }

                var remaining = product.PurchaseLimit - state.PurchasedTotalOf(state.SelectedProductId);
                return remaining < 0 ? 0 : remaining;
            }
        }

        public void Close() => _lobby.CloseFeature();

        public void SelectCategory(ShopCategory category)
        {
            if (Current.Category == category)
            {
                return;
            }

            _state.Set(Current.WithCategory(category).WithSelectedProduct(string.Empty).WithDialogClosed());
        }

        public void SelectProduct(string productId)
        {
            if (string.IsNullOrEmpty(productId) || Current.SelectedProductId == productId)
            {
                return;
            }

            _state.Set(Current.WithSelectedProduct(productId).WithDialogClosed());
        }

        public void OpenPurchaseDialog()
        {
            var state = Current;
            if (state.SelectedProductId.Length == 0 || state.IsBusy || state.IsDialogOpen)
            {
                return;
            }

            if (IsSoldOut(state.SelectedProductId))
            {
                _state.Set(state.WithStatusMessage(
                    LobbyOperationMessages.Describe(LobbyOperationStatus.LimitReached)));
                return;
            }

            _state.Set(state.WithDialogOpen(1).WithStatusMessage(string.Empty));
        }

        public void ChangePurchaseQuantity(int delta) =>
            SetPurchaseQuantity(Current.PurchaseQuantity + delta);

        /// <summary>
        /// 设定购买数量。上界同时受限购剩余量与单次上限约束，
        /// 因此界面上根本无法拼出一个必定被服务端拒绝的数量。
        /// </summary>
        public void SetPurchaseQuantity(int quantity)
        {
            var state = Current;
            if (!state.IsDialogOpen)
            {
                return;
            }

            var maximum = MaximumPurchasableQuantity(state.SelectedProductId, state);
            var clamped = quantity < 1 ? 1 : quantity;
            if (clamped > maximum)
            {
                clamped = maximum;
            }

            if (clamped == state.PurchaseQuantity)
            {
                return;
            }

            _state.Set(state.WithPurchaseQuantity(clamped));
        }

        public void CancelPurchase()
        {
            if (!Current.IsDialogOpen)
            {
                return;
            }

            _state.Set(Current.WithDialogClosed());
        }

        public void ConfirmPurchase()
        {
            var state = Current;
            if (!state.IsDialogOpen || state.SelectedProductId.Length == 0 || state.PurchaseQuantity <= 0)
            {
                return;
            }

            _state.Set(state.WithDialogClosed());
            PurchaseAsync(state.SelectedProductId, state.PurchaseQuantity).Forget();
        }

        public async UniTask ReloadAsync(CancellationToken cancellationToken)
        {
            if (_isLoading)
            {
                return;
            }

            if (!_capabilities.Has(NarakaServerCapabilities.Shop))
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
                    Apply(await _gateway.RequestShopAsync(linked.Token));
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

        public IDisposable Subscribe(IObserver<ShopPresentationState> observer) => _state.Subscribe(observer);

        public void Dispose()
        {
            _lobbySubscription?.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
            _state.Dispose();
        }

        /// <summary>
        /// 单次购买上限：单次数量上限、限购剩余量与"当前余额买得起多少"三者取最小。
        /// 余额上界只是界面便利，服务端仍然会重新判定。
        /// </summary>
        private int MaximumPurchasableQuantity(string productId, ShopPresentationState state)
        {
            var maximum = ShopPresentationState.MaximumQuantityPerPurchase;
            if (!_config.IsLoaded || !_config.Catalog.TryGetShopProduct(productId, out var product))
            {
                return 1;
            }

            if (product.PurchaseLimit > 0)
            {
                var remaining = product.PurchaseLimit - state.PurchasedTotalOf(productId);
                maximum = (int)Math.Min(maximum, Math.Max(remaining, 0));
            }

            if (product.UnitPrice > 0)
            {
                var affordable = state.BalanceOf(product.CurrencyId) / product.UnitPrice;
                maximum = (int)Math.Min(maximum, Math.Max(affordable, 0));
            }

            return maximum < 1 ? 1 : maximum;
        }

        private bool IsSoldOut(string productId)
        {
            if (!_config.IsLoaded || !_config.Catalog.TryGetShopProduct(productId, out var product) ||
                product.PurchaseLimit <= 0)
            {
                return false;
            }

            return Current.PurchasedTotalOf(productId) >= product.PurchaseLimit;
        }

        private async UniTaskVoid PurchaseAsync(string productId, int quantity)
        {
            if (_isPurchasing || !Current.IsOpen)
            {
                return;
            }

            if (!_capabilities.Has(NarakaServerCapabilities.Shop))
            {
                _state.Set(Current.WithStatusMessage(
                    LobbyOperationMessages.Describe(LobbyOperationStatus.ServerCapabilityMissing)));
                return;
            }

            _isPurchasing = true;
            _state.Set(Current.WithBusy(true).WithStatusMessage(string.Empty));
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           CancellationToken.None, _lifetime.Token))
                {
                    // 每次购买生成一个新的 OrderId。重试同一笔订单由界面重新点击触发，
                    // 届时会带上新的 OrderId，因此不会把两次真实购买合并成一次。
                    var result = await _gateway.PurchaseAsync(
                        productId, quantity, Guid.NewGuid().ToString("N"), linked.Token);
                    Apply(result);

                    if (result.IsSuccess)
                    {
                        // 购买改变了余额与仓库；大厅余额必须跟着刷新。
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
                _isPurchasing = false;
            }
        }

        private void Apply(ShopResult result)
        {
            if (result.IsSuccess)
            {
                _state.Set(Current
                    .WithServerState(result.Purchases, result.Copper, result.Silk, result.Gold)
                    .WithStatusMessage(string.Empty));
                return;
            }

            // 失败只更新提示，绝不覆盖上一次成功的购买计数与余额。
            _state.Set(Current
                .WithLoading(false)
                .WithBusy(false)
                .WithStatusMessage(LobbyOperationMessages.Describe(result.Status)));
        }

        private bool MatchesCategory(ShopProductConfig product, ShopCategory category)
        {
            if (category == ShopCategory.All)
            {
                return true;
            }

            return string.Equals(product.ShopCategory, ToConfigCategory(category), StringComparison.Ordinal);
        }

        private static string ToConfigCategory(ShopCategory category)
        {
            switch (category)
            {
                case ShopCategory.Soulstone:
                    return ConfigItemCategory.Soulstone;
                case ShopCategory.Material:
                    return ConfigItemCategory.Material;
                case ShopCategory.Special:
                    return ConfigItemCategory.Special;
                case ShopCategory.Consumable:
                    return ConfigItemCategory.Consumable;
                case ShopCategory.Armor:
                    return ConfigItemCategory.Armor;
                default:
                    return string.Empty;
            }
        }

        private void ApplyLobbyState(LobbyPresentationState lobby)
        {
            var isOpen = lobby.IsFeatureOpen && lobby.OpenFeature == LobbyFeature.Shop;
            if (isOpen == _wasOpen)
            {
                return;
            }

            _wasOpen = isOpen;
            _state.Set(Current.WithOpen(isOpen).WithDialogClosed());
            if (isOpen)
            {
                ReloadAsync(CancellationToken.None).Forget();
            }
        }

        private sealed class LobbyStateObserver : IObserver<LobbyPresentationState>
        {
            private readonly ShopController _owner;

            public LobbyStateObserver(ShopController owner) => _owner = owner;

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
