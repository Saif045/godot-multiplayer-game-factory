extends Node

# This file is deliberately the only GDScript in GameFactory that speaks to
# GodotSteam. It performs vendor calls and forwards raw data; C# owns policy.
const INVITE_DIALOG_METHOD := &"activateGameOverlayInviteDialog"

signal lobby_created_result(result: int, lobby_id: int)
signal lobby_joined_result(lobby_id: int, response: int)
signal lobby_data_changed(lobby_id: int)
signal lobby_member_changed(lobby_id: int, changed_id: int, making_change_id: int, chat_state: int)
signal lobby_invited(inviter_id: int, lobby_id: int)
signal join_requested(lobby_id: int, friend_id: int)
signal friend_presence_updated(user_id: int)
signal overlay_changed(active: bool)
signal lobby_search_completed(lobbies: Array)
signal transport_trace(event_name: String, fields: Dictionary)

var _trace_enabled := false
var _peer_generation := 0
var _traced_peers: Dictionary = {}
var _retain_for_test := false
var _retained_test_peers: Array[MultiplayerPeer] = []

func _ready() -> void:
	_trace_enabled = "--steam-transport-trace" in OS.get_cmdline_args() or "--steam-transport-trace" in OS.get_cmdline_user_args()
	_retain_for_test = _trace_enabled and ("--steam-transport-retain-closed-peer" in OS.get_cmdline_args() or "--steam-transport-retain-closed-peer" in OS.get_cmdline_user_args())
	set_process(_trace_enabled)
	if _trace_enabled:
		Steam.network_connection_status_changed.connect(_on_native_connection_status_changed)
	# print("Steam singleton: ", Engine.has_singleton("Steam"))
	# print("SteamMultiplayerPeer: ", ClassDB.class_exists("SteamMultiplayerPeer"))
	Steam.lobby_created.connect(_on_lobby_created)
	Steam.lobby_joined.connect(_on_lobby_joined)
	Steam.lobby_data_update.connect(_on_lobby_data_update)
	Steam.lobby_chat_update.connect(_on_lobby_chat_update)
	Steam.lobby_invite.connect(_on_lobby_invite)
	Steam.join_requested.connect(_on_join_requested)
	Steam.friend_rich_presence_update.connect(_on_friend_rich_presence_update)
	Steam.overlay_toggled.connect(_on_overlay_toggled)
	Steam.lobby_match_list.connect(_on_lobby_match_list)

func initialize(app_id: int) -> Dictionary:
	return Steam.steamInitEx(app_id, true)

func shutdown() -> void:
	Steam.steamShutdown()

func local_user() -> Dictionary:
	return { "id": Steam.getSteamID(), "name": Steam.getPersonaName() }

func create_lobby(lobby_type: int, max_members: int) -> void:
	Steam.createLobby(lobby_type, max_members)

func join_lobby(lobby_id: int) -> void:
	_trace("lobby_join_requested", { "lobby_id": str(lobby_id) })
	Steam.joinLobby(lobby_id)

func leave_lobby(lobby_id: int) -> void:
	_trace("lobby_leave_requested", { "lobby_id": str(lobby_id) })
	Steam.leaveLobby(lobby_id)
	_trace("lobby_leave_returned", { "lobby_id": str(lobby_id) })

func set_lobby_joinable(lobby_id: int, joinable: bool) -> bool:
	return Steam.setLobbyJoinable(lobby_id, joinable)

func set_lobby_member_limit(lobby_id: int, member_limit: int) -> bool:
	return Steam.setLobbyMemberLimit(lobby_id, member_limit)

func set_lobby_data(lobby_id: int, key: String, value: String) -> bool:
	return Steam.setLobbyData(lobby_id, key, value)

func set_lobby_member_data(lobby_id: int, key: String, value: String) -> void:
	Steam.setLobbyMemberData(lobby_id, key, value)

func get_lobby_summary(lobby_id: int) -> Dictionary:
	return {
		"id": lobby_id,
		"owner_id": Steam.getLobbyOwner(lobby_id),
		"member_count": Steam.getNumLobbyMembers(lobby_id),
		"member_limit": Steam.getLobbyMemberLimit(lobby_id),
		"joinable": Steam.getLobbyData(lobby_id, "joinable") != "false",
		"gamefactory_protocol": Steam.getLobbyData(lobby_id, "gamefactory_protocol")
	}

func get_lobby_members(lobby_id: int) -> Array:
	var members: Array = []
	var count := Steam.getNumLobbyMembers(lobby_id)
	for index in count:
		var user_id := Steam.getLobbyMemberByIndex(lobby_id, index)
		members.append({ "id": user_id, "name": Steam.getFriendPersonaName(user_id) })
	return members

