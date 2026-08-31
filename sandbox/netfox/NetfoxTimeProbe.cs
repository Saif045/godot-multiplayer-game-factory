using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Steam;
using GameFactory.Steam.Adapters.GodotSteam;
using GameFactory.Steam.Models;

namespace GameFactory.Sandbox.Netfox;

/// <summary>
/// Phase-1 boundary probe: existing SteamMultiplayerPeer transport plus Netfox
/// NetworkEvents-owned NetworkTime. It intentionally contains no gameplay,
/// rollback, prediction, or replication ownership.
/// </summary>
public partial class NetfoxTimeProbe : Node
{
    private const int SampleTickDistance = 30;

    private SteamSession? _session;
    private GodotSteamAdapter? _adapter;
    private Node _networkTime = null!;
    private Node _networkEvents = null!;
    private string? _testRunId;
    private string? _role;
    private long _syncTick;
    private long _lastObservedTick = -1;
    private bool _initialSyncObserved;
    private bool _tickSampleReported;
    private bool _clientSampleReported;
    private bool _hostShutdownRequested;
    private MultiplayerPeer.ConnectionStatus? _lastPeerConnectionStatus;
    private double _peerStatusSampleElapsed;

    private Callable _afterSyncCallable;
    private Callable _afterClientSyncCallable;
    private Callable _serverStopCallable;
    private Callable _clientStopCallable;

