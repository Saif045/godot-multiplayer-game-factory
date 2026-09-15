using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Gameplay.Carry;
using GameFactory.Networking.Netfox.Player3D;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Objects.Components.Replication;
using GameFactory.Networking.Peers;

namespace GameFactory.Gameplay.Inventory;

/// <summary>
/// One server-authoritative inventory slot. It stores a stable runtime item
/// identity; it never destroys, recreates, or transfers authority of the item.
/// </summary>
public partial class PlayerInventory : Node
{
    private const string StoreAction = "store_item";
    private const string RetrieveAction = "retrieve_item";

    private NetworkObject _player = null!;
    private Node3D _playerHost = null!;
    private NetworkPlayer3D _networkPlayer = null!;
    private INetworkReplication _replication = null!;
    private Label3D _label = null!;
    private long _lastAppliedStoredItemId = long.MinValue;

    public long StoredItemNetworkObjectId => _networkPlayer.InventoryStoredItemNetworkObjectId;
    public bool HasStoredItem => StoredItemNetworkObjectId > 0;

    public override void _Ready()
    {
        _playerHost = GetParent<Node3D>();
        _networkPlayer = (NetworkPlayer3D)_playerHost;
        _player = _playerHost.GetNode<NetworkObject>("NetworkObject");
        _replication = _player.GetComponent<INetworkReplication>();
        _replication.Synchronized += OnReplicated;
        _replication.DeltaSynchronized += OnReplicated;
        _label = CreateLabel();
        ApplyReplicatedState("ready");
    }

    public override void _ExitTree()
    {
        if (_replication is not null)
        {
            _replication.Synchronized -= OnReplicated;
            _replication.DeltaSynchronized -= OnReplicated;
        }
    }

    public override void _Process(double _delta)
    {
        if (!IsLocalOwner())
            return;

        if (Input.IsActionJustPressed(StoreAction))
            RequestStore();
        if (Input.IsActionJustPressed(RetrieveAction))
            RequestRetrieve();
    }

    private void RequestStore()
    {
        Log("store_requested", new Dictionary<string, string?>());
        if (Multiplayer.IsServer())
            HandleStore(PeerId.Server);
        else
            RpcId(PeerId.Server.Value, MethodName.RequestStoreRpc);
    }

