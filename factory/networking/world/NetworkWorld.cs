using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Networking.World;

public partial class NetworkWorld : Node
{
    private readonly Dictionary<NetworkObjectId, NetworkObject> _objects = [];
    private readonly Dictionary<NetworkSpawnGroupKind, NetworkSpawnGroup> _spawnGroups = [];

    private long _nextId = 1;

    public override void _EnterTree()
    {
        CreateSpawnGroups();
    }

    public NetworkObject Spawn(PackedScene scene)
    {
        return Spawn(scene, PeerId.Server);
    }

    public NetworkObject Spawn(
        PackedScene scene,
        PeerId ownerPeerId)
    {
        return SpawnCore(scene, ownerPeerId);
    }

    public T Spawn<T>(
        PackedScene scene,
        Action<T> configure)
        where T : Node
    {
        return Spawn(scene, PeerId.Server, configure);
    }

    public T Spawn<T>(
        PackedScene scene,
        PeerId ownerPeerId,
        Action<T> configure)
        where T : Node
    {
        ArgumentNullException.ThrowIfNull(configure);

        T? typedHost = null;

        SpawnCore(
            scene,
            ownerPeerId,
            host =>
            {
                if (host is not T match)
                {
                    throw new InvalidOperationException(
                        $"Scene '{scene.ResourcePath}' has root type " +
                        $"'{host.GetType().Name}', but spawn expected " +
                        $"'{typeof(T).Name}'.");
                }

                typedHost = match;
                configure(match);
            });

        return typedHost
            ?? throw new InvalidOperationException(
                "Spawn did not return the configured host.");
    }

    private NetworkObject SpawnCore(
        PackedScene scene,
        PeerId ownerPeerId,
        Action<Node>? configure = null)
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

        long prefabUid =
            ResourceLoader.GetResourceUid(scene.ResourcePath);

        if (prefabUid == ResourceUid.InvalidId)
        {
            throw new InvalidOperationException(
                $"Scene '{scene.ResourcePath}' has no valid resource UID.");
        }

        Node host = scene.Instantiate();

        try
        {
            NetworkObject networkObject =
                NetworkObject.RequireFromHost(host);

            NetworkSpawnGroupKind kind =
                networkObject.SpawnGroup;

            if (!_spawnGroups.TryGetValue(
                    kind,
                    out NetworkSpawnGroup? group))
            {
                throw new InvalidOperationException(
                    $"NetworkWorld does not contain spawn group '{kind}'.");
            }

            configure?.Invoke(host);

            if (!GodotObject.IsInstanceValid(host))
            {
                throw new InvalidOperationException(
                    "Spawn configuration freed the network object.");
            }

            if (host.IsInsideTree())
            {
                host.QueueFree();

                throw new InvalidOperationException(
                    "Spawn configuration cannot add the network object " +
                    "to the scene tree.");
            }

            networkObject =
                NetworkObject.RequireFromHost(host);

            if (networkObject.SpawnGroup != kind)
            {
                throw new InvalidOperationException(
                    $"Spawn configuration changed the object's spawn group " +
                    $"from '{kind}' to '{networkObject.SpawnGroup}'.");
            }

            NetworkObjectId id = AllocateId();

            NetworkObject spawned =
                group.Spawn(
                    prefabUid,
                    id,
                    ownerPeerId,
                    host);

            GD.Print(
                $"[world][spawn] {id} -> {spawned.Host.Name} " +
                $"[{kind}] owner={ownerPeerId}");

            return spawned;
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

    public void Despawn(NetworkObjectId id)
    {
        if (!Multiplayer.IsServer())
        {
            throw new InvalidOperationException(
                "Only the server can despawn network objects.");
        }

        if (!_objects.TryGetValue(
                id,
                out NetworkObject? networkObject))
        {
            throw new InvalidOperationException(
                $"Network object {id} does not exist.");
        }

        GD.Print(
            $"[world][despawn] {id} -> {networkObject.Host.Name}");

        networkObject.Host.QueueFree();
    }

    public bool TryGet(
        NetworkObjectId id,
        out NetworkObject? networkObject)
    {
        return _objects.TryGetValue(
            id,
            out networkObject);
    }

    internal void Register(NetworkObject networkObject)
    {
        if (!_objects.TryAdd(
                networkObject.Id,
                networkObject))
        {
            throw new InvalidOperationException(
                $"Network object ID {networkObject.Id} " +
                "is already registered.");
        }

        GD.Print(
            $"[world][register] {networkObject.Id} -> " +
            $"{networkObject.Host.Name}");
    }

    internal void Unregister(NetworkObject networkObject)
    {
        if (!_objects.Remove(networkObject.Id))
        {
            GD.PushWarning(
                $"Network object {networkObject.Id} " +
                "was not registered.");

            return;
        }

        GD.Print(
            $"[world][unregister] {networkObject.Id}");
    }

    private void CreateSpawnGroups()
    {
        foreach (
            NetworkSpawnGroupKind kind
            in Enum.GetValues<NetworkSpawnGroupKind>())
        {
            NetworkSpawnGroup group = new()
            {
                Name = kind.ToString()
            };

            group.Configure(
                this,
                kind);

            if (!_spawnGroups.TryAdd(
                    kind,
                    group))
            {
                throw new InvalidOperationException(
                    $"Spawn group '{kind}' already exists.");
            }

            AddChild(group);
        }
    }

    private NetworkObjectId AllocateId()
    {
        return new NetworkObjectId(_nextId++);
    }
}
