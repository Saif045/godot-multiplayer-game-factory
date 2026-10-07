using System;
using System.Linq;
using System.Reflection;
using Godot;
using GameFactory.Gameplay.Carry;
using GameFactory.Gameplay.Inventory;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Netfox.Player3D;
using GameFactory.Networking.Peers;
using GameFactory.Networking.World;
using GameFactory.Shell;

namespace GameFactory.Sandbox.Shell;

/// <summary>Local composition check, not a substitute for two-account shell acceptance.</summary>
public partial class RoundReuseProbe : Node
{
    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        ENetMultiplayerPeer peer = new();
        OnlineGameplayWorld? gameplay = null;
        int result = 1;
        try
        {
            Require(peer.CreateServer(0) == Error.Ok, "Local ENet server could not start.");
            Multiplayer.MultiplayerPeer = peer;
            Node networkTime = GetNode("/root/NetworkTime");
            ulong syncDeadline = Time.GetTicksMsec() + 5000;
            while (!networkTime.Call("is_initial_sync_done").AsBool())
            {
                Require(Time.GetTicksMsec() < syncDeadline, "Local Netfox startup timed out.");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Node shell = GD.Load<PackedScene>("res://factory/shell/online_gameplay.tscn").Instantiate();
            gameplay = shell.GetNode<OnlineGameplayWorld>("OnlineGameplayWorld");
            shell.RemoveChild(gameplay);
            shell.Free();
            AddChild(gameplay);
            NetworkWorld world = gameplay.GetNode<NetworkWorld>("NetworkWorld");
            long previousMax = 0;
            for (int round = 1; round <= 3; round++)
            {
                Require(gameplay.IsEmpty, "World must be empty before Start.");
                gameplay.Start();
                Require(world.Count == 3, "Expected one player, switch, and cube.");
                Require(world.Objects.All(obj => obj.Id.Value > previousMax), "Object IDs were reused.");
                previousMax = world.Objects.Max(obj => obj.Id.Value);
                NetworkPlayer3D player = world.Objects.Select(obj => obj.Host).OfType<NetworkPlayer3D>().Single();
                // Normal Netfox node registration is deferred; let it finish
                // before exercising the lifetime of the populated round.
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(player.GasHealth == 100 && player.GasStamina == 100 && !player.IsIncapacitated &&
                    player.InventoryStoredItemNetworkObjectId == 0 && player.InventoryEquippedItemNetworkObjectId == 0 &&
                    player.GasMoveSpeed == 6 && player.RespawnRevision == 0 && player.GasDashAuthorizationRevision == 0,
                    "New player retained round state.");
                Require(!world.Objects.Select(obj => obj.Host).OfType<InteractableSwitch>().Single().IsOn,
                    "Switch retained round state.");
                CarryableItem item = world.Objects.Select(obj => obj.Host).OfType<CarryableItem>().Single();
                Require(item.StorageState == CarryableItem.WorldState && item.HolderNetworkObjectId == 0,
                    "Cube retained round state.");
                Require(player.GetNode<PlayerCarrier>("PlayerCarrier").TryPickup(item), "Pickup failed.");
                PlayerInventory inventory = player.GetNode<PlayerInventory>("PlayerInventory");
                Dispatch(inventory, "HandleStore");
                if (round != 2) Dispatch(inventory, "HandleEquip");
                Require(round == 2 ? inventory.HasStoredItem : inventory.HasEquippedItem,
                    "Round did not contain meaningful hidden item state.");
                await gameplay.ResetRoundAsync();
                Require(gameplay.IsEmpty, "Reset left registries or objects active.");
                Require(!GodotObject.IsInstanceValid(player) && !GodotObject.IsInstanceValid(item),
                    "Old round nodes survived reset.");
                Require(ReferenceEquals(Multiplayer.MultiplayerPeer, peer), "Round reset replaced transport.");
                GD.Print($"ROUND_REUSE_PASS round={round} max_id={previousMax}");
            }
            result = 0;
        }
        catch (Exception exception) { GD.PushError($"ROUND_REUSE_FAIL: {exception}"); }
        finally
        {
            gameplay?.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Multiplayer.MultiplayerPeer = null;
            peer.Close();
            peer.Dispose();
            GetTree().Quit(result);
        }
    }

    private static void Dispatch(PlayerInventory inventory, string method) =>
        typeof(PlayerInventory).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(inventory, new object[] { PeerId.Server });

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }
}
