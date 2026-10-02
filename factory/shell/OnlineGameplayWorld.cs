using System;
using System.Collections.Generic;
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

    [Export] public PackedScene PlayerScene { get; set; } = null!;
    [Export] public PackedScene SwitchScene { get; set; } = null!;
    [Export] public PackedScene CarryableScene { get; set; } = null!;

    public override void _Ready()
    {
        _world = GetNode<NetworkWorld>("NetworkWorld");
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
    }

    public void Start()
    {
        if (_started) return;
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
            _peers.Add(new PeerId(peerValue), isLocal: false);

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

    public override void _ExitTree()
    {
        Multiplayer.PeerConnected -= OnPeerConnected;
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

    private void OnPeerConnected(long peerValue)
    {
        // Godot emits the server ID as the transport becomes active. It is
        // already registered as the local server peer in Start().
        if (_started && Multiplayer.IsServer() && peerValue > PeerId.Server.Value)
            _peers.Add(new PeerId(peerValue), isLocal: false);
    }

    private void OnPeerDisconnected(long peerValue)
    {
        if (_started && Multiplayer.IsServer())
            _peers.Remove(new PeerId(peerValue));
    }
}
