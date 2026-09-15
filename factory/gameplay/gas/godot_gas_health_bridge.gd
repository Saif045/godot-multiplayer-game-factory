class_name GodotGasHealthBridge
extends Node

const HealthAttributeSetScript = preload("res://factory/gameplay/gas/health_attribute_set.gd")
const SelfDamageAbilityScript = preload("res://factory/gameplay/gas/self_damage_ability.gd")
const FortifyAbilityScript = preload("res://factory/gameplay/gas/fortify_ability.gd")
const SpeedBoostAbilityScript = preload("res://factory/gameplay/gas/speed_boost_ability.gd")
const DashAbilityScript = preload("res://factory/gameplay/gas/dash_ability.gd")

const SprintingTag := &"State.Sprinting"
const ExhaustedTag := &"State.Exhausted"
const RegeneratingTag := &"State.StaminaRegenerating"
const EquipmentCubeTag := &"State.EquipmentCube"
const ExhaustionRecoveryThreshold := 25.0

var _asc: AbilitySystemComponent
var _self_damage: GameplayAbility
var _fortify: GameplayAbility
var _speed_boost: GameplayAbility
var _dash: GameplayAbility
var _last_old_health: float = -1.0
var _last_new_health: float = -1.0
var _lifecycle_changed := false
var _sprint_intent := false
var _drain_effect: GameplayEffect
var _regeneration_effect: GameplayEffect
var _exhaustion_effect: GameplayEffect

func _ready() -> void:
	_asc = AbilitySystemComponent.new()
	_asc.attribute_sets = [HealthAttributeSetScript.new()]
	add_child(_asc)
	_asc.initialize_attribute_overrides({"Health": 100.0, "MoveSpeed": 6.0, "Stamina": 100.0})
	_asc.attribute_changed.connect(_on_attribute_changed)
	_asc.active_effect_added.connect(_on_active_effect_lifecycle)
	_asc.active_effect_removed.connect(_on_active_effect_lifecycle)
	_self_damage = SelfDamageAbilityScript.new()
	_asc.grant_ability(_self_damage)
	_fortify = FortifyAbilityScript.new()
	_asc.grant_ability(_fortify)
	_speed_boost = SpeedBoostAbilityScript.new()
	_asc.grant_ability(_speed_boost)
	_dash = DashAbilityScript.new()
	_asc.grant_ability(_dash)
	_drain_effect = _make_periodic_effect(-5.0, SprintingTag)
	_regeneration_effect = _make_periodic_effect(3.0, RegeneratingTag)
	_exhaustion_effect = GameplayEffect.new()
	_exhaustion_effect.policy = GameplayEffect.DurationPolicy.INFINITE
	_exhaustion_effect.granted_tags = [ExhaustedTag]
	_start_regeneration()

func get_health() -> float:
	return _asc.get_attribute("Health").current_value

func get_move_speed() -> float:
	return _asc.get_attribute("MoveSpeed").current_value

func get_stamina() -> float:
	return _asc.get_attribute("Stamina").current_value

func is_exhausted() -> bool:
	return _asc.has_tag_exact(ExhaustedTag)

func is_sprinting() -> bool:
	return _asc.has_tag_exact(SprintingTag)

func set_sprint_intent(held: bool) -> void:
	if _sprint_intent == held:
		return
	_sprint_intent = held
	_reconcile_sprint_state()

func apply_speed_boost() -> bool:
	if not _asc.can_activate_ability(_speed_boost, true): return false
	_speed_boost.try_activate()
	return true

func apply_dash() -> bool:
	if not _asc.can_activate_ability(_dash, true): return false
	_dash.try_activate()
	return true

func apply_equipment_cube_capability(source_node: Node) -> bool:
	if source_node == null or _asc.has_tag_exact(EquipmentCubeTag):
		return false
	var effect := GameplayEffect.new()
	effect.policy = GameplayEffect.DurationPolicy.INFINITE
	effect.granted_tags = [EquipmentCubeTag]
	var modifier := GameplayEffectModifier.new()
	modifier.attribute_name = "MoveSpeed"
	modifier.operation = GameplayEffectModifier.Operation.ADD
	modifier.magnitude = 6.0
	effect.modifiers = [modifier]
	var context := GameplayEffectContext.new(source_node)
	var spec := GameplayEffectSpec.new(effect, context)
	return _asc.apply_effect_spec(spec) != null

func remove_equipment_cube_capability(source_node: Node) -> bool:
	if source_node == null or not _asc.has_tag_exact(EquipmentCubeTag):
		return false
	_asc.remove_effects_from_source(source_node)
	return not _asc.has_tag_exact(EquipmentCubeTag)

func is_equipment_cube_capability_active() -> bool:
	return _asc.has_tag_exact(EquipmentCubeTag)

func get_dash_cooldown_remaining() -> float:
	return _asc.get_tag_duration_remaining(&"Cooldown.Dash")

func apply_snapshot(health: float, stamina: float) -> void:
	_asc.initialize_attribute_overrides({"Health": health, "Stamina": stamina})

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
	if attribute_name == "Stamina":
		_lifecycle_changed = true
		if new_value <= 0.0 and not is_exhausted():
			_stop_drain()
			_asc.apply_gameplay_effect(_exhaustion_effect, _asc)
		elif is_exhausted() and new_value >= ExhaustionRecoveryThreshold:
			_asc.remove_effects_with_tag(ExhaustedTag)
			_reconcile_sprint_state()

func _on_active_effect_lifecycle(active_effect: ActiveGameplayEffect) -> void:
	var effect := active_effect.get_effect_def()
	if &"State.Fortified" in effect.granted_tags or &"Cooldown.Fortify" in effect.granted_tags or &"Cooldown.Dash" in effect.granted_tags:
		_lifecycle_changed = true
	for modifier in effect.modifiers:
		if modifier.attribute_name == "MoveSpeed" or modifier.attribute_name == "Stamina":
			_lifecycle_changed = true

func _make_periodic_effect(delta: float, state_tag: StringName) -> GameplayEffect:
	var effect := GameplayEffect.new()
	effect.policy = GameplayEffect.DurationPolicy.INFINITE
	effect.period = 0.25
	effect.granted_tags = [state_tag]
	var modifier := GameplayEffectModifier.new()
	modifier.attribute_name = "Stamina"
	modifier.operation = GameplayEffectModifier.Operation.ADD
	modifier.magnitude = delta
	effect.modifiers = [modifier]
	return effect

func _reconcile_sprint_state() -> void:
	if _sprint_intent and get_stamina() > 0.0 and not is_exhausted():
		_stop_regeneration()
		if not is_sprinting():
			_asc.apply_gameplay_effect(_drain_effect, _asc)
	else:
		_stop_drain()
		_start_regeneration()

func _stop_drain() -> void:
	_asc.remove_effects_with_tag(SprintingTag)

func _start_regeneration() -> void:
	if not _asc.has_tag_exact(RegeneratingTag):
		_asc.apply_gameplay_effect(_regeneration_effect, _asc)

func _stop_regeneration() -> void:
	_asc.remove_effects_with_tag(RegeneratingTag)
