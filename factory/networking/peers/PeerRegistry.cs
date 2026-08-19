using System;
using System.Collections.Generic;
namespace GameFactory.Networking.Peers;

public sealed class PeerRegistry
{
    private readonly Dictionary<PeerId, NetworkPeer> _peers = new();

    public IReadOnlyCollection<NetworkPeer> Peers => _peers.Values;

    public int Count => _peers.Count;

    public event Action<NetworkPeer>? PeerAdded;
    public event Action<NetworkPeer>? PeerRemoved;

    public NetworkPeer Add(PeerId id, bool isLocal)
    {
        if (_peers.TryGetValue(id, out NetworkPeer? existing))
        {
            if (existing.IsLocal != isLocal)
            {
                throw new InvalidOperationException(
                    $"Peer {id} was registered with " +
                    "conflicting locality.");
            }

            return existing;
        }

        var peer = new NetworkPeer(id, isLocal);

        _peers.Add(id, peer);

        PeerAdded?.Invoke(peer);

        return peer;
    }

    public bool Remove(PeerId id)
    {
        if (!_peers.Remove(id, out NetworkPeer? peer))
        {
            return false;
        }

        PeerRemoved?.Invoke(peer);

        return true;
    }

    public NetworkPeer? Find(PeerId id)
    {
        _peers.TryGetValue(id, out NetworkPeer? peer);

        return peer;
    }

    public bool Contains(PeerId id)
    {
        return _peers.ContainsKey(id);
    }

    public void Clear()
    {
        NetworkPeer[] peers = [.. _peers.Values];

        _peers.Clear();

        foreach (NetworkPeer peer in peers)
        {
            PeerRemoved?.Invoke(peer);
        }
    }
}
