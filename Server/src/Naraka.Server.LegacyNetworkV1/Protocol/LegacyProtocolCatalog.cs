using System.Collections.ObjectModel;

namespace Naraka.Server.LegacyNetworkV1.Protocol;

/// <summary>
/// Protocol numbers observed in the audited legacy source. Names, not numeric values, are transmitted on the wire.
/// Separate catalogs intentionally preserve the client/server drift found during the P0 audit.
/// </summary>
public static class LegacyProtocolCatalog
{
    public static IReadOnlyDictionary<string, int> Client { get; } = CreateClient();

    public static IReadOnlyDictionary<string, int> Server { get; } = CreateServer();

    public static IReadOnlyDictionary<string, string> ClientResponseAliases { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MsgPlayerDataResponse"] = "MsgLoadPlayerData",
            ["MsgInventoryResponse"] = "MsgLoadInventory",
            ["MsgTaskResponse"] = "MsgLoadTask"
        });

    private static IReadOnlyDictionary<string, int> CreateClient() =>
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["None"] = 0,
            ["MsgSecret"] = 1,
            ["MsgPing"] = 2,
            ["MsgTest"] = 3,
            ["MsgRegister"] = 4,
            ["MsgLogin"] = 5,
            ["MsgLoadPlayerData"] = 6,
            ["MsgSavePlayerData"] = 7,
            ["MsgLoadInventory"] = 8,
            ["MsgSaveInventory"] = 9,
            ["MsgLoadTask"] = 10,
            ["MsgSaveTask"] = 11
        });

    private static IReadOnlyDictionary<string, int> CreateServer() =>
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["None"] = 0,
            ["MsgSecret"] = 1,
            ["MsgPing"] = 2,
            ["MsgTest"] = 3,
            ["MsgRegister"] = 4,
            ["MsgLogin"] = 5,
            ["MsgLoadPlayerData"] = 6,
            ["MsgSavePlayerData"] = 7,
            ["MsgLoadInventory"] = 8,
            ["MsgSaveInventory"] = 9,
            ["MsgLoadTask"] = 10,
            ["MsgSaveTask"] = 11,
            ["MsgPlayerDataResponse"] = 12,
            ["MsgInventoryResponse"] = 13,
            ["MsgTaskResponse"] = 14,
            ["MsgLoadConfig"] = 15,
            ["MsgConfigResponse"] = 16,
            ["MsgShopPurchase"] = 17,
            ["MsgTaskReward"] = 18
        });
}
