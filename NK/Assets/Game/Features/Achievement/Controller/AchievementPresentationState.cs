using System;
using System.Collections.Generic;
using Naraka.Core.Application.MVC;

namespace Naraka.Features.Achievement.Controller
{
    /// <summary>界面的两个分页。成就与账号等级奖励是两套独立系统，只是共用一个入口。</summary>
    public enum AchievementTab
    {
        /// <summary>账号等级奖励：由账号等级解锁，账号经验只来自任务。</summary>
        AccountLevel = 0,

        /// <summary>成就：使用独立的成就经验与成就等级。</summary>
        Achievement = 1
    }

    /// <summary>单条成就的展示数据。</summary>
    public readonly struct AchievementSnapshot
    {
        public AchievementSnapshot(
            string achievementId,
            string category,
            long progress,
            long targetProgress,
            bool isClaimed,
            bool isActive)
        {
            AchievementId = achievementId ?? string.Empty;
            Category = category ?? string.Empty;
            Progress = progress < 0 ? 0 : progress;
            TargetProgress = targetProgress;
            IsClaimed = isClaimed;
            IsActive = isActive;
        }

        public string AchievementId { get; }

        public string Category { get; }

        /// <summary>当前进度。事件源尚未接入时恒为 0，绝不显示编造的进度。</summary>
        public long Progress { get; }

        public long TargetProgress { get; }

        public bool IsClaimed { get; }

        /// <summary>P1 已经存在事件源。为 false 表示等待 P2/P3 接入。</summary>
        public bool IsActive { get; }

        public bool IsCompleted => TargetProgress > 0 && Progress >= TargetProgress;

        public bool IsClaimable => IsCompleted && !IsClaimed;

        /// <summary>进度条比例，取值 0-1。</summary>
        public float Ratio
        {
            get
            {
                if (TargetProgress <= 0)
                {
                    return 0f;
                }

                var ratio = (float)Progress / TargetProgress;
                if (ratio < 0f)
                {
                    return 0f;
                }

                return ratio > 1f ? 1f : ratio;
            }
        }
    }

    /// <summary>单个账号等级奖励节点。</summary>
    public readonly struct AccountLevelRewardSnapshot
    {
        public AccountLevelRewardSnapshot(int level, bool hasReward, bool isUnlocked, bool isClaimed)
        {
            Level = level;
            HasReward = hasReward;
            IsUnlocked = isUnlocked;
            IsClaimed = isClaimed;
        }

        public int Level { get; }

        /// <summary>该等级配置了奖励。为 false 时服务端也会拒绝领取。</summary>
        public bool HasReward { get; }

        /// <summary>账号等级已经达到。</summary>
        public bool IsUnlocked { get; }

        public bool IsClaimed { get; }

        /// <summary>可以领取：有奖励、已解锁且未领取。奖励必须手动领取。</summary>
        public bool IsClaimable => HasReward && IsUnlocked && !IsClaimed;
    }

