using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Networking.Peers;
using GameFactory.Networking.World;

namespace GameFactory.Networking.Objects;

public partial class NetworkObject : Node
{
    private readonly List<NetworkObjectComponent> _components = [];

    public Node Host { get; private set; } = null!;

    [Export]
    public NetworkSpawnGroupKind SpawnGroup { get; set; } =
        NetworkSpawnGroupKind.WorldObjects;

    private NetworkWorld? _world;
    private NetworkObjectId _id;
    private PeerId _ownerPeerId;

    public bool IsBound => _world is not null;

    public NetworkWorld World =>
        _world
        ?? throw new InvalidOperationException(
            "NetworkObject is not bound to a NetworkWorld.");

    public NetworkObjectId Id
    {
        get
        {
            if (!IsBound)
            {
                throw new InvalidOperationException(
                    "NetworkObject does not have an ID before binding.");
            }

            return _id;
        }
    }

    public PeerId OwnerPeerId
    {
        get
        {
            if (!IsBound)
            {
                throw new InvalidOperationException(
                    "NetworkObject does not have an owner before binding.");
            }

            return _ownerPeerId;
        }
    }

    internal void Bind(
        NetworkWorld world,
        NetworkObjectId id,
        PeerId ownerPeerId)
    {
        if (IsBound)
        {
            throw new InvalidOperationException(
                "NetworkObject is already bound.");
        }

        if (IsInsideTree())
        {
            throw new InvalidOperationException(
                "NetworkObject must be bound before entering the scene tree.");
        }

        _world = world;
        _id = id;
        _ownerPeerId = ownerPeerId;
    }

    public override void _EnterTree()
    {
        Host = GetParent()
            ?? throw new InvalidOperationException(
                "NetworkObject must be a child of a host node.");

        if (!IsBound)
        {
            throw new InvalidOperationException(
                "NetworkObject entered the tree before being bound.");
        }

        World.Register(this);
    }

    public override void _ExitTree()
    {
        if (IsBound)
        {
            World.Unregister(this);
        }
    }

    public override void _Ready()
    {
        foreach (NetworkObjectComponent component in _components)
        {
            component.Initialize();
        }
    }

    internal void RegisterComponent(NetworkObjectComponent component)
    {
        _components.Add(component);
    }

    internal void UnregisterComponent(NetworkObjectComponent component)
    {
        _components.Remove(component);
    }

    public T GetComponent<T>()
        where T : class
    {
        T? result = null;

        foreach (NetworkObjectComponent component in _components)
        {
            if (component is not T match)
                continue;

            if (result is not null)
            {
                throw new InvalidOperationException(
                    $"{nameof(NetworkObject)} contains multiple components " +
                    $"providing {typeof(T).Name}.");
            }

            result = match;
        }

        return result
            ?? throw new InvalidOperationException(
                $"{nameof(NetworkObject)} does not contain a component " +
                $"providing {typeof(T).Name}.");
    }

    public bool TryGetComponent<T>(out T? result)
        where T : class
    {
        result = null;

        foreach (NetworkObjectComponent component in _components)
        {
            if (component is not T match)
                continue;

            if (result is not null)
            {
                throw new InvalidOperationException(
                    $"{nameof(NetworkObject)} contains multiple components " +
                    $"providing {typeof(T).Name}.");
            }

            result = match;
        }

        return result is not null;
    }

    public IEnumerable<T> GetComponents<T>()
        where T : class
    {
        foreach (NetworkObjectComponent component in _components)
        {
            if (component is T match)
                yield return match;
        }
    }

    internal static NetworkObject RequireFromHost(Node host)
    {
        return host.GetNodeOrNull<NetworkObject>("NetworkObject")
            ?? throw new InvalidOperationException(
                $"Scene '{host.Name}' is not networkable. " +
                "It must contain a NetworkObject child.");
    }
}
