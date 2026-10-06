extends Node

# A focused headless contract test using the production simulation and Netfox
# recorder/snapshots. Fixture projection fields stand in for ordinary replication.
class ProbeInput extends Node:
	var movement := Vector2.ONE
	var jump_pressed := true
	var dash_pressed := true
	var sprint_held := true

class ProbeGas extends Node:
	var IsIncapacitated := true
	var reset_events := 0
	func LogRespawnApplied(_revision: int, _tick: int) -> void:
		reset_events += 1

class ProbePlayer extends CharacterBody3D:
	var IsIncapacitated := false
	var GasDashAuthorizationRevision := 4
	var GasMoveSpeed := 12.0
	var GasIsSprinting := true
	var RespawnRevision := 0
	var RespawnTick := 20
	var OriginalSpawnPosition := Vector3(2, 5, 3)

func _ready() -> void:
	_run.call_deferred()

func _check(condition: bool, reason: String) -> void:
	if not condition:
		push_error("VITAL_MOTION_FAIL: " + reason)
		get_tree().quit(1)

func _run() -> void:
	var player := ProbePlayer.new()
	add_child(player)
	var input := ProbeInput.new()
	input.name = "Input"
	player.add_child(input)
	var gas := ProbeGas.new()
	gas.name = "NetworkGasComponent"
	player.add_child(gas)
	var simulation := preload("res://factory/networking/netfox/player_3d/network_player_3d_simulation.gd").new()
	simulation.name = "Simulation"
	player.add_child(simulation)
	player.position = Vector3(30, 10, 30)
	player.velocity = Vector3(24, 0, 24)
	simulation.dash_time_remaining = 0.16
	simulation.dash_direction = Vector3.FORWARD
	simulation.grounded = true
	await get_tree().physics_frame
	simulation._rollback_tick(1.0 / 60.0, 10, true)
	_check(player.velocity.x == 0 and player.velocity.z == 0, "stale inputs moved incapacitated player")
	_check(player.velocity.y < 0 and player.position.y < 10, "gravity/jump restriction")
	_check(simulation.dash_time_remaining == 0, "active dash not cancelled")
	var cache := PropertyCache.new(player)
	var config := _PropertyConfig.new()
	# Read the actual prefab list, so the test catches missing consumption state.
	var prefab := load("res://factory/networking/netfox/player_3d/network_player_3d.tscn") as PackedScene
	var authored := prefab.get_state()
	for node_index in authored.get_node_count():
		if authored.get_node_name(node_index) != &"RollbackSynchronizer": continue
		for property_index in authored.get_node_property_count(node_index):
			if authored.get_node_property_name(node_index, property_index) == &"state_properties":
				var paths: Array[String] = []
				paths.assign(authored.get_node_property_value(node_index, property_index))
				config.set_properties_from_paths(paths, cache)
	var states := _PropertyHistoryBuffer.new()
	var recorder := _RollbackHistoryRecorder.new()
	recorder.configure(states, _PropertyHistoryBuffer.new(), config, _PropertyConfig.new(), cache, _Set.new())
	recorder.set_latest_state_tick(-1)
	recorder.record_state(20)
	player.IsIncapacitated = false
	gas.IsIncapacitated = false
	player.RespawnRevision = 1
	simulation._rollback_tick(1.0 / 60.0, 20, true)
	_check(player.position == player.OriginalSpawnPosition and player.velocity == Vector3.ZERO, "respawn reset")
	_check(not simulation.grounded and simulation.dash_direction == Vector3.ZERO, "respawn motion residue")
	recorder.record_state(21)
	var reset := states.get_history(21)
	_check(reset.get_value("Simulation:last_respawn_revision") == 1, "consumption missing from rollback state")
	recorder.apply_tick(20)
	_check(simulation.last_respawn_revision == 0 and player.position != player.OriginalSpawnPosition, "stale history fixture")
	simulation._rollback_tick(1.0 / 60.0, 20, false)
	_check(player.position == player.OriginalSpawnPosition and player.velocity == Vector3.ZERO, "stale rollback undid respawn")
	_check(gas.reset_events == 1, "transition log repeated during replay")
	recorder.apply_tick(21)
	input.jump_pressed = false
	input.dash_pressed = false
	input.sprint_held = false
	simulation._rollback_tick(1.0 / 60.0, 21, true)
	_check(player.velocity.x != 0 and player.position != player.OriginalSpawnPosition, "movement failed to resume")
	print("VITAL_MOTION_PASS: incapacitated_stale_input gravity dash_cancel respawn stale_history_replay consumed_revision movement_resumes")
	player.free()
	get_tree().quit(0)
