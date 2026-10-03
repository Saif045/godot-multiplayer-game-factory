using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Steam.Models;

namespace GameFactory.Steam;

/// <summary>Coordinates one Steam lobby/peer lifecycle and assigns its peer to Godot.</summary>
public sealed class SteamSession : IDisposable
{
    private readonly ISteamAdapter _adapter;
    private readonly MultiplayerApi _multiplayer;
    private MultiplayerPeer? _activePeer;
    private bool _disposed;
    private readonly bool _traceEnabled = Array.Exists(OS.GetCmdlineArgs(), arg => arg == "--steam-transport-trace") ||
        Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--steam-transport-trace");
    private readonly string _traceSessionId = Guid.NewGuid().ToString("N");
    private int _traceAttempt;
    private ulong _tracePeerInstanceId;

    public SteamSessionState State { get; private set; } = SteamSessionState.Offline;
    public string? LastError { get; private set; }
    public SteamLobby? Lobby => _adapter.CurrentLobby;
    public MultiplayerPeer? ActivePeer => _activePeer;
    public event Action<SteamSessionState, SteamSessionState>? StateChanged;
    public event Action<SteamLobbyId, SteamUserId>? LobbyJoinRequested;
    /// <summary>Raised after the local closing event is recorded but before Godot's peer is removed.</summary>
    public event Action? PeerTearingDown;

