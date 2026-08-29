using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Players;
using GameFactory.Runtime;

namespace GameFactory.Tests;

public sealed class PlayerLifecycleTests
{
    [Fact]
    public void Listen_server_creates_player_for_already_present_local_server_peer()
    {
        ServerFixture fixture = CreateServer(RuntimeMode.ListenServer);
        var players = new PlayerRegistry();
        var spawned = new List<(NetworkPeer Peer, PlayerId PlayerId)>();

        using var lifecycle = CreateLifecycle(fixture, players, spawned);

        NetworkPlayer player = Assert.Single(players.Players);
        Assert.Equal(PeerId.Server, player.PeerId);
        Assert.Equal(new PlayerId(1), player.Id);
        Assert.Equal(new NetworkObjectId(101), player.ObjectId);
        Assert.Equal([(PeerId.Server, new PlayerId(1))],
            spawned.Select(entry => (entry.Peer.Id, entry.PlayerId)));
    }

    [Fact]
    public void Dedicated_server_skips_local_server_peer_but_creates_remote_player()
    {
        ServerFixture fixture = CreateServer(RuntimeMode.DedicatedServer);
        var players = new PlayerRegistry();
        var spawned = new List<(NetworkPeer Peer, PlayerId PlayerId)>();

        using var lifecycle = CreateLifecycle(fixture, players, spawned);
        fixture.Peers.Add(new PeerId(8), isLocal: false);

        NetworkPlayer player = Assert.Single(players.Players);
        Assert.Equal(new PeerId(8), player.PeerId);
        Assert.Equal(new PlayerId(1), player.Id);
        Assert.Single(spawned);
    }

    [Fact]
    public void Listen_server_creates_player_for_remote_peer()
    {
        ServerFixture fixture = CreateServer(RuntimeMode.ListenServer);
        var players = new PlayerRegistry();
        var spawned = new List<(NetworkPeer Peer, PlayerId PlayerId)>();

        using var lifecycle = CreateLifecycle(fixture, players, spawned);
        fixture.Peers.Add(new PeerId(8), isLocal: false);

        NetworkPlayer remotePlayer = players.FindByPeer(new PeerId(8))!;
        Assert.Equal(new PlayerId(2), remotePlayer.Id);
        Assert.Equal(new NetworkObjectId(102), remotePlayer.ObjectId);
    }

    [Fact]
    public void Disconnect_despawns_and_removes_player_even_if_despawn_throws()
    {
        ServerFixture fixture = CreateServer(RuntimeMode.DedicatedServer);
        var players = new PlayerRegistry();
        fixture.Peers.Add(new PeerId(8), isLocal: false);
        int despawnCalls = 0;

        using var lifecycle = new PlayerLifecycle(
            fixture.Peers,
            players,
            fixture.Runtime,
            (_, _) => new NetworkObjectId(101),
            _ =>
            {
                despawnCalls++;
                throw new InvalidOperationException("despawn failed");
            });

        Assert.Throws<InvalidOperationException>(
            () => fixture.Peers.Remove(new PeerId(8)));

        Assert.Equal(1, despawnCalls);
        Assert.Empty(players.Players);
        Assert.Null(players.FindByPeer(new PeerId(8)));
    }

    [Fact]
    public void Disconnect_despawns_and_removes_player()
    {
        ServerFixture fixture = CreateServer(RuntimeMode.DedicatedServer);
        var players = new PlayerRegistry();
        fixture.Peers.Add(new PeerId(8), isLocal: false);
        var despawned = new List<NetworkObjectId>();

        using var lifecycle = new PlayerLifecycle(
            fixture.Peers,
            players,
            fixture.Runtime,
            (_, _) => new NetworkObjectId(101),
            objectId => despawned.Add(objectId));

        bool removed = fixture.Peers.Remove(new PeerId(8));

        Assert.True(removed);
        Assert.Equal([new NetworkObjectId(101)], despawned);
        Assert.Empty(players.Players);
    }

    [Fact]
    public void Construction_reconciles_all_already_present_eligible_peers()
    {
        ServerFixture fixture = CreateServer(RuntimeMode.ListenServer);
        fixture.Peers.Add(new PeerId(8), isLocal: false);
        fixture.Peers.Add(new PeerId(9), isLocal: false);
        var players = new PlayerRegistry();
        var spawned = new List<(NetworkPeer Peer, PlayerId PlayerId)>();

        using var lifecycle = CreateLifecycle(fixture, players, spawned);

        Assert.Equal(3, players.Count);
        Assert.Equal(
            [PeerId.Server, new PeerId(8), new PeerId(9)],
            spawned.Select(entry => entry.Peer.Id));
    }

    [Fact]
    public void Dispose_unsubscribes_from_peer_events()
    {
        ServerFixture fixture = CreateServer(RuntimeMode.ListenServer);
        var players = new PlayerRegistry();
        var spawned = new List<(NetworkPeer Peer, PlayerId PlayerId)>();
        PlayerLifecycle lifecycle = CreateLifecycle(fixture, players, spawned);

        lifecycle.Dispose();
        fixture.Peers.Add(new PeerId(8), isLocal: false);

        Assert.Single(players.Players);
        Assert.Single(spawned);
    }

    [Fact]
    public void Client_runtime_does_not_authoritatively_spawn_players()
    {
        var runtime = new RuntimeContext(RuntimeMode.Client);
        var peers = new PeerRegistry();
        var players = new PlayerRegistry();
        int spawnCount = 0;

        using var lifecycle = new PlayerLifecycle(
            peers,
            players,
            runtime,
            (_, _) =>
            {
                spawnCount++;
                return new NetworkObjectId(101);
            },
            _ => { });

        peers.Add(new PeerId(9), isLocal: false);

        Assert.Equal(0, spawnCount);
        Assert.Empty(players.Players);
    }

    private static PlayerLifecycle CreateLifecycle(
        ServerFixture fixture,
        PlayerRegistry players,
        List<(NetworkPeer Peer, PlayerId PlayerId)> spawned)
    {
        return new PlayerLifecycle(
            fixture.Peers,
            players,
            fixture.Runtime,
            (peer, playerId) =>
            {
                spawned.Add((peer, playerId));
                return new NetworkObjectId(100 + spawned.Count);
            },
            _ => { });
    }

    private static ServerFixture CreateServer(RuntimeMode mode)
    {
        var runtime = new RuntimeContext(mode);
        var peers = new PeerRegistry();
        peers.Add(PeerId.Server, isLocal: true);

        return new ServerFixture(runtime, peers);
    }

    private sealed record ServerFixture(
        RuntimeContext Runtime,
        PeerRegistry Peers);
}
