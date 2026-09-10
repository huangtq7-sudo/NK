using System;
using System.Collections.Generic;
using System.Globalization;
using Naraka.Config;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.MVC;
using Naraka.Features.Forge.Controller;
using Naraka.Features.Loadout.View;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Naraka.Features.Forge.View
{
    /// <summary>
    /// 锻造界面。
    ///
    /// 左上是武器展示与切换，右上是当前等级与"攻击力 68 → 75"这类下一级预览，
    /// 下方是材料与货币需求、"必定成功"提示与锻造按钮。
    /// 预览数值全部来自配置，成功与否只取决于材料是否足够——这里没有任何成功率。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ForgePanelView :
        MonoBehaviour,
        IView<ForgePresentationState>,
        IObserver<ForgePresentationState>
    {
        [SerializeField] private VisualTreeAsset forgeLayout;
        [SerializeField] private LoadoutIconCatalog icons;

        private readonly List<KeyValuePair<Button, Action>> _handlers =
            new List<KeyValuePair<Button, Action>>();

        private IForgeController _controller;
        private IGameConfigProvider _config;
        private IDisposable _subscription;
        private VisualElement _screen;
        private VisualElement _weaponIcon;
        private VisualElement _costList;
        private Label _weaponName;
        private Label _level;
        private Label _attackPreview;
        private Label _attackSpeedPreview;
        private Label _maxLevel;
        private Label _successRate;
        private Label _status;
        private Button _previous;
        private Button _next;
        private Button _upgrade;
        private Button _close;

        [Inject]
        public void Construct(IForgeController controller, IGameConfigProvider config)
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

        public void Render(ForgePresentationState state)
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

            _previous.SetEnabled(!state.IsBusy);
            _next.SetEnabled(!state.IsBusy);

            if (!state.HasServerState || _config == null || !_config.IsLoaded ||
                !_config.Catalog.TryGetWeapon(state.PreviewWeaponId, out var weapon))
            {
                // 还没有权威数据时显示加载中，绝不显示一个猜测出来的等级。
                _weaponName.text = state.IsLoading ? "正在读取锻造数据…" : "锻造数据不可用";
                _level.text = string.Empty;
                _attackPreview.text = string.Empty;
                _attackSpeedPreview.text = string.Empty;
                _maxLevel.text = string.Empty;
                _successRate.text = string.Empty;
                _costList.Clear();
                _upgrade.SetEnabled(false);
                return;
            }

            var level = state.LevelOf(weapon.WeaponId);
            _weaponName.text = weapon.DisplayName;
            _level.text = "当前等级 " + level.ToString(CultureInfo.InvariantCulture) +
                          " / " + weapon.MaxLevel.ToString(CultureInfo.InvariantCulture);

            var texture = icons == null ? null : icons.GetByKey(weapon.IconKey);
            if (texture != null)
            {
                _weaponIcon.style.backgroundImage = Background.FromTexture2D(texture);
            }

            var current = _controller.CurrentLevelStats;
            var next = _controller.NextLevelStats;
            var recipe = _controller.CurrentRecipe;

            if (recipe == null || next == null)
            {
                _attackPreview.text = current == null
                    ? "攻击力 --"
                    : "攻击力 " + current.Attack.ToString(CultureInfo.InvariantCulture);
                _attackSpeedPreview.text = string.Empty;
                _maxLevel.text = "已达到最高等级";
                _successRate.text = string.Empty;
                _costList.Clear();
                // 已达最高等级：按钮禁用，客户端连请求都不会发。
                _upgrade.SetEnabled(false);
                return;
            }

            _attackPreview.text = "攻击力 " +
                                  current.Attack.ToString(CultureInfo.InvariantCulture) + " → " +
                                  next.Attack.ToString(CultureInfo.InvariantCulture);
            _attackSpeedPreview.text = "攻击速度 " +
                                       current.AttackSpeed.ToString("0.00", CultureInfo.InvariantCulture) + " → " +
                                       next.AttackSpeed.ToString("0.00", CultureInfo.InvariantCulture);
            _maxLevel.text = string.Empty;

            // 玩法基线：材料足够时必定成功。这里显示的是事实，不是安慰性文案。
            _successRate.text = "材料齐备时必定成功（成功率 100%）";

            RebuildCosts(state, recipe);
            _upgrade.SetEnabled(!state.IsBusy && _controller.CanAffordUpgrade);
        }

        public void OnNext(ForgePresentationState value) => Render(value);

        public void OnError(Exception error)
        {
            if (_status != null)
            {
                _status.text = "锻造界面发生错误。";
                _status.style.display = DisplayStyle.Flex;
            }
        }

        public void OnCompleted()
        {
        }

        private void RebuildCosts(ForgePresentationState state, ForgeRecipeConfig recipe)
        {
            _costList.Clear();

            AddCost(
                _config.Catalog.GetCurrencyDisplayName(recipe.CurrencyId),
                state.BalanceOf(recipe.CurrencyId),
                recipe.CurrencyAmount);

            AddMaterialCost(state, recipe.Material1ItemId, recipe.Material1Amount);
            AddMaterialCost(state, recipe.Material2ItemId, recipe.Material2Amount);
        }

        private void AddMaterialCost(ForgePresentationState state, string itemId, int amount)
        {
            if (string.IsNullOrEmpty(itemId) || amount <= 0)
            {
                return;
            }

            AddCost(_config.Catalog.GetItemDisplayName(itemId), state.MaterialOf(itemId), amount);
        }

        private void AddCost(string displayName, long owned, long required)
        {
            var label = new Label(
                displayName + " " +
                owned.ToString(CultureInfo.InvariantCulture) + " / " +
                required.ToString(CultureInfo.InvariantCulture));
            label.AddToClassList("forge-cost-entry");
            if (owned < required)
            {
                label.AddToClassList("forge-cost-entry--missing");
            }

            label.pickingMode = PickingMode.Ignore;
            _costList.Add(label);
        }

        private bool TryBuild(VisualElement root)
        {
            if (forgeLayout == null)
            {
                Debug.LogError(
                    "ForgePanelView 未绑定 ForgePanel.uxml；请运行菜单 NARAKA/Setup/Apply P0 Project Settings。",
                    this);
                return false;
            }

            forgeLayout.CloneTree(root);
            _screen = root.Q<VisualElement>("ForgeScreen");
            _weaponIcon = root.Q<VisualElement>("ForgeWeaponIcon");
            _costList = root.Q<VisualElement>("ForgeCostList");
            _weaponName = root.Q<Label>("ForgeWeaponNameLabel");
            _level = root.Q<Label>("ForgeLevelLabel");
            _attackPreview = root.Q<Label>("ForgeAttackPreviewLabel");
            _attackSpeedPreview = root.Q<Label>("ForgeAttackSpeedPreviewLabel");
            _maxLevel = root.Q<Label>("ForgeMaxLevelLabel");
            _successRate = root.Q<Label>("ForgeSuccessRateLabel");
            _status = root.Q<Label>("ForgeStatusLabel");
            _previous = root.Q<Button>("ForgePreviousButton");
            _next = root.Q<Button>("ForgeNextButton");
            _upgrade = root.Q<Button>("ForgeUpgradeButton");
            _close = root.Q<Button>("ForgeCloseButton");

            if (_screen == null || _weaponIcon == null || _costList == null || _weaponName == null ||
                _level == null || _attackPreview == null || _attackSpeedPreview == null ||
                _maxLevel == null || _successRate == null || _status == null ||
                _previous == null || _next == null || _upgrade == null || _close == null)
            {
                Debug.LogError("ForgePanel.uxml 缺少必需的元素名称，锻造界面未能装配。", this);
                _screen = null;
                return false;
            }

            _screen.style.display = DisplayStyle.None;
            Bind(_previous, () => _controller.PreviewPreviousWeapon());
            Bind(_next, () => _controller.PreviewNextWeapon());
            Bind(_upgrade, () => _controller.Upgrade());
            Bind(_close, () => _controller.Close());
            return true;
        }

        private void Bind(Button button, Action handler)
        {
            button.clicked += handler;
            _handlers.Add(new KeyValuePair<Button, Action>(button, handler));
        }

        private void OnDestroy()
        {
            foreach (var entry in _handlers)
            {
                entry.Key.clicked -= entry.Value;
            }

            _handlers.Clear();
            _screen?.RemoveFromHierarchy();
            _subscription?.Dispose();
        }
    }
}
