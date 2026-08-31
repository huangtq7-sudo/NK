using System.Buffers.Binary;
using System.Text;

namespace Naraka.Server.LegacyNetworkV1.Protocol;

/// <summary>
/// Frozen V1 wire frame: Int32LE payload length, UInt16LE UTF-8 protocol-name length,
/// protocol name, then AES-encrypted protobuf-net payload.
/// </summary>
public static class LegacyFrameCodec
{
    public const int HeaderLength = sizeof(int);
    public const int NameLengthPrefixSize = sizeof(ushort);
    public const int MaximumPayloadLength = 16 * 1024 * 1024;

    public static byte[] Encode(string protocolName, ReadOnlySpan<byte> encryptedBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        if (encryptedBody.IsEmpty)
        {
            throw new ArgumentException("Legacy encrypted body must not be empty.", nameof(encryptedBody));
        }

        var nameBytes = Encoding.UTF8.GetBytes(protocolName);
        if (nameBytes.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(protocolName), "Protocol name exceeds the V1 UInt16 limit.");
        }

        var payloadLength = checked(NameLengthPrefixSize + nameBytes.Length + encryptedBody.Length);
        if (payloadLength > MaximumPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(encryptedBody), "Legacy frame exceeds the adapter safety limit.");
        }

        var packet = new byte[HeaderLength + payloadLength];
        BinaryPrimitives.WriteInt32LittleEndian(packet, payloadLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(HeaderLength), checked((ushort)nameBytes.Length));
        nameBytes.CopyTo(packet.AsSpan(HeaderLength + NameLengthPrefixSize));
        encryptedBody.CopyTo(packet.AsSpan(HeaderLength + NameLengthPrefixSize + nameBytes.Length));
        return packet;
    }

    public static bool TryDecode(ReadOnlySpan<byte> buffer, out LegacyFrame? frame, out int consumed)
    {
        frame = null;
        consumed = 0;
        if (buffer.Length < HeaderLength)
        {
            return false;
        }

        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(buffer);
        if (payloadLength < NameLengthPrefixSize || payloadLength > MaximumPayloadLength)
        {
            throw new InvalidDataException($"Invalid legacy payload length: {payloadLength}.");
        }

        var packetLength = checked(HeaderLength + payloadLength);
        if (buffer.Length < packetLength)
        {
            return false;
        }

        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer[HeaderLength..]);
        if (nameLength == 0 || NameLengthPrefixSize + nameLength >= payloadLength)
        {
            throw new InvalidDataException($"Invalid legacy protocol-name length: {nameLength}.");
        }

        var nameStart = HeaderLength + NameLengthPrefixSize;
        var protocolName = Encoding.UTF8.GetString(buffer.Slice(nameStart, nameLength));
        var bodyStart = nameStart + nameLength;
        var bodyLength = packetLength - bodyStart;
        frame = new LegacyFrame(protocolName, buffer.Slice(bodyStart, bodyLength).ToArray());
        consumed = packetLength;
        return true;
    }
}
