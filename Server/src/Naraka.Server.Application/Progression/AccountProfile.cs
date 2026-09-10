namespace Naraka.Server.Application.Progression;

/// <summary>
/// 账号级单例状态。头像、头像框与出战选择都使用稳定配置 ID，绝不使用列表下标：
/// 配置里插入一个英雄就会让下标全部错位，而玩家的出战选择必须跨版本稳定。
/// </summary>
public sealed record AccountProfileRecord(
    string AvatarId,
    string AvatarFrameId,
    string SelectedHeroId,
    string SelectedWeaponId,
    string SelectedPetId,
    long AccountXp,
    int InventoryTier);

/// <summary>新账号首次登录时写入的默认值与初始赠送。全部来自生成配置，不在代码里写死。</summary>
public sealed record AccountProvisionDefaults(
    string AvatarId,
    string AvatarFrameId,
    string HeroId,
    string WeaponId,
    string PetId,
    IReadOnlyList<CurrencyGrant> StarterGrants);

/// <summary>一次货币赠送。</summary>
public sealed record CurrencyGrant(string CurrencyId, long Amount);

/// <summary>货币余额快照。键为配置中的 CurrencyId。</summary>
public sealed record CurrencyBalances(IReadOnlyDictionary<string, long> Balances)
{
    public long Get(string currencyId) => Balances.TryGetValue(currencyId, out var value) ? value : 0L;
}

/// <summary>
/// 一次账号开通的结果。
/// <paramref name="StarterGrantApplied"/> 为 false 表示该账号此前已经领取过初始赠送，
/// 这一次没有重复发放——这正是幂等的可观测证据。
/// </summary>
public sealed record AccountProvisionResult(
    AccountProfileRecord Profile,
    CurrencyBalances Balances,
    bool ProfileCreated,
    bool StarterGrantApplied);

/// <summary>货币流水的变动原因。写入不可变流水表，供对账使用。</summary>
public static class CurrencyLedgerReason
{
    public const string StarterGrant = "StarterGrant";
    public const string ShopPurchase = "ShopPurchase";
    public const string ItemSell = "ItemSell";
    public const string Forge = "Forge";
    public const string Gacha = "Gacha";
    public const string SignInReward = "SignInReward";
    public const string SignInMilestone = "SignInMilestone";
    public const string AccountLevelReward = "AccountLevelReward";
    public const string AchievementReward = "AchievementReward";
    public const string InventoryExpand = "InventoryExpand";
}

/// <summary>一次性赠送的键。同一账号同一个键只会执行一次。</summary>
public static class AccountGrantKey
{
    public const string StarterGrant = "StarterGrant";
}

/// <summary>存储层故障。Application 把它映射为稳定的 DatabaseUnavailable，不泄露连接串或驱动异常文本。</summary>
public sealed class AccountProfileStorageException : Exception
{
    public AccountProfileStorageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public interface IAccountProfileRepository
{
    /// <summary>读取账号资料。尚未开通时返回 null。</summary>
    Task<AccountProfileRecord?> FindProfileAsync(long accountId, CancellationToken cancellationToken);

    Task<CurrencyBalances?> FindBalancesAsync(long accountId, CancellationToken cancellationToken);

    /// <summary>
    /// 在一个事务内确保资料行存在，并在尚未发放时按 <paramref name="defaults"/> 发放初始赠送。
    ///
    /// 必须同时满足：
    /// 一次赠送记录 + 余额增加 + 每种货币一条不可变流水，任一步失败全部回滚；
    /// 同一账号重复调用只发放一次，且不得把已有余额覆盖成赠送值。
    /// </summary>
    Task<AccountProvisionResult> ProvisionAsync(
        long accountId,
        AccountProvisionDefaults defaults,
        CancellationToken cancellationToken);

    /// <summary>更新头像与头像框。账号不存在资料行时返回 false。</summary>
    Task<bool> UpdateAppearanceAsync(
        long accountId,
        string avatarId,
        string avatarFrameId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 把仓库容量档位从 <paramref name="expectedTier"/> 推进到 <paramref name="nextTier"/>。
    ///
    /// 带上期望值是为了让并发扩容变成一次比较并交换：两个请求同时到达时只有一个能成功推进，
    /// 另一个看到 false 后按已扣费的事实返回当前状态，而不是把档位推进两次。
    /// </summary>
    Task<bool> TryAdvanceInventoryTierAsync(
        long accountId,
        int expectedTier,
        int nextTier,
        CancellationToken cancellationToken);

    /// <summary>更新出战英雄、兵器与宠物。账号不存在资料行时返回 false。</summary>
    Task<bool> UpdateLoadoutAsync(
        long accountId,
        string heroId,
        string weaponId,
        string petId,
        CancellationToken cancellationToken);
}
