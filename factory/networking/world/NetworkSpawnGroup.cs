using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

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
        long prefabUid,
        NetworkObjectId id,
        PeerId ownerPeerId,
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
                "Network spawn host must be outside the scene tree.");
        }

        NetworkObject networkObject =
            NetworkObject.RequireFromHost(localHost);

        if (networkObject.SpawnGroup != Kind)
        {
            throw new InvalidOperationException(
                $"Network object declares '{networkObject.SpawnGroup}' " +
                $"but was routed through '{Kind}'.");
        }

        if (!_pendingLocalSpawns.TryAdd(id, localHost))
        {
            throw new InvalidOperationException(
                $"Pending spawn already exists for {id}.");
        }

        Godot.Collections.Dictionary data = new()
        {
            ["id"] = id.Value,
            ["prefab_uid"] = prefabUid,
            ["owner_peer_id"] = ownerPeerId.Value
        };

        try
        {
            Node host = _spawner.Spawn(data);
            return NetworkObject.RequireFromHost(host);
        }
        finally
        {
            _pendingLocalSpawns.Remove(id);
        }
    }

    private Node CreateSpawnedObject(Variant data)
    {
        Godot.Collections.Dictionary spawnData =
            data.AsGodotDictionary();

        NetworkObjectId id =
            new((long)spawnData["id"]);

        long prefabUid =
            (long)spawnData["prefab_uid"];

        PeerId ownerPeerId =
            new((long)spawnData["owner_peer_id"]);

        Node? host = null;

        try
        {
            if (_pendingLocalSpawns.Remove(
                    id,
                    out Node? pendingHost))
            {
                host = pendingHost
                    ?? throw new InvalidOperationException(
                        $"Pending spawn {id} has no host node.");
            }
            else
            {
                if (!ResourceUid.HasId(prefabUid))
                {
                    throw new InvalidOperationException(
                        $"Unknown network prefab UID '{prefabUid}'.");
                }

                string? scenePath =
                    ResourceUid.GetIdPath(prefabUid);

                if (string.IsNullOrWhiteSpace(scenePath))
                {
                    throw new InvalidOperationException(
                        $"Network prefab UID '{prefabUid}' has no resource path.");
                }

                PackedScene? scene =
                    ResourceLoader.Load<PackedScene>(scenePath);

                if (scene is null)
                {
                    throw new InvalidOperationException(
                        $"Network prefab UID '{prefabUid}' " +
                        $"resolved to '{scenePath}', but it is not a PackedScene.");
                }

                host = scene.Instantiate();
            }

            if (host is null)
            {
                throw new InvalidOperationException(
                    "Network spawn did not produce a host node.");
            }

            NetworkObject networkObject =
                NetworkObject.RequireFromHost(host);

            if (networkObject.SpawnGroup != Kind)
            {
                throw new InvalidOperationException(
                    $"Network prefab UID '{prefabUid}' declares group " +
                    $"'{networkObject.SpawnGroup}', but arrived through '{Kind}'.");
            }

            host.Name = $"NetworkObject_{id.Value}";

            networkObject.Bind(World, id, ownerPeerId);

            return host;
        }
        catch
        {
            if (GodotObject.IsInstanceValid(host) &&
                !host.IsInsideTree())
            {
                host.Free();
            }

            throw;
        }
    }
}
