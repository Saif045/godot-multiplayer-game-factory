extends Node
const _gf_trace = preload("res://factory/networking/netfox/netfox_lifecycle_trace.gd")
## This class provides convenience signals for multiplayer games.
##
## While the client start/stop and peer join/leave events are trivial, the 
## server side has no similar events. This means that if you'd like to add some
## funcionality that should happen on server start, you either have to couple
## the code ( i.e. call it wherever you start the server ) or introduce a custom
## event to decouple your code from your network init code.
##
## By providing these convenience events, you can forego all that and instead 
## just listen to a single signal that should work no matter what.
##
## [i]Note:[/i] This class also manages [NetworkTime] start/stop, so as long as
## network events are enabled, you don't need to manually call start/stop.

## Event emitted when the [MultiplayerAPI] is changed
signal on_multiplayer_change(old: MultiplayerAPI, new: MultiplayerAPI)

## Event emitted when the server starts
signal on_server_start()

## Event emitted when the server stops for any reason
signal on_server_stop()

## Event emitted when the client starts
signal on_client_start(id: int)

## Event emitted when the client stops.
##
## This can happen due to either the client itself or the server disconnecting
## for whatever reason.
signal on_client_stop()

## Event emitted when a new peer joins the game.
signal on_peer_join(id: int)

## Event emitted when a peer leaves the game.
signal on_peer_leave(id: int)

## Whether the events are enabled.
##
## Events are only emitted when it's enabled. Disabling this can free up some 
## performance, as when enabled, the multiplayer API and the host are
## continuously checked for changes.
##
## The initial value is taken from the Netfox project settings.
var enabled: bool:
	get: return _enabled
	set(v): _set_enabled(v)

var _is_server: bool = false
# Local v1.35.3 correction: a listen-server close can emit server_disconnected.
# Pair the notification with its active role; polling remains the server fallback.
var _is_client: bool = false
var _multiplayer: MultiplayerAPI
var _enabled: bool = false

## Check if we're running as server.
func is_server() -> bool:
	if multiplayer == null:
		return false
	
	var peer := multiplayer.multiplayer_peer
	if peer == null:
		return false
	
	if peer is OfflineMultiplayerPeer:
		return false
		
	if peer.get_connection_status() != MultiplayerPeer.CONNECTION_CONNECTED:
		return false
	
	if not multiplayer.is_server():
		return false
	
	return true

func _ready() -> void:
	NetfoxLogger.register_tag(_get_peer_id_tag, -99)

	enabled = ProjectSettings.get_setting(&"netfox/events/enabled", true)
	_gf_trace.record(self, "trace_ready")

	# Automatically start ticking when entering multiplayer and stop when 
	# leaving multiplayer
	on_server_start.connect(NetworkTime.start)
	on_server_stop.connect(NetworkTime.stop)
	on_client_start.connect(func(id): NetworkTime.start())
	on_client_stop.connect(NetworkTime.stop)

func _exit_tree() -> void:
	NetfoxLogger.free_tag(_get_peer_id_tag)

func _get_peer_id_tag() -> String:
	return "#%d" % multiplayer.get_unique_id()

func _process(_delta: float) -> void:
	if multiplayer != _multiplayer:
		_gf_trace.record(self, "multiplayer_change", {"old": _multiplayer.get_instance_id() if _multiplayer else 0, "new": multiplayer.get_instance_id(), "was_server": _is_server})
		_disconnect_handlers(_multiplayer)
		_connect_handlers(multiplayer)
		
		on_multiplayer_change.emit(_multiplayer, multiplayer)
		_multiplayer = multiplayer
	
	if not _is_server and is_server():
		_is_server = true
		_gf_trace.record(self, "server_start", {"old_server": false, "new_server": true})
		on_server_start.emit()
	
	if _is_server and not is_server():
		_is_server = false
		_gf_trace.record(self, "server_stop", {"old_server": true, "new_server": false})
		on_server_stop.emit()

func _connect_handlers(mp: MultiplayerAPI) -> void:
	if mp == null:
		return

	_gf_trace.record(self, "handlers_connect", {"api": mp.get_instance_id()})
	mp.connected_to_server.connect(_handle_connected_to_server)
	mp.server_disconnected.connect(_handle_server_disconnected)
	mp.peer_connected.connect(_handle_peer_connected)
	mp.peer_disconnected.connect(_handle_peer_disconnected)

func _disconnect_handlers(mp: MultiplayerAPI) -> void:
	if mp == null:
		return

	_gf_trace.record(self, "handlers_disconnect", {"api": mp.get_instance_id()})
	mp.connected_to_server.disconnect(_handle_connected_to_server)
	mp.server_disconnected.disconnect(_handle_server_disconnected)
	mp.peer_connected.disconnect(_handle_peer_connected)
	mp.peer_disconnected.disconnect(_handle_peer_disconnected)

func _handle_connected_to_server() -> void:
	_is_client = true
	_gf_trace.record(self, "client_start")
	on_client_start.emit(multiplayer.get_unique_id())

func _handle_server_disconnected() -> void:
	if _is_server:
		# Stop immediately, before peer detachment and another NetworkTime frame.
		_is_server = false
		_gf_trace.record(self, "server_stop", {"old_server": true, "new_server": false, "trigger": "server_disconnected"})
		on_server_stop.emit()
		return
	if not _is_client:
		_gf_trace.record(self, "server_disconnected_without_client")
		return
	_is_client = false
	_gf_trace.record(self, "client_stop")
	on_client_stop.emit()

func _handle_peer_connected(id: int) -> void:
	_gf_trace.record(self, "peer_join", {"peer": id})
	on_peer_join.emit(id)

func _handle_peer_disconnected(id: int) -> void:
	_gf_trace.record(self, "peer_leave", {"peer": id})
	on_peer_leave.emit(id)

func _set_enabled(enable: bool) -> void:
	if _enabled and not enable:
		_disconnect_handlers(_multiplayer)
		_multiplayer = null
	if not _enabled and enable:
		_multiplayer = multiplayer
		_connect_handlers(_multiplayer)

	_enabled = enable
	set_process(enable)
