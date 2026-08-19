using GameFactory.Networking.Peers;

namespace GameFactory.Tests;

public sealed class PeerRegistryTests
{
    [Fact]
    public void Add_registers_peer_and_publishes_event()
    {
        var registry = new PeerRegistry();
        NetworkPeer? published = null;
        registry.PeerAdded += peer => published = peer;

        NetworkPeer added = registry.Add(new PeerId(8), isLocal: true);

        Assert.Same(added, published);
        Assert.Same(added, registry.Find(new PeerId(8)));
        Assert.True(registry.Contains(new PeerId(8)));
        Assert.Equal(1, registry.Count);
        Assert.Single(registry.Peers);
    }

    [Fact]
    public void Add_is_idempotent_for_same_id_and_locality()
    {
        var registry = new PeerRegistry();
        int eventCount = 0;
        registry.PeerAdded += _ => eventCount++;

        NetworkPeer first = registry.Add(new PeerId(8), isLocal: false);
        NetworkPeer second = registry.Add(new PeerId(8), isLocal: false);

        Assert.Same(first, second);
        Assert.Equal(1, registry.Count);
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void Add_rejects_conflicting_locality_for_known_id()
    {
        var registry = new PeerRegistry();
        registry.Add(new PeerId(8), isLocal: false);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => registry.Add(new PeerId(8), isLocal: true));

        Assert.Contains("conflicting locality", exception.Message);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Remove_unknown_peer_returns_false_without_event()
    {
        var registry = new PeerRegistry();
        int eventCount = 0;
        registry.PeerRemoved += _ => eventCount++;

        bool removed = registry.Remove(new PeerId(8));

        Assert.False(removed);
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void Remove_known_peer_publishes_removed_instance()
    {
        var registry = new PeerRegistry();
        NetworkPeer added = registry.Add(new PeerId(8), isLocal: false);
        NetworkPeer? published = null;
        registry.PeerRemoved += peer => published = peer;

        bool removed = registry.Remove(new PeerId(8));

        Assert.True(removed);
        Assert.Same(added, published);
        Assert.False(registry.Contains(new PeerId(8)));
        Assert.Null(registry.Find(new PeerId(8)));
    }

    [Fact]
    public void Clear_removes_all_peers_and_publishes_each_removal()
    {
        var registry = new PeerRegistry();
        registry.Add(new PeerId(8), isLocal: false);
        registry.Add(new PeerId(9), isLocal: true);
        var removed = new List<PeerId>();
        registry.PeerRemoved += peer => removed.Add(peer.Id);

        registry.Clear();

        Assert.Empty(registry.Peers);
        Assert.Equal(0, registry.Count);
        Assert.Equal([new PeerId(8), new PeerId(9)], removed);
    }
}
