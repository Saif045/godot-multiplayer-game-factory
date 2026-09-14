using System;
using Godot;

namespace GameFactory.Gameplay.Gas;

/// <summary>
/// The sole C# boundary for the vendored GodotGAS GDScript API.
/// Networking and authority are deliberately outside this adapter.
/// </summary>
public sealed class GodotGasAdapter
{
    private const string AbilitySystemComponentScenePath =
        "res://factory/gameplay/gas/godot_gas_component.tscn";
    private static readonly StringName GetHealthMethod = "get_health";
    private static readonly StringName ApplySelfDamageMethod = "apply_self_damage";
    private static readonly StringName IsSelfDamageGrantedMethod = "is_self_damage_granted";
    private static readonly StringName DidObserveSelfDamageChangeMethod = "did_observe_self_damage_change";
    private static readonly StringName ApplyHealthSnapshotMethod = "apply_health_snapshot";

    private readonly Node _component;

    private GodotGasAdapter(Node component)
    {
        _component = component;
    }

    public Node Component => _component;

    /// <summary>Creates and attaches a real GodotGAS AbilitySystemComponent.</summary>
    public static GodotGasAdapter Create(Node owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        PackedScene componentScene = GD.Load<PackedScene>(AbilitySystemComponentScenePath)
            ?? throw new InvalidOperationException("GodotGAS AbilitySystemComponent scene could not be loaded.");
        Node component = componentScene.Instantiate<Node>();
        owner.AddChild(component);
        return new GodotGasAdapter(component);
    }

    public int GetHealth() => Mathf.RoundToInt(_component.Call(GetHealthMethod).AsSingle());

    /// <summary>Executes the project-owned test effect through the real ASC.</summary>
    public int ApplySelfDamage() =>
        Mathf.RoundToInt(_component.Call(ApplySelfDamageMethod).AsSingle());

    public bool IsSelfDamageGranted() => _component.Call(IsSelfDamageGrantedMethod).AsBool();

    public bool DidObserveSelfDamageChange() =>
        _component.Call(DidObserveSelfDamageChangeMethod).AsBool();

    public GasSnapshot CaptureSnapshot() => new(GetHealth());

    public void ApplySnapshot(GasSnapshot snapshot) =>
        _component.Call(ApplyHealthSnapshotMethod, snapshot.Health);
}