func get_lobby_data(lobby_id: int, key: String) -> String:
	return Steam.getLobbyData(lobby_id, key)

func get_lobby_owner(lobby_id: int) -> int:
	return Steam.getLobbyOwner(lobby_id)

func find_lobbies(metadata: Dictionary, max_results: int) -> void:
	Steam.addRequestLobbyListResultCountFilter(max_results)
	for key in metadata:
		Steam.addRequestLobbyListStringFilter(str(key), str(metadata[key]), Steam.LOBBY_COMPARISON_EQUAL)
	Steam.requestLobbyList()

func get_friends() -> Array:
	var friends: Array = []
	var count := Steam.getFriendCount()
	for index in count:
		var user_id := Steam.getFriendByIndex(index, 4)
		friends.append({ "id": user_id, "name": Steam.getFriendPersonaName(user_id) })
	return friends

func get_presence(user_id: int) -> Dictionary:
	return {
		"state": str(Steam.getFriendPersonaState(user_id)),
		"connect": Steam.getFriendRichPresence(user_id, "connect"),
		"gamefactory_protocol": Steam.getFriendRichPresence(user_id, "gamefactory_protocol")
	}

func request_friend_presence(user_id: int) -> void:
	Steam.requestFriendRichPresence(user_id)

func is_friend(user_id: int) -> bool:
	return Steam.getFriendRelationship(user_id) == 3

func activate_invite_overlay(lobby_id: int) -> void:
	var exists := Steam.has_method(INVITE_DIALOG_METHOD)
	print("[steam][bridge] method=", INVITE_DIALOG_METHOD, "; lobby=", lobby_id, "; exists=", exists, "; enabled=", Steam.isOverlayEnabled())
	if not exists:
		push_error("GodotSteam does not expose " + String(INVITE_DIALOG_METHOD))
		return
	Steam.call(INVITE_DIALOG_METHOD, lobby_id)

func activate_friends_overlay() -> void:
	print("[steam][bridge] friends overlay requested; enabled=", Steam.isOverlayEnabled())
	Steam.activateGameOverlay("Friends")

func is_overlay_enabled() -> bool:
	return Steam.isOverlayEnabled()

func activate_user_overlay(user_id: int) -> void:
	Steam.activateGameOverlayToUser("steamid", user_id)

func set_rich_presence(key: String, value: String) -> bool:
	return Steam.setRichPresence(key, value)

func clear_rich_presence() -> void:
	Steam.clearRichPresence()

func create_host_peer(lobby_id: int, _virtual_port: int) -> MultiplayerPeer:
	var peer := SteamMultiplayerPeer.new()
	_trace_new_peer(peer, lobby_id, "host")
	var result := peer.host_with_lobby(lobby_id)
	_trace("peer_create_returned", { "peer_instance_id": str(peer.get_instance_id()), "result": str(result) })
	if result != OK:
		push_error("SteamMultiplayerPeer.host_with_lobby failed: %s" % result)
		return null
	return peer

func create_client_peer(lobby_id: int, _virtual_port: int) -> MultiplayerPeer:
	var peer := SteamMultiplayerPeer.new()
	_trace_new_peer(peer, lobby_id, "client")
	var result := peer.connect_to_lobby(lobby_id)
	_trace("peer_create_returned", { "peer_instance_id": str(peer.get_instance_id()), "result": str(result) })
	if result != OK:
		push_error("SteamMultiplayerPeer.connect_to_lobby failed: %s" % result)
		return null
	return peer

func get_steam_id_for_peer(peer: MultiplayerPeer, peer_id: int) -> int:
	return peer.get_steam_id_for_peer_id(peer_id)

func get_peer_id_for_steam(peer: MultiplayerPeer, user_id: int) -> int:
	return peer.get_peer_id_for_steam_id(user_id)

func _on_lobby_created(result: int, lobby_id: int) -> void:
	_trace("lobby_created_callback", { "lobby_id": str(lobby_id), "result": str(result) })
	lobby_created_result.emit(result, lobby_id)

func _on_lobby_joined(lobby_id: int, _permissions: int, _locked: bool, response: int) -> void:
	_trace("lobby_joined_callback", { "lobby_id": str(lobby_id), "response": str(response) })
	lobby_joined_result.emit(lobby_id, response)

func _on_lobby_data_update(success: int, lobby_id: int, _member_id: int) -> void:
	if success == 1:
		lobby_data_changed.emit(lobby_id)

