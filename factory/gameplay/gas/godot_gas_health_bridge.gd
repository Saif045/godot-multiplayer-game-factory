class_name GodotGasHealthBridge
extends Node

const HealthAttributeSetScript = preload("res://factory/gameplay/gas/health_attribute_set.gd")
const SelfDamageAbilityScript = preload("res://factory/gameplay/gas/self_damage_ability.gd")

var _asc: AbilitySystemComponent
var _self_damage: GameplayAbility
var _last_old_health: float = -1.0
var _last_new_health: float = -1.0

func _ready() -> void:
	_asc = AbilitySystemComponent.new()
	_asc.attribute_sets = [HealthAttributeSetScript.new()]
	add_child(_asc)
	_asc.initialize_attribute_overrides({"Health": 100.0})
	_asc.attribute_changed.connect(_on_attribute_changed)
	_self_damage = SelfDamageAbilityScript.new()
	_asc.grant_ability(_self_damage)

func get_health() -> float:
	return _asc.get_attribute("Health").current_value

func apply_health_snapshot(health: float) -> void:
	_asc.initialize_attribute_overrides({"Health": health})

func apply_self_damage() -> float:
	_self_damage.try_activate()
	return get_health()

func is_self_damage_granted() -> bool:
	return _self_damage.owner_asc == _asc

func did_observe_self_damage_change() -> bool:
	return _last_old_health == 100.0 and _last_new_health == 75.0

func _on_attribute_changed(attribute_name: String, old_value: float, new_value: float, _spec: GameplayEffectSpec) -> void:
	if attribute_name == "Health":
		_last_old_health = old_value
		_last_new_health = new_value
