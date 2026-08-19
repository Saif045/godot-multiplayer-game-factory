using System;
using Godot;
using GameFactory.Networking.Objects;

namespace GameFactory.Networking.World;

public partial class NetworkSpawnGroup : Node
{
    private MultiplayerSpawner _spawner = null!;

    public NetworkWorld World { get; private set; } = null!;

    public override void _EnterTree()
    {
        World = GetParent() as NetworkWorld
            ?? throw new InvalidOperationException(
                $"{nameof(NetworkSpawnGroup)} must be a direct child of NetworkWorld.");
    }

    public override void _Ready()
    {
        _spawner = GetNode<MultiplayerSpawner>("MultiplayerSpawner");

        _spawner.SpawnFunction =
            Callable.From<Variant, Node>(CreateSpawnedObject);
    }

    internal NetworkObject Spawn(
        PackedScene scene,
        NetworkObjectId id)
    {
        if (!Multiplayer.IsServer())
        {
            throw new InvalidOperationException(
                "Only the server can spawn network objects.");
        }

        if (string.IsNullOrWhiteSpace(scene.ResourcePath))
        {
            throw new InvalidOperationException(
                "The network scene must be a saved PackedScene.");
        }

        Godot.Collections.Dictionary data = new()
        {
            ["id"] = id.Value,
            ["scene"] = scene.ResourcePath
        };

        Node host = _spawner.Spawn(data);

        return FindNetworkObject(host);
    }

    private Node CreateSpawnedObject(Variant data)
    {
        Godot.Collections.Dictionary spawnData =
            data.AsGodotDictionary();

        NetworkObjectId id =
            new((long)spawnData["id"]);

        string scenePath =
            (string)spawnData["scene"];

        PackedScene? scene =
            GD.Load<PackedScene>(scenePath);

        if (scene is null)
        {
            throw new InvalidOperationException(
                $"Unable to load network scene '{scenePath}'.");
        }

        Node host = scene.Instantiate();

        host.Name = $"NetworkObject_{id.Value}";

        NetworkObject networkObject =
            FindNetworkObject(host);

        networkObject.Bind(World, id);

        return host;
    }

    private static NetworkObject FindNetworkObject(Node host)
    {
        NetworkObject? networkObject =
            host.GetNodeOrNull<NetworkObject>("NetworkObject");

        return networkObject
            ?? throw new InvalidOperationException(
                $"Scene '{host.Name}' is not networkable. " +
                $"It must contain a NetworkObject child.");
    }
}
