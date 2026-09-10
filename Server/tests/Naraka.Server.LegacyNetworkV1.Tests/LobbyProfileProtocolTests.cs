using Naraka.Server.Application.Progression;
using Naraka.Server.LegacyNetworkV1.Messages;
using Naraka.Server.LegacyNetworkV1.Protocol;

namespace Naraka.Server.LegacyNetworkV1.Tests;

/// <summary>
/// P1.1-B 协议 21-24 的登记、序列化与路由。
/// </summary>
public sealed class LobbyProfileProtocolTests
{
    [Fact]
    public void ProfileProtocolsAreRegisteredWithTheNumbersTheEnumDeclares()
    {
        Assert.Equal(21, ApplicationProtocolCatalog.Inbound[ApplicationProtocolCatalog.LobbyProfileRequest]);
        Assert.Equal(22, ApplicationProtocolCatalog.Outbound[ApplicationProtocolCatalog.LobbyProfileResponse]);
        Assert.Equal(23, ApplicationProtocolCatalog.Inbound[ApplicationProtocolCatalog.LobbySetAppearanceRequest]);
        Assert.Equal(24, ApplicationProtocolCatalog.Outbound[ApplicationProtocolCatalog.LobbySetAppearanceResponse]);

        Assert.Equal(
            (int)LegacyProtocolValue.MsgLobbyProfileRequest,
            ApplicationProtocolCatalog.Inbound[ApplicationProtocolCatalog.LobbyProfileRequest]);
        Assert.Equal(
            ApplicationProtocolCatalog.LobbySetAppearanceResponse,
            LegacyProtocolValue.MsgLobbySetAppearanceResponse.ToString());
    }

    [Fact]
    public void ProfileResponseSurvivesSerializationRoundTrip()
    {
        var response = new LegacyMsgLobbyProfileResponse
        {
            RequestId = "req-profile",
            Status = LegacyLobbyOperationStatus.Success,
            AvatarId = "avatar_07",
            AvatarFrameId = "frame_gold",
            SelectedHeroId = "hero_pei_xingzhou",
            SelectedWeaponId = "weapon_tachi",
            SelectedPetId = "pet_lingyu",
            AccountXp = 1234,
            AccountLevel = 6,
            InventoryTier = 2,
            Copper = 1000,
            Silk = 2000,
            Gold = 3000
        };

        var payload = LegacyProtobufCodec.Serialize(response);
        var decoded = Assert.IsType<LegacyMsgLobbyProfileResponse>(
            LegacyProtobufCodec.DeserializeOutgoing(ApplicationProtocolCatalog.LobbyProfileResponse, payload));

        Assert.Equal("avatar_07", decoded.AvatarId);
        Assert.Equal("frame_gold", decoded.AvatarFrameId);
        Assert.Equal("hero_pei_xingzhou", decoded.SelectedHeroId);
        Assert.Equal("weapon_tachi", decoded.SelectedWeaponId);
        Assert.Equal("pet_lingyu", decoded.SelectedPetId);
        Assert.Equal(1234, decoded.AccountXp);
        Assert.Equal(6, decoded.AccountLevel);
        Assert.Equal(2, decoded.InventoryTier);
        Assert.Equal(3000, decoded.Gold);
    }

    [Fact]
    public void AppearanceRequestSurvivesSerializationRoundTrip()
    {
        var request = new LegacyMsgLobbySetAppearanceRequest
        {
            RequestId = "req-appearance",
            AvatarId = "avatar_12",
            AvatarFrameId = "frame_purple"
        };

        var payload = LegacyProtobufCodec.Serialize(request);
        var decoded = Assert.IsType<LegacyMsgLobbySetAppearanceRequest>(
            LegacyProtobufCodec.DeserializeIncoming(
                ApplicationProtocolCatalog.LobbySetAppearanceRequest, payload));

        Assert.Equal("req-appearance", decoded.RequestId);
        Assert.Equal("avatar_12", decoded.AvatarId);
        Assert.Equal("frame_purple", decoded.AvatarFrameId);
    }

    [Fact]
    public void ProfileRequestCarriesNoAccountId()
    {
        // 请求体里根本没有 AccountId 属性，客户端因此无法选择读取谁的资料。
        var properties = typeof(LegacyMsgLobbyProfileRequest).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain("AccountId", properties);
        Assert.Contains("RequestId", properties);
    }

    [Fact]
    public void ResponsesRouteThroughTheApplicationCatalogOnly()
    {
        foreach (var name in new[]
                 {
                     ApplicationProtocolCatalog.LobbyProfileResponse,
                     ApplicationProtocolCatalog.LobbySetAppearanceResponse
                 })
        {
            var outbound = LegacyOutboundProtocolResolver.Resolve(name);
            Assert.Equal(name, outbound.WireProtocolName);
            Assert.False(LegacyProtocolCatalog.Client.ContainsKey(name));
            Assert.False(LegacyProtocolCatalog.Server.ContainsKey(name));
        }
    }

    [Fact]
    public void ResponsesCannotBeSubmittedAsInboundRequests()
    {
        var payload = LegacyProtobufCodec.Serialize(new LegacyMsgLobbyProfileResponse());

        Assert.Throws<InvalidDataException>(() => LegacyProtobufCodec.DeserializeIncoming(
            ApplicationProtocolCatalog.LobbyProfileResponse, payload));
    }

