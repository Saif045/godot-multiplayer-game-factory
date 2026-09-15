namespace GameFactory.Gameplay.Gas;

/// <summary>Small authoritative projection of GAS state for transport boundaries.</summary>
public readonly record struct GasSnapshot(
    float Health,
    bool IsFortified = false,
    float FortifyCooldownRemaining = 0f,
    float Stamina = 100f,
    bool IsExhausted = false,
    bool IsSprinting = false,
    float DashCooldownRemaining = 0f);
