using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Players;
using GameFactory.Networking.World;
using GameFactory.Runtime;
using GameFactory.Steam;
using GameFactory.Steam.Adapters.GodotSteam;
using GameFactory.Steam.Models;

namespace GameFactory.Sandbox.Netfox;

/// <summary>
/// A deliberately bounded Netfox-only gameplay acceptance scenario. GameFactory
/// owns spawning and identity; Netfox owns ticking, input history, rollback,
/// state delivery, and interpolation inside the sandbox scenes.
/// </summary>
public partial class NetfoxGameplayProbe : Node
{
    private const int RequiredWorldObjectCount = 3;
    private const long ScenarioLeadTicks = 30;
    private const long ScenarioCompletionTick = 170;
    private const float MinimumDivergence = 0.05f;
    private const float ConvergenceTolerance = 0.10f;
    private const long ConvergenceDeadlineTicks = 40;

    private readonly PeerRegistry _peers = new();
    private readonly PlayerRegistry _players = new();
    private readonly RuntimeContext _runtime = new();
    private SteamSession? _session;
    private PlayerLifecycle? _playerLifecycle;
    private NetworkWorld _world = null!;
    private Node _networkTime = null!;
    private Callable _afterSyncCallable;
    private bool _timeSynchronized;
    private bool _clientReadyReported;
    private bool _clientReadyReceived;
    private long _clientPeerId;
    private bool _scenarioStarted;
    private bool _scenarioCompleted;
    private long _scenarioStartTick = -1;
    private string? _role;
    private string? _testRunId;

    [Export] public PackedScene PlayerScene { get; set; } = null!;
    [Export] public PackedScene StateProbeScene { get; set; } = null!;

