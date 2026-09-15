using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Objects.Components.Replication;
using GameFactory.Networking.Peers;

namespace GameFactory.Gameplay.Carry;

/// <summary>
/// Server-owned world item. Replicated holder state selects a local
/// presentation anchor; only a world/drop transform is replicated.
/// </summary>
public partial class CarryableItem : Node3D, IInteractable, INetworkSpawnInitializable
{
    public const int WorldState = 0;
    public const int CarriedState = 1;
    public const int StoredState = 2;
    public const int EquippedState = 3;

    [Replicated(ReplicationMode.OnChange)]
    public long HolderNetworkObjectId { get; set; }

    [Replicated(ReplicationMode.OnChange)]
    public int StorageState { get; set; } = WorldState;

    [Replicated(ReplicationMode.OnChange)]
    public Transform3D WorldTransform { get; set; } = Transform3D.Identity;

    private NetworkObject _networkObject = null!;
    private INetworkReplication _replication = null!;
    private CollisionShape3D _collision = null!;
    private bool _pendingHolderResolution;
    private double _followSampleElapsed;
    private Vector3 _lastAnchorPosition;
    private bool _hasAnchorSample;

    public void ApplyNetworkSpawnData(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary)
            throw new InvalidOperationException("CarryableItem spawn data must be a Dictionary.");

        Godot.Collections.Dictionary values = data.AsGodotDictionary();
        if (!values.ContainsKey("spawn_transform"))
            throw new InvalidOperationException("CarryableItem spawn data is missing spawn_transform.");

