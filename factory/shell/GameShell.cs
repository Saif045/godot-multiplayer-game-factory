using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Steam;
using GameFactory.Steam.Models;

namespace GameFactory.Shell;

/// <summary>Thin application-flow bridge; SteamSession remains owned by the active gameplay scene.</summary>
public partial class GameShell : Node
{
    public const string MainMenuScenePath = "res://factory/shell/main_menu.tscn";
    public const string OnlineGameplayScenePath = "res://factory/shell/online_gameplay.tscn";
    public const string JoinMenuScenePath = "res://factory/shell/join_menu.tscn";
    private bool _leaveInProgress;
    private OnlineGameplayLaunchIntent? _onlineGameplayLaunchIntent;
    private SteamPlatform? _steamPlatform;
    private VBoxContainer? _joinList;

    public override void _Ready()
    {
        GameLog.EnsureInitialized();
        _steamPlatform = GetNode<SteamPlatform>("/root/SteamPlatform");
        _steamPlatform.Adapter.LobbyJoinRequested += OnLobbyJoinRequested;
        _steamPlatform.Adapter.FriendPresenceUpdated += OnFriendPresenceUpdated;
        GameLog.Info("shell", "ready");
    }

    public override void _ExitTree()
    {
        if (_steamPlatform is not null)
            _steamPlatform.Adapter.LobbyJoinRequested -= OnLobbyJoinRequested;
        if (_steamPlatform is not null)
            _steamPlatform.Adapter.FriendPresenceUpdated -= OnFriendPresenceUpdated;
    }

    public void GameStartRequested()
    {
        _onlineGameplayLaunchIntent = OnlineGameplayLaunchIntent.Host();
        GameLog.Info("shell", "host_requested", fields: LaunchIntentFields(_onlineGameplayLaunchIntent.Value));
    }

    public void MainMenuShown()
    {
        GameLog.Info("shell", "main_menu");
    }

    public void OpenJoinMenu()
    {
        GetNode("/root/SceneLoader").Call("load_scene", JoinMenuScenePath);
        GameLog.Info("shell", "join_menu_requested");
    }

    public async void PopulateJoinLobbies(VBoxContainer list)
    {
        try
        {
            await _steamPlatform!.ReadyTask;
            _joinList = list;
            ISteamAdapter adapter = _steamPlatform.Adapter;
            IReadOnlyList<SteamFriend> friends = adapter.GetFriends();
            RenderJoinLobbies(friends);
            foreach (SteamFriend friend in friends)
                if (adapter.IsFriend(friend.User.Id)) adapter.RequestFriendPresence(friend.User.Id);
        }
        catch (Exception exception)
        {
            list.AddChild(new Label { Text = "Could not search for GameFactory lobbies." });
            GameLog.Error("shell", "join_search_failed", exception.Message);
        }
    }

    public void JoinLobby(string lobbyId)
    {
        if (!ulong.TryParse(lobbyId, out ulong value) || value == 0) { GameplayLaunchFailed("invalid_lobby_id"); return; }
        _onlineGameplayLaunchIntent = OnlineGameplayLaunchIntent.Join(new SteamLobbyId(value));
        GetNode("/root/SceneLoader").Call("load_scene", OnlineGameplayScenePath);
        GameLog.Info("shell", "join_requested", fields: LaunchIntentFields(_onlineGameplayLaunchIntent.Value));
    }

    public void GameEntered()
    {
        GameLog.Info("shell", "game_entered");
    }
    public void PauseOpened() => GameLog.Info("shell", "pause_opened");
    public void PauseClosed() => GameLog.Info("shell", "pause_closed");

    public async void LeaveGame()
    {
        if (_leaveInProgress)
            return;

        _leaveInProgress = true;
        GameLog.Info("shell", "leave_requested");
        try
        {
            if (GetTree().CurrentScene is OnlineGameplayShell gameplay)
                await gameplay.LeaveGameAsync();
        }
        catch (Exception exception)
        {
            GameLog.Error("shell", "leave_failed", exception.Message);
        }
        finally
        {
            _onlineGameplayLaunchIntent = null;
            GetNode("/root/SceneLoader").Call("load_scene", MainMenuScenePath);
            GameLog.Info("shell", "returned_to_menu");
            _leaveInProgress = false;
        }
    }

