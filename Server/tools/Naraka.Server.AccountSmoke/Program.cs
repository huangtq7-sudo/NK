using MySqlConnector;
using Naraka.Server.Application.Accounts;
using Naraka.Server.Infrastructure.Persistence;
using Naraka.Server.Infrastructure.Persistence.Accounts;
using Naraka.Server.Infrastructure.Security;

var source = new EnvironmentMySqlConnectionStringSource();
if (!MySqlConnectionStringPolicy.TryNormalize(source.GetConnectionString(), out var connectionString, out var status))
{
    Console.Error.WriteLine(status);
    return 2;
}

var username = $"p0_smoke_{Guid.NewGuid():N}";
var password = $"Smoke-{Guid.NewGuid():N}!";
long? accountId = null;

try
{
    var repository = new SqlSugarAccountRepository(new SqlSugarClientFactory(source));
    var accounts = new AccountService(repository, new Argon2idPasswordHasher());

    var registration = await accounts.RegisterAsync(username, password, CancellationToken.None);
    if (registration.Status != AccountRegistrationStatus.Success || registration.AccountId is null)
    {
        Console.Error.WriteLine($"Registration smoke failed: {registration.Status}.");
        return 3;
    }

    accountId = registration.AccountId;
    var authentication = await accounts.AuthenticateAsync(username, password, CancellationToken.None);
    var rejected = await accounts.AuthenticateAsync(username, "definitely-wrong", CancellationToken.None);
    if (authentication.Status != AccountAuthenticationStatus.Success ||
        authentication.AccountId != accountId ||
        rejected.Status != AccountAuthenticationStatus.WrongPassword)
    {
        Console.Error.WriteLine("Authentication smoke failed.");
        return 4;
    }

    Console.WriteLine("Account smoke passed: register, correct login and wrong-password rejection.");
    return 0;
}
finally
{
    if (accountId is not null)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cleanup = new MySqlCommand(
            "DELETE FROM accounts WHERE account_id = @accountId AND username = @username",
            connection);
        cleanup.Parameters.AddWithValue("@accountId", accountId.Value);
        cleanup.Parameters.AddWithValue("@username", username);
        await cleanup.ExecuteNonQueryAsync();
        Console.WriteLine("Account smoke fixture removed.");
    }
}