        WorldTransform = values["spawn_transform"].AsTransform3D();
        GlobalTransform = WorldTransform;
    }

    public override void _Ready()
    {
        SetMultiplayerAuthority((int)PeerId.Server.Value, recursive: false);
        _networkObject = GetNode<NetworkObject>("NetworkObject");
        _replication = _networkObject.GetComponent<INetworkReplication>();
        _collision = GetNode<CollisionShape3D>("CollisionShape3D");
        _replication.Synchronized += OnReplicated;
        _replication.DeltaSynchronized += OnReplicated;
        ApplyReplicatedState("ready");
    }

    public override void _ExitTree()
    {
        if (_replication is null)
            return;

        _replication.Synchronized -= OnReplicated;
        _replication.DeltaSynchronized -= OnReplicated;
    }

    public override void _Process(double delta)
    {
        if (StorageState != CarriedState)
            return;

        if (TryResolveCarrier(out PlayerCarrier carrier))
        {
            GlobalTransform = carrier.CarryAnchor.GlobalTransform;
            ObserveFollow(carrier);
            if (_pendingHolderResolution)
            {
                _pendingHolderResolution = false;
                Log("state_applied", "holder_resolved");
            }
        }
        else
        {
            _pendingHolderResolution = true;
        }
    }

    public bool CanInteract(InteractionContext context)
    {
        return StorageState == WorldState && HolderNetworkObjectId == 0 &&
               context.Player.Host.GetNodeOrNull<PlayerCarrier>("PlayerCarrier") is { HasCarriedItem: false };
    }

    public void Interact(InteractionContext context)
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may mutate a CarryableItem.");

        PlayerCarrier? carrier = context.Player.Host.GetNodeOrNull<PlayerCarrier>("PlayerCarrier");
        if (carrier is null)
        {
            Log("rejected", "player_missing_carrier");
            return;
        }

        carrier.TryPickup(this);
    }

    internal NetworkObject GetNetworkObject() => _networkObject;

    internal bool TryPickUp(NetworkObject holder)
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may assign a CarryableItem holder.");

        if (HolderNetworkObjectId != 0)
        {
            Log("rejected", "item_already_held");
            return false;
        }

        HolderNetworkObjectId = holder.Id.Value;
        StorageState = CarriedState;
        ApplyReplicatedState("authority_change");
        Log("picked_up", "authority_change", holder.Id.Value);
        return true;
    }

    internal bool TryDrop(NetworkObject holder, Transform3D dropTransform)
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may drop a CarryableItem.");

        if (StorageState != CarriedState || HolderNetworkObjectId != holder.Id.Value)
            return false;

        HolderNetworkObjectId = 0;
        StorageState = WorldState;
        WorldTransform = dropTransform;
        ApplyReplicatedState("authority_change");
        return true;
    }

    internal bool TryStore(NetworkObject holder)
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may store a CarryableItem.");
        if (StorageState != CarriedState || HolderNetworkObjectId != holder.Id.Value)
            return false;

        HolderNetworkObjectId = 0;
        StorageState = StoredState;
        ApplyReplicatedState("authority_change");
        Log("item_stored", "authority_change", holder.Id.Value);
        return true;
    }

    internal bool TryRetrieve(NetworkObject holder)
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may retrieve a CarryableItem.");
        if (StorageState != StoredState || HolderNetworkObjectId != 0)
            return false;

        HolderNetworkObjectId = holder.Id.Value;
        StorageState = CarriedState;
        ApplyReplicatedState("authority_change");
        Log("item_retrieved", "authority_change", holder.Id.Value);
        return true;
    }

    internal bool TryEquip()
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may equip a CarryableItem.");
        if (StorageState != StoredState || HolderNetworkObjectId != 0)
            return false;

        StorageState = EquippedState;
        ApplyReplicatedState("authority_change");
        Log("item_equipped", "authority_change");
        return true;
    }

    internal bool TryUnequip()
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may unequip a CarryableItem.");
        if (StorageState != EquippedState || HolderNetworkObjectId != 0)
            return false;

        StorageState = StoredState;
        ApplyReplicatedState("authority_change");
        Log("item_unequipped", "authority_change");
        return true;
    }

    private void OnReplicated() => ApplyReplicatedState("replicated");

    private void ApplyReplicatedState(string source)
    {
        if (StorageState == WorldState)
        {
            Visible = true;
            GlobalTransform = WorldTransform;
            _collision.SetDeferred(CollisionShape3D.PropertyName.Disabled, false);
            AddToGroup("interactable");
            _pendingHolderResolution = false;
            _hasAnchorSample = false;
            Log("state_applied", source);
            return;
        }

        if (StorageState is StoredState or EquippedState)
        {
            Visible = false;
            _collision.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
            RemoveFromGroup("interactable");
            _pendingHolderResolution = false;
            _hasAnchorSample = false;
            Log("state_applied", source);
            return;
        }

        Visible = true;
        _collision.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        RemoveFromGroup("interactable");
        if (TryResolveCarrier(out PlayerCarrier carrier))
        {
            GlobalTransform = carrier.CarryAnchor.GlobalTransform;
            _pendingHolderResolution = false;
            Log("state_applied", source);
        }
        else
        {
            _pendingHolderResolution = true;
        }
    }

    private bool TryResolveCarrier(out PlayerCarrier carrier)
    {
        carrier = null!;
        if (HolderNetworkObjectId <= 0 ||
            !_networkObject.World.TryGet(new NetworkObjectId(HolderNetworkObjectId), out NetworkObject? holder) ||
            holder?.Host is not Node holderHost)
            return false;

        PlayerCarrier? resolved = holderHost.GetNodeOrNull<PlayerCarrier>("PlayerCarrier");
        if (resolved is null)
            return false;

        carrier = resolved;
        return true;
    }

    private void ObserveFollow(PlayerCarrier carrier)
    {
        _followSampleElapsed += GetProcessDeltaTime();
        if (_followSampleElapsed < .5)
            return;

        _followSampleElapsed = 0;
        Vector3 anchorPosition = carrier.CarryAnchor.GlobalPosition;
        float anchorDelta = _hasAnchorSample
            ? anchorPosition.DistanceTo(_lastAnchorPosition)
            : 0;
        _lastAnchorPosition = anchorPosition;
        _hasAnchorSample = true;
        float anchorDistance = GlobalPosition.DistanceTo(anchorPosition);
        if (anchorDelta <= .05f || anchorDistance > .05f)
            return;

        GameLog.Info("carry", "follow_observed", fields: new Dictionary<string, string?>
        {
            ["item_network_object_id"] = _networkObject.Id.ToString(),
            ["holder_network_object_id"] = HolderNetworkObjectId.ToString(),
            ["anchor_distance"] = anchorDistance.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["anchor_delta"] = anchorDelta.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["role"] = Multiplayer.IsServer() ? "host" : "client"
        });
    }

    private void Log(string eventName, string source, long? holderId = null)
    {
        if (_networkObject is null || !_networkObject.IsBound)
            return;

        GameLog.Info("carry", eventName, fields: new Dictionary<string, string?>
        {
            ["item_network_object_id"] = _networkObject.Id.ToString(),
            ["holder_network_object_id"] = (holderId ?? HolderNetworkObjectId).ToString(),
            ["source"] = source,
            ["role"] = Multiplayer.IsServer() ? "host" : "client"
        });
    }
}