    public override async void _Ready()
    {
        try
        {
            GameLog.EnsureInitialized();
            string[] arguments = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            string? scenario = ReadArgument(arguments, "--test-scenario=");
            if (scenario is not null && !string.Equals(scenario, "netfox_time_sync", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Unknown test scenario '{scenario}'. Available scenarios: netfox_time_sync.");

            _testRunId = ReadArgument(arguments, "--test-run-id=");
            _role = arguments.Contains("--steam-host")
                ? "host"
                : arguments.Any(argument => argument.StartsWith("--steam-lobby=", StringComparison.Ordinal)) ? "client" : null;
            _networkTime = GetNode<Node>("/root/NetworkTime");
            _networkEvents = GetNode<Node>("/root/NetworkEvents");
            ConnectNetfoxSignals();

            _adapter = GetNode<SteamPlatform>("/root/SteamPlatform").Adapter;
            _session = new SteamSession(_adapter, Multiplayer);
            SubscribeToMultiplayer();
            await _session.InitializeAsync();
            LogTime("probe_ready", new Dictionary<string, string?>
            {
                ["network_events_enabled"] = _networkEvents.Get("enabled").AsBool().ToString(),
                ["lifecycle_owner"] = "NetworkEvents",
                ["tickrate"] = ReadTickrate().ToString()
            });

            if (arguments.Contains("--steam-host"))
            {
                await HostAsync();
                return;
            }

            string? joinArgument = arguments.FirstOrDefault(argument => argument.StartsWith("--steam-lobby=", StringComparison.Ordinal));
            if (joinArgument is not null && ulong.TryParse(joinArgument["--steam-lobby=".Length..], out ulong lobbyValue) && lobbyValue != 0)
                await JoinAsync(new SteamLobbyId(lobbyValue));
        }
        catch (Exception exception)
        {
            GameLog.Error("netfox.time", "initialization_failed", exception.Message, ScenarioFields());
        }
    }

    public override void _Process(double _delta)
    {
        _peerStatusSampleElapsed += _delta;
        if (_peerStatusSampleElapsed >= 1.0)
        {
            _peerStatusSampleElapsed = 0.0;
            LogPeerStatus("periodic");
        }

        if (!_initialSyncObserved || _tickSampleReported) return;

        long tick = ReadTick();
        if (tick < _lastObservedTick)
        {
            GameLog.Error("netfox.time", "tick_non_monotonic", fields: new Dictionary<string, string?>(TimeFields())
            {
                ["previous_tick"] = _lastObservedTick.ToString(),
                ["observed_tick"] = tick.ToString()
            });
            return;
        }
        _lastObservedTick = tick;
        if (tick < _syncTick + SampleTickDistance) return;

        _tickSampleReported = true;
        var fields = TimeFields();
        fields["ticks_since_initial_sync"] = (tick - _syncTick).ToString();
        fields["tick_monotonic"] = "true";
        fields["rtt_known"] = (!Multiplayer.IsServer()).ToString().ToLowerInvariant();
        LogTime("tick_progress", fields);

        if (!Multiplayer.IsServer())
        {
            _clientSampleReported = true;
            RpcId(1, MethodName.ReportClientSampleRpc, tick, ReadRemoteRttMilliseconds(), ReadTickrate());
            LogTime("client_sample_sent", fields);
        }
    }

    public override void _ExitTree()
    {
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
        DisconnectNetfoxSignals();
        _session?.Dispose();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReportClientSampleRpc(long clientTick, double remoteRttMilliseconds, long tickrate)
    {
        if (!Multiplayer.IsServer() || _hostShutdownRequested) return;

        LogTime("client_sample_received", new Dictionary<string, string?>(TimeFields())
        {
            ["client_tick"] = clientTick.ToString(),
            ["client_remote_rtt_ms"] = remoteRttMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["client_tickrate"] = tickrate.ToString()
        });
        _hostShutdownRequested = true;
        CallDeferred(nameof(LeaveHostAfterScenario));
    }

    private async void LeaveHostAfterScenario()
    {
        try
        {
            LogTime("host_session_leave_requested", TimeFields());
            await _session!.LeaveAsync();
        }
        catch (Exception exception)
        {
            GameLog.Error("netfox.time", "host_session_leave_failed", exception.Message, TimeFields());
        }
    }

    private async Task HostAsync()
    {
        SteamLobby lobby = await _session!.HostAsync(new SteamLobbyCreateOptions(), new SteamListenServerOptions());
        LogPeerStatus("initial");
        LogScenario("host_ready", new Dictionary<string, string?> { ["lobby_id"] = lobby.Id.ToString() });
    }

    private async Task JoinAsync(SteamLobbyId lobbyId)
    {
        await _session!.JoinAsync(lobbyId, new SteamClientOptions());
        LogPeerStatus("initial");
        LogScenario("client_joined_lobby", new Dictionary<string, string?> { ["lobby_id"] = lobbyId.ToString() });
    }

    private void SubscribeToMultiplayer()
    {
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    private void ConnectNetfoxSignals()
    {
        _afterSyncCallable = Callable.From(OnInitialTimeSync);
        _afterClientSyncCallable = Callable.From<long>(OnClientTimeSync);
        _serverStopCallable = Callable.From(OnServerStopped);
        _clientStopCallable = Callable.From(OnClientStopped);
        _networkTime.Connect("after_sync", _afterSyncCallable);
        _networkTime.Connect("after_client_sync", _afterClientSyncCallable);
        _networkEvents.Connect("on_server_stop", _serverStopCallable);
        _networkEvents.Connect("on_client_stop", _clientStopCallable);
    }

    private void DisconnectNetfoxSignals()
    {
        DisconnectIfConnected(_networkTime, "after_sync", _afterSyncCallable);
        DisconnectIfConnected(_networkTime, "after_client_sync", _afterClientSyncCallable);
        DisconnectIfConnected(_networkEvents, "on_server_stop", _serverStopCallable);
        DisconnectIfConnected(_networkEvents, "on_client_stop", _clientStopCallable);
    }

    private static void DisconnectIfConnected(Node? node, StringName signal, Callable callable)
    {
        if (node is not null && GodotObject.IsInstanceValid(node) && node.IsConnected(signal, callable))
            node.Disconnect(signal, callable);
    }

    private void OnConnectedToServer()
    {
        LogPeerStatus("godot_signal");
        LogScenario("godot_connected_to_server");
    }

    private void OnConnectionFailed()
    {
        LogPeerStatus("godot_signal");
        LogScenario("godot_connection_failed");
    }

    private void OnServerDisconnected()
    {
        LogPeerStatus("godot_signal");
        LogScenario("godot_server_disconnected");
    }

    private void OnInitialTimeSync()
    {
        _initialSyncObserved = true;
        _syncTick = ReadTick();
        _lastObservedTick = _syncTick;
        var fields = TimeFields();
        fields["initial_sync_elapsed_ms"] = GameLog.ElapsedMilliseconds.ToString();
        LogTime("initial_sync_complete", fields);
    }

    private void OnClientTimeSync(long peerId)
    {
        LogTime("client_sync_complete", new Dictionary<string, string?>(TimeFields())
        {
            ["peer_id"] = peerId.ToString()
        });
    }

    private void OnServerStopped() => LogTime("stopped", new Dictionary<string, string?>(ScenarioFields())
    {
        ["reason"] = "network_events_server_stop",
        ["lifecycle_owner"] = "NetworkEvents"
    });

    private void OnClientStopped() => LogTime("stopped", new Dictionary<string, string?>(ScenarioFields())
    {
        ["reason"] = "network_events_client_stop",
        ["lifecycle_owner"] = "NetworkEvents"
    });

    private void LogPeerStatus(string reason)
    {
        MultiplayerPeer? peer = Multiplayer.MultiplayerPeer;
        if (peer is null) return;

        MultiplayerPeer.ConnectionStatus status = peer.GetConnectionStatus();
        bool changed = _lastPeerConnectionStatus != status;
        _lastPeerConnectionStatus = status;
        var fields = ScenarioFields();
        fields["reason"] = reason;
        fields["peer_type"] = peer.GetType().Name;
        fields["connection_status"] = status.ToString();
        fields["local_unique_id"] = Multiplayer.GetUniqueId().ToString();
        GameLog.Info("steam.peer_status", changed ? "changed" : "sampled", fields: fields);
    }

    private Dictionary<string, string?> TimeFields()
    {
        var fields = ScenarioFields();
        fields["godot_peer_id"] = Multiplayer.GetUniqueId().ToString();
        fields["network_tick"] = ReadTick().ToString();
        fields["network_time"] = ReadDouble("time").ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        fields["local_tick"] = ReadLong("local_tick").ToString();
        fields["local_time"] = ReadDouble("local_time").ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        fields["remote_tick"] = ReadLong("remote_tick").ToString();
        fields["remote_rtt_ms"] = ReadRemoteRttMilliseconds().ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        fields["tickrate"] = ReadTickrate().ToString();
        fields["initial_sync_done"] = _networkTime.Call("is_initial_sync_done").AsBool().ToString().ToLowerInvariant();
        return fields;
    }

    private Dictionary<string, string?> ScenarioFields() => new()
    {
        ["scenario"] = "netfox_time_sync",
        ["test_run_id"] = _testRunId,
        ["role"] = _role ?? (Multiplayer.IsServer() ? "host" : "client"),
        ["lobby_id"] = _session?.Lobby?.Id.ToString(),
        ["local_steam_id"] = _adapter?.LocalUser.Id.ToString(),
        ["lifecycle_owner"] = "NetworkEvents"
    };

    private void LogScenario(string eventName, IReadOnlyDictionary<string, string?>? fields = null)
        => GameLog.Info("netfox.scenario", eventName, fields: MergeScenarioFields(fields));

    private void LogTime(string eventName, IReadOnlyDictionary<string, string?>? fields = null)
        => GameLog.Info("netfox.time", eventName, fields: MergeScenarioFields(fields));

    private Dictionary<string, string?> MergeScenarioFields(IReadOnlyDictionary<string, string?>? fields)
    {
        var merged = ScenarioFields();
        if (fields is not null)
            foreach ((string key, string? value) in fields) merged[key] = value;
        return merged;
    }

    private long ReadTick() => ReadLong("tick");
    private long ReadTickrate() => ReadLong("tickrate");
    private long ReadLong(StringName property) => _networkTime.Get(property).AsInt64();
    private double ReadDouble(StringName property) => _networkTime.Get(property).AsDouble();
    private double ReadRemoteRttMilliseconds() => ReadDouble("remote_rtt") * 1000.0;

    private static string? ReadArgument(IEnumerable<string> arguments, string prefix)
    {
        string? argument = arguments.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return null;
        string value = argument[prefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
