using System;
using GameFactory.Core;
using GameFactory.Networking.Core;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Transport;

namespace GameFactory.Networking.Sessions;

public sealed class NetworkSession : IDisposable
{
    private readonly INetworkTransport _transport;
    private readonly RuntimeContext _runtime;
    private readonly PeerRegistry _peers;
    private bool _isEnding;
    private bool _isDisposed;

    public SessionState State { get; private set; } = SessionState.Offline;
    public SessionEndReason LastEndReason { get; private set; } = SessionEndReason.None;
    public string? LastError { get; private set; }
    public event Action<SessionState, SessionState>? StateChanged;

    public NetworkSession(INetworkTransport transport, RuntimeContext runtime, PeerRegistry peers)
    {
        _transport = transport;
        _runtime = runtime;
        _peers = peers;
        SubscribeToTransport();
    }

    public SessionResult Host(int port, int maxClients, HostMode hostMode = HostMode.Listen)
    {
        ThrowIfDisposed();
        if (State != SessionState.Offline)
            return SessionResult.Fail($"Cannot host while session is {State}.");

        ResetLastResult();
        TransitionTo(SessionState.Starting);
        ThrowIfDisposed();
        TransportResult result = _transport.StartServer(port, maxClients);
        ThrowIfDisposed();
        if (!result.Success)
            return Fail(SessionEndReason.HostStartFailed, result.Error ?? "Failed to start server.");

        _runtime.SetMode(hostMode == HostMode.Dedicated ? RuntimeMode.DedicatedServer : RuntimeMode.ListenServer);
        _peers.Add(_transport.GetLocalPeerId(), isLocal: true);
        TransitionTo(SessionState.Running);
        return SessionResult.Ok();
    }

    public SessionResult Join(string address, int port)
    {
        ThrowIfDisposed();
        if (State != SessionState.Offline)
            return SessionResult.Fail($"Cannot join while session is {State}.");

        ResetLastResult();
        TransitionTo(SessionState.Connecting);
        ThrowIfDisposed();
        _runtime.SetMode(RuntimeMode.Client);
        TransportResult result = _transport.Connect(address, port);
        ThrowIfDisposed();
        if (State == SessionState.Failed)
            return SessionResult.Fail(LastError ?? "Connection to server failed.");
        if (!result.Success)
            return Fail(SessionEndReason.ConnectionFailed, result.Error ?? "Failed to initialize connection.");

        return SessionResult.Ok();
    }

    public SessionResult Leave()
    {
        ThrowIfDisposed();
        if (_runtime.Mode != RuntimeMode.Client)
            return SessionResult.Fail("Leave() is only valid for a client.");
        if (State is not (SessionState.Running or SessionState.Connecting))
            return SessionResult.Fail($"Cannot leave while session is {State}.");

        return EndIntentionally(SessionEndReason.LocalLeave);
    }

    public SessionResult ShutdownHost()
    {
        ThrowIfDisposed();
        if (!_runtime.IsServer)
            return SessionResult.Fail("ShutdownHost() requires a server runtime.");
        if (State is not (SessionState.Running or SessionState.Starting))
            return SessionResult.Fail($"Cannot shut down host while session is {State}.");

        return EndIntentionally(SessionEndReason.HostShutdown);
    }

    public SessionResult ResetFailure()
    {
        ThrowIfDisposed();
        if (State != SessionState.Failed)
            return SessionResult.Fail("Session is not in Failed state.");

        LastError = null;
        LastEndReason = SessionEndReason.None;
        TransitionTo(SessionState.Offline);
        return SessionResult.Ok();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        UnsubscribeFromTransport();
        try
        {
            bool hasActiveSessionUse = State is SessionState.Starting or SessionState.Connecting or SessionState.Running;
            if (hasActiveSessionUse)
                TransitionTo(SessionState.Stopping);

            Exception? cleanupException = Cleanup(closeTransport: hasActiveSessionUse);
            if (cleanupException is not null)
            {
                LastEndReason = SessionEndReason.CleanupFailed;
                LastError = FormatCleanupError(cleanupException);
            }

            if (State is SessionState.Stopping or SessionState.Failed)
                TransitionTo(SessionState.Offline);
        }
        finally
        {
            _isEnding = false;
        }
    }

    private void SubscribeToTransport()
    {
        _transport.PeerConnected += OnPeerConnected;
        _transport.PeerDisconnected += OnPeerDisconnected;
        _transport.ConnectedToServer += OnConnectedToServer;
        _transport.ConnectionFailed += OnConnectionFailed;
        _transport.ServerDisconnected += OnServerDisconnected;
    }

