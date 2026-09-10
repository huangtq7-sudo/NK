using System;
using System.Collections.Generic;
using System.Globalization;
using Naraka.Config;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.MVC;
using Naraka.Features.Loadout.Controller;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Naraka.Features.Loadout.View
{
    /// <summary>
    /// 英雄界面。
    ///
    /// 左侧：上一个 / 预览 / 下一个 与选择按钮；右侧：背景介绍、属性与技能列表。
    /// 全部内容由配置驱动，View 只做展示与意图转发，不判断哪个英雄"已出战"——
    /// 那由服务端保存的出战选择决定。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HeroPanelView :
        MonoBehaviour,
        IView<LoadoutPresentationState>,
        IObserver<LoadoutPresentationState>
    {
        private const string SkillSelectedClass = "hero-skill-button--selected";

        [SerializeField] private VisualTreeAsset heroLayout;
        [SerializeField] private LoadoutIconCatalog icons;

        private readonly List<KeyValuePair<Button, Action>> _handlers =
            new List<KeyValuePair<Button, Action>>();

        private readonly List<KeyValuePair<Button, string>> _skillButtons =
            new List<KeyValuePair<Button, string>>();

        private ILoadoutController _controller;
        private IGameConfigProvider _config;
        private IDisposable _subscription;
        private VisualElement _screen;
        private VisualElement _portrait;
        private VisualElement _skillBar;
        private Label _name;
        private Label _title;
        private Label _background;
        private Label _health;
        private Label _attack;
        private Label _defense;
        private Label _speed;
        private Label _skillName;
        private Label _skillCooldown;
        private Label _skillDescription;
        private Label _skillEffect;
        private Label _status;
        private Button _previous;
        private Button _next;
        private Button _equip;
        private Button _close;
        private string _builtSkillsForHero = string.Empty;

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

            var visible = state.Panel == LoadoutPanel.Hero;
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
                !_config.Catalog.TryGetHero(state.PreviewHeroId, out var hero))
            {
                // 还没有权威数据时显示加载中并禁用全部按钮，绝不显示一个猜测出来的英雄。
                _name.text = "加载中…";
                _previous.SetEnabled(false);
                _next.SetEnabled(false);
                _equip.SetEnabled(false);
                return;
            }

            _previous.SetEnabled(!state.IsSaving);
            _next.SetEnabled(!state.IsSaving);

            _name.text = hero.DisplayName;
            _title.text = hero.Title;
            _background.text = hero.Background;
            _health.text = "生命 " + hero.Health.ToString(CultureInfo.InvariantCulture);
            _attack.text = "攻击 " + hero.Attack.ToString(CultureInfo.InvariantCulture);
            _defense.text = "防御 " + hero.Defense.ToString(CultureInfo.InvariantCulture);
            _speed.text = "移动速度 " + hero.MoveSpeed.ToString("0.0", CultureInfo.InvariantCulture);

            var portrait = icons == null ? null : icons.GetByKey(hero.PortraitKey);
            if (portrait != null)
            {
                _portrait.style.backgroundImage = Background.FromTexture2D(portrait);
            }

            // 已经出战的英雄按钮显示"已出战"并禁用，避免重复提交同一个选择。
            _equip.text = state.IsPreviewHeroEquipped ? "已出战" : "选择";
            _equip.SetEnabled(!state.IsPreviewHeroEquipped && !state.IsSaving);

            RebuildSkillsIfNeeded(hero.HeroId);
            ApplySkillDetail(state.SelectedSkillId);
        }

        public void OnNext(LoadoutPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
            if (_status != null)
            {
                _status.text = "英雄界面发生错误。";
                _status.style.display = DisplayStyle.Flex;
            }
        }

        public void OnCompleted()
        {
        }

        private bool TryBuild(VisualElement root)
        {
            if (heroLayout == null)
            {
                Debug.LogError("HeroPanelView 未绑定 HeroPanel.uxml；请运行菜单 NARAKA/Setup/Apply P0 Project Settings。", this);
                return false;
            }

            heroLayout.CloneTree(root);
            _screen = root.Q<VisualElement>("HeroScreen");
            _portrait = root.Q<VisualElement>("HeroPortrait");
            _skillBar = root.Q<VisualElement>("HeroSkillBar");
            _name = root.Q<Label>("HeroNameLabel");
            _title = root.Q<Label>("HeroTitleLabel");
            _background = root.Q<Label>("HeroBackgroundLabel");
            _health = root.Q<Label>("HeroHealthLabel");
            _attack = root.Q<Label>("HeroAttackLabel");
            _defense = root.Q<Label>("HeroDefenseLabel");
            _speed = root.Q<Label>("HeroSpeedLabel");
            _skillName = root.Q<Label>("HeroSkillNameLabel");
            _skillCooldown = root.Q<Label>("HeroSkillCooldownLabel");
            _skillDescription = root.Q<Label>("HeroSkillDescriptionLabel");
            _skillEffect = root.Q<Label>("HeroSkillEffectLabel");
            _status = root.Q<Label>("HeroStatusLabel");
            _previous = root.Q<Button>("HeroPreviousButton");
            _next = root.Q<Button>("HeroNextButton");
            _equip = root.Q<Button>("HeroEquipButton");
            _close = root.Q<Button>("HeroCloseButton");

            if (_screen == null || _portrait == null || _skillBar == null || _name == null ||
                _title == null || _background == null || _health == null || _attack == null ||
                _defense == null || _speed == null || _skillName == null || _skillCooldown == null ||
                _skillDescription == null || _skillEffect == null || _status == null ||
                _previous == null || _next == null || _equip == null || _close == null)
            {
                Debug.LogError("HeroPanel.uxml 缺少必需的元素名称，英雄界面未能装配。", this);
                _screen = null;
                return false;
            }

            _screen.style.display = DisplayStyle.None;
            Bind(_previous, () => _controller.PreviewPreviousHero());
            Bind(_next, () => _controller.PreviewNextHero());
            Bind(_equip, () => _controller.EquipPreviewHero());
            Bind(_close, () => _controller.Close());
            return true;
        }

        /// <summary>技能按钮只在切换英雄时重建，翻页与刷新不会每帧重建元素。</summary>
        private void RebuildSkillsIfNeeded(string heroId)
        {
            if (string.Equals(_builtSkillsForHero, heroId, StringComparison.Ordinal))
            {
                return;
            }

            foreach (var entry in _skillButtons)
            {
                entry.Key.RemoveFromHierarchy();
            }

            _skillButtons.Clear();
            _skillBar.Clear();

            foreach (var skill in _config.Catalog.GetHeroSkills(heroId))
            {
                var button = new Button { name = "HeroSkillButton_" + skill.SkillId, text = skill.SlotKey };
                button.AddToClassList("hero-skill-button");
                var icon = icons == null ? null : icons.GetByKey(skill.IconKey);
                if (icon != null)
                {
                    button.style.backgroundImage = Background.FromTexture2D(icon);
                    button.text = string.Empty;
                }

                var skillId = skill.SkillId;
                Bind(button, () => _controller.SelectSkill(skillId));
                _skillBar.Add(button);
                _skillButtons.Add(new KeyValuePair<Button, string>(button, skillId));
            }

            _builtSkillsForHero = heroId;
        }

        private void ApplySkillDetail(string selectedSkillId)
        {
            foreach (var entry in _skillButtons)
            {
                entry.Key.EnableInClassList(
                    SkillSelectedClass,
                    string.Equals(entry.Value, selectedSkillId, StringComparison.Ordinal));
            }

            var skill = FindSkill(selectedSkillId);
            if (skill == null)
            {
                _skillName.text = string.Empty;
                _skillCooldown.text = string.Empty;
                _skillDescription.text = string.Empty;
                _skillEffect.text = string.Empty;
                return;
            }

            _skillName.text = skill.SlotKey + "  " + skill.DisplayName;
            _skillCooldown.text =
                "冷却 " + skill.CooldownSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " 秒" +
                "    伤害倍率 " + skill.DamageMultiplier.ToString("0.##", CultureInfo.InvariantCulture) +
                "    范围 " + skill.RangeMeters.ToString("0.#", CultureInfo.InvariantCulture) + " 米";
            _skillDescription.text = skill.Description;
            _skillEffect.text = skill.EffectSummary;
        }

        private HeroSkillConfig FindSkill(string skillId)
        {
            if (string.IsNullOrEmpty(skillId) || _config == null || !_config.IsLoaded)
            {
                return null;
            }

            foreach (var skill in _config.Catalog.Catalog.HeroSkills)
            {
                if (string.Equals(skill.SkillId, skillId, StringComparison.Ordinal))
                {
                    return skill;
                }
            }

            return null;
        }

        // 保存委托实例，否则 OnDestroy 里用新建的 lambda 无法解除订阅。
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
            _skillButtons.Clear();
            _screen?.RemoveFromHierarchy();
            _subscription?.Dispose();
        }
    }
}
