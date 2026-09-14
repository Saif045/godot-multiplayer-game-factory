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
            if (initialHealth != 100 || !abilityGranted || resultingHealth != 75 || !attributeChangedObserved ||
                snapshot.Health != 75 || reconstructedHealth != 75 || !fortifyActivated ||
                !fortifyActive.IsFortified || fortifyActive.FortifyCooldownRemaining <= 0f ||
                !cooldownBlockedRepeat || fortifyExpired.IsFortified ||
                fortifyExpired.FortifyCooldownRemaining <= 0f ||
                cooldownExpired.FortifyCooldownRemaining > 0.05f)
                throw new InvalidOperationException("Canonical GodotGAS effect lifecycle did not complete.");

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
