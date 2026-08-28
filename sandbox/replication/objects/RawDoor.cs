using System;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Objects.Components.Authority;
using GameFactory.Networking.Objects.Components.Replication;

namespace GameFactory.Sandbox.Replication;

public partial class RawDoor : Node3D
{
    [Replicated]
    public bool IsOpen { get; set; }

    private NetworkObject _network = null!;
    private INetworkReplication _replication = null!;
    private INetworkAuthority _authority = null!;

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
            GD.Print(
                "[door] no multiplayer peer available");

            return;
        }

        // For B1 we only test:
        //
        // client -> server
        //
        // The host-side player case comes later.
        if (_authority.HasAuthority)
        {
            GD.Print(
                "[door][server] E pressed locally; " +
                "B1 only tests remote client requests.");

            return;
        }

        GD.Print(
            $"[door][client] requesting open; " +
            $"local IsOpen = {IsOpen}");

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

        GD.Print(
            $"[door][server] RequestOpen received " +
            $"from peer {senderId}");

        if (!_authority.HasAuthority)
        {
            GD.PushWarning(
                "[door] RequestOpen executed " +
                "on a non-server peer");

            return;
        }

        if (IsOpen)
        {
            GD.Print(
                "[door][server] door already open");

            return;
        }

        Open();
    }

    private void Open()
    {
        IsOpen = true;

        GD.Print(
            $"[door][server] door opened; " +
            $"IsOpen = {IsOpen}");
    }

    public void SetOpenOnAuthority(bool isOpen)
    {
        if (!_authority.HasAuthority)
        {
            throw new InvalidOperationException("Only the authoritative server can change the probe door state.");
        }

        IsOpen = isOpen;
        GameLog.Info("gameplay.replication", "door_mutated", fields: new System.Collections.Generic.Dictionary<string, string?>
        {
            ["network_object_id"] = _network.Id.ToString(),
            ["is_open"] = IsOpen.ToString()
        });
    }

    public override void _Ready()
    {
        _network = GetNode<NetworkObject>("NetworkObject");

        _replication =
            _network.GetComponent<INetworkReplication>();

        _authority =
            _network.GetComponent<INetworkAuthority>();

        GD.Print(
            $"[door][network] authority peer = {_authority.AuthorityPeerId}, " +
            $"has authority = {_authority.HasAuthority}");

        _replication.Synchronized += () =>
        {
            GD.Print(
                $"[door][sync] full sync received; " +
                $"IsOpen = {IsOpen}");
            LogReplicationObservation("full_sync");
        };

        _replication.DeltaSynchronized += () =>
        {
            GD.Print(
                $"[door][sync] delta received; " +
                $"IsOpen = {IsOpen}");
            LogReplicationObservation("delta_sync");
        };

        GD.Print(
            $"[door] created on peer " +
            $"{Multiplayer.GetUniqueId()}");

        GameLog.Info("gameplay.world", "door_ready", fields: new System.Collections.Generic.Dictionary<string, string?>
        {
            ["network_object_id"] = _network.Id.ToString(),
            ["owner_peer_id"] = _network.OwnerPeerId.ToString(),
            ["is_open"] = IsOpen.ToString()
        });
    }

    private void LogReplicationObservation(string eventName)
    {
        GameLog.Info("gameplay.replication", eventName, fields: new System.Collections.Generic.Dictionary<string, string?>
        {
            ["network_object_id"] = _network.Id.ToString(),
            ["is_open"] = IsOpen.ToString(),
            ["local_peer_id"] = Multiplayer.GetUniqueId().ToString()
        });
    }
}
