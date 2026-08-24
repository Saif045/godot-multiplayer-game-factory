using GameFactory.Networking.Players;

namespace GameFactory.Tests;

public sealed class PlayerIdTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveValues(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerId(value));
    }

    [Fact]
    public void Constructor_PreservesPositiveValue()
    {
        PlayerId id = new(42);

        Assert.Equal(42, id.Value);
        Assert.Equal("42", id.ToString());
    }

    [Fact]
    public void Equality_IsBasedOnValue()
    {
        Assert.Equal(new PlayerId(7), new PlayerId(7));
        Assert.NotEqual(new PlayerId(7), new PlayerId(8));
    }
}
