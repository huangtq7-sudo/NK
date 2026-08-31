using ProtoBuf;

namespace Naraka.Server.LegacyNetworkV1.Messages;

public abstract class LegacyMessage
{
    public abstract LegacyProtocolValue ProtocolType { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgSecret : LegacyMessage
{
    public LegacyMsgSecret() => ProtocolType = LegacyProtocolValue.MsgSecret;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)]
    public string? Secret { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgPing : LegacyMessage
{
    public LegacyMsgPing() => ProtocolType = LegacyProtocolValue.MsgPing;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgRegister : LegacyMessage
{
    public LegacyMsgRegister() => ProtocolType = LegacyProtocolValue.MsgRegister;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)]
    public string? Account { get; set; }

    [ProtoMember(3)]
    public string? Password { get; set; }

    [ProtoMember(4)]
    public LegacyRegisterResult Result { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLogin : LegacyMessage
{
    public LegacyMsgLogin() => ProtocolType = LegacyProtocolValue.MsgLogin;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)]
    public string? Account { get; set; }

    [ProtoMember(3)]
    public string? Password { get; set; }

    [ProtoMember(4)]
    public LegacyLoginResult Result { get; set; }

    [ProtoMember(5)]
    public int AccountId { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgPlayerDataResponse : LegacyMessage
{
    public LegacyMsgPlayerDataResponse() => ProtocolType = LegacyProtocolValue.MsgLoadPlayerData;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public int Result { get; set; }
    [ProtoMember(3)] public int Gold { get; set; }
    [ProtoMember(4)] public int MaxHealth { get; set; }
    [ProtoMember(5)] public int CurrentHealth { get; set; }
    [ProtoMember(6)] public int Attack { get; set; }
    [ProtoMember(7)] public int Defense { get; set; }
    [ProtoMember(8)] public int CurrentWeaponId { get; set; }
    [ProtoMember(9)] public int SelectedHeroId { get; set; }
}
