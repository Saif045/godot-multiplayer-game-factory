extends Node
# Read-only observations of the actual library handler and its received payload.
var tap: MultiplayerPeerExtension
var observed := false
var safe := false
var old_id := -1
var old_path := ""
var new_input: Node
var replay_seen := false
var replay_target: Node
var replay_tick := -1

func configure(packet_peer: MultiplayerPeerExtension) -> void:
	tap = packet_peer
	NetworkCommandServer._rpc_transport.on_receive.connect(_observe)
	NetworkRollback.after_process_tick.connect(_observe_replay)

func _observe_replay(tick: int) -> void:
	if replay_seen: return
	if tick != replay_tick or not is_instance_valid(replay_target): return
	var ticks: Array = RollbackSimulationServer._simulated_ticks.get(replay_target, [])
	if ticks.count(tick) > 1:
		replay_seen = true
		print("ROLLBACK_REPLAY_OBSERVED " + JSON.stringify({"tick": tick, "node": str(replay_target.get_path()), "peer": multiplayer.get_unique_id()}))

func request_replay(simulation: Node) -> bool:
	# A low-latency connection need not naturally simulate any tick twice.
	# Exercise rewind deterministically through the normal before-loop API.
	var ticks: Array = RollbackSimulationServer._simulated_ticks.get(simulation, [])
	if ticks.is_empty(): return false
	replay_target = simulation
	replay_tick = ticks.back()
	replay_seen = false
	if NetworkHistoryServer._get_rollback_input_snapshot(replay_tick) == null: return false
	if NetworkHistoryServer._get_rollback_state_snapshot(replay_tick) == null: return false
	NetworkRollback.before_loop.connect(_request_replay.bind(replay_tick), CONNECT_ONE_SHOT)
	print("ROLLBACK_REPLAY_REQUESTED " + JSON.stringify({"tick": replay_tick, "node": str(simulation.get_path()), "peer": multiplayer.get_unique_id()}))
	return true

func _request_replay(tick: int) -> void:
	NetworkRollback.notify_resimulation_start(tick)

func identity_id(node: Node) -> int:
	var identifier := NetworkIdentityServer._get_identifier_of(node)
	return identifier.get_local_id() if identifier else -1

func remote_identity_id(node: Node, peer: int) -> int:
	var identifier := NetworkIdentityServer._get_identifier_of(node)
	return identifier.get_id_for(peer) if identifier else -1

func prepare(id: int, path: String, replacement: Node) -> void:
	old_id = id
	old_path = path
	new_input = replacement

func removed_identity() -> bool:
	return NetworkIdentityServer._resolve_reference(1, _NetworkIdentityReference.of_id(old_id), false) == null and NetworkIdentityServer._resolve_reference(1, _NetworkIdentityReference.of_full_name(old_path), false) == null

func _observe(sender: int, command: int, data: PackedByteArray) -> void:
	if not tap.delivered_held or observed: return
	observed = true
	var sync := NetworkSynchronizationServer
	var references: Array[String] = []
	var buffer := StreamPeerBuffer.new()
	buffer.data_array = data
	var varuint := NetworkSchemas.varuint()
	var netref := NetworkSchemas._netref()
	while buffer.get_available_bytes() > 0:
		var snapshot_buffer := StreamPeerBuffer.new()
		snapshot_buffer.data_array = buffer.get_partial_data(varuint.decode(buffer))[1]
		snapshot_buffer.get_u32()
		while snapshot_buffer.get_available_bytes() > 0:
			var reference := netref.decode(snapshot_buffer) as _NetworkIdentityReference
			references.append(str(reference))
			if not reference.has_id() or reference.get_id() != old_id:
				push_error("LATE_INPUT_FAIL: payload did not refer only to retired input identity")
				return
			snapshot_buffer.get_partial_data(varuint.decode(snapshot_buffer))
	buffer.seek(0)
	var snapshots := sync._redundant_serializer.read_from(sender, sync._rb_input_properties, buffer, true)
	var empty := snapshots.size() > 0
	for snapshot in snapshots:
		empty = empty and snapshot.is_empty()
		# The actual command handler ran before this observer. Check both its
		# stored tick snapshot and per-object history for replacement contamination.
		var stored := NetworkHistoryServer._get_rollback_input_snapshot(snapshot.tick)
		empty = empty and (stored == null or not stored.has_subject(new_input))
	safe = command == sync._cmd_input._idx and empty and removed_identity() and references.size() > 0 and new_input.movement == Vector2.ZERO and identity_id(new_input) > old_id
	print("LATE_INPUT_OBSERVED " + JSON.stringify({"command": command, "expected_command": sync._cmd_input._idx,
		"references": references, "old_id": old_id, "new_id": identity_id(new_input),
		"snapshots_empty": empty, "old_identity_removed": removed_identity(), "safe": safe}))
