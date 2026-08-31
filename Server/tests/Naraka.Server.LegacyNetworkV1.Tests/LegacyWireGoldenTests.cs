using System.Text.Json;
using Naraka.Server.LegacyNetworkV1.Protocol;

namespace Naraka.Server.LegacyNetworkV1.Tests;

public sealed class LegacyWireGoldenTests
{
    private static readonly GoldenContract Contract = LoadContract();

    public static TheoryData<GoldenCase> GoldenCases =>
        new(Contract.Cases.ToArray());

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void AesAndFrameEncodingMatchFrozenExecutable(GoldenCase golden)
    {
        var plaintext = Convert.FromHexString(golden.ProtobufHex);
        var expectedCiphertext = Convert.FromBase64String(golden.CipherBase64);
        var expectedPacket = Convert.FromBase64String(golden.PacketBase64);
        var key = golden.KeyKind switch
        {
            "public" => Contract.FixturePublicKey,
            "session" => Contract.FixtureSessionKey,
            _ => throw new InvalidDataException($"Unknown fixture key kind: {golden.KeyKind}.")
        };

        var ciphertext = LegacyAesCodec.Encrypt(plaintext, key);
        Assert.Equal(expectedCiphertext, ciphertext);
        Assert.Equal(plaintext, LegacyAesCodec.Decrypt(ciphertext, key));

        var packet = LegacyFrameCodec.Encode(golden.ProtocolName, ciphertext);
        Assert.Equal(expectedPacket, packet);
        Assert.True(LegacyFrameCodec.TryDecode(packet, out var decoded, out var consumed));
        Assert.NotNull(decoded);
        Assert.Equal(packet.Length, consumed);
        Assert.Equal(golden.ProtocolName, decoded.ProtocolName);
        Assert.Equal(ciphertext, decoded.EncryptedBody);
    }

    [Fact]
    public void DecoderWaitsForACompletePacket()
    {
        var packet = Convert.FromBase64String(Contract.Cases.Single(item => item.Name == "ping").PacketBase64);

        Assert.False(LegacyFrameCodec.TryDecode(packet.AsSpan(0, packet.Length - 1), out var frame, out var consumed));
        Assert.Null(frame);
        Assert.Equal(0, consumed);
    }

    [Fact]
    public void CatalogPreservesObservedClientServerResponseDrift()
    {
        Assert.Equal(12, LegacyProtocolCatalog.Client.Count);
        Assert.Equal(19, LegacyProtocolCatalog.Server.Count);
        Assert.False(LegacyProtocolCatalog.Client.ContainsKey("MsgPlayerDataResponse"));
        Assert.Equal(12, LegacyProtocolCatalog.Server["MsgPlayerDataResponse"]);
        Assert.Equal("MsgLoadPlayerData", LegacyProtocolCatalog.ClientResponseAliases["MsgPlayerDataResponse"]);
        Assert.Equal("MsgLoadInventory", LegacyProtocolCatalog.ClientResponseAliases["MsgInventoryResponse"]);
        Assert.Equal("MsgLoadTask", LegacyProtocolCatalog.ClientResponseAliases["MsgTaskResponse"]);
    }

    private static GoldenContract LoadContract()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Golden", "legacy-wire-v1.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<GoldenContract>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("Legacy golden contract is empty.");
    }

    public sealed record GoldenContract(
        int SchemaVersion,
        string FixturePublicKey,
        string FixtureSessionKey,
        IReadOnlyList<GoldenCase> Cases);

    public sealed record GoldenCase(
        string Name,
        string ProtocolName,
        string KeyKind,
        string ProtobufHex,
        string CipherBase64,
        string PacketBase64);
}
