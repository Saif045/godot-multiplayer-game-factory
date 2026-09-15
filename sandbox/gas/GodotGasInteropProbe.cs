using System;
using System.Collections.Generic;
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
            GodotGasAdapter reconstructed = GodotGasAdapter.Create(this);
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
}
