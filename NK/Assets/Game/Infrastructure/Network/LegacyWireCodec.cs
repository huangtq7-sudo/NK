using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ProtoBuf;

namespace Naraka.Infrastructure.Network
{
    internal static class LegacyWireCodec
    {
        public const int HeaderLength = sizeof(int);
        public const int MaximumPayloadLength = 16 * 1024 * 1024;

        private static readonly byte[] InitializationVector =
            Convert.FromBase64String("Rkb4jvUy/ye7Cd7k89QQgQ==");

        private static readonly byte[] Salt =
            Convert.FromBase64String("gsf4jvkyhye5/d7k8OrLgM==");

        public static byte[] Encode(LegacyMessage message, string passphrase)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            var protocolName = message.ProtocolType.ToString();
            var name = Encoding.UTF8.GetBytes(protocolName);
            if (name.Length > ushort.MaxValue)
            {
                throw new InvalidDataException("Legacy protocol name is too long.");
            }

            byte[] protobuf;
            using (var stream = new MemoryStream())
            {
                Serializer.NonGeneric.Serialize(stream, message);
                protobuf = stream.ToArray();
            }

            var ciphertext = Encrypt(protobuf, passphrase);
            var payloadLength = checked(sizeof(ushort) + name.Length + ciphertext.Length);
            if (payloadLength > MaximumPayloadLength)
            {
                throw new InvalidDataException("Legacy payload exceeds the safety limit.");
            }

            var packet = new byte[HeaderLength + payloadLength];
            WriteInt32LittleEndian(packet, 0, payloadLength);
            WriteUInt16LittleEndian(packet, HeaderLength, (ushort)name.Length);
            Buffer.BlockCopy(name, 0, packet, HeaderLength + sizeof(ushort), name.Length);
            Buffer.BlockCopy(
                ciphertext,
                0,
                packet,
                HeaderLength + sizeof(ushort) + name.Length,
                ciphertext.Length);
            return packet;
        }

        public static bool TryDecode(
            byte[] buffer,
            int count,
            string passphrase,
            out LegacyMessage message,
            out int consumed)
        {
            message = null;
            consumed = 0;
            if (count < HeaderLength)
            {
                return false;
            }

            var payloadLength = ReadInt32LittleEndian(buffer, 0);
            if (payloadLength < sizeof(ushort) || payloadLength > MaximumPayloadLength)
            {
                throw new InvalidDataException("Invalid legacy payload length.");
            }

            var packetLength = checked(HeaderLength + payloadLength);
            if (count < packetLength)
            {
                return false;
            }

            var nameLength = ReadUInt16LittleEndian(buffer, HeaderLength);
            if (nameLength == 0 || sizeof(ushort) + nameLength >= payloadLength)
            {
                throw new InvalidDataException("Invalid legacy protocol name length.");
            }

            var nameOffset = HeaderLength + sizeof(ushort);
            var protocolName = Encoding.UTF8.GetString(buffer, nameOffset, nameLength);
            var bodyOffset = nameOffset + nameLength;
            var bodyLength = packetLength - bodyOffset;
            var ciphertext = new byte[bodyLength];
            Buffer.BlockCopy(buffer, bodyOffset, ciphertext, 0, bodyLength);
            var plaintext = Decrypt(ciphertext, passphrase);

            var type = ResolveIncomingType(protocolName);
            using (var stream = new MemoryStream(plaintext, false))
            {
                message = (LegacyMessage)Serializer.NonGeneric.Deserialize(type, stream);
            }

            if (message == null || message.ProtocolType.ToString() != protocolName)
            {
                throw new InvalidDataException("Legacy protocol envelope mismatch.");
            }

            consumed = packetLength;
            return true;
        }

        private static Type ResolveIncomingType(string protocolName)
        {
            switch (protocolName)
            {
                case "MsgSecret": return typeof(LegacyMsgSecret);
                case "MsgPing": return typeof(LegacyMsgPing);
                case "MsgRegister": return typeof(LegacyMsgRegister);
                case "MsgLogin": return typeof(LegacyMsgLogin);
                default: throw new InvalidDataException("Unsupported legacy inbound protocol: " + protocolName + ".");
            }
        }

        private static byte[] Encrypt(byte[] plaintext, string passphrase)
        {
            if (plaintext == null || plaintext.Length == 0)
            {
                throw new ArgumentException("Legacy plaintext cannot be empty.", nameof(plaintext));
            }

            using (var algorithm = CreateAlgorithm(passphrase))
            using (var transform = algorithm.CreateEncryptor())
            {
                return transform.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }
        }

        private static byte[] Decrypt(byte[] ciphertext, string passphrase)
        {
            if (ciphertext == null || ciphertext.Length == 0)
            {
                throw new ArgumentException("Legacy ciphertext cannot be empty.", nameof(ciphertext));
            }

            using (var algorithm = CreateAlgorithm(passphrase))
            using (var transform = algorithm.CreateDecryptor())
            {
                return transform.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            }
        }

        private static SymmetricAlgorithm CreateAlgorithm(string passphrase)
        {
            if (string.IsNullOrWhiteSpace(passphrase))
            {
                throw new ArgumentException("Legacy passphrase cannot be empty.", nameof(passphrase));
            }

#pragma warning disable 618
            var derivation = new PasswordDeriveBytes(passphrase, Salt);
#pragma warning restore 618
            var algorithm = Rijndael.Create();
            algorithm.KeySize = 256;
            algorithm.BlockSize = 128;
            algorithm.Mode = CipherMode.CBC;
            algorithm.Padding = PaddingMode.PKCS7;
            algorithm.Key = derivation.GetBytes(32);
            algorithm.IV = InitializationVector;
            derivation.Dispose();
            return algorithm;
        }

        private static int ReadInt32LittleEndian(byte[] bytes, int offset) =>
            bytes[offset] |
            bytes[offset + 1] << 8 |
            bytes[offset + 2] << 16 |
            bytes[offset + 3] << 24;

        private static ushort ReadUInt16LittleEndian(byte[] bytes, int offset) =>
            (ushort)(bytes[offset] | bytes[offset + 1] << 8);

        private static void WriteInt32LittleEndian(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt16LittleEndian(byte[] bytes, int offset, ushort value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
        }
    }
}
