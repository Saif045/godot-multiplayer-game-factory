using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Gameplay.Gas;

namespace GameFactory.Sandbox.Gas;

/// <summary>Headless proof that C# can create, invoke, and read a GodotGAS ASC.</summary>
public partial class GodotGasInteropProbe : Node
{
    private static readonly StringName ProbeTag = "Probe.CSharpInterop";

    public override void _Ready()
    {
        GameLog.EnsureInitialized();
        try
        {
            GodotGasAdapter gas = GodotGasAdapter.Create(this);
            bool tagRoundTripSucceeded = gas.AddAndConfirmTag(ProbeTag);
            if (!tagRoundTripSucceeded)
                throw new InvalidOperationException("GodotGAS did not report the tag added by C#.");

            GameLog.Info("gas.interop", "probe_passed", fields: new Dictionary<string, string?>
            {
                ["component_type"] = gas.Component.GetClass(),
                ["tag"] = ProbeTag.ToString(),
                ["tag_round_trip"] = "true"
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
