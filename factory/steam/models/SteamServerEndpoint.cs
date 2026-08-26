namespace GameFactory.Steam.Models;

public sealed record SteamServerEndpoint(string Host, int Port, SteamUserId? SteamId = null);
