using GameFactory.Networking.Peers;

namespace GameFactory.Tests;

public sealed class NetworkPeerTests
{
    [Theory]
    [InlineData(1, true, true, "1 (local, server)")]
    [InlineData(1, false, true, "1 (remote, server)")]
    [InlineData(9, true, false, "9 (local, client)")]
    [InlineData(9, false, false, "9 (remote, client)")]
    public void Locality_and_server_role_are_independent(
        long value,
        bool isLocal,
        bool isServer,
        string text)
    {
        var peer = new NetworkPeer(new PeerId(value), isLocal);

        Assert.Equal(new PeerId(value), peer.Id);
        Assert.Equal(isLocal, peer.IsLocal);
        Assert.Equal(isServer, peer.IsServer);
        Assert.Equal(text, peer.ToString());
    }
}
