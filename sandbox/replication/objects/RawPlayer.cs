using System;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Objects.Components.Authority;
using GameFactory.Networking.Objects.Components.Replication;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Players;

namespace GameFactory.Sandbox.Replication;

public partial class RawPlayer : Node3D, INetworkSpawnInitializable
{
    public long PlayerId { get; set; }

    public void ApplyNetworkSpawnData(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary)
        {
            throw new InvalidOperationException(
                "Player spawn data must be a Godot Dictionary.");
        }

        Godot.Collections.Dictionary spawnData =
            data.AsGodotDictionary();

        if (!spawnData.ContainsKey("player_id"))
        {
            throw new InvalidOperationException(
                "Player spawn data is missing 'player_id'.");
        }

        PlayerId = new PlayerId(
            (long)spawnData["player_id"]).Value;
    }

    public override void _Ready()
    {
        NetworkObject networkObject =
            GetNode<NetworkObject>("NetworkObject");

        INetworkAuthority authority =
            networkObject.GetComponent<INetworkAuthority>();

        INetworkReplication replication =
            networkObject.GetComponent<INetworkReplication>();

        PrintState(networkObject, authority, "ready");

        replication.Synchronized += () =>
        {
            PrintState(networkObject, authority, "sync");
        };
    }

    private void PrintState(
        NetworkObject networkObject,
        INetworkAuthority authority,
        string phase)
    {
        long localPeerValue = Multiplayer.GetUniqueId();

        bool isLocalOwner =
            localPeerValue > 0 &&
            networkObject.OwnerPeerId ==
                new PeerId(localPeerValue);

        GameLog.Info("gameplay.player", phase, fields: new System.Collections.Generic.Dictionary<string, string?>
        {
            ["player_id"] = PlayerId.ToString(),
            ["network_object_id"] = networkObject.Id.ToString(),
            ["owner_peer_id"] = networkObject.OwnerPeerId.ToString(),
            ["authority_peer_id"] = authority.AuthorityPeerId.ToString(),
            ["local_peer_id"] = localPeerValue.ToString(),
            ["is_local_owner"] = isLocalOwner.ToString(),
            ["has_godot_authority"] = authority.HasAuthority.ToString()
        });
    }
}
