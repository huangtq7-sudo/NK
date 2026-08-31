namespace Naraka.Server.Application.Accounts;

public sealed record AccountCredentialRecord(
    long AccountId,
    string Username,
    byte[] PasswordHash,
    byte[] PasswordSalt,
    string PasswordParameters,
    byte Status);
