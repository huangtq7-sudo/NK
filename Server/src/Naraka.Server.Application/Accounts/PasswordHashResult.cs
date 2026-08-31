namespace Naraka.Server.Application.Accounts;

public sealed record PasswordHashResult(byte[] Hash, byte[] Salt, string Parameters);
