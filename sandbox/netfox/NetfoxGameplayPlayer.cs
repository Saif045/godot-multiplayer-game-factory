using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Sandbox.Netfox;

/// <summary>
/// A spawned, interactive movement avatar. Its state remains server-owned;
/// only the Input child is delegated to the peer identified by OwnerPeerId.
/// </summary>
public partial class NetfoxGameplayPlayer : Node2D, INetworkSpawnInitializable
{
    private const float MarkerRadius = 22.0f;
    private const float CorrectionDetectionEpsilonPixels = 0.001f;
    private const float PresentationConvergenceBoundPixels = 8.0f;
    private const int ReplayResultCacheLength = 128;
    private static readonly int[] HistoryAgeThresholds = [32, 48, 56, 64];

    private long _playerId;
    private double _movementLogElapsed;
    private double _historyDiagnosticsElapsed;
    private double _historyCadencePollElapsed;
    private bool _inputActive;
    private bool _configured;
    private long? _lastKnownInputTick;
    private long? _lastKnownStateTick;
    private int _inputAdvanceEvents;
    private int _stateAdvanceEvents;
    private long _largestInputTickAdvance;
    private long _largestStateTickAdvance;
    private readonly HashSet<int> _inputAgeThresholdsReported = [];
    private readonly HashSet<int> _stateAgeThresholdsReported = [];
    private readonly HashSet<int> _inputRollbackAgeThresholdsReported = [];
    private readonly HashSet<int> _stateRollbackAgeThresholdsReported = [];
    private readonly Dictionary<long, Vector2> _simulationResultsByTick = [];
    private double _reconciliationWindowElapsed;
    private float _maxPresentationErrorInWindow;
    private bool _correctionWindowActive;
    private double _correctionWindowElapsed;
    private long _firstCorrectionTick;
    private long _lastCorrectionTick;
    private int _replayedTicksInCorrectionWindow;
    private int _sameTickCorrectionCount;
    private float _maxCorrectionInWindow;

    public void ApplyNetworkSpawnData(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary)
            throw new InvalidOperationException("Netfox player spawn data must be a Dictionary.");

        Godot.Collections.Dictionary values = data.AsGodotDictionary();
        if (!values.ContainsKey("player_id"))
            throw new InvalidOperationException("Netfox player spawn data is missing player_id.");

