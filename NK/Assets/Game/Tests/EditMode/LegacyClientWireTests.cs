using System;
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
    }
}
