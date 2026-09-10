using System.Collections.Concurrent;
using Naraka.Server.Application.Config;
using Naraka.Server.Application.Progression;

namespace Naraka.Server.Application.Tests;

/// <summary>
/// 账号资料与初始赠送的业务规则。
///
/// 使用仓库中真实生成的配置：默认英雄、默认头像和 1000/1000/1000 的初始赠送
/// 必须与实际发布的配置一致，用假配置测出来的通过毫无意义。
/// </summary>
public sealed class AccountProfileServiceTests
{
    private static readonly GameConfig Config = LoadConfig();

    private static GameConfig LoadConfig()
    {
        var json = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Config", "naraka-config.json"),
            System.Text.Encoding.UTF8);
        var result = GameConfig.Load(json, GameConfig.RequiredSchemaVersion);
        return result.Config ?? throw new InvalidOperationException(result.Message);
    }

    private static (AccountProfileService Service, FakeProfileRepository Repository) Create()
    {
        var repository = new FakeProfileRepository();
        return (new AccountProfileService(repository, Config), repository);
    }

    [Fact]
    public void DefaultsComeFromConfigurationNotFromCode()
    {
        var (service, _) = Create();

        var defaults = service.BuildDefaults();

        Assert.Equal(Config.DefaultHeroId, defaults.HeroId);
        Assert.Equal(Config.DefaultWeaponId, defaults.WeaponId);
        Assert.Equal(Config.DefaultPetId, defaults.PetId);
        Assert.Equal(Config.DefaultAvatarId, defaults.AvatarId);
        Assert.Equal(Config.DefaultAvatarFrameId, defaults.AvatarFrameId);
        Assert.Equal(3, defaults.StarterGrants.Count);
        Assert.All(defaults.StarterGrants, grant => Assert.Equal(1000L, grant.Amount));
    }

    [Fact]
    public async Task ProvisioningGrantsOneThousandOfEachCurrency()
    {
        var (service, repository) = Create();

        var result = await service.EnsureProvisionedAsync(42, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Equal(1000L, result.View!.Copper);
        Assert.Equal(1000L, result.View.Silk);
        Assert.Equal(1000L, result.View.Gold);
        Assert.Equal(3, repository.Ledger.Count);
        Assert.All(repository.Ledger, entry => Assert.Equal(CurrencyLedgerReason.StarterGrant, entry.Reason));
    }

    [Fact]
    public async Task ProvisioningIsIdempotentAcrossRepeatedLogins()
    {
        var (service, repository) = Create();

        await service.EnsureProvisionedAsync(42, CancellationToken.None);
        await service.EnsureProvisionedAsync(42, CancellationToken.None);
        var third = await service.EnsureProvisionedAsync(42, CancellationToken.None);

        // 三次登录只能发一次：余额停在 1000，流水只有三条（每种货币一条）。
        Assert.Equal(1000L, third.View!.Copper);
        Assert.Equal(3, repository.Ledger.Count);
        Assert.Equal(1, repository.ProvisionGrantCount);
    }

    [Fact]
    public async Task ProvisioningAddsToExistingBalanceInsteadOfOverwritingIt()
    {
        var (service, repository) = Create();
        // 已有账号在 P1.1-A 时期已经积累了余额，初始赠送不能把它抹成 1000。
        repository.SeedBalance(42, "Copper", 250);

        var result = await service.EnsureProvisionedAsync(42, CancellationToken.None);

        Assert.Equal(1250L, result.View!.Copper);
        Assert.Equal(1000L, result.View.Silk);
    }

    [Fact]
    public async Task NewAccountStartsAtLevelOneWithZeroExperience()
    {
        var (service, _) = Create();

        var result = await service.EnsureProvisionedAsync(42, CancellationToken.None);

        Assert.Equal(0L, result.View!.AccountXp);
        Assert.Equal(1, result.View.AccountLevel);
    }

    [Fact]
    public async Task AccountLevelIsDerivedFromExperienceOnly()
    {
        var (service, repository) = Create();
        await service.EnsureProvisionedAsync(42, CancellationToken.None);
        // 账号经验只能来自任务；这里直接改存储只是为了验证换算。
        repository.SetExperience(42, 100);

        var result = await service.GetProfileAsync(42, CancellationToken.None);

        Assert.Equal(2, result.View!.AccountLevel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveAccountIdIsRejected(long accountId)
    {
        var (service, _) = Create();

        var result = await service.EnsureProvisionedAsync(accountId, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task ProfileOfUnknownAccountIsNotFound()
    {
        var (service, _) = Create();

        var result = await service.GetProfileAsync(999, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task AppearanceAcceptsConfiguredIds()
    {
        var (service, _) = Create();
        await service.EnsureProvisionedAsync(42, CancellationToken.None);
        var avatar = Config.AvatarsInDisplayOrder[3].AvatarId;
        var frame = Config.AvatarFramesInDisplayOrder[2].AvatarFrameId;

        var result = await service.SetAppearanceAsync(42, avatar, frame, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Equal(avatar, result.View!.AvatarId);
        Assert.Equal(frame, result.View.AvatarFrameId);
    }

    [Theory]
    [InlineData("avatar_does_not_exist", "frame_white")]
    [InlineData("avatar_01", "frame_does_not_exist")]
    [InlineData("", "frame_white")]
    [InlineData(null, null)]
    public async Task AppearanceRejectsIdsThatAreNotInConfiguration(string? avatar, string? frame)
    {
        var (service, _) = Create();
        await service.EnsureProvisionedAsync(42, CancellationToken.None);

        var result = await service.SetAppearanceAsync(42, avatar, frame, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task AppearanceOnUnknownAccountIsNotFound()
    {
        var (service, _) = Create();

        var result = await service.SetAppearanceAsync(
            999, Config.DefaultAvatarId, Config.DefaultAvatarFrameId, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task StorageFaultBecomesDatabaseUnavailable()
    {
        var (service, repository) = Create();
        repository.FailWithStorageError = true;

        var result = await service.EnsureProvisionedAsync(42, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.DatabaseUnavailable, result.Status);
    }

    private sealed class FakeProfileRepository : IAccountProfileRepository
    {
        private readonly ConcurrentDictionary<long, AccountProfileRecord> _profiles = new();
        private readonly ConcurrentDictionary<long, Dictionary<string, long>> _balances = new();
        private readonly HashSet<long> _granted = new();

        public List<(long AccountId, string CurrencyId, long Delta, string Reason)> Ledger { get; } = new();

        public int ProvisionGrantCount { get; private set; }

        public bool FailWithStorageError { get; set; }

        public void SeedBalance(long accountId, string currencyId, long amount) =>
            _balances.GetOrAdd(accountId, _ => new Dictionary<string, long>(StringComparer.Ordinal))[currencyId] =
                amount;

        public void SetExperience(long accountId, long accountXp) =>
            _profiles[accountId] = _profiles[accountId] with { AccountXp = accountXp };

        public Task<AccountProfileRecord?> FindProfileAsync(long accountId, CancellationToken cancellationToken)
        {
            Guard();
            return Task.FromResult(_profiles.TryGetValue(accountId, out var profile) ? profile : null);
        }

        public Task<CurrencyBalances?> FindBalancesAsync(long accountId, CancellationToken cancellationToken)
        {
            Guard();
            return Task.FromResult<CurrencyBalances?>(
                _profiles.ContainsKey(accountId)
                    ? new CurrencyBalances(Snapshot(accountId))
                    : null);
        }

        public Task<AccountProvisionResult> ProvisionAsync(
            long accountId,
            AccountProvisionDefaults defaults,
            CancellationToken cancellationToken)
        {
            Guard();

            var created = false;
            var profile = _profiles.GetOrAdd(accountId, _ =>
            {
                created = true;
                return new AccountProfileRecord(
                    defaults.AvatarId, defaults.AvatarFrameId, defaults.HeroId,
                    defaults.WeaponId, defaults.PetId, 0, 0);
            });

            var balances = _balances.GetOrAdd(
                accountId, _ => new Dictionary<string, long>(StringComparer.Ordinal));

            var applied = false;
            if (_granted.Add(accountId))
            {
                foreach (var grant in defaults.StarterGrants)
                {
                    balances.TryGetValue(grant.CurrencyId, out var current);
                    balances[grant.CurrencyId] = current + grant.Amount;
                    Ledger.Add((accountId, grant.CurrencyId, grant.Amount, CurrencyLedgerReason.StarterGrant));
                }

                ProvisionGrantCount++;
                applied = true;
            }

            return Task.FromResult(new AccountProvisionResult(
                profile, new CurrencyBalances(Snapshot(accountId)), created, applied));
        }

        public Task<bool> TryAdvanceInventoryTierAsync(
            long accountId,
            int expectedTier,
            int nextTier,
            CancellationToken cancellationToken)
        {
            Guard();
            if (!_profiles.TryGetValue(accountId, out var profile) || profile.InventoryTier != expectedTier)
            {
                return Task.FromResult(false);
            }

            _profiles[accountId] = profile with { InventoryTier = nextTier };
            return Task.FromResult(true);
        }

        public Task<bool> UpdateAppearanceAsync(
            long accountId, string avatarId, string avatarFrameId, CancellationToken cancellationToken)
        {
            Guard();
            if (!_profiles.TryGetValue(accountId, out var profile))
            {
                return Task.FromResult(false);
            }

            _profiles[accountId] = profile with { AvatarId = avatarId, AvatarFrameId = avatarFrameId };
            return Task.FromResult(true);
        }

        public Task<bool> UpdateLoadoutAsync(
            long accountId, string heroId, string weaponId, string petId, CancellationToken cancellationToken)
        {
            Guard();
            if (!_profiles.TryGetValue(accountId, out var profile))
            {
                return Task.FromResult(false);
            }

            _profiles[accountId] = profile with
            {
                SelectedHeroId = heroId,
                SelectedWeaponId = weaponId,
                SelectedPetId = petId
            };
            return Task.FromResult(true);
        }

        private Dictionary<string, long> Snapshot(long accountId) =>
            _balances.TryGetValue(accountId, out var balances)
                ? new Dictionary<string, long>(balances, StringComparer.Ordinal)
                : new Dictionary<string, long>(StringComparer.Ordinal);

        private void Guard()
        {
            if (FailWithStorageError)
            {
                throw new AccountProfileStorageException("injected storage fault");
            }
        }
    }
}