    public SteamSession(ISteamAdapter adapter, MultiplayerApi multiplayer)
    {
        _adapter = adapter;
        _multiplayer = multiplayer;
        _adapter.LobbyJoinRequested += OnLobbyJoinRequested;
        _adapter.Error += OnError;
        if (_traceEnabled)
        {
            _multiplayer.ConnectedToServer += TraceConnectedToServer;
            _multiplayer.ConnectionFailed += TraceConnectionFailed;
            _multiplayer.ServerDisconnected += TraceServerDisconnected;
            _multiplayer.PeerConnected += TracePeerConnected;
            _multiplayer.PeerDisconnected += TracePeerDisconnected;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        EnsureState(SteamSessionState.Offline);
        TransitionTo(SteamSessionState.Initializing);
        try
        {
            await _adapter.InitializeAsync(cancellationToken);
            TransitionTo(SteamSessionState.Ready);
        }
        catch (Exception exception)
        {
            Fail(exception);
            throw;
        }
    }

    public async Task<SteamLobby> HostAsync(
        SteamLobbyCreateOptions lobbyOptions,
        SteamListenServerOptions peerOptions,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        EnsureState(SteamSessionState.Ready);
        _traceAttempt++;
        Trace("host_requested");
        TransitionTo(SteamSessionState.CreatingLobby);
        try
        {
            SteamLobby lobby = await _adapter.CreateLobbyAsync(lobbyOptions, cancellationToken);
            LogLifecycle("lobby_created", "host", lobby);
            MultiplayerPeer peer = await _adapter.CreateListenServerPeerAsync(peerOptions, cancellationToken);
            LogPeer("created", "host", peer, lobby);
            InstallPeer(peer, "host", lobby);
            TransitionTo(SteamSessionState.Hosting);
            return lobby;
        }
        catch (Exception exception)
        {
            await RollbackFailedConnectionAsync(exception);
            throw;
        }
    }

    public async Task<SteamLobby> JoinAsync(
        SteamLobbyId lobbyId,
        SteamClientOptions peerOptions,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        EnsureState(SteamSessionState.Ready);
        _traceAttempt++;
        Trace("join_requested");
        TransitionTo(SteamSessionState.JoiningLobby);
        try
        {
            SteamLobby lobby = await _adapter.JoinLobbyAsync(lobbyId, cancellationToken);
            LogLifecycle("lobby_joined", "client", lobby);
            MultiplayerPeer peer = await _adapter.CreateLobbyClientPeerAsync(lobbyId, peerOptions, cancellationToken);
            LogPeer("created", "client", peer, lobby);
            InstallPeer(peer, "client", lobby);
            TransitionTo(SteamSessionState.Connected);
            return lobby;
        }
        catch (Exception exception)
        {
            await RollbackFailedConnectionAsync(exception);
            throw;
        }
    }

    public async Task LeaveAsync()
    {
        EnsureNotDisposed();
        if (State is not (SteamSessionState.Hosting or SteamSessionState.Connected or SteamSessionState.Failed))
            return;

        TransitionTo(SteamSessionState.Leaving);
        try
        {
            Exception? peerTeardownException = null;
            try { await TearDownActivePeerAsync(); }
            catch (Exception exception) { peerTeardownException = exception; }

            await _adapter.LeaveLobbyAsync();
            if (peerTeardownException is not null)
                throw new InvalidOperationException("Steam peer teardown failed after the lobby was left.", peerTeardownException);

            LastError = null;
            TransitionTo(SteamSessionState.Ready);
        }
        catch (Exception exception)
        {
            Fail(exception);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _adapter.LobbyJoinRequested -= OnLobbyJoinRequested;
        _adapter.Error -= OnError;
        if (_traceEnabled)
        {
            _multiplayer.ConnectedToServer -= TraceConnectedToServer;
            _multiplayer.ConnectionFailed -= TraceConnectionFailed;
            _multiplayer.ServerDisconnected -= TraceServerDisconnected;
            _multiplayer.PeerConnected -= TracePeerConnected;
            _multiplayer.PeerDisconnected -= TracePeerDisconnected;
        }
        Trace("session_disposing");
        try { TearDownActivePeer(); }
        finally
        {
            _ = _adapter.LeaveLobbyAsync();
        }
    }

    private void InstallPeer(MultiplayerPeer peer, string role, SteamLobby lobby)
    {
        _tracePeerInstanceId = peer.GetInstanceId();
        Trace("before_peer_assignment");
        _activePeer = peer;
        _multiplayer.MultiplayerPeer = peer;
        Trace("after_peer_assignment");
        LogPeer("assigned_to_multiplayer_api", role, peer, lobby);
    }

    private static void LogLifecycle(string eventName, string role, SteamLobby lobby) =>
        GameLog.Info("steam.lifecycle", eventName, fields: new Dictionary<string, string?>
        {
            ["role"] = role,
            ["lobby_id"] = lobby.Id.ToString(),
            ["owner_steam_id"] = lobby.OwnerId.ToString(),
            ["member_count"] = lobby.Members.Count.ToString()
        });

    private static void LogPeer(string eventName, string role, MultiplayerPeer peer, SteamLobby lobby) =>
        GameLog.Info("steam.peer", eventName, fields: new Dictionary<string, string?>
        {
            ["role"] = role,
            ["peer_type"] = peer.GetType().Name,
            ["connection_status"] = peer.GetConnectionStatus().ToString(),
            ["lobby_id"] = lobby.Id.ToString(),
            ["owner_steam_id"] = lobby.OwnerId.ToString(),
            ["member_count"] = lobby.Members.Count.ToString()
        });

    private void TearDownActivePeer()
    {
        MultiplayerPeer? peer = CloseActivePeer();
        if (peer is null) return;

        FinalizePeerTeardown(peer);
    }

    private async Task TearDownActivePeerAsync()
    {
        MultiplayerPeer? peer = CloseActivePeer();
        if (peer is null) return;

        // Netfox observes the Steam disconnect through the active
        // MultiplayerApi. Let that notification run before removing the peer.
        if (Engine.GetMainLoop() is SceneTree tree)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        FinalizePeerTeardown(peer);
    }

    private MultiplayerPeer? CloseActivePeer()
    {
        MultiplayerPeer? peer = _activePeer;
        _activePeer = null;
        if (peer is null) return null;

        GameLog.Info("steam.peer", "closing", peer.GetType().Name);
        Trace("before_peer_close");
        try { PeerTearingDown?.Invoke(); }
        catch (Exception exception)
        {
            GameLog.Warning("steam.peer", "pre_teardown_hook_failed", exception.Message);
        }
        if (IsPeerValid(peer))
        {
            try
            {
                peer.Close();
                Trace("after_peer_close");
                GameLog.Info("steam.peer", "closed");
            }
            catch (ObjectDisposedException)
            {
                GameLog.Info("steam.peer", "already_disposed");
            }
            catch (Exception exception)
            {
                GameLog.Warning("steam.peer", "close_failed", exception.Message);
            }
        }
        else
        {
            GameLog.Info("steam.peer", "already_disposed");
        }

        return peer;
    }

    private void FinalizePeerTeardown(MultiplayerPeer peer)
    {
        try
        {
            if (ReferenceEquals(_multiplayer.MultiplayerPeer, peer))
            {
                _multiplayer.MultiplayerPeer = null;
                Trace("after_peer_clear");
                GameLog.Info("steam.peer", "cleared_from_multiplayer_api");
            }
        }
        catch (ObjectDisposedException)
        {
            // Godot can dispose the native peer while its managed wrapper is
            // still retained by MultiplayerApi. That is equivalent to an
            // already-cleared peer, not a failed leave operation.
            GameLog.Info("steam.peer", "already_disposed");
        }

        if (!IsPeerValid(peer))
            return;

        try
        {
            peer.Dispose();
            Trace("after_peer_dispose");
            GameLog.Info("steam.peer", "disposed");
        }
        catch (ObjectDisposedException)
        {
            GameLog.Info("steam.peer", "already_disposed");
        }
        catch (Exception exception)
        {
            GameLog.Warning("steam.peer", "dispose_failed", exception.Message);
        }
    }

    private static bool IsPeerValid(MultiplayerPeer peer)
    {
        try { return GodotObject.IsInstanceValid(peer); }
        catch (ObjectDisposedException) { return false; }
    }

    private async Task RollbackFailedConnectionAsync(Exception operationException)
    {
        Exception? cleanupException = null;
        try { await TearDownActivePeerAsync(); }
        catch (Exception exception) { cleanupException = exception; }

        try { await _adapter.LeaveLobbyAsync(); }
        catch (Exception exception) { cleanupException ??= exception; }

        LastError = operationException.Message;
        if (cleanupException is null)
        {
            TransitionTo(SteamSessionState.Ready);
            return;
        }

        Fail(cleanupException);
    }

    private void OnLobbyJoinRequested(SteamLobbyId lobbyId, SteamUserId inviter) => LobbyJoinRequested?.Invoke(lobbyId, inviter);
    private void TraceConnectedToServer() => Trace("godot_connected_to_server");
    private void TraceConnectionFailed() => Trace("godot_connection_failed");
    private void TraceServerDisconnected() => Trace("godot_server_disconnected");
    private void TracePeerConnected(long peerId) => Trace("godot_peer_connected", peerId);
    private void TracePeerDisconnected(long peerId) => Trace("godot_peer_disconnected", peerId);

    private void Trace(string eventName, long? remotePeerId = null)
    {
        if (!_traceEnabled) return;
        MultiplayerPeer? assigned = _multiplayer.MultiplayerPeer;
        bool valid = assigned is not null && IsPeerValid(assigned);
        GameLog.Info("steam.session_trace", eventName, fields: new Dictionary<string, string?>
        {
            ["session_instance_id"] = _traceSessionId,
            ["process_id"] = System.Environment.ProcessId.ToString(),
            ["local_steam_id"] = _adapter.IsInitialized ? _adapter.LocalUser.Id.ToString() : null,
            ["session_attempt"] = _traceAttempt.ToString(),
            ["peer_instance_id"] = _tracePeerInstanceId.ToString(),
            ["assigned_peer_instance_id"] = valid ? assigned!.GetInstanceId().ToString() : null,
            ["assigned_peer_valid"] = valid.ToString(),
            ["connection_status"] = valid ? assigned!.GetConnectionStatus().ToString() : null,
            ["remote_peer_id"] = remotePeerId?.ToString(),
            ["lobby_id"] = Lobby?.Id.ToString(),
            ["state"] = State.ToString()
        });
    }
    private void OnError(SteamAdapterError error) => LastError = error.Message;
    private void Fail(Exception exception) { LastError = exception.Message; TransitionTo(SteamSessionState.Failed); }
    private void EnsureState(SteamSessionState expected)
    {
        if (State != expected) throw new InvalidOperationException($"Steam session must be {expected}, but is {State}.");
    }
    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    private void TransitionTo(SteamSessionState next)
    {
        if (State == next) return;
        SteamSessionState previous = State;
        State = next;
        GameLog.Info("steam.session", "state_changed", $"{previous} -> {next}", new System.Collections.Generic.Dictionary<string, string?>
        {
            ["previous"] = previous.ToString(),
            ["next"] = next.ToString()
        });
        StateChanged?.Invoke(previous, next);
    }
}
