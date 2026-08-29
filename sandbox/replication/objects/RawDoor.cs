using System;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Objects.Components.Authority;
using GameFactory.Networking.Objects.Components.Replication;
using GameFactory.Networking.Peers;

namespace GameFactory.Sandbox.Replication;

public partial class RawDoor : Node3D
{
    [Replicated]
    public bool IsOpen { get; set; }

    /// <summary>Authoritative state revision for acceptance diagnostics; not an event stream.</summary>
    [Replicated]
    public long Revision { get; set; }

    private NetworkObject _network = null!;
    private INetworkReplication _replication = null!;
    private INetworkAuthority _authority = null!;
    private long _lastAppliedRevision = -1;

    public event Action<NetworkObjectId, long>? AuthorityUpdated;
    public event Action<NetworkObjectId, long, PeerId>? ConfirmationAcknowledged;

    public override void _UnhandledInput(
        InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey key)
            return;

        if (!key.Pressed || key.Echo)
            return;

        if (key.Keycode != Key.E)
            return;

        if (!Multiplayer.HasMultiplayerPeer())
        {
            GameLog.Warning("gameplay.replication", "request_rejected", "No multiplayer peer is available.");

            return;
        }

        // For B1 we only test:
        //
        // client -> server
        //
        // The host-side player case comes later.
        if (_authority.HasAuthority)
        {
            GameLog.Info("gameplay.replication", "host_request_not_needed", "The authority already owns this door.");

            return;
        }

        GameLog.Info("gameplay.replication", "open_requested", fields: new System.Collections.Generic.Dictionary<string, string?>
        {
            ["network_object_id"] = _network.Id.ToString(), ["is_open"] = IsOpen.ToString(), ["local_peer_id"] = Multiplayer.GetUniqueId().ToString()
        });

        RpcId(
            1,
            MethodName.RequestOpen);
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode =
            MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestOpen()
    {
        int senderId = Multiplayer.GetRemoteSenderId();

        GameLog.Info("gameplay.replication", "open_request_received", fields: new System.Collections.Generic.Dictionary<string, string?>
        {
            ["network_object_id"] = _network.Id.ToString(), ["sender_peer_id"] = senderId.ToString()
        });

        if (!_authority.HasAuthority)
        {
            GameLog.Warning("gameplay.replication", "request_rejected", "Open request executed on a non-authoritative peer.");

            return;
        }

        if (IsOpen)
        {
            GameLog.Info("gameplay.replication", "open_request_ignored", "Door is already open.");

            return;
        }

        CommitAuthorityState(isOpen: true);
    }

    private void CommitAuthorityState(bool isOpen)
    {
        IsOpen = isOpen;
        Revision++;

        GameLog.Info("gameplay.replication", "authority_update", fields: new System.Collections.Generic.Dictionary<string, string?>
        {
            ["network_object_id"] = _network.Id.ToString(), ["revision"] = Revision.ToString(), ["is_open"] = IsOpen.ToString()
        });
        AuthorityUpdated?.Invoke(_network.Id, Revision);
    }

    public void SetOpenOnAuthority(bool isOpen)
    {
        if (!_authority.HasAuthority)
        {
            throw new InvalidOperationException("Only the authoritative server can change the probe door state.");
        }

        CommitAuthorityState(isOpen);
    }

    public override void _Ready()
    {
        _network = GetNode<NetworkObject>("NetworkObject");

        _replication =
            _network.GetComponent<INetworkReplication>();

        _authority =
            _network.GetComponent<INetworkAuthority>();

        GameLog.Info("gameplay.replication", "door_network_ready", fields: new System.Collections.Generic.Dictionary<string, string?>
        {
            ["network_object_id"] = _network.Id.ToString(), ["authority_peer_id"] = _authority.AuthorityPeerId.ToString(), ["has_authority"] = _authority.HasAuthority.ToString()
        });

        _replication.Synchronized += () =>
        {
            LogReplicationObservation("full_sync");
        };

        _replication.DeltaSynchronized += () =>
        {
            LogReplicationObservation("delta_sync");
        };

        GameLog.Info("gameplay.world", "door_ready", fields: new System.Collections.Generic.Dictionary<string, string?>
        {
            ["network_object_id"] = _network.Id.ToString(),
            ["owner_peer_id"] = _network.OwnerPeerId.ToString(),
            ["is_open"] = IsOpen.ToString(), ["revision"] = Revision.ToString(), ["local_peer_id"] = Multiplayer.GetUniqueId().ToString()
        });
    }

    private void LogReplicationObservation(string eventName)
    {
        GameLog.Info("gameplay.replication", eventName, fields: new System.Collections.Generic.Dictionary<string, string?>
        {
            ["network_object_id"] = _network.Id.ToString(),
            ["is_open"] = IsOpen.ToString(),
            ["revision"] = Revision.ToString(),
            ["local_peer_id"] = Multiplayer.GetUniqueId().ToString()
        });

        if (_authority.HasAuthority || Revision == _lastAppliedRevision) return;
        _lastAppliedRevision = Revision;
        long localPeerId = Multiplayer.GetUniqueId();
        GameLog.Info("gameplay.replication", "applied", fields: new System.Collections.Generic.Dictionary<string, string?>
        {
            ["network_object_id"] = _network.Id.ToString(), ["revision"] = Revision.ToString(), ["is_open"] = IsOpen.ToString(), ["local_peer_id"] = localPeerId.ToString()
        });
        if (localPeerId > 0)
            RpcId(PeerId.Server.Value, MethodName.AcknowledgeRevisionRpc, _network.Id.Value, Revision);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void AcknowledgeRevisionRpc(long networkObjectId, long revision)
    {
        if (!_authority.HasAuthority || networkObjectId != _network.Id.Value) return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (sender <= 0 || sender == PeerId.Server.Value) return;
        ConfirmationAcknowledged?.Invoke(_network.Id, revision, new PeerId(sender));
    }
}
