using System;
using Godot;

namespace GameFactory.Networking.Objects;

public abstract partial class NetworkObjectComponent : Node
{
    public NetworkObject NetworkObject { get; private set; } = null!;

    public Node Host => NetworkObject.Host;

    public override void _EnterTree()
    {
        NetworkObject = GetParent() as NetworkObject
            ?? throw new InvalidOperationException(
                $"{GetType().Name} must be a direct child of NetworkObject.");

        NetworkObject.RegisterComponent(this);
    }

    internal void Initialize()
    {
        OnNetworkInitialize();
    }

    public override void _ExitTree()
    {
        OnNetworkShutdown();
        NetworkObject.UnregisterComponent(this);
    }

    protected virtual void OnNetworkInitialize()
    {
    }

    protected virtual void OnNetworkShutdown()
    {
    }
}
