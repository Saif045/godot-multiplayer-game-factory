using System;
using Godot;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Objects.Components.Replication;

namespace GameFactory.Networking.Netfox.Player3D;

/// <summary>
/// Reusable rollback-safe CharacterBody3D host. Netfox configuration remains
/// visible in the accompanying scene; gameplay beyond basic locomotion does
/// not belong to this first composition slice.
/// </summary>
public partial class NetworkPlayer3D : CharacterBody3D, INetworkSpawnInitializable
{
    private static readonly Color[] OwnerColors =
    [
        new Color("4ea8de"),
        new Color("f4a261"),
        new Color("80ed99"),
        new Color("c77dff"),
        new Color("ffd166"),
        new Color("ef476f")
    ];

    /// <summary>
    /// Authoritative, non-rollback projection of the player's GAS health.
    /// NetworkGasComponent owns mutation and local GAS mirroring; this host
    /// property exists solely for the normal replication component.
    /// </summary>
    [Replicated(ReplicationMode.OnChange)]
    public float GasHealth { get; set; } = 100f;

    [Replicated(ReplicationMode.OnChange)]
    public bool GasIsFortified { get; set; }

    [Replicated(ReplicationMode.OnChange)]
    public float GasFortifyCooldownRemaining { get; set; }
    [Replicated(ReplicationMode.OnChange)]
    public float GasMoveSpeed { get; set; } = 6f;

    public override void _Ready()
    {
        ApplyOwnerColor();
    }

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

    private void ApplyOwnerColor()
    {
        NetworkObject networkObject = GetNetworkObject();
        int paletteIndex = (int)(networkObject.OwnerPeerId.Value % OwnerColors.Length);
        if (paletteIndex < 0)
            paletteIndex += OwnerColors.Length;

        MeshInstance3D mesh = GetNode<MeshInstance3D>("Presentation/VisualRoot/Mesh");
        if (mesh.GetActiveMaterial(0) is not StandardMaterial3D source)
        {
            throw new InvalidOperationException(
                "NetworkPlayer3D requires a StandardMaterial3D on its presentation mesh.");
        }

        StandardMaterial3D material = source.Duplicate() as StandardMaterial3D
            ?? throw new InvalidOperationException(
                "NetworkPlayer3D could not duplicate its presentation material.");
        material.AlbedoColor = OwnerColors[paletteIndex];
        material.EmissionEnabled = true;
        material.Emission = OwnerColors[paletteIndex] * 0.18f;
        mesh.MaterialOverride = material;
    }
}
