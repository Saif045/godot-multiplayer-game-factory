using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Gameplay.Carry;

/// <summary>
/// Player-local carry capacity and the owner-to-server drop request boundary.
/// The carried item remains server-owned; this component never transfers
/// network authority to the player.
/// </summary>
public partial class PlayerCarrier : Node
{
    [Export]
    public Vector3 DropOffset { get; set; } = new(0, 0.5f, -1.25f);

    private NetworkObject _player = null!;
    private Node3D _playerHost = null!;
    private Node3D _carryAnchor = null!;
    private long _carriedItemId;

    public bool HasCarriedItem => _carriedItemId > 0;
    internal bool CanReceiveRetrievedItem => !HasCarriedItem;

    public Node3D CarryAnchor => _carryAnchor;

    public override void _Ready()
    {
        _playerHost = GetParent<Node3D>();
        _player = _playerHost.GetNode<NetworkObject>("NetworkObject");
        _carryAnchor = _playerHost.GetNode<Node3D>("Presentation/CarryAnchor");
    }

    public override void _Process(double delta)
    {
        if (!IsLocalOwner() || !Input.IsActionJustPressed("drop_item") ||
            !HasHeldItemLocally())
            return;

        if (Multiplayer.IsServer())
        {
            HandleDrop(PeerId.Server);
            return;
        }

        RpcId(PeerId.Server.Value, MethodName.RequestDropRpc);
    }

    public override void _ExitTree()
    {
        if (Multiplayer.IsServer() && HasCarriedItem)
            HandleDrop(PeerId.Server, "carrier_exit");
    }

    public bool TryPickup(CarryableItem item)
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may assign a carried item.");

        if (HasCarriedItem)
        {
            Log("rejected", item, "carrier_already_holding");
            return false;
        }

        if (!item.TryPickUp(_player))
            return false;

        _carriedItemId = item.GetNetworkObject().Id.Value;
        return true;
    }

    internal void ClearStoredItem(CarryableItem item)
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may clear a stored carried item.");
        if (_carriedItemId == item.GetNetworkObject().Id.Value)
            _carriedItemId = 0;
    }

    internal void SetRetrievedItem(CarryableItem item)
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may assign a retrieved carried item.");
        if (HasCarriedItem)
            throw new InvalidOperationException("Cannot retrieve into an occupied carrier.");
        _carriedItemId = item.GetNetworkObject().Id.Value;
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestDropRpc()
    {
        if (!Multiplayer.IsServer())
            return;

        long sender = Multiplayer.GetRemoteSenderId();
        if (sender <= 0)
        {
            Log("rejected", null, "invalid_rpc_sender");
            return;
        }

        HandleDrop(new PeerId(sender));
    }

    private void HandleDrop(PeerId sender, string source = "request")
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may drop a carried item.");

        if (_player.OwnerPeerId != sender)
        {
            Log("rejected", null, "sender_is_not_player_owner");
            return;
        }

        if (!HasCarriedItem)
        {
            Log("rejected", null, "player_not_carrying");
            return;
        }

        NetworkObjectId itemId = new(_carriedItemId);
        if (!_player.World.TryGet(itemId, out NetworkObject? itemObject) ||
            itemObject?.Host is not CarryableItem item)
        {
            _carriedItemId = 0;
            Log("rejected", null, "carried_item_missing");
            return;
        }

        Transform3D dropTransform = _playerHost.GlobalTransform;
        dropTransform.Origin += dropTransform.Basis * DropOffset;
        if (!item.TryDrop(_player, dropTransform))
        {
            Log("rejected", item, "item_holder_mismatch");
            return;
        }

        _carriedItemId = 0;
        Log("dropped", item, source);
    }

    private bool IsLocalOwner() =>
        _player.IsBound && _player.OwnerPeerId.Value == Multiplayer.GetUniqueId();

    private bool HasHeldItemLocally()
    {
        if (HasCarriedItem)
            return true;

        foreach (NetworkObject networkObject in _player.World.Objects)
        {
            if (networkObject.Host is CarryableItem item &&
                item.HolderNetworkObjectId == _player.Id.Value)
                return true;
        }

        return false;
    }

    private void Log(string eventName, CarryableItem? item, string reason)
    {
        Dictionary<string, string?> fields = new()
        {
            ["player_network_object_id"] = _player.IsBound ? _player.Id.ToString() : null,
            ["item_network_object_id"] = item?.GetNetworkObject().Id.ToString(),
            ["reason"] = reason,
            ["role"] = Multiplayer.IsServer() ? "host" : "client"
        };
        GameLog.Info("carry", eventName, fields: fields);
    }
}
