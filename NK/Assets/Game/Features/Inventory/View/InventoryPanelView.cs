using System;
using System.Collections.Generic;
using System.Globalization;
using Naraka.Config;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.MVC;
using Naraka.Features.Inventory.Controller;
using Naraka.Features.Inventory.Model;
using Naraka.Features.Loadout.View;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Naraka.Features.Inventory.View
{
    /// <summary>
    /// 仓库界面。
    ///
    /// 左侧网格按分类展示堆叠物品，数量画在图标右上角；右侧是选中物品的详情与操作；
    /// 丢弃与售卖都必须经过二次确认。所有数量与容量都直接来自服务端快照，
    /// View 不做任何本地推算。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class InventoryPanelView :
        MonoBehaviour,
        IView<InventoryPresentationState>,
        IObserver<InventoryPresentationState>
    {
        private const string CellClass = "inv-cell";
        private const string CellSelectedClass = "inv-cell--selected";
        private const string CategoryActiveClass = "inv-category--active";

        [SerializeField] private VisualTreeAsset inventoryLayout;
        [SerializeField] private LoadoutIconCatalog icons;

        private readonly List<KeyValuePair<Button, Action>> _handlers =
            new List<KeyValuePair<Button, Action>>();

        private readonly List<KeyValuePair<Button, string>> _cells =
            new List<KeyValuePair<Button, string>>();

        private readonly List<KeyValuePair<Button, InventoryCategory>> _categories =
            new List<KeyValuePair<Button, InventoryCategory>>();

        private readonly List<Button> _equipSlots = new List<Button>();

        private IInventoryController _controller;
        private IGameConfigProvider _config;
        private IDisposable _subscription;
        private VisualElement _screen;
        private VisualElement _grid;
        private VisualElement _equipBar;
        private VisualElement _detailIcon;
        private VisualElement _confirm;
        private Label _capacity;
        private Label _detailName;
        private Label _detailQuality;
        private Label _detailOwned;
        private Label _detailStack;
        private Label _detailPrice;
        private Label _detailDescription;
        private Label _confirmMessage;
        private Label _status;
        private Button _close;
        private Button _sort;
        private Button _expand;
        private Button _discard;
        private Button _sell;
        private Button _confirmOk;
        private Button _confirmCancel;

        [Inject]
        public void Construct(IInventoryController controller, IGameConfigProvider config)
        {
            _controller = controller;
            _config = config;
        }

        private void Start()
        {
            if (!TryBuild(GetComponent<UIDocument>().rootVisualElement))
            {
                return;
            }

            _subscription = _controller.Subscribe(this);
        }

        public void Render(InventoryPresentationState state)
        {
            if (_screen == null)
            {
                return;
            }

            _screen.style.display = state.IsOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!state.IsOpen)
            {
                return;
            }

            _status.text = state.StatusMessage;
            _status.style.display = string.IsNullOrEmpty(state.StatusMessage)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            foreach (var entry in _categories)
            {
                entry.Key.EnableInClassList(CategoryActiveClass, entry.Value == state.Category);
                entry.Key.SetEnabled(!state.IsBusy);
            }

            if (!state.HasSnapshot)
            {
                // 还没有权威快照时显示加载中，绝不把仓库画成空的。
                _capacity.text = state.IsLoading ? "正在读取仓库…" : "仓库数据不可用";
                _grid.Clear();
                _cells.Clear();
                ApplyDetail(state);
                _confirm.style.display = DisplayStyle.None;
                SetActionsEnabled(false);
                return;
            }

            _capacity.text = "容量 " +
                             state.Snapshot.UsedSlots.ToString(CultureInfo.InvariantCulture) + " / " +
                             state.Snapshot.Capacity.ToString(CultureInfo.InvariantCulture) +
                             "    档位 " + state.Snapshot.Tier.ToString(CultureInfo.InvariantCulture);

            RebuildGrid(state);
            RebuildEquipBar(state);
            ApplyDetail(state);

            _confirm.style.display = state.IsConfirmOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (state.IsConfirmOpen)
            {
                var verb = state.ConfirmKind == InventoryConfirmKind.Discard ? "丢弃" : "售卖";
                _confirmMessage.text =
                    "确认" + verb + " " + DisplayNameOf(state.SelectedItemId) + " × " +
                    state.ConfirmQuantity.ToString(CultureInfo.InvariantCulture) + " 吗？此操作不可撤销。";
            }

            SetActionsEnabled(!state.IsBusy);
        }

        public void OnNext(InventoryPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
            if (_status != null)
            {
                _status.text = "仓库界面发生错误。";
                _status.style.display = DisplayStyle.Flex;
            }
        }

        public void OnCompleted()
        {
        }

        private void SetActionsEnabled(bool enabled)
        {
            _sort.SetEnabled(enabled);
            _expand.SetEnabled(enabled);
            var hasSelection = enabled && Current().SelectedItemId.Length > 0;
            _discard.SetEnabled(hasSelection);
            _sell.SetEnabled(hasSelection && SellPriceOf(Current().SelectedItemId) > 0);
            foreach (var cell in _cells)
            {
                cell.Key.SetEnabled(enabled);
            }
        }

        private InventoryPresentationState Current() => _controller.Current;

        /// <summary>
        /// 重建网格。只在可见集合发生变化时重建元素，翻分类与刷新不会每帧重建。
        /// </summary>
        private void RebuildGrid(InventoryPresentationState state)
        {
            var visible = _controller.VisibleSlots;
            if (_cells.Count == visible.Count && SameItems(visible))
            {
                ApplyCellSelection(state);
                return;
            }

            foreach (var entry in _cells)
            {
                Unbind(entry.Key);
            }

            _cells.Clear();
            _grid.Clear();

            foreach (var slot in visible)
            {
                var itemId = slot.ItemId;
                var cell = new Button { name = "InventoryCell_" + itemId };
                cell.AddToClassList(CellClass);

                var texture = IconOf(itemId);
                if (texture != null)
                {
                    cell.style.backgroundImage = Background.FromTexture2D(texture);
                }
                else
                {
                    cell.text = DisplayNameOf(itemId);
                }

                var quantity = new Label(slot.Quantity.ToString(CultureInfo.InvariantCulture));
                quantity.AddToClassList("inv-cell-quantity");
                quantity.pickingMode = PickingMode.Ignore;
                cell.Add(quantity);

                if (state.Snapshot.EquippedCount(itemId) > 0)
                {
                    var badge = new Label("装备中");
                    badge.AddToClassList("inv-cell-equipped");
                    badge.pickingMode = PickingMode.Ignore;
                    cell.Add(badge);
                }

                Bind(cell, () => _controller.SelectItem(itemId));
                _grid.Add(cell);
                _cells.Add(new KeyValuePair<Button, string>(cell, itemId));
            }

            ApplyCellSelection(state);
        }

        private bool SameItems(IReadOnlyList<InventorySlotSnapshot> visible)
        {
            for (var i = 0; i < visible.Count; i++)
            {
                if (!string.Equals(_cells[i].Value, visible[i].ItemId, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyCellSelection(InventoryPresentationState state)
        {
            foreach (var entry in _cells)
            {
                entry.Key.EnableInClassList(
                    CellSelectedClass,
                    string.Equals(entry.Value, state.SelectedItemId, StringComparison.Ordinal));
            }
        }

        /// <summary>
        /// 战斗负载：6 个魂玉槽 + 1 个护甲槽。点击空槽装备当前选中物品，点击已占用的槽卸下。
        /// </summary>
        private void RebuildEquipBar(InventoryPresentationState state)
        {
            if (_equipSlots.Count == 0)
            {
                BuildEquipSlot(InventorySnapshot.SoulstoneSlotKind, InventorySnapshot.SoulstoneSlotCount);
                BuildEquipSlot(InventorySnapshot.ArmorSlotKind, InventorySnapshot.ArmorSlotCount);
            }

            foreach (var button in _equipSlots)
            {
                var parts = button.name.Split('_');
                var slotKind = parts[1];
                var slotIndex = int.Parse(parts[2], CultureInfo.InvariantCulture);
                var itemId = state.Snapshot.EquippedAt(slotKind, slotIndex);

                if (itemId.Length == 0)
                {
                    button.style.backgroundImage = StyleKeyword.None;
                    button.text = slotKind == InventorySnapshot.ArmorSlotKind ? "护甲" : "魂玉";
                }
                else
                {
                    var texture = IconOf(itemId);
                    button.text = texture == null ? DisplayNameOf(itemId) : string.Empty;
                    if (texture != null)
                    {
                        button.style.backgroundImage = Background.FromTexture2D(texture);
                    }
                }

                button.SetEnabled(!state.IsBusy);
            }
        }

        private void BuildEquipSlot(string slotKind, int count)
        {
            for (var index = 0; index < count; index++)
            {
                var slotIndex = index;
                var button = new Button { name = "InventoryEquip_" + slotKind + "_" + index };
                button.AddToClassList("inv-equip-slot");
                Bind(button, () => ToggleEquip(slotKind, slotIndex));
                _equipBar.Add(button);
                _equipSlots.Add(button);
            }
        }

        private void ToggleEquip(string slotKind, int slotIndex)
        {
            var state = _controller.Current;
            if (state.Snapshot.EquippedAt(slotKind, slotIndex).Length > 0)
            {
                _controller.Unequip(slotKind, slotIndex);
                return;
            }

            _controller.Equip(slotKind, slotIndex, state.SelectedItemId);
        }

        private void ApplyDetail(InventoryPresentationState state)
        {
            var itemId = state.SelectedItemId;
            if (itemId.Length == 0 || _config == null || !_config.IsLoaded ||
                !_config.Catalog.TryGetItem(itemId, out var item))
            {
                _detailName.text = "未选择物品";
                _detailQuality.text = string.Empty;
                _detailOwned.text = string.Empty;
                _detailStack.text = string.Empty;
                _detailPrice.text = string.Empty;
                _detailDescription.text = string.Empty;
                _detailIcon.style.backgroundImage = StyleKeyword.None;
                return;
            }

            _detailName.text = item.DisplayName;
            _detailQuality.text = "品质 " + DescribeQuality(item.Quality);
            _detailOwned.text = "拥有 " +
                                state.Snapshot.QuantityOf(itemId).ToString(CultureInfo.InvariantCulture);
            _detailStack.text = "堆叠上限 " + item.StackLimit.ToString(CultureInfo.InvariantCulture);
            _detailPrice.text = item.SellPrice > 0
                ? "售价 " + item.SellPrice.ToString(CultureInfo.InvariantCulture) + " " +
                  _config.Catalog.GetCurrencyDisplayName(item.SellCurrencyId) + " / 个"
                : "该物品不可售卖";
            _detailDescription.text = item.Description;

            var texture = IconOf(itemId);
            if (texture != null)
            {
                _detailIcon.style.backgroundImage = Background.FromTexture2D(texture);
            }
        }

        private static string DescribeQuality(string quality)
        {
            switch (quality)
            {
                case ConfigQuality.White: return "白";
                case ConfigQuality.Blue: return "蓝";
                case ConfigQuality.Purple: return "紫";
                case ConfigQuality.Gold: return "金";
                case ConfigQuality.Red: return "红";
                default: return quality;
            }
        }

        private long SellPriceOf(string itemId) =>
            _config != null && _config.IsLoaded && _config.Catalog.TryGetItem(itemId, out var item)
                ? item.SellPrice
                : 0L;

        private string DisplayNameOf(string itemId) =>
            _config != null && _config.IsLoaded ? _config.Catalog.GetItemDisplayName(itemId) : itemId;

        private Texture2D IconOf(string itemId)
        {
            if (icons == null || _config == null || !_config.IsLoaded ||
                !_config.Catalog.TryGetItem(itemId, out var item))
            {
                return null;
            }

            return icons.GetByKey(item.IconKey);
        }

        private bool TryBuild(VisualElement root)
        {
            if (inventoryLayout == null)
            {
                Debug.LogError(
                    "InventoryPanelView 未绑定 InventoryPanel.uxml；请运行菜单 NARAKA/Setup/Apply P0 Project Settings。",
                    this);
                return false;
            }

            inventoryLayout.CloneTree(root);
            _screen = root.Q<VisualElement>("InventoryScreen");
            _grid = root.Q<VisualElement>("InventoryGrid");
            _equipBar = root.Q<VisualElement>("InventoryEquipBar");
            _detailIcon = root.Q<VisualElement>("InventoryDetailIcon");
            _confirm = root.Q<VisualElement>("InventoryConfirm");
            _capacity = root.Q<Label>("InventoryCapacityLabel");
            _detailName = root.Q<Label>("InventoryDetailName");
            _detailQuality = root.Q<Label>("InventoryDetailQuality");
            _detailOwned = root.Q<Label>("InventoryDetailOwned");
            _detailStack = root.Q<Label>("InventoryDetailStack");
            _detailPrice = root.Q<Label>("InventoryDetailPrice");
            _detailDescription = root.Q<Label>("InventoryDetailDescription");
            _confirmMessage = root.Q<Label>("InventoryConfirmMessage");
            _status = root.Q<Label>("InventoryStatusLabel");
            _close = root.Q<Button>("InventoryCloseButton");
            _sort = root.Q<Button>("InventorySortButton");
            _expand = root.Q<Button>("InventoryExpandButton");
            _discard = root.Q<Button>("InventoryDiscardButton");
            _sell = root.Q<Button>("InventorySellButton");
            _confirmOk = root.Q<Button>("InventoryConfirmOk");
            _confirmCancel = root.Q<Button>("InventoryConfirmCancel");

            if (_screen == null || _grid == null || _equipBar == null || _detailIcon == null ||
                _confirm == null || _capacity == null || _detailName == null || _detailQuality == null ||
                _detailOwned == null || _detailStack == null || _detailPrice == null ||
                _detailDescription == null || _confirmMessage == null || _status == null ||
                _close == null || _sort == null || _expand == null || _discard == null ||
                _sell == null || _confirmOk == null || _confirmCancel == null)
            {
                Debug.LogError("InventoryPanel.uxml 缺少必需的元素名称，仓库界面未能装配。", this);
                _screen = null;
                return false;
            }

            _screen.style.display = DisplayStyle.None;
            _confirm.style.display = DisplayStyle.None;

            Bind(_close, () => _controller.Close());
            Bind(_sort, () => _controller.AutoSort());
            Bind(_expand, () => _controller.Expand());
            // 详情面板一次处理一份：批量丢弃需要数量选择器，那属于商店购买弹窗的形态，
            // 仓库这里保持"选中一格 → 处理一份"的最小可预测行为。
            Bind(_discard, () => _controller.RequestDiscard(1));
            Bind(_sell, () => _controller.RequestSell(1));
            Bind(_confirmOk, () => _controller.ConfirmPendingOperation());
            Bind(_confirmCancel, () => _controller.CancelConfirm());

            BindCategory(root, "InventoryCategoryAll", InventoryCategory.All);
            BindCategory(root, "InventoryCategorySoulstone", InventoryCategory.Soulstone);
            BindCategory(root, "InventoryCategoryMaterial", InventoryCategory.Material);
            BindCategory(root, "InventoryCategorySpecial", InventoryCategory.Special);
            BindCategory(root, "InventoryCategoryConsumable", InventoryCategory.Consumable);
            BindCategory(root, "InventoryCategoryArmor", InventoryCategory.Armor);
            return true;
        }

        private void BindCategory(VisualElement root, string elementName, InventoryCategory category)
        {
            var button = root.Q<Button>(elementName);
            if (button == null)
            {
                Debug.LogError($"InventoryPanel.uxml 缺少分类按钮 {elementName}。", this);
                return;
            }

            Bind(button, () => _controller.SelectCategory(category));
            _categories.Add(new KeyValuePair<Button, InventoryCategory>(button, category));
        }

        // 保存委托实例，否则 OnDestroy 里用新建的 lambda 无法解除订阅。
        private void Bind(Button button, Action handler)
        {
            button.clicked += handler;
            _handlers.Add(new KeyValuePair<Button, Action>(button, handler));
        }

        private void Unbind(Button button)
        {
            for (var i = _handlers.Count - 1; i >= 0; i--)
            {
                if (_handlers[i].Key == button)
                {
                    button.clicked -= _handlers[i].Value;
                    _handlers.RemoveAt(i);
                }
            }
        }

        private void OnDestroy()
        {
            foreach (var entry in _handlers)
            {
                entry.Key.clicked -= entry.Value;
            }

            _handlers.Clear();
            _cells.Clear();
            _categories.Clear();
            _equipSlots.Clear();
            _screen?.RemoveFromHierarchy();
            _subscription?.Dispose();
        }
    }
}