    public bool TryConsumeOnlineGameplayLaunchIntent(out OnlineGameplayLaunchIntent intent)
    {
        if (_onlineGameplayLaunchIntent is not OnlineGameplayLaunchIntent pending)
        {
            intent = default;
            return false;
        }

        _onlineGameplayLaunchIntent = null;
        intent = pending;
        return true;
    }

    public void GameplayLaunchFailed(string reason)
    {
        _onlineGameplayLaunchIntent = null;
        GameLog.Warning("shell", "gameplay_launch_returning_to_menu", reason);
        GetNode("/root/SceneLoader").CallDeferred("load_scene", MainMenuScenePath);
    }

    public Dictionary<string, string?> LaunchIntentFields(OnlineGameplayLaunchIntent intent) => new()
    {
        ["kind"] = intent.Kind.ToString(),
        ["lobby_id"] = intent.LobbyId?.ToString()
    };

    private void AddLobbyButton(VBoxContainer list, SteamLobbyId lobbyId, string hostName)
    {
        Button button = new() { Text = hostName };
        button.Pressed += () => JoinLobby(lobbyId.ToString());
        list.AddChild(button);
    }

    private void OnLobbyJoinRequested(SteamLobbyId lobbyId, SteamUserId inviter)
    {
        if (HasActiveOnlineSession())
        {
            GameLog.Warning("shell", "join_request_ignored_active_gameplay", fields: new Dictionary<string, string?>
            {
                ["lobby_id"] = lobbyId.ToString(),
                ["inviter_steam_id"] = inviter.ToString()
            });
            return;
        }

        OnlineGameplayLaunchIntent intent = OnlineGameplayLaunchIntent.Join(lobbyId);
        _onlineGameplayLaunchIntent = intent;
        GameLog.Info("shell", "join_requested", fields: new Dictionary<string, string?>
        {
            ["lobby_id"] = lobbyId.ToString(),
            ["inviter_steam_id"] = inviter.ToString()
        });
        GetNode("/root/SceneLoader").Call("load_scene", OnlineGameplayScenePath);
        GameLog.Info("shell", "online_gameplay_scene_requested", fields: LaunchIntentFields(intent));
    }

    private void OnFriendPresenceUpdated(SteamUserId _)
    {
        if (_joinList is null || !GodotObject.IsInstanceValid(_joinList) || !_joinList.IsInsideTree()) return;
        try { RenderJoinLobbies(_steamPlatform!.Adapter.GetFriends()); }
        catch (Exception exception) { GameLog.Warning("shell", "join_search_refresh_failed", exception.Message); }
    }

    private void RenderJoinLobbies(IReadOnlyList<SteamFriend> friends)
    {
        if (_joinList is null || !GodotObject.IsInstanceValid(_joinList)) return;
        foreach (Node child in _joinList.GetChildren()) child.QueueFree();
        HashSet<SteamLobbyId> displayedLobbyIds = [];
        foreach (SteamFriend friend in friends)
        {
            if (friend.Presence.GameFactoryProtocol != "1" ||
                !TryReadConnectLobby(friend.Presence.ConnectString, out SteamLobbyId lobbyId) ||
                !displayedLobbyIds.Add(lobbyId))
                continue;
            AddLobbyButton(_joinList, lobbyId, friend.User.DisplayName);
        }

        if (displayedLobbyIds.Count == 0)
            _joinList.AddChild(new Label { Text = "No joinable GameFactory lobbies found. Refreshing friends..." });
        GameLog.Info("shell", "join_search_completed", fields: new Dictionary<string, string?>
        {
            ["friend_count"] = friends.Count.ToString(),
            ["displayed_count"] = displayedLobbyIds.Count.ToString()
        });
    }

    private bool HasActiveOnlineSession() =>
        _onlineGameplayLaunchIntent is not null || GetTree().CurrentScene is OnlineGameplayShell;

    private static bool TryReadConnectLobby(string? connect, out SteamLobbyId lobbyId)
    {
        lobbyId = default;
        string[] parts = (connect ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0] == "+connect_lobby" && ulong.TryParse(parts[1], out ulong value) && value != 0 && (lobbyId = new SteamLobbyId(value)).Value != 0;
    }
}
