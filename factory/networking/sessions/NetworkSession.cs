using System;
using GameFactory.Core;
using GameFactory.Networking.Core;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Transport;

namespace GameFactory.Networking.Sessions;

public sealed class NetworkSession
{
    private readonly INetworkTransport _transport;
    private readonly RuntimeContext _runtime;
    private readonly PeerRegistry _peers;

    private bool _isEnding;

    public SessionState State { get; private set; } = SessionState.Offline;

    public SessionEndReason LastEndReason { get; private set; } = SessionEndReason.None;

    public string? LastError { get; private set; }

    public event Action<SessionState, SessionState>? StateChanged;

    public NetworkSession(
        INetworkTransport transport,
        RuntimeContext runtime,
        PeerRegistry peers)
    {
        _transport = transport;
        _runtime = runtime;
        _peers = peers;

        SubscribeToTransport();
    }

    public SessionResult Host(
        int port,
        int maxClients,
        HostMode hostMode = HostMode.Listen)
    {
        if (State != SessionState.Offline)
        {
            return SessionResult.Fail(
                $"Cannot host while session is {State}.");
        }

        ResetLastResult();

        TransitionTo(SessionState.Starting);

        TransportResult result = _transport.StartServer(port, maxClients);

        if (!result.Success)
        {
            string error = result.Error ?? "Failed to start server.";

            Fail(SessionEndReason.HostStartFailed, error);

            return SessionResult.Fail(error);
        }

        RuntimeMode runtimeMode = hostMode == HostMode.Dedicated
            ? RuntimeMode.DedicatedServer
            : RuntimeMode.ListenServer;

        _runtime.SetMode(runtimeMode);

        PeerId localPeerId = _transport.GetLocalPeerId();

        _peers.Add(localPeerId, isLocal: true);

        TransitionTo(SessionState.Running);

        return SessionResult.Ok();
    }

    public SessionResult Join(
        string address,
        int port)
    {
        if (State != SessionState.Offline)
        {
            return SessionResult.Fail(
                $"Cannot join while session is {State}.");
        }

        ResetLastResult();

        TransitionTo(SessionState.Connecting);

        TransportResult result = _transport.Connect(address, port);

        if (!result.Success)
        {
            Fail(
                SessionEndReason.ConnectionFailed,
                result.Error ?? "Failed to initialize connection.");

            return SessionResult.Fail(result.Error ?? "Failed to initialize connection.");
        }

        _runtime.SetMode(RuntimeMode.Client);

        // Creating the client transport does not confirm the server connection.
        // OnConnectedToServer moves the session from Connecting to Running.

        return SessionResult.Ok();
    }

    public SessionResult Leave()
    {
        if (_runtime.Mode != RuntimeMode.Client)
        {
            return SessionResult.Fail(
                "Leave() is only valid for a client.");
        }

        if (State is not SessionState.Running
            and not SessionState.Connecting)
        {
            return SessionResult.Fail(
                $"Cannot leave while session is {State}.");
        }

        EndIntentionally(SessionEndReason.LocalLeave);

        return SessionResult.Ok();
    }

    public SessionResult ShutdownHost()
    {
        if (!_runtime.IsServer)
        {
            return SessionResult.Fail(
                "ShutdownHost() requires a server runtime.");
        }

        if (State is not SessionState.Running
            and not SessionState.Starting)
        {
            return SessionResult.Fail(
                $"Cannot shut down host while session is {State}.");
        }

        EndIntentionally(SessionEndReason.HostShutdown);

        return SessionResult.Ok();
    }

    public SessionResult ResetFailure()
    {
        if (State != SessionState.Failed)
        {
            return SessionResult.Fail(
                "Session is not in Failed state.");
        }

        LastError = null;
        LastEndReason = SessionEndReason.None;

        TransitionTo(SessionState.Offline);

        return SessionResult.Ok();
    }

    private void SubscribeToTransport()
    {
        _transport.PeerConnected += OnPeerConnected;
        _transport.PeerDisconnected += OnPeerDisconnected;
        _transport.ConnectedToServer += OnConnectedToServer;
        _transport.ConnectionFailed += OnConnectionFailed;
        _transport.ServerDisconnected += OnServerDisconnected;
    }

    private void OnPeerConnected(PeerId peerId)
    {
        if (_isEnding)
            return;

        _peers.Add(peerId, isLocal: false);
    }

    private void OnPeerDisconnected(PeerId peerId)
    {
        if (_isEnding)
            return;

        _peers.Remove(peerId);

        // ServerDisconnected handles the loss of a client's server. This event
        // also fires normally when another client leaves a server session.
    }

    private void OnConnectedToServer()
    {
        if (_isEnding)
            return;

        if (State != SessionState.Connecting)
            return;

        PeerId localPeerId = _transport.GetLocalPeerId();

        _peers.Add(localPeerId, isLocal: true);

        TransitionTo(SessionState.Running);
    }

    private void OnConnectionFailed()
    {
        if (_isEnding)
            return;

        if (State != SessionState.Connecting)
            return;

        Fail(
            SessionEndReason.ConnectionFailed,
            "Connection to server failed.");
    }

    private void OnServerDisconnected()
    {
        if (_isEnding)
            return;

        if (State is SessionState.Offline
            or SessionState.Failed)
        {
            return;
        }

        Fail(
            SessionEndReason.ServerDisconnected,
            "Server disconnected.");
    }

    private void EndIntentionally(SessionEndReason reason)
    {
        if (_isEnding)
            return;

        _isEnding = true;

        TransitionTo(SessionState.Stopping);

        LastEndReason = reason;
        LastError = null;

        Cleanup();

        TransitionTo(SessionState.Offline);

        _isEnding = false;
    }

    private void Fail(
        SessionEndReason reason,
        string error)
    {
        if (_isEnding)
            return;

        _isEnding = true;

        LastEndReason = reason;
        LastError = error;

        Cleanup();

        TransitionTo(
            SessionState.Failed);

        _isEnding = false;
    }

    private void Cleanup()
    {
        _transport.Close();

        _peers.Clear();

        _runtime.Reset();
    }

    private void TransitionTo(SessionState next)
    {
        if (State == next)
            return;

        SessionState previous = State;

        State = next;

        StateChanged?.Invoke(previous, next);
    }

    private void ResetLastResult()
    {
        LastError = null;
        LastEndReason = SessionEndReason.None;
    }
}
