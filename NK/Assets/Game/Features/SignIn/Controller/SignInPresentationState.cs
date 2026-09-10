using System;
using System.Collections.Generic;
using Naraka.Core.Application.MVC;

namespace Naraka.Features.SignIn.Controller
{
    /// <summary>七日格子中单独一天的状态。</summary>
    public enum SignInDayStatus
    {
        /// <summary>未来的日子，还没轮到。</summary>
        Upcoming = 0,

        /// <summary>今天，可以直接领取。</summary>
        Claimable = 1,

        /// <summary>已领取。</summary>
        Claimed = 2,

        /// <summary>已通过补签卡领取。</summary>
        MadeUp = 3,

        /// <summary>漏签且本周期还能补签。</summary>
        Missed = 4,

        /// <summary>漏签但本周期的补签次数已用完，无法再补。</summary>
        MissedLocked = 5
    }

    /// <summary>七日格子中的一天。所有判定都来自服务端返回的领取记录与服务器日。</summary>
    public readonly struct SignInDaySnapshot
    {
        public SignInDaySnapshot(int day, SignInDayStatus status)
        {
            Day = day;
            Status = status;
        }

        public int Day { get; }

        public SignInDayStatus Status { get; }

        public bool IsClaimed => Status == SignInDayStatus.Claimed || Status == SignInDayStatus.MadeUp;

        public bool CanMakeUp => Status == SignInDayStatus.Missed;
    }

    /// <summary>连续签到节点。</summary>
    public readonly struct SignInMilestoneSnapshot
    {
        public SignInMilestoneSnapshot(int milestoneDays, string rewardId, bool isReached, bool isClaimed)
        {
            MilestoneDays = milestoneDays;
            RewardId = rewardId ?? string.Empty;
            IsReached = isReached;
            IsClaimed = isClaimed;
        }

        public int MilestoneDays { get; }

        public string RewardId { get; }

        /// <summary>连续天数已经达到节点要求。</summary>
        public bool IsReached { get; }

        public bool IsClaimed { get; }

        /// <summary>可以领取：达标且未领取。奖励必须手动领取。</summary>
        public bool IsClaimable => IsReached && !IsClaimed;
    }

