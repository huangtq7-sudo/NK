using Naraka.Server.Application.Config;
using Naraka.Server.Application.Inventory;
using Naraka.Server.Application.Progression;

namespace Naraka.Server.Application.Forge;

/// <summary>
/// 一把武器的账号内唯一实例。武器不进入仓库，因此没有数量，只有等级与战斗统计。
/// </summary>
public sealed record AccountWeapon(string WeaponId, int Level, long Proficiency, long KillCount);

/// <summary>一次强化尝试的结果。</summary>
public enum ForgeUpgradeOutcome
{
    Applied = 0,

    /// <summary>同一个 RequestId 已经成功过。重放首次结果，不再消耗也不再升级。</summary>
    AlreadyApplied = 1,
    InsufficientCurrency = 2,
    InsufficientItems = 3,
    LevelMismatch = 4,
    AccountMissing = 5
}

public sealed record ForgeUpgradeResult(ForgeUpgradeOutcome Outcome, int LevelAfter);

/// <summary>
/// 一次强化的完整参数。配方、消耗与目标等级都由 Application 从配置算好后传入，
/// 仓储不读取配置，也不做业务判断。
/// </summary>
public sealed record ForgeUpgradeCommand(
    long AccountId,
    string WeaponId,
    int FromLevel,
    int ToLevel,
    string CurrencyId,
    long CurrencyAmount,
    IReadOnlyList<InventoryDelta> MaterialCosts,
    string RequestId);

