using System.Collections.Generic;

namespace GameFactory.Steam.Models;

public sealed record SteamLobbyCreateOptions(
    SteamLobbyVisibility Visibility = SteamLobbyVisibility.FriendsOnly,
    int MaxMembers = 4,
    bool IsJoinable = true,
    IReadOnlyDictionary<string, string>? Metadata = null);
