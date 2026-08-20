using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Networking.Objects;

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

        // This is the real authoritative instance.
        // It is still completely outside the scene tree.
        Node host = scene.Instantiate();

        try
        {
            NetworkObject networkObject =
                FindNetworkObject(host);

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
                    scene,
                    id,
                    host);

            GD.Print(
                $"[world][spawn] {id} -> {spawned.Host.Name} " +
                $"[{kind}]");

            return spawned;
        }
        catch
        {
            // If ownership never reached MultiplayerSpawner,
            // clean up our off-tree instance.
            if (!host.IsInsideTree() &&
                GodotObject.IsInstanceValid(host))
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