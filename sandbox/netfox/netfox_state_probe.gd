extends Node2D

var authoritative_position: Vector2 = Vector2.ZERO

func _ready() -> void:
	NetworkTime.before_tick.connect(_advance_authoritative_state)

func _exit_tree() -> void:
	if NetworkTime.before_tick.is_connected(_advance_authoritative_state):
		NetworkTime.before_tick.disconnect(_advance_authoritative_state)

func _advance_authoritative_state(_delta: float, tick: int) -> void:
	if not multiplayer.is_server():
		return
	authoritative_position = Vector2(float(tick % 120) / 12.0, 1.0)
	position = authoritative_position
