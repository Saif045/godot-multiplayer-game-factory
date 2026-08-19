using System;
using System.Collections.Generic;
using Godot;

namespace GameFactory.Networking.Objects;

public partial class NetworkObject : Node
{
    private readonly List<NetworkObjectComponent> _components = [];

    public Node Host { get; private set; } = null!;

    public override void _EnterTree()
    {
        Host = GetParent()
            ?? throw new InvalidOperationException(
                "NetworkObject must be a child of a host node.");
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
}
