using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Peers;
using GameFactory.Steam;
using GameFactory.Steam.Models;

namespace GameFactory.Shell;

/// <summary>Owns one Steam session and starts visible gameplay only after the host begins the lobby.</summary>
public partial class OnlineGameplayShell : Node
{
    private SteamSession _session = null!;
    private OnlineGameplayWorld _gameplay = null!;
    private Control? _lobby;
    private GameFactory.Steam.ISteamAdapter? _adapter;
    private bool _gameStarted;
    private bool _clientPhaseReadySent;
    private bool _leaving;

    public override async void _Ready()
    {
        _gameplay = GetNode<OnlineGameplayWorld>("OnlineGameplayWorld");
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        GameShell shell = GetNode<GameShell>("/root/GameShell");
        try
        {
            SteamPlatform platform = GetNode<SteamPlatform>("/root/SteamPlatform");
            await platform.ReadyTask;
            _session = new SteamSession(platform.Adapter, Multiplayer);
            await _session.InitializeAsync();
            if (!shell.TryConsumeOnlineGameplayLaunchIntent(out OnlineGameplayLaunchIntent intent))
            {
                GameLog.Warning("shell", "gameplay_launch_missing_intent");
                shell.GameplayLaunchFailed("missing_intent");
                return;
            }

            GameLog.Info("shell", "launch_intent_consumed", fields: shell.LaunchIntentFields(intent));
            switch (intent.Kind)
            {
                case OnlineGameplayLaunchKind.Host:
                    SteamLobby hostedLobby = await _session.HostAsync(new SteamLobbyCreateOptions(
                        Metadata: new Dictionary<string, string> { ["gamefactory_protocol"] = "1" }), new SteamListenServerOptions());
                    platform.Adapter.SetRichPresence("connect", $"+connect_lobby {hostedLobby.Id}");
                    platform.Adapter.SetRichPresence("gamefactory_protocol", "1");
                    break;
                case OnlineGameplayLaunchKind.Join:
                    await _session.JoinAsync(intent.RequireLobbyId(), new SteamClientOptions());
                    break;
                default:
                    throw new InvalidOperationException($"Unknown online gameplay launch intent '{intent.Kind}'.");
            }

            ShowLobby();
            GameLog.Info("shell", "lobby_entered");
        }
        catch (Exception exception)
        {
            GameLog.Error("shell", "gameplay_launch_failed", exception.Message);
            shell.GameplayLaunchFailed(exception.Message);
        }
    }

    public async Task LeaveGameAsync()
    {
        _leaving = true;
        // Keep replicated nodes alive until the Steam peer closes and Netfox
        // observes the disconnect. Scene replacement destroys them afterward.
        _gameplay.Stop();
        SteamPlatform platform = GetNode<SteamPlatform>("/root/SteamPlatform");
        platform.Adapter.ClearRichPresence();
        await _session.LeaveAsync();
    }

    public override void _ExitTree()
    {
        if (_adapter is not null) _adapter.LobbyUpdated -= RefreshLobby;
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
        _gameplay.Stop();
        _session?.Dispose();
    }

