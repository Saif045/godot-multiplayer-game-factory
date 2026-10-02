using System;
using GameFactory.Steam.Models;

namespace GameFactory.Shell;

/// <summary>One-shot application-flow instruction for the next online gameplay scene.</summary>
public readonly record struct OnlineGameplayLaunchIntent
{
    public OnlineGameplayLaunchKind Kind { get; }
    public SteamLobbyId? LobbyId { get; }

    private OnlineGameplayLaunchIntent(OnlineGameplayLaunchKind kind, SteamLobbyId? lobbyId)
    {
        Kind = kind;
        LobbyId = lobbyId;
    }

    public static OnlineGameplayLaunchIntent Host() => new(OnlineGameplayLaunchKind.Host, null);

    public static OnlineGameplayLaunchIntent Join(SteamLobbyId lobbyId) =>
        new(OnlineGameplayLaunchKind.Join, lobbyId);

    public SteamLobbyId RequireLobbyId() =>
        Kind == OnlineGameplayLaunchKind.Join && LobbyId is SteamLobbyId lobbyId
            ? lobbyId
            : throw new InvalidOperationException("Only a Join launch intent has a Steam lobby ID.");
}

public enum OnlineGameplayLaunchKind
{
    Host,
    Join
}
