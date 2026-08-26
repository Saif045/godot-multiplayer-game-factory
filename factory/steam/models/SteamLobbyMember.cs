using System.Collections.Generic;

namespace GameFactory.Steam.Models;

public sealed record SteamLobbyMember(SteamUser User, IReadOnlyDictionary<string, string> Metadata);
