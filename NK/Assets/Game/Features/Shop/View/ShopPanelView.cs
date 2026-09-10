using System;
using System.Collections.Generic;
using System.Globalization;
using Naraka.Config;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.MVC;
using Naraka.Features.Loadout.View;
using Naraka.Features.Shop.Controller;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Naraka.Features.Shop.View
{
    /// <summary>
    /// 商店界面。
    ///
    /// 左侧是分类与商品列表，右侧是选中商品的详情与购买按钮；购买弹窗提供数量加减、
    /// 滑条与总价预览。总价只是<b>显示</b>：真正的扣费与限购判定都在服务端完成。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ShopPanelView :
        MonoBehaviour,
        IView<ShopPresentationState>,
        IObserver<ShopPresentationState>
    {
        private const string RowClass = "shop-row";
        private const string RowSelectedClass = "shop-row--selected";
        private const string CategoryActiveClass = "shop-category--active";

        [SerializeField] private VisualTreeAsset shopLayout;
        [SerializeField] private LoadoutIconCatalog icons;

        private readonly List<KeyValuePair<Button, Action>> _handlers =
            new List<KeyValuePair<Button, Action>>();

        private readonly List<KeyValuePair<Button, string>> _rows =
            new List<KeyValuePair<Button, string>>();

        private readonly List<KeyValuePair<Button, ShopCategory>> _categories =
            new List<KeyValuePair<Button, ShopCategory>>();

        private IShopController _controller;
        private IGameConfigProvider _config;
        private IDisposable _subscription;
        private VisualElement _screen;
        private VisualElement _list;
        private VisualElement _detailIcon;
        private VisualElement _dialog;
        private Label _detailName;
        private Label _detailQuality;
        private Label _detailStack;
        private Label _detailOwned;
        private Label _detailLimit;
        private Label _detailDescription;
        private Label _dialogTitle;
        private Label _quantityLabel;
        private Label _totalPrice;
        private Label _status;
        private SliderInt _quantitySlider;
        private Button _close;
        private Button _buy;
        private Button _minus;
        private Button _plus;
        private Button _confirm;
        private Button _cancel;
        private EventCallback<ChangeEvent<int>> _sliderCallback;

        [Inject]
        public void Construct(IShopController controller, IGameConfigProvider config)
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

        public void Render(ShopPresentationState state)
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

            RebuildList(state);
            ApplyDetail(state);
            ApplyDialog(state);
        }

        public void OnNext(ShopPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
            if (_status != null)
            {
                _status.text = "商店界面发生错误。";
                _status.style.display = DisplayStyle.Flex;
            }
        }

        public void OnCompleted()
        {
        }

        private void RebuildList(ShopPresentationState state)
        {
            var products = _controller.VisibleProducts;
            if (_rows.Count != products.Count || !SameProducts(products))
            {
                foreach (var entry in _rows)
                {
                    Unbind(entry.Key);
                }

                _rows.Clear();
                _list.Clear();

                foreach (var product in products)
                {
                    var productId = product.ProductId;
                    var row = new Button { name = "ShopRow_" + productId };
                    row.AddToClassList(RowClass);

                    var icon = new VisualElement();
                    icon.AddToClassList("shop-row-icon");
                    icon.pickingMode = PickingMode.Ignore;
                    if (_config.IsLoaded && _config.Catalog.TryGetItem(product.ItemId, out var item))
                    {
                        var texture = icons == null ? null : icons.GetByKey(item.IconKey);
                        if (texture != null)
                        {
                            icon.style.backgroundImage = Background.FromTexture2D(texture);
                        }
                    }

                    row.Add(icon);

                    var name = new Label(DisplayNameOf(product.ItemId));
                    name.AddToClassList("shop-row-name");
                    name.pickingMode = PickingMode.Ignore;
                    row.Add(name);

                    var limit = new Label(DescribeLimit(product, state));
                    limit.AddToClassList("shop-row-limit");
                    limit.pickingMode = PickingMode.Ignore;
                    row.Add(limit);

                    var price = new Label(DescribePrice(product));
                    price.AddToClassList("shop-row-price");
                    price.pickingMode = PickingMode.Ignore;
                    row.Add(price);

                    Bind(row, () => _controller.SelectProduct(productId));
                    _list.Add(row);
                    _rows.Add(new KeyValuePair<Button, string>(row, productId));
                }
            }

            foreach (var entry in _rows)
            {
                entry.Key.EnableInClassList(
                    RowSelectedClass,
                    string.Equals(entry.Value, state.SelectedProductId, StringComparison.Ordinal));
                entry.Key.SetEnabled(!state.IsBusy);
            }
        }

        private bool SameProducts(IReadOnlyList<ShopProductConfig> products)
        {
            for (var i = 0; i < products.Count; i++)
            {
                if (!string.Equals(_rows[i].Value, products[i].ProductId, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyDetail(ShopPresentationState state)
        {
            if (state.SelectedProductId.Length == 0 || !_config.IsLoaded ||
                !_config.Catalog.TryGetShopProduct(state.SelectedProductId, out var product) ||
                !_config.Catalog.TryGetItem(product.ItemId, out var item))
            {
                _detailName.text = "未选择商品";
                _detailQuality.text = string.Empty;
                _detailStack.text = string.Empty;
                _detailOwned.text = string.Empty;
                _detailLimit.text = string.Empty;
                _detailDescription.text = string.Empty;
                _detailIcon.style.backgroundImage = StyleKeyword.None;
                _buy.SetEnabled(false);
                return;
            }

            _detailName.text = item.DisplayName;
            _detailQuality.text = "品质 " + DescribeQuality(item.Quality);
            _detailStack.text = "堆叠上限 " + item.StackLimit.ToString(CultureInfo.InvariantCulture);
            // 当前拥有数量需要仓库数据；商店只显示购买记录，避免在这里编一个数字。
            _detailOwned.text = "单价 " + DescribePrice(product);
            _detailLimit.text = DescribeLimit(product, state);
            _detailDescription.text = item.Description;

            var texture = icons == null ? null : icons.GetByKey(item.IconKey);
            if (texture != null)
            {
                _detailIcon.style.backgroundImage = Background.FromTexture2D(texture);
            }

            _buy.SetEnabled(!state.IsBusy && state.HasServerState);
        }

        private void ApplyDialog(ShopPresentationState state)
        {
            _dialog.style.display = state.IsDialogOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!state.IsDialogOpen)
            {
                return;
            }

            _config.Catalog.TryGetShopProduct(state.SelectedProductId, out var product);
            _dialogTitle.text = "购买 " + DisplayNameOf(product.ItemId);
            _quantityLabel.text = state.PurchaseQuantity.ToString(CultureInfo.InvariantCulture);

            var maximum = (int)Math.Max(1, Math.Min(
                ShopPresentationState.MaximumQuantityPerPurchase,
                product.PurchaseLimit > 0
                    ? product.PurchaseLimit - state.PurchasedTotalOf(product.ProductId)
                    : ShopPresentationState.MaximumQuantityPerPurchase));

            _quantitySlider.lowValue = 1;
            _quantitySlider.highValue = maximum;
            _quantitySlider.SetValueWithoutNotify(state.PurchaseQuantity);

            _totalPrice.text = "总价 " +
                               (product.UnitPrice * state.PurchaseQuantity).ToString(CultureInfo.InvariantCulture) +
                               " " + _config.Catalog.GetCurrencyDisplayName(product.CurrencyId);

            _confirm.SetEnabled(!state.IsBusy);
            _minus.SetEnabled(!state.IsBusy && state.PurchaseQuantity > 1);
            _plus.SetEnabled(!state.IsBusy && state.PurchaseQuantity < maximum);
        }

        private string DescribePrice(ShopProductConfig product) =>
            product.UnitPrice.ToString(CultureInfo.InvariantCulture) + " " +
            (_config.IsLoaded ? _config.Catalog.GetCurrencyDisplayName(product.CurrencyId) : product.CurrencyId);

        private static string DescribeLimit(ShopProductConfig product, ShopPresentationState state)
        {
            if (product.PurchaseLimit <= 0)
            {
                return "不限购";
            }

            var remaining = product.PurchaseLimit - state.PurchasedTotalOf(product.ProductId);
            return remaining <= 0
                ? "已售罄"
                : "限购剩余 " + remaining.ToString(CultureInfo.InvariantCulture) +
                  " / " + product.PurchaseLimit.ToString(CultureInfo.InvariantCulture);
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

        private string DisplayNameOf(string itemId) =>
            _config != null && _config.IsLoaded ? _config.Catalog.GetItemDisplayName(itemId) : itemId;

        private bool TryBuild(VisualElement root)
        {
            if (shopLayout == null)
            {
                Debug.LogError(
                    "ShopPanelView 未绑定 ShopPanel.uxml；请运行菜单 NARAKA/Setup/Apply P0 Project Settings。",
                    this);
                return false;
            }

            shopLayout.CloneTree(root);
            _screen = root.Q<VisualElement>("ShopScreen");
            _list = root.Q<VisualElement>("ShopList");
            _detailIcon = root.Q<VisualElement>("ShopDetailIcon");
            _dialog = root.Q<VisualElement>("ShopPurchaseDialog");
            _detailName = root.Q<Label>("ShopDetailName");
            _detailQuality = root.Q<Label>("ShopDetailQuality");
            _detailStack = root.Q<Label>("ShopDetailStack");
            _detailOwned = root.Q<Label>("ShopDetailOwned");
            _detailLimit = root.Q<Label>("ShopDetailLimit");
            _detailDescription = root.Q<Label>("ShopDetailDescription");
            _dialogTitle = root.Q<Label>("ShopDialogTitle");
            _quantityLabel = root.Q<Label>("ShopQuantityLabel");
            _totalPrice = root.Q<Label>("ShopTotalPriceLabel");
            _status = root.Q<Label>("ShopStatusLabel");
            _quantitySlider = root.Q<SliderInt>("ShopQuantitySlider");
            _close = root.Q<Button>("ShopCloseButton");
            _buy = root.Q<Button>("ShopBuyButton");
            _minus = root.Q<Button>("ShopQuantityMinus");
            _plus = root.Q<Button>("ShopQuantityPlus");
            _confirm = root.Q<Button>("ShopDialogConfirm");
            _cancel = root.Q<Button>("ShopDialogCancel");

            if (_screen == null || _list == null || _detailIcon == null || _dialog == null ||
                _detailName == null || _detailQuality == null || _detailStack == null ||
                _detailOwned == null || _detailLimit == null || _detailDescription == null ||
                _dialogTitle == null || _quantityLabel == null || _totalPrice == null ||
                _status == null || _quantitySlider == null || _close == null || _buy == null ||
                _minus == null || _plus == null || _confirm == null || _cancel == null)
            {
                Debug.LogError("ShopPanel.uxml 缺少必需的元素名称，商店界面未能装配。", this);
                _screen = null;
                return false;
            }

            _screen.style.display = DisplayStyle.None;
            _dialog.style.display = DisplayStyle.None;

            Bind(_close, () => _controller.Close());
            Bind(_buy, () => _controller.OpenPurchaseDialog());
            Bind(_minus, () => _controller.ChangePurchaseQuantity(-1));
            Bind(_plus, () => _controller.ChangePurchaseQuantity(1));
            Bind(_confirm, () => _controller.ConfirmPurchase());
            Bind(_cancel, () => _controller.CancelPurchase());

            // 保存回调实例，否则 OnDestroy 里无法解除滑条订阅。
            _sliderCallback = evt => _controller.SetPurchaseQuantity(evt.newValue);
            _quantitySlider.RegisterValueChangedCallback(_sliderCallback);

            BindCategory(root, "ShopCategoryAll", ShopCategory.All);
            BindCategory(root, "ShopCategorySoulstone", ShopCategory.Soulstone);
            BindCategory(root, "ShopCategoryMaterial", ShopCategory.Material);
            BindCategory(root, "ShopCategorySpecial", ShopCategory.Special);
            BindCategory(root, "ShopCategoryConsumable", ShopCategory.Consumable);
            BindCategory(root, "ShopCategoryArmor", ShopCategory.Armor);
            return true;
        }

        private void BindCategory(VisualElement root, string elementName, ShopCategory category)
        {
            var button = root.Q<Button>(elementName);
            if (button == null)
            {
                Debug.LogError($"ShopPanel.uxml 缺少分类按钮 {elementName}。", this);
                return;
            }

            Bind(button, () => _controller.SelectCategory(category));
            _categories.Add(new KeyValuePair<Button, ShopCategory>(button, category));
        }

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
            _rows.Clear();
            _categories.Clear();
            if (_quantitySlider != null && _sliderCallback != null)
            {
                _quantitySlider.UnregisterValueChangedCallback(_sliderCallback);
            }

            _screen?.RemoveFromHierarchy();
            _subscription?.Dispose();
        }
    }
}
