using System.Collections.Generic;

namespace GameFactory.Steam.Models;

public sealed record SteamLobbyInfo(
    SteamLobbyId Id,
    SteamUserId OwnerId,
    int MemberCount,
    int MemberLimit,
    IReadOnlyDictionary<string, string> Metadata);
