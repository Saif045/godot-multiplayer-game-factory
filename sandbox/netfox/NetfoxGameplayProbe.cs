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
/// Interactive Netfox movement playground. GameFactory owns the session,
/// identities, and spawning; Netfox owns ticked input, state history,
/// prediction, reconciliation, and presentation interpolation.
/// </summary>
public partial class NetfoxGameplayProbe : Node
{
    private readonly PeerRegistry _peers = new();
    private readonly PlayerRegistry _players = new();
    private readonly RuntimeContext _runtime = new();

    private SteamSession? _session;
    private PlayerLifecycle? _playerLifecycle;
    private NetworkWorld _world = null!;
    private bool _clientPlayersReported;
    private string? _role;
    private Node? _networkTime;
    private Node? _networkTimeSynchronizer;
    private Callable? _timeSyncPanicCallable;
    private MultiplayerPeer.ConnectionStatus? _lastPeerConnectionStatus;
    private double _peerStatusSampleElapsed;

    [Export] public PackedScene PlayerScene { get; set; } = null!;

    public override async void _Ready()
    {
        try
        {
            GameLog.EnsureInitialized();
            string[] arguments = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            _role = arguments.Contains("--steam-host") ? "host" : "client";
            _world = GetNode<NetworkWorld>("NetworkWorld");
            _networkTime = GetNode<Node>("/root/NetworkTime");
            _networkTimeSynchronizer = GetNode<Node>("/root/NetworkTimeSynchronizer");
            _timeSyncPanicCallable = Callable.From<double>(OnTimeSyncPanic);
            _networkTimeSynchronizer.Connect("on_panic", _timeSyncPanicCallable.Value);
            SubscribeToMultiplayer();

            GodotSteamAdapter adapter = GetNode<SteamPlatform>("/root/SteamPlatform").Adapter;
            _session = new SteamSession(adapter, Multiplayer);
            await _session.InitializeAsync();

            if (arguments.Contains("--steam-host"))
                await HostAsync();
            else if (TryReadLobby(arguments, out SteamLobbyId lobbyId))
                await JoinAsync(lobbyId);
            else
                throw new ArgumentException("Use --steam-host or --steam-lobby=<id> with --run=netfox-gameplay.");
        }
        catch (Exception exception)
        {
            Log("initialization_failed", new Dictionary<string, string?> { ["error"] = exception.Message });
        }
    }

