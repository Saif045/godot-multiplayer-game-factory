using System.Linq;
using Godot;
using GameFactory.Core;
using GameFactory.Networking.Transport;
using GameFactory.Networking.Sessions;
using GameFactory.Networking.Peers;

namespace GameFactory.Sandbox.Replication;

public partial class ReplicationProbe : Node
{
  private const int Port = 7000;
  private const int MaxClients = 8;
  private const string DefaultAddress = "127.0.0.1";

  private readonly RuntimeContext _runtime = new();

  private INetworkTransport? _transport;
  private NetworkSession? _session;
  private readonly PeerRegistry _peers =
      new();

  private readonly PackedScene _doorScene =
GD.Load<PackedScene>(
    "res://sandbox/replication/objects/raw_door.tscn");
  private Node? _runtimeDoor;

  private MultiplayerSpawner _spawner = null!;

  private void SpawnDoor()
  {
    if (!Multiplayer.IsServer())
      return;

    _runtimeDoor =
        _doorScene.Instantiate();

    _runtimeDoor.Name = "RuntimeDoor";

    GetNode<Node>("spawn_root")
        .AddChild(_runtimeDoor);

    GD.Print(
        "[spawn][server] spawned RuntimeDoor");
  }
  private void DespawnDoor()
  {
    if (!Multiplayer.IsServer())
      return;

    if (_runtimeDoor is null)
      return;

    GD.Print(
        "[spawn][server] despawning RuntimeDoor");

    _runtimeDoor.QueueFree();
    _runtimeDoor = null;
  }
  public override void _UnhandledInput(
    InputEvent inputEvent)
  {
    if (inputEvent is not InputEventKey key)
      return;

    if (!key.Pressed || key.Echo)
      return;

    if (key.Keycode == Key.Delete)
    {
      DespawnDoor();
    }
  }

  public override void _Ready()
  {
    _transport = new ENetTransport(
        Multiplayer);

    _session = new NetworkSession(
    _transport,
    _runtime,
    _peers);

    SubscribeToSessionEvents();
    SubscribeToPeerEvents();
    // SubscribeToTransportDebugEvents();
    _spawner =
        GetNode<MultiplayerSpawner>(
            "MultiplayerSpawner");

    _spawner.Despawned += node =>
    {
      GD.Print(
      $"[spawn][remote] despawned {node.Name}");
    };

    string[] args =
        OS.GetCmdlineUserArgs();

    if (args.Contains("--server"))
    {
      StartServer(
          HostMode.Listen);

      return;
    }

    if (args.Contains("--dedicated-server"))
    {
      StartServer(
          HostMode.Dedicated);

      return;
    }

    if (args.Contains("--client"))
    {
      StartClient();
      return;
    }

    GD.Print(
        "[probe] No runtime mode selected. " +
        "Use --server or --client.");
  }

  public override void _ExitTree()
  {
    if (_session is not null)
    {
      if (_runtime.Mode == RuntimeMode.Client)
      {
        _session.Leave();
      }
      else if (_runtime.IsServer)
      {
        _session.ShutdownHost();
      }
    }

    _transport?.Dispose();
  }

  private void StartServer(
    HostMode hostMode)
  {
    SessionResult result =
        _session!.Host(
            Port,
            MaxClients,
            hostMode);

    if (!result.Success)
    {
      GD.PushError(
          $"[server] {result.Error}");
      return;
    }

    GD.Print($"[server] mode: {hostMode}");
    GD.Print($"[server] listening on UDP {Port}");
    GD.Print(
        $"[server] peer id: {Multiplayer.GetUniqueId()}");

    SpawnDoor();
  }

  private void StartClient()
  {
    SessionResult result =
        _session!.Join(
            DefaultAddress,
            Port);

    if (!result.Success)
    {
      GD.PushError(
          $"[client] {result.Error}");

      return;
    }

    GD.Print(
        $"[client] connecting to " +
        $"{DefaultAddress}:{Port}");
  }
  private void SubscribeToSessionEvents()
  {
    _session!.StateChanged +=
        (previous, next) =>
        {
          GD.Print(
                  $"[session] " +
                  $"{previous} -> {next}");

          if (next == SessionState.Failed)
          {
            GD.Print(
                    $"[session] end reason: " +
                    $"{_session.LastEndReason}");

            GD.Print(
                    $"[session] error: " +
                    $"{_session.LastError}");
          }

          if (next == SessionState.Offline
                  && _session.LastEndReason
                      != SessionEndReason.None)
          {
            GD.Print(
                    $"[session] ended: " +
                    $"{_session.LastEndReason}");
          }
        };
  }
  private void SubscribeToPeerEvents()
  {
    _peers.PeerAdded += peer =>
    {
      GD.Print(
              $"[peers] added: {peer}");
    };

    _peers.PeerRemoved += peer =>
    {
      GD.Print(
              $"[peers] removed: {peer}");
    };
  }
  private void SubscribeToTransportDebugEvents()
  {
    _transport!.PeerConnected += peerId =>
    {
      GD.Print(
              $"[transport] peer connected: " +
              $"{peerId}");
    };

    _transport.PeerDisconnected += peerId =>
    {
      GD.Print(
              $"[transport] peer disconnected: " +
              $"{peerId}");
    };
  }
}