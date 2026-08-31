namespace Naraka.Server.Application.Accounts;

public sealed record CreateAccountRecord(
    string Username,
    byte[] PasswordHash,
    byte[] PasswordSalt,
    string PasswordParameters);
