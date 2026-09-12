using System;
using Godot;
using GameFactory.Networking.Objects;

namespace GameFactory.Networking.Netfox.Player3D;

/// <summary>
/// Reusable rollback-safe CharacterBody3D host. Netfox configuration remains
/// visible in the accompanying scene; gameplay beyond basic locomotion does
/// not belong to this first composition slice.
/// </summary>
public partial class NetworkPlayer3D : CharacterBody3D, INetworkSpawnInitializable
{
    public void ApplyNetworkSpawnData(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary)
            throw new InvalidOperationException("NetworkPlayer3D spawn data must be a Dictionary.");

        Godot.Collections.Dictionary values = data.AsGodotDictionary();
        if (!values.ContainsKey("spawn_position"))
            throw new InvalidOperationException("NetworkPlayer3D spawn data is missing spawn_position.");

        Position = values["spawn_position"].AsVector3();
    }

    public NetworkObject GetNetworkObject() => GetNode<NetworkObject>("NetworkObject");
}
