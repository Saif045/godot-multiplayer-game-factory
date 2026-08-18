using GameFactory.Networking.Core;

namespace GameFactory.Networking.Peers;

public sealed class NetworkPeer
{
    public PeerId Id { get; }

    public bool IsLocal { get; }

    public bool IsServer => Id.IsServer;

    public NetworkPeer(PeerId id, bool isLocal)
    {
        Id = id;
        IsLocal = isLocal;
    }

    public override string ToString()
    {
        string locality = IsLocal ? "local" : "remote";
        string role = IsServer ? "server" : "client";

        return $"{Id} ({locality}, {role})";
    }
}