    /// <summary>
    /// 签到界面的只读展示状态。
    ///
    /// 主进度（<see cref="Days"/>）与连续次数（<see cref="ConsecutiveDays"/>）是两份独立数据，
    /// 界面也分开呈现：补签能补上格子，但不会让连续次数前进。
    /// </summary>
    public readonly struct SignInPresentationState : IPresentationState
    {
        public const int CycleLength = 7;

        public SignInPresentationState(
            bool isOpen,
            bool isLoading,
            bool isBusy,
            bool hasServerState,
            long serverDay,
            long cycleStartDay,
            int consecutiveDays,
            long makeupCardCount,
            bool makeupUsedThisCycle,
            IReadOnlyList<SignInDaySnapshot> days,
            IReadOnlyList<SignInMilestoneSnapshot> milestones,
            string statusMessage)
        {
            IsOpen = isOpen;
            IsLoading = isLoading;
            IsBusy = isBusy;
            HasServerState = hasServerState;
            ServerDay = serverDay;
            CycleStartDay = cycleStartDay;
            ConsecutiveDays = consecutiveDays;
            MakeupCardCount = makeupCardCount;
            MakeupUsedThisCycle = makeupUsedThisCycle;
            Days = days ?? Array.Empty<SignInDaySnapshot>();
            Milestones = milestones ?? Array.Empty<SignInMilestoneSnapshot>();
            StatusMessage = statusMessage ?? string.Empty;
        }

        public static SignInPresentationState Initial => new SignInPresentationState(
            false, false, false, false, 0, 0, 0, 0, false, null, null, string.Empty);

        public bool IsOpen { get; }

        public bool IsLoading { get; }

        /// <summary>领取请求在途。期间所有领取按钮禁用，避免重复点击。</summary>
        public bool IsBusy { get; }

        public bool HasServerState { get; }

        /// <summary>服务器日。以 05:00 为日界，由服务端计算。</summary>
        public long ServerDay { get; }

        public long CycleStartDay { get; }

        /// <summary>连续签到天数。与七日主进度是两份独立字段。</summary>
        public int ConsecutiveDays { get; }

        /// <summary>背包里的补签卡数量。补签卡是普通可堆叠物品。</summary>
        public long MakeupCardCount { get; }

        /// <summary>本周期的补签次数已用完。</summary>
        public bool MakeupUsedThisCycle { get; }

        public IReadOnlyList<SignInDaySnapshot> Days { get; }

        public IReadOnlyList<SignInMilestoneSnapshot> Milestones { get; }

        public string StatusMessage { get; }

        /// <summary>周期内的今天是第几天，取值 1-7。没有服务端数据时为 0。</summary>
        public int TodayIndex
        {
            get
            {
                if (!HasServerState)
                {
                    return 0;
                }

                var offset = ServerDay - CycleStartDay;
                if (offset < 0 || offset >= CycleLength)
                {
                    return 0;
                }

                return (int)offset + 1;
            }
        }

        /// <summary>今天是否还能签到。</summary>
        public bool CanClaimToday
        {
            get
            {
                var today = TodayIndex;
                if (today <= 0)
                {
                    return false;
                }

                foreach (var day in Days)
                {
                    if (day.Day == today)
                    {
                        return day.Status == SignInDayStatus.Claimable;
                    }
                }

                return false;
            }
        }

        /// <summary>是否存在可以补签的日子，且补签卡与周期次数都还够。</summary>
        public bool CanMakeUpAny
        {
            get
            {
                if (MakeupUsedThisCycle || MakeupCardCount <= 0)
                {
                    return false;
                }

                foreach (var day in Days)
                {
                    if (day.CanMakeUp)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>第一个可补签的日子，没有时为 0。</summary>
        public int FirstMakeUpDay
        {
            get
            {
                foreach (var day in Days)
                {
                    if (day.CanMakeUp)
                    {
                        return day.Day;
                    }
                }

                return 0;
            }
        }

        /// <summary>界面上是否存在任何"可领取"。红点只在这里为 true 时才亮。</summary>
        public bool HasClaimable
        {
            get
            {
                if (CanClaimToday)
                {
                    return true;
                }

                foreach (var milestone in Milestones)
                {
                    if (milestone.IsClaimable)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public SignInPresentationState WithOpen(bool isOpen) => Copy(isOpen: isOpen);

        public SignInPresentationState WithLoading(bool isLoading) => Copy(isLoading: isLoading);

        public SignInPresentationState WithBusy(bool isBusy) => Copy(isBusy: isBusy);

        public SignInPresentationState WithStatusMessage(string statusMessage) =>
            Copy(statusMessage: statusMessage);

        public SignInPresentationState WithServerState(
            long serverDay,
            long cycleStartDay,
            int consecutiveDays,
            long makeupCardCount,
            bool makeupUsedThisCycle,
            IReadOnlyList<SignInDaySnapshot> days,
            IReadOnlyList<SignInMilestoneSnapshot> milestones) =>
            new SignInPresentationState(
                IsOpen,
                false,
                false,
                true,
                serverDay,
                cycleStartDay,
                consecutiveDays,
                makeupCardCount,
                makeupUsedThisCycle,
                days,
                milestones,
                StatusMessage);

        private SignInPresentationState Copy(
            bool? isOpen = null,
            bool? isLoading = null,
            bool? isBusy = null,
            string statusMessage = null) =>
            new SignInPresentationState(
                isOpen ?? IsOpen,
                isLoading ?? IsLoading,
                isBusy ?? IsBusy,
                HasServerState,
                ServerDay,
                CycleStartDay,
                ConsecutiveDays,
                MakeupCardCount,
                MakeupUsedThisCycle,
                Days,
                Milestones,
                statusMessage ?? StatusMessage);
    }
}
