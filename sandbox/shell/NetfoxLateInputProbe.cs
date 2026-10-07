using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GameFactory.Networking.Netfox.Player3D;
using GameFactory.Networking.World;
using GameFactory.Shell;

namespace GameFactory.Sandbox.Shell;

/// <summary>Deliberately delivers a real input packet after its player is gone.</summary>
public partial class NetfoxLateInputProbe : Node
{
    private OnlineGameplayWorld _gameplay = null!;
    private MultiplayerPeerExtension _tap = null!;
    private Node _observer = null!;
    private int _readyRound;
    private bool _cleared;
    private bool _done;
    private bool _clientMoved;
    private NetworkWorld World => _gameplay.GetNode<NetworkWorld>("NetworkWorld");
    private NetworkPlayer3D RemotePlayer => World.Objects.Select(obj => obj.Host)
        .OfType<NetworkPlayer3D>().Single(player => player.GetNode("Input").GetMultiplayerAuthority() != 1);

    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        ENetMultiplayerPeer native = new();
        int result = 1;
        try
        {
            bool host = OS.GetCmdlineUserArgs().Contains("--rpc-host");
            Require((host ? native.CreateServer(24872) : native.CreateClient("127.0.0.1", 24872)) == Error.Ok, "ENet preflight");
            _tap = (MultiplayerPeerExtension)GD.Load<GDScript>("res://sandbox/shell/late_input_packet_peer.gd").New().AsGodotObject();
            _tap.Call("configure", native, this);
            Multiplayer.MultiplayerPeer = _tap;
            _observer = (Node)GD.Load<GDScript>("res://sandbox/shell/late_input_observer.gd").New().AsGodotObject();
            AddChild(_observer);
            _observer.Call("configure", _tap);
            Node shell = GD.Load<PackedScene>("res://factory/shell/online_gameplay.tscn").Instantiate();
            _gameplay = shell.GetNode<OnlineGameplayWorld>("OnlineGameplayWorld");
            shell.RemoveChild(_gameplay);
            shell.Free();
            AddChild(_gameplay);
            await Until(() => GetNode("/root/NetworkTime").Call("is_initial_sync_done").AsBool() && Multiplayer.GetPeers().Length > 0);
            ulong peerInstance = Multiplayer.MultiplayerPeer.GetInstanceId();
            ulong timeInstance = GetNode("/root/NetworkTime").GetInstanceId();
            if (host)
            {
                _gameplay.Start();
                Rpc(MethodName.StartRpc, 1);
                await Until(() => _readyRound == 1 && World.Count == 4);
                NetworkPlayer3D oldPlayer = RemotePlayer;
                Node oldInput = oldPlayer.GetNode("Input");
                long oldId = _observer.Call("identity_id", oldInput).AsInt64();
                string oldPath = oldInput.GetPath().ToString();
                float oldX = oldPlayer.Position.X;
                await Until(() => oldPlayer.Position.X > oldX + 0.5f && _clientMoved);
                Require(_observer.Call("request_replay", oldPlayer.GetNode("Simulation")).AsBool(), "Host replay history unavailable");
                await Until(() => _observer.Get("replay_seen").AsBool());
                GD.Print("LATE_INPUT_MOVEMENT round=1 host_authoritative=true client_local=true rollback_replayed=true");
                _tap.Call("arm");
                await Until(() => _tap.Call("has_held").AsBool());
                Require(oldId > 0, "Old input identity not registered");
                Rpc(MethodName.ResetRpc);
                await _gameplay.ResetRoundAsync();
                await Until(() => _cleared);
                Require(_gameplay.IsEmpty && !GodotObject.IsInstanceValid(oldPlayer), "Old player survived removal");
                _gameplay.Start();
                Rpc(MethodName.StartRpc, 2);
                await Until(() => _readyRound == 2 && World.Count == 4);
                NetworkPlayer3D replacement = RemotePlayer;
                Node newInput = replacement.GetNode("Input");
                await Until(() => _observer.Call("identity_id", newInput).AsInt64() > oldId);
                _observer.Call("prepare", oldId, oldPath, newInput);
                Require(_observer.Call("removed_identity").AsBool(), "Old identity still resolves");
                Require(newInput.Get("movement").AsVector2() == Vector2.Zero, "Replacement input was not neutral");
                _tap.Call("release");
                await Until(() => _observer.Get("observed").AsBool());
                Require(_observer.Get("safe").AsBool(), "Late data contaminated replacement or skipped wrong command");
                float newX = replacement.Position.X;
                _clientMoved = false;
                Rpc(MethodName.MoveRpc);
                await Until(() => replacement.Position.X > newX + 0.5f && _clientMoved);
                Require(Multiplayer.MultiplayerPeer.GetInstanceId() == peerInstance && GetNode("/root/NetworkTime").GetInstanceId() == timeInstance, "Peer/time replaced");
                GD.Print("LATE_INPUT_PASS unreliable persistent_receiver stale_identity_skipped new_identity_distinct next_round_movement rollback_replay no_restart");
                // Complete authoritative replicated teardown before terminal
                // process cleanup; clients must not free replicated players
                // while the host's despawn commands are still arriving.
                _cleared = false;
                Rpc(MethodName.ResetRpc);
                await _gameplay.ResetRoundAsync();
                await Until(() => _cleared);
                Require(_gameplay.IsEmpty, "Terminal world did not clear");
                Rpc(MethodName.FinishRpc);
                result = 0;
            }
            else
            {
                await Until(() => _done, 20000);
                GD.Print("LATE_INPUT_CLIENT_PASS");
                result = 0;
            }
        }
        catch (Exception exception) { GD.PushError($"LATE_INPUT_FAIL: {exception}"); }
        finally
        {
            Input.ActionRelease("move_right");
            _gameplay?.QueueFree();
            _observer?.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Multiplayer.MultiplayerPeer = null;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            native.Close();
            _tap?.Call("detach");
            _tap?.Dispose();
            native.Dispose();
            GetTree().Quit(result);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void StartRpc(int round)
    {
        try
        {
            _gameplay.Start();
            await Until(() => World.Count == 4);
            Node input = RemotePlayer.GetNode("Input");
            await Until(() => _observer.Call("remote_identity_id", input, 1).AsInt64() > 0);
            RpcId(1, MethodName.ReadyRpc, round);
            if (round == 1) MoveRpc();
        }
        catch (Exception exception) { GD.PushError($"LATE_INPUT_FAIL: {exception}"); GetTree().Quit(1); }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReadyRpc(int round) => _readyRound = round;

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void MoveRpc()
    {
        try
        {
            float x = RemotePlayer.Position.X;
            Input.ActionPress("move_right");
            await Until(() => RemotePlayer.Position.X > x + 0.5f);
            GD.Print($"LATE_INPUT_LOCAL_MOVED peer={Multiplayer.GetUniqueId()} delta_x={RemotePlayer.Position.X - x:F3}");
            Require(_observer.Call("request_replay", RemotePlayer.GetNode("Simulation")).AsBool(), "Client replay history unavailable");
            await Until(() => _observer.Get("replay_seen").AsBool());
            RpcId(1, MethodName.MovedRpc);
        }
        catch (Exception exception) { GD.PushError($"LATE_INPUT_FAIL: {exception}"); GetTree().Quit(1); }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void MovedRpc() => _clientMoved = true;

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void ResetRpc()
    {
        try
        {
            Input.ActionRelease("move_right");
            await _gameplay.ResetRoundAsync();
            RpcId(1, MethodName.ClearedRpc);
        }
        catch (Exception exception) { GD.PushError($"LATE_INPUT_FAIL: {exception}"); GetTree().Quit(1); }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClearedRpc() => _cleared = true;

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void FinishRpc() => _done = true;

    private async Task Until(Func<bool> condition, ulong timeout = 10000)
    {
        ulong deadline = Time.GetTicksMsec() + timeout;
        while (!condition())
        {
            Require(Time.GetTicksMsec() < deadline, "Observable condition timed out");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }
}
