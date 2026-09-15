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
    private static readonly StringName ApplyFortifyMethod = "apply_fortify";
    private static readonly StringName ApplySpeedBoostMethod = "apply_speed_boost";
    private static readonly StringName ApplyDashMethod = "apply_dash";
    private static readonly StringName ApplyEquipmentCubeCapabilityMethod = "apply_equipment_cube_capability";
    private static readonly StringName RemoveEquipmentCubeCapabilityMethod = "remove_equipment_cube_capability";
    private static readonly StringName IsEquipmentCubeCapabilityActiveMethod = "is_equipment_cube_capability_active";
    private static readonly StringName GetMoveSpeedMethod = "get_move_speed";
    private static readonly StringName GetStaminaMethod = "get_stamina";
    private static readonly StringName IsExhaustedMethod = "is_exhausted";
    private static readonly StringName IsSprintingMethod = "is_sprinting";
    private static readonly StringName SetSprintIntentMethod = "set_sprint_intent";
    private static readonly StringName GetDashCooldownRemainingMethod = "get_dash_cooldown_remaining";
    private static readonly StringName IsSelfDamageGrantedMethod = "is_self_damage_granted";
    private static readonly StringName DidObserveSelfDamageChangeMethod = "did_observe_self_damage_change";
    private static readonly StringName ApplySnapshotMethod = "apply_snapshot";
    private static readonly StringName IsFortifiedMethod = "is_fortified";
    private static readonly StringName GetFortifyCooldownRemainingMethod = "get_fortify_cooldown_remaining";
    private static readonly StringName ConsumeLifecycleChangeMethod = "consume_lifecycle_change";

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

    public bool ApplyFortify() => _component.Call(ApplyFortifyMethod).AsBool();
    public bool ApplySpeedBoost() => _component.Call(ApplySpeedBoostMethod).AsBool();
    public bool ApplyDash() => _component.Call(ApplyDashMethod).AsBool();
    public bool ApplyEquipmentCubeCapability(Node source) =>
        _component.Call(ApplyEquipmentCubeCapabilityMethod, source).AsBool();
    public bool RemoveEquipmentCubeCapability(Node source) =>
        _component.Call(RemoveEquipmentCubeCapabilityMethod, source).AsBool();
    public bool IsEquipmentCubeCapabilityActive() =>
        _component.Call(IsEquipmentCubeCapabilityActiveMethod).AsBool();
    public float GetMoveSpeed() => _component.Call(GetMoveSpeedMethod).AsSingle();
    public float GetStamina() => _component.Call(GetStaminaMethod).AsSingle();
    public bool IsExhausted() => _component.Call(IsExhaustedMethod).AsBool();
    public bool IsSprinting() => _component.Call(IsSprintingMethod).AsBool();
    public void SetSprintIntent(bool held) => _component.Call(SetSprintIntentMethod, held);
    public float GetDashCooldownRemaining() => _component.Call(GetDashCooldownRemainingMethod).AsSingle();

    public GasSnapshot CaptureSnapshot() => new(
        GetHealth(),
        _component.Call(IsFortifiedMethod).AsBool(),
        _component.Call(GetFortifyCooldownRemainingMethod).AsSingle(),
        GetStamina(),
        IsExhausted(),
        IsSprinting(),
        GetDashCooldownRemaining());

    public void ApplySnapshot(GasSnapshot snapshot)
    {
        _component.Call(ApplySnapshotMethod, snapshot.Health, snapshot.Stamina);
    }

    public bool ConsumeLifecycleChange() =>
        _component.Call(ConsumeLifecycleChangeMethod).AsBool();
}
