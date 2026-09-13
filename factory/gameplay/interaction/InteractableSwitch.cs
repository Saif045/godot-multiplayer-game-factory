using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Objects.Components.Replication;

namespace GameFactory.Gameplay.Interaction;

/// <summary>
/// Minimal replicated interaction target. The server toggles <see cref="IsOn"/>
/// while every peer derives its material and light from that replicated state.
/// </summary>
public partial class InteractableSwitch : Node3D, IInteractable, INetworkSpawnInitializable
{
    [Replicated(ReplicationMode.OnChange)]
    public bool IsOn { get; set; }

    private NetworkObject _networkObject = null!;
    private INetworkReplication _replication = null!;
    private MeshInstance3D _mesh = null!;
    private OmniLight3D _light = null!;
    private StandardMaterial3D _material = null!;

    public void ApplyNetworkSpawnData(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary)
            throw new InvalidOperationException("InteractableSwitch spawn data must be a Dictionary.");

        Godot.Collections.Dictionary values = data.AsGodotDictionary();
        if (!values.ContainsKey("spawn_position"))
            throw new InvalidOperationException("InteractableSwitch spawn data is missing spawn_position.");

        Position = values["spawn_position"].AsVector3();
    }

    public override void _Ready()
    {
        _networkObject = GetNode<NetworkObject>("NetworkObject");
        _replication = _networkObject.GetComponent<INetworkReplication>();
        _mesh = GetNode<MeshInstance3D>("Mesh");
        _light = GetNode<OmniLight3D>("OmniLight3D");
        _material = ((StandardMaterial3D)_mesh.GetActiveMaterial(0)).Duplicate() as StandardMaterial3D
            ?? throw new InvalidOperationException("InteractableSwitch requires a StandardMaterial3D.");
        _mesh.SetSurfaceOverrideMaterial(0, _material);
        _replication.Synchronized += OnReplicated;
        _replication.DeltaSynchronized += OnReplicated;
        ApplyVisuals("ready");
    }

    public override void _ExitTree()
    {
        if (_replication is not null)
        {
            _replication.Synchronized -= OnReplicated;
            _replication.DeltaSynchronized -= OnReplicated;
        }
    }

    public bool CanInteract(InteractionContext context) => true;

    public void Interact(InteractionContext context)
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may mutate an InteractableSwitch.");

        IsOn = !IsOn;
        ApplyVisuals("authority_change");
        GameLog.Info("interaction.switch", "state_changed", fields: new Dictionary<string, string?>
        {
            ["network_object_id"] = _networkObject.Id.ToString(),
            ["is_on"] = IsOn.ToString(),
            ["requesting_peer_id"] = context.RequestingPeerId.ToString(),
            ["role"] = "host"
        });
    }

    private void OnReplicated() => ApplyVisuals("replicated");

    private void ApplyVisuals(string source)
    {
        if (_material is null)
            return;

        _material.AlbedoColor = IsOn
            ? new Color("ffd166")
            : new Color("27313d");
        _material.EmissionEnabled = IsOn;
        _material.Emission = IsOn
            ? new Color("ff9f1c")
            : Colors.Black;
        _light.Visible = IsOn;

        if (_networkObject is not null && _networkObject.IsBound)
        {
            GameLog.Info("interaction.switch", "visual_applied", fields: new Dictionary<string, string?>
            {
                ["network_object_id"] = _networkObject.Id.ToString(),
                ["is_on"] = IsOn.ToString(),
                ["source"] = source,
                ["role"] = Multiplayer.IsServer() ? "host" : "client"
            });
        }
    }
}
