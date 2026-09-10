extends Node

var movement: Vector2 = Vector2.ZERO

func _ready() -> void:
	NetworkTime.before_tick_loop.connect(_gather_input)

func _exit_tree() -> void:
	if NetworkTime.before_tick_loop.is_connected(_gather_input):
		NetworkTime.before_tick_loop.disconnect(_gather_input)

func _gather_input() -> void:
	if not is_multiplayer_authority():
		return

	movement = Input.get_vector("move_left", "move_right", "move_forward", "move_backward")
	get_parent().call("ReportLocalInput", movement)