    public override void _Process(double delta)
    {
        _peerStatusSampleElapsed += delta;
        if (_peerStatusSampleElapsed >= 1.0)
        {
            _peerStatusSampleElapsed = 0.0;
            LogPeerStatus("periodic");
        }

        if (delta > 1.0)
            LogTimeEvent("stall_observed", new Dictionary<string, string?>
            {
                ["process_delta_ms"] = (delta * 1000.0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
            });

        if (Multiplayer.IsServer() || _clientPlayersReported)
            return;

        NetfoxGameplayPlayer[] players = GetPlayers();
        if (players.Length != 2 || !players.All(HasExpectedAuthority))
            return;

        _clientPlayersReported = true;
        Log("players_ready", new Dictionary<string, string?>
        {
            ["player_count"] = players.Length.ToString(),
            ["world_object_count"] = _world.Count.ToString()
        });
        GD.Print("[netfox] Connected. Use WASD to move your bright player; the other colour is the remote player.");
    }

    public override void _ExitTree()
    {
        if (_networkTimeSynchronizer is not null && _timeSyncPanicCallable is not null &&
            _networkTimeSynchronizer.IsConnected("on_panic", _timeSyncPanicCallable.Value))
            _networkTimeSynchronizer.Disconnect("on_panic", _timeSyncPanicCallable.Value);

        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
        _playerLifecycle?.Dispose();
        _session?.Dispose();
    }

    private async Task HostAsync()
    {
        SteamLobby lobby = await _session!.HostAsync(new SteamLobbyCreateOptions(), new SteamListenServerOptions());
        _runtime.SetMode(RuntimeMode.ListenServer);
        _playerLifecycle = new PlayerLifecycle(_peers, _players, _runtime, SpawnPlayer, _world.Despawn);
        _peers.Add(PeerId.Server, isLocal: true);
        LogPeerStatus("initial");
        Log("host_ready", new Dictionary<string, string?> { ["lobby_id"] = lobby.Id.ToString() });
        GD.Print($"[netfox] Hosting lobby {lobby.Id}. Start a second instance with --steam-lobby={lobby.Id}; use WASD in each instance.");
    }

    private async Task JoinAsync(SteamLobbyId lobbyId)
    {
        await _session!.JoinAsync(lobbyId, new SteamClientOptions());
        _runtime.SetMode(RuntimeMode.Client);
        LogPeerStatus("initial");
        Log("client_joined_lobby", new Dictionary<string, string?> { ["lobby_id"] = lobbyId.ToString() });
    }

    private NetworkObjectId SpawnPlayer(NetworkPeer peer, PlayerId playerId)
    {
        NetfoxGameplayPlayer player = _world.Spawn<NetfoxGameplayPlayer>(
            PlayerScene,
            peer.Id,
            new Godot.Collections.Dictionary { ["player_id"] = playerId.Value });
        NetworkObject networkObject = player.GetNode<NetworkObject>("NetworkObject");
        Log("player_spawned", new Dictionary<string, string?>
        {
            ["player_id"] = playerId.ToString(),
            ["network_object_id"] = networkObject.Id.ToString(),
            ["owner_peer_id"] = peer.Id.ToString()
        });
        return networkObject.Id;
    }

    private void OnPeerConnected(long peerValue)
    {
        if (!Multiplayer.IsServer())
            return;

        _peers.Add(new PeerId(peerValue), isLocal: false);
        Log("peer_connected", new Dictionary<string, string?> { ["peer_id"] = peerValue.ToString() });
    }

    private void OnPeerDisconnected(long peerValue)
    {
        if (Multiplayer.IsServer())
            _peers.Remove(new PeerId(peerValue));
        Log("peer_disconnected", new Dictionary<string, string?> { ["peer_id"] = peerValue.ToString() });
    }

    private void OnConnectedToServer()
    {
        LogPeerStatus("godot_signal");
        Log("godot_connected_to_server", null);
    }

    private void OnConnectionFailed()
    {
        LogPeerStatus("godot_signal");
        Log("godot_connection_failed", null);
    }

    private void OnServerDisconnected()
    {
        LogPeerStatus("godot_signal");
        Log("godot_server_disconnected", null);
    }

    private void OnTimeSyncPanic(double offsetSeconds) => LogTimeEvent("panic", new Dictionary<string, string?>
    {
        ["panic_offset_ms"] = (offsetSeconds * 1000.0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
    });

    private void LogPeerStatus(string reason)
    {
        MultiplayerPeer? peer = Multiplayer.MultiplayerPeer;
        if (peer is null)
            return;

        MultiplayerPeer.ConnectionStatus status = peer.GetConnectionStatus();
        bool changed = _lastPeerConnectionStatus != status;
        _lastPeerConnectionStatus = status;
        GameLog.Info("steam.peer_status", changed ? "changed" : "sampled", fields: new Dictionary<string, string?>
        {
            ["reason"] = reason,
            ["role"] = _role,
            ["peer_type"] = peer.GetType().Name,
            ["connection_status"] = status.ToString(),
            ["local_unique_id"] = Multiplayer.GetUniqueId().ToString(),
            ["lobby_id"] = _session?.Lobby?.Id.ToString()
        });
    }

    private void LogTimeEvent(string eventName, Dictionary<string, string?> fields)
    {
        if (_networkTime is not null)
        {
            fields["network_time_tick"] = _networkTime.Get("tick").AsInt64().ToString();
            fields["clock_offset_ms"] = (_networkTime.Get("clock_offset").AsDouble() * 1000.0)
                .ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            fields["clock_stretch_factor"] = _networkTime.Get("clock_stretch_factor").AsDouble()
                .ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
            fields["remote_clock_offset_ms"] = (_networkTime.Get("remote_clock_offset").AsDouble() * 1000.0)
                .ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        }

        Log(eventName, fields, "netfox.time");
    }

    private NetfoxGameplayPlayer[] GetPlayers() => GetTree()
        .GetNodesInGroup("netfox_gameplay_player")
        .OfType<NetfoxGameplayPlayer>()
        .ToArray();

    private static bool HasExpectedAuthority(NetfoxGameplayPlayer player)
    {
        NetworkObject networkObject = player.GetNode<NetworkObject>("NetworkObject");
        return player.GetMultiplayerAuthority() == PeerId.Server.Value &&
            player.GetNode<Node>("Simulation").GetMultiplayerAuthority() == PeerId.Server.Value &&
            player.GetNode<Node>("Input").GetMultiplayerAuthority() == networkObject.OwnerPeerId.Value;
    }

    private void SubscribeToMultiplayer()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    private void Log(string eventName, IReadOnlyDictionary<string, string?>? fields, string category = "netfox.movement")
    {
        Dictionary<string, string?> values = new(fields ?? new Dictionary<string, string?>())
        {
            ["role"] = _role,
            ["local_peer_id"] = Multiplayer.GetUniqueId().ToString()
        };
        GameLog.Info(category, eventName, fields: values);
    }

    private static bool TryReadLobby(IEnumerable<string> arguments, out SteamLobbyId lobbyId)
    {
        const string prefix = "--steam-lobby=";
        string? value = arguments.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (value is not null && ulong.TryParse(value[prefix.Length..], out ulong parsed))
        {
            lobbyId = new SteamLobbyId(parsed);
            return true;
        }

        lobbyId = default;
        return false;
    }
}