    [Fact]
    public void WireStatusMirrorsTheApplicationStatusValueForValue()
    {
        foreach (var status in Enum.GetValues<LobbyOperationStatus>())
        {
            var wire = (LegacyLobbyOperationStatus)(int)status;
            Assert.Equal(status.ToString(), wire.ToString());
        }

        Assert.Equal(
            Enum.GetValues<LobbyOperationStatus>().Length,
            Enum.GetValues<LegacyLobbyOperationStatus>().Length);
    }
}

/// <summary>P1.2 协议 25/26 的登记、序列化与校验路径。</summary>
public sealed class LobbySetLoadoutProtocolTests
{
    [Fact]
    public void LoadoutProtocolsAreRegisteredWithTheNumbersTheEnumDeclares()
    {
        Assert.Equal(25, ApplicationProtocolCatalog.Inbound[ApplicationProtocolCatalog.LobbySetLoadoutRequest]);
        Assert.Equal(26, ApplicationProtocolCatalog.Outbound[ApplicationProtocolCatalog.LobbySetLoadoutResponse]);
        Assert.Equal(
            ApplicationProtocolCatalog.LobbySetLoadoutRequest,
            LegacyProtocolValue.MsgLobbySetLoadoutRequest.ToString());
    }

    [Fact]
    public void RequestSurvivesSerializationRoundTrip()
    {
        var request = new LegacyMsgLobbySetLoadoutRequest
        {
            RequestId = "req-loadout",
            HeroId = "hero_pei_xingzhou",
            WeaponId = "weapon_tachi",
            PetId = "pet_lingyu"
        };

        var payload = LegacyProtobufCodec.Serialize(request);
        var decoded = Assert.IsType<LegacyMsgLobbySetLoadoutRequest>(
            LegacyProtobufCodec.DeserializeIncoming(
                ApplicationProtocolCatalog.LobbySetLoadoutRequest, payload));

        Assert.Equal("hero_pei_xingzhou", decoded.HeroId);
        Assert.Equal("weapon_tachi", decoded.WeaponId);
        Assert.Equal("pet_lingyu", decoded.PetId);
        Assert.DoesNotContain(
            "AccountId",
            typeof(LegacyMsgLobbySetLoadoutRequest).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public void ResponseRoutesThroughTheApplicationCatalogOnly()
    {
        var outbound = LegacyOutboundProtocolResolver.Resolve(
            ApplicationProtocolCatalog.LobbySetLoadoutResponse);

        Assert.Equal(26, outbound.EmbeddedProtocolValue);
        Assert.False(LegacyProtocolCatalog.Server.ContainsKey(outbound.WireProtocolName));
    }
}

/// <summary>
/// 出战选择的服务端校验。P1 只有默认宠物是拥有状态，
/// 破锋要等到 P3 击败赤霄炎龙才解锁，因此现在选它必须被拒绝。
/// </summary>
public sealed class LoadoutValidationTests
{
    [Fact]
    public async Task LockedPetIsRejectedAsNotAvailable()
    {
        var repository = new MemoryAccountProfileRepository();
        var service = TestAccountProfiles.Service(repository);
        await service.EnsureProvisionedAsync(42, CancellationToken.None);
        var locked = TestAccountProfiles.Config.PetsInDisplayOrder.First(pet => !pet.DefaultOwned);

        var result = await service.SetLoadoutAsync(
            42,
            TestAccountProfiles.Config.DefaultHeroId,
            TestAccountProfiles.Config.DefaultWeaponId,
            locked.PetId,
            CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.NotAvailable, result.Status);
    }

    [Theory]
    [InlineData("hero_does_not_exist", "weapon_longsword", "pet_lingyu")]
    [InlineData("hero_gu_chenyue", "weapon_does_not_exist", "pet_lingyu")]
    [InlineData("hero_gu_chenyue", "weapon_longsword", "pet_does_not_exist")]
    [InlineData("", "", "")]
    public async Task UnknownIdsAreRejected(string heroId, string weaponId, string petId)
    {
        var repository = new MemoryAccountProfileRepository();
        var service = TestAccountProfiles.Service(repository);
        await service.EnsureProvisionedAsync(42, CancellationToken.None);

        var result = await service.SetLoadoutAsync(42, heroId, weaponId, petId, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task ValidLoadoutIsPersistedAndReadBack()
    {
        var repository = new MemoryAccountProfileRepository();
        var service = TestAccountProfiles.Service(repository);
        await service.EnsureProvisionedAsync(42, CancellationToken.None);
        var hero = TestAccountProfiles.Config.HeroesInDisplayOrder[1];
        var weapon = TestAccountProfiles.Config.WeaponsInDisplayOrder[1];

        var applied = await service.SetLoadoutAsync(
            42, hero.HeroId, weapon.WeaponId, TestAccountProfiles.Config.DefaultPetId, CancellationToken.None);
        var reread = await service.GetProfileAsync(42, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, applied.Status);
        Assert.Equal(hero.HeroId, reread.View!.SelectedHeroId);
        Assert.Equal(weapon.WeaponId, reread.View.SelectedWeaponId);
    }
}
