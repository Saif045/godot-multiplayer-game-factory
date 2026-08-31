using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Sandbox.Netfox;

/// <summary>
/// Spawned sandbox player whose GameFactory ownership metadata is intentionally
/// separate from its node-level Godot authority topology.
/// </summary>
public partial class NetfoxGameplayPlayer : Node2D, INetworkSpawnInitializable
{
    public long PlayerId { get; private set; }

    private Node _networkTime = null!;
    private Node _networkRollback = null!;
    private Callable _beforeRollbackCallable;
    private Callable _afterRollbackCallable;
    private bool _loopHasRealReplay;
    private long _loopStartTick;
    private bool _mispredictionStarted;
    private bool _mispredictionEnded;
    private bool _predictionConfirmed;
    private bool _divergenceConfirmed;
    private bool _replayObserved;
    private bool _convergenceConfirmed;
    private float _maxDivergence;
    private int _replayCount;
    private long _presentationTick = -1;
    private Vector2 _presentationPosition;
    private bool _playerInterpolationConfirmed;

    public long ScenarioStartTick { get; private set; } = -1;
    public bool AuthorityConfigured { get; private set; }
    public bool ClientEvidenceComplete => _predictionConfirmed && _divergenceConfirmed && _replayObserved && _convergenceConfirmed;
    public bool PlayerInterpolationConfirmed => _playerInterpolationConfirmed;

    public void ApplyNetworkSpawnData(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary)
            throw new InvalidOperationException("Netfox player spawn data must be a Dictionary.");

        Godot.Collections.Dictionary values = data.AsGodotDictionary();
        if (!values.ContainsKey("player_id"))
            throw new InvalidOperationException("Netfox player spawn data is missing player_id.");

