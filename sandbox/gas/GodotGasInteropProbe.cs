using System;
using System.Collections.Generic;
using System.Reflection;
using GameFactory.Gameplay.Carry;
using GameFactory.Gameplay.Interaction;
using GameFactory.Gameplay.Inventory;
using GameFactory.Networking.Netfox.Player3D;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;
using GameFactory.Networking.World;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Gameplay.Gas;

namespace GameFactory.Sandbox.Gas;

/// <summary>Headless proof that C# can create, invoke, and read a GodotGAS ASC.</summary>
public partial class GodotGasInteropProbe : Node
{
    public override async void _Ready()
    {
        GameLog.EnsureInitialized();
        try
        {
            GodotGasAdapter gas = GodotGasAdapter.Create(this);
            int initialHealth = gas.GetHealth();
            bool abilityGranted = gas.IsSelfDamageGranted();
            int resultingHealth = gas.ApplySelfDamage();
            bool attributeChangedObserved = gas.DidObserveSelfDamageChange();
            GasSnapshot snapshot = gas.CaptureSnapshot();
            GodotGasAdapter reconstructed = GodotGasAdapter.Create(this, authoritative: false);
            reconstructed.ApplySnapshot(snapshot);
            int reconstructedHealth = reconstructed.GetHealth();
            bool fortifyActivated = gas.ApplyFortify();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GasSnapshot fortifyActive = gas.CaptureSnapshot();
            bool cooldownBlockedRepeat = !gas.ApplyFortify();
            await ToSignal(GetTree().CreateTimer(3.25), SceneTreeTimer.SignalName.Timeout);
            GasSnapshot fortifyExpired = gas.CaptureSnapshot();
            await ToSignal(GetTree().CreateTimer(2.25), SceneTreeTimer.SignalName.Timeout);
            GasSnapshot cooldownExpired = gas.CaptureSnapshot();
            bool speedBoostActivated = gas.ApplySpeedBoost();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            float boostedMoveSpeed = gas.GetMoveSpeed();
            bool speedBoostBlockedWhileActive = !gas.ApplySpeedBoost();
            await ToSignal(GetTree().CreateTimer(3.25), SceneTreeTimer.SignalName.Timeout);
            float restoredMoveSpeed = gas.GetMoveSpeed();
            Node equipmentSource = new() { Name = "EquipmentCubeSource" };
            AddChild(equipmentSource);
            bool equipmentCapabilityApplied = gas.ApplyEquipmentCubeCapability(equipmentSource);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            float equippedMoveSpeed = gas.GetMoveSpeed();
            bool equipmentCapabilityActive = gas.IsEquipmentCubeCapabilityActive();
            bool duplicateEquipmentCapabilityBlocked = !gas.ApplyEquipmentCubeCapability(equipmentSource);
            bool equipmentCapabilityRemoved = gas.RemoveEquipmentCubeCapability(equipmentSource);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            float unequippedMoveSpeed = gas.GetMoveSpeed();
            gas.SetSprintIntent(true);
            await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
            float sprintDrainObserved = gas.GetStamina();
            bool sprintStateObserved = gas.IsSprinting();
            await ToSignal(GetTree().CreateTimer(5.2), SceneTreeTimer.SignalName.Timeout);
            bool exhaustionObserved = gas.IsExhausted();
            float exhaustedStamina = gas.GetStamina();
            gas.SetSprintIntent(false);
            await ToSignal(GetTree().CreateTimer(2.4), SceneTreeTimer.SignalName.Timeout);
            float recoveredStamina = gas.GetStamina();
            bool exhaustionCleared = !gas.IsExhausted();
            float staminaBeforeDash = gas.GetStamina();
            bool dashActivated = gas.ApplyDash();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            float staminaAfterDash = gas.GetStamina();
            float dashCooldownActive = gas.GetDashCooldownRemaining();
            bool dashBlockedByCooldown = !gas.ApplyDash();
            // Cooldown clears in 0.75s; wait through regeneration as well so
            // the second activation proves the independent cost gate clears.
            await ToSignal(GetTree().CreateTimer(2.1), SceneTreeTimer.SignalName.Timeout);
            bool dashAvailableAfterCooldown = gas.ApplyDash();
            if (initialHealth != 100 || !abilityGranted || resultingHealth != 75 || !attributeChangedObserved ||
                snapshot.Health != 75 || reconstructedHealth != 75 || !fortifyActivated ||
                !fortifyActive.IsFortified || fortifyActive.FortifyCooldownRemaining <= 0f ||
                !cooldownBlockedRepeat || fortifyExpired.IsFortified ||
                fortifyExpired.FortifyCooldownRemaining <= 0f ||
                cooldownExpired.FortifyCooldownRemaining > 0.05f ||
                !speedBoostActivated || !Mathf.IsEqualApprox(boostedMoveSpeed, 18f) ||
                !speedBoostBlockedWhileActive || !Mathf.IsEqualApprox(restoredMoveSpeed, 6f) ||
                !equipmentCapabilityApplied || !equipmentCapabilityActive ||
                !Mathf.IsEqualApprox(equippedMoveSpeed, 12f) || !duplicateEquipmentCapabilityBlocked ||
                !equipmentCapabilityRemoved || !Mathf.IsEqualApprox(unequippedMoveSpeed, 6f) ||
                !sprintStateObserved || sprintDrainObserved >= 100f || !exhaustionObserved ||
                exhaustedStamina > 0.05f || !exhaustionCleared || recoveredStamina < 25f ||
                !dashActivated || staminaBeforeDash - staminaAfterDash < 24.5f ||
                dashCooldownActive <= 0f || !dashBlockedByCooldown || !dashAvailableAfterCooldown)
                throw new InvalidOperationException(
                    $"Canonical GodotGAS effect lifecycle did not complete: dashActivated={dashActivated}, " +
                    $"staminaBeforeDash={staminaBeforeDash:F1}, staminaAfterDash={staminaAfterDash:F1}, " +
                    $"dashCooldownActive={dashCooldownActive:F2}, dashBlockedByCooldown={dashBlockedByCooldown}, " +
                    $"dashAvailableAfterCooldown={dashAvailableAfterCooldown}.");

            // Exercise the real ASC's vital effects alongside a persistent source.
            gas.ResetVitals();
            if (!gas.ApplyEquipmentCubeCapability(equipmentSource))
                throw new InvalidOperationException("Vital probe equipment grant failed.");
            gas.SetSprintIntent(true);
            for (int i = 0; i < 4; i++) gas.ApplySelfDamage();
            GasSnapshot downed = gas.CaptureSnapshot();
            reconstructed.ApplySnapshot(downed);
            bool downedMirror = reconstructed.IsDowned() && !reconstructed.IsDead();
            bool actionsBlocked = !gas.ApplyDash() && !gas.ApplyFortify() && !gas.ApplySpeedBoost();
            gas.ApplySelfDamage();
            bool revived = gas.TryRevive();
            bool duplicateReviveBlocked = !gas.TryRevive();
            GasSnapshot revivedState = gas.CaptureSnapshot();
            gas.ApplySelfDamage();
            gas.ApplySelfDamage();
            gas.MarkDead();
            GasSnapshot dead = gas.CaptureSnapshot();
            bool deadReviveBlocked = !gas.TryRevive();
            gas.ResetVitals();
            GasSnapshot respawn = gas.CaptureSnapshot();
            bool equipmentSurvived = gas.IsEquipmentCubeCapabilityActive() && Mathf.IsEqualApprox(gas.GetMoveSpeed(), 12f);
            if (downed.Health != 0 || !downed.IsDowned || downed.IsDead || downed.IsSprinting ||
                !downedMirror || !actionsBlocked || !revived || !duplicateReviveBlocked ||
                revivedState.Health != 50 || revivedState.IsDowned || revivedState.IsDead ||
                dead.Health != 0 || dead.IsDowned || !dead.IsDead || !deadReviveBlocked ||
                respawn.Health != 100 || respawn.Stamina != 100 || respawn.IsDowned || respawn.IsDead ||
                respawn.IsExhausted || respawn.IsSprinting || respawn.DashCooldownRemaining > 0 || !equipmentSurvived)
                throw new InvalidOperationException("Vital lifecycle or equipment preservation failed.");
            if (!gas.RemoveEquipmentCubeCapability(equipmentSource) || !Mathf.IsEqualApprox(gas.GetMoveSpeed(), 6f))
                throw new InvalidOperationException("Preserved equipment source could not be removed.");
            reconstructed.ApplySnapshot(respawn);
            if (reconstructed.IsDowned() || reconstructed.IsDead())
                throw new InvalidOperationException("Vital mirror tags did not clear.");
            GameLog.Info("gas.interop", "vital_probe_passed", fields: new Dictionary<string, string?>
            {
                ["downed_health"] = downed.Health.ToString(), ["revive_health"] = revivedState.Health.ToString(),
                ["dead_health"] = dead.Health.ToString(), ["respawn_health"] = respawn.Health.ToString(),
                ["equipment_survived"] = equipmentSurvived.ToString(), ["actions_blocked"] = actionsBlocked.ToString(),
                ["double_revive_blocked"] = duplicateReviveBlocked.ToString(), ["dead_revive_blocked"] = deadReviveBlocked.ToString()
            });

            await ValidatePlayerComposition();

            GameLog.Info("gas.interop", "probe_passed", fields: new Dictionary<string, string?>
            {
                ["component_type"] = gas.Component.GetClass(),
                ["initial_health"] = initialHealth.ToString(),
                ["ability_granted"] = abilityGranted.ToString(),
                ["resulting_health"] = resultingHealth.ToString(),
                ["attribute_changed_observed"] = attributeChangedObserved.ToString()
                , ["snapshot_health"] = snapshot.Health.ToString()
                , ["reconstructed_health"] = reconstructedHealth.ToString()
                , ["fortify_activated"] = fortifyActivated.ToString()
                , ["fortify_active"] = fortifyActive.IsFortified.ToString()
                , ["repeat_blocked_by_cooldown"] = cooldownBlockedRepeat.ToString()
                , ["fortify_expired"] = (!fortifyExpired.IsFortified).ToString()
                , ["cooldown_cleared"] = (cooldownExpired.FortifyCooldownRemaining <= 0.05f).ToString()
                , ["speed_boost_activated"] = speedBoostActivated.ToString()
                , ["boosted_move_speed"] = boostedMoveSpeed.ToString("F1")
                , ["speed_boost_blocked_while_active"] = speedBoostBlockedWhileActive.ToString()
                , ["restored_move_speed"] = restoredMoveSpeed.ToString("F1")
                , ["equipment_capability_applied"] = equipmentCapabilityApplied.ToString()
                , ["equipment_capability_active"] = equipmentCapabilityActive.ToString()
                , ["equipped_move_speed"] = equippedMoveSpeed.ToString("F1")
                , ["duplicate_equipment_capability_blocked"] = duplicateEquipmentCapabilityBlocked.ToString()
                , ["equipment_capability_removed"] = equipmentCapabilityRemoved.ToString()
                , ["unequipped_move_speed"] = unequippedMoveSpeed.ToString("F1")
                , ["sprint_drain_observed"] = sprintDrainObserved.ToString("F1")
                , ["exhaustion_observed"] = exhaustionObserved.ToString()
                , ["recovered_stamina"] = recoveredStamina.ToString("F1")
                , ["exhaustion_cleared"] = exhaustionCleared.ToString()
                , ["dash_activated"] = dashActivated.ToString()
                , ["dash_stamina_cost"] = (staminaBeforeDash - staminaAfterDash).ToString("F1")
                , ["dash_cooldown_active"] = dashCooldownActive.ToString("F2")
                , ["dash_blocked_by_cooldown"] = dashBlockedByCooldown.ToString()
                , ["dash_available_after_cooldown"] = dashAvailableAfterCooldown.ToString()
            });
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GameLog.Error("gas.interop", "probe_failed", exception.Message);
            GetTree().Quit(1);
        }
    }

