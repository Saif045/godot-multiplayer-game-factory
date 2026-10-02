namespace GameFactory.Steam.Models;

public sealed record SteamPresence(
    string State,
    string? ConnectString = null,
    string? GameFactoryProtocol = null);
