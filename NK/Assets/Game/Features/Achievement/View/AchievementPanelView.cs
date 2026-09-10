using System;
using System.Collections.Generic;
using System.Globalization;
using Naraka.Config;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.MVC;
using Naraka.Features.Achievement.Controller;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Naraka.Features.Achievement.View
{
    /// <summary>
    /// 成就与账号等级奖励界面。
    ///
    /// 两个分页对应两套独立系统：账号等级由账号经验（只来自任务）解锁，成就使用独立的
    /// 成就经验与成就等级。界面顶部同时显示两条经验线，让"领成就不会涨账号等级"这件事
    /// 一眼可见。等待 P2/P3 事件源的成就整体压暗且进度恒为 0，不显示任何编造的进度。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class AchievementPanelView :
        MonoBehaviour,
        IView<AchievementPresentationState>,
        IObserver<AchievementPresentationState>
    {
        [SerializeField] private VisualTreeAsset achievementLayout;

        private readonly List<KeyValuePair<Button, Action>> _handlers =
            new List<KeyValuePair<Button, Action>>();

        private readonly List<KeyValuePair<Button, Action>> _rowHandlers =
            new List<KeyValuePair<Button, Action>>();

        private AchievementController _controller;
        private IGameConfigProvider _config;
        private IDisposable _subscription;
        private VisualElement _screen;
        private VisualElement _categories;
        private VisualElement _list;
        private ScrollView _scroll;
        private Label _accountLine;
        private Label _xpLine;
        private Label _empty;
        private Label _status;
        private Button _tabAccountLevel;
        private Button _tabAchievement;
        private Button _close;
        private readonly Dictionary<string, Button> _categoryButtons =
            new Dictionary<string, Button>(StringComparer.Ordinal);

        private int _builtSignature;

        [Inject]
        public void Construct(AchievementController controller, IGameConfigProvider config)
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

        public void Render(AchievementPresentationState state)
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

            SetSelected(_tabAccountLevel, state.Tab == AchievementTab.AccountLevel, "achv-tab--selected");
            SetSelected(_tabAchievement, state.Tab == AchievementTab.Achievement, "achv-tab--selected");
            _categories.style.display =
                state.Tab == AchievementTab.Achievement ? DisplayStyle.Flex : DisplayStyle.None;

            foreach (var pair in _categoryButtons)
            {
                SetSelected(
                    pair.Value,
                    string.Equals(state.Category, pair.Key, StringComparison.Ordinal),
                    "achv-category--selected");
            }

            if (!state.HasServerState)
            {
                _empty.style.display = DisplayStyle.Flex;
                _empty.text = state.IsLoading ? "正在读取数据…" : "暂无数据。";
                _scroll.style.display = DisplayStyle.None;
                _accountLine.text = string.Empty;
                _xpLine.text = string.Empty;
                return;
            }

            _scroll.style.display = DisplayStyle.Flex;
            _accountLine.text =
                "账号等级 " + state.AccountLevel.ToString(CultureInfo.InvariantCulture) +
                "（账号经验 " + state.AccountXp.ToString(CultureInfo.InvariantCulture) + "，只来自任务）";
            _xpLine.text =
                "成就等级 " + state.AchievementLevel.ToString(CultureInfo.InvariantCulture) +
                "（成就经验 " + state.AchievementXp.ToString(CultureInfo.InvariantCulture) + "）";

            Rebuild(state);
        }

        public void OnNext(AchievementPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }

        /// <summary>
        /// 只在列表内容真的变化时重建。签名把分页、分类、忙碌标记与每一行的领取状态压成一个整数。
        /// </summary>
        private void Rebuild(AchievementPresentationState state)
        {
            var signature = (int)state.Tab * 397 ^ state.Category.GetHashCode();
            signature = signature * 31 + (state.IsBusy ? 1 : 0);
            if (state.Tab == AchievementTab.Achievement)
            {
                foreach (var achievement in state.AchievementsInCategory)
                {
                    signature = signature * 31 +
                                (achievement.IsClaimed ? 4 : 0) +
                                (achievement.IsCompleted ? 2 : 0) +
                                (achievement.IsActive ? 1 : 0);
                    signature = signature * 31 + achievement.Progress.GetHashCode();
                }
            }
            else
            {
                foreach (var reward in state.AccountLevelRewards)
                {
                    signature = signature * 31 +
                                (reward.IsClaimed ? 4 : 0) +
                                (reward.IsUnlocked ? 2 : 0) +
                                (reward.HasReward ? 1 : 0);
                }
            }

            if (signature == _builtSignature)
            {
                return;
            }

            _builtSignature = signature;
            ClearRowHandlers();
            _list.Clear();

            if (state.Tab == AchievementTab.Achievement)
            {
                var rows = state.AchievementsInCategory;
                _empty.style.display = rows.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                _empty.text = "该分类下暂无成就。";
                foreach (var achievement in rows)
                {
                    _list.Add(BuildAchievementRow(achievement, state.IsBusy));
                }

                return;
            }

            var rewards = state.AccountLevelRewards;
            _empty.style.display = rewards.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _empty.text = "暂无账号等级奖励。";
            foreach (var reward in rewards)
            {
                _list.Add(BuildAccountLevelRow(reward, state.IsBusy));
            }
        }

        private VisualElement BuildAchievementRow(AchievementSnapshot achievement, bool isBusy)
        {
            var row = new VisualElement { name = "AchievementRow_" + achievement.AchievementId };
            row.AddToClassList("achv-row");
            if (!achievement.IsActive)
            {
                row.AddToClassList("achv-row--inactive");
            }
            else if (achievement.IsClaimed)
            {
                row.AddToClassList("achv-row--claimed");
            }
            else if (achievement.IsClaimable)
            {
                row.AddToClassList("achv-row--claimable");
            }

            var text = new VisualElement();
            text.AddToClassList("achv-row-text");
            text.pickingMode = PickingMode.Ignore;

            var configuredRow = Lookup(achievement.AchievementId);

            var title = new Label(configuredRow != null
                ? configuredRow.DisplayName
                : achievement.AchievementId);
            title.AddToClassList("achv-row-title");
            title.pickingMode = PickingMode.Ignore;
            text.Add(title);

            var desc = new Label(configuredRow != null ? configuredRow.Description : string.Empty);
            desc.AddToClassList("achv-row-desc");
            desc.pickingMode = PickingMode.Ignore;
            text.Add(desc);

            var track = new VisualElement();
            track.AddToClassList("achv-row-progress-track");
            track.pickingMode = PickingMode.Ignore;
            var fill = new VisualElement();
            fill.AddToClassList("achv-row-progress-fill");
            fill.pickingMode = PickingMode.Ignore;
            fill.style.width = new StyleLength(new Length(achievement.Ratio * 100f, LengthUnit.Percent));
            track.Add(fill);
            text.Add(track);

            var progress = new Label(achievement.IsActive
                ? achievement.Progress.ToString(CultureInfo.InvariantCulture) + " / " +
                  achievement.TargetProgress.ToString(CultureInfo.InvariantCulture)
                : "等待后续版本开放");
            progress.AddToClassList("achv-row-progress-label");
            progress.pickingMode = PickingMode.Ignore;
            text.Add(progress);
            row.Add(text);

            var reward = new Label(configuredRow != null
                ? DescribeReward(
                      configuredRow.RewardKind, configuredRow.ItemId, configuredRow.ItemAmount,
                      configuredRow.CurrencyId, configuredRow.CurrencyAmount) +
                  "　成就经验 " + configuredRow.AchievementXp.ToString(CultureInfo.InvariantCulture)
                : string.Empty);
            reward.AddToClassList("achv-row-reward");
            reward.pickingMode = PickingMode.Ignore;
            row.Add(reward);

            var button = new Button
            {
                name = "AchievementClaim_" + achievement.AchievementId,
                text = achievement.IsClaimed ? "已领取" : "领取"
            };
            button.AddToClassList("achv-row-button");
            button.SetEnabled(achievement.IsClaimable && !isBusy);

            var id = achievement.AchievementId;
            Action handler = () => _controller.ClaimAchievement(id);
            button.clicked += handler;
            _rowHandlers.Add(new KeyValuePair<Button, Action>(button, handler));
            row.Add(button);
            return row;
        }

        private VisualElement BuildAccountLevelRow(AccountLevelRewardSnapshot reward, bool isBusy)
        {
            var level = reward.Level.ToString(CultureInfo.InvariantCulture);
            var row = new VisualElement { name = "AccountLevelRow_" + level };
            row.AddToClassList("achv-row");
            if (reward.IsClaimed)
            {
                row.AddToClassList("achv-row--claimed");
            }
            else if (reward.IsClaimable)
            {
                row.AddToClassList("achv-row--claimable");
            }

            var text = new VisualElement();
            text.AddToClassList("achv-row-text");
            text.pickingMode = PickingMode.Ignore;

            var title = new Label("账号等级 " + level);
            title.AddToClassList("achv-row-title");
            title.pickingMode = PickingMode.Ignore;
            text.Add(title);

            var configured = LookupLevel(reward.Level);
            var desc = new Label(configured != null
                ? "需要累计账号经验 " + configured.XpToReach.ToString(CultureInfo.InvariantCulture)
                : string.Empty);
            desc.AddToClassList("achv-row-desc");
            desc.pickingMode = PickingMode.Ignore;
            text.Add(desc);
            row.Add(text);

            var rewardLabel = new Label(configured != null
                ? DescribeReward(
                    configured.RewardKind, configured.ItemId, configured.ItemAmount,
                    configured.CurrencyId, configured.CurrencyAmount)
                : string.Empty);
            rewardLabel.AddToClassList("achv-row-reward");
            rewardLabel.pickingMode = PickingMode.Ignore;
            row.Add(rewardLabel);

            var button = new Button
            {
                name = "AccountLevelClaim_" + level,
                text = reward.IsClaimed ? "已领取" : reward.HasReward ? "领取" : "无奖励"
            };
            button.AddToClassList("achv-row-button");
            button.SetEnabled(reward.IsClaimable && !isBusy);

            var target = reward.Level;
            Action handler = () => _controller.ClaimAccountLevelReward(target);
            button.clicked += handler;
            _rowHandlers.Add(new KeyValuePair<Button, Action>(button, handler));
            row.Add(button);
            return row;
        }

        private AchievementConfig Lookup(string achievementId) =>
            _config.IsLoaded && _config.Catalog.TryGetAchievement(achievementId, out var achievement)
                ? achievement
                : null;

        private AccountLevelRewardConfig LookupLevel(int level)
        {
            if (!_config.IsLoaded)
            {
                return null;
            }

            foreach (var reward in _config.Catalog.AccountLevelRewardsInLevelOrder)
            {
                if (reward.Level == level)
                {
                    return reward;
                }
            }

            return null;
        }

        private string DescribeReward(
            string rewardKind,
            string itemId,
            int itemAmount,
            string currencyId,
            long currencyAmount)
        {
            if (string.Equals(rewardKind, ConfigRewardKind.Item, StringComparison.Ordinal))
            {
                return _config.Catalog.GetItemDisplayName(itemId) + " × " +
                       itemAmount.ToString(CultureInfo.InvariantCulture);
            }

            if (string.Equals(rewardKind, ConfigRewardKind.Currency, StringComparison.Ordinal))
            {
                return _config.Catalog.GetCurrencyDisplayName(currencyId) + " × " +
                       currencyAmount.ToString(CultureInfo.InvariantCulture);
            }

            return "无奖励";
        }

        private static void SetSelected(Button button, bool isSelected, string className)
        {
            if (isSelected)
            {
                button.AddToClassList(className);
            }
            else
            {
                button.RemoveFromClassList(className);
            }
        }

        private bool TryBuild(VisualElement root)
        {
            if (achievementLayout == null)
            {
                Debug.LogError(
                    "AchievementPanelView 未绑定 AchievementPanel.uxml；请运行菜单 NARAKA/Setup/Apply P0 Project Settings。",
                    this);
                return false;
            }

            achievementLayout.CloneTree(root);
            _screen = root.Q<VisualElement>("AchievementScreen");
            _categories = root.Q<VisualElement>("AchievementCategories");
            _list = root.Q<VisualElement>("AchievementList");
            _scroll = root.Q<ScrollView>("AchievementScroll");
            _accountLine = root.Q<Label>("AchievementAccountLabel");
            _xpLine = root.Q<Label>("AchievementXpLabel");
            _empty = root.Q<Label>("AchievementEmptyLabel");
            _status = root.Q<Label>("AchievementStatusLabel");
            _tabAccountLevel = root.Q<Button>("AchievementTabAccountLevel");
            _tabAchievement = root.Q<Button>("AchievementTabAchievement");
            _close = root.Q<Button>("AchievementCloseButton");

            _categoryButtons.Clear();
            AddCategory(root, "AchievementCategoryAdventure", ConfigAchievementCategory.Adventure);
            AddCategory(root, "AchievementCategoryBattle", ConfigAchievementCategory.Battle);
            AddCategory(root, "AchievementCategoryForge", ConfigAchievementCategory.Forge);
            AddCategory(root, "AchievementCategoryWealth", ConfigAchievementCategory.Wealth);

            if (_screen == null || _categories == null || _list == null || _scroll == null ||
                _accountLine == null || _xpLine == null || _empty == null || _status == null ||
                _tabAccountLevel == null || _tabAchievement == null || _close == null ||
                _categoryButtons.Count != ConfigAchievementCategory.All.Length)
            {
                Debug.LogError("AchievementPanel.uxml 缺少必需的元素名称，成就界面未能装配。", this);
                _screen = null;
                return false;
            }

            _screen.style.display = DisplayStyle.None;

            Bind(_tabAccountLevel, () => _controller.SelectTab(AchievementTab.AccountLevel));
            Bind(_tabAchievement, () => _controller.SelectTab(AchievementTab.Achievement));
            Bind(_close, () => _controller.Close());
            foreach (var pair in _categoryButtons)
            {
                var category = pair.Key;
                Bind(pair.Value, () => _controller.SelectCategory(category));
            }

            return true;
        }

        private void AddCategory(VisualElement root, string elementName, string category)
        {
            var button = root.Q<Button>(elementName);
            if (button != null)
            {
                _categoryButtons[category] = button;
            }
        }

        private void Bind(Button button, Action handler)
        {
            button.clicked += handler;
            _handlers.Add(new KeyValuePair<Button, Action>(button, handler));
        }

        private void ClearRowHandlers()
        {
            foreach (var entry in _rowHandlers)
            {
                entry.Key.clicked -= entry.Value;
            }

            _rowHandlers.Clear();
        }

        private void OnDestroy()
        {
            foreach (var entry in _handlers)
            {
                entry.Key.clicked -= entry.Value;
            }

            _handlers.Clear();
            ClearRowHandlers();
            _screen?.RemoveFromHierarchy();
            _subscription?.Dispose();
        }
    }
}
