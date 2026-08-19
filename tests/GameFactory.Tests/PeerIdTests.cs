using GameFactory.Networking.Peers;

namespace GameFactory.Tests;

public sealed class PeerIdTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void Constructor_rejects_non_positive_values(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PeerId(value));
    }

    [Fact]
    public void Server_uses_the_reserved_server_value()
    {
        Assert.Equal(1, PeerId.ServerValue);
        Assert.Equal(new PeerId(1), PeerId.Server);
        Assert.True(PeerId.Server.IsServer);
    }

    [Fact]
    public void Non_server_id_preserves_value_and_value_equality()
    {
        var first = new PeerId(42);
        var second = new PeerId(42);

        Assert.Equal(42, first.Value);
        Assert.False(first.IsServer);
        Assert.Equal(first, second);
        Assert.Equal("42", first.ToString());
    }
}
