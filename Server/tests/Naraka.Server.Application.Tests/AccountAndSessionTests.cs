using Naraka.Server.Application.Accounts;
using Naraka.Server.Application.Sessions;

namespace Naraka.Server.Application.Tests;

public sealed class AccountAndSessionTests
{
    [Fact]
    public async Task RegistrationHashesPasswordBeforePersistence()
    {
        var repository = new FakeAccountRepository();
        var hasher = new FakePasswordHasher();
        var service = new AccountService(repository, hasher);

        var result = await service.RegisterAsync(" player-one ", "correct-horse-battery", CancellationToken.None);

        Assert.Equal(AccountRegistrationStatus.Success, result.Status);
        Assert.Equal(42, result.AccountId);
        Assert.Equal("player-one", repository.Created?.Username);
        Assert.Equal(FakePasswordHasher.Hash, repository.Created?.PasswordHash);
        Assert.Equal(FakePasswordHasher.Salt, repository.Created?.PasswordSalt);
        Assert.Equal(FakePasswordHasher.Parameters, repository.Created?.PasswordParameters);
    }

    [Fact]
    public async Task DuplicateRegistrationDoesNotHashOrCreate()
    {
        var repository = new FakeAccountRepository
        {
            Existing = CreateCredential(7, status: 0)
        };
        var hasher = new FakePasswordHasher();
        var service = new AccountService(repository, hasher);

        var result = await service.RegisterAsync("player-one", "correct-horse-battery", CancellationToken.None);

        Assert.Equal(AccountRegistrationStatus.AlreadyExists, result.Status);
        Assert.Equal(0, hasher.HashCalls);
        Assert.Null(repository.Created);
    }

    [Theory]
    [InlineData(null, AccountAuthenticationStatus.UserNotFound)]
    [InlineData((byte)1, AccountAuthenticationStatus.Forbidden)]
    public async Task AuthenticationRejectsMissingOrForbiddenAccount(
        byte? status,
        AccountAuthenticationStatus expected)
    {
        var repository = new FakeAccountRepository
        {
            Existing = status is null ? null : CreateCredential(7, status.Value)
        };
        var service = new AccountService(repository, new FakePasswordHasher());

        var result = await service.AuthenticateAsync("player-one", "correct-horse-battery", CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.AccountId);
    }

    [Fact]
    public async Task SuccessfulLoginBindsAndDisconnectRemovesAuthoritativeSession()
    {
        var repository = new FakeAccountRepository
        {
            Existing = CreateCredential(7, status: 0)
        };
        var sessions = new InMemoryAuthenticatedSessionRegistry();
        var login = new LoginSessionService(
            new AccountService(repository, new FakePasswordHasher()),
            sessions);
        var connectionId = ConnectionId.New();

        var result = await login.LoginAsync(
            connectionId,
            "player-one",
            "correct-horse-battery",
            CancellationToken.None);

        Assert.Equal(AccountAuthenticationStatus.Success, result.Status);
        Assert.Equal(7, result.Session?.AccountId);
        Assert.True(sessions.TryGet(connectionId, out var stored));
        Assert.Equal(7, stored?.AccountId);
        Assert.True(login.Disconnect(connectionId));
        Assert.False(sessions.TryGet(connectionId, out _));
    }

    private static AccountCredentialRecord CreateCredential(long id, byte status) =>
        new(id, "player-one", FakePasswordHasher.Hash, FakePasswordHasher.Salt, FakePasswordHasher.Parameters, status);

    private sealed class FakeAccountRepository : IAccountRepository
    {
        public AccountCredentialRecord? Existing { get; init; }

        public CreateAccountRecord? Created { get; private set; }

        public Task<AccountCredentialRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken) =>
            Task.FromResult(Existing);

        public Task<long> CreateAsync(CreateAccountRecord account, CancellationToken cancellationToken)
        {
            Created = account;
            return Task.FromResult(42L);
        }
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public static readonly byte[] Hash = [1, 2, 3];
        public static readonly byte[] Salt = [4, 5, 6];
        public const string Parameters = "fixture";

        public int HashCalls { get; private set; }

        public ValueTask<PasswordHashResult> HashAsync(string password, CancellationToken cancellationToken)
        {
            HashCalls++;
            return ValueTask.FromResult(new PasswordHashResult(Hash, Salt, Parameters));
        }

        public ValueTask<bool> VerifyAsync(
            string password,
            byte[] expectedHash,
            byte[] salt,
            string parameters,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(password == "correct-horse-battery");
    }
}