    private void ShowLobby()
    {
        var panel = new PanelContainer { Name = "Lobby" };
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0), Position = new Vector2(32, 32) };
        panel.AddChild(box);
        GameFactory.Steam.SteamPlatform platform = GetNode<GameFactory.Steam.SteamPlatform>("/root/SteamPlatform");
        _adapter = platform.Adapter;
        _adapter.LobbyUpdated -= RefreshLobby;
        _adapter.LobbyUpdated += RefreshLobby;
        var lobby = platform.Adapter.CurrentLobby!;
        box.AddChild(new Label { Text = $"Lobby\nOwner: {lobby.OwnerId}\nMembers:" });
        foreach (var member in platform.Adapter.GetLobbyMembers())
            box.AddChild(new Label { Text = $"• {member.User.DisplayName}" });
        Button invite = new() { Text = "Invite Friends" };
        invite.Pressed += () => platform.Adapter.OpenInviteOverlay();
        box.AddChild(invite);
        if (platform.Adapter.IsLobbyOwner)
        {
            Button start = new() { Text = "Start Game" };
            start.Pressed += StartGame;
            box.AddChild(start);
        }
        Button leave = new() { Text = "Leave" };
        leave.Pressed += GetNode<GameShell>("/root/GameShell").LeaveGame;
        box.AddChild(leave);
        AddChild(panel);
        _lobby = panel;
    }

    private void RefreshLobby(GameFactory.Steam.Models.SteamLobby _)
    {
        if (_lobby is null) return;
        _lobby.QueueFree();
        _lobby = null;
        CallDeferred(nameof(ShowLobby));
        GameLog.Info("shell", "lobby_members_updated");
    }

    private void StartGame()
    {
        if (!Multiplayer.IsServer()) return;

        // This slice supports assembling a lobby before play begins. Netfox's
        // dynamic-world bootstrap is not yet a supported late-join contract,
        // so stop Steam discovery before any peer enters the running world.
        SteamPlatform platform = GetNode<SteamPlatform>("/root/SteamPlatform");
        platform.Adapter.SetLobbyJoinable(false);
        platform.Adapter.ClearRichPresence();
        GameLog.Info("shell", "lobby_closed_for_gameplay");
        _gameStarted = true;
        Rpc(nameof(EnterGameplayRpc));
        EnterGameplayRpc();
    }

    private void OnConnectedToServer()
    {
        if (_clientPhaseReadySent || Multiplayer.IsServer()) return;
        _clientPhaseReadySent = true;
        RpcId(PeerId.Server.Value, MethodName.ClientReadyForPhaseRpc);
        GameLog.Info("shell", "client_phase_ready_sent");
    }

    private void OnPeerConnected(long peerValue)
    {
        if (!Multiplayer.IsServer() || !_gameStarted || peerValue <= PeerId.Server.Value)
            return;

        // Steam's joinable flag controls discovery but cannot prevent a client
        // already holding stale presence from opening a transport connection.
        // This slice has no late-world bootstrap, so enforce the phase boundary
        // at the host before that connection can enter gameplay.
        GameLog.Warning("shell", "late_join_rejected", fields: new Dictionary<string, string?>
        {
            ["peer_id"] = peerValue.ToString()
        });
        Multiplayer.MultiplayerPeer.DisconnectPeer((int)peerValue, true);
    }

    private async void OnServerDisconnected()
    {
        if (Multiplayer.IsServer() || _leaving)
            return;

        _leaving = true;
        GameLog.Warning("shell", "server_disconnected_returning_to_menu");
        try
        {
            _gameplay.Stop();
            await _session.LeaveAsync();
        }
        catch (Exception exception)
        {
            GameLog.Warning("shell", "server_disconnect_cleanup_failed", exception.Message);
        }
        finally
        {
            GetNode<GameShell>("/root/GameShell").GameplayLaunchFailed("host_game_already_started");
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientReadyForPhaseRpc()
    {
        if (!Multiplayer.IsServer()) return;
        long peerId = Multiplayer.GetRemoteSenderId();
        if (peerId <= PeerId.Server.Value) return;
        GameLog.Info("shell", "client_phase_ready_received", fields: new Dictionary<string, string?>
        {
            ["peer_id"] = peerId.ToString(),
            ["game_started"] = _gameStarted.ToString()
        });
        if (_gameStarted)
            GameLog.Warning("shell", "late_join_ready_rejected", fields: new Dictionary<string, string?>
            {
                ["peer_id"] = peerId.ToString()
            });
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void EnterGameplayRpc()
    {
        _gameStarted = true;
        _lobby?.QueueFree();
        _lobby = null;
        _gameplay.Start();
        GetNode<GameShell>("/root/GameShell").GameEntered();
        GameLog.Info("shell", "gameplay_entered");
    }
}
