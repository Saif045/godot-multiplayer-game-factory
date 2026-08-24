using System;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;
using GameFactory.Runtime;

namespace GameFactory.Networking.Players;

public sealed class PlayerLifecycle : IDisposable
{
    private readonly PeerRegistry _peers;
    private readonly PlayerRegistry _players;
    private readonly RuntimeContext _runtime;
    private readonly Func<NetworkPeer, PlayerId, NetworkObjectId> _spawn;
    private readonly Action<NetworkObjectId> _despawn;

    private long _nextPlayerId = 1;
    private bool _isDisposed;

    public PlayerLifecycle(
        PeerRegistry peers,
        PlayerRegistry players,
        RuntimeContext runtime,
        Func<NetworkPeer, PlayerId, NetworkObjectId> spawn,
        Action<NetworkObjectId> despawn)
    {
        _peers = peers ?? throw new ArgumentNullException(nameof(peers));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _spawn = spawn ?? throw new ArgumentNullException(nameof(spawn));
        _despawn = despawn ?? throw new ArgumentNullException(nameof(despawn));

        _peers.PeerAdded += OnPeerAdded;
        _peers.PeerRemoved += OnPeerRemoved;

        foreach (NetworkPeer peer in _peers.Peers)
        {
            CreatePlayerIfNeeded(peer);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _peers.PeerAdded -= OnPeerAdded;
        _peers.PeerRemoved -= OnPeerRemoved;
    }

    private void OnPeerAdded(NetworkPeer peer)
    {
        CreatePlayerIfNeeded(peer);
    }

    private void OnPeerRemoved(NetworkPeer peer)
    {
        NetworkPlayer? player = _players.FindByPeer(peer.Id);
        if (player is null)
        {
            return;
        }

        try
        {
            _despawn(player.ObjectId);
        }
        finally
        {
            _players.Remove(player.Id);
        }
    }

    private void CreatePlayerIfNeeded(NetworkPeer peer)
    {
        if (!ShouldCreatePlayer(peer) ||
            _players.FindByPeer(peer.Id) is not null)
        {
            return;
        }

        PlayerId playerId = new(_nextPlayerId++);
        NetworkObjectId objectId = _spawn(peer, playerId);
        _players.Add(new NetworkPlayer(playerId, peer.Id, objectId));
    }

    private bool ShouldCreatePlayer(NetworkPeer peer)
    {
        if (!_runtime.IsServer)
        {
            return false;
        }

        return _runtime.Mode != RuntimeMode.DedicatedServer ||
            !peer.IsLocal;
    }
}
