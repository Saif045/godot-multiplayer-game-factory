extends Node

const WALK_SPEED := 6.0
const JUMP_VELOCITY := 5.0
const GRAVITY := 18.0

# Grounded affects the next simulation tick's jump decision, so it is tracked
# as rollback state together with the CharacterBody3D position and velocity.
var grounded := false

func _rollback_tick(delta: float, _tick: int, _is_fresh: bool) -> void:
	var player := get_parent() as CharacterBody3D
	var input := player.get_node("Input")
	var move_input: Vector2 = input.movement
	var desired := Vector3(move_input.x, 0.0, move_input.y)

	player.velocity.x = desired.x * WALK_SPEED
	player.velocity.z = desired.z * WALK_SPEED
	if grounded and input.jump_pressed:
		player.velocity.y = JUMP_VELOCITY
	else:
		player.velocity.y -= GRAVITY * delta

	# CharacterBody3D assumes Godot physics-frame timing. Netfox may run its
	# rollback tick at a different cadence, so compensate exactly around the
	# engine motion call and restore the persistent velocity afterwards.
	player.velocity *= NetworkTime.physics_factor
	player.move_and_slide()
	player.velocity /= NetworkTime.physics_factor
	grounded = player.is_on_floor()
