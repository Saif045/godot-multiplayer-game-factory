extends MultiplayerPeerExtension
# Diagnostic-only ENet packet tap. It forwards bytes unchanged, decoding the
# Godot 4.7 RPC header before SceneMultiplayer tries to resolve its target.
var peer: ENetMultiplayerPeer
var paths: Dictionary = {}
var context: Node
var target := 0
var missing_rpcs := 0
var cached_id := 0
var host := false
const RPC_NAMES = ["_ack_diff_state", "_ack_full_state", "_submit_diff_state", "_submit_full_state", "_submit_input"]

func configure(native_peer: ENetMultiplayerPeer, node: Node) -> void:
	peer = native_peer
	context = node
	cached_id = peer.get_unique_id()
	host = cached_id == MultiplayerPeer.TARGET_PEER_SERVER
	peer.peer_connected.connect(func(id): peer_connected.emit(id))
	peer.peer_disconnected.connect(func(id): peer_disconnected.emit(id))

func _get_packet_script() -> PackedByteArray:
	var sender := peer.get_packet_peer()
	var mode := peer.get_packet_mode()
	var packet := peer.get_packet()
	decode(packet, sender, mode, "receive")
	return packet

func decode(packet: PackedByteArray, sender: int, mode: int, direction: String) -> void:
	if packet.is_empty(): return
	var command := packet[0] & 7
	if command == 1 and packet.size() >= 38:
		paths["%s:%s" % [sender, packet.decode_u32(34)]] = packet.slice(38).get_string_from_utf8()
	elif command == 0:
		var sizes := [1, 2, 4]
		var width: int = sizes[(packet[0] >> 4) & 3]
		var cache_id: int = packet[1] if width == 1 else (packet.decode_u16(1) if width == 2 else packet.decode_u32(1))
		var offset := 1 + width
		var method_id: int = packet[offset] if (packet[0] & 64) == 0 else packet.decode_u16(offset)
		offset += 1 if (packet[0] & 64) == 0 else 2
		var path: String = packet.slice(cache_id & 0x7fffffff).get_string_from_utf8() if (cache_id & 0x80000000) else paths.get("%s:%s" % [sender, cache_id], "")
		if not path.ends_with("/RollbackSynchronizer/Node"): return
		var exists := context.get_tree().root.has_node(NodePath(path))
		if direction == "receive" and not exists: missing_rpcs += 1
		var tick := -1
		if packet.size() > offset + 2 and (packet[offset + 1] & 31) == TYPE_INT:
			var integer_width := (packet[offset + 1] >> 6) & 3
			match integer_width:
				0: tick = packet.decode_s8(offset + 2)
				1: tick = packet.decode_s16(offset + 2)
				2: tick = packet.decode_s32(offset + 2)
				3: tick = packet.decode_s64(offset + 2)
		print("RPC_PACKET " + JSON.stringify({"utc_seconds": Time.get_unix_time_from_system(), "sender": sender,
			"receiver": _get_unique_id() if direction == "receive" else target, "path": path,
			"direction": direction, "tick": tick, "cache_id": cache_id, "method_id": method_id,
			"method": RPC_NAMES[method_id] if method_id < RPC_NAMES.size() else "unknown",
			"mode": mode, "target_exists": exists}))

func _put_packet_script(buffer: PackedByteArray) -> Error:
	decode(buffer, _get_unique_id(), peer.transfer_mode, "send")
	return peer.put_packet(buffer)
func _get_available_packet_count() -> int: return peer.get_available_packet_count()
func _get_max_packet_size() -> int: return 65535
func _get_packet_channel() -> int: return peer.get_packet_channel()
func _get_packet_mode() -> MultiplayerPeer.TransferMode: return peer.get_packet_mode()
func _get_packet_peer() -> int: return peer.get_packet_peer()
func _get_connection_status() -> MultiplayerPeer.ConnectionStatus: return peer.get_connection_status()
func _get_unique_id() -> int:
	return cached_id
func _get_transfer_mode() -> MultiplayerPeer.TransferMode: return peer.transfer_mode
func _set_transfer_mode(mode: MultiplayerPeer.TransferMode) -> void: peer.transfer_mode = mode
func _get_transfer_channel() -> int: return peer.transfer_channel
func _set_transfer_channel(channel: int) -> void: peer.transfer_channel = channel
func _set_target_peer(id: int) -> void:
	target = id
	peer.set_target_peer(id)
func _is_server() -> bool: return host
func _is_server_relay_supported() -> bool: return peer.is_server_relay_supported()
func _is_refusing_new_connections() -> bool: return peer.refuse_new_connections
func _set_refuse_new_connections(value: bool) -> void: peer.refuse_new_connections = value
func _poll() -> void: peer.poll()
func _close() -> void: peer.close()
func _disconnect_peer(id: int, force: bool) -> void: peer.disconnect_peer(id, force)
