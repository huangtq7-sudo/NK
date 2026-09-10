using Naraka.Config;
using Naraka.Server.Application.Config;
using Naraka.Server.Application.Inventory;
using Naraka.Server.Application.Progression;

namespace Naraka.Server.Application.Gacha;

/// <summary>一张已经固化的抽奖订单。<paramref name="IsShown"/> 为 false 表示客户端还没确认展示过。</summary>
public sealed record GachaOrder(
    string OrderId,
    string PoolId,
    int PullCount,
    bool IsShown,
    IReadOnlyList<GachaRolledReward> Rewards);

public sealed record GachaAccountState(string PoolId, int PityCounter, long TotalPulls);

public enum GachaPullOutcome
{
    Applied = 0,

    /// <summary>同一个 OrderId 已经成功过。重放首次结果，不再扣费也不再重新抽取。</summary>
    AlreadyApplied = 1,
    InsufficientCurrency = 2,
    InventoryFull = 3,
    AccountMissing = 4
}

public sealed record GachaPullResult(GachaPullOutcome Outcome, GachaOrder? Order);

/// <summary>
/// 一次抽奖落盘的完整参数。价格、奖励与保底计数都由 Application 算好后传入，
/// 仓储只负责在一个事务里原子地写下去。
/// </summary>
public sealed record GachaPullCommand(
    long AccountId,
    string OrderId,
    string PoolId,
    int PullCount,
    string CurrencyId,
    long Price,
    IReadOnlyList<GachaRolledReward> Rewards,
    IReadOnlyDictionary<string, int> StackLimits,
    int Capacity,
    int PityCounterAfter);

