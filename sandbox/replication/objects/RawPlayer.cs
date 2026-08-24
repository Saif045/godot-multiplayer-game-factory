using Godot;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Objects.Components.Authority;
using GameFactory.Networking.Objects.Components.Replication;
using GameFactory.Networking.Peers;

namespace GameFactory.Sandbox.Replication;

public partial class RawPlayer : Node3D
{
    [Replicated]
    public long PlayerId { get; set; }

    public override void _Ready()
    {
        NetworkObject networkObject =
            GetNode<NetworkObject>("NetworkObject");

        INetworkAuthority authority =
            networkObject.GetComponent<INetworkAuthority>();

        long localPeerValue = Multiplayer.GetUniqueId();
        bool isLocalOwner =
            localPeerValue > 0 &&
            networkObject.OwnerPeerId == new PeerId(localPeerValue);

        GD.Print(
            $"[player] player={PlayerId} object={networkObject.Id} " +
            $"owner={networkObject.OwnerPeerId} " +
            $"godot_authority={authority.AuthorityPeerId} " +
            $"local_peer={localPeerValue} " +
            $"is_local_owner={isLocalOwner} " +
            $"has_godot_authority={authority.HasAuthority}");
    }
}
