using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Networking.Objects;

namespace GameFactory.Networking.World;

public partial class NetworkSpawnGroup : Node
{
    private readonly Dictionary<NetworkObjectId, Node> _pendingLocalSpawns = [];

    private MultiplayerSpawner _spawner = null!;

    public NetworkWorld World { get; private set; } = null!;

    public NetworkSpawnGroupKind Kind { get; private set; }

    internal void Configure(
        NetworkWorld world,
        NetworkSpawnGroupKind kind)
    {
        if (IsInsideTree())
        {
            throw new InvalidOperationException(
                "NetworkSpawnGroup must be configured " +
                "before entering the scene tree.");
        }

        World = world;
        Kind = kind;
    }

    public override void _EnterTree()
    {
        if (World is null)
        {
            throw new InvalidOperationException(
                "NetworkSpawnGroup was not configured.");
        }

        if (GetParent() != World)
        {
            throw new InvalidOperationException(
                $"{nameof(NetworkSpawnGroup)} must be a " +
                "direct child of its NetworkWorld.");
        }

        _spawner = new MultiplayerSpawner
        {
            Name = "MultiplayerSpawner",

            // Spawned objects become children of this
            // NetworkSpawnGroup.
            SpawnPath = new NodePath(".."),

            SpawnFunction =
                Callable.From<Variant, Node>(
                    CreateSpawnedObject)
        };

        AddChild(_spawner);
    }

    internal NetworkObject Spawn(
        PackedScene scene,
        NetworkObjectId id,
        Node localHost)
    {
        if (!Multiplayer.IsServer())
        {
            throw new InvalidOperationException(
                "Only the server can spawn network objects.");
        }

        if (localHost.IsInsideTree())
        {
            throw new InvalidOperationException(
                "Network spawn host must still be " +
                "outside the scene tree.");
        }

        if (string.IsNullOrWhiteSpace(scene.ResourcePath))
        {
            throw new InvalidOperationException(
                "The network scene must be a saved PackedScene.");
        }

        NetworkObject networkObject =
            FindNetworkObject(localHost);

        if (networkObject.SpawnGroup != Kind)
        {
            throw new InvalidOperationException(
                $"Network object declares spawn group " +
                $"'{networkObject.SpawnGroup}', but was routed " +
                $"through '{Kind}'.");
        }

        if (!_pendingLocalSpawns.TryAdd(
                id,
                localHost))
        {
            throw new InvalidOperationException(
                $"A pending spawn already exists for ID {id}.");
        }

        Godot.Collections.Dictionary data = new()
        {
            ["id"] = id.Value,

            // Still temporary.
            // We haven't solved stable prefab identity yet.
            ["scene"] = scene.ResourcePath
        };

        try
        {
            Node host =
                _spawner.Spawn(data);

            return FindNetworkObject(host);
        }
        finally
        {
            // Normally CreateSpawnedObject removes this.
            // This also protects us if spawning throws early.
            _pendingLocalSpawns.Remove(id);
        }
    }

    private Node CreateSpawnedObject(
        Variant data)
    {
        Godot.Collections.Dictionary spawnData =
            data.AsGodotDictionary();

        NetworkObjectId id =
            new((long)spawnData["id"]);

        string scenePath =
            (string)spawnData["scene"];

        Node host;

        // Server:
        // reuse the exact instance NetworkWorld already created.
        if (_pendingLocalSpawns.Remove(
                id,
                out Node? pendingHost))
        {
            host = pendingHost;
        }
        else
        {
            // Remote peer:
            // construct its local copy from the network payload.
            PackedScene? scene =
                GD.Load<PackedScene>(scenePath);

            if (scene is null)
            {
                throw new InvalidOperationException(
                    $"Unable to load network scene " +
                    $"'{scenePath}'.");
            }

            host = scene.Instantiate();
        }

        NetworkObject networkObject =
            FindNetworkObject(host);

        // Very useful sanity check on every peer.
        if (networkObject.SpawnGroup != Kind)
        {
            host.Free();

            throw new InvalidOperationException(
                $"Network object declares spawn group " +
                $"'{networkObject.SpawnGroup}', but was spawned " +
                $"through '{Kind}'.");
        }

        host.Name =
            $"NetworkObject_{id.Value}";

        networkObject.Bind(
            World,
            id);

        return host;
    }

    private static NetworkObject FindNetworkObject(
        Node host)
    {
        NetworkObject? networkObject =
            host.GetNodeOrNull<NetworkObject>(
                "NetworkObject");

        return networkObject
            ?? throw new InvalidOperationException(
                $"Scene '{host.Name}' is not networkable. " +
                "It must contain a NetworkObject child.");
    }
}