        PlayerId = (long)values["player_id"];
    }

    public override void _EnterTree()
    {
        GetNode<Node>("RollbackSynchronizer").Set("root", this);
        GetNode<Node>("TickInterpolator").Set("root", this);
    }

    public override void _Ready()
    {
        _networkTime = GetNode<Node>("/root/NetworkTime");
        _networkRollback = GetNode<Node>("/root/NetworkRollback");
        _beforeRollbackCallable = Callable.From(OnRollbackStarted);
        _afterRollbackCallable = Callable.From(OnRollbackCompleted);
        _networkRollback.Connect("before_loop", _beforeRollbackCallable);
        _networkRollback.Connect("after_loop", _afterRollbackCallable);
        CallDeferred(nameof(ConfigureNetfoxAuthority));
    }

    public override void _Process(double _delta)
    {
        if (ScenarioStartTick < 0) return;

        long tick = _networkTime.Get("tick").AsInt64();
        long scenarioTick = tick - ScenarioStartTick;
        NetworkObject networkObject = GetNode<NetworkObject>("NetworkObject");
        bool isOwningClient = !Multiplayer.IsServer() &&
            networkObject.OwnerPeerId.Value == Multiplayer.GetUniqueId();

        if (!Multiplayer.IsServer() && !isOwningClient && !_playerInterpolationConfirmed)
        {
            Vector2 presentation = GetNode<Node2D>("Presentation").Position;
            if (_presentationTick == tick && presentation != _presentationPosition)
            {
                _playerInterpolationConfirmed = true;
                GameLog.Info("netfox.interpolation", "player_interpolation_confirmed", fields: new Dictionary<string, string?>
                {
                    ["role"] = "client", ["network_tick"] = tick.ToString(), ["previous_presentation_position"] = _presentationPosition.ToString(), ["current_presentation_position"] = presentation.ToString()
                });
            }
            _presentationTick = tick;
            _presentationPosition = presentation;
        }

        if (isOwningClient && !_mispredictionStarted && scenarioTick >= 70)
        {
            _mispredictionStarted = true;
            LogGameplay("misprediction_started", networkObject, tick, scenarioTick);
        }
        if (isOwningClient && !_mispredictionEnded && scenarioTick >= 90)
        {
            _mispredictionEnded = true;
            LogGameplay("misprediction_ended", networkObject, tick, scenarioTick);
        }
    }

    public override void _ExitTree()
    {
        if (_networkRollback is not null && GodotObject.IsInstanceValid(_networkRollback))
        {
            if (_networkRollback.IsConnected("before_loop", _beforeRollbackCallable))
                _networkRollback.Disconnect("before_loop", _beforeRollbackCallable);
            if (_networkRollback.IsConnected("after_loop", _afterRollbackCallable))
                _networkRollback.Disconnect("after_loop", _afterRollbackCallable);
        }
    }

    private void ConfigureNetfoxAuthority()
    {
        NetworkObject networkObject = GetNode<NetworkObject>("NetworkObject");
        Node input = GetNode<Node>("Input");
        Node simulation = GetNode<Node>("Simulation");
        Node rollbackSynchronizer = GetNode<Node>("RollbackSynchronizer");
        AssertConfiguredRoot(rollbackSynchronizer, "RollbackSynchronizer");
        AssertConfiguredRoot(GetNode<Node>("TickInterpolator"), "TickInterpolator");

        // State stays server-authoritative. Only the input property belongs to
        // the owning peer; Netfox uses the split when it records/replays ticks.
        SetMultiplayerAuthority((int)PeerId.Server.Value, recursive: false);
        simulation.SetMultiplayerAuthority((int)PeerId.Server.Value, recursive: false);
        input.SetMultiplayerAuthority((int)networkObject.OwnerPeerId.Value, recursive: false);
        rollbackSynchronizer.Call("process_settings");
        AuthorityConfigured = true;

        GameLog.Info("netfox.gameplay", "player_authority_configured", fields: new Dictionary<string, string?>
        {
            ["player_id"] = PlayerId.ToString(),
            ["network_object_id"] = networkObject.Id.ToString(),
            ["owner_peer_id"] = networkObject.OwnerPeerId.ToString(),
            ["root_multiplayer_authority"] = GetMultiplayerAuthority().ToString(),
            ["simulation_multiplayer_authority"] = simulation.GetMultiplayerAuthority().ToString(),
            ["input_multiplayer_authority"] = input.GetMultiplayerAuthority().ToString()
        });
    }

    private void AssertConfiguredRoot(Node synchronizer, string type)
    {
        Variant configuredRoot = synchronizer.Get("root");
        if (configuredRoot.VariantType != Variant.Type.Object || configuredRoot.AsGodotObject() is not Node root || root != this)
        {
            GameLog.Error("netfox.configuration", "invalid_root", fields: new Dictionary<string, string?> { ["node"] = Name, ["synchronizer"] = type, ["expected_root"] = GetPath(), ["actual_root"] = configuredRoot.ToString() });
            throw new InvalidOperationException($"{type} root was not configured to its host node.");
        }
        GameLog.Info("netfox.configuration", "configured", fields: new Dictionary<string, string?> { ["node"] = Name, ["synchronizer"] = type, ["root"] = root.GetPath() });
    }

    /// <summary>Starts the deterministic schedule at a host-selected Netfox tick.</summary>
    public void StartScenario(long startTick)
    {
        ScenarioStartTick = startTick;
        SetMeta("netfox_gameplay_scenario", true);
        SetMeta("scenario_start_tick", startTick);
        NetworkObject networkObject = GetNode<NetworkObject>("NetworkObject");
        LogGameplay("scenario_player_started", networkObject, ReadTick(), 0);
    }

    // Called by the rollback simulation. It observes state only; it never
    // feeds values back into Netfox, correction, or gameplay simulation.
    public void ObserveSimulationTick(long tick, Vector2 position, bool isFresh)
    {
        if (ScenarioStartTick < 0 || Multiplayer.IsServer()) return;
        NetworkObject networkObject = GetNode<NetworkObject>("NetworkObject");
        if (networkObject.OwnerPeerId.Value != Multiplayer.GetUniqueId()) return;
        long scenarioTick = tick - ScenarioStartTick;
        Node rollbackSynchronizer = GetNode<Node>("RollbackSynchronizer");
        long lastKnown = rollbackSynchronizer.Call("get_last_known_state").AsInt64();
        Vector2 expected = ExpectedAuthoritativePosition(scenarioTick);
        float error = position.DistanceTo(expected);
        _maxDivergence = Math.Max(_maxDivergence, error);
        if (!_predictionConfirmed && scenarioTick >= 20 && position != Vector2.Zero && tick > lastKnown)
        {
            _predictionConfirmed = true;
            LogObservation("prediction_confirmed", networkObject, tick, scenarioTick, position, expected, error, lastKnown);
        }
        if (!_divergenceConfirmed && scenarioTick >= 70 && scenarioTick < 90 && error >= 0.10f)
        {
            _divergenceConfirmed = true;
            LogObservation("divergence_confirmed", networkObject, tick, scenarioTick, position, expected, error, lastKnown);
        }
        if (_divergenceConfirmed && !isFresh)
        {
            if (!_loopHasRealReplay)
            {
                _loopHasRealReplay = true;
                GameLog.Info("netfox.rollback", "started", fields: new Dictionary<string, string?> { ["role"] = "client", ["network_object_id"] = networkObject.Id.ToString(), ["network_tick"] = tick.ToString(), ["scenario_tick"] = scenarioTick.ToString() });
            }
            _replayCount++;
            if (!_replayObserved) { _replayObserved = true; LogObservation("replay_observed", networkObject, tick, scenarioTick, position, expected, error, lastKnown); }
        }
        if (_divergenceConfirmed && _replayObserved && !_convergenceConfirmed && scenarioTick >= 90 && scenarioTick <= 130 && error <= 0.10f)
        {
            _convergenceConfirmed = true;
            LogObservation("convergence_confirmed", networkObject, tick, scenarioTick, position, expected, error, lastKnown);
        }
    }

    private Vector2 ExpectedAuthoritativePosition(long scenarioTick)
    {
        Vector2 position = Vector2.Zero;
        for (long tick = 20; tick <= Math.Min(scenarioTick, 139); tick++)
        {
            Vector2 move = tick < 60 ? Vector2.Right : tick < 100 ? Vector2.Down : Vector2.Left;
            position += move * (5f / 30f);
        }
        return position;
    }

    private void LogObservation(string eventName, NetworkObject networkObject, long tick, long scenarioTick, Vector2 actual, Vector2 expected, float error, long lastKnown)
        => GameLog.Info(eventName == "prediction_confirmed" ? "netfox.prediction" : eventName == "convergence_confirmed" ? "netfox.reconciliation" : "netfox.gameplay", eventName, fields: new Dictionary<string, string?>
        {
            ["role"] = "client", ["network_object_id"] = networkObject.Id.ToString(), ["scenario_tick"] = scenarioTick.ToString(),
            ["network_tick"] = tick.ToString(), ["predicted_position"] = actual.ToString(), ["expected_authoritative_position"] = expected.ToString(),
            ["divergence"] = error.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), ["max_divergence"] = _maxDivergence.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["last_known_authoritative_state_tick"] = lastKnown.ToString(), ["replay_count"] = _replayCount.ToString()
        });

    private void OnRollbackStarted()
    {
        _loopHasRealReplay = false;
        _loopStartTick = _networkRollback.Get("tick").AsInt64();
    }

    private void OnRollbackCompleted()
    {
        if (!_loopHasRealReplay) return;
        long endTick = _networkRollback.Get("tick").AsInt64();
        _loopHasRealReplay = false;
        NetworkObject networkObject = GetNode<NetworkObject>("NetworkObject");
        GameLog.Info("netfox.rollback", "completed", fields: new Dictionary<string, string?>
        {
            ["network_object_id"] = networkObject.Id.ToString(),
            ["owner_peer_id"] = networkObject.OwnerPeerId.ToString(),
            ["role"] = Multiplayer.IsServer() ? "host" : "client",
            ["rollback_from_tick"] = _loopStartTick.ToString(),
            ["rollback_to_tick"] = endTick.ToString(),
            ["replayed_ticks"] = Math.Max(0, endTick - _loopStartTick).ToString(),
            ["scenario_tick"] = (ScenarioStartTick < 0 ? -1 : endTick - ScenarioStartTick).ToString()
        });
    }

    private long ReadTick() => _networkTime.Get("tick").AsInt64();

    private static void LogGameplay(string eventName, NetworkObject networkObject, long tick, long scenarioTick)
        => GameLog.Info("netfox.gameplay", eventName, fields: new Dictionary<string, string?>
        {
            ["network_object_id"] = networkObject.Id.ToString(),
            ["owner_peer_id"] = networkObject.OwnerPeerId.ToString(),
            ["local_peer_id"] = networkObject.Multiplayer.GetUniqueId().ToString(),
            ["role"] = networkObject.Multiplayer.IsServer() ? "host" : "client",
            ["network_tick"] = tick.ToString(),
            ["scenario_tick"] = scenarioTick.ToString()
        });
}