func _on_lobby_chat_update(lobby_id: int, changed_id: int, making_change_id: int, chat_state: int) -> void:
	_trace("lobby_member_callback", { "lobby_id": str(lobby_id), "changed_steam_id": str(changed_id), "chat_state": str(chat_state) })
	lobby_member_changed.emit(lobby_id, changed_id, making_change_id, chat_state)

func _on_lobby_invite(inviter_id: int, lobby_id: int, _game_id: int) -> void:
	lobby_invited.emit(inviter_id, lobby_id)

func _on_join_requested(lobby_id: int, friend_id: int) -> void:
	join_requested.emit(lobby_id, friend_id)

func _on_friend_rich_presence_update(user_id: int, _app_id: int) -> void:
	friend_presence_updated.emit(user_id)

func _on_overlay_toggled(active: bool, _user_initiated: bool, _app_id: int) -> void:
	overlay_changed.emit(active)

func _on_lobby_match_list(lobbies: Array) -> void:
	lobby_search_completed.emit(lobbies)

# Weak references and ID-only signal bindings keep observation from extending
# the lifetime of a RefCounted peer. Native handles are recorded independently
# because the Steam callback is process-wide, not proof of peer ownership.
func _trace_new_peer(peer: MultiplayerPeer, lobby_id: int, role: String) -> void:
	if not _trace_enabled:
		return
	_peer_generation += 1
	# Explicit test mode holds at most the current and previous peer. This
	# proves close isolation without depending on garbage collection timing.
	if _retain_for_test:
		_retained_test_peers.append(peer)
		if _retained_test_peers.size() > 2:
			_retained_test_peers.pop_front()
	var instance_id := peer.get_instance_id()
	_traced_peers[instance_id] = { "ref": weakref(peer), "status": -1, "generation": _peer_generation, "lobby_id": str(lobby_id), "role": role }
	peer.set_debug_level(SteamMultiplayerPeer.DEBUG_LEVEL_PEER)
	peer.peer_connected.connect(_on_transport_peer_signal.bind(instance_id, "peer_connected"))
	peer.peer_disconnected.connect(_on_transport_peer_signal.bind(instance_id, "peer_disconnected"))
	_trace("peer_object_created", { "peer_instance_id": str(instance_id), "peer_generation": str(_peer_generation), "lobby_id": str(lobby_id), "role": role })
	if _retain_for_test:
		_trace("peer_retained_for_test", { "peer_instance_id": str(instance_id), "retained_count": str(_retained_test_peers.size()) })

func _on_transport_peer_signal(peer_id: int, instance_id: int, event_name: String) -> void:
	var record: Dictionary = _traced_peers.get(instance_id, {})
	var peer: MultiplayerPeer = record.get("ref").get_ref() if not record.is_empty() else null
	_trace(event_name, { "peer_instance_id": str(instance_id), "peer_generation": str(record.get("generation")), "lobby_id": record.get("lobby_id"), "remote_peer_id": str(peer_id), "remote_steam_id": str(peer.get_steam_id_for_peer_id(peer_id)) if peer != null and event_name == "peer_connected" else null })

func _process(_delta: float) -> void:
	for instance_id in _traced_peers.keys():
		var record: Dictionary = _traced_peers[instance_id]
		var peer: MultiplayerPeer = record.ref.get_ref()
		if peer == null:
			_trace("peer_object_released", { "peer_instance_id": str(instance_id), "peer_generation": str(record.generation), "lobby_id": record.lobby_id })
			_traced_peers.erase(instance_id)
			continue
		var status := peer.get_connection_status()
		if status != record.status:
			record.status = status
			_trace("peer_status_changed", { "peer_instance_id": str(instance_id), "peer_generation": str(record.generation), "lobby_id": record.lobby_id, "connection_status": str(status), "local_peer_id": str(peer.get_unique_id()) })

func _on_native_connection_status_changed(handle: int, connection: Dictionary, old_state: int) -> void:
	var fields := { "native_handle": str(handle), "old_state": str(old_state), "observed_peer_instance_ids": str(_traced_peers.keys()) }
	for key in ["identity", "user_data", "listen_socket", "connection_state", "end_reason", "end_debug", "debug_description"]:
		fields[str(key)] = str(connection.get(key))
	_trace("native_connection_status_changed", fields)

func _trace(event_name: String, fields: Dictionary) -> void:
	if not _trace_enabled:
		return
	fields["local_steam_id"] = str(Steam.getSteamID())
	fields["process_id"] = str(OS.get_process_id())
	transport_trace.emit(event_name, fields)