    public override async void _Ready()
    {
        try
        {
            GameLog.EnsureInitialized();
            string[] arguments = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            string? scenario = ReadArgument(arguments, "--test-scenario=");
            if (!string.Equals(scenario, "netfox_gameplay", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("NetfoxGameplayProbe requires --test-scenario=netfox_gameplay.");

            _role = arguments.Contains("--steam-host") ? "host" : "client";
            _testRunId = ReadArgument(arguments, "--test-run-id=");
            _world = GetNode<NetworkWorld>("NetworkWorld");
            _networkTime = GetNode<Node>("/root/NetworkTime");
            _afterSyncCallable = Callable.From(OnInitialTimeSync);
            _networkTime.Connect("after_sync", _afterSyncCallable);
            SubscribeToMultiplayer();

            GodotSteamAdapter adapter = GetNode<SteamPlatform>("/root/SteamPlatform").Adapter;
            _session = new SteamSession(adapter, Multiplayer);
            await _session.InitializeAsync();

            if (arguments.Contains("--steam-host"))
                await HostAsync();
            else if (TryReadLobby(arguments, out SteamLobbyId lobbyId))
                await JoinAsync(lobbyId);
            else
                throw new ArgumentException("Expected --steam-host or --steam-lobby=<id>.");
        }
        catch (Exception exception)
        {
            Log("netfox.gameplay", "initialization_failed", new Dictionary<string, string?> { ["error"] = exception.Message });
        }
    }

    public override void _Process(double _delta)
    {
        if (!_timeSynchronized) return;

        if (!Multiplayer.IsServer() && !_clientReadyReported && WorldTopologyReady())
        {
            _clientReadyReported = true;
            Log("netfox.gameplay", "client_topology_verified", TopologyFields());
            RpcId(PeerId.Server.Value, MethodName.ClientReadyRpc);
        }

        if (!Multiplayer.IsServer() || _scenarioCompleted || !_scenarioStarted) return;
        long scenarioTick = ReadTick() - _scenarioStartTick;
        if (scenarioTick < ScenarioCompletionTick) return;

        _scenarioCompleted = true;
        Log("netfox.gameplay", "scenario_complete", new Dictionary<string, string?>(ScenarioFields())
        {
            ["scenario_tick"] = scenarioTick.ToString(),
            ["minimum_divergence"] = MinimumDivergence.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["convergence_tolerance"] = ConvergenceTolerance.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["convergence_deadline_ticks"] = ConvergenceDeadlineTicks.ToString()
        });
    }

    public override void _ExitTree()
    {
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
        if (_networkTime is not null && GodotObject.IsInstanceValid(_networkTime) &&
            _networkTime.IsConnected("after_sync", _afterSyncCallable))
            _networkTime.Disconnect("after_sync", _afterSyncCallable);
        _playerLifecycle?.Dispose();
        _session?.Dispose();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientReadyRpc()
    {
        if (!Multiplayer.IsServer() || _clientReadyReceived) return;
        _clientReadyReceived = true;
        _clientPeerId = Multiplayer.GetRemoteSenderId();
        Log("netfox.gameplay", "client_ready_received", TopologyFields());
        TryStartScenario();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StartScenarioRpc(long startTick)
    {
        ConfigureScenario(startTick);
    }

    private async Task HostAsync()
    {
        SteamLobby lobby = await _session!.HostAsync(new SteamLobbyCreateOptions(), new SteamListenServerOptions());
        _runtime.SetMode(RuntimeMode.ListenServer);
        _playerLifecycle = new PlayerLifecycle(_peers, _players, _runtime, SpawnPlayer, _world.Despawn);
        _peers.Add(PeerId.Server, isLocal: true);
        _world.Spawn(StateProbeScene, PeerId.Server);
        Log("netfox.gameplay", "host_ready", new Dictionary<string, string?> { ["lobby_id"] = lobby.Id.ToString() });
    }

    private async Task JoinAsync(SteamLobbyId lobbyId)
    {
        await _session!.JoinAsync(lobbyId, new SteamClientOptions());
        _runtime.SetMode(RuntimeMode.Client);
        Log("netfox.gameplay", "client_joined_lobby", new Dictionary<string, string?> { ["lobby_id"] = lobbyId.ToString() });
    }

    private NetworkObjectId SpawnPlayer(NetworkPeer peer, PlayerId playerId)
    {
        NetfoxGameplayPlayer player = _world.Spawn<NetfoxGameplayPlayer>(
            PlayerScene, peer.Id, new Godot.Collections.Dictionary { ["player_id"] = playerId.Value });
        NetworkObject networkObject = player.GetNode<NetworkObject>("NetworkObject");
        Log("netfox.gameplay", "player_spawned", new Dictionary<string, string?>
        {
            ["player_id"] = playerId.ToString(),
            ["network_object_id"] = networkObject.Id.ToString(),
            ["owner_peer_id"] = peer.Id.ToString()
        });
        return networkObject.Id;
    }

    private void OnInitialTimeSync()
    {
        _timeSynchronized = true;
        Log("netfox.gameplay", "time_sync_ready", new Dictionary<string, string?> { ["network_tick"] = ReadTick().ToString() });
        TryStartScenario();
    }

    private void OnPeerConnected(long peerValue)
    {
        if (!Multiplayer.IsServer()) return;
        _peers.Add(new PeerId(peerValue), isLocal: false);
        Log("netfox.gameplay", "peer_connected", new Dictionary<string, string?> { ["peer_id"] = peerValue.ToString() });
    }

    private void OnPeerDisconnected(long peerValue)
    {
        if (Multiplayer.IsServer()) _peers.Remove(new PeerId(peerValue));
        Log("netfox.gameplay", "peer_disconnected", new Dictionary<string, string?> { ["peer_id"] = peerValue.ToString() });
    }

    private void OnConnectedToServer() => Log("netfox.gameplay", "godot_connected_to_server", null);
    private void OnConnectionFailed() => Log("netfox.gameplay", "godot_connection_failed", null);
    private void OnServerDisconnected() => Log("netfox.gameplay", "godot_server_disconnected", null);

    private void TryStartScenario()
    {
        if (!Multiplayer.IsServer() || !_timeSynchronized || !_clientReadyReceived || _scenarioStarted || !WorldTopologyReady()) return;
        _scenarioStarted = true;
        _scenarioStartTick = ReadTick() + ScenarioLeadTicks;
        ConfigureScenario(_scenarioStartTick);
        RpcId(_clientPeerId, MethodName.StartScenarioRpc, _scenarioStartTick);
        Log("netfox.gameplay", "scenario_started", new Dictionary<string, string?>(ScenarioFields())
        {
            ["scenario_start_tick"] = _scenarioStartTick.ToString(),
            ["input_schedule"] = "20-59:right,60-99:down,100-139:left,140+:zero"
        });
    }

    private void ConfigureScenario(long startTick)
    {
        _scenarioStarted = true;
        _scenarioStartTick = startTick;
        foreach (NetfoxGameplayPlayer player in GetTree().GetNodesInGroup("netfox_gameplay_player").OfType<NetfoxGameplayPlayer>())
            player.StartScenario(startTick);
    }

    private bool WorldTopologyReady()
    {
        NetfoxGameplayPlayer[] players = GetTree().GetNodesInGroup("netfox_gameplay_player").OfType<NetfoxGameplayPlayer>().ToArray();
        return _world.Count >= RequiredWorldObjectCount && players.Length == 2 && players.All(player => player.AuthorityConfigured);
    }

    private long ReadTick() => _networkTime.Get("tick").AsInt64();
    private void SubscribeToMultiplayer()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    private Dictionary<string, string?> ScenarioFields() => new()
    {
        ["role"] = _role, ["test_run_id"] = _testRunId,
        ["network_tick"] = ReadTick().ToString(), ["scenario_start_tick"] = _scenarioStartTick.ToString()
    };
    private Dictionary<string, string?> TopologyFields()
    {
        Dictionary<string, string?> fields = ScenarioFields();
        fields["world_object_count"] = _world.Count.ToString();
        fields["player_count"] = _players.Count.ToString();
        return fields;
    }
    private void Log(string category, string eventName, IReadOnlyDictionary<string, string?>? fields)
    {
        Dictionary<string, string?> merged = ScenarioFields();
        if (fields is not null) foreach ((string key, string? value) in fields) merged[key] = value;
        GameLog.Info(category, eventName, fields: merged);
    }
    private static bool TryReadLobby(IEnumerable<string> arguments, out SteamLobbyId lobbyId)
    {
        string? value = ReadArgument(arguments, "--steam-lobby=");
        if (ulong.TryParse(value, out ulong parsed) && parsed != 0) { lobbyId = new SteamLobbyId(parsed); return true; }
        lobbyId = default; return false;
    }
    private static string? ReadArgument(IEnumerable<string> arguments, string prefix)
        => arguments.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
}
