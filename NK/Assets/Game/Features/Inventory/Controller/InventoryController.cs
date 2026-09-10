using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Features.Inventory.Model;
using Naraka.Features.Lobby.Controller;

namespace Naraka.Features.Inventory.Controller
{
    public interface IInventoryController : IReadOnlyState<InventoryPresentationState>
    {
        void Close();

        void SelectCategory(InventoryCategory category);

        void SelectItem(string itemId);

        /// <summary>打开丢弃确认弹窗。真正的删除只在确认后发生。</summary>
        void RequestDiscard(long quantity);

        /// <summary>打开售卖确认弹窗。</summary>
        void RequestSell(long quantity);

        void CancelConfirm();

        void ConfirmPendingOperation();

        void AutoSort();

        void MoveSelectedTo(int targetSlotIndex);

        void Equip(string slotKind, int slotIndex, string itemId);

        void Unequip(string slotKind, int slotIndex);

        void Expand();

        UniTask ReloadAsync(CancellationToken cancellationToken);

        /// <summary>按当前分类过滤后的槽位。界面直接绘制这个列表。</summary>
        IReadOnlyList<InventorySlotSnapshot> VisibleSlots { get; }
    }

    /// <summary>
    /// 仓库界面的编排。
    ///
    /// 界面显示的数量、容量与档位全部来自服务端快照，本控制器不做任何本地推算：
    /// 丢弃与售卖只发送意图，结果以服务端返回的新快照为准。
    /// </summary>
    public sealed class InventoryController : IController, IInventoryController, IDisposable
    {
        private readonly IGameConfigProvider _config;
        private readonly IInventoryGateway _gateway;
        private readonly ILobbyController _lobby;
        private readonly IServerCapabilities _capabilities;
        private readonly ReactiveState<InventoryPresentationState> _state;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly IDisposable _lobbySubscription;
        private readonly List<InventorySlotSnapshot> _visible = new List<InventorySlotSnapshot>();
        private bool _isLoading;
        private bool _isMutating;
        private bool _wasOpen;