public sealed class ForgeStorageException : Exception
{
    public ForgeStorageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public interface IWeaponRepository
{
    Task<IReadOnlyList<AccountWeapon>> ListAsync(long accountId, CancellationToken cancellationToken);

    /// <summary>
    /// 确保账号拥有配置中的每一把武器，缺失的补建为 1 级。
    /// 重复调用不会重置已有等级——那会抹掉玩家的全部强化进度。
    /// </summary>
    Task EnsureWeaponsAsync(
        long accountId,
        IReadOnlyList<string> weaponIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// 在<b>一个</b>事务内完成材料消耗、扣费、货币流水与武器升级。
    /// 任何一步失败都整体回滚，因此不会出现"扣了材料没升级"。
    /// </summary>
    Task<ForgeUpgradeResult> TryUpgradeAsync(ForgeUpgradeCommand command, CancellationToken cancellationToken);
}

/// <summary>锻造界面需要的一次性快照：武器状态 + 当前余额 + 相关材料库存。</summary>
public sealed record ForgeView(
    IReadOnlyList<AccountWeapon> Weapons,
    IReadOnlyList<InventorySlot> Materials,
    CurrencyBalances Balances);

public readonly struct ForgeResult
{
    private ForgeResult(LobbyOperationStatus status, ForgeView? view)
    {
        Status = status;
        View = view;
    }

    public LobbyOperationStatus Status { get; }

    public ForgeView? View { get; }

    public static ForgeResult Success(ForgeView view) => new(LobbyOperationStatus.Success, view);

    public static ForgeResult Failed(LobbyOperationStatus status) => new(status, null);
}

/// <summary>
/// 锻造业务。
///
/// 玩法基线明确规定：<b>材料足够时强化必定成功</b>，没有随机失败。
/// 因此这里没有任何随机数——成功与否完全由"材料与货币是否足够"决定，
/// 界面上的"必定成功"不是安慰性文案，而是这段代码的事实。
/// </summary>
public sealed class ForgeService(
    IWeaponRepository weapons,
    IInventoryRepository inventory,
    IAccountProfileRepository profiles,
    GameConfig config)
{
    private readonly IWeaponRepository _weapons = weapons ?? throw new ArgumentNullException(nameof(weapons));

    private readonly IInventoryRepository _inventory =
        inventory ?? throw new ArgumentNullException(nameof(inventory));

    private readonly IAccountProfileRepository _profiles =
        profiles ?? throw new ArgumentNullException(nameof(profiles));

    private readonly GameConfig _config = config ?? throw new ArgumentNullException(nameof(config));

    public async Task<ForgeResult> GetAsync(long accountId, CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return ForgeResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            await _weapons.EnsureWeaponsAsync(accountId, ConfiguredWeaponIds(), cancellationToken);
            return await ReadViewAsync(accountId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return ForgeResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return ForgeResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    /// <summary>
    /// 强化一级。
    ///
    /// <paramref name="expectedLevel"/> 是客户端界面上显示的当前等级：带上它可以把
    /// "界面已经过期"与"材料不足"区分开，玩家因此不会因为一次陈旧的点击而多消耗一次材料。
    /// </summary>
    public async Task<ForgeResult> UpgradeAsync(
        long accountId,
        string? weaponId,
        int expectedLevel,
        string? requestId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 ||
            string.IsNullOrWhiteSpace(weaponId) ||
            string.IsNullOrWhiteSpace(requestId) ||
            requestId.Length > 64)
        {
            return ForgeResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        if (!_config.TryGetWeapon(weaponId, out var weapon) || weapon is null)
        {
            return ForgeResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            await _weapons.EnsureWeaponsAsync(accountId, ConfiguredWeaponIds(), cancellationToken);

            var owned = await _weapons.ListAsync(accountId, cancellationToken);
            var current = owned.FirstOrDefault(entry => entry.WeaponId == weaponId);
            if (current is null)
            {
                return ForgeResult.Failed(LobbyOperationStatus.NotFound);
            }

            if (expectedLevel > 0 && expectedLevel != current.Level)
            {
                // 界面显示的等级与服务端不符：让客户端刷新后重试，而不是按陈旧数据消耗材料。
                return ForgeResult.Failed(LobbyOperationStatus.Conflict);
            }

            if (current.Level >= weapon.MaxLevel)
            {
                return ForgeResult.Failed(LobbyOperationStatus.NotAvailable);
            }

            if (!_config.TryGetForgeRecipe(weaponId, current.Level, out var recipe) || recipe is null)
            {
                return ForgeResult.Failed(LobbyOperationStatus.NotAvailable);
            }

            var command = new ForgeUpgradeCommand(
                accountId,
                weaponId,
                current.Level,
                recipe.ToLevel,
                recipe.CurrencyId,
                recipe.CurrencyAmount,
                ToMaterialCosts(recipe.Material1ItemId, recipe.Material1Amount,
                    recipe.Material2ItemId, recipe.Material2Amount),
                requestId!);

            var result = await _weapons.TryUpgradeAsync(command, cancellationToken);
            return result.Outcome switch
            {
                ForgeUpgradeOutcome.Applied or ForgeUpgradeOutcome.AlreadyApplied =>
                    await ReadViewAsync(accountId, cancellationToken),
                ForgeUpgradeOutcome.InsufficientCurrency =>
                    ForgeResult.Failed(LobbyOperationStatus.InsufficientCurrency),
                ForgeUpgradeOutcome.InsufficientItems =>
                    ForgeResult.Failed(LobbyOperationStatus.InsufficientItems),
                ForgeUpgradeOutcome.LevelMismatch =>
                    ForgeResult.Failed(LobbyOperationStatus.Conflict),
                ForgeUpgradeOutcome.AccountMissing =>
                    ForgeResult.Failed(LobbyOperationStatus.NotFound),
                _ => ForgeResult.Failed(LobbyOperationStatus.InternalError)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return ForgeResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return ForgeResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    private static IReadOnlyList<InventoryDelta> ToMaterialCosts(
        string firstItemId,
        int firstAmount,
        string secondItemId,
        int secondAmount)
    {
        var costs = new List<InventoryDelta>(2);
        if (!string.IsNullOrEmpty(firstItemId) && firstAmount > 0)
        {
            costs.Add(new InventoryDelta(firstItemId, -firstAmount));
        }

        if (!string.IsNullOrEmpty(secondItemId) && secondAmount > 0)
        {
            costs.Add(new InventoryDelta(secondItemId, -secondAmount));
        }

        return costs;
    }

    private IReadOnlyList<string> ConfiguredWeaponIds() =>
        _config.WeaponsInDisplayOrder.Select(weapon => weapon.WeaponId).ToArray();

    private async Task<ForgeResult> ReadViewAsync(long accountId, CancellationToken cancellationToken)
    {
        var balances = await _profiles.FindBalancesAsync(accountId, cancellationToken);
        if (balances is null)
        {
            return ForgeResult.Failed(LobbyOperationStatus.NotFound);
        }

        return ForgeResult.Success(new ForgeView(
            await _weapons.ListAsync(accountId, cancellationToken),
            await _inventory.ListSlotsAsync(accountId, cancellationToken),
            balances));
    }

    private static bool IsStorageFault(Exception exception) =>
        exception is ForgeStorageException or InventoryStorageException or AccountProfileStorageException;
}
