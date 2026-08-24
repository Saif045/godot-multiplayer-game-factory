using Godot;

namespace GameFactory.Networking.Objects;

public interface INetworkSpawnInitializable
{
    void ApplyNetworkSpawnData(Variant data);
}
