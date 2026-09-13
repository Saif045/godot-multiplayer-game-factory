using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Gameplay.Interaction;

/// <summary>
/// Player-side bridge for a discrete, server-authoritative interaction.
/// Candidate selection is intentionally local and convenient; authorization,
/// target resolution, and range validation are always performed by the server.
/// </summary>
public partial class PlayerInteractor : Node
{
    [Export(PropertyHint.Range, "0.5,10,0.1")]
    public float InteractionRange { get; set; } = 2.75f;

    private NetworkObject _player = null!;
    private Node3D _playerHost = null!;

    public override void _Ready()
    {
        _playerHost = GetParent<Node3D>();
        _player = _playerHost.GetNode<NetworkObject>("NetworkObject");
    }

    public override void _Process(double delta)
    {
        if (!IsLocalOwner() || !Input.IsActionJustPressed("interact"))
            return;

        NetworkObject? target = FindLocalCandidate();
        if (target is null)
        {
            Log("rejected", new Dictionary<string, string?>
            {
                ["reason"] = "no_local_candidate"
            });
            return;
        }

        Log("requested", new Dictionary<string, string?>
        {
            ["target_network_object_id"] = target.Id.ToString()
        });

        if (Multiplayer.IsServer())
        {
            HandleRequest(PeerId.Server, target.Id);
            return;
        }

        RpcId(
            PeerId.Server.Value,
            MethodName.RequestInteractionRpc,
            target.Id.Value);
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestInteractionRpc(long targetId)
    {
        if (!Multiplayer.IsServer())
        {
            Log("rejected", new Dictionary<string, string?>
            {
                ["reason"] = "request_received_on_non_server"
            });
            return;
        }

        long sender = Multiplayer.GetRemoteSenderId();
        if (sender <= 0)
        {
            Log("rejected", new Dictionary<string, string?>
            {
                ["reason"] = "invalid_rpc_sender"
            });
            return;
        }

        HandleRequest(new PeerId(sender), CreateIdOrReject(targetId));
    }

    private NetworkObjectId? CreateIdOrReject(long value)
    {
        if (value > 0)
            return new NetworkObjectId(value);

        Log("rejected", new Dictionary<string, string?>
        {
            ["reason"] = "invalid_target_id",
            ["target_network_object_id"] = value.ToString()
        });
        return null;
    }

    private void HandleRequest(PeerId sender, NetworkObjectId? targetId)
    {
        if (targetId is null)
            return;

        if (_player.OwnerPeerId != sender)
        {
            Reject(sender, targetId.Value, "sender_is_not_player_owner");
            return;
        }

        if (!_player.World.TryGet(targetId.Value, out NetworkObject? target) ||
            target is null)
        {
            Reject(sender, targetId.Value, "target_not_found");
            return;
        }

        if (target.Host is not IInteractable interactable ||
            target.Host is not Node3D targetHost)
        {
            Reject(sender, targetId.Value, "target_not_interactable");
            return;
        }

        float distance = _playerHost.GlobalPosition.DistanceTo(targetHost.GlobalPosition);
        if (distance > InteractionRange)
        {
            Reject(sender, targetId.Value, "target_out_of_range", distance);
            return;
        }

        InteractionContext context = new(
            sender,
            _player,
            target,
            _playerHost.GlobalPosition);

        if (!interactable.CanInteract(context))
        {
            Reject(sender, targetId.Value, "target_disallowed", distance);
            return;
        }

        interactable.Interact(context);
        Log("accepted", new Dictionary<string, string?>
        {
            ["requesting_peer_id"] = sender.ToString(),
            ["target_network_object_id"] = targetId.Value.ToString(),
            ["server_distance"] = distance.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
        });
    }

    private NetworkObject? FindLocalCandidate()
    {
        NetworkObject? best = null;
        float bestDistance = InteractionRange;
        foreach (Node node in GetTree().GetNodesInGroup("interactable"))
        {
            if (node is not Node3D host || host is not IInteractable)
                continue;

            NetworkObject? candidate = host.GetNodeOrNull<NetworkObject>("NetworkObject");
            if (candidate is null || !candidate.IsBound)
                continue;

            float distance = _playerHost.GlobalPosition.DistanceTo(host.GlobalPosition);
            if (distance > bestDistance)
                continue;

            best = candidate;
            bestDistance = distance;
        }

        return best;
    }

    private bool IsLocalOwner() =>
        _player.IsBound &&
        _player.OwnerPeerId.Value == Multiplayer.GetUniqueId();

    private void Reject(
        PeerId sender,
        NetworkObjectId targetId,
        string reason,
        float? distance = null)
    {
        Dictionary<string, string?> fields = new()
        {
            ["requesting_peer_id"] = sender.ToString(),
            ["target_network_object_id"] = targetId.ToString(),
            ["reason"] = reason
        };
        if (distance is not null)
        {
            fields["server_distance"] = distance.Value.ToString(
                "F3",
                System.Globalization.CultureInfo.InvariantCulture);
        }
        Log("rejected", fields);
    }

    private void Log(
        string eventName,
        IReadOnlyDictionary<string, string?> fields)
    {
        Dictionary<string, string?> values = new(fields)
        {
            ["role"] = Multiplayer.IsServer() ? "host" : "client",
            ["local_peer_id"] = Multiplayer.GetUniqueId().ToString()
        };
        GameLog.Info("interaction", eventName, fields: values);
    }
}
