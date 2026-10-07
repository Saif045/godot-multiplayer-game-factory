using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GameFactory.Shell;

namespace GameFactory.Sandbox.Shell;

/// <summary>Two real local Godot RPC/spawner peers, with a diagnostic byte tap.</summary>
public partial class RoundRpcProbe : Node
{
    private OnlineGameplayWorld _gameplay = null!;
    private MultiplayerPeerExtension _tap = null!;
    private int _readyRound;
    private int _clearedRound;
    private bool _done;

    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        ENetMultiplayerPeer peer = new();
        int result = 1;
        try
        {
            bool host = OS.GetCmdlineUserArgs().Contains("--rpc-host");
            Error error = host ? peer.CreateServer(24871) : peer.CreateClient("127.0.0.1", 24871);
            Require(error == Error.Ok, "ENet creation failed.");
            _tap = (MultiplayerPeerExtension)GD.Load<GDScript>("res://sandbox/shell/rpc_packet_trace.gd").New().AsGodotObject();
            _tap.Call("configure", peer, this);
            Multiplayer.MultiplayerPeer = _tap;
            Node shell = GD.Load<PackedScene>("res://factory/shell/online_gameplay.tscn").Instantiate();
            _gameplay = shell.GetNode<OnlineGameplayWorld>("OnlineGameplayWorld");
            shell.RemoveChild(_gameplay);
            shell.Free();
            AddChild(_gameplay);
            await Until(() => GetNode("/root/NetworkTime").Call("is_initial_sync_done").AsBool() && Multiplayer.GetPeers().Length > 0);
            if (host)
            {
                for (int round = 1; round <= 3; round++)
                {
                    _gameplay.Start();
                    Rpc(MethodName.RoundReadyRpc, round);
                    await Until(() => _readyRound == round);
                    long tick = GetNode("/root/NetworkTime").Get("tick").AsInt64();
                    await Until(() => GetNode("/root/NetworkTime").Get("tick").AsInt64() >= tick + 12);
                    Rpc(MethodName.ResetRpc, round);
                    await _gameplay.ResetRoundAsync();
                    await Until(() => _clearedRound == round);
                    Require(_gameplay.IsEmpty, "World did not clear.");
                    GD.Print($"RPC_ROUND_CLEARED round={round} missing={_tap.Get("missing_rpcs")}");
                }
                Rpc(MethodName.FinishRpc);
                result = _tap.Get("missing_rpcs").AsInt32() == 0 ? 0 : 2;
            }
            else
            {
                await Until(() => _done, 15000);
                result = _tap.Get("missing_rpcs").AsInt32() == 0 ? 0 : 2;
            }
        }
        catch (Exception exception) { GD.PushError($"RPC_PROBE_FAIL: {exception}"); }
        finally
        {
            _gameplay?.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Multiplayer.MultiplayerPeer = null;
            peer.Close();
            _tap?.Dispose();
            peer.Dispose();
            GetTree().Quit(result);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void RoundReadyRpc(int round)
    {
        _gameplay.Start();
        await Until(() => _gameplay.GetNode<GameFactory.Networking.World.NetworkWorld>("NetworkWorld").Count == 4);
        RpcId(1, MethodName.ReadyRpc, round);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReadyRpc(int round) => _readyRound = round;

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void ResetRpc(int round)
    {
        await _gameplay.ResetRoundAsync();
        RpcId(1, MethodName.ClearedRpc, round);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClearedRpc(int round) => _clearedRound = round;

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void FinishRpc() => _done = true;

    private async Task Until(Func<bool> condition, ulong timeout = 10000)
    {
        ulong deadline = Time.GetTicksMsec() + timeout;
        while (!condition())
        {
            Require(Time.GetTicksMsec() < deadline, "Observable RPC probe condition timed out.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }
}
