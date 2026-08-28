using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Networking.World;

public partial class NetworkWorld : Node
{
    private readonly Dictionary<NetworkObjectId, NetworkObject> _objects = [];
    private readonly Dictionary<NetworkSpawnGroupKind, NetworkSpawnGroup> _spawnGroups = [];

    private long _nextId = 1;

    /// <summary>Number of locally registered network objects in this world.</summary>
    public int Count => _objects.Count;

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
        return SpawnCore(scene, ownerPeerId, default);
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
        return Spawn(scene, ownerPeerId, default, configure);
    }

    public T Spawn<T>(
        PackedScene scene,
        PeerId ownerPeerId,
        Variant spawnData,
        Action<T>? configure = null)
        where T : Node
    {
        T? typedHost = null;

        SpawnCore(
            scene,
            ownerPeerId,
            spawnData,
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
                configure?.Invoke(match);
            });

        return typedHost
            ?? throw new InvalidOperationException(
                "Spawn did not return the configured host.");
    }

    private NetworkObject SpawnCore(
        PackedScene scene,
        PeerId ownerPeerId,
        Variant spawnData,
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

            NetworkObjectId id = AllocateId();

            NetworkObject spawned =
                group.Spawn(
                    prefabUid,
                    id,
                    ownerPeerId,
                    spawnData,
                    host,
                    configure);

            GameLog.Info("network_object", "spawned", $"{id} -> {spawned.Host.Name} [{kind}] owner={ownerPeerId}");

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

        GameLog.Info("network_object", "despawned", $"{id} -> {networkObject.Host.Name}");

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

        GameLog.Info("network_object", "registered", $"{networkObject.Id} -> {networkObject.Host.Name}");
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

        GameLog.Info("network_object", "unregistered", networkObject.Id.ToString());
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
