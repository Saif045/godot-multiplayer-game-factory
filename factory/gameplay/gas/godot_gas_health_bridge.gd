class_name GodotGasHealthBridge
extends Node

const HealthAttributeSetScript = preload("res://factory/gameplay/gas/health_attribute_set.gd")
const SelfDamageAbilityScript = preload("res://factory/gameplay/gas/self_damage_ability.gd")
const FortifyAbilityScript = preload("res://factory/gameplay/gas/fortify_ability.gd")
const SpeedBoostAbilityScript = preload("res://factory/gameplay/gas/speed_boost_ability.gd")

var _asc: AbilitySystemComponent
var _self_damage: GameplayAbility
var _fortify: GameplayAbility
var _speed_boost: GameplayAbility
var _last_old_health: float = -1.0
var _last_new_health: float = -1.0
var _lifecycle_changed := false

func _ready() -> void:
	_asc = AbilitySystemComponent.new()
	_asc.attribute_sets = [HealthAttributeSetScript.new()]
	add_child(_asc)
	_asc.initialize_attribute_overrides({"Health": 100.0, "MoveSpeed": 6.0})
	_asc.attribute_changed.connect(_on_attribute_changed)
	_asc.active_effect_added.connect(_on_active_effect_lifecycle)
	_asc.active_effect_removed.connect(_on_active_effect_lifecycle)
	_self_damage = SelfDamageAbilityScript.new()
	_asc.grant_ability(_self_damage)
	_fortify = FortifyAbilityScript.new()
	_asc.grant_ability(_fortify)
	_speed_boost = SpeedBoostAbilityScript.new()
	_asc.grant_ability(_speed_boost)

func get_health() -> float:
	return _asc.get_attribute("Health").current_value

func get_move_speed() -> float:
	return _asc.get_attribute("MoveSpeed").current_value

func apply_speed_boost() -> bool:
	if not _asc.can_activate_ability(_speed_boost, true): return false
	_speed_boost.try_activate()
	return true

func apply_health_snapshot(health: float) -> void:
	_asc.initialize_attribute_overrides({"Health": health})

func apply_self_damage() -> float:
	_self_damage.try_activate()
	return get_health()

func apply_fortify() -> bool:
	if not _asc.can_activate_ability(_fortify, true):
		return false
	_fortify.try_activate()
	return true

func is_fortified() -> bool:
	return _asc.has_tag_exact(&"State.Fortified")

func get_fortify_cooldown_remaining() -> float:
	return _asc.get_tag_duration_remaining(&"Cooldown.Fortify")

func consume_lifecycle_change() -> bool:
	var changed := _lifecycle_changed
	_lifecycle_changed = false
	return changed

func is_self_damage_granted() -> bool:
	return _self_damage.owner_asc == _asc

func did_observe_self_damage_change() -> bool:
	return _last_old_health == 100.0 and _last_new_health == 75.0

func _on_attribute_changed(attribute_name: String, old_value: float, new_value: float, _spec: GameplayEffectSpec) -> void:
	if attribute_name == "Health":
		_last_old_health = old_value
		_last_new_health = new_value

func _on_active_effect_lifecycle(active_effect: ActiveGameplayEffect) -> void:
	var effect := active_effect.get_effect_def()
	if &"State.Fortified" in effect.granted_tags or &"Cooldown.Fortify" in effect.granted_tags:
		_lifecycle_changed = true
	for modifier in effect.modifiers:
		if modifier.attribute_name == "MoveSpeed":
			_lifecycle_changed = true
