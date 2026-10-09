using System;
using System.Linq;
using Naraka.Infrastructure.Network;
using NUnit.Framework;

namespace Naraka.P0.Tests
{
    public sealed class LegacyClientWireTests
    {
        [Test]
        public void LoginPacketMatchesFrozenExecutableGoldenVector()
        {
            var message = new LegacyMsgLogin
            {
                Account = "fixture-user",
                Password = "fixture-password"
            };

            var packet = LegacyWireCodec.Encode(message, "golden-session-key");

            Assert.That(
                Convert.ToBase64String(packet),
                Is.EqualTo(
                    "OgAAAAgATXNnTG9naW51P9JfoVMuKMwBer/v2AmHsAtwvvVaBnwaersh/c4zudnfh1mg6rj/WhceqD8eLyQ="));
        }

        [Test]
        public void DecoderWaitsForCompleteSecretPacketAndThenDecodes()
        {
            var packet = Convert.FromBase64String(
                "KwAAAAkATXNnU2VjcmV0HYO0QVTThDxsCTYu04va5lQ9RJkXJ+NsC3t4KwVNRlc=");

            Assert.That(
                LegacyWireCodec.TryDecode(
                    packet,
                    packet.Length - 1,
                    "golden-public-key",
                    out _,
                    out _),
                Is.False);

            Assert.That(
                LegacyWireCodec.TryDecode(
                    packet,
                    packet.Length,
                    "golden-public-key",
                    out var decoded,
                    out var consumed),
                Is.True);
            Assert.That(consumed, Is.EqualTo(packet.Length));
            Assert.That(decoded, Is.TypeOf<LegacyMsgSecret>());
            Assert.That(((LegacyMsgSecret)decoded).Secret, Is.EqualTo("golden-session-key"));
        }

        [Test]
        public void ExpeditionProtocolsAreAppendedAfterTheFrozenP1Range()
        {
            Assert.That((int)LegacyProtocolValue.MsgLobbyChatResponse, Is.EqualTo(64));
            Assert.That((int)LegacyProtocolValue.MsgExpeditionRequest, Is.EqualTo(65));
            Assert.That((int)LegacyProtocolValue.MsgExpeditionResponse, Is.EqualTo(66));
        }

        [Test]
        public void ExpeditionRequestContainsNoAccountOrClientGrantedDropFields()
        {
            var propertyNames = typeof(LegacyMsgExpeditionRequest)
                .GetProperties()
                .Select(property => property.Name)
                .ToArray();

            Assert.That(propertyNames, Is.EquivalentTo(new[]
            {
                "ProtocolType", "RequestId", "Operation", "ExpeditionId"
            }));
            Assert.That(propertyNames, Does.Not.Contain("AccountId"));
            Assert.That(propertyNames, Does.Not.Contain("Assets"));
            Assert.That(propertyNames, Does.Not.Contain("Drops"));
        }

        [Test]
        public void ExpeditionResponseRoundTripsThroughTheLegacyEnvelope()
        {
            var response = new LegacyMsgExpeditionResponse
            {
                RequestId = "request-1",
                Operation = LegacyExpeditionOperation.GetActive,
                Status = LegacyLobbyOperationStatus.Success,
                Snapshot = new LegacyExpeditionSnapshot
                {
                    ExpeditionId = "expedition-1",
                    EntryMapId = "map-01",
                    StartedUnixMilliseconds = 1700000000000,
                    State = LegacyExpeditionState.Active,
                    DeathCount = 0
                }
            };
            response.Snapshot.TemporaryAssets.Add(new LegacyExpeditionAsset
            {
                Kind = LegacyExpeditionAssetKind.Item,
                AssetId = "material_wolf_claw",
                Quantity = 2,
                Source = LegacyExpeditionAssetSource.MonsterDrop
            });

            var packet = LegacyWireCodec.Encode(response, "expedition-session-key");

            Assert.That(LegacyWireCodec.TryDecode(
                packet, packet.Length, "expedition-session-key", out var decoded, out var consumed),
                Is.True);
            Assert.That(consumed, Is.EqualTo(packet.Length));
            Assert.That(decoded, Is.TypeOf<LegacyMsgExpeditionResponse>());
            var roundTrip = (LegacyMsgExpeditionResponse)decoded;
            Assert.That(roundTrip.RequestId, Is.EqualTo("request-1"));
            Assert.That(roundTrip.Snapshot.TemporaryAssets, Has.Count.EqualTo(1));
            Assert.That(roundTrip.Snapshot.TemporaryAssets[0].AssetId,
                Is.EqualTo("material_wolf_claw"));
        }
    }
}
