extends Node3D

# Presentation is intentionally not rollback state. It is an independent,
# top-level visual transform updated after the completed simulation tick and
# smoothed by TickInterpolator.
func _ready() -> void:
	top_level = true
	NetworkTime.after_tick_loop.connect(_copy_completed_simulation)

func _exit_tree() -> void:
	if NetworkTime.after_tick_loop.is_connected(_copy_completed_simulation):
		NetworkTime.after_tick_loop.disconnect(_copy_completed_simulation)

func _copy_completed_simulation() -> void:
	global_position = get_parent().global_position
