extends Node2D

func _ready() -> void:
	NetworkTime.before_tick.connect(_advance_authoritative_state)

func _exit_tree() -> void:
	if NetworkTime.before_tick.is_connected(_advance_authoritative_state):
		NetworkTime.before_tick.disconnect(_advance_authoritative_state)

func _advance_authoritative_state(_delta: float, tick: int) -> void:
	if not multiplayer.is_server():
		return
	position = Vector2(float(tick % 120) / 12.0, 1.0)
