extends MultiplayerPeerExtension
# Sandbox only: hold exactly one already-received command RPC, then return its
# unchanged bytes to SceneMultiplayer after the test has removed its player.
var peer: ENetMultiplayerPeer
var context: Node
var cached_id := 0
var host := false
var target := 0
var paths := {}
var incoming: Array[Dictionary] = []
var held := {}
var armed := false
var released := false
var delivered_held := false
var receiver_path := ""
var receiver_instance := 0

func configure(native_peer: ENetMultiplayerPeer, node: Node) -> void:
	peer = native_peer
	context = node
	cached_id = peer.get_unique_id()
	host = cached_id == 1
	peer.peer_connected.connect(func(id): peer_connected.emit(id))
	peer.peer_disconnected.connect(func(id): peer_disconnected.emit(id))

func arm() -> void:
	assert(host and held.is_empty())
	armed = true

func release() -> void:
	assert(not held.is_empty() and not released)
	assert(context.get_tree().root.get_node(receiver_path).get_instance_id() == receiver_instance)
	released = true
	incoming.push_front(held)
	print("LATE_PACKET_RELEASE " + JSON.stringify({"path": receiver_path, "receiver": receiver_instance,
		"mode": held.mode, "sha256": held.bytes.hex_encode().sha256_text()}))

func _poll() -> void:
	peer.poll()
	while peer.get_available_packet_count() > 0:
		var packet := {"sender": peer.get_packet_peer(), "mode": peer.get_packet_mode(),
			"channel": peer.get_packet_channel(), "late": false, "bytes": peer.get_packet()}
		var path := _rpc_path(packet.bytes, packet.sender)
		if armed and packet.mode == MultiplayerPeer.TRANSFER_MODE_UNRELIABLE and path.begins_with("NetworkCommandServer/"):
			armed = false
			packet.late = true
			held = packet
			receiver_path = path
			receiver_instance = context.get_tree().root.get_node(path).get_instance_id()
			print("LATE_PACKET_HELD " + JSON.stringify({"path": path, "receiver": receiver_instance,
				"mode": packet.mode, "sender": packet.sender, "sha256": packet.bytes.hex_encode().sha256_text()}))
		else:
			incoming.append(packet)

func _rpc_path(packet: PackedByteArray, sender: int) -> String:
	if packet.is_empty(): return ""
	var command := packet[0] & 7
	if command == 1 and packet.size() >= 38:
		paths["%s:%s" % [sender, packet.decode_u32(34)]] = packet.slice(38).get_string_from_utf8()
	elif command == 0:
		var sizes := [1, 2, 4]
		var width: int = sizes[(packet[0] >> 4) & 3]
		var cache_id: int = packet[1] if width == 1 else (packet.decode_u16(1) if width == 2 else packet.decode_u32(1))
		return packet.slice(cache_id & 0x7fffffff).get_string_from_utf8() if cache_id & 0x80000000 else paths.get("%s:%s" % [sender, cache_id], "")
	return ""

func has_held() -> bool: return not held.is_empty()

func detach() -> void:
	# The native peer owns forwarding lambdas that retain this RefCounted tap.
	# Break that diagnostic-only cycle after MultiplayerAPI has detached it.
	for signal_name in ["peer_connected", "peer_disconnected"]:
		for connection in peer.get_signal_connection_list(signal_name):
			peer.disconnect(signal_name, connection.callable)
	incoming.clear()
	held.clear()
	context = null
	peer = null

func _get_packet_script() -> PackedByteArray:
	var packet := incoming.pop_front() as Dictionary
	delivered_held = packet.late
	return packet.bytes
func _get_available_packet_count() -> int: return incoming.size()
func _get_packet_channel() -> int: return incoming.front().channel
func _get_packet_mode() -> MultiplayerPeer.TransferMode: return incoming.front().mode
func _get_packet_peer() -> int: return incoming.front().sender
func _put_packet_script(buffer: PackedByteArray) -> Error: return peer.put_packet(buffer)
func _get_max_packet_size() -> int: return 65535
func _get_connection_status() -> MultiplayerPeer.ConnectionStatus: return peer.get_connection_status()
func _get_unique_id() -> int: return cached_id
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
func _close() -> void: peer.close()
func _disconnect_peer(id: int, force: bool) -> void: peer.disconnect_peer(id, force)
