extends Node

var move: Vector2 = Vector2.ZERO

func _ready() -> void:
	NetworkTime.before_tick.connect(_prepare_input)

func _exit_tree() -> void:
	if NetworkTime.before_tick.is_connected(_prepare_input):
		NetworkTime.before_tick.disconnect(_prepare_input)

func _prepare_input(_delta: float, tick: int) -> void:
	if not is_multiplayer_authority():
		return

	# The test schedule and live controls feed the same synchronized property.
	# The gameplay probe enables the scenario through node metadata before spawn.
	var start_tick := int(get_parent().get_meta("scenario_start_tick", -1))
	if start_tick >= 0:
		var scenario_tick := tick - start_tick
		if scenario_tick < 20:
			move = Vector2.ZERO
		elif scenario_tick < 60:
			move = Vector2(1, 0)
		elif scenario_tick < 100:
			move = Vector2(0, 1)
		elif scenario_tick < 140:
			move = Vector2(-1, 0)
		else:
			move = Vector2.ZERO
	else:
		move = Input.get_vector("move_left", "move_right", "move_forward", "move_backward")
