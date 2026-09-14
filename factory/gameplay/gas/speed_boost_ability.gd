class_name GameFactorySpeedBoostAbility
extends GameplayAbility

var speed_effect: GameplayEffect

func _ready() -> void:
	ability_name = "SpeedBoost"
	ability_tag = &"Ability.SpeedBoost"
	speed_effect = GameplayEffect.new()
	speed_effect.policy = GameplayEffect.DurationPolicy.DURATION
	speed_effect.duration = 3.0
	var modifier := GameplayEffectModifier.new()
	modifier.attribute_name = "MoveSpeed"
	modifier.operation = GameplayEffectModifier.Operation.ADD
	modifier.magnitude = 6.0
	speed_effect.modifiers = [modifier]
	super._ready()

func _activate_ability() -> bool:
	owner_asc.apply_gameplay_effect(speed_effect, owner_asc, ability_level)
	return true
