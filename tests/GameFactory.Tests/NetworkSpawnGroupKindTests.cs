using GameFactory.Networking.World;

namespace GameFactory.Tests;

public sealed class NetworkSpawnGroupKindTests
{
    [Fact]
    public void DefaultValue_IsWorldObjects()
    {
        Assert.Equal(
            NetworkSpawnGroupKind.WorldObjects,
            default(NetworkSpawnGroupKind));
    }

    [Fact]
    public void Values_AreStableForSerializedSpawnMetadata()
    {
        Assert.Equal(0, (int)NetworkSpawnGroupKind.WorldObjects);
        Assert.Equal(1, (int)NetworkSpawnGroupKind.Players);
        Assert.Equal(2, (int)NetworkSpawnGroupKind.Projectiles);
    }
}
