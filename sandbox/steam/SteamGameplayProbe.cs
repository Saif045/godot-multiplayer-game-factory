using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Diagnostics.Network;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Players;
using GameFactory.Networking.World;
using GameFactory.Runtime;
using GameFactory.Sandbox.Replication;
using GameFactory.Steam;
using GameFactory.Steam.Adapters.GodotSteam;
using GameFactory.Steam.Models;

namespace GameFactory.Sandbox.Steam;

/// <summary>Manual full-stack GameFactory acceptance probe over a Steam-backed Godot peer.</summary>
public partial class SteamGameplayProbe : Node
{
    private readonly PeerRegistry _peers = new();
    private readonly PlayerRegistry _players = new();
    private readonly RuntimeContext _runtime = new();

    private SteamSession? _session;
    private GodotSteamAdapter? _adapter;
    private NetworkLogRelay? _diagnostics;
    private PlayerLifecycle? _playerLifecycle;
    private NetworkWorld _world = null!;
    private RawDoor? _door;
    private NetworkObjectId? _doorId;

    [Export] public PackedScene DoorScene { get; set; } = null!;
    [Export] public PackedScene PlayerScene { get; set; } = null!;

    public override async void _Ready()
    {
        try
        {
            _world = GetNode<NetworkWorld>("NetworkWorld");
            SubscribeToRegistries();
            SubscribeToMultiplayer();

            _diagnostics = new NetworkLogRelay { Name = "NetworkLogRelay" };
            AddChild(_diagnostics);
            _adapter = GodotSteamAdapter.Create(this);
            _diagnostics.SourceMetadataResolver = ResolveSteamMetadata;
            _session = new SteamSession(_adapter, Multiplayer);
            _session.StateChanged += OnSessionStateChanged;

            await _session.InitializeAsync();
            GameLog.Info("gameplay.probe", "ready", "Use --steam-host or --steam-lobby=<id>. Keys: H host, R mutate door, P snapshot, L leave.");

            string[] args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            if (args.Contains("--steam-host"))
            {
                await HostAsync();
                return;
            }

            string? joinArgument = args.FirstOrDefault(argument => argument.StartsWith("--steam-lobby=", StringComparison.Ordinal));
            if (joinArgument is not null && ulong.TryParse(joinArgument["--steam-lobby=".Length..], out ulong lobbyValue) && lobbyValue != 0)
                await JoinAsync(new SteamLobbyId(lobbyValue));
        }
        catch (Exception exception)
        {
            GameLog.Error("gameplay.probe", "initialization_failed", exception.Message);
        }
    }