    /// <summary>
    /// 成就与账号等级奖励界面的只读展示状态。
    ///
    /// <see cref="AccountXp"/> 与 <see cref="AchievementXp"/> 分别来自两张表，界面上也分别显示：
    /// 领取成就奖励不会让账号等级前进，这是产品规则，不是显示上的巧合。
    /// </summary>
    public readonly struct AchievementPresentationState : IPresentationState
    {
        public AchievementPresentationState(
            bool isOpen,
            bool isLoading,
            bool isBusy,
            bool hasServerState,
            AchievementTab tab,
            string category,
            long accountXp,
            int accountLevel,
            long achievementXp,
            int achievementLevel,
            IReadOnlyList<AchievementSnapshot> achievements,
            IReadOnlyList<AccountLevelRewardSnapshot> accountLevelRewards,
            string statusMessage)
        {
            IsOpen = isOpen;
            IsLoading = isLoading;
            IsBusy = isBusy;
            HasServerState = hasServerState;
            Tab = tab;
            Category = category ?? string.Empty;
            AccountXp = accountXp;
            AccountLevel = accountLevel;
            AchievementXp = achievementXp;
            AchievementLevel = achievementLevel;
            Achievements = achievements ?? Array.Empty<AchievementSnapshot>();
            AccountLevelRewards = accountLevelRewards ?? Array.Empty<AccountLevelRewardSnapshot>();
            StatusMessage = statusMessage ?? string.Empty;
        }

        public static AchievementPresentationState Initial => new AchievementPresentationState(
            false, false, false, false, AchievementTab.AccountLevel, string.Empty,
            0, 0, 0, 0, null, null, string.Empty);

        public bool IsOpen { get; }

        public bool IsLoading { get; }

        public bool IsBusy { get; }

        public bool HasServerState { get; }

        public AchievementTab Tab { get; }

        /// <summary>成就分页当前选中的分类。</summary>
        public string Category { get; }

        /// <summary>账号经验。只来自任务。</summary>
        public long AccountXp { get; }

        public int AccountLevel { get; }

        /// <summary>成就经验。与账号经验完全独立。</summary>
        public long AchievementXp { get; }

        public int AchievementLevel { get; }

        public IReadOnlyList<AchievementSnapshot> Achievements { get; }

        public IReadOnlyList<AccountLevelRewardSnapshot> AccountLevelRewards { get; }

        public string StatusMessage { get; }

        /// <summary>是否存在任何可领取项。两套系统共用一个入口红点。</summary>
        public bool HasClaimable => HasClaimableAchievement || HasClaimableAccountLevel;

        public bool HasClaimableAchievement
        {
            get
            {
                foreach (var achievement in Achievements)
                {
                    if (achievement.IsClaimable)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool HasClaimableAccountLevel
        {
            get
            {
                foreach (var reward in AccountLevelRewards)
                {
                    if (reward.IsClaimable)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>当前分类下的成就。</summary>
        public IReadOnlyList<AchievementSnapshot> AchievementsInCategory
        {
            get
            {
                if (Category.Length == 0)
                {
                    return Achievements;
                }

                var rows = new List<AchievementSnapshot>();
                foreach (var achievement in Achievements)
                {
                    if (string.Equals(achievement.Category, Category, StringComparison.Ordinal))
                    {
                        rows.Add(achievement);
                    }
                }

                return rows;
            }
        }

        public AchievementPresentationState WithOpen(bool isOpen) => Copy(isOpen: isOpen);

        public AchievementPresentationState WithLoading(bool isLoading) => Copy(isLoading: isLoading);

        public AchievementPresentationState WithBusy(bool isBusy) => Copy(isBusy: isBusy);

        public AchievementPresentationState WithTab(AchievementTab tab) => Copy(tab: tab);

        public AchievementPresentationState WithCategory(string category) => Copy(category: category);

        public AchievementPresentationState WithStatusMessage(string statusMessage) =>
            Copy(statusMessage: statusMessage);

        public AchievementPresentationState WithServerState(
            long accountXp,
            int accountLevel,
            long achievementXp,
            int achievementLevel,
            IReadOnlyList<AchievementSnapshot> achievements,
            IReadOnlyList<AccountLevelRewardSnapshot> accountLevelRewards) =>
            new AchievementPresentationState(
                IsOpen,
                false,
                false,
                true,
                Tab,
                Category,
                accountXp,
                accountLevel,
                achievementXp,
                achievementLevel,
                achievements,
                accountLevelRewards,
                StatusMessage);

        private AchievementPresentationState Copy(
            bool? isOpen = null,
            bool? isLoading = null,
            bool? isBusy = null,
            AchievementTab? tab = null,
            string category = null,
            string statusMessage = null) =>
            new AchievementPresentationState(
                isOpen ?? IsOpen,
                isLoading ?? IsLoading,
                isBusy ?? IsBusy,
                HasServerState,
                tab ?? Tab,
                category ?? Category,
                AccountXp,
                AccountLevel,
                AchievementXp,
                AchievementLevel,
                Achievements,
                AccountLevelRewards,
                statusMessage ?? StatusMessage);
    }
}
