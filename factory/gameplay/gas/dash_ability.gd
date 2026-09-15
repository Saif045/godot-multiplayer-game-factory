class_name GameFactoryDashAbility
extends GameplayAbility

const ExhaustedTag := &"State.Exhausted"
const CooldownTag := &"Cooldown.Dash"
const StaminaCost := 25.0
const CooldownDuration := 0.75

func _ready() -> void:
	ability_name = "Dash"
	ability_tag = &"Ability.Dash"
	activation_blocked_tags = [ExhaustedTag]

	cost_effect = GameplayEffect.new()
	cost_effect.policy = GameplayEffect.DurationPolicy.INSTANT
	var cost := GameplayEffectModifier.new()
	cost.attribute_name = "Stamina"
	cost.operation = GameplayEffectModifier.Operation.ADD
	cost.magnitude = -StaminaCost
	cost_effect.modifiers = [cost]

	cooldown_effect = GameplayEffect.new()
	cooldown_effect.policy = GameplayEffect.DurationPolicy.DURATION
	cooldown_effect.duration = CooldownDuration
	cooldown_effect.granted_tags = [CooldownTag]
	super._ready()

func _activate_ability() -> bool:
	# GodotGAS commits the stamina cost and canonical cooldown atomically after
	# its blocked-tag, cooldown, and affordability gates have passed.
	commit_ability()
	return true
