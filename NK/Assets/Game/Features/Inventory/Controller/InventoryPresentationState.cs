using Naraka.Core.Application.MVC;
using Naraka.Features.Inventory.Model;

namespace Naraka.Features.Inventory.Controller
{
    /// <summary>仓库分类页签。与配置中的物品分类一一对应，另加一个"全部"。</summary>
    public enum InventoryCategory
    {
        All,
        Soulstone,
        Material,
        Special,
        Consumable,
        Armor
    }

    /// <summary>二次确认弹窗。丢弃与售卖都必须经过它。</summary>
    public enum InventoryConfirmKind
    {
        None,
        Discard,
        Sell
    }

    /// <summary>
    /// 仓库界面的只读展示状态。
    ///
    /// 数量、容量与档位全部来自服务端快照；本状态自己拥有的只有页签、选中格与确认弹窗
    /// 这类纯界面信息。
    /// </summary>
    public readonly struct InventoryPresentationState : IPresentationState
    {
        public InventoryPresentationState(
            bool isOpen,
            bool isLoading,
            bool isBusy,
            bool hasSnapshot,
            InventorySnapshot snapshot,
            InventoryCategory category,
            string selectedItemId,
            InventoryConfirmKind confirmKind,
            long confirmQuantity,
            string statusMessage)
        {
            IsOpen = isOpen;
            IsLoading = isLoading;
            IsBusy = isBusy;
            HasSnapshot = hasSnapshot;
            Snapshot = snapshot ?? InventorySnapshot.Empty;
            Category = category;
            SelectedItemId = selectedItemId ?? string.Empty;
            ConfirmKind = confirmKind;
            ConfirmQuantity = confirmQuantity < 0 ? 0 : confirmQuantity;
            StatusMessage = statusMessage ?? string.Empty;
        }

        public static InventoryPresentationState Initial => new InventoryPresentationState(
            false, false, false, false, InventorySnapshot.Empty,
            InventoryCategory.All, string.Empty, InventoryConfirmKind.None, 0, string.Empty);

        public bool IsOpen { get; }

        public bool IsLoading { get; }

        /// <summary>写请求在途。期间禁用全部操作按钮，避免重复点击产生多次写请求。</summary>
        public bool IsBusy { get; }

        /// <summary>是否已经拿到过一次服务端快照。为 false 时界面保持占位而不是显示空仓库。</summary>
        public bool HasSnapshot { get; }

        public InventorySnapshot Snapshot { get; }

        public InventoryCategory Category { get; }

        public string SelectedItemId { get; }

        public InventoryConfirmKind ConfirmKind { get; }

        public long ConfirmQuantity { get; }

        public string StatusMessage { get; }

        public bool IsConfirmOpen => ConfirmKind != InventoryConfirmKind.None;

        public InventoryPresentationState WithOpen(bool isOpen) => Copy(isOpen: isOpen);

        public InventoryPresentationState WithLoading(bool isLoading) => Copy(isLoading: isLoading);

        public InventoryPresentationState WithBusy(bool isBusy) => Copy(isBusy: isBusy);

        public InventoryPresentationState WithSnapshot(InventorySnapshot snapshot) =>
            Copy(hasSnapshot: true, isLoading: false, isBusy: false, snapshot: snapshot);

        public InventoryPresentationState WithoutSnapshot() =>
            Copy(hasSnapshot: false, snapshot: InventorySnapshot.Empty, selectedItemId: string.Empty);

        public InventoryPresentationState WithCategory(InventoryCategory category) =>
            Copy(category: category);

        public InventoryPresentationState WithSelectedItem(string itemId) => Copy(selectedItemId: itemId);

        public InventoryPresentationState WithConfirm(InventoryConfirmKind kind, long quantity) =>
            Copy(confirmKind: kind, confirmQuantity: quantity);

        public InventoryPresentationState WithConfirmClosed() =>
            Copy(confirmKind: InventoryConfirmKind.None, confirmQuantity: 0);

        public InventoryPresentationState WithStatusMessage(string statusMessage) =>
            Copy(statusMessage: statusMessage);

        private InventoryPresentationState Copy(
            bool? isOpen = null,
            bool? isLoading = null,
            bool? isBusy = null,
            bool? hasSnapshot = null,
            InventorySnapshot snapshot = null,
            InventoryCategory? category = null,
            string selectedItemId = null,
            InventoryConfirmKind? confirmKind = null,
            long? confirmQuantity = null,
            string statusMessage = null) =>
            new InventoryPresentationState(
                isOpen ?? IsOpen,
                isLoading ?? IsLoading,
                isBusy ?? IsBusy,
                hasSnapshot ?? HasSnapshot,
                snapshot ?? Snapshot,
                category ?? Category,
                selectedItemId ?? SelectedItemId,
                confirmKind ?? ConfirmKind,
                confirmQuantity ?? ConfirmQuantity,
                statusMessage ?? StatusMessage);
    }
}
