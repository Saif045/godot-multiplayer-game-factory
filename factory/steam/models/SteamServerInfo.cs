namespace GameFactory.Steam.Models;

public sealed record SteamServerInfo(SteamServerEndpoint Endpoint, string Name, int PlayerCount, int MaxPlayers);
