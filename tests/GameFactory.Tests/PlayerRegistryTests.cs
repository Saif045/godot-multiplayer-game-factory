using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Players;

namespace GameFactory.Tests;

public sealed class PlayerRegistryTests
{
    [Fact]
    public void Add_registers_player_and_publishes_event()
    {
        var registry = new PlayerRegistry();
        NetworkPlayer player = CreatePlayer(1, 8, 20);
        NetworkPlayer? published = null;
        registry.PlayerAdded += added => published = added;

        registry.Add(player);

        Assert.Same(player, published);
        Assert.Same(player, registry.Find(player.Id));
        Assert.Same(player, registry.FindByPeer(player.PeerId));
        Assert.Single(registry.Players);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Remove_unregisters_player_and_publishes_event()
    {
        var registry = new PlayerRegistry();
        NetworkPlayer player = CreatePlayer(1, 8, 20);
        registry.Add(player);
        NetworkPlayer? published = null;
        registry.PlayerRemoved += removed => published = removed;

        bool removed = registry.Remove(player.Id);

        Assert.True(removed);
        Assert.Same(player, published);
        Assert.Null(registry.Find(player.Id));
        Assert.Null(registry.FindByPeer(player.PeerId));
        Assert.Empty(registry.Players);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Remove_unknown_player_returns_false_without_event()
    {
        var registry = new PlayerRegistry();
        int eventCount = 0;
        registry.PlayerRemoved += _ => eventCount++;

        bool removed = registry.Remove(new PlayerId(1));

        Assert.False(removed);
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void Add_rejects_duplicate_player_id()
    {
        var registry = new PlayerRegistry();
        registry.Add(CreatePlayer(1, 8, 20));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => registry.Add(CreatePlayer(1, 9, 21)));

        Assert.Contains("already registered", exception.Message);
    }

    [Fact]
    public void Add_rejects_duplicate_peer_id()
    {
        var registry = new PlayerRegistry();
        registry.Add(CreatePlayer(1, 8, 20));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => registry.Add(CreatePlayer(2, 8, 21)));

        Assert.Contains("already has a player", exception.Message);
    }

    private static NetworkPlayer CreatePlayer(
        long playerId,
        long peerId,
        long objectId)
    {
        return new NetworkPlayer(
            new PlayerId(playerId),
            new PeerId(peerId),
            new NetworkObjectId(objectId));
    }
}