        _playerId = (long)values["player_id"];
        Vector2 startPosition = _playerId == 1 ? new Vector2(360, 324) : new Vector2(792, 324);
        Node2D simulation = GetNode<Node2D>("Simulation");
        simulation.Set("simulated_position", startPosition);
        simulation.Position = startPosition;
    }

    public override void _Ready()
    {
        NetworkObject networkObject = GetNode<NetworkObject>("NetworkObject");
        Node input = GetNode<Node>("Input");
        Node simulation = GetNode<Node>("Simulation");

        _configured = true;

        GameLog.Info("netfox.movement", "player_configured", fields: new Dictionary<string, string?>
        {
            ["player_id"] = _playerId.ToString(),
            ["network_object_id"] = networkObject.Id.ToString(),
            ["owner_peer_id"] = networkObject.OwnerPeerId.ToString(),
            ["root_multiplayer_authority"] = GetMultiplayerAuthority().ToString(),
            ["simulation_multiplayer_authority"] = simulation.GetMultiplayerAuthority().ToString(),
            ["input_multiplayer_authority"] = input.GetMultiplayerAuthority().ToString()
        });
    }

    public override void _Process(double delta)
    {
        _movementLogElapsed += delta;
        _historyDiagnosticsElapsed += delta;
        _historyCadencePollElapsed += delta;
        _reconciliationWindowElapsed += delta;
        if (_correctionWindowActive)
            _correctionWindowElapsed += delta;
        ObservePresentationError();
        if (_configured && _historyCadencePollElapsed >= 0.1)
        {
            _historyCadencePollElapsed = 0;
            ObserveHistoryCadence();
        }
        ReportHistoryDiagnostics();
        ReportReconciliationWindow();
        QueueRedraw();
    }

    public override void _Draw()
    {
        NetworkObject? networkObject = GetNodeOrNull<NetworkObject>("NetworkObject");
        Node2D? presentation = GetNodeOrNull<Node2D>("Presentation");
        if (networkObject is null || presentation is null)
            return;

        bool localOwner = networkObject.OwnerPeerId.Value == Multiplayer.GetUniqueId();
        Color fill = networkObject.OwnerPeerId == PeerId.Server ? new Color("4ea8de") : new Color("f4a261");
        DrawCircle(presentation.Position, MarkerRadius, fill);
        DrawArc(presentation.Position, MarkerRadius + 4.0f, 0.0f, Mathf.Tau, 32,
            localOwner ? Colors.White : new Color(1.0f, 1.0f, 1.0f, 0.35f), localOwner ? 3.0f : 1.0f);
        DrawString(ThemeDB.FallbackFont, presentation.Position + new Vector2(-36, 48),
            localOwner ? "YOU" : networkObject.OwnerPeerId == PeerId.Server ? "HOST" : "CLIENT",
            HorizontalAlignment.Left, -1, 16, Colors.White);
    }

    /// <summary>Called by the input node when the locally owned input changes.</summary>
    public void ReportLocalInput(Vector2 movement)
    {
        bool active = movement != Vector2.Zero;
        if (_inputActive == active)
            return;

        _inputActive = active;
        NetworkObject networkObject = GetNode<NetworkObject>("NetworkObject");
        GameLog.Info("netfox.movement", "local_input_active", fields: new Dictionary<string, string?>
        {
            ["role"] = Multiplayer.IsServer() ? "host" : "client",
            ["player_id"] = _playerId.ToString(),
            ["network_object_id"] = networkObject.Id.ToString(),
            ["active"] = active.ToString(),
            ["movement"] = movement.ToString()
        });
    }

    /// <summary>Called from rollback simulation; rate-limited for operator logs.</summary>
    public void ReportMovement(long tick, Vector2 position, bool _isFresh)
    {
        if (!_configured || _movementLogElapsed < 0.5)
            return;

        _movementLogElapsed = 0;
        NetworkObject networkObject = GetNode<NetworkObject>("NetworkObject");
        bool localOwner = networkObject.OwnerPeerId.Value == Multiplayer.GetUniqueId();
        GameLog.Info("netfox.movement", localOwner ? "local_player_moved" : "remote_player_moved", fields: new Dictionary<string, string?>
        {
            ["role"] = Multiplayer.IsServer() ? "host" : "client",
            ["player_id"] = _playerId.ToString(),
            ["network_object_id"] = networkObject.Id.ToString(),
            ["owner_peer_id"] = networkObject.OwnerPeerId.ToString(),
            ["network_tick"] = tick.ToString(),
            ["position"] = position.ToString()
        });
    }

    /// <summary>
    /// Records a simulation result for reconciliation diagnostics. A correction is
    /// measured only when Netfox re-simulates the same tick with a different result.
    /// </summary>
    public void ReportSimulationTick(long tick, Vector2 position, bool isFresh)
    {
        if (!_configured)
            return;

        float correctionMagnitude = 0.0f;
        bool hasSameTickCorrection = !isFresh &&
            _simulationResultsByTick.TryGetValue(tick, out Vector2 previousResult) &&
            (correctionMagnitude = previousResult.DistanceTo(position)) > CorrectionDetectionEpsilonPixels;

        if (hasSameTickCorrection)
        {
            StartCorrectionWindow(tick, correctionMagnitude);
            _sameTickCorrectionCount++;
            _maxCorrectionInWindow = Mathf.Max(_maxCorrectionInWindow, correctionMagnitude);
        }

        if (_correctionWindowActive && !isFresh)
        {
            _replayedTicksInCorrectionWindow++;
            _lastCorrectionTick = tick;
        }

        _simulationResultsByTick[tick] = position;
        TrimSimulationResultCache(tick);
    }

    private void ReportReconciliationWindow()
    {
        if (!_configured || _reconciliationWindowElapsed < 1.0)
            return;

        _reconciliationWindowElapsed = 0;
        NetworkObject networkObject = GetNode<NetworkObject>("NetworkObject");
        Node2D simulation = GetNode<Node2D>("Simulation");
        Node2D presentation = GetNode<Node2D>("Presentation");
        float presentationError = presentation.Position.DistanceTo(simulation.Position);
        _maxPresentationErrorInWindow = Mathf.Max(_maxPresentationErrorInWindow, presentationError);
        GameLog.Info("netfox.reconciliation", "presentation_sample", fields: new Dictionary<string, string?>
        {
            ["player_id"] = _playerId.ToString(),
            ["network_object_id"] = networkObject.Id.ToString(),
            ["role"] = Multiplayer.IsServer() ? "host" : "client",
            ["presentation_error_pixels"] = presentationError.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["max_presentation_error_pixels"] = _maxPresentationErrorInWindow.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["correction_window_active"] = _correctionWindowActive.ToString()
        });

        if (_correctionWindowActive)
        {
            GameLog.Info("netfox.reconciliation", "correction_window_summary", fields: new Dictionary<string, string?>
            {
                ["player_id"] = _playerId.ToString(),
                ["network_object_id"] = networkObject.Id.ToString(),
                ["role"] = Multiplayer.IsServer() ? "host" : "client",
                ["first_replayed_tick"] = _firstCorrectionTick.ToString(),
                ["last_replayed_tick"] = _lastCorrectionTick.ToString(),
                ["replayed_tick_count"] = _replayedTicksInCorrectionWindow.ToString(),
                ["same_tick_correction_count"] = _sameTickCorrectionCount.ToString(),
                ["max_same_tick_correction_pixels"] = _maxCorrectionInWindow.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                ["max_presentation_error_pixels"] = _maxPresentationErrorInWindow.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                ["presentation_converged"] = (presentationError <= PresentationConvergenceBoundPixels).ToString(),
                ["convergence_bound_pixels"] = PresentationConvergenceBoundPixels.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                ["window_duration_ms"] = (_correctionWindowElapsed * 1000.0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
            });

            _correctionWindowActive = false;
            _correctionWindowElapsed = 0;
            _replayedTicksInCorrectionWindow = 0;
            _sameTickCorrectionCount = 0;
            _maxCorrectionInWindow = 0.0f;
        }

        _maxPresentationErrorInWindow = 0.0f;
    }

    private void StartCorrectionWindow(long tick, float correctionMagnitude)
    {
        if (_correctionWindowActive)
            return;

        _correctionWindowActive = true;
        _correctionWindowElapsed = 0;
        _firstCorrectionTick = tick;
        _lastCorrectionTick = tick;
        _replayedTicksInCorrectionWindow = 0;
        _sameTickCorrectionCount = 0;
        _maxCorrectionInWindow = correctionMagnitude;
        GameLog.Info("netfox.reconciliation", "correction_window_started", fields: new Dictionary<string, string?>
        {
            ["player_id"] = _playerId.ToString(),
            ["role"] = Multiplayer.IsServer() ? "host" : "client",
            ["first_replayed_tick"] = tick.ToString(),
            ["initial_same_tick_correction_pixels"] = correctionMagnitude.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
        });
    }

    private void ObservePresentationError()
    {
        if (!_configured)
            return;

        Node2D simulation = GetNode<Node2D>("Simulation");
        Node2D presentation = GetNode<Node2D>("Presentation");
        _maxPresentationErrorInWindow = Mathf.Max(
            _maxPresentationErrorInWindow,
            presentation.Position.DistanceTo(simulation.Position));
    }

    private void TrimSimulationResultCache(long newestTick)
    {
        long earliestTick = newestTick - ReplayResultCacheLength;
        List<long>? expired = null;
        foreach (long tick in _simulationResultsByTick.Keys)
        {
            if (tick < earliestTick)
                (expired ??= []).Add(tick);
        }

        if (expired is null)
            return;

        foreach (long tick in expired)
            _simulationResultsByTick.Remove(tick);
    }

    private void ReportHistoryDiagnostics()
    {
        if (!_configured || _historyDiagnosticsElapsed < 1.0)
            return;

        _historyDiagnosticsElapsed = 0;
        Node rollbackSynchronizer = GetNode<Node>("RollbackSynchronizer");
        NetworkObject networkObject = GetNode<NetworkObject>("NetworkObject");
        Node networkTime = GetNode<Node>("/root/NetworkTime");
        Node networkTimeSynchronizer = GetNode<Node>("/root/NetworkTimeSynchronizer");
        long networkTimeTick = networkTime.Get("tick").AsInt64();
        long rollbackTick = GetNode<Node>("/root/NetworkRollback").Get("tick").AsInt64();
        long lastKnownInputTick = rollbackSynchronizer.Call("get_last_known_input").AsInt64();
        long lastKnownStateTick = rollbackSynchronizer.Call("get_last_known_state").AsInt64();
        bool hasInput = rollbackSynchronizer.Call("has_input").AsBool();
        bool isPredicting = rollbackSynchronizer.Call("is_predicting").AsBool();

        int? inputNetworkTimeAge = CalculateAge(networkTimeTick, lastKnownInputTick);
        int? stateNetworkTimeAge = CalculateAge(networkTimeTick, lastKnownStateTick);
        int? inputRollbackAge = CalculateAge(rollbackTick, lastKnownInputTick);
        int? stateRollbackAge = CalculateAge(rollbackTick, lastKnownStateTick);
        ReportHistoryAgeThresholds("input", "network_time", inputNetworkTimeAge, _inputAgeThresholdsReported,
            networkTimeTick, rollbackTick, lastKnownInputTick, networkObject);
        ReportHistoryAgeThresholds("state", "network_time", stateNetworkTimeAge, _stateAgeThresholdsReported,
            networkTimeTick, rollbackTick, lastKnownStateTick, networkObject);
        ReportHistoryAgeThresholds("input", "network_rollback", inputRollbackAge, _inputRollbackAgeThresholdsReported,
            networkTimeTick, rollbackTick, lastKnownInputTick, networkObject);
        ReportHistoryAgeThresholds("state", "network_rollback", stateRollbackAge, _stateRollbackAgeThresholdsReported,
            networkTimeTick, rollbackTick, lastKnownStateTick, networkObject);

        MultiplayerPeer? peer = Multiplayer.MultiplayerPeer;
        Dictionary<string, string?> fields = new()
        {
            ["role"] = Multiplayer.IsServer() ? "host" : "client",
            ["player_id"] = _playerId.ToString(),
            ["owner_peer_id"] = networkObject.OwnerPeerId.ToString(),
            ["local_peer_id"] = Multiplayer.GetUniqueId().ToString(),
            ["network_time_tick"] = networkTimeTick.ToString(),
            ["network_rollback_tick"] = rollbackTick.ToString(),
            ["last_known_input_tick"] = FormatKnownTick(lastKnownInputTick),
            ["last_known_state_tick"] = FormatKnownTick(lastKnownStateTick),
            ["input_age"] = FormatAge(inputNetworkTimeAge),
            ["state_age"] = FormatAge(stateNetworkTimeAge),
            ["input_age_network_time"] = FormatAge(inputNetworkTimeAge),
            ["state_age_network_time"] = FormatAge(stateNetworkTimeAge),
            ["input_age_network_rollback"] = FormatAge(inputRollbackAge),
            ["state_age_network_rollback"] = FormatAge(stateRollbackAge),
            ["clock_offset_ms"] = FormatMilliseconds(networkTime.Get("clock_offset").AsDouble()),
            ["clock_stretch_factor"] = networkTime.Get("clock_stretch_factor").AsDouble().ToString("F6", System.Globalization.CultureInfo.InvariantCulture),
            ["remote_clock_offset_ms"] = FormatMilliseconds(networkTime.Get("remote_clock_offset").AsDouble()),
            ["remote_rtt_ms"] = FormatMilliseconds(networkTimeSynchronizer.Get("rtt").AsDouble()),
            ["has_input"] = hasInput.ToString(),
            ["is_predicting"] = isPredicting.ToString(),
            ["peer_type"] = peer?.GetType().Name,
            ["peer_connection_status"] = peer?.GetConnectionStatus().ToString(),
            ["available_packet_count"] = peer?.GetAvailablePacketCount().ToString(),
            ["input_advance_events"] = _inputAdvanceEvents.ToString(),
            ["state_advance_events"] = _stateAdvanceEvents.ToString(),
            ["largest_input_tick_advance"] = _largestInputTickAdvance.ToString(),
            ["largest_state_tick_advance"] = _largestStateTickAdvance.ToString()
        };
        GameLog.Info("netfox.history_age", "sample", fields: fields);
        GameLog.Info("netfox.transport_cadence", "sample", fields: fields);

        ResetCadenceWindow();
    }

    private void ObserveHistoryCadence()
    {
        Node rollbackSynchronizer = GetNode<Node>("RollbackSynchronizer");
        NetworkObject networkObject = GetNode<NetworkObject>("NetworkObject");
        long networkTimeTick = GetNode<Node>("/root/NetworkTime").Get("tick").AsInt64();
        long rollbackTick = GetNode<Node>("/root/NetworkRollback").Get("tick").AsInt64();
        long lastKnownInputTick = rollbackSynchronizer.Call("get_last_known_input").AsInt64();
        long lastKnownStateTick = rollbackSynchronizer.Call("get_last_known_state").AsInt64();
        TrackHistoryCadence(ref _lastKnownInputTick, lastKnownInputTick,
            ref _inputAdvanceEvents, ref _largestInputTickAdvance);
        TrackHistoryCadence(ref _lastKnownStateTick, lastKnownStateTick,
            ref _stateAdvanceEvents, ref _largestStateTickAdvance);
        ReportHistoryAgeThresholds("input", "network_time", CalculateAge(networkTimeTick, lastKnownInputTick),
            _inputAgeThresholdsReported, networkTimeTick, rollbackTick, lastKnownInputTick, networkObject);
        ReportHistoryAgeThresholds("state", "network_time", CalculateAge(networkTimeTick, lastKnownStateTick),
            _stateAgeThresholdsReported, networkTimeTick, rollbackTick, lastKnownStateTick, networkObject);
        ReportHistoryAgeThresholds("input", "network_rollback", CalculateAge(rollbackTick, lastKnownInputTick),
            _inputRollbackAgeThresholdsReported, networkTimeTick, rollbackTick, lastKnownInputTick, networkObject);
        ReportHistoryAgeThresholds("state", "network_rollback", CalculateAge(rollbackTick, lastKnownStateTick),
            _stateRollbackAgeThresholdsReported, networkTimeTick, rollbackTick, lastKnownStateTick, networkObject);
    }

    private void TrackHistoryCadence(ref long? previousTick, long currentTick,
        ref int advanceEvents, ref long largestAdvance)
    {
        if (currentTick < 0)
            return;

        if (previousTick is long previous && currentTick > previous)
        {
            advanceEvents++;
            largestAdvance = Math.Max(largestAdvance, currentTick - previous);
        }

        previousTick = currentTick;
    }

    private void ReportHistoryAgeThresholds(string historyKind, string clockBasis, int? age, HashSet<int> reportedThresholds,
        long networkTimeTick, long rollbackTick, long knownTick, NetworkObject networkObject)
    {
        if (age is not int actualAge)
            return;

        foreach (int threshold in HistoryAgeThresholds)
        {
            if (actualAge < threshold || !reportedThresholds.Add(threshold))
                continue;

            GameLog.Warning("netfox.history_age", "threshold_crossed", fields: new Dictionary<string, string?>
            {
                ["history_kind"] = historyKind,
                ["clock_basis"] = clockBasis,
                ["role"] = Multiplayer.IsServer() ? "host" : "client",
                ["threshold"] = threshold.ToString(),
                ["age"] = actualAge.ToString(),
                ["player_id"] = _playerId.ToString(),
                ["owner_peer_id"] = networkObject.OwnerPeerId.ToString(),
                ["local_peer_id"] = Multiplayer.GetUniqueId().ToString(),
                ["network_time_tick"] = networkTimeTick.ToString(),
                ["network_rollback_tick"] = rollbackTick.ToString(),
                ["last_known_tick"] = knownTick.ToString()
            });
        }
    }

    private void ResetCadenceWindow()
    {
        _inputAdvanceEvents = 0;
        _stateAdvanceEvents = 0;
        _largestInputTickAdvance = 0;
        _largestStateTickAdvance = 0;
    }

    private static int? CalculateAge(long currentTick, long knownTick) => knownTick < 0 ? null : checked((int)(currentTick - knownTick));
    private static string FormatKnownTick(long tick) => tick < 0 ? "unavailable" : tick.ToString();
    private static string FormatAge(int? age) => age?.ToString() ?? "unavailable";
    private static string FormatMilliseconds(double seconds) => (seconds * 1000.0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
}
