extends Node2D

const MOVE_SPEED := 240.0

var simulated_position: Vector2 = Vector2.ZERO

func _rollback_tick(delta: float, tick: int, is_fresh: bool) -> void:
	var input := get_parent().get_node("Input").movement as Vector2
	simulated_position += input.normalized() * MOVE_SPEED * delta
	position = simulated_position
	get_parent().call("ReportSimulationTick", tick, simulated_position, is_fresh)
	get_parent().call("ReportMovement", tick, simulated_position, is_fresh)
