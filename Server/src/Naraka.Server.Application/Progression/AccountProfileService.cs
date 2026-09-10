using Naraka.Server.Application.Config;

namespace Naraka.Server.Application.Progression;

public sealed record AccountProfileView(
    string AvatarId,
    string AvatarFrameId,
    string SelectedHeroId,
    string SelectedWeaponId,
    string SelectedPetId,
    long AccountXp,
    int AccountLevel,
    int InventoryTier,
    long Copper,
    long Silk,
    long Gold);

public readonly struct AccountProfileResult
{
    private AccountProfileResult(LobbyOperationStatus status, AccountProfileView? view)
    {
        Status = status;
        View = view;
    }

    public LobbyOperationStatus Status { get; }

    public AccountProfileView? View { get; }

    public static AccountProfileResult Success(AccountProfileView view) =>
        new(LobbyOperationStatus.Success, view);

    public static AccountProfileResult Failed(LobbyOperationStatus status) => new(status, null);
}

/// <summary>
/// 账号资料的读取与修改。
///
/// 这里是"新账号获得 1000/1000/1000"这条规则唯一的执行点：调用方只提供已认证的 AccountId，
/// 赠送数量、默认英雄和默认头像全部来自生成配置，客户端无法影响其中任何一项。
/// </summary>
public sealed class AccountProfileService(
    IAccountProfileRepository repository,
    GameConfig config)
{
    private readonly IAccountProfileRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    private readonly GameConfig _config = config ?? throw new ArgumentNullException(nameof(config));

    /// <summary>
    /// 确保账号已开通并返回最新资料。登录成功后调用一次即可：
    /// 已开通的账号只走一次读取，不会重复赠送，也不会改写既有余额。
    /// </summary>
    public async Task<AccountProfileResult> EnsureProvisionedAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return AccountProfileResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            var result = await _repository.ProvisionAsync(accountId, BuildDefaults(), cancellationToken);
            return AccountProfileResult.Success(ToView(result.Profile, result.Balances));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AccountProfileStorageException)
        {
            return AccountProfileResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return AccountProfileResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    public async Task<AccountProfileResult> GetProfileAsync(long accountId, CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return AccountProfileResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            var profile = await _repository.FindProfileAsync(accountId, cancellationToken);
            if (profile is null)
            {
                return AccountProfileResult.Failed(LobbyOperationStatus.NotFound);
            }

            var balances = await _repository.FindBalancesAsync(accountId, cancellationToken);
            return balances is null
                ? AccountProfileResult.Failed(LobbyOperationStatus.NotFound)
                : AccountProfileResult.Success(ToView(profile, balances));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AccountProfileStorageException)
        {
            return AccountProfileResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return AccountProfileResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    /// <summary>
    /// 修改头像与头像框。两个 ID 都必须存在于配置中，
    /// 否则客户端可以写入任意字符串，界面之后就再也找不到对应贴图。
    /// </summary>
    public async Task<AccountProfileResult> SetAppearanceAsync(
        long accountId,
        string? avatarId,
        string? avatarFrameId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return AccountProfileResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        var avatar = avatarId?.Trim() ?? string.Empty;
        var frame = avatarFrameId?.Trim() ?? string.Empty;
        if (!_config.TryGetAvatar(avatar, out _) || !_config.TryGetAvatarFrame(frame, out _))
        {
            return AccountProfileResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            if (!await _repository.UpdateAppearanceAsync(accountId, avatar, frame, cancellationToken))
            {
                return AccountProfileResult.Failed(LobbyOperationStatus.NotFound);
            }

            return await GetProfileAsync(accountId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AccountProfileStorageException)
        {
            return AccountProfileResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return AccountProfileResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    /// <summary>
    /// 修改出战英雄、兵器与宠物。宠物必须是账号已经拥有的：
    /// P1 只有默认宠物是拥有状态，破锋要等到 P3 击败赤霄炎龙才解锁。
    /// </summary>
    public async Task<AccountProfileResult> SetLoadoutAsync(
        long accountId,
        string? heroId,
        string? weaponId,
        string? petId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return AccountProfileResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        var hero = heroId?.Trim() ?? string.Empty;
        var weapon = weaponId?.Trim() ?? string.Empty;
        var pet = petId?.Trim() ?? string.Empty;

        if (!_config.TryGetHero(hero, out _) || !_config.TryGetWeapon(weapon, out _))
        {
            return AccountProfileResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        if (!_config.TryGetPet(pet, out var petConfig) || petConfig is null)
        {
            return AccountProfileResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        if (!petConfig.DefaultOwned)
        {
            // 未解锁的宠物不是"请求非法"而是"当前不可用"，客户端据此给出正确提示。
            return AccountProfileResult.Failed(LobbyOperationStatus.NotAvailable);
        }

        try
        {
            if (!await _repository.UpdateLoadoutAsync(accountId, hero, weapon, pet, cancellationToken))
            {
                return AccountProfileResult.Failed(LobbyOperationStatus.NotFound);
            }

            return await GetProfileAsync(accountId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AccountProfileStorageException)
        {
            return AccountProfileResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return AccountProfileResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    public AccountProvisionDefaults BuildDefaults() => new(
        _config.DefaultAvatarId,
        _config.DefaultAvatarFrameId,
        _config.DefaultHeroId,
        _config.DefaultWeaponId,
        _config.DefaultPetId,
        _config.Currencies
            .Where(currency => currency.StarterGrant > 0)
            .OrderBy(currency => currency.CurrencyId, StringComparer.Ordinal)
            .Select(currency => new CurrencyGrant(currency.CurrencyId, currency.StarterGrant))
            .ToArray());

    private AccountProfileView ToView(AccountProfileRecord profile, CurrencyBalances balances) => new(
        profile.AvatarId,
        profile.AvatarFrameId,
        profile.SelectedHeroId,
        profile.SelectedWeaponId,
        profile.SelectedPetId,
        profile.AccountXp,
        // 账号等级完全由账号经验推导，经验只能来自任务。
        _config.ResolveAccountLevel(profile.AccountXp),
        profile.InventoryTier,
        balances.Get("Copper"),
        balances.Get("Silk"),
        balances.Get("Gold"));
}
