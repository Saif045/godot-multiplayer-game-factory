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
    private static readonly StringName AddTagMethod = "add_tag";
    private static readonly StringName HasTagExactMethod = "has_tag_exact";

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

    /// <summary>
    /// Minimal interop contract: invoke the real ASC and read its resulting
    /// state back. This is intentionally not an ability or network operation.
    /// </summary>
    public bool AddAndConfirmTag(StringName tag)
    {
        _component.Call(AddTagMethod, tag);
        return _component.Call(HasTagExactMethod, tag).AsBool();
    }
}
