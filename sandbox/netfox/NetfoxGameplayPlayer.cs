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
    private bool _rollbackActive;
    private long _rollbackStartTick;
    private bool _mispredictionStarted;
    private bool _mispredictionEnded;

    public long ScenarioStartTick { get; private set; } = -1;
    public bool AuthorityConfigured { get; private set; }

    public void ApplyNetworkSpawnData(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary)
            throw new InvalidOperationException("Netfox player spawn data must be a Dictionary.");

        Godot.Collections.Dictionary values = data.AsGodotDictionary();
        if (!values.ContainsKey("player_id"))
            throw new InvalidOperationException("Netfox player spawn data is missing player_id.");

        PlayerId = (long)values["player_id"];
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
        Node rollbackSynchronizer = GetNode<Node>("RollbackSynchronizer");

        // State stays server-authoritative. Only the input property belongs to
        // the owning peer; Netfox uses the split when it records/replays ticks.
        SetMultiplayerAuthority((int)PeerId.Server.Value, recursive: false);
        input.SetMultiplayerAuthority((int)networkObject.OwnerPeerId.Value, recursive: false);
        rollbackSynchronizer.Call("process_settings");
        AuthorityConfigured = true;

        GameLog.Info("netfox.gameplay", "player_authority_configured", fields: new Dictionary<string, string?>
        {
            ["player_id"] = PlayerId.ToString(),
            ["network_object_id"] = networkObject.Id.ToString(),
            ["owner_peer_id"] = networkObject.OwnerPeerId.ToString(),
            ["root_multiplayer_authority"] = GetMultiplayerAuthority().ToString(),
            ["input_multiplayer_authority"] = input.GetMultiplayerAuthority().ToString()
        });
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

    private void OnRollbackStarted()
    {
        if (_rollbackActive) return;
        _rollbackActive = true;
        _rollbackStartTick = _networkRollback.Get("tick").AsInt64();
        NetworkObject networkObject = GetNode<NetworkObject>("NetworkObject");
        long scenarioTick = ScenarioStartTick < 0 ? -1 : _rollbackStartTick - ScenarioStartTick;
        GameLog.Info("netfox.rollback", "started", fields: new Dictionary<string, string?>
        {
            ["network_object_id"] = networkObject.Id.ToString(),
            ["owner_peer_id"] = networkObject.OwnerPeerId.ToString(),
            ["role"] = Multiplayer.IsServer() ? "host" : "client",
            ["network_tick"] = _rollbackStartTick.ToString(),
            ["scenario_tick"] = scenarioTick.ToString()
        });
    }

    private void OnRollbackCompleted()
    {
        if (!_rollbackActive) return;
        long endTick = _networkRollback.Get("tick").AsInt64();
        _rollbackActive = false;
        NetworkObject networkObject = GetNode<NetworkObject>("NetworkObject");
        GameLog.Info("netfox.rollback", "completed", fields: new Dictionary<string, string?>
        {
            ["network_object_id"] = networkObject.Id.ToString(),
            ["owner_peer_id"] = networkObject.OwnerPeerId.ToString(),
            ["role"] = Multiplayer.IsServer() ? "host" : "client",
            ["rollback_from_tick"] = _rollbackStartTick.ToString(),
            ["rollback_to_tick"] = endTick.ToString(),
            ["replayed_ticks"] = Math.Max(0, endTick - _rollbackStartTick).ToString(),
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
