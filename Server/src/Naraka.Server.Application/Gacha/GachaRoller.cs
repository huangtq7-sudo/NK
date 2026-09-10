using Naraka.Config;

namespace Naraka.Server.Application.Gacha;

/// <summary>抽出的一个奖励。</summary>
public sealed record GachaRolledReward(string RewardId, string ItemId, int Amount, string Quality);

/// <summary>一次抽奖的结果与随之更新的保底计数。</summary>
public sealed record GachaRollOutcome(IReadOnlyList<GachaRolledReward> Rewards, int PityCounterAfter);

/// <summary>
/// 随机源。抽象出来只是为了让测试能用确定序列驱动抽奖，
/// 生产实现始终是密码学安全随机数——绝不允许客户端影响任何一次抽取。
/// </summary>
public interface IGachaRandom
{
    /// <summary>返回 [0, exclusiveUpperBound) 区间内的一个整数。</summary>
    int Next(int exclusiveUpperBound);
}

/// <summary>生产随机源。</summary>
public sealed class CryptoGachaRandom : IGachaRandom
{
    public int Next(int exclusiveUpperBound) =>
        System.Security.Cryptography.RandomNumberGenerator.GetInt32(exclusiveUpperBound);
}

/// <summary>
/// 抽奖的纯计算部分。
///
/// 三条保底规则全部在这里执行，且都可以脱离数据库单独测试：
/// 1. 权重抽取——按配置权重加权随机；
/// 2. 十连保底——一次十连中若没有出现 TenPullMinimumQuality 及以上的奖励，把最后一次替换掉；
/// 3. 硬保底——累计到 PityCount 抽仍未出 PityQuality 时，这一抽必定替换为该品质，并重置计数；
///    提前出现该品质同样立即重置计数。
///
/// 客户端不参与其中任何一步，也无法观察到中间状态。
/// </summary>
public static class GachaRoller
{
    public static GachaRollOutcome Roll(
        GachaPoolConfig pool,
        IReadOnlyList<GachaEntryConfig> entries,
        int pullCount,
        int pityCounterBefore,
        IGachaRandom random)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(random);
        if (pullCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pullCount));
        }

        if (entries.Count == 0)
        {
            throw new ArgumentException("Gacha pool has no entries.", nameof(entries));
        }

        var pityThreshold = ConfigQuality.IndexOf(pool.PityQuality);
        var tenPullThreshold = ConfigQuality.IndexOf(pool.TenPullMinimumQuality);
        var rewards = new List<GachaRolledReward>(pullCount);
        var pity = pityCounterBefore;

        for (var index = 0; index < pullCount; index++)
        {
            // 硬保底：这一抽已经是第 PityCount 抽，必须直接给出保底品质。
            var forcePity = pool.PityCount > 0 && pity + 1 >= pool.PityCount;
            var reward = forcePity
                ? PickByQuality(entries, pityThreshold, random)
                : PickByWeight(entries, random);

            rewards.Add(ToReward(reward));

            // 无论是保底给出还是自然抽出，达到保底品质都重置计数。
            pity = ConfigQuality.IndexOf(reward.Quality) >= pityThreshold ? 0 : pity + 1;
        }

        // 十连保底：整轮结束后仍没有达到门槛品质时，替换最后一个奖励。
        if (pullCount >= 10 && tenPullThreshold >= 0 &&
            !rewards.Any(reward => ConfigQuality.IndexOf(reward.Quality) >= tenPullThreshold))
        {
            var replacement = PickByQuality(entries, tenPullThreshold, random);
            rewards[^1] = ToReward(replacement);
            if (ConfigQuality.IndexOf(replacement.Quality) >= pityThreshold)
            {
                pity = 0;
            }
        }

        return new GachaRollOutcome(rewards, pity);
    }

    private static GachaRolledReward ToReward(GachaEntryConfig entry) =>
        new(entry.RewardId, entry.ItemId, entry.Amount, entry.Quality);

    private static GachaEntryConfig PickByWeight(
        IReadOnlyList<GachaEntryConfig> entries,
        IGachaRandom random)
    {
        var total = 0;
        foreach (var entry in entries)
        {
            total = checked(total + Math.Max(entry.Weight, 0));
        }

        if (total <= 0)
        {
            // 配置编译器保证总权重大于 0；真的走到这里说明配置被绕过了，直接失败而不是静默返回第一项。
            throw new InvalidOperationException("Gacha pool total weight must be positive.");
        }

        var roll = random.Next(total);
        foreach (var entry in entries)
        {
            var weight = Math.Max(entry.Weight, 0);
            if (roll < weight)
            {
                return entry;
            }

            roll -= weight;
        }

        return entries[^1];
    }

    /// <summary>在指定品质及以上的奖励中按权重抽取。用于两种保底替换。</summary>
    private static GachaEntryConfig PickByQuality(
        IReadOnlyList<GachaEntryConfig> entries,
        int minimumQualityIndex,
        IGachaRandom random)
    {
        var candidates = entries
            .Where(entry => ConfigQuality.IndexOf(entry.Quality) >= minimumQualityIndex && entry.Weight > 0)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                "Gacha pool has no reward at or above the required quality; the configuration compiler should have rejected it.");
        }

        return PickByWeight(candidates, random);
    }
}
