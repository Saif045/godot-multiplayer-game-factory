extends Node2D

const SERVER_SPEED := 5.0
const TEST_CLIENT_SPEED := 6.0
const MISPREDICTION_START_TICK := 70
const MISPREDICTION_END_TICK := 90

var simulated_position: Vector2 = Vector2.ZERO

func _rollback_tick(delta: float, tick: int, _is_fresh: bool) -> void:
	var input := get_parent().get_node("Input").move as Vector2
	var speed := SERVER_SPEED
	# Test-only, bounded local divergence. The server always uses SERVER_SPEED.
	var start_tick := int(get_parent().get_meta("scenario_start_tick", -1))
	var scenario_tick := tick - start_tick
	if start_tick >= 0 and not multiplayer.is_server() and scenario_tick >= MISPREDICTION_START_TICK and scenario_tick < MISPREDICTION_END_TICK:
		speed = TEST_CLIENT_SPEED

	simulated_position += input.normalized() * speed * delta
	position = simulated_position
