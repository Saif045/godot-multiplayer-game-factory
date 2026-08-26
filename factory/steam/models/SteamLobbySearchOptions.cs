using System.Collections.Generic;

namespace GameFactory.Steam.Models;

public sealed record SteamLobbySearchOptions(
    IReadOnlyDictionary<string, string>? Metadata = null,
    int MaxResults = 50);
