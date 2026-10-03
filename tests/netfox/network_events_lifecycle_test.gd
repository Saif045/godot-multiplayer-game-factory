extends SceneTree
## Run with Godot --headless --path . --script <this file> --netfox-lifecycle-trace.
## Exercises event pairing without starting a lobby or Netfox time loop.
const Trace = preload("res://factory/networking/netfox/netfox_lifecycle_trace.gd")

func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	var events := root.get_node("NetworkEvents")
	events.set("enabled", false)
	# Observe the real event handlers without invoking the autoload time loop.
	for signal_name in ["on_client_start", "on_client_stop", "on_server_stop"]:
		for connection in events.get_signal_connection_list(signal_name):
			events.disconnect(signal_name, connection.callable)
	var observed: Array[String] = []
	events.connect("on_client_start", func(_id): observed.append("start"))
	events.connect("on_client_stop", func(): observed.append("stop"))
	var server_stops: Array[int] = []
	events.connect("on_server_stop", func(): server_stops.append(1))

	# Server close (or a repeated notification) is not a client lifecycle.
	events.call("_handle_server_disconnected")
	if not _expect(observed.is_empty(), "host disconnect emitted client-stop"):
		return
	# A listen-server notification must stop its active role immediately.
	events.set("_is_server", true)
	events.call("_handle_server_disconnected")
	events.call("_handle_server_disconnected")
	if not _expect(server_stops.size() == 1 and observed.is_empty() and not events.get("_is_server"), "host stop was deferred, duplicated, or misrouted to client-stop"):
		return
	for session in range(2):
		events.call("_handle_connected_to_server")
		events.call("_handle_server_disconnected")
		events.call("_handle_server_disconnected")
		if not _expect(observed.size() == (session + 1) * 2, "client stop was duplicated or session reuse lost its stop"):
			return
	if not _expect(observed == ["start", "stop", "start", "stop"], "client lifecycle order changed"):
		return

	# Teardown observations must not query peer APIs after detachment.
	events.multiplayer.multiplayer_peer = null
	Trace.record(events, "detached_peer_test")
	print("NETFOX_LIFECYCLE_TEST_PASS")
	quit(0)

func _expect(condition: bool, message: String) -> bool:
	if not condition:
		push_error(message)
		quit(1)
	return condition
