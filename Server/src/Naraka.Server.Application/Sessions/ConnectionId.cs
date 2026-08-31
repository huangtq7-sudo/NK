namespace Naraka.Server.Application.Sessions;

public readonly record struct ConnectionId(Guid Value)
{
    public static ConnectionId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}
