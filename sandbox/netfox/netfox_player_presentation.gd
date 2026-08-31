extends Node2D

# Presentation is intentionally not a rollback state. It copies the completed
# simulation tick, then TickInterpolator renders between those snapshots.
func _ready() -> void:
	NetworkTime.after_tick_loop.connect(_copy_completed_simulation)

func _exit_tree() -> void:
	if NetworkTime.after_tick_loop.is_connected(_copy_completed_simulation):
		NetworkTime.after_tick_loop.disconnect(_copy_completed_simulation)

func _copy_completed_simulation() -> void:
	position = get_parent().get_node("Simulation").simulated_position
