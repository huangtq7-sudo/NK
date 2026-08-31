using ProtoBuf;

namespace Naraka.Infrastructure.Network
{
    internal enum LegacyProtocolValue
    {
        None = 0,
        MsgSecret = 1,
        MsgPing = 2,
        MsgTest = 3,
        MsgRegister = 4,
        MsgLogin = 5,
        MsgLoadPlayerData = 6,
        MsgSavePlayerData = 7,
        MsgLoadInventory = 8,
        MsgSaveInventory = 9,
        MsgLoadTask = 10,
        MsgSaveTask = 11
    }

    internal enum LegacyRegisterResult
    {
        Success,
        Failed,
        AlreadyExist,
        WrongCode,
        Forbidden
    }

    internal enum LegacyLoginResult
    {
        Success,
        Failed,
        WrongPwd,
        UserNotExist,
        TimeoutToken
    }

    internal abstract class LegacyMessage
    {
        public abstract LegacyProtocolValue ProtocolType { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgSecret : LegacyMessage
    {
        public LegacyMsgSecret()
        {
            ProtocolType = LegacyProtocolValue.MsgSecret;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)]
        public string Secret { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgPing : LegacyMessage
    {
        public LegacyMsgPing()
        {
            ProtocolType = LegacyProtocolValue.MsgPing;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgRegister : LegacyMessage
    {
        public LegacyMsgRegister()
        {
            ProtocolType = LegacyProtocolValue.MsgRegister;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)]
        public string Account { get; set; }

        [ProtoMember(3)]
        public string Password { get; set; }

        [ProtoMember(4)]
        public LegacyRegisterResult Result { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLogin : LegacyMessage
    {
        public LegacyMsgLogin()
        {
            ProtocolType = LegacyProtocolValue.MsgLogin;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)]
        public string Account { get; set; }

        [ProtoMember(3)]
        public string Password { get; set; }

        [ProtoMember(4)]
        public LegacyLoginResult Result { get; set; }

        [ProtoMember(5)]
        public int AccountId { get; set; }
    }
}
