using System;
using System.Collections.Generic;
using System.Linq;
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
    private const int HostPreparationAttempts = 2;
    private SteamSession _session = null!;
    private OnlineGameplayWorld _gameplay = null!;
    private Control? _lobby;
    private GameFactory.Steam.ISteamAdapter? _adapter;
    private bool _gameStarted;
    private bool _clientPhaseReadySent;
    private bool _leaving;
    private readonly HashSet<SteamUserId> _expectedParticipants = new();
    private readonly HashSet<SteamUserId> _readyParticipants = new();
    private readonly Dictionary<PeerId, SteamUserId> _readyPeerUsers = new();
    private readonly string _sessionGeneration = Guid.NewGuid().ToString("N");

    public override async void _Ready()
    {
        _gameplay = GetNode<OnlineGameplayWorld>("OnlineGameplayWorld");
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
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
                    await HostPreparedLobbyAsync(platform.Adapter);
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
            shell.GameplayLaunchFailed("Could not prepare a safe lobby. We closed it before anyone could join. Try Host Game again; if this repeats, restart Steam and report the time of this message.");
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
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
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
        RefreshExpectedParticipants(platform.Adapter);
        box.AddChild(new Label { Text = $"Lobby\nOwner: {lobby.OwnerId}\nMembers:" });
        foreach (var member in platform.Adapter.GetLobbyMembers())
            box.AddChild(new Label { Text = $"• {member.User.DisplayName}" });
        Button invite = new() { Text = "Invite Friends" };
        Label inviteFeedback = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        invite.Pressed += () =>
        {
            string? feedback = SteamInviteFeedback.Request(platform.Adapter.IsOverlayAvailable, platform.Adapter.OpenInviteOverlay);
            inviteFeedback.Text = feedback ?? string.Empty;
            if (feedback is not null) GameLog.Info("shell", "invite_overlay_unavailable");
        };
        box.AddChild(invite);
        box.AddChild(inviteFeedback);
        if (platform.Adapter.IsLobbyOwner)
        {
            bool canStart = IsReadyToStart();
            Button start = new()
            {
                Text = canStart
                    ? "Start Game"
                    : $"Waiting for players... ({_readyParticipants.Count}/{_expectedParticipants.Count})",
                Disabled = !canStart
            };
            start.Pressed += StartGame;
            box.AddChild(start);
        }
        Button leave = new() { Text = "Leave" };
        leave.Pressed += GetNode<GameShell>("/root/GameShell").LeaveGame;
        box.AddChild(leave);
        AddChild(panel);
        _lobby = panel;
    }

    private async Task HostPreparedLobbyAsync(GameFactory.Steam.ISteamAdapter adapter)
    {
        Exception? lastFailure = null;
        for (int attempt = 1; attempt <= HostPreparationAttempts; attempt++)
        {
            try
            {
                GameLog.Info("shell", "host_preparation_started", fields: new Dictionary<string, string?>
                {
                    ["attempt"] = attempt.ToString(),
                    ["max_attempts"] = HostPreparationAttempts.ToString()
                });
                SteamLobby lobby = await _session.HostAsync(new SteamLobbyCreateOptions(
                    IsJoinable: false,
                    Metadata: new Dictionary<string, string> { ["gamefactory_protocol"] = "1" }), new SteamListenServerOptions());
                MultiplayerPeer? peer = _session.ActivePeer;
                if (peer is null || peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected ||
                    !ReferenceEquals(Multiplayer.MultiplayerPeer, peer))
                    throw new InvalidOperationException("The host Steam peer was not ready after lobby creation.");

                adapter.SetLobbyJoinable(true);
                adapter.SetRichPresence("connect", $"+connect_lobby {lobby.Id}");
                adapter.SetRichPresence("gamefactory_protocol", "1");
                GameLog.Info("shell", "host_preparation_ready", fields: new Dictionary<string, string?>
                {
                    ["attempt"] = attempt.ToString(),
                    ["lobby_id"] = lobby.Id.ToString(),
                    ["peer_status"] = peer.GetConnectionStatus().ToString()
                });
                return;
            }
            catch (Exception exception)
            {
                lastFailure = exception;
                GameLog.Warning("shell", "host_preparation_failed", exception.Message, new Dictionary<string, string?>
                {
                    ["attempt"] = attempt.ToString(),
                    ["max_attempts"] = HostPreparationAttempts.ToString()
                });
                if (_session.State == SteamSessionState.Hosting)
                    await _session.LeaveAsync();
            }
        }

        throw new InvalidOperationException("The host lobby could not be prepared after two attempts.", lastFailure);
    }

    private void RefreshLobby(GameFactory.Steam.Models.SteamLobby _)
    {
        if (_adapter is not null)
            RefreshExpectedParticipants(_adapter);
        if (_lobby is null) return;
        _lobby.QueueFree();
        _lobby = null;
        CallDeferred(nameof(ShowLobby));
        GameLog.Info("shell", "lobby_members_updated");
    }

    private void StartGame()
    {
        if (!Multiplayer.IsServer()) return;

        SteamPlatform platform = GetNode<SteamPlatform>("/root/SteamPlatform");
        RefreshExpectedParticipants(platform.Adapter);
        if (!IsReadyToStart())
        {
            GameLog.Warning("shell", "start_blocked_not_ready", fields: ReadinessFields());
            RefreshLobby(platform.Adapter.CurrentLobby!);
            return;
        }

        // This slice supports assembling a lobby before play begins. Netfox's
        // dynamic-world bootstrap is not yet a supported late-join contract,
        // so stop Steam discovery before any peer enters the running world.
        platform.Adapter.SetLobbyJoinable(false);
        platform.Adapter.ClearRichPresence();
        _gameStarted = true;
        GameLog.Info("shell", "start_committed", fields: ReadinessFields());
        GameLog.Info("shell", "lobby_closed_for_gameplay");
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
        if (!Multiplayer.IsServer() || peerValue <= PeerId.Server.Value)
            return;

        if (!_gameStarted)
        {
            GameLog.Info("shell", "peer_connected", fields: new Dictionary<string, string?>
            {
                ["peer_id"] = peerValue.ToString(),
                ["session_generation"] = _sessionGeneration
            });
            return;
        }

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

    private void OnPeerDisconnected(long peerValue)
    {
        if (!Multiplayer.IsServer() || peerValue <= PeerId.Server.Value)
            return;

        PeerId peerId = new(peerValue);
        if (_readyPeerUsers.Remove(peerId, out SteamUserId userId) && _readyParticipants.Remove(userId))
        {
            GameLog.Info("shell", "participant_readiness_cleared", fields: new Dictionary<string, string?>
            {
                ["steam_user_id"] = userId.ToString(),
                ["peer_id"] = peerValue.ToString(),
                ["reason"] = "peer_disconnected",
                ["session_generation"] = _sessionGeneration
            });
            RefreshLobby(_adapter?.CurrentLobby!);
        }
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
        SteamPlatform platform = GetNode<SteamPlatform>("/root/SteamPlatform");
        PeerId peer = new(peerId);
        if (!platform.Adapter.TryGetSteamUserForPeer(peer, out SteamUserId userId) ||
            !_expectedParticipants.Contains(userId))
        {
            GameLog.Warning("shell", "client_phase_ready_rejected", fields: new Dictionary<string, string?>
            {
                ["peer_id"] = peerId.ToString(),
                ["reason"] = "not_current_lobby_member",
                ["session_generation"] = _sessionGeneration
            });
            return;
        }
        GameLog.Info("shell", "client_phase_ready_received", fields: new Dictionary<string, string?>
        {
            ["peer_id"] = peerId.ToString(),
            ["steam_user_id"] = userId.ToString(),
            ["game_started"] = _gameStarted.ToString(),
            ["session_generation"] = _sessionGeneration
        });
        if (_gameStarted)
        {
            GameLog.Warning("shell", "late_join_ready_rejected", fields: new Dictionary<string, string?>
            {
                ["peer_id"] = peerId.ToString()
            });
            return;
        }

        _readyParticipants.Add(userId);
        _readyPeerUsers[peer] = userId;
        GameLog.Info("shell", "participant_ready", fields: ReadinessFields(new Dictionary<string, string?>
        {
            ["steam_user_id"] = userId.ToString(),
            ["peer_id"] = peerId.ToString()
        }));
        RefreshLobby(platform.Adapter.CurrentLobby!);
    }

    private void RefreshExpectedParticipants(GameFactory.Steam.ISteamAdapter adapter)
    {
        HashSet<SteamUserId> current = adapter.GetLobbyMembers().Select(member => member.User.Id).ToHashSet();
        foreach (SteamUserId removed in _expectedParticipants.Except(current).ToArray())
        {
            _expectedParticipants.Remove(removed);
            if (_readyParticipants.Remove(removed))
                GameLog.Info("shell", "participant_readiness_cleared", fields: new Dictionary<string, string?>
                {
                    ["steam_user_id"] = removed.ToString(),
                    ["reason"] = "lobby_member_left",
                    ["session_generation"] = _sessionGeneration
                });
        }

        foreach (SteamUserId added in current.Except(_expectedParticipants))
        {
            _expectedParticipants.Add(added);
            GameLog.Info("shell", "lobby_member_expected", fields: new Dictionary<string, string?>
            {
                ["steam_user_id"] = added.ToString(),
                ["session_generation"] = _sessionGeneration
            });
        }

        if (current.Contains(adapter.LocalUser.Id))
            _readyParticipants.Add(adapter.LocalUser.Id);
        _readyParticipants.IntersectWith(_expectedParticipants);
        foreach (PeerId peer in _readyPeerUsers.Where(pair => !_readyParticipants.Contains(pair.Value)).Select(pair => pair.Key).ToArray())
            _readyPeerUsers.Remove(peer);

        if (IsReadyToStart())
            GameLog.Info("shell", "start_enabled", fields: ReadinessFields());
    }

    private bool IsReadyToStart() =>
        _expectedParticipants.Count > 0 && _expectedParticipants.SetEquals(_readyParticipants);

    private Dictionary<string, string?> ReadinessFields(Dictionary<string, string?>? fields = null)
    {
        fields ??= new Dictionary<string, string?>();
        fields["expected_count"] = _expectedParticipants.Count.ToString();
        fields["ready_count"] = _readyParticipants.Count.ToString();
        fields["session_generation"] = _sessionGeneration;
        return fields;
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
