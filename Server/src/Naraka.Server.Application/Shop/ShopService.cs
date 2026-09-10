using Naraka.Server.Application.Config;
using Naraka.Server.Application.Inventory;
using Naraka.Server.Application.Progression;

namespace Naraka.Server.Application.Shop;

/// <summary>某个商品的账号累计购买量。限购上限来自配置，不保存在这里。</summary>
public sealed record ShopPurchaseCount(string ProductId, long PurchasedTotal);

/// <summary>一次购买尝试的结果。每一种失败都对应一个稳定错误码，绝不含糊成"失败"。</summary>
public enum ShopPurchaseOutcome
{
    Applied = 0,

    /// <summary>同一个 RequestId 已经成功过。重放首次结果，不再扣费也不再发货。</summary>
    AlreadyApplied = 1,
    InsufficientCurrency = 2,
    LimitReached = 3,
    InventoryFull = 4,
    AccountMissing = 5
}

public sealed record ShopPurchaseResult(
    ShopPurchaseOutcome Outcome,
    CurrencyBalances? Balances,
    long PurchasedTotal);

public sealed class ShopStorageException : Exception
{
    public ShopStorageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public interface IShopRepository
{
    Task<IReadOnlyList<ShopPurchaseCount>> ListPurchasesAsync(long accountId, CancellationToken cancellationToken);

    /// <summary>
    /// 在<b>一个</b>事务内完成扣费、货币流水、物品入库与限购计数。
    ///
    /// 拆成多个事务是不可接受的：中途失败会留下"扣了钱没发货"或"发了货没扣钱"的账，
    /// 而这两种状态都无法自动修复。
    /// </summary>
    Task<ShopPurchaseResult> TryPurchaseAsync(
        ShopPurchaseCommand command,
        CancellationToken cancellationToken);
}

/// <summary>
/// 一次购买的完整参数。价格、限购、堆叠上限与容量都由 Application 从配置算好后传入，
/// 仓储不读取配置，也不做业务判断。
/// </summary>
public sealed record ShopPurchaseCommand(
    long AccountId,
    string ProductId,
    string ItemId,
    int Quantity,
    string CurrencyId,
    long TotalPrice,
    int PurchaseLimit,
    int StackLimit,
    int Capacity,
    string RequestId);

public sealed record ShopView(
    IReadOnlyList<ShopPurchaseCount> Purchases,
    CurrencyBalances Balances);

public readonly struct ShopResult
{
    private ShopResult(LobbyOperationStatus status, ShopView? view)
    {
        Status = status;
        View = view;
    }

    public LobbyOperationStatus Status { get; }

    public ShopView? View { get; }

    public static ShopResult Success(ShopView view) => new(LobbyOperationStatus.Success, view);

    public static ShopResult Failed(LobbyOperationStatus status) => new(status, null);
}

/// <summary>
/// 商店业务。
///
/// 价格、限购、分类与堆叠上限全部来自生成配置；客户端提交的只有商品 ID 和数量，
/// 因此改价格是一次配置发布，而不是一次代码修改，客户端也无法影响任何一项。
/// </summary>
public sealed class ShopService(
    IShopRepository shop,
    IAccountProfileRepository profiles,
    GameConfig config)
{
    /// <summary>单次购买数量上限。防止一次请求写出一个溢出的总价。</summary>
    public const int MaximumQuantityPerPurchase = 999;

    private readonly IShopRepository _shop = shop ?? throw new ArgumentNullException(nameof(shop));

    private readonly IAccountProfileRepository _profiles =
        profiles ?? throw new ArgumentNullException(nameof(profiles));

    private readonly GameConfig _config = config ?? throw new ArgumentNullException(nameof(config));

    public async Task<ShopResult> GetAsync(long accountId, CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return ShopResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            var balances = await _profiles.FindBalancesAsync(accountId, cancellationToken);
            if (balances is null)
            {
                return ShopResult.Failed(LobbyOperationStatus.NotFound);
            }

            return ShopResult.Success(new ShopView(
                await _shop.ListPurchasesAsync(accountId, cancellationToken), balances));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return ShopResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return ShopResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    public async Task<ShopResult> PurchaseAsync(
        long accountId,
        string? productId,
        int quantity,
        string? requestId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 ||
            string.IsNullOrWhiteSpace(productId) ||
            quantity <= 0 ||
            quantity > MaximumQuantityPerPurchase ||
            string.IsNullOrWhiteSpace(requestId) ||
            requestId.Length > 64)
        {
            return ShopResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        if (!_config.TryGetShopProduct(productId, out var product) || product is null)
        {
            return ShopResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        if (!product.IsAvailable)
        {
            return ShopResult.Failed(LobbyOperationStatus.NotAvailable);
        }

        if (!_config.TryGetItem(product.ItemId, out var item) || item is null)
        {
            // 配置编译器会拦住这种情况；运行时再确认一次，绝不发一个不存在的物品。
            return ShopResult.Failed(LobbyOperationStatus.InternalError);
        }

        try
        {
            var profile = await _profiles.FindProfileAsync(accountId, cancellationToken);
            if (profile is null)
            {
                return ShopResult.Failed(LobbyOperationStatus.NotFound);
            }

            var command = new ShopPurchaseCommand(
                accountId,
                product.ProductId,
                product.ItemId,
                quantity,
                product.CurrencyId,
                checked(product.UnitPrice * quantity),
                product.PurchaseLimit,
                item.StackLimit,
                _config.GetInventoryCapacity(profile.InventoryTier),
                requestId);

            var result = await _shop.TryPurchaseAsync(command, cancellationToken);
            return result.Outcome switch
            {
                ShopPurchaseOutcome.Applied or ShopPurchaseOutcome.AlreadyApplied =>
                    await GetAsync(accountId, cancellationToken),
                ShopPurchaseOutcome.InsufficientCurrency =>
                    ShopResult.Failed(LobbyOperationStatus.InsufficientCurrency),
                ShopPurchaseOutcome.LimitReached =>
                    ShopResult.Failed(LobbyOperationStatus.LimitReached),
                ShopPurchaseOutcome.InventoryFull =>
                    ShopResult.Failed(LobbyOperationStatus.InventoryFull),
                ShopPurchaseOutcome.AccountMissing =>
                    ShopResult.Failed(LobbyOperationStatus.NotFound),
                _ => ShopResult.Failed(LobbyOperationStatus.InternalError)
            };
        }
        catch (OverflowException)
        {
            return ShopResult.Failed(LobbyOperationStatus.InvalidRequest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return ShopResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return ShopResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    private static bool IsStorageFault(Exception exception) =>
        exception is ShopStorageException or AccountProfileStorageException or InventoryStorageException;
}
