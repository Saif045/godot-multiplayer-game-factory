using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Netfox.Player3D;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Players;
using GameFactory.Networking.World;
using GameFactory.Runtime;
using GameFactory.Steam;
using GameFactory.Steam.Adapters.GodotSteam;
using GameFactory.Steam.Models;

namespace GameFactory.Sandbox.Netfox;

/// <summary>Visual Steam/Netfox acceptance sandbox for the reusable 3D player.</summary>
public partial class NetfoxPlayer3DProbe : Node3D
{
    private readonly PeerRegistry _peers = new();
    private readonly PlayerRegistry _players = new();
    private readonly RuntimeContext _runtime = new();
    private readonly Dictionary<NetworkObjectId, Vector3> _lastPositions = [];
    private SteamSession? _session;
    private PlayerLifecycle? _playerLifecycle;
    private NetworkWorld _world = null!;
    private string? _role;
    private bool _playersReady;
    private double _sampleElapsed;
    private MultiplayerPeer.ConnectionStatus? _lastPeerConnectionStatus;
    private double _peerStatusSampleElapsed;

    [Export] public PackedScene PlayerScene { get; set; } = null!;
    [Export] public PackedScene SwitchScene { get; set; } = null!;

    public override async void _Ready()
    {
        try
        {
            GameLog.EnsureInitialized();
            string[] arguments = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            _role = arguments.Contains("--steam-host") ? "host" : "client";
            _world = GetNode<NetworkWorld>("NetworkWorld");
            SubscribeToMultiplayer();
            GodotSteamAdapter adapter = GetNode<SteamPlatform>("/root/SteamPlatform").Adapter;
            _session = new SteamSession(adapter, Multiplayer);
            await _session.InitializeAsync();
            if (arguments.Contains("--steam-host")) await HostAsync();
            else if (TryReadLobby(arguments, out SteamLobbyId lobbyId)) await JoinAsync(lobbyId);
            else throw new ArgumentException("Use --steam-host or --steam-lobby=<id> with --run=netfox-player-3d.");
        }
        catch (Exception exception) { Log("initialization_failed", new Dictionary<string, string?> { ["error"] = exception.Message }); }
    }

    public override void _Process(double delta)
    {
        _peerStatusSampleElapsed += delta;
        if (_peerStatusSampleElapsed >= 1.0)
        {
            _peerStatusSampleElapsed = 0;
            LogPeerStatus("periodic");
        }

        NetworkPlayer3D[] players = GetPlayers();
        if (!_playersReady && players.Length == 2 && players.All(HasExpectedAuthority))
        {
            _playersReady = true;
            Log("players_ready", new Dictionary<string, string?> { ["player_count"] = players.Length.ToString() });
            GD.Print("[netfox-player-3d] Connected. WASD moves, Space jumps, and E toggles the nearby switch.");
        }
        _sampleElapsed += delta;
        if (_sampleElapsed < .5 || !_playersReady) return;
        _sampleElapsed = 0;
        foreach (NetworkPlayer3D player in players) SamplePlayer(player);
    }

    public override void _ExitTree()
    {
        Multiplayer.PeerConnected -= OnPeerConnected; Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnectedToServer; Multiplayer.ConnectionFailed -= OnConnectionFailed; Multiplayer.ServerDisconnected -= OnServerDisconnected;
        _playerLifecycle?.Dispose(); _session?.Dispose();
    }

    private async Task HostAsync()
    {
        SteamLobby lobby = await _session!.HostAsync(new SteamLobbyCreateOptions(), new SteamListenServerOptions());
        _runtime.SetMode(RuntimeMode.ListenServer);
        _playerLifecycle = new PlayerLifecycle(_peers, _players, _runtime, SpawnPlayer, _world.Despawn);
        _peers.Add(PeerId.Server, isLocal: true);
        InteractableSwitch interactableSwitch = _world.Spawn<InteractableSwitch>(
            SwitchScene,
            PeerId.Server,
            new Godot.Collections.Dictionary
            {
                ["spawn_position"] = new Vector3(0, 0.625f, -1.5f)
            });
        Log("switch_spawned", new Dictionary<string, string?>
        {
            ["network_object_id"] = interactableSwitch
                .GetNode<NetworkObject>("NetworkObject")
                .Id.ToString()
        });
        LogPeerStatus("initial");
        Log("host_ready", new Dictionary<string, string?> { ["lobby_id"] = lobby.Id.ToString() });
        GD.Print($"[netfox-player-3d] Hosting lobby {lobby.Id}.");
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
        Vector3 spawnPosition = playerId.Value == 1 ? new Vector3(-2, 2, 0) : new Vector3(2, 2, 0);
        NetworkPlayer3D player = _world.Spawn<NetworkPlayer3D>(PlayerScene, peer.Id, new Godot.Collections.Dictionary { ["spawn_position"] = spawnPosition });
        NetworkObject networkObject = player.GetNetworkObject();
        Log("player_spawned", new Dictionary<string, string?> { ["player_id"] = playerId.ToString(), ["network_object_id"] = networkObject.Id.ToString(), ["owner_peer_id"] = peer.Id.ToString() });
        return networkObject.Id;
    }