public sealed class GachaStorageException : Exception
{
    public GachaStorageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public interface IGachaRepository
{
    Task<GachaAccountState?> FindStateAsync(long accountId, string poolId, CancellationToken cancellationToken);

    /// <summary>尚未确认展示的订单，按创建时间升序。断线或跳过动画后由它恢复结果。</summary>
    Task<IReadOnlyList<GachaOrder>> ListUnshownOrdersAsync(long accountId, CancellationToken cancellationToken);

    /// <summary>
    /// 在<b>一个</b>事务内完成扣费、货币流水、奖励入库、订单与结果固化、保底计数更新。
    /// 全部成功之后客户端才会看到结果，因此动画播放的永远是一个已经存在于数据库的事实。
    /// </summary>
    Task<GachaPullResult> TryPullAsync(GachaPullCommand command, CancellationToken cancellationToken);

    /// <summary>标记订单已经展示。重复标记是安全的。</summary>
    Task<bool> MarkShownAsync(long accountId, string orderId, CancellationToken cancellationToken);
}

public sealed record GachaView(
    GachaAccountState State,
    IReadOnlyList<GachaOrder> UnshownOrders,
    CurrencyBalances Balances);

public readonly struct GachaResult
{
    private GachaResult(LobbyOperationStatus status, GachaView? view, GachaOrder? order)
    {
        Status = status;
        View = view;
        Order = order;
    }

    public LobbyOperationStatus Status { get; }

    public GachaView? View { get; }

    /// <summary>本次抽奖固化的订单。只有抽奖请求会带它。</summary>
    public GachaOrder? Order { get; }

    public static GachaResult Success(GachaView view, GachaOrder? order = null) =>
        new(LobbyOperationStatus.Success, view, order);

    public static GachaResult Failed(LobbyOperationStatus status) => new(status, null, null);
}

/// <summary>
/// 抽奖业务。
///
/// 顺序是刻意的：<b>先由服务端原子地扣费、抽取、发奖并固化结果，客户端再播放动画</b>。
/// 因此动画被跳过、中断、崩溃或断线都不会丢失或重复任何一次抽取——
/// 下次登录读回尚未确认展示的订单即可继续。
/// </summary>
public sealed class GachaService(
    IGachaRepository gacha,
    IAccountProfileRepository profiles,
    GameConfig config,
    IGachaRandom random)
{
    /// <summary>单次请求允许的最大抽数。十连是上限，防止一次请求写出超长订单。</summary>
    public const int MaximumPullCount = 10;

    private readonly IGachaRepository _gacha = gacha ?? throw new ArgumentNullException(nameof(gacha));

    private readonly IAccountProfileRepository _profiles =
        profiles ?? throw new ArgumentNullException(nameof(profiles));

    private readonly GameConfig _config = config ?? throw new ArgumentNullException(nameof(config));

    private readonly IGachaRandom _random = random ?? throw new ArgumentNullException(nameof(random));

    public async Task<GachaResult> GetAsync(long accountId, string? poolId, CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return GachaResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        var pool = ResolvePool(poolId);
        if (pool is null)
        {
            return GachaResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            return await ReadViewAsync(accountId, pool.PoolId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return GachaResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return GachaResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    public async Task<GachaResult> PullAsync(
        long accountId,
        string? poolId,
        int pullCount,
        string? orderId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 ||
            pullCount <= 0 ||
            pullCount > MaximumPullCount ||
            string.IsNullOrWhiteSpace(orderId) ||
            orderId.Length > 64)
        {
            return GachaResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        // 只允许单抽与十连两种形态；中间数量既没有配置价格，也不是玩法允许的操作。
        if (pullCount != 1 && pullCount != MaximumPullCount)
        {
            return GachaResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        var pool = ResolvePool(poolId);
        if (pool is null)
        {
            return GachaResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        var entries = _config.GetGachaEntries(pool.PoolId);
        if (entries.Count == 0)
        {
            return GachaResult.Failed(LobbyOperationStatus.InternalError);
        }

        try
        {
            var profile = await _profiles.FindProfileAsync(accountId, cancellationToken);
            if (profile is null)
            {
                return GachaResult.Failed(LobbyOperationStatus.NotFound);
            }

            var state = await _gacha.FindStateAsync(accountId, pool.PoolId, cancellationToken);
            var pityBefore = state?.PityCounter ?? 0;

            // 抽取发生在服务端，客户端既不提供随机数也无法观察中间状态。
            var outcome = GachaRoller.Roll(pool, entries, pullCount, pityBefore, _random);

            var command = new GachaPullCommand(
                accountId,
                orderId!,
                pool.PoolId,
                pullCount,
                pool.CurrencyId,
                pullCount == MaximumPullCount ? pool.TenPullPrice : pool.SinglePrice,
                outcome.Rewards,
                StackLimits(),
                _config.GetInventoryCapacity(profile.InventoryTier),
                outcome.PityCounterAfter);

            var result = await _gacha.TryPullAsync(command, cancellationToken);
            switch (result.Outcome)
            {
                case GachaPullOutcome.Applied:
                case GachaPullOutcome.AlreadyApplied:
                    var view = await ReadViewAsync(accountId, pool.PoolId, cancellationToken);
                    return view.Status == LobbyOperationStatus.Success
                        ? GachaResult.Success(view.View!, result.Order)
                        : view;

                case GachaPullOutcome.InsufficientCurrency:
                    return GachaResult.Failed(LobbyOperationStatus.InsufficientCurrency);
                case GachaPullOutcome.InventoryFull:
                    return GachaResult.Failed(LobbyOperationStatus.InventoryFull);
                case GachaPullOutcome.AccountMissing:
                    return GachaResult.Failed(LobbyOperationStatus.NotFound);
                default:
                    return GachaResult.Failed(LobbyOperationStatus.InternalError);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return GachaResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return GachaResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    /// <summary>
    /// 确认某个订单已经展示过。只有确认之后它才不再出现在"未展示结果"里，
    /// 因此玩家永远不会因为一次中断而错过已经属于自己的奖励。
    /// </summary>
    public async Task<GachaResult> AcknowledgeAsync(
        long accountId,
        string? poolId,
        string? orderId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 || string.IsNullOrWhiteSpace(orderId) || orderId.Length > 64)
        {
            return GachaResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        var pool = ResolvePool(poolId);
        if (pool is null)
        {
            return GachaResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            // 标记不存在的订单不是错误：重复确认必须是安全的。
            await _gacha.MarkShownAsync(accountId, orderId!, cancellationToken);
            return await ReadViewAsync(accountId, pool.PoolId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return GachaResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return GachaResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    /// <summary>默认奖池。配置里只有一个奖池时客户端可以不传 PoolId。</summary>
    public string DefaultPoolId =>
        _config.Catalog.GachaPools.Length > 0 ? _config.Catalog.GachaPools[0].PoolId : string.Empty;

    private GachaPoolConfig? ResolvePool(string? poolId)
    {
        var id = string.IsNullOrWhiteSpace(poolId) ? DefaultPoolId : poolId;
        return _config.TryGetGachaPool(id, out var pool) ? pool : null;
    }

    private IReadOnlyDictionary<string, int> StackLimits() =>
        _config.Catalog.Items.ToDictionary(item => item.ItemId, item => item.StackLimit, StringComparer.Ordinal);

    private async Task<GachaResult> ReadViewAsync(
        long accountId,
        string poolId,
        CancellationToken cancellationToken)
    {
        var balances = await _profiles.FindBalancesAsync(accountId, cancellationToken);
        if (balances is null)
        {
            return GachaResult.Failed(LobbyOperationStatus.NotFound);
        }

        var state = await _gacha.FindStateAsync(accountId, poolId, cancellationToken)
                    ?? new GachaAccountState(poolId, 0, 0);

        return GachaResult.Success(new GachaView(
            state,
            await _gacha.ListUnshownOrdersAsync(accountId, cancellationToken),
            balances));
    }

    private static bool IsStorageFault(Exception exception) =>
        exception is GachaStorageException or InventoryStorageException or AccountProfileStorageException;
}
