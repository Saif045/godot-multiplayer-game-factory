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
    private enum Phase { Lobby, Gameplay, Transitioning }
    private Phase _phase = Phase.Lobby;
    private int _lobbyRevision;
    private int _round;
    private int _clientReadyRevision = -1;
    private readonly HashSet<long> _clearedPeers = new();
    private Task? _returnTask;
    private bool _leaving;
    private readonly HashSet<SteamUserId> _expectedParticipants = new();
    private readonly HashSet<SteamUserId> _readyParticipants = new();
    private readonly Dictionary<PeerId, SteamUserId> _readyPeerUsers = new();
    private readonly string _sessionGeneration = Guid.NewGuid().ToString("N");

    public bool CanReturnToLobby() => !_leaving && _phase == Phase.Gameplay && Multiplayer.IsServer();
    public bool CanLeaveGame() => !_leaving && _phase != Phase.Transitioning;

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
            GameLog.Info("shell", "lobby_entered", fields: ReadinessFields());
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

    public Task ReturnToLobbyAsync()
    {
        if (!CanReturnToLobby()) return _returnTask ?? Task.CompletedTask;
        _phase = Phase.Transitioning;
        _returnTask = ReturnRoundAsync();
        return _returnTask;
    }

    private async Task ReturnRoundAsync()
    {
        try
        {
            GameLog.Info("shell", "round_end_requested", fields: ReadinessFields());
            _lobbyRevision++;
            _readyParticipants.Clear();
            _readyPeerUsers.Clear();
            _clearedPeers.Clear();
            long[] participants = Multiplayer.GetPeers().Where(peer => peer > PeerId.Server.Value).Select(peer => (long)peer).ToArray();
            Rpc(MethodName.ResetRoundRpc, _lobbyRevision);
            ClosePauseMenu();
            await _gameplay.ResetRoundAsync();
            ulong deadline = Time.GetTicksMsec() + 10000;
            while (participants.Any(peer => Multiplayer.GetPeers().Contains((int)peer) && !_clearedPeers.Contains(peer)))
            {
                if (_leaving || !IsInsideTree() || Time.GetTicksMsec() >= deadline)
                    throw new TimeoutException("Participants did not confirm an empty world within 10 seconds.");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            if (_leaving) return;
            _phase = Phase.Lobby;
            RefreshExpectedParticipants(_adapter!);
            Rpc(MethodName.LobbyPhaseRpc, _lobbyRevision);
            _adapter!.SetLobbyJoinable(true);
            _adapter.SetRichPresence("connect", $"+connect_lobby {_adapter.CurrentLobby!.Id}");
            _adapter.SetRichPresence("gamefactory_protocol", "1");
            ShowLobby();
            GameLog.Info("shell", "round_returned_to_lobby", fields: ReadinessFields());
            GameLog.Info("shell", "lobby_phase_started", fields: ReadinessFields());
        }
        catch (Exception exception)
        {
            // Never advertise or start a partially reset world. A failed reset
            // terminates the session instead of weakening the despawn contract.
            if (_leaving) return;
            GameLog.Error("shell", "round_reset_failed", exception.Message, ReadinessFields());
            _phase = Phase.Gameplay;
            GetNode<GameShell>("/root/GameShell").LeaveGame();
        }
        finally { _returnTask = null; }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void ResetRoundRpc(int revision)
    {
        if (_leaving || _phase != Phase.Gameplay || revision != _lobbyRevision + 1) return;
        _lobbyRevision = revision;
        _phase = Phase.Transitioning;
        ClosePauseMenu();
        try
        {
            await _gameplay.ResetRoundAsync();
            if (!_leaving) RpcId(PeerId.Server.Value, MethodName.ClientWorldClearedRpc, revision);
        }
        catch (Exception exception)
        {
            if (_leaving) return;
            GameLog.Error("shell", "round_reset_failed", exception.Message, ReadinessFields());
            _phase = Phase.Gameplay;
            GetNode<GameShell>("/root/GameShell").LeaveGame();
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientWorldClearedRpc(int revision)
    {
        long peer = Multiplayer.GetRemoteSenderId();
        if (Multiplayer.IsServer() && _phase == Phase.Transitioning && revision == _lobbyRevision &&
            peer > PeerId.Server.Value && Multiplayer.GetPeers().Contains((int)peer))
        {
            _clearedPeers.Add(peer);
            GameLog.Info("shell", "round_peer_world_cleared", fields: ReadinessFields(new() { ["peer_id"] = peer.ToString() }));
        }
    }

    private void ClosePauseMenu()
    {
        Node controller = GetNode("PauseMenuController");
        Variant menu = controller.Get("pause_menu");
        if (menu.AsGodotObject() is Node pause) pause.Call("close");
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
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
        if (_phase != Phase.Lobby || _leaving || _lobby is not null) return;
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetNode("PauseMenuController").ProcessMode = ProcessModeEnum.Disabled;
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
            if (platform.Adapter.IsOverlayAvailable)
            {
                platform.Adapter.OpenInviteOverlay();
                inviteFeedback.Text = string.Empty;
            }
            else
            {
                inviteFeedback.Text = "Steam Overlay is unavailable. Your friend can still join from Join Game.";
            }
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
        if (!Multiplayer.IsServer() || _leaving || _phase != Phase.Lobby) return;

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
        _phase = Phase.Transitioning;
        _round++;
        GameLog.Info("shell", "round_start_requested", fields: ReadinessFields());
        platform.Adapter.SetLobbyJoinable(false);
        platform.Adapter.ClearRichPresence();
        GameLog.Info("shell", "start_committed", fields: ReadinessFields());
        GameLog.Info("shell", "lobby_closed_for_gameplay");
        Rpc(MethodName.EnterGameplayRpc, _lobbyRevision, _round);
        EnterGameplayRpc(_lobbyRevision, _round);
    }

    private void OnConnectedToServer()
    {
        if (Multiplayer.IsServer() || _leaving) return;
        // A rejoining client must learn the host's current revision first.
        RpcId(PeerId.Server.Value, MethodName.RequestLobbyPhaseRpc);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestLobbyPhaseRpc()
    {
        long peer = Multiplayer.GetRemoteSenderId();
        if (Multiplayer.IsServer() && _phase == Phase.Lobby && !_leaving && peer > PeerId.Server.Value)
            RpcId(peer, MethodName.LobbyPhaseRpc, _lobbyRevision);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void LobbyPhaseRpc(int revision)
    {
        if (_leaving || revision < _lobbyRevision || _phase == Phase.Gameplay || !_gameplay.IsEmpty) return;
        _lobbyRevision = revision;
        _phase = Phase.Lobby;
        ShowLobby();
        if (_clientReadyRevision == revision) return;
        _clientReadyRevision = revision;
        RpcId(PeerId.Server.Value, MethodName.ClientReadyForPhaseRpc, revision);
        GameLog.Info("shell", "lobby_phase_ready_sent", fields: ReadinessFields());
    }

    private void OnPeerConnected(long peerValue)
    {
        if (!Multiplayer.IsServer() || peerValue <= PeerId.Server.Value)
            return;

        if (_phase == Phase.Lobby && !_leaving)
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
            GetNode<GameShell>("/root/GameShell").GameplayLaunchFailed("The host closed the session or the connection was lost.");
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientReadyForPhaseRpc(int revision)
    {
        if (!Multiplayer.IsServer()) return;
        long peerId = Multiplayer.GetRemoteSenderId();
        if (peerId <= PeerId.Server.Value) return;
        if (_leaving || _phase != Phase.Lobby || revision != _lobbyRevision)
        {
            GameLog.Warning("shell", "lobby_phase_ready_rejected", fields: ReadinessFields(new()
            {
                ["peer_id"] = peerId.ToString(), ["received_revision"] = revision.ToString(),
                ["reason"] = "stale_revision_or_phase"
            }));
            return;
        }
        SteamPlatform platform = GetNode<SteamPlatform>("/root/SteamPlatform");
        RefreshExpectedParticipants(platform.Adapter);
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
            ["lobby_revision"] = _lobbyRevision.ToString(),
            ["session_generation"] = _sessionGeneration
        });
        _readyParticipants.Add(userId);
        _readyPeerUsers[peer] = userId;
        GameLog.Info("shell", "lobby_phase_ready_received", fields: ReadinessFields(new() { ["peer_id"] = peerId.ToString() }));
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

        if (Multiplayer.IsServer() && _phase == Phase.Lobby && current.Contains(adapter.LocalUser.Id))
            _readyParticipants.Add(adapter.LocalUser.Id);
        _readyParticipants.IntersectWith(_expectedParticipants);
        foreach (PeerId peer in _readyPeerUsers.Where(pair => !_readyParticipants.Contains(pair.Value)).Select(pair => pair.Key).ToArray())
            _readyPeerUsers.Remove(peer);

        if (IsReadyToStart())
            GameLog.Info("shell", "start_enabled", fields: ReadinessFields());
    }

    private bool IsReadyToStart() =>
        !_leaving && _phase == Phase.Lobby && _gameplay.IsEmpty &&
        _expectedParticipants.Count > 0 && _expectedParticipants.SetEquals(_readyParticipants);

    private Dictionary<string, string?> ReadinessFields(Dictionary<string, string?>? fields = null)
    {
        fields ??= new Dictionary<string, string?>();
        fields["expected_count"] = _expectedParticipants.Count.ToString();
        fields["ready_count"] = _readyParticipants.Count.ToString();
        fields["session_generation"] = _sessionGeneration;
        fields["lobby_revision"] = _lobbyRevision.ToString();
        fields["round"] = _round.ToString();
        fields["phase"] = _phase.ToString();
        fields["lobby_id"] = _adapter?.CurrentLobby?.Id.ToString();
        fields["session_state"] = _session?.State.ToString();
        fields["steam_peer_instance"] = _session?.ActivePeer?.GetInstanceId().ToString();
        foreach (var pair in _gameplay.StateFields()) fields[pair.Key] = pair.Value;
        return fields;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void EnterGameplayRpc(int revision, int round)
    {
        if (_leaving || revision != _lobbyRevision || _phase == Phase.Gameplay) return;
        _round = round;
        _phase = Phase.Gameplay;
        GetNode("PauseMenuController").ProcessMode = ProcessModeEnum.Inherit;
        _lobby?.QueueFree();
        _lobby = null;
        _gameplay.Start();
        GetNode<GameShell>("/root/GameShell").GameEntered();
        GameLog.Info("shell", "gameplay_entered");
        GameLog.Info("shell", "round_started", fields: ReadinessFields());
    }
}