    public override async void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true, Echo: false } key || _session is null)
            return;

        try
        {
            switch (key.Keycode)
            {
                case Key.H when _session.State == SteamSessionState.Ready:
                    await HostAsync();
                    break;
                case Key.R:
                    ToggleDoor();
                    break;
                case Key.P:
                    LogSnapshot("manual");
                    break;
                case Key.L:
                    await _session.LeaveAsync();
                    break;
            }
        }
        catch (Exception exception)
        {
            GameLog.Error("gameplay.probe", "action_failed", exception.Message);
        }
    }

    public override void _ExitTree()
    {
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        _playerLifecycle?.Dispose();
        if (_session is not null) _session.StateChanged -= OnSessionStateChanged;
        _session?.Dispose();
    }

    private async Task HostAsync()
    {
        SteamLobby lobby = await _session!.HostAsync(new SteamLobbyCreateOptions(), new SteamListenServerOptions());
        _runtime.SetMode(RuntimeMode.ListenServer);
        InitializeAuthoritativeGameplay();
        _diagnostics?.StartHostSession();
        GameLog.Info("gameplay.session", "hosting", $"lobby={lobby.Id}");
        LogSnapshot("host_initialized");
    }

    private async Task JoinAsync(SteamLobbyId lobbyId)
    {
        await _session!.JoinAsync(lobbyId, new SteamClientOptions());
        _runtime.SetMode(RuntimeMode.Client);
        GameLog.Info("gameplay.session", "joining", $"lobby={lobbyId}");
    }

    private void InitializeAuthoritativeGameplay()
    {
        _playerLifecycle ??= new PlayerLifecycle(_peers, _players, _runtime, SpawnPlayer, _world.Despawn);
        _peers.Add(PeerId.Server, isLocal: true);

        _door = _world.Spawn<RawDoor>(DoorScene, door => door.IsOpen = false);
        _doorId = _door.GetNode<NetworkObject>("NetworkObject").Id;
        GameLog.Info("gameplay.world", "pre_client_object_spawned", fields: new Dictionary<string, string?>
        {
            ["network_object_id"] = _doorId.Value.ToString(),
            ["is_open"] = _door.IsOpen.ToString()
        });
    }

    private NetworkObjectId SpawnPlayer(NetworkPeer peer, PlayerId playerId)
    {
        RawPlayer player = _world.Spawn<RawPlayer>(
            PlayerScene,
            peer.Id,
            new Godot.Collections.Dictionary { ["player_id"] = playerId.Value });
        NetworkObject networkObject = player.GetNode<NetworkObject>("NetworkObject");
        GameLog.Info("gameplay.player", "spawned", fields: new Dictionary<string, string?>
        {
            ["player_id"] = playerId.ToString(),
            ["peer_id"] = peer.Id.ToString(),
            ["network_object_id"] = networkObject.Id.ToString(),
            ["owner_peer_id"] = networkObject.OwnerPeerId.ToString()
        });
        return networkObject.Id;
    }

    private void OnPeerConnected(long peerValue)
    {
        PeerId peerId = new(peerValue);
        if (Multiplayer.IsServer())
        {
            _peers.Add(peerId, isLocal: false);
            if (_adapter!.TryGetSteamUserForPeer(peerId, out SteamUserId steamId))
                GameLog.Info("gameplay.mapping", "peer_mapped", fields: new Dictionary<string, string?>
                {
                    ["peer_id"] = peerId.ToString(),
                    ["steam_id"] = steamId.ToString()
                });
            LogSnapshot("remote_peer_connected");
        }
    }

    private async void OnPeerDisconnected(long peerValue)
    {
        if (!Multiplayer.IsServer()) return;
        _peers.Remove(new PeerId(peerValue));
        LogSnapshot("remote_peer_disconnect_started");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        LogSnapshot("remote_peer_cleanup_complete");
    }

    private void OnConnectedToServer() => LogSnapshot("connected_to_server");

    private void OnSessionStateChanged(SteamSessionState _, SteamSessionState next)
    {
        if (next == SteamSessionState.Leaving && _diagnostics?.SessionId is not null)
            _diagnostics.EndSession();
    }

    private void ToggleDoor()
    {
        if (!Multiplayer.IsServer() || _door is null)
        {
            GameLog.Warning("gameplay.replication", "mutation_rejected", "Only the host can mutate the probe door.");
            return;
        }

        _door.SetOpenOnAuthority(!_door.IsOpen);
        LogSnapshot("door_mutated");
    }

    private void SubscribeToMultiplayer()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
    }

    private void SubscribeToRegistries()
    {
        _players.PlayerAdded += player => GameLog.Info("gameplay.player", "registered", fields: new Dictionary<string, string?>
        {
            ["player_id"] = player.Id.ToString(), ["peer_id"] = player.PeerId.ToString(), ["network_object_id"] = player.ObjectId.ToString()
        });
        _players.PlayerRemoved += player => GameLog.Info("gameplay.player", "removed", fields: new Dictionary<string, string?>
        {
            ["player_id"] = player.Id.ToString(), ["peer_id"] = player.PeerId.ToString(), ["network_object_id"] = player.ObjectId.ToString()
        });
    }

    private IReadOnlyDictionary<string, string?>? ResolveSteamMetadata(PeerId peer)
    {
        if (_adapter is null || !_adapter.TryGetSteamUserForPeer(peer, out SteamUserId user)) return null;
        return new Dictionary<string, string?> { ["steam_id"] = user.ToString() };
    }

    private void LogSnapshot(string reason)
    {
        GameLog.Info("gameplay.snapshot", reason, fields: new Dictionary<string, string?>
        {
            ["runtime_mode"] = _runtime.Mode.ToString(),
            ["registered_peers"] = _peers.Count.ToString(),
            ["registered_players"] = _players.Count.ToString(),
            ["world_objects"] = _world.Count.ToString(),
            ["door_id"] = _doorId?.ToString(),
            ["door_is_open"] = _door?.IsOpen.ToString()
        });
    }
}
