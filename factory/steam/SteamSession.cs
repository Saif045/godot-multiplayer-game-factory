using System;
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

    public SteamSessionState State { get; private set; } = SteamSessionState.Offline;
    public string? LastError { get; private set; }
    public SteamLobby? Lobby => _adapter.CurrentLobby;
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
        TransitionTo(SteamSessionState.CreatingLobby);
        try
        {
            SteamLobby lobby = await _adapter.CreateLobbyAsync(lobbyOptions, cancellationToken);
            MultiplayerPeer peer = await _adapter.CreateListenServerPeerAsync(peerOptions, cancellationToken);
            InstallPeer(peer);
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
        TransitionTo(SteamSessionState.JoiningLobby);
        try
        {
            SteamLobby lobby = await _adapter.JoinLobbyAsync(lobbyId, cancellationToken);
            MultiplayerPeer peer = await _adapter.CreateLobbyClientPeerAsync(lobbyId, peerOptions, cancellationToken);
            InstallPeer(peer);
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
            try { TearDownActivePeer(); }
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
        try { TearDownActivePeer(); }
        finally
        {
            _ = _adapter.LeaveLobbyAsync();
        }
    }

    private void InstallPeer(MultiplayerPeer peer)
    {
        _activePeer = peer;
        _multiplayer.MultiplayerPeer = peer;
    }

    private void TearDownActivePeer()
    {
        MultiplayerPeer? peer = _activePeer;
        _activePeer = null;
        if (peer is null) return;

        GameLog.Info("steam.peer", "closing", peer.GetType().Name);
        try { PeerTearingDown?.Invoke(); }
        catch (Exception exception)
        {
            GameLog.Warning("steam.peer", "pre_teardown_hook_failed", exception.Message);
        }
        if (GodotObject.IsInstanceValid(peer))
        {
            try
            {
                peer.Close();
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

        if (ReferenceEquals(_multiplayer.MultiplayerPeer, peer))
        {
            _multiplayer.MultiplayerPeer = null;
            GameLog.Info("steam.peer", "cleared_from_multiplayer_api");
        }

        if (!GodotObject.IsInstanceValid(peer))
            return;

        try
        {
            peer.Dispose();
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

    private async Task RollbackFailedConnectionAsync(Exception operationException)
    {
        Exception? cleanupException = null;
        try { TearDownActivePeer(); }
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
