using System;
using System.Collections.Generic;
using Naraka.Core.Application.MVC;

namespace Naraka.Features.Shop.Controller
{
    /// <summary>商店分类页签。与配置中的物品分类一一对应，另加一个"全部"。</summary>
    public enum ShopCategory
    {
        All,
        Soulstone,
        Material,
        Special,
        Consumable,
        Armor
    }

    /// <summary>
    /// 商店界面的只读展示状态。
    ///
    /// 购买计数与余额来自服务端；商品目录来自两端共享的配置，因此不在这里保存。
    /// </summary>
    public readonly struct ShopPresentationState : IPresentationState
    {
        /// <summary>单次购买数量上限。与服务端 <c>ShopService.MaximumQuantityPerPurchase</c> 一致。</summary>
        public const int MaximumQuantityPerPurchase = 999;

        private readonly IReadOnlyList<ShopPurchaseCount> _purchases;

        public ShopPresentationState(
            bool isOpen,
            bool isLoading,
            bool isBusy,
            bool hasServerState,
            IReadOnlyList<ShopPurchaseCount> purchases,
            long copper,
            long silk,
            long gold,
            ShopCategory category,
            string selectedProductId,
            bool isDialogOpen,
            int purchaseQuantity,
            string statusMessage)
        {
            IsOpen = isOpen;
            IsLoading = isLoading;
            IsBusy = isBusy;
            HasServerState = hasServerState;
            _purchases = purchases ?? Array.Empty<ShopPurchaseCount>();
            Copper = copper;
            Silk = silk;
            Gold = gold;
            Category = category;
            SelectedProductId = selectedProductId ?? string.Empty;
            IsDialogOpen = isDialogOpen;
            PurchaseQuantity = purchaseQuantity < 1 ? 1 : purchaseQuantity;
            StatusMessage = statusMessage ?? string.Empty;
        }

        public static ShopPresentationState Initial => new ShopPresentationState(
            false, false, false, false, Array.Empty<ShopPurchaseCount>(), 0, 0, 0,
            ShopCategory.All, string.Empty, false, 1, string.Empty);

        public bool IsOpen { get; }

        public bool IsLoading { get; }

        /// <summary>购买请求在途。期间禁用确认按钮，避免重复点击产生两笔订单。</summary>
        public bool IsBusy { get; }

        /// <summary>是否已经拿到过一次服务端数据。为 false 时界面保持占位。</summary>
        public bool HasServerState { get; }

        public IReadOnlyList<ShopPurchaseCount> Purchases => _purchases ?? Array.Empty<ShopPurchaseCount>();

        public long Copper { get; }

        public long Silk { get; }

        public long Gold { get; }

        public ShopCategory Category { get; }

        public string SelectedProductId { get; }

        public bool IsDialogOpen { get; }

        public int PurchaseQuantity { get; }

        public string StatusMessage { get; }

        public long PurchasedTotalOf(string productId)
        {
            foreach (var purchase in Purchases)
            {
                if (string.Equals(purchase.ProductId, productId, StringComparison.Ordinal))
                {
                    return purchase.PurchasedTotal;
                }
            }

            return 0L;
        }

        public long BalanceOf(string currencyId)
        {
            switch (currencyId)
            {
                case "Copper": return Copper;
                case "Silk": return Silk;
                case "Gold": return Gold;
                default: return 0L;
            }
        }

        public ShopPresentationState WithOpen(bool isOpen) => Copy(isOpen: isOpen);

        public ShopPresentationState WithLoading(bool isLoading) => Copy(isLoading: isLoading);

        public ShopPresentationState WithBusy(bool isBusy) => Copy(isBusy: isBusy);

        public ShopPresentationState WithServerState(
            IReadOnlyList<ShopPurchaseCount> purchases, long copper, long silk, long gold) =>
            Copy(
                hasServerState: true,
                isLoading: false,
                isBusy: false,
                purchases: purchases,
                copper: copper,
                silk: silk,
                gold: gold);

        public ShopPresentationState WithCategory(ShopCategory category) => Copy(category: category);

        public ShopPresentationState WithSelectedProduct(string productId) =>
            Copy(selectedProductId: productId);

        public ShopPresentationState WithDialogOpen(int quantity) =>
            Copy(isDialogOpen: true, purchaseQuantity: quantity);

        public ShopPresentationState WithDialogClosed() =>
            Copy(isDialogOpen: false, purchaseQuantity: 1);

        public ShopPresentationState WithPurchaseQuantity(int quantity) =>
            Copy(purchaseQuantity: quantity);

        public ShopPresentationState WithStatusMessage(string statusMessage) =>
            Copy(statusMessage: statusMessage);

        private ShopPresentationState Copy(
            bool? isOpen = null,
            bool? isLoading = null,
            bool? isBusy = null,
            bool? hasServerState = null,
            IReadOnlyList<ShopPurchaseCount> purchases = null,
            long? copper = null,
            long? silk = null,
            long? gold = null,
            ShopCategory? category = null,
            string selectedProductId = null,
            bool? isDialogOpen = null,
            int? purchaseQuantity = null,
            string statusMessage = null) =>
            new ShopPresentationState(
                isOpen ?? IsOpen,
                isLoading ?? IsLoading,
                isBusy ?? IsBusy,
                hasServerState ?? HasServerState,
                purchases ?? Purchases,
                copper ?? Copper,
                silk ?? Silk,
                gold ?? Gold,
                category ?? Category,
                selectedProductId ?? SelectedProductId,
                isDialogOpen ?? IsDialogOpen,
                purchaseQuantity ?? PurchaseQuantity,
                statusMessage ?? StatusMessage);
    }
}
