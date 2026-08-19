using GameFactory.Networking.Objects;

namespace GameFactory.Tests;

public sealed class NetworkObjectIdTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveValues(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkObjectId(value));
    }

    [Fact]
    public void Constructor_PreservesPositiveValue()
    {
        NetworkObjectId id = new(42);

        Assert.Equal(42, id.Value);
        Assert.Equal("42", id.ToString());
    }

    [Fact]
    public void Equality_IsBasedOnValue()
    {
        Assert.Equal(new NetworkObjectId(7), new NetworkObjectId(7));
        Assert.NotEqual(new NetworkObjectId(7), new NetworkObjectId(8));
    }
}