    private void SamplePlayer(NetworkPlayer3D player)
    {
        NetworkObject networkObject = player.GetNetworkObject();
        bool isLocalOwner = networkObject.OwnerPeerId.Value == Multiplayer.GetUniqueId();
        Vector3 position = player.GlobalPosition;
        _lastPositions.TryGetValue(networkObject.Id, out Vector3 previous);
        float distance = previous.DistanceTo(position);
        _lastPositions[networkObject.Id] = position;
        Node simulation = player.GetNode<Node>("Simulation");
        Log("player_sample", new Dictionary<string, string?> { ["network_object_id"] = networkObject.Id.ToString(), ["owner_peer_id"] = networkObject.OwnerPeerId.ToString(), ["is_local_owner"] = isLocalOwner.ToString(), ["position"] = position.ToString(), ["velocity"] = player.Velocity.ToString(), ["grounded"] = simulation.Get("grounded").AsBool().ToString(), ["position_delta"] = distance.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) });
        if (distance > .05f) Log(isLocalOwner ? "local_player_moved" : "remote_player_moved", new Dictionary<string, string?> { ["network_object_id"] = networkObject.Id.ToString(), ["position_delta"] = distance.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) });
        if (player.Velocity.Y > .1f) Log(isLocalOwner ? "local_jump_observed" : "remote_jump_observed", new Dictionary<string, string?> { ["network_object_id"] = networkObject.Id.ToString(), ["velocity_y"] = player.Velocity.Y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) });
    }

    private void OnPeerConnected(long peerValue) { if (!Multiplayer.IsServer()) return; _peers.Add(new PeerId(peerValue), isLocal: false); Log("peer_connected", new Dictionary<string, string?> { ["peer_id"] = peerValue.ToString() }); }
    private void OnPeerDisconnected(long peerValue) { if (Multiplayer.IsServer()) _peers.Remove(new PeerId(peerValue)); Log("peer_disconnected", new Dictionary<string, string?> { ["peer_id"] = peerValue.ToString() }); }
    private void OnConnectedToServer() { LogPeerStatus("godot_signal"); Log("godot_connected_to_server", null); }
    private void OnConnectionFailed() { LogPeerStatus("godot_signal"); Log("godot_connection_failed", null); }
    private void OnServerDisconnected() { LogPeerStatus("godot_signal"); Log("godot_server_disconnected", null); }

    private void LogPeerStatus(string reason)
    {
        MultiplayerPeer? peer = Multiplayer.MultiplayerPeer;
        if (peer is null) return;

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
    private NetworkPlayer3D[] GetPlayers() => GetTree().GetNodesInGroup("network_player_3d").OfType<NetworkPlayer3D>().ToArray();
    private static bool HasExpectedAuthority(NetworkPlayer3D player) { NetworkObject networkObject = player.GetNetworkObject(); return player.GetMultiplayerAuthority() == PeerId.Server.Value && player.GetNode<Node>("Simulation").GetMultiplayerAuthority() == PeerId.Server.Value && player.GetNode<Node>("Input").GetMultiplayerAuthority() == networkObject.OwnerPeerId.Value; }
    private void SubscribeToMultiplayer() { Multiplayer.PeerConnected += OnPeerConnected; Multiplayer.PeerDisconnected += OnPeerDisconnected; Multiplayer.ConnectedToServer += OnConnectedToServer; Multiplayer.ConnectionFailed += OnConnectionFailed; Multiplayer.ServerDisconnected += OnServerDisconnected; }
    private void Log(string eventName, IReadOnlyDictionary<string, string?>? fields) { Dictionary<string, string?> values = new(fields ?? new Dictionary<string, string?>()) { ["role"] = _role, ["local_peer_id"] = Multiplayer.GetUniqueId().ToString() }; GameLog.Info("netfox.player3d", eventName, fields: values); }
    private static bool TryReadLobby(IEnumerable<string> arguments, out SteamLobbyId lobbyId) { const string prefix = "--steam-lobby="; string? value = arguments.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)); if (value is not null && ulong.TryParse(value[prefix.Length..], out ulong parsed)) { lobbyId = new SteamLobbyId(parsed); return true; } lobbyId = default; return false; }
}
