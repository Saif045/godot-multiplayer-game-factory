using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Gameplay.Gas;

namespace GameFactory.Sandbox.Gas;

/// <summary>Headless proof that C# can create, invoke, and read a GodotGAS ASC.</summary>
public partial class GodotGasInteropProbe : Node
{
    public override void _Ready()
    {
        GameLog.EnsureInitialized();
        try
        {
            GodotGasAdapter gas = GodotGasAdapter.Create(this);
            int initialHealth = gas.GetHealth();
            bool abilityGranted = gas.IsSelfDamageGranted();
            int resultingHealth = gas.ApplySelfDamage();
            bool attributeChangedObserved = gas.DidObserveSelfDamageChange();
            if (initialHealth != 100 || !abilityGranted || resultingHealth != 75 || !attributeChangedObserved)
                throw new InvalidOperationException("Canonical GodotGAS health ability lifecycle did not complete.");

            GameLog.Info("gas.interop", "probe_passed", fields: new Dictionary<string, string?>
            {
                ["component_type"] = gas.Component.GetClass(),
                ["initial_health"] = initialHealth.ToString(),
                ["ability_granted"] = abilityGranted.ToString(),
                ["resulting_health"] = resultingHealth.ToString(),
                ["attribute_changed_observed"] = attributeChangedObserved.ToString()
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
