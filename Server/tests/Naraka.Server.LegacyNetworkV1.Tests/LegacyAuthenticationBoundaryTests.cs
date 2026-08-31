using Naraka.Server.Application.Sessions;
using Naraka.Server.LegacyNetworkV1.Authentication;
using Naraka.Server.LegacyNetworkV1.Protocol;

namespace Naraka.Server.LegacyNetworkV1.Tests;

public sealed class LegacyAuthenticationBoundaryTests
{
    [Fact]
    public void ResolverUsesSessionAccountAndAcceptsOnlyMatchingLegacyClaim()
    {
        var sessions = new InMemoryAuthenticatedSessionRegistry();
        var connectionId = ConnectionId.New();
        sessions.Bind(connectionId, 42);
        var resolver = new LegacySessionAccountResolver(sessions);

        Assert.Equal(42, resolver.Resolve(connectionId));
        Assert.Equal(42, resolver.Resolve(connectionId, claimedAccountId: 42));
        Assert.Throws<UnauthorizedAccessException>(() => resolver.Resolve(connectionId, claimedAccountId: 99));
    }

    [Fact]
    public void ResolverRejectsUnauthenticatedConnection()
    {
        var resolver = new LegacySessionAccountResolver(new InMemoryAuthenticatedSessionRegistry());

        Assert.Throws<UnauthorizedAccessException>(() => resolver.Resolve(ConnectionId.New(), claimedAccountId: 1));
    }

    [Theory]
    [InlineData("MsgPlayerDataResponse", "MsgLoadPlayerData", 6)]
    [InlineData("MsgInventoryResponse", "MsgLoadInventory", 8)]
    [InlineData("MsgTaskResponse", "MsgLoadTask", 10)]
    [InlineData("MsgLogin", "MsgLogin", 5)]
    public void OutboundResolverUsesFrozenClientWireNames(
        string messageType,
        string expectedWireName,
        int expectedEmbeddedValue)
    {
        var protocol = LegacyOutboundProtocolResolver.Resolve(messageType);

        Assert.Equal(messageType, protocol.MessageTypeName);
        Assert.Equal(expectedWireName, protocol.WireProtocolName);
        Assert.Equal(expectedEmbeddedValue, protocol.EmbeddedProtocolValue);
    }

    [Fact]
    public void OutboundResolverRejectsServerOnlyProtocolUnknownToFrozenClient()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LegacyOutboundProtocolResolver.Resolve("MsgConfigResponse"));
    }
}
