using System;
using System.Collections.Generic;
using System.Globalization;
using Naraka.Config;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.MVC;
using Naraka.Features.SignIn.Controller;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Naraka.Features.SignIn.View
{
    /// <summary>
    /// 签到界面。
    ///
    /// 七个格子的状态完全由服务端返回的领取记录决定，界面不做任何"今天大概能领"的推断。
    /// 主进度与连续次数分别显示：补签会点亮格子，但连续次数不动，这一点在界面上是可见的。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SignInPanelView :
        MonoBehaviour,
        IView<SignInPresentationState>,
        IObserver<SignInPresentationState>
    {
        [SerializeField] private VisualTreeAsset signInLayout;

        private readonly List<KeyValuePair<Button, Action>> _handlers =
            new List<KeyValuePair<Button, Action>>();

        private readonly List<KeyValuePair<Button, Action>> _milestoneHandlers =
            new List<KeyValuePair<Button, Action>>();

        private SignInController _controller;
        private IGameConfigProvider _config;
        private IDisposable _subscription;
        private VisualElement _screen;
        private VisualElement _dayGrid;
        private VisualElement _milestoneList;
        private VisualElement _milestones;
        private Label _streak;
        private Label _makeupCard;
        private Label _empty;
        private Label _status;
        private Button _claim;
        private Button _makeup;
        private Button _close;
        private long _builtDaysSignature = -1;
        private long _builtMilestoneSignature = -1;

        [Inject]
        public void Construct(SignInController controller, IGameConfigProvider config)
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

        public void Render(SignInPresentationState state)
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

            if (!state.HasServerState)
            {
                _empty.style.display = DisplayStyle.Flex;
                _empty.text = state.IsLoading ? "正在读取签到数据…" : "签到数据暂不可用。";
                _dayGrid.style.display = DisplayStyle.None;
                _milestones.style.display = DisplayStyle.None;
                _claim.SetEnabled(false);
                _makeup.SetEnabled(false);
                return;
            }

            _empty.style.display = DisplayStyle.None;
            _dayGrid.style.display = DisplayStyle.Flex;
            _milestones.style.display = DisplayStyle.Flex;

            _streak.text = "连续签到 " +
                           state.ConsecutiveDays.ToString(CultureInfo.InvariantCulture) + " 天";
            _makeupCard.text = state.MakeupUsedThisCycle
                ? "补签卡 " + state.MakeupCardCount.ToString(CultureInfo.InvariantCulture) +
                  " 张（本周期补签次数已用完）"
                : "补签卡 " + state.MakeupCardCount.ToString(CultureInfo.InvariantCulture) + " 张";

            RebuildDays(state);
            RebuildMilestones(state);

            _claim.SetEnabled(state.CanClaimToday && !state.IsBusy);
            _claim.text = state.CanClaimToday ? "签到" : "今日已签到";
            _makeup.SetEnabled(state.CanMakeUpAny && !state.IsBusy);
        }

        public void OnNext(SignInPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }

        /// <summary>
        /// 只在格子内容真的变化时重建。
        ///
        /// 签名把七天的状态与补签卡数量压成一个整数：状态没变就不重建，避免每帧丢弃并重新分配
        /// 七个元素。
        /// </summary>
        private void RebuildDays(SignInPresentationState state)
        {
            long signature = state.MakeupCardCount * 31 + (state.MakeupUsedThisCycle ? 1 : 0);
            foreach (var day in state.Days)
            {
                signature = signature * 7 + (int)day.Status;
            }

            if (signature == _builtDaysSignature)
            {
                return;
            }

            _builtDaysSignature = signature;
            _dayGrid.Clear();
            foreach (var day in state.Days)
            {
                _dayGrid.Add(BuildDay(day));
            }
        }

        private VisualElement BuildDay(SignInDaySnapshot day)
        {
            var cell = new VisualElement
            {
                name = "SignInDay" + day.Day.ToString(CultureInfo.InvariantCulture)
            };
            cell.AddToClassList("signin-day");
            cell.pickingMode = PickingMode.Ignore;

            switch (day.Status)
            {
                case SignInDayStatus.Claimable:
                    cell.AddToClassList("signin-day--claimable");
                    break;
                case SignInDayStatus.Claimed:
                case SignInDayStatus.MadeUp:
                    cell.AddToClassList("signin-day--claimed");
                    break;
                case SignInDayStatus.Missed:
                    cell.AddToClassList("signin-day--missed");
                    break;
                case SignInDayStatus.MissedLocked:
                    cell.AddToClassList("signin-day--locked");
                    break;
            }

            var title = new Label("第 " + day.Day.ToString(CultureInfo.InvariantCulture) + " 天");
            title.AddToClassList("signin-day-title");
            title.pickingMode = PickingMode.Ignore;
            cell.Add(title);

            var reward = new Label(DescribeDayReward(day.Day));
            reward.AddToClassList("signin-day-reward");
            reward.pickingMode = PickingMode.Ignore;
            cell.Add(reward);

            var stateLabel = new Label(DescribeDayStatus(day.Status));
            stateLabel.AddToClassList("signin-day-state");
            stateLabel.pickingMode = PickingMode.Ignore;
            cell.Add(stateLabel);
            return cell;
        }

        private void RebuildMilestones(SignInPresentationState state)
        {
            long signature = 0;
            foreach (var milestone in state.Milestones)
            {
                signature = signature * 5 +
                            (milestone.IsClaimed ? 2 : 0) + (milestone.IsReached ? 1 : 0);
            }

            signature = signature * 3 + (state.IsBusy ? 1 : 0);
            if (signature == _builtMilestoneSignature)
            {
                return;
            }

            _builtMilestoneSignature = signature;
            ClearMilestoneHandlers();
            _milestoneList.Clear();
            foreach (var milestone in state.Milestones)
            {
                _milestoneList.Add(BuildMilestone(milestone, state.IsBusy));
            }
        }

        private VisualElement BuildMilestone(SignInMilestoneSnapshot milestone, bool isBusy)
        {
            var days = milestone.MilestoneDays.ToString(CultureInfo.InvariantCulture);
            var card = new VisualElement { name = "SignInMilestone" + days };
            card.AddToClassList("signin-milestone");
            if (milestone.IsClaimed)
            {
                card.AddToClassList("signin-milestone--claimed");
            }
            else if (milestone.IsClaimable)
            {
                card.AddToClassList("signin-milestone--claimable");
            }

            var label = new Label("连续 " + days + " 天");
            label.AddToClassList("signin-milestone-label");
            label.pickingMode = PickingMode.Ignore;
            card.Add(label);

            var reward = new Label(DescribeMilestoneReward(milestone.MilestoneDays));
            reward.AddToClassList("signin-milestone-reward");
            reward.pickingMode = PickingMode.Ignore;
            card.Add(reward);

            var button = new Button
            {
                name = "SignInMilestoneClaim" + days,
                text = milestone.IsClaimed ? "已领取" : "领取"
            };
            button.AddToClassList("signin-milestone-button");
            button.SetEnabled(milestone.IsClaimable && !isBusy);

            var milestoneDays = milestone.MilestoneDays;
            Action handler = () => _controller.ClaimMilestone(milestoneDays);
            button.clicked += handler;
            _milestoneHandlers.Add(new KeyValuePair<Button, Action>(button, handler));
            card.Add(button);
            return card;
        }

        /// <summary>奖励文案只来自配置，绝不在界面里写死数量。</summary>
        private string DescribeDayReward(int day)
        {
            if (!_config.IsLoaded)
            {
                return string.Empty;
            }

            foreach (var reward in _config.Catalog.SignInRewardsInDayOrder)
            {
                if (reward.Day == day)
                {
                    return DescribeReward(
                        reward.RewardKind, reward.ItemId, reward.ItemAmount,
                        reward.CurrencyId, reward.CurrencyAmount);
                }
            }

            return string.Empty;
        }

        private string DescribeMilestoneReward(int milestoneDays)
        {
            if (!_config.IsLoaded)
            {
                return string.Empty;
            }

            foreach (var milestone in _config.Catalog.SignInMilestonesInDayOrder)
            {
                if (milestone.MilestoneDays == milestoneDays)
                {
                    return DescribeReward(
                        milestone.RewardKind, milestone.ItemId, milestone.ItemAmount,
                        milestone.CurrencyId, milestone.CurrencyAmount);
                }
            }

            return string.Empty;
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

            return string.Empty;
        }

        private static string DescribeDayStatus(SignInDayStatus status)
        {
            switch (status)
            {
                case SignInDayStatus.Claimable: return "可领取";
                case SignInDayStatus.Claimed: return "已签到";
                case SignInDayStatus.MadeUp: return "已补签";
                case SignInDayStatus.Missed: return "可补签";
                case SignInDayStatus.MissedLocked: return "已错过";
                default: return "未开始";
            }
        }

        private bool TryBuild(VisualElement root)
        {
            if (signInLayout == null)
            {
                Debug.LogError(
                    "SignInPanelView 未绑定 SignInPanel.uxml；请运行菜单 NARAKA/Setup/Apply P0 Project Settings。",
                    this);
                return false;
            }

            signInLayout.CloneTree(root);
            _screen = root.Q<VisualElement>("SignInScreen");
            _dayGrid = root.Q<VisualElement>("SignInDayGrid");
            _milestones = root.Q<VisualElement>("SignInMilestones");
            _milestoneList = root.Q<VisualElement>("SignInMilestoneList");
            _streak = root.Q<Label>("SignInStreakLabel");
            _makeupCard = root.Q<Label>("SignInMakeupCardLabel");
            _empty = root.Q<Label>("SignInEmptyLabel");
            _status = root.Q<Label>("SignInStatusLabel");
            _claim = root.Q<Button>("SignInClaimButton");
            _makeup = root.Q<Button>("SignInMakeupButton");
            _close = root.Q<Button>("SignInCloseButton");

            if (_screen == null || _dayGrid == null || _milestones == null || _milestoneList == null ||
                _streak == null || _makeupCard == null || _empty == null || _status == null ||
                _claim == null || _makeup == null || _close == null)
            {
                Debug.LogError("SignInPanel.uxml 缺少必需的元素名称，签到界面未能装配。", this);
                _screen = null;
                return false;
            }

            _screen.style.display = DisplayStyle.None;

            Bind(_claim, () => _controller.ClaimToday());
            // 补签目标取当前第一个可补签的日子；服务端仍会独立校验这一天是否真的漏签。
            Bind(_makeup, () =>
            {
                var day = _controller.Current.FirstMakeUpDay;
                if (day > 0)
                {
                    _controller.MakeUp(day);
                }
            });
            Bind(_close, () => _controller.Close());
            return true;
        }

        private void Bind(Button button, Action handler)
        {
            button.clicked += handler;
            _handlers.Add(new KeyValuePair<Button, Action>(button, handler));
        }

        private void ClearMilestoneHandlers()
        {
            foreach (var entry in _milestoneHandlers)
            {
                entry.Key.clicked -= entry.Value;
            }

            _milestoneHandlers.Clear();
        }

        private void OnDestroy()
        {
            foreach (var entry in _handlers)
            {
                entry.Key.clicked -= entry.Value;
            }

            _handlers.Clear();
            ClearMilestoneHandlers();
            _screen?.RemoveFromHierarchy();
            _subscription?.Dispose();
        }
    }
}