        public InventoryController(
            IGameConfigProvider config,
            IInventoryGateway gateway,
            ILobbyController lobby,
            IServerCapabilities capabilities)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));

            _state = new ReactiveState<InventoryPresentationState>(InventoryPresentationState.Initial);
            _lobbySubscription = _lobby.Subscribe(new LobbyStateObserver(this));
            ApplyLobbyState(_lobby.Current);
        }

        public InventoryPresentationState Current => _state.Current;

        public IReadOnlyList<InventorySlotSnapshot> VisibleSlots
        {
            get
            {
                _visible.Clear();
                foreach (var slot in Current.Snapshot.Slots)
                {
                    if (MatchesCategory(slot.ItemId, Current.Category))
                    {
                        _visible.Add(slot);
                    }
                }

                return _visible;
            }
        }

        // 「哪个面板是打开的」只有大厅一个权威来源。
        public void Close() => _lobby.CloseFeature();

        public void SelectCategory(InventoryCategory category)
        {
            if (Current.Category == category)
            {
                return;
            }

            // 换分类时清空选中格：上一个分类里的选中项在新分类下不可见，
            // 留着它会让右侧详情与左侧网格对不上。
            _state.Set(Current.WithCategory(category).WithSelectedItem(string.Empty).WithConfirmClosed());
        }

        public void SelectItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || Current.SelectedItemId == itemId)
            {
                return;
            }

            _state.Set(Current.WithSelectedItem(itemId).WithConfirmClosed());
        }

        public void RequestDiscard(long quantity) => OpenConfirm(InventoryConfirmKind.Discard, quantity);

        public void RequestSell(long quantity) => OpenConfirm(InventoryConfirmKind.Sell, quantity);

        public void CancelConfirm()
        {
            if (!Current.IsConfirmOpen)
            {
                return;
            }

            _state.Set(Current.WithConfirmClosed());
        }

        public void ConfirmPendingOperation()
        {
            var state = Current;
            if (!state.IsConfirmOpen || state.SelectedItemId.Length == 0 || state.ConfirmQuantity <= 0)
            {
                return;
            }

            var operation = state.ConfirmKind == InventoryConfirmKind.Discard
                ? InventoryOperation.Discard
                : InventoryOperation.Sell;

            _state.Set(state.WithConfirmClosed());
            MutateAsync(operation, state.SelectedItemId, state.ConfirmQuantity, string.Empty, 0, null).Forget();
        }

        public void AutoSort() =>
            MutateAsync(InventoryOperation.AutoSort, string.Empty, 0, string.Empty, 0, null).Forget();

        /// <summary>
        /// 把选中格移动到目标槽位。客户端算出完整的新顺序后整体提交，
        /// 服务端会校验这个排列与实际持有的物品集合完全一致，因此重排不可能凭空增删物品。
        /// </summary>
        public void MoveSelectedTo(int targetSlotIndex)
        {
            var state = Current;
            if (state.SelectedItemId.Length == 0 || targetSlotIndex < 0)
            {
                return;
            }

            var order = new List<string>(state.Snapshot.Slots.Count);
            foreach (var slot in state.Snapshot.Slots)
            {
                if (!string.Equals(slot.ItemId, state.SelectedItemId, StringComparison.Ordinal))
                {
                    order.Add(slot.ItemId);
                }
            }

            if (order.Count == state.Snapshot.Slots.Count)
            {
                // 选中项不在当前快照里，说明界面状态已经过期。
                return;
            }

            var index = targetSlotIndex > order.Count ? order.Count : targetSlotIndex;
            order.Insert(index, state.SelectedItemId);
            MutateAsync(InventoryOperation.Reorder, string.Empty, 0, string.Empty, 0, order).Forget();
        }

        public void Equip(string slotKind, int slotIndex, string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return;
            }

            MutateAsync(InventoryOperation.Equip, itemId, 0, slotKind, slotIndex, null).Forget();
        }

        public void Unequip(string slotKind, int slotIndex) =>
            MutateAsync(InventoryOperation.Unequip, string.Empty, 0, slotKind, slotIndex, null).Forget();

        public void Expand() =>
            MutateAsync(InventoryOperation.Expand, string.Empty, 0, string.Empty, 0, null).Forget();

        public async UniTask ReloadAsync(CancellationToken cancellationToken)
        {
            if (_isLoading)
            {
                return;
            }

            if (!_capabilities.Has(NarakaServerCapabilities.Inventory))
            {
                // 旧云端没有部署仓库协议，此时连发都不能发，否则会被对端当成非法帧直接断线。
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
                    Apply(await _gateway.RequestInventoryAsync(linked.Token));
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

        public IDisposable Subscribe(IObserver<InventoryPresentationState> observer) =>
            _state.Subscribe(observer);

        public void Dispose()
        {
            _lobbySubscription?.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
            _state.Dispose();
        }

        /// <summary>
        /// 丢弃与售卖必须二次确认，且数量不能超过"可自由处置"的数量：
        /// 已装备的份数必须保留，否则装备会指向一个已经不存在的物品。
        /// </summary>
        private void OpenConfirm(InventoryConfirmKind kind, long quantity)
        {
            var state = Current;
            if (state.SelectedItemId.Length == 0 || quantity <= 0 || state.IsBusy)
            {
                return;
            }

            var owned = state.Snapshot.QuantityOf(state.SelectedItemId);
            var reserved = state.Snapshot.EquippedCount(state.SelectedItemId);
            var disposable = owned - reserved;
            if (quantity > disposable)
            {
                _state.Set(state.WithStatusMessage(reserved > 0
                    ? "该物品正在装备中，请先卸下再处理。"
                    : "数量超过当前拥有的数量。"));
                return;
            }

            _state.Set(state.WithConfirm(kind, quantity).WithStatusMessage(string.Empty));
        }

        private async UniTaskVoid MutateAsync(
            InventoryOperation operation,
            string itemId,
            long quantity,
            string slotKind,
            int slotIndex,
            IReadOnlyList<string> itemOrder)
        {
            if (_isMutating || !Current.IsOpen)
            {
                return;
            }

            if (!_capabilities.Has(NarakaServerCapabilities.Inventory))
            {
                _state.Set(Current.WithStatusMessage(
                    LobbyOperationMessages.Describe(LobbyOperationStatus.ServerCapabilityMissing)));
                return;
            }

            _isMutating = true;
            _state.Set(Current.WithBusy(true).WithStatusMessage(string.Empty));
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           CancellationToken.None, _lifetime.Token))
                {
                    var result = await _gateway.MutateAsync(
                        operation, itemId, quantity, slotKind, slotIndex, itemOrder, linked.Token);
                    Apply(result);

                    // 售卖与扩容会改变余额；大厅的余额显示必须跟着刷新，
                    // 否则玩家会看到一个已经过期的数字。
                    if (result.IsSuccess && result.HasBalances)
                    {
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
                _isMutating = false;
            }
        }

        /// <summary>
        /// 应用服务端结果。失败时只更新提示，绝不覆盖上一次成功的快照，
        /// 也不把仓库显示成空的。
        /// </summary>
        private void Apply(InventoryResult result)
        {
            if (result.IsSuccess)
            {
                var next = Current.WithSnapshot(result.Snapshot).WithStatusMessage(string.Empty);

                // 选中的物品可能已经被丢光；此时清空选中而不是留下一个指向空气的详情。
                if (next.SelectedItemId.Length > 0 && next.Snapshot.QuantityOf(next.SelectedItemId) <= 0)
                {
                    next = next.WithSelectedItem(string.Empty);
                }

                _state.Set(next);
                return;
            }

            _state.Set(Current
                .WithLoading(false)
                .WithBusy(false)
                .WithStatusMessage(LobbyOperationMessages.Describe(result.Status)));
        }

        private bool MatchesCategory(string itemId, InventoryCategory category)
        {
            if (category == InventoryCategory.All)
            {
                return true;
            }

            if (!_config.IsLoaded || !_config.Catalog.TryGetItem(itemId, out var item))
            {
                return false;
            }

            return string.Equals(item.Category, ToConfigCategory(category), StringComparison.Ordinal);
        }

        private static string ToConfigCategory(InventoryCategory category)
        {
            switch (category)
            {
                case InventoryCategory.Soulstone:
                    return ConfigItemCategory.Soulstone;
                case InventoryCategory.Material:
                    return ConfigItemCategory.Material;
                case InventoryCategory.Special:
                    return ConfigItemCategory.Special;
                case InventoryCategory.Consumable:
                    return ConfigItemCategory.Consumable;
                case InventoryCategory.Armor:
                    return ConfigItemCategory.Armor;
                default:
                    return string.Empty;
            }
        }

        private void ApplyLobbyState(LobbyPresentationState lobby)
        {
            var isOpen = lobby.IsFeatureOpen && lobby.OpenFeature == LobbyFeature.Inventory;
            if (isOpen == _wasOpen)
            {
                return;
            }

            _wasOpen = isOpen;
            _state.Set(Current.WithOpen(isOpen).WithConfirmClosed());

            // 每次打开都重新拉一次：仓库可能被商店、锻造或抽奖改过。
            if (isOpen)
            {
                ReloadAsync(CancellationToken.None).Forget();
            }
        }

        private sealed class LobbyStateObserver : IObserver<LobbyPresentationState>
        {
            private readonly InventoryController _owner;

            public LobbyStateObserver(InventoryController owner) => _owner = owner;

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
