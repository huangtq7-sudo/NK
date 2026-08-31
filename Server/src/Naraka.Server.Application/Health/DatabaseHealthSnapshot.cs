namespace Naraka.Server.Application.Health;

public sealed record DatabaseHealthSnapshot(bool IsReady, string Status);
