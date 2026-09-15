using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Gameplay.Carry;
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
    private const string FortifyAction = "fortify";
    private const string SpeedBoostAction = "speed_boost";
    private const string DashAction = "dash";

    private NetworkPlayer3D _playerHost = null!;
    private NetworkObject _player = null!;
    private INetworkReplication _replication = null!;
    private GodotGasAdapter _gas = null!;
    private Label3D _healthLabel = null!;
    private GasSnapshot? _lastAppliedSnapshot;
    private float _lastAppliedMoveSpeed = float.NaN;
    private bool? _lastSprintIntent;
    private long? _lastObservedDashAuthorizationRevision;
    private string? _lastEffectiveMovementSignature;
    private double _cooldownReplicationElapsed;

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
        if (Multiplayer.IsServer())
        {
            UpdateAuthoritativeSprintIntent();
            RefreshAuthoritativeCooldownProjection(delta);
        }

        if (Multiplayer.IsServer() && _gas.ConsumeLifecycleChange())
        {
            PublishAuthoritativeSnapshot("effect_lifecycle_changed");
        }

        LogEffectiveNetfoxMovementProjection();

        if (!IsLocalOwner())
            return;

        if (Input.IsActionJustPressed(SelfDamageAction))
            RequestSelfDamage();

        if (Input.IsActionJustPressed(FortifyAction))
            RequestFortify();
        if (Input.IsActionJustPressed(SpeedBoostAction))
            RequestSpeedBoost();
        if (Input.IsActionJustPressed(DashAction))
            RequestDash();
    }

    private void RefreshAuthoritativeCooldownProjection(double delta)
    {
        GasSnapshot snapshot = _gas.CaptureSnapshot();
        if (snapshot.FortifyCooldownRemaining <= 0f && snapshot.DashCooldownRemaining <= 0f)
        {
            _cooldownReplicationElapsed = 0d;
            return;
        }

        _cooldownReplicationElapsed += delta;
        if (_cooldownReplicationElapsed < 0.25d)
            return;

        _cooldownReplicationElapsed = 0d;
        PublishAuthoritativeSnapshot("cooldown_sample");
    }

    private void UpdateAuthoritativeSprintIntent()
    {
        bool sprintHeld = _playerHost.GetNode<Node>("Input").Get("sprint_held").AsBool();
        _gas.SetSprintIntent(sprintHeld);
        if (_lastSprintIntent == sprintHeld)
            return;

        _lastSprintIntent = sprintHeld;
        Log("sprint_input_state", new Dictionary<string, string?> { ["sprint_held"] = sprintHeld.ToString() });
    }

    private void LogEffectiveNetfoxMovementProjection()
    {
        bool sprintHeld = _playerHost.GetNode<Node>("Input").Get("sprint_held").AsBool();
        bool sprintAllowed = _playerHost.GasIsSprinting;
        float effectiveSpeed = _playerHost.GasMoveSpeed * (sprintHeld && sprintAllowed ? 1.5f : 1f);
        string signature = $"{sprintHeld}:{sprintAllowed}:{_playerHost.GasIsExhausted}:{effectiveSpeed:F3}";
        if (_lastEffectiveMovementSignature == signature)
            return;

        _lastEffectiveMovementSignature = signature;
        Log("netfox_effective_move_speed", new Dictionary<string, string?>
        {
            ["sprint_held"] = sprintHeld.ToString(),
            ["sprint_allowed"] = sprintAllowed.ToString(),
            ["is_exhausted"] = _playerHost.GasIsExhausted.ToString(),
            ["effective_move_speed"] = effectiveSpeed.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
        });
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

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestFortifyRpc()
    {
        if (!Multiplayer.IsServer())
            return;

        long sender = Multiplayer.GetRemoteSenderId();
        if (sender <= 0)
        {
            Log("activation_rejected", new Dictionary<string, string?>
            {
                ["ability"] = "fortify",
                ["reason"] = "invalid_rpc_sender"
            });
            return;
        }

        HandleFortifyActivation(new PeerId(sender));
    }

    private void RequestSelfDamage()
    {
        Log("activation_requested", new Dictionary<string, string?>
        {
            ["ability"] = "self_damage"
        });

        if (Multiplayer.IsServer())
            HandleActivation(PeerId.Server);
        else
            RpcId(PeerId.Server.Value, MethodName.RequestSelfDamageRpc);
    }

    private void RequestFortify()
    {
        Log("activation_requested", new Dictionary<string, string?>
        {
            ["ability"] = "fortify"
        });

        if (Multiplayer.IsServer())
            HandleFortifyActivation(PeerId.Server);
        else
            RpcId(PeerId.Server.Value, MethodName.RequestFortifyRpc);
    }

    private void RequestSpeedBoost()
    {
        Log("activation_requested", new Dictionary<string, string?> { ["ability"] = "speed_boost" });
        if (Multiplayer.IsServer()) HandleSpeedBoost(PeerId.Server);
        else RpcId(PeerId.Server.Value, MethodName.RequestSpeedBoostRpc);
    }

    private void RequestDash()
    {
        // The Input child separately queues this exact edge into Netfox. This
        // request is only the discrete server validation/cost boundary.
        Log("dash_input", new Dictionary<string, string?>
        {
            ["input"] = "dash_pressed",
            ["prediction"] = "queued_in_netfox_input"
        });
        Log("dash_predicted_start", new Dictionary<string, string?>
        {
            ["input"] = "dash_pressed",
            ["simulation"] = "netfox_queued_edge"
        });
        if (Multiplayer.IsServer()) HandleDash(PeerId.Server);
        else RpcId(PeerId.Server.Value, MethodName.RequestDashRpc);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestSpeedBoostRpc()
    {
        if (!Multiplayer.IsServer()) return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (sender > 0) HandleSpeedBoost(new PeerId(sender));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestDashRpc()
    {
        if (!Multiplayer.IsServer()) return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (sender <= 0)
        {
            Log("activation_rejected", new Dictionary<string, string?>
            {
                ["ability"] = "dash",
                ["reason"] = "invalid_rpc_sender"
            });
            return;
        }
        HandleDash(new PeerId(sender));
    }

    private void HandleSpeedBoost(PeerId sender)
    {
        if (_player.OwnerPeerId != sender || !_gas.ApplySpeedBoost())
        {
            Log("activation_rejected", new Dictionary<string, string?> { ["ability"] = "speed_boost", ["reason"] = "not_owner_or_not_activated" });
            return;
        }
        Log("activation_accepted", new Dictionary<string, string?> { ["ability"] = "speed_boost", ["requesting_peer_id"] = sender.ToString() });
        PublishAuthoritativeSnapshot("speed_boost_activated");
    }

    private void HandleDash(PeerId sender)
    {
        if (_player.OwnerPeerId != sender || !_gas.ApplyDash())
        {
            Log("activation_rejected", new Dictionary<string, string?>
            {
                ["ability"] = "dash",
                ["requesting_peer_id"] = sender.ToString(),
                ["reason"] = "not_owner_or_gas_gate",
                ["stamina"] = _gas.GetStamina().ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                ["dash_cooldown_remaining"] = _gas.GetDashCooldownRemaining().ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
            });
            return;
        }

        _playerHost.GasDashAuthorizationRevision++;
        _gas.ConsumeLifecycleChange();
        Log("activation_accepted", new Dictionary<string, string?>
        {
            ["ability"] = "dash",
            ["requesting_peer_id"] = sender.ToString(),
            ["authorization_revision"] = _playerHost.GasDashAuthorizationRevision.ToString(),
            ["stamina"] = _gas.GetStamina().ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["dash_cooldown_remaining"] = _gas.GetDashCooldownRemaining().ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
        });
        PublishAuthoritativeSnapshot("dash_activated");
    }

    internal bool TryApplyEquipmentCubeCapability(CarryableItem item)
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may grant equipment GAS capabilities.");
        if (!_gas.ApplyEquipmentCubeCapability(item))
            return false;

        _gas.ConsumeLifecycleChange();
        PublishAuthoritativeSnapshot("equipment_cube_grant");
        return true;
    }

    internal bool TryRemoveEquipmentCubeCapability(CarryableItem item)
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may remove equipment GAS capabilities.");
        if (!_gas.RemoveEquipmentCubeCapability(item))
            return false;

        _gas.ConsumeLifecycleChange();
        PublishAuthoritativeSnapshot("equipment_cube_remove");
        return true;
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

    private void HandleFortifyActivation(PeerId sender)
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the server may activate Fortify.");

        if (_player.OwnerPeerId != sender)
        {
            Log("activation_rejected", new Dictionary<string, string?>
            {
                ["ability"] = "fortify",
                ["requesting_peer_id"] = sender.ToString(),
                ["reason"] = "sender_is_not_player_owner"
            });
            return;
        }

        if (!_gas.ApplyFortify())
        {
            Log("activation_rejected", new Dictionary<string, string?>
            {
                ["ability"] = "fortify",
                ["requesting_peer_id"] = sender.ToString(),
                ["reason"] = "ability_not_activated"
            });
            return;
        }

        _gas.ConsumeLifecycleChange();
        Log("activation_accepted", new Dictionary<string, string?>
        {
            ["ability"] = "fortify",
            ["requesting_peer_id"] = sender.ToString()
        });
        PublishAuthoritativeSnapshot("fortify_activated");
    }

    private void PublishAuthoritativeSnapshot(string reason)
    {
        GasSnapshot snapshot = _gas.CaptureSnapshot();
        _playerHost.GasHealth = snapshot.Health;
        _playerHost.GasIsFortified = snapshot.IsFortified;
        _playerHost.GasFortifyCooldownRemaining = snapshot.FortifyCooldownRemaining;
        _playerHost.GasMoveSpeed = _gas.GetMoveSpeed();
        _playerHost.GasStamina = snapshot.Stamina;
        _playerHost.GasIsExhausted = snapshot.IsExhausted;
        _playerHost.GasIsSprinting = snapshot.IsSprinting;
        _playerHost.GasDashCooldownRemaining = snapshot.DashCooldownRemaining;
        UpdateHealthLabel(snapshot);
        Log("authoritative_snapshot", new Dictionary<string, string?>
        {
            ["health"] = snapshot.Health.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["is_fortified"] = snapshot.IsFortified.ToString(),
            ["fortify_cooldown_remaining"] = snapshot.FortifyCooldownRemaining.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["move_speed"] = _playerHost.GasMoveSpeed.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["stamina"] = snapshot.Stamina.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["is_exhausted"] = snapshot.IsExhausted.ToString(),
            ["is_sprinting"] = snapshot.IsSprinting.ToString(),
            ["dash_cooldown_remaining"] = snapshot.DashCooldownRemaining.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["dash_authorization_revision"] = _playerHost.GasDashAuthorizationRevision.ToString(),
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
        GasSnapshot snapshot = new(
            _playerHost.GasHealth,
            _playerHost.GasIsFortified,
            _playerHost.GasFortifyCooldownRemaining,
            _playerHost.GasStamina,
            _playerHost.GasIsExhausted,
            _playerHost.GasIsSprinting,
            _playerHost.GasDashCooldownRemaining);
        if (_lastAppliedSnapshot is GasSnapshot previous &&
            Mathf.IsEqualApprox(previous.Health, snapshot.Health) &&
            previous.IsFortified == snapshot.IsFortified &&
            Mathf.IsEqualApprox(previous.FortifyCooldownRemaining, snapshot.FortifyCooldownRemaining) &&
            Mathf.IsEqualApprox(previous.Stamina, snapshot.Stamina) &&
            previous.IsExhausted == snapshot.IsExhausted &&
            previous.IsSprinting == snapshot.IsSprinting &&
            Mathf.IsEqualApprox(previous.DashCooldownRemaining, snapshot.DashCooldownRemaining) &&
            Mathf.IsEqualApprox(_playerHost.GasMoveSpeed, _lastAppliedMoveSpeed) &&
            _lastObservedDashAuthorizationRevision == _playerHost.GasDashAuthorizationRevision)
            return;

        _gas.ApplySnapshot(snapshot);
        _lastAppliedSnapshot = snapshot;
        _lastAppliedMoveSpeed = _playerHost.GasMoveSpeed;
        if (_lastObservedDashAuthorizationRevision != _playerHost.GasDashAuthorizationRevision)
        {
            _lastObservedDashAuthorizationRevision = _playerHost.GasDashAuthorizationRevision;
            Log("dash_authorization_replicated", new Dictionary<string, string?>
            {
                ["authorization_revision"] = _playerHost.GasDashAuthorizationRevision.ToString(),
                ["reconciliation"] = "netfox_replays_authorized_motion_from_rollback_state"
            });
        }
        UpdateHealthLabel(snapshot);
        Log("replicated_snapshot_applied", new Dictionary<string, string?>
        {
            ["health"] = snapshot.Health.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["is_fortified"] = snapshot.IsFortified.ToString(),
            ["fortify_cooldown_remaining"] = snapshot.FortifyCooldownRemaining.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["move_speed"] = _playerHost.GasMoveSpeed.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["stamina"] = snapshot.Stamina.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["is_exhausted"] = snapshot.IsExhausted.ToString(),
            ["is_sprinting"] = snapshot.IsSprinting.ToString(),
            ["dash_cooldown_remaining"] = snapshot.DashCooldownRemaining.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["dash_authorization_revision"] = _playerHost.GasDashAuthorizationRevision.ToString(),
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

    private void UpdateHealthLabel(GasSnapshot snapshot)
    {
        string fortify = snapshot.IsFortified
            ? $"FORTIFIED ({snapshot.FortifyCooldownRemaining:F1}s)"
            : snapshot.FortifyCooldownRemaining > 0f
                ? $"COOLDOWN ({snapshot.FortifyCooldownRemaining:F1}s)"
                : string.Empty;
        string dash = snapshot.DashCooldownRemaining > 0f
            ? $"DASH COOLDOWN ({snapshot.DashCooldownRemaining:F1}s)"
            : "DASH READY (X)";
        _healthLabel.Text = $"HP {Mathf.RoundToInt(snapshot.Health)}" +
            $"\nSTAMINA {Mathf.RoundToInt(snapshot.Stamina)}" +
            (snapshot.IsExhausted ? " EXHAUSTED" : snapshot.IsSprinting ? " SPRINTING" : string.Empty) +
            (string.IsNullOrEmpty(fortify) ? string.Empty : $"\n{fortify}") +
            $"\n{dash}" +
            (_playerHost.GasMoveSpeed > 6f
                ? $"\nSPEED BOOST: {_playerHost.GasMoveSpeed:F0} (x{_playerHost.GasMoveSpeed / 6f:F1})"
                : string.Empty);

        // Presentation only: GAS remains the authoritative source and Netfox
        // consumes the replicated scalar. This makes the active state obvious.
        Node3D visualRoot = _playerHost.GetNode<Node3D>("Presentation/VisualRoot");
        visualRoot.Scale = _playerHost.GasMoveSpeed > 6f
            ? Vector3.One * 1.35f
            : Vector3.One;
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
