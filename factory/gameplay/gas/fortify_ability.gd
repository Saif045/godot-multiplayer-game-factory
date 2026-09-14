class_name GameFactoryFortifyAbility
extends GameplayAbility

const FortifiedTag := &"State.Fortified"
const CooldownTag := &"Cooldown.Fortify"
const FortifyDuration := 3.0
const CooldownDuration := 5.0

var fortify_effect: GameplayEffect

func _ready() -> void:
	ability_name = "Fortify"
	ability_tag = &"Ability.Fortify"
	activation_blocked_tags = [FortifiedTag]

	fortify_effect = GameplayEffect.new()
	fortify_effect.policy = GameplayEffect.DurationPolicy.DURATION
	fortify_effect.duration = FortifyDuration
	fortify_effect.granted_tags = [FortifiedTag]

	cooldown_effect = GameplayEffect.new()
	cooldown_effect.policy = GameplayEffect.DurationPolicy.DURATION
	cooldown_effect.duration = CooldownDuration
	cooldown_effect.granted_tags = [CooldownTag]
	super._ready()

func _activate_ability() -> bool:
	commit_ability()
	owner_asc.apply_gameplay_effect(fortify_effect, owner_asc, ability_level)
	return true
