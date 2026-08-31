namespace Naraka.Server.Application.Modules;

public static class ModuleCatalog
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "Gateway/Session",
        "Player",
        "Inventory",
        "Economy",
        "Quest",
        "Expedition",
        "Gacha",
        "Config",
        "Presence",
    ];
}
