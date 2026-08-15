using System;
using Godot;
using GameFactory.Networking.Core;

namespace GameFactory.Networking.Transport;

public sealed class ENetTransport : INetworkTransport
{
    private readonly MultiplayerApi _multiplayer;

    private ENetMultiplayerPeer? _peer;

    private bool _disposed;

    public bool IsRunning =>
        _peer is not null;

    public event Action<PeerId>? PeerConnected;
    public event Action<PeerId>? PeerDisconnected;

    public event Action? ConnectedToServer;
    public event Action? ConnectionFailed;
    public event Action? ServerDisconnected;

    public ENetTransport(
        MultiplayerApi multiplayer)
    {
        _multiplayer = multiplayer;

        _multiplayer.PeerConnected +=
            OnPeerConnected;

        _multiplayer.PeerDisconnected +=
            OnPeerDisconnected;

        _multiplayer.ConnectedToServer +=
            OnConnectedToServer;

        _multiplayer.ConnectionFailed +=
            OnConnectionFailed;

        _multiplayer.ServerDisconnected +=
            OnServerDisconnected;
    }

    public TransportResult StartServer(
        int port,
        int maxClients)
    {
        ThrowIfDisposed();

        if (IsRunning)
        {
            return TransportResult.Fail(
                "Transport is already running.");
        }

        var peer =
            new ENetMultiplayerPeer();

        Error error =
            peer.CreateServer(
                port,
                maxClients);

        if (error != Error.Ok)
        {
            peer.Dispose();

            return TransportResult.Fail(
                $"Could not create ENet server: {error}");
        }

        _peer = peer;

        _multiplayer.MultiplayerPeer =
            peer;

        return TransportResult.Ok();
    }

    public TransportResult Connect(
        string address,
        int port)
    {
        ThrowIfDisposed();

        if (IsRunning)
        {
            return TransportResult.Fail(
                "Transport is already running.");
        }

        var peer =
            new ENetMultiplayerPeer();

        Error error =
            peer.CreateClient(
                address,
                port);

        if (error != Error.Ok)
        {
            peer.Dispose();

            return TransportResult.Fail(
                $"Could not create ENet client: {error}");
        }

        _peer = peer;

        _multiplayer.MultiplayerPeer =
            peer;

        return TransportResult.Ok();
    }

    public PeerId GetLocalPeerId()
    {
        ThrowIfDisposed();

        if (_peer is null)
        {
            throw new InvalidOperationException(
                "Transport is not running.");
        }

        return new PeerId(
            _multiplayer.GetUniqueId());
    }

    public void Close()
    {
        if (_disposed)
            return;

        ClosePeer();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _multiplayer.PeerConnected -=
            OnPeerConnected;

        _multiplayer.PeerDisconnected -=
            OnPeerDisconnected;

        _multiplayer.ConnectedToServer -=
            OnConnectedToServer;

        _multiplayer.ConnectionFailed -=
            OnConnectionFailed;

        _multiplayer.ServerDisconnected -=
            OnServerDisconnected;

        ClosePeer();

        GC.SuppressFinalize(this);
    }

    private void ClosePeer()
    {
        if (_peer is null)
            return;

        _peer.Close();

        _multiplayer.MultiplayerPeer =
            null;

        _peer.Dispose();

        _peer = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    private void OnPeerConnected(
        long peerId)
    {
        PeerConnected?.Invoke(
            new PeerId(peerId));
    }

    private void OnPeerDisconnected(
        long peerId)
    {
        PeerDisconnected?.Invoke(
            new PeerId(peerId));
    }

    private void OnConnectedToServer()
    {
        ConnectedToServer?.Invoke();
    }

    private void OnConnectionFailed()
    {
        ConnectionFailed?.Invoke();
    }

    private void OnServerDisconnected()
    {
        ServerDisconnected?.Invoke();
    }
}