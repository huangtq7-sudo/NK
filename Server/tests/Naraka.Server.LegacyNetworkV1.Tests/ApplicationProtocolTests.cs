using Naraka.Server.LegacyNetworkV1.Messages;
using Naraka.Server.LegacyNetworkV1.Protocol;

namespace Naraka.Server.LegacyNetworkV1.Tests;

/// <summary>
/// Guards the ADR-0007 registration rule: P1 numbers start at 19, never collide with the frozen
/// 0-18 audit, and never reuse a frozen protocol name.
/// </summary>
public sealed class ApplicationProtocolTests
{
    [Fact]
    public void ApplicationNumbersStartAfterTheFrozenRange()
    {
        Assert.Equal(19, ApplicationProtocolCatalog.FirstApplicationProtocolValue);
        Assert.All(
            ApplicationProtocolCatalog.All(),
            entry => Assert.True(
                entry.Value >= ApplicationProtocolCatalog.FirstApplicationProtocolValue,
                $"{entry.Key} uses {entry.Value}, which collides with the frozen range."));
    }

    [Fact]
    public void ApplicationNamesAndNumbersNeverCollideWithTheFrozenCatalog()
    {
        var frozenNames = LegacyProtocolCatalog.Client.Keys
            .Concat(LegacyProtocolCatalog.Server.Keys)
            .ToHashSet(StringComparer.Ordinal);
        var frozenNumbers = LegacyProtocolCatalog.Client.Values
            .Concat(LegacyProtocolCatalog.Server.Values)
            .ToHashSet();

        foreach (var entry in ApplicationProtocolCatalog.All())
        {
            Assert.DoesNotContain(entry.Key, frozenNames);
            Assert.DoesNotContain(entry.Value, frozenNumbers);
        }
    }

    [Fact]
    public void ApplicationNumbersAreUniqueAcrossDirections()
    {
        var all = ApplicationProtocolCatalog.All().ToArray();
        Assert.Equal(all.Length, all.Select(entry => entry.Value).Distinct().Count());
        Assert.Equal(all.Length, all.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void RequestAndResponseNamesMatchTheEnumMembers()
    {
        Assert.Equal(
            (int)LegacyProtocolValue.MsgLobbyAccountSummaryRequest,
            ApplicationProtocolCatalog.Inbound[ApplicationProtocolCatalog.LobbyAccountSummaryRequest]);
        Assert.Equal(
            (int)LegacyProtocolValue.MsgLobbyAccountSummaryResponse,
            ApplicationProtocolCatalog.Outbound[ApplicationProtocolCatalog.LobbyAccountSummaryResponse]);
        Assert.Equal(
            ApplicationProtocolCatalog.LobbyAccountSummaryRequest,
            LegacyProtocolValue.MsgLobbyAccountSummaryRequest.ToString());
        Assert.Equal(
            ApplicationProtocolCatalog.LobbyAccountSummaryResponse,
            LegacyProtocolValue.MsgLobbyAccountSummaryResponse.ToString());
    }

    [Fact]
    public void RequestSurvivesSerializationRoundTrip()
    {
        var request = new LegacyMsgLobbyAccountSummaryRequest { RequestId = "req-0001" };

        var payload = LegacyProtobufCodec.Serialize(request);
        var decoded = Assert.IsType<LegacyMsgLobbyAccountSummaryRequest>(
            LegacyProtobufCodec.DeserializeIncoming(
                ApplicationProtocolCatalog.LobbyAccountSummaryRequest,
                payload));

        Assert.Equal("req-0001", decoded.RequestId);
        Assert.Equal(LegacyProtocolValue.MsgLobbyAccountSummaryRequest, decoded.ProtocolType);
    }

    [Fact]
    public void ResponseSurvivesSerializationRoundTrip()
    {
        var response = new LegacyMsgLobbyAccountSummaryResponse
        {
            RequestId = "req-0002",
            Status = LegacyLobbyAccountSummaryStatus.Success,
            AccountLevel = 9,
            Copper = 12345678901,
            Silk = 4242,
            Gold = 17
        };

        var payload = LegacyProtobufCodec.Serialize(response);
        var decoded = Assert.IsType<LegacyMsgLobbyAccountSummaryResponse>(
            LegacyProtobufCodec.DeserializeOutgoing(
                ApplicationProtocolCatalog.LobbyAccountSummaryResponse,
                payload));

        Assert.Equal("req-0002", decoded.RequestId);
        Assert.Equal(LegacyLobbyAccountSummaryStatus.Success, decoded.Status);
        Assert.Equal(9, decoded.AccountLevel);
        Assert.Equal(12345678901, decoded.Copper);
        Assert.Equal(4242, decoded.Silk);
        Assert.Equal(17, decoded.Gold);
    }

    [Fact]
    public void ResponseRoutesThroughTheOutboundResolverWithoutTouchingTheFrozenCatalog()
    {
        var outbound = LegacyOutboundProtocolResolver.Resolve(
            ApplicationProtocolCatalog.LobbyAccountSummaryResponse);

        Assert.Equal(ApplicationProtocolCatalog.LobbyAccountSummaryResponse, outbound.WireProtocolName);
        Assert.Equal(20, outbound.EmbeddedProtocolValue);
        Assert.False(LegacyProtocolCatalog.Client.ContainsKey(outbound.WireProtocolName));
    }

    [Fact]
    public void ServerRefusesToTreatAResponseAsAnInboundRequest()
    {
        var payload = LegacyProtobufCodec.Serialize(new LegacyMsgLobbyAccountSummaryResponse());

        Assert.Throws<InvalidDataException>(() => LegacyProtobufCodec.DeserializeIncoming(
            ApplicationProtocolCatalog.LobbyAccountSummaryResponse,
            payload));
    }
}
