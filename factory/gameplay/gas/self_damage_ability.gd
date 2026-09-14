class_name GameFactorySelfDamageAbility
extends GameplayAbility

var self_damage_effect: GameplayEffect

func _ready() -> void:
	ability_name = "SelfDamage"
	ability_tag = &"Ability.SelfDamage"
	self_damage_effect = GameplayEffect.new()
	var modifier := GameplayEffectModifier.new()
	modifier.attribute_name = "Health"
	modifier.operation = GameplayEffectModifier.Operation.ADD
	modifier.magnitude = -25.0
	self_damage_effect.modifiers = [modifier]
	super._ready()

func _activate_ability() -> bool:
	owner_asc.apply_gameplay_effect(self_damage_effect, owner_asc, ability_level)
	return true