    // Invoke the actual private server handlers with a trusted test sender.
    // This exercises stale/malicious requests without adding production test APIs.
    private static void Dispatch(Node component, string method, params object[] arguments) =>
        component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, arguments);

    private static void Require(bool assertion, string reason)
    {
        if (!assertion) throw new InvalidOperationException(reason);
    }

    private async System.Threading.Tasks.Task ValidatePlayerComposition()
    {
        using ENetMultiplayerPeer peer = new();
        Require(peer.CreateServer(0) == Error.Ok, "Local probe ENet preflight failed.");
        Multiplayer.MultiplayerPeer = peer;
        NetworkWorld world = new() { Name = "VitalProbeWorld" };
        AddChild(world);
        PackedScene prefab = GD.Load<PackedScene>("res://factory/networking/netfox/player_3d/network_player_3d.tscn");
        PackedScene itemPrefab = GD.Load<PackedScene>("res://factory/gameplay/carry/carryable_item.tscn");
        NetworkPlayer3D host = world.Spawn<NetworkPlayer3D>(prefab, PeerId.Server,
            new Godot.Collections.Dictionary { ["spawn_position"] = new Vector3(0, 3, 0) });
        NetworkPlayer3D client = world.Spawn<NetworkPlayer3D>(prefab, new PeerId(2),
            new Godot.Collections.Dictionary { ["spawn_position"] = new Vector3(1, 3, 0) });
        CarryableItem item = world.Spawn<CarryableItem>(itemPrefab, PeerId.Server,
            new Godot.Collections.Dictionary { ["spawn_transform"] = Transform3D.Identity });
        NetworkGasComponent clientGas = client.GetNode<NetworkGasComponent>("NetworkGasComponent");
        NetworkGasComponent hostGas = host.GetNode<NetworkGasComponent>("NetworkGasComponent");
        PlayerInteractor hostInteractor = host.GetNode<PlayerInteractor>("PlayerInteractor");
        PlayerInteractor clientInteractor = client.GetNode<PlayerInteractor>("PlayerInteractor");
        PlayerCarrier carrier = client.GetNode<PlayerCarrier>("PlayerCarrier");
        PlayerInventory inventory = client.GetNode<PlayerInventory>("PlayerInventory");
        NetworkObjectId identity = client.GetNetworkObject().Id;
        Require(carrier.TryPickup(item), "Composed carry pickup failed.");
        for (int i = 0; i < 4; i++) Dispatch(clientGas, "HandleActivation", new PeerId(2));
        Require(client.GasIsDowned && item.StorageState == CarryableItem.WorldState && !carrier.HasCarriedItem,
            "Composed Downed/automatic drop failed.");
        Dispatch(clientInteractor, "HandleRequest", new PeerId(2), (NetworkObjectId?)identity);
        Require(client.GasIsDowned, "Self revive was accepted.");
        Dispatch(hostInteractor, "HandleRequest", new PeerId(3), (NetworkObjectId?)identity);
        Require(client.GasIsDowned, "Unowned revive was accepted.");
        Vector3 saved = host.Position;
        host.Position = new Vector3(30, 3, 0);
        Dispatch(hostInteractor, "HandleRequest", PeerId.Server, (NetworkObjectId?)identity);
        Require(client.GasIsDowned, "Out-of-range revive was accepted.");
        host.Position = saved;
        Dispatch(hostInteractor, "HandleRequest", PeerId.Server, (NetworkObjectId?)identity);
        Dispatch(hostInteractor, "HandleRequest", PeerId.Server, (NetworkObjectId?)identity);
        Require(client.GasHealth == 50 && !client.IsIncapacitated, "Composed client revive failed.");
        for (int i = 0; i < 4; i++) Dispatch(hostGas, "HandleActivation", PeerId.Server);
        Dispatch(clientInteractor, "HandleRequest", new PeerId(2), (NetworkObjectId?)host.GetNetworkObject().Id);
        Require(host.GasHealth == 50 && !host.IsIncapacitated, "Composed host revive failed.");
        Require(carrier.TryPickup(item), "Pickup after revive failed.");
        Dispatch(inventory, "HandleStore", new PeerId(2));
        Dispatch(inventory, "HandleEquip", new PeerId(2));
        Require(inventory.HasEquippedItem && client.GasMoveSpeed == 12, "Composed equipment grant failed.");
        Dispatch(clientGas, "HandleActivation", new PeerId(2));
        Dispatch(clientGas, "HandleActivation", new PeerId(2));
        foreach (string method in new[] { "HandleActivation", "HandleDash", "HandleFortifyActivation", "HandleSpeedBoost" })
            Dispatch(clientGas, method, new PeerId(2));
        foreach (string method in new[] { "HandleStore", "HandleRetrieve", "HandleEquip", "HandleUnequip" })
            Dispatch(inventory, method, new PeerId(2));
        Dispatch(carrier, "HandleDrop", new PeerId(2), "request");
        Dispatch(clientInteractor, "HandleRequest", new PeerId(2), (NetworkObjectId?)host.GetNetworkObject().Id);
        Require(client.GasHealth == 0 && client.GasIsDowned && inventory.HasEquippedItem && client.GasMoveSpeed == 12,
            "Stale action request altered incapacitated/equipped state.");
        await ToSignal(GetTree().CreateTimer(10.1), SceneTreeTimer.SignalName.Timeout);
        Require(client.GasIsDead && !client.GasIsDowned, "Server bleedout timer failed.");
        Dispatch(hostInteractor, "HandleRequest", PeerId.Server, (NetworkObjectId?)identity);
        Require(client.GasIsDead && client.GasHealth == 0, "Dead revive accepted.");
        await ToSignal(GetTree().CreateTimer(2.1), SceneTreeTimer.SignalName.Timeout);
        Require(client.GasHealth == 100 && !client.IsIncapacitated && client.RespawnRevision == 1 &&
            client.GetNetworkObject().Id == identity && inventory.HasEquippedItem && client.GasMoveSpeed == 12,
            "Composed respawn/identity/equipment contract failed.");
        world.Despawn(identity);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(item.StorageState == CarryableItem.WorldState && item.HolderNetworkObjectId == 0 && item.Visible,
            "Equipped disconnect cleanup failed.");
        PlayerCarrier hostCarrier = host.GetNode<PlayerCarrier>("PlayerCarrier");
        Require(hostCarrier.TryPickup(item), "Disconnected item was not recoverable.");
        Dispatch(host.GetNode<PlayerInventory>("PlayerInventory"), "HandleStore", PeerId.Server);
        world.Despawn(host.GetNetworkObject().Id);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(item.StorageState == CarryableItem.WorldState && item.Visible, "Stored disconnect cleanup failed.");
        world.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Multiplayer.MultiplayerPeer = null;
        peer.Close();
        GameLog.Info("gas.interop", "player_composition_probe_passed", fields: new Dictionary<string, string?>
        { ["bidirectional_revive"] = "True", ["self_owner_range_dead_rejections"] = "True",
          ["stale_actions_rejected"] = "True", ["auto_drop"] = "True", ["timer_respawn_identity"] = "True",
          ["stored_equipped_cleanup"] = "True" });
    }

}
