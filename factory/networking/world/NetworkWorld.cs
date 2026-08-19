using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Networking.Objects;

namespace GameFactory.Networking.World;

public partial class NetworkWorld : Node
{
    private readonly Dictionary<NetworkObjectId, NetworkObject> _objects = [];

    private long _nextId = 1;

    public NetworkObject Spawn(
        NetworkSpawnGroup group,
        PackedScene scene)
    {
        if (!Multiplayer.IsServer())
        {
            throw new InvalidOperationException(
                "Only the server can spawn network objects.");
        }

        if (group.World != this)
        {
            throw new InvalidOperationException(
                "Spawn group does not belong to this NetworkWorld.");
        }

        NetworkObjectId id = AllocateId();

        NetworkObject networkObject =
            group.Spawn(scene, id);

        GD.Print(
            $"[world][spawn] {id} -> {networkObject.Host.Name}");

        return networkObject;
    }

    public void Despawn(NetworkObjectId id)
    {
        if (!Multiplayer.IsServer())
        {
            throw new InvalidOperationException(
                "Only the server can despawn network objects.");
        }

        if (!_objects.TryGetValue(id, out NetworkObject? networkObject))
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
        return _objects.TryGetValue(id, out networkObject);
    }

    internal void Register(NetworkObject networkObject)
    {
        if (!_objects.TryAdd(
                networkObject.Id,
                networkObject))
        {
            throw new InvalidOperationException(
                $"Network object ID {networkObject.Id} is already registered.");
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
                $"Network object {networkObject.Id} was not registered.");
            return;
        }

        GD.Print(
            $"[world][unregister] {networkObject.Id}");
    }

    private NetworkObjectId AllocateId()
    {
        return new NetworkObjectId(_nextId++);
    }
}
