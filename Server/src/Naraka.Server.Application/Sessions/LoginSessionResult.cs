using Naraka.Server.Application.Accounts;

namespace Naraka.Server.Application.Sessions;

public sealed record LoginSessionResult(
    AccountAuthenticationStatus Status,
    AuthenticatedSession? Session = null);
