namespace GameFactory.Steam.Models;

/// <summary>Reserved for a future Steam GameServer implementation; not supported by the first adapter.</summary>
public sealed record SteamDedicatedServerOptions(string ProductName, int GamePort = 0, int QueryPort = 0);
