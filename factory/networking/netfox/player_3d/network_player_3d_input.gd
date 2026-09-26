extends Node

# These are the only player-owned rollback inputs. Netfox records them through
# RollbackSynchronizer; simulation never reads Godot Input directly.
var movement: Vector2 = Vector2.ZERO
var jump_pressed := false
var dash_pressed := false
var sprint_held := false
var _jump_queued := false
var _dash_queued := false

func _ready() -> void:
	NetworkTime.before_tick_loop.connect(_gather_input)

func _exit_tree() -> void:
	if NetworkTime.before_tick_loop.is_connected(_gather_input):
		NetworkTime.before_tick_loop.disconnect(_gather_input)

func _process(_delta: float) -> void:
	if is_multiplayer_authority() and Input.is_action_just_pressed("jump"):
		_jump_queued = true
	if is_multiplayer_authority() and Input.is_action_just_pressed("dash"):
		_dash_queued = true

func _gather_input() -> void:
	if not is_multiplayer_authority():
		return

	movement = Input.get_vector("move_left", "move_right", "move_forward", "move_backward")
	sprint_held = Input.is_action_pressed("sprint")
	# Input is recorded per Netfox tick. Queue the physical press until that
	# boundary, then expose a single-tick pulse for rollback/replay.
	jump_pressed = _jump_queued
	_jump_queued = false
	dash_pressed = _dash_queued
	_dash_queued = false
