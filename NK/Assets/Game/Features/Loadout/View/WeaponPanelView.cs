using System;
using System.Collections.Generic;
using System.Globalization;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.MVC;
using Naraka.Features.Loadout.Controller;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Naraka.Features.Loadout.View
{
    /// <summary>
    /// 兵器界面。
    ///
    /// 左侧：上一把 / 预览 / 下一把 与选择按钮；右侧：背景描述、等级、攻击力、攻击速度、
    /// 熟练度与击杀计数。每个武器类型每账号唯一一把，因此这里没有"数量"的概念，
    /// 武器也不会出现在仓库里。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class WeaponPanelView :
        MonoBehaviour,
        IView<LoadoutPresentationState>,
        IObserver<LoadoutPresentationState>
    {
        [SerializeField] private VisualTreeAsset weaponLayout;
        [SerializeField] private LoadoutIconCatalog icons;

        private readonly List<KeyValuePair<Button, Action>> _handlers =
            new List<KeyValuePair<Button, Action>>();

        private ILoadoutController _controller;
        private IGameConfigProvider _config;
        private IDisposable _subscription;
        private VisualElement _screen;
        private VisualElement _icon;
        private Label _name;
        private Label _description;
        private Label _level;
        private Label _attack;
        private Label _attackSpeed;
        private Label _proficiency;
        private Label _killCount;
        private Label _uniqueNote;
        private Label _status;
        private Button _previous;
        private Button _next;
        private Button _equip;
        private Button _close;

        [Inject]
        public void Construct(ILoadoutController controller, IGameConfigProvider config)
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

        public void Render(LoadoutPresentationState state)
        {
            if (_screen == null)
            {
                return;
            }

            var visible = state.Panel == LoadoutPanel.Weapon;
            _screen.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
            {
                return;
            }

            _status.text = state.StatusMessage;
            _status.style.display = string.IsNullOrEmpty(state.StatusMessage)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            if (!state.IsReady || _config == null || !_config.IsLoaded ||
                !_config.Catalog.TryGetWeapon(state.PreviewWeaponId, out var weapon))
            {
                _name.text = "加载中…";
                _previous.SetEnabled(false);
                _next.SetEnabled(false);
                _equip.SetEnabled(false);
                return;
            }

            _previous.SetEnabled(!state.IsSaving);
            _next.SetEnabled(!state.IsSaving);

            _name.text = weapon.DisplayName;
            _description.text = weapon.Description;

            var level = state.EquippedWeaponLevel;
            _level.text = "等级 " + level.ToString(CultureInfo.InvariantCulture) +
                          " / " + weapon.MaxLevel.ToString(CultureInfo.InvariantCulture);

            if (_config.Catalog.TryGetWeaponLevel(weapon.WeaponId, level, out var levelConfig))
            {
                _attack.text = "攻击力 " + levelConfig.Attack.ToString(CultureInfo.InvariantCulture);
                _attackSpeed.text = "攻击速度 " +
                                    levelConfig.AttackSpeed.ToString("0.00", CultureInfo.InvariantCulture);
            }
            else
            {
                _attack.text = "攻击力 --";
                _attackSpeed.text = "攻击速度 --";
            }

            // 熟练度与击杀计数由 P2 战斗产生。P1 没有事件源，显示 0 并注明来源，
            // 而不是编一个看起来像真的数字。
            _proficiency.text = "熟练度 0（战斗系统接入后累计）";
            _killCount.text = "击杀计数 0（战斗系统接入后累计）";
            _uniqueNote.text = "每种兵器每个账号只有唯一一把，不进入仓库，也不在商店出售。";

            var texture = icons == null ? null : icons.GetByKey(weapon.IconKey);
            if (texture != null)
            {
                _icon.style.backgroundImage = Background.FromTexture2D(texture);
            }

            _equip.text = state.IsPreviewWeaponEquipped ? "已装备" : "选择";
            _equip.SetEnabled(!state.IsPreviewWeaponEquipped && !state.IsSaving);
        }

        public void OnNext(LoadoutPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
            if (_status != null)
            {
                _status.text = "兵器界面发生错误。";
                _status.style.display = DisplayStyle.Flex;
            }
        }

        public void OnCompleted()
        {
        }

        private bool TryBuild(VisualElement root)
        {
            if (weaponLayout == null)
            {
                Debug.LogError(
                    "WeaponPanelView 未绑定 WeaponPanel.uxml；请运行菜单 NARAKA/Setup/Apply P0 Project Settings。",
                    this);
                return false;
            }

            weaponLayout.CloneTree(root);
            _screen = root.Q<VisualElement>("WeaponScreen");
            _icon = root.Q<VisualElement>("WeaponIcon");
            _name = root.Q<Label>("WeaponNameLabel");
            _description = root.Q<Label>("WeaponDescriptionLabel");
            _level = root.Q<Label>("WeaponLevelLabel");
            _attack = root.Q<Label>("WeaponAttackLabel");
            _attackSpeed = root.Q<Label>("WeaponAttackSpeedLabel");
            _proficiency = root.Q<Label>("WeaponProficiencyLabel");
            _killCount = root.Q<Label>("WeaponKillCountLabel");
            _uniqueNote = root.Q<Label>("WeaponUniqueNoteLabel");
            _status = root.Q<Label>("WeaponStatusLabel");
            _previous = root.Q<Button>("WeaponPreviousButton");
            _next = root.Q<Button>("WeaponNextButton");
            _equip = root.Q<Button>("WeaponEquipButton");
            _close = root.Q<Button>("WeaponCloseButton");

            if (_screen == null || _icon == null || _name == null || _description == null ||
                _level == null || _attack == null || _attackSpeed == null || _proficiency == null ||
                _killCount == null || _uniqueNote == null || _status == null ||
                _previous == null || _next == null || _equip == null || _close == null)
            {
                Debug.LogError("WeaponPanel.uxml 缺少必需的元素名称，兵器界面未能装配。", this);
                _screen = null;
                return false;
            }

            _screen.style.display = DisplayStyle.None;
            Bind(_previous, () => _controller.PreviewPreviousWeapon());
            Bind(_next, () => _controller.PreviewNextWeapon());
            Bind(_equip, () => _controller.EquipPreviewWeapon());
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
