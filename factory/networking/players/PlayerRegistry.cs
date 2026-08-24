using System;
using System.Collections.Generic;
using GameFactory.Networking.Peers;

namespace GameFactory.Networking.Players;

public sealed class PlayerRegistry
{
    private readonly Dictionary<PlayerId, NetworkPlayer> _players = [];
    private readonly Dictionary<PeerId, NetworkPlayer> _playersByPeer = [];

    public IReadOnlyCollection<NetworkPlayer> Players => _players.Values;

    public int Count => _players.Count;

    public event Action<NetworkPlayer>? PlayerAdded;
    public event Action<NetworkPlayer>? PlayerRemoved;

    public void Add(NetworkPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (_players.ContainsKey(player.Id))
        {
            throw new InvalidOperationException(
                $"Player {player.Id} is already registered.");
        }

        if (_playersByPeer.ContainsKey(player.PeerId))
        {
            throw new InvalidOperationException(
                $"Peer {player.PeerId} already has a player.");
        }

        _players.Add(player.Id, player);
        _playersByPeer.Add(player.PeerId, player);
        PlayerAdded?.Invoke(player);
    }

    public bool Remove(PlayerId id)
    {
        if (!_players.Remove(id, out NetworkPlayer? player))
        {
            return false;
        }

        if (!_playersByPeer.Remove(player.PeerId))
        {
            throw new InvalidOperationException(
                $"Player {id} was missing its peer association.");
        }

        PlayerRemoved?.Invoke(player);
        return true;
    }

    public NetworkPlayer? Find(PlayerId id)
    {
        _players.TryGetValue(id, out NetworkPlayer? player);
        return player;
    }

    public NetworkPlayer? FindByPeer(PeerId peerId)
    {
        _playersByPeer.TryGetValue(peerId, out NetworkPlayer? player);
        return player;
    }
}
