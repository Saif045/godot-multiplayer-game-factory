using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Netfox.Player3D;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Objects.Components.Replication;
using GameFactory.Networking.Peers;

namespace GameFactory.Gameplay.Gas;

/// <summary>
/// Narrow owner-request/server-execution boundary for the first networked GAS
/// slice. GAS remains outside Netfox rollback: only an authoritative health
/// projection is replicated by the player's normal NetworkObject component.
/// </summary>
public partial class NetworkGasComponent : Node
{
    private const string SelfDamageAction = "self_damage";

    private NetworkPlayer3D _playerHost = null!;
    private NetworkObject _player = null!;
    private INetworkReplication _replication = null!;
    private GodotGasAdapter _gas = null!;
    private Label3D _healthLabel = null!;
    private float _lastAppliedHealth = float.NaN;

    public override void _Ready()
    {
        _playerHost = GetParent<NetworkPlayer3D>();
        _player = _playerHost.GetNetworkObject();
        _replication = _player.GetComponent<INetworkReplication>();
        _replication.Synchronized += OnReplicated;
        _replication.DeltaSynchronized += OnReplicated;
        _gas = GodotGasAdapter.Create(this);
        _healthLabel = CreateHealthLabel();

        if (Multiplayer.IsServer())
        {
            PublishAuthoritativeSnapshot("initial_state");
        }
        else
        {
            ApplyReplicatedSnapshot("ready");
        }
    }

    public override void _ExitTree()
    {
        if (_replication is not null)
        {
            _replication.Synchronized -= OnReplicated;
            _replication.DeltaSynchronized -= OnReplicated;
        }
    }

    public override void _Process(double delta)
    {
        if (!IsLocalOwner() || !Input.IsActionJustPressed(SelfDamageAction))
            return;

        Log("activation_requested", new Dictionary<string, string?>
        {
            ["ability"] = "self_damage"
        });

        if (Multiplayer.IsServer())
        {
            HandleActivation(PeerId.Server);
            return;
        }

        RpcId(PeerId.Server.Value, MethodName.RequestSelfDamageRpc);
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestSelfDamageRpc()
    {
        if (!Multiplayer.IsServer())
            return;

        long sender = Multiplayer.GetRemoteSenderId();
        if (sender <= 0)
        {
            Log("activation_rejected", new Dictionary<string, string?>
            {
                ["reason"] = "invalid_rpc_sender"
            });
            return;
        }

        HandleActivation(new PeerId(sender));
    }

    private void HandleActivation(PeerId sender)
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may activate SelfDamage.");

        if (_player.OwnerPeerId != sender)
        {
            Log("activation_rejected", new Dictionary<string, string?>
            {
                ["requesting_peer_id"] = sender.ToString(),
                ["reason"] = "sender_is_not_player_owner"
            });
            return;
        }

        Log("activation_accepted", new Dictionary<string, string?>
        {
            ["requesting_peer_id"] = sender.ToString(),
            ["ability"] = "self_damage"
        });
        _gas.ApplySelfDamage();
        PublishAuthoritativeSnapshot("self_damage");
    }

    private void PublishAuthoritativeSnapshot(string reason)
    {
        GasSnapshot snapshot = _gas.CaptureSnapshot();
        _playerHost.GasHealth = snapshot.Health;
        UpdateHealthLabel(snapshot.Health);
        Log("authoritative_snapshot", new Dictionary<string, string?>
        {
            ["health"] = snapshot.Health.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["reason"] = reason
        });
    }

    private void OnReplicated()
    {
        if (!Multiplayer.IsServer())
            ApplyReplicatedSnapshot("replicated");
    }

    private void ApplyReplicatedSnapshot(string source)
    {
        GasSnapshot snapshot = new(_playerHost.GasHealth);
        if (Mathf.IsEqualApprox(_lastAppliedHealth, snapshot.Health))
            return;

        _gas.ApplySnapshot(snapshot);
        _lastAppliedHealth = snapshot.Health;
        UpdateHealthLabel(snapshot.Health);
        Log("replicated_snapshot_applied", new Dictionary<string, string?>
        {
            ["health"] = snapshot.Health.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["source"] = source
        });
    }

    private bool IsLocalOwner() =>
        _player.IsBound && _player.OwnerPeerId.Value == Multiplayer.GetUniqueId();

    private Label3D CreateHealthLabel()
    {
        Label3D label = new()
        {
            Name = "GasHealthLabel",
            Position = new Vector3(0, 1.35f, 0),
            FontSize = 64,
            OutlineSize = 8,
            Modulate = new Color("f8f9fa"),
            Text = "HP 100"
        };
        _playerHost.GetNode<Node3D>("Presentation").AddChild(label);
        return label;
    }

    private void UpdateHealthLabel(float health)
    {
        _healthLabel.Text = $"HP {Mathf.RoundToInt(health)}";
    }

    private void Log(string eventName, IReadOnlyDictionary<string, string?> fields)
    {
        Dictionary<string, string?> values = new(fields)
        {
            ["player_network_object_id"] = _player.IsBound ? _player.Id.ToString() : null,
            ["owner_peer_id"] = _player.IsBound ? _player.OwnerPeerId.ToString() : null,
            ["local_peer_id"] = Multiplayer.GetUniqueId().ToString(),
            ["role"] = Multiplayer.IsServer() ? "host" : "client"
        };
        GameLog.Info("gas.network", eventName, fields: values);
    }
}