    private void UnsubscribeFromTransport()
    {
        _transport.PeerConnected -= OnPeerConnected;
        _transport.PeerDisconnected -= OnPeerDisconnected;
        _transport.ConnectedToServer -= OnConnectedToServer;
        _transport.ConnectionFailed -= OnConnectionFailed;
        _transport.ServerDisconnected -= OnServerDisconnected;
    }

    private void OnPeerConnected(PeerId peerId)
    {
        if (!CanAcceptTransportEvents() || State != SessionState.Running)
            return;
        _peers.Add(peerId, isLocal: false);
    }

    private void OnPeerDisconnected(PeerId peerId)
    {
        if (!CanAcceptTransportEvents() || State != SessionState.Running)
            return;
        _peers.Remove(peerId);
    }

    private void OnConnectedToServer()
    {
        if (!CanAcceptTransportEvents() || State != SessionState.Connecting || _runtime.Mode != RuntimeMode.Client)
            return;
        _peers.Add(_transport.GetLocalPeerId(), isLocal: true);
        TransitionTo(SessionState.Running);
    }

    private void OnConnectionFailed()
    {
        if (!CanAcceptTransportEvents() || State != SessionState.Connecting || _runtime.Mode != RuntimeMode.Client)
            return;
        Fail(SessionEndReason.ConnectionFailed, "Connection to server failed.");
    }

    private void OnServerDisconnected()
    {
        if (!CanAcceptTransportEvents()
            || State is not (SessionState.Connecting or SessionState.Running)
            || _runtime.Mode != RuntimeMode.Client)
        {
            return;
        }
        Fail(SessionEndReason.ServerDisconnected, "Server disconnected.");
    }

    private SessionResult EndIntentionally(SessionEndReason reason)
    {
        _isEnding = true;
        try
        {
            TransitionTo(SessionState.Stopping);
            LastEndReason = reason;
            LastError = null;
            Exception? cleanupException = Cleanup();
            if (cleanupException is null)
            {
                TransitionTo(SessionState.Offline);
                return SessionResult.Ok();
            }

            LastEndReason = SessionEndReason.CleanupFailed;
            LastError = FormatCleanupError(cleanupException);
            TransitionTo(SessionState.Failed);
            return SessionResult.Fail(LastError);
        }
        finally
        {
            _isEnding = false;
        }
    }

    private SessionResult Fail(SessionEndReason reason, string error)
    {
        _isEnding = true;
        try
        {
            LastEndReason = reason;
            LastError = error;
            Exception? cleanupException = Cleanup();
            if (cleanupException is not null)
                LastError = $"{error} Cleanup also failed: {FormatCleanupError(cleanupException)}";

            TransitionTo(SessionState.Failed);
            return SessionResult.Fail(LastError);
        }
        finally
        {
            _isEnding = false;
        }
    }

    private Exception? Cleanup(bool closeTransport = true)
    {
        Exception? firstException = null;
        if (closeTransport)
            TryCleanupStep(_transport.Close, ref firstException);
        TryCleanupStep(_peers.Clear, ref firstException);
        TryCleanupStep(_runtime.Reset, ref firstException);
        return firstException;
    }

    private static void TryCleanupStep(Action action, ref Exception? firstException)
    {
        try { action(); }
        catch (Exception exception) { firstException ??= exception; }
    }

    private bool CanAcceptTransportEvents() => !_isDisposed && !_isEnding;

    private void TransitionTo(SessionState next)
    {
        if (State == next)
            return;
        if (!IsValidTransition(State, next))
            throw new InvalidOperationException($"Invalid session transition: {State} -> {next}.");

        SessionState previous = State;
        State = next;
        StateChanged?.Invoke(previous, next);
    }

    private static bool IsValidTransition(SessionState current, SessionState next)
    {
        return (current, next) switch
        {
            (SessionState.Offline, SessionState.Starting) or
            (SessionState.Offline, SessionState.Connecting) or
            (SessionState.Starting, SessionState.Running) or
            (SessionState.Starting, SessionState.Failed) or
            (SessionState.Starting, SessionState.Stopping) or
            (SessionState.Connecting, SessionState.Running) or
            (SessionState.Connecting, SessionState.Stopping) or
            (SessionState.Connecting, SessionState.Failed) or
            (SessionState.Running, SessionState.Stopping) or
            (SessionState.Running, SessionState.Failed) or
            (SessionState.Stopping, SessionState.Offline) or
            (SessionState.Stopping, SessionState.Failed) or
            (SessionState.Failed, SessionState.Offline) => true,
            _ => false
        };
    }

    private static string FormatCleanupError(Exception exception) => $"Session cleanup failed: {exception.Message}";
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);

    private void ResetLastResult()
    {
        LastError = null;
        LastEndReason = SessionEndReason.None;
    }
}
