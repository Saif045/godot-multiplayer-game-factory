extends Node

const JUMP_VELOCITY := 5.0
const GRAVITY := 18.0
const DASH_DURATION := 0.16
const DASH_SPEED := 24.0

# Grounded affects the next simulation tick's jump decision, so it is tracked
# as rollback state together with the CharacterBody3D position and velocity.
var grounded := false
var dash_time_remaining := 0.0
var dash_direction := Vector3.ZERO
var last_dash_authorization_revision := 0
var last_respawn_revision := 0
var _logged_respawn_revision := 0

func _rollback_tick(delta: float, tick: int, _is_fresh: bool) -> void:
	var player := get_parent() as CharacterBody3D
	# Ordinary lifecycle projection never enters history. Only consumption of
	# the physical reset is replayable state, applied at/after a server tick.
	if player.RespawnRevision > last_respawn_revision and tick >= player.RespawnTick:
		player.position = player.OriginalSpawnPosition
		player.velocity = Vector3.ZERO
		grounded = false
		dash_time_remaining = 0.0
		dash_direction = Vector3.ZERO
		last_dash_authorization_revision = player.GasDashAuthorizationRevision
		last_respawn_revision = player.RespawnRevision
		if last_respawn_revision > _logged_respawn_revision:
			_logged_respawn_revision = last_respawn_revision
			player.get_node("NetworkGasComponent").LogRespawnApplied(last_respawn_revision, tick)
		# No stale jump/dash/movement from this tick accompanies the reset.
		return
	var input := player.get_node("Input")
	var move_input: Vector2 = input.movement
	# Server reads canonical tags through the bridge, independently of recorded
	# owner inputs and even a not-yet-published ordinary projection.
	var incapacitated: bool = player.get_node("NetworkGasComponent").IsIncapacitated
	var desired := Vector3.ZERO if incapacitated else Vector3(move_input.x, 0.0, move_input.y)
	if incapacitated:
		dash_time_remaining = 0.0
		dash_direction = Vector3.ZERO
		last_dash_authorization_revision = player.GasDashAuthorizationRevision
	var authorized_revision: int = player.GasDashAuthorizationRevision

	# A remote owner begins instantly from its recorded one-shot input. The
	# server later either emits the matching authorization revision (same dash)
	# or does not; normal Netfox reconciliation then corrects a rejected guess.
	if not incapacitated and input.dash_pressed and not multiplayer.is_server():
		_start_dash(desired)
		last_dash_authorization_revision = authorized_revision + 1
	if not incapacitated and authorized_revision > last_dash_authorization_revision:
		_start_dash(desired)
		last_dash_authorization_revision = authorized_revision

	if dash_time_remaining > 0.0:
		player.velocity.x = dash_direction.x * DASH_SPEED
		player.velocity.z = dash_direction.z * DASH_SPEED
		dash_time_remaining = maxf(0.0, dash_time_remaining - delta)
	else:
		var effective_move_speed: float = player.GasMoveSpeed
		if input.sprint_held and player.GasIsSprinting:
			effective_move_speed *= 1.5
		player.velocity.x = desired.x * effective_move_speed
		player.velocity.z = desired.z * effective_move_speed
	if not incapacitated and grounded and input.jump_pressed:
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

func _start_dash(desired: Vector3) -> void:
	dash_direction = desired.normalized()
	if dash_direction == Vector3.ZERO:
		dash_direction = Vector3.FORWARD
	dash_time_remaining = DASH_DURATION
