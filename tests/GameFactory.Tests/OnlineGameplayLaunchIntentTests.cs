using System;
using GameFactory.Shell;
using GameFactory.Steam.Models;

namespace GameFactory.Tests;

public sealed class OnlineGameplayLaunchIntentTests
{
    [Fact]
    public void Host_has_no_lobby_id()
    {
        OnlineGameplayLaunchIntent intent = OnlineGameplayLaunchIntent.Host();

        Assert.Equal(OnlineGameplayLaunchKind.Host, intent.Kind);
        Assert.Null(intent.LobbyId);
        Assert.Throws<InvalidOperationException>(() => intent.RequireLobbyId());
    }

    [Fact]
    public void Join_preserves_the_requested_lobby_id()
    {
        SteamLobbyId lobbyId = new(76561198000000001);
        OnlineGameplayLaunchIntent intent = OnlineGameplayLaunchIntent.Join(lobbyId);

        Assert.Equal(OnlineGameplayLaunchKind.Join, intent.Kind);
        Assert.Equal(lobbyId, intent.RequireLobbyId());
    }
}
