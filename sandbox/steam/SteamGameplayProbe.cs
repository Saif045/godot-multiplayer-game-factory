using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Diagnostics.Network;
using GameFactory.Diagnostics.Replication;
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
    private readonly ReplicationConfirmationTracker _confirmations = new();
    private const long ConfirmationTimeoutMilliseconds = 1500;

    private SteamSession? _session;
    private GodotSteamAdapter? _adapter;
    private NetworkLogRelay? _diagnostics;
    private PlayerLifecycle? _playerLifecycle;
    private NetworkWorld _world = null!;
    private RawDoor? _door;
    private NetworkObjectId? _doorId;
    private string? _testScenario;
    private string? _testRunId;
    private string? _testRole;
    private MultiplayerPeer.ConnectionStatus? _lastPeerConnectionStatus;
    private double _peerStatusSampleSeconds;
    private bool _scenarioDoorMutationStarted;
    private bool _scenarioClientWorldReported;
    private bool _scenarioClientDoorReported;
    private bool _scenarioHostPassed;
    private long? _scenarioDoorRevision;

    [Export] public PackedScene DoorScene { get; set; } = null!;
    [Export] public PackedScene PlayerScene { get; set; } = null!;

    public override async void _Ready()
    {
        try
        {
            _world = GetNode<NetworkWorld>("NetworkWorld");
            _confirmations.Changed += OnConfirmationChanged;
            SubscribeToRegistries();
            SubscribeToMultiplayer();

            _diagnostics = new NetworkLogRelay { Name = "NetworkLogRelay" };
            AddChild(_diagnostics);
            _adapter = GetNode<SteamPlatform>("/root/SteamPlatform").Adapter;
            _diagnostics.SourceMetadataResolver = ResolveSteamMetadata;
            _session = new SteamSession(_adapter, Multiplayer);
            _session.StateChanged += OnSessionStateChanged;
            _session.PeerTearingDown += OnPeerTearingDown;

            await _session.InitializeAsync();
            GameLog.Info("gameplay.probe", "ready", "Use --steam-host or --steam-lobby=<id>. Keys: H host, R mutate door, P snapshot, L leave.");

            string[] args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            _testScenario = ReadArgument(args, "--test-scenario=");
            _testRunId = ReadArgument(args, "--test-run-id=");
            _testRole = args.Contains("--steam-host") ? "host" : args.Any(argument => argument.StartsWith("--steam-lobby=", StringComparison.Ordinal)) ? "client" : null;
            if (_testScenario is not null && !string.Equals(_testScenario, "steam_basic", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Unknown test scenario '{_testScenario}'. Available scenarios: steam_basic.");
            if (args.Contains("--steam-host"))
            {
                await HostGameAsync();
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
                    await HostGameAsync();
                    break;
                case Key.R:
                    ToggleDoor();
                    break;
                case Key.P:
                    LogSnapshot("manual");
                    break;
                case Key.L:
                    await LeaveGameAsync();
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
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
        _confirmations.Changed -= OnConfirmationChanged;
        _playerLifecycle?.Dispose();
        if (_session is not null)
        {
            _session.StateChanged -= OnSessionStateChanged;
            _session.PeerTearingDown -= OnPeerTearingDown;
        }
        _session?.Dispose();
    }

    public async Task HostGameAsync()
    {
        SteamLobby lobby = await _session!.HostAsync(new SteamLobbyCreateOptions(), new SteamListenServerOptions());
        _runtime.SetMode(RuntimeMode.ListenServer);
        InitializeAuthoritativeGameplay();
        _diagnostics?.StartHostSession();
        GameLog.Info("gameplay.session", "hosting", $"lobby={lobby.Id}");
        LogScenario("host_ready", new Dictionary<string, string?> { ["lobby_id"] = lobby.Id.ToString() });
        LogPeerStatus("initial");
        LogSnapshot("host_initialized");
    }

    private async Task JoinAsync(SteamLobbyId lobbyId)
    {
        await _session!.JoinAsync(lobbyId, new SteamClientOptions());
        _runtime.SetMode(RuntimeMode.Client);
        GameLog.Info("gameplay.session", "joining", $"lobby={lobbyId}");
        LogScenario("client_joined_lobby", new Dictionary<string, string?> { ["lobby_id"] = lobbyId.ToString() });
        LogPeerStatus("initial");
    }

    public Task LeaveGameAsync() => _session?.LeaveAsync() ?? Task.CompletedTask;

    private void InitializeAuthoritativeGameplay()
    {
        _playerLifecycle ??= new PlayerLifecycle(_peers, _players, _runtime, SpawnPlayer, _world.Despawn);
        _peers.Add(PeerId.Server, isLocal: true);

        _door = _world.Spawn<RawDoor>(DoorScene, door => door.IsOpen = false);
        _doorId = _door.GetNode<NetworkObject>("NetworkObject").Id;
        _door.AuthorityUpdated += OnDoorAuthorityUpdated;
        _door.ConfirmationAcknowledged += OnDoorConfirmationAcknowledged;
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
            if (_doorId is NetworkObjectId doorId)
            {
                long revision = _door?.Revision ?? 0;
                if (_confirmations.TryGetSnapshot(doorId, revision, out _))
                {
                    _confirmations.Expect(doorId, revision, peerId, GameLog.ElapsedMilliseconds, "late_join");
                }
                else
                {
                    _confirmations.Begin(doorId, revision, [peerId], GameLog.ElapsedMilliseconds, "late_join");
                    LogConfirmationExpected(doorId, revision, peerId, "late_join", 1);
                }
            }
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

    private void OnConnectedToServer()
    {
        LogScenario("godot_connected_to_server");
        LogPeerStatus("godot_signal");
        LogSnapshot("connected_to_server");
    }

    private void OnConnectionFailed()
    {
        LogScenarioFailure("godot_connection_failed");
        LogPeerStatus("godot_signal");
    }

    private void OnServerDisconnected()
    {
        LogScenarioFailure("godot_server_disconnected");
        LogPeerStatus("godot_signal");
    }

    private void OnSessionStateChanged(SteamSessionState _, SteamSessionState next)
    {
        if (next == SteamSessionState.Ready && _diagnostics?.SessionId is not null)
            _diagnostics.EndSession();
    }

    private void OnPeerTearingDown() => _diagnostics?.FlushBeforePeerTeardown();

    public override void _Process(double delta)
    {
        _peerStatusSampleSeconds -= delta;
        if (_peerStatusSampleSeconds <= 0)
        {
            LogPeerStatus("periodic");
            _peerStatusSampleSeconds = 1.0;
        }

        if (!Multiplayer.HasMultiplayerPeer()) return;
        if (Multiplayer.IsServer())
        {
            _confirmations.Expire(GameLog.ElapsedMilliseconds, ConfirmationTimeoutMilliseconds);
            TryRunHostScenario();
            return;
        }

        TryReportClientScenarioState();
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
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
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

    private void OnDoorAuthorityUpdated(NetworkObjectId objectId, long revision)
    {
        PeerId[] expected = _peers.Peers.Where(peer => !peer.IsLocal).Select(peer => peer.Id).ToArray();
        _confirmations.Begin(objectId, revision, expected, GameLog.ElapsedMilliseconds);
        foreach (PeerId peerId in expected)
            LogConfirmationExpected(objectId, revision, peerId, "mutation", expected.Length);
    }

    private void OnDoorConfirmationAcknowledged(NetworkObjectId objectId, long revision, PeerId peerId)
    {
        _confirmations.Confirm(objectId, revision, peerId, GameLog.ElapsedMilliseconds);
    }

    private void OnConfirmationChanged(ReplicationConfirmationEvent change)
    {
        ReplicationConfirmationSnapshot? snapshot = change.Snapshot;
        if (snapshot is null) return;
        switch (change.Kind)
        {
            case ReplicationConfirmationEventKind.Expected when change.PeerId is PeerId expected:
                LogConfirmationExpected(snapshot.ObjectId, snapshot.Revision, expected, change.Reason ?? "unknown", snapshot.ExpectedPeers.Count);
                break;
            case ReplicationConfirmationEventKind.Confirmed when change.PeerId is PeerId peer:
                LogConfirmation("confirmed", snapshot, peer, change.LatencyMilliseconds);
                break;
            case ReplicationConfirmationEventKind.LateConfirmed when change.PeerId is PeerId latePeer:
                LogConfirmation("confirmation_late", snapshot, latePeer, change.LatencyMilliseconds);
                break;
            case ReplicationConfirmationEventKind.Completed:
                GameLog.Info("gameplay.replication", "confirmation_complete", fields: ConfirmationFields(snapshot));
                if (IsSteamBasicScenario && !_scenarioHostPassed && snapshot.Revision == _scenarioDoorRevision)
                {
                    _scenarioHostPassed = true;
                    LogScenario("host_passed", new Dictionary<string, string?>(ConfirmationFields(snapshot))
                    {
                        ["assertion"] = "authoritative_door_revision_confirmed"
                    });
                }
                break;
            case ReplicationConfirmationEventKind.TimedOut:
                var fields = new Dictionary<string, string?>(ConfirmationFields(snapshot))
                {
                    ["missing_peer_ids"] = string.Join(",", change.MissingPeers?.Select(peer => peer.Value) ?? []),
                    ["elapsed_ms"] = change.ElapsedMilliseconds?.ToString(),
                    ["reason"] = change.Reason
                };
                GameLog.Warning("gameplay.replication", "confirmation_timeout", fields: fields);
                break;
        }
    }

    private static Dictionary<string, string?> ConfirmationFields(ReplicationConfirmationSnapshot snapshot) => new()
    {
        ["network_object_id"] = snapshot.ObjectId.ToString(),
        ["revision"] = snapshot.Revision.ToString(),
        ["expected_count"] = snapshot.ExpectedPeers.Count.ToString(),
        ["confirmed_count"] = snapshot.ConfirmedLatencyMilliseconds.Count.ToString()
    };

    private static void LogConfirmationExpected(NetworkObjectId objectId, long revision, PeerId peerId, string reason, int expectedCount)
    {
        GameLog.Info("gameplay.replication", "confirmation_expected", fields: new Dictionary<string, string?>
        {
            ["network_object_id"] = objectId.ToString(), ["revision"] = revision.ToString(), ["peer_id"] = peerId.ToString(), ["reason"] = reason, ["expected_count"] = expectedCount.ToString()
        });
    }

    private static void LogConfirmation(string eventName, ReplicationConfirmationSnapshot snapshot, PeerId peerId, long? latency)
    {
        var fields = ConfirmationFields(snapshot);
        fields["peer_id"] = peerId.ToString();
        fields["latency_ms"] = latency?.ToString();
        GameLog.Info("gameplay.replication", eventName, fields: fields);
    }

    private IReadOnlyDictionary<string, string?>? ResolveSteamMetadata(PeerId peer)
    {
        if (_adapter is null || !_adapter.TryGetSteamUserForPeer(peer, out SteamUserId user)) return null;
        return new Dictionary<string, string?> { ["steam_id"] = user.ToString() };
    }

    private void LogSnapshot(string reason)
    {
        if (Multiplayer.IsServer())
        {
            GameLog.Info("gameplay.snapshot", reason, fields: new Dictionary<string, string?>
            {
                ["runtime_mode"] = _runtime.Mode.ToString(), ["connected_peers"] = _peers.Count.ToString(), ["authoritative_players"] = _players.Count.ToString(),
                ["network_objects"] = _world.Count.ToString(), ["door_id"] = _doorId?.ToString(), ["door_is_open"] = _door?.IsOpen.ToString(), ["door_revision"] = _door?.Revision.ToString()
            });
            return;
        }

        long localPeerValue = Multiplayer.GetUniqueId();
        var observedPlayers = _world.Objects
            .Select(networkObject => new { NetworkObject = networkObject, Player = networkObject.Host as RawPlayer })
            .Where(item => item.Player is not null)
            .ToArray();
        var localPlayer = observedPlayers.FirstOrDefault(item => localPeerValue > 0 && item.NetworkObject.OwnerPeerId == new PeerId(localPeerValue));
        var observedDoor = _world.Objects
            .Select(networkObject => new { NetworkObject = networkObject, Door = networkObject.Host as RawDoor })
            .FirstOrDefault(item => item.Door is not null);
        GameLog.Info("gameplay.snapshot", reason, fields: new Dictionary<string, string?>
        {
            ["runtime_mode"] = _runtime.Mode.ToString(), ["local_peer_id"] = localPeerValue.ToString(), ["observed_players"] = observedPlayers.Length.ToString(),
            ["network_objects"] = _world.Count.ToString(), ["local_player_id"] = localPlayer?.Player?.PlayerId.ToString(), ["local_player_object_id"] = localPlayer?.NetworkObject.Id.ToString(),
            ["door_id"] = observedDoor?.NetworkObject.Id.ToString(), ["door_is_open"] = observedDoor?.Door?.IsOpen.ToString(), ["door_revision"] = observedDoor?.Door?.Revision.ToString()
        });
    }

    private bool IsSteamBasicScenario => string.Equals(_testScenario, "steam_basic", StringComparison.OrdinalIgnoreCase);

    private void TryRunHostScenario()
    {
        if (!IsSteamBasicScenario || _scenarioDoorMutationStarted || _door is null || _doorId is null)
            return;

        NetworkPeer[] remotePeers = _peers.Peers.Where(peer => !peer.IsLocal).ToArray();
        if (remotePeers.Length != 1 || _players.Count < 2 || _world.Count < 3)
            return;

        _scenarioDoorMutationStarted = true;
        LogScenario("host_world_ready", new Dictionary<string, string?>
        {
            ["remote_peer_id"] = remotePeers[0].Id.ToString(),
            ["players"] = _players.Count.ToString(),
            ["network_objects"] = _world.Count.ToString()
        });
        SetDoorOpenForScenario();
    }

    private void SetDoorOpenForScenario()
    {
        if (_door is null) return;
        _door.SetOpenOnAuthority(true);
        _scenarioDoorRevision = _door.Revision;
        LogScenario("host_door_mutated", new Dictionary<string, string?>
        {
            ["network_object_id"] = _doorId?.ToString(),
            ["revision"] = _door.Revision.ToString(),
            ["is_open"] = _door.IsOpen.ToString()
        });
    }

    private void TryReportClientScenarioState()
    {
        if (!IsSteamBasicScenario) return;

        RawDoor? observedDoor = _world.Objects
            .Select(networkObject => networkObject.Host as RawDoor)
            .FirstOrDefault(door => door is not null);
        int playerCount = _world.Objects.Count(networkObject => networkObject.Host is RawPlayer);
        if (!_scenarioClientWorldReported && observedDoor is not null && playerCount >= 2)
        {
            _scenarioClientWorldReported = true;
            LogScenario("client_world_ready", new Dictionary<string, string?>
            {
                ["players"] = playerCount.ToString(),
                ["network_objects"] = _world.Count.ToString(),
                ["door_id"] = observedDoor.GetNode<NetworkObject>("NetworkObject").Id.ToString()
            });
        }

        if (_scenarioClientDoorReported || observedDoor is null || !observedDoor.IsOpen || observedDoor.Revision < 1)
            return;

        _scenarioClientDoorReported = true;
        LogScenario("client_passed", new Dictionary<string, string?>
        {
            ["assertion"] = "authoritative_door_revision_observed",
            ["revision"] = observedDoor.Revision.ToString(),
            ["is_open"] = observedDoor.IsOpen.ToString()
        });
    }

    private void LogScenario(string eventName, IReadOnlyDictionary<string, string?>? fields = null)
    {
        if (!IsSteamBasicScenario) return;

        var scenarioFields = fields is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(fields);
        scenarioFields["scenario"] = _testScenario;
        scenarioFields["test_run_id"] = _testRunId;
        scenarioFields["role"] = _testRole ?? (Multiplayer.IsServer() ? "host" : "client");
        GameLog.Info("ab_test.scenario", eventName, fields: scenarioFields);
    }

    private void LogScenarioFailure(string eventName)
    {
        if (!IsSteamBasicScenario) return;
        GameLog.Warning("ab_test.scenario", eventName, fields: new Dictionary<string, string?>
        {
            ["scenario"] = _testScenario,
            ["test_run_id"] = _testRunId,
            ["role"] = _testRole ?? (Multiplayer.IsServer() ? "host" : "client")
        });
    }

    private void LogPeerStatus(string reason)
    {
        MultiplayerPeer? peer = _session?.ActivePeer;
        if (peer is null || !GodotObject.IsInstanceValid(peer)) return;

        MultiplayerPeer.ConnectionStatus status = peer.GetConnectionStatus();
        bool changed = _lastPeerConnectionStatus != status;
        _lastPeerConnectionStatus = status;
        SteamLobby? lobby = _session?.Lobby;
        long uniqueId = 0;
        try { uniqueId = Multiplayer.GetUniqueId(); }
        catch (Exception) { }
        GameLog.Info("steam.peer_status", changed ? "changed" : "sampled", fields: new Dictionary<string, string?>
        {
            ["reason"] = reason,
            ["role"] = _testRole,
            ["peer_type"] = peer.GetType().Name,
            ["connection_status"] = status.ToString(),
            ["local_unique_id"] = uniqueId.ToString(),
            ["lobby_id"] = lobby?.Id.ToString(),
            ["local_steam_id"] = _adapter?.LocalUser.Id.ToString(),
            ["owner_steam_id"] = lobby?.OwnerId.ToString(),
            ["member_count"] = lobby?.Members.Count.ToString()
        });
    }

    private static string? ReadArgument(IEnumerable<string> arguments, string prefix)
    {
        string? argument = arguments.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return null;

        string value = argument[prefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