    private void RequestRetrieve()
    {
        Log("retrieve_requested", new Dictionary<string, string?>());
        if (Multiplayer.IsServer())
            HandleRetrieve(PeerId.Server);
        else
            RpcId(PeerId.Server.Value, MethodName.RequestRetrieveRpc);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestStoreRpc()
    {
        if (!Multiplayer.IsServer())
            return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (sender <= 0)
        {
            Reject("store_rejected", "invalid_rpc_sender");
            return;
        }
        HandleStore(new PeerId(sender));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestRetrieveRpc()
    {
        if (!Multiplayer.IsServer())
            return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (sender <= 0)
        {
            Reject("retrieve_rejected", "invalid_rpc_sender");
            return;
        }
        HandleRetrieve(new PeerId(sender));
    }

    private void HandleStore(PeerId sender)
    {
        if (_player.OwnerPeerId != sender)
        {
            Reject("store_rejected", "sender_is_not_player_owner");
            return;
        }
        if (HasStoredItem)
        {
            Reject("store_rejected", "inventory_slot_occupied");
            return;
        }
        if (!TryFindCarriedItem(out CarryableItem item))
        {
            Reject("store_rejected", "player_not_carrying");
            return;
        }
        if (!item.TryStore(_player))
        {
            Reject("store_rejected", "item_holder_mismatch");
            return;
        }

        SetStoredItemNetworkObjectId(item.GetNetworkObject().Id.Value);
        _playerHost.GetNode<PlayerCarrier>("PlayerCarrier").ClearStoredItem(item);
        ApplyReplicatedState("authority_change");
        Log("store_accepted", new Dictionary<string, string?>
        {
            ["item_network_object_id"] = StoredItemNetworkObjectId.ToString()
        });
    }

    private void HandleRetrieve(PeerId sender)
    {
        if (_player.OwnerPeerId != sender)
        {
            Reject("retrieve_rejected", "sender_is_not_player_owner");
            return;
        }
        if (!HasStoredItem)
        {
            Reject("retrieve_rejected", "inventory_slot_empty");
            return;
        }
        if (!TryResolveStoredItem(out CarryableItem item))
        {
            SetStoredItemNetworkObjectId(0);
            ApplyReplicatedState("stored_item_missing");
            Reject("retrieve_rejected", "stored_item_missing");
            return;
        }
        if (!_playerHost.GetNode<PlayerCarrier>("PlayerCarrier").CanReceiveRetrievedItem ||
            !item.TryRetrieve(_player))
        {
            Reject("retrieve_rejected", "carrier_not_empty_or_item_not_stored");
            return;
        }

        _playerHost.GetNode<PlayerCarrier>("PlayerCarrier").SetRetrievedItem(item);
        SetStoredItemNetworkObjectId(0);
        ApplyReplicatedState("authority_change");
        Log("retrieve_accepted", new Dictionary<string, string?>
        {
            ["item_network_object_id"] = item.GetNetworkObject().Id.ToString()
        });
    }

    private bool TryFindCarriedItem(out CarryableItem item)
    {
        foreach (NetworkObject candidate in _player.World.Objects)
        {
            if (candidate.Host is CarryableItem carryable &&
                carryable.HolderNetworkObjectId == _player.Id.Value &&
                carryable.StorageState == CarryableItem.CarriedState)
            {
                item = carryable;
                return true;
            }
        }
        item = null!;
        return false;
    }

    private bool TryResolveStoredItem(out CarryableItem item)
    {
        if (_player.World.TryGet(new NetworkObjectId(StoredItemNetworkObjectId), out NetworkObject? target) &&
            target?.Host is CarryableItem carryable &&
            carryable.StorageState == CarryableItem.StoredState)
        {
            item = carryable;
            return true;
        }
        item = null!;
        return false;
    }

    private void OnReplicated() => ApplyReplicatedState("replicated");

    private void SetStoredItemNetworkObjectId(long itemId)
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may mutate inventory contents.");
        _networkPlayer.InventoryStoredItemNetworkObjectId = itemId;
    }

    private void ApplyReplicatedState(string source)
    {
        if (_lastAppliedStoredItemId == StoredItemNetworkObjectId)
            return;
        _lastAppliedStoredItemId = StoredItemNetworkObjectId;
        _label.Text = HasStoredItem
            ? $"INVENTORY: CUBE ({StoredItemNetworkObjectId})\nR RETRIEVE"
            : "INVENTORY: EMPTY\nZ STORE";
        Log("inventory_state_applied", new Dictionary<string, string?>
        {
            ["stored_item_network_object_id"] = StoredItemNetworkObjectId.ToString(),
            ["source"] = source
        });
    }

    private Label3D CreateLabel()
    {
        Label3D label = new()
        {
            Name = "InventoryLabel",
            Position = new Vector3(0, 2.05f, 0),
            FontSize = 42,
            OutlineSize = 6,
            Modulate = new Color("d8f3dc")
        };
        _playerHost.GetNode<Node3D>("Presentation").AddChild(label);
        return label;
    }

    private bool IsLocalOwner() =>
        _player.IsBound && _player.OwnerPeerId.Value == Multiplayer.GetUniqueId();

    private void Reject(string eventName, string reason) =>
        Log(eventName, new Dictionary<string, string?> { ["reason"] = reason });

    private void Log(string eventName, IReadOnlyDictionary<string, string?> fields)
    {
        Dictionary<string, string?> values = new(fields)
        {
            ["player_network_object_id"] = _player.IsBound ? _player.Id.ToString() : null,
            ["owner_peer_id"] = _player.IsBound ? _player.OwnerPeerId.ToString() : null,
            ["role"] = Multiplayer.IsServer() ? "host" : "client"
        };
        GameLog.Info("inventory", eventName, fields: values);
    }
}
