using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Gameplay.Carry;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Netfox.Player3D;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Players;
using GameFactory.Networking.World;
using GameFactory.Runtime;

namespace GameFactory.Shell;

/// <summary>
/// The small, visible gameplay composition used by the online shell. Steam
/// session ownership stays with <see cref="OnlineGameplayShell"/>; this node
/// only creates and removes the server-authoritative Netfox world.
/// </summary>
public partial class OnlineGameplayWorld : Node3D
{
    private readonly PeerRegistry _peers = new();
    private readonly PlayerRegistry _players = new();
    private readonly RuntimeContext _runtime = new();
    private NetworkWorld _world = null!;
    private PlayerLifecycle? _playerLifecycle;
    private bool _started;

    public bool IsEmpty => !_started && _playerLifecycle is null &&
        _players.Count == 0 && _peers.Count == 0 && _world.Count == 0;

    public Dictionary<string, string?> StateFields() => new()
    {
        ["players"] = _players.Count.ToString(),
        ["peers"] = _peers.Count.ToString(),
        ["network_objects"] = _world.Count.ToString(),
        ["player_lifecycle_active"] = (_playerLifecycle is not null).ToString()
    };

    [Export] public PackedScene PlayerScene { get; set; } = null!;
    [Export] public PackedScene SwitchScene { get; set; } = null!;
    [Export] public PackedScene CarryableScene { get; set; } = null!;

    public override void _Ready()
    {
        _world = GetNode<NetworkWorld>("NetworkWorld");
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
    }

    public void Start()
    {
        if (_started) return;
        if (Multiplayer.IsServer() && !IsEmpty)
            throw new InvalidOperationException("The previous gameplay world is not empty.");
        _started = true;

        if (!Multiplayer.IsServer())
        {
            _runtime.SetMode(RuntimeMode.Client);
            GameLog.Info("shell.gameplay", "client_started");
            return;
        }

        _runtime.SetMode(RuntimeMode.ListenServer);
        _peers.Add(PeerId.Server, isLocal: true);
        foreach (int peerValue in Multiplayer.GetPeers())
        {
            // SteamMultiplayerPeer may include the listen server (1) in this
            // list. The server was registered above as local, not remote.
            if (peerValue > PeerId.Server.Value)
                _peers.Add(new PeerId(peerValue), isLocal: false);
        }

        _playerLifecycle = new PlayerLifecycle(_peers, _players, _runtime, SpawnPlayer, _world.Despawn);
        _world.Spawn<InteractableSwitch>(SwitchScene, PeerId.Server, new Godot.Collections.Dictionary
        {
            ["spawn_position"] = new Vector3(0, 0.625f, -1.5f)
        });
        _world.Spawn<CarryableItem>(CarryableScene, PeerId.Server, new Godot.Collections.Dictionary
        {
            ["spawn_transform"] = new Transform3D(Basis.Identity, new Vector3(0, 0.35f, 1.5f))
        });
        GameLog.Info("shell.gameplay", "host_started", fields: new Dictionary<string, string?>
        {
            ["players"] = _players.Count.ToString(),
            ["network_objects"] = _world.Count.ToString()
        });
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _playerLifecycle?.Dispose();
        _playerLifecycle = null;
        _peers.Clear();
        GameLog.Info("shell.gameplay", "stopped");
    }

    /// <summary>Reset only round objects; the shell keeps its connected Steam session.</summary>
    public async Task ResetRoundAsync()
    {
        _started = false;
        GameLog.Info("shell.gameplay", "round_teardown_started", fields: StateFields());
        if (Multiplayer.IsServer())
        {
            // Peer removal must reach the live lifecycle before it is disposed.
            // Queued player exits release hidden items and remove GAS effects.
            _peers.Clear();
            await WaitUntilAsync(() => !_world.Objects.Any(obj => obj.Host is NetworkPlayer3D));
            foreach (NetworkObject obj in _world.Objects.ToArray())
                _world.Despawn(obj.Id);
        }
        // Clients observe authoritative MultiplayerSpawner despawns; they never
        // free replicated objects themselves or reset the world's ID allocator.
        await WaitUntilAsync(() => _world.Count == 0);
        _playerLifecycle?.Dispose();
        _playerLifecycle = null;
        if (!IsEmpty) throw new InvalidOperationException("Round teardown left gameplay state registered.");
        GameLog.Info("shell.gameplay", "round_world_cleared", fields: StateFields());
    }

    private async Task WaitUntilAsync(Func<bool> condition)
    {
        ulong deadline = Time.GetTicksMsec() + 10000;
        while (!condition())
        {
            if (!IsInsideTree() || Time.GetTicksMsec() >= deadline)
                throw new TimeoutException("Round world did not clear within 10 seconds.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    public override void _ExitTree()
    {
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Stop();
    }

    private NetworkObjectId SpawnPlayer(NetworkPeer peer, PlayerId playerId)
    {
        Vector3 position = playerId.Value == 1 ? new Vector3(-2, 2, 0) : new Vector3(2, 2, 0);
        NetworkPlayer3D player = _world.Spawn<NetworkPlayer3D>(PlayerScene, peer.Id, new Godot.Collections.Dictionary
        {
            ["spawn_position"] = position
        });
        return player.GetNetworkObject().Id;
    }

    private void OnPeerDisconnected(long peerValue)
    {
        if (_started && Multiplayer.IsServer())
        {
            _peers.Remove(new PeerId(peerValue));
            GameLog.Info("shell.gameplay", "gameplay_peer_left", fields: StateFields());
        }
    }
}
