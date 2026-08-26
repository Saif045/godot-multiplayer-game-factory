using System.Collections.Generic;

namespace GameFactory.Steam.Models;

public sealed record SteamLobby(
    SteamLobbyId Id,
    SteamUserId OwnerId,
    SteamLobbyVisibility Visibility,
    bool IsJoinable,
    int MemberLimit,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<SteamLobbyMember> Members);
