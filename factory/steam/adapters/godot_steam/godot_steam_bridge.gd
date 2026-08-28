extends Node

# This file is deliberately the only GDScript in GameFactory that speaks to
# GodotSteam. It performs vendor calls and forwards raw data; C# owns policy.
const INVITE_DIALOG_METHOD := &"activateGameOverlayInviteDialog"

signal lobby_created_result(result: int, lobby_id: int)
signal lobby_joined_result(lobby_id: int, response: int)
signal lobby_data_changed(lobby_id: int)
signal lobby_member_changed(lobby_id: int, changed_id: int, making_change_id: int, chat_state: int)
signal lobby_invited(inviter_id: int, lobby_id: int)
signal overlay_changed(active: bool)
signal lobby_search_completed(lobbies: Array)

func _ready() -> void:
	# print("Steam singleton: ", Engine.has_singleton("Steam"))
	# print("SteamMultiplayerPeer: ", ClassDB.class_exists("SteamMultiplayerPeer"))
	Steam.lobby_created.connect(_on_lobby_created)
	Steam.lobby_joined.connect(_on_lobby_joined)
	Steam.lobby_data_update.connect(_on_lobby_data_update)
	Steam.lobby_chat_update.connect(_on_lobby_chat_update)
	Steam.lobby_invite.connect(_on_lobby_invite)
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
	Steam.joinLobby(lobby_id)

func leave_lobby(lobby_id: int) -> void:
	Steam.leaveLobby(lobby_id)

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
		"joinable": Steam.getLobbyData(lobby_id, "joinable") != "false"
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
	return { "state": str(Steam.getFriendPersonaState(user_id)), "connect": Steam.getFriendRichPresence(user_id, "connect") }

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
	var result := peer.host_with_lobby(lobby_id)
	if result != OK:
		push_error("SteamMultiplayerPeer.host_with_lobby failed: %s" % result)
		return null
	return peer

func create_client_peer(lobby_id: int, _virtual_port: int) -> MultiplayerPeer:
	var peer := SteamMultiplayerPeer.new()
	var result := peer.connect_to_lobby(lobby_id)
	if result != OK:
		push_error("SteamMultiplayerPeer.connect_to_lobby failed: %s" % result)
		return null
	return peer

func get_steam_id_for_peer(peer: MultiplayerPeer, peer_id: int) -> int:
	return peer.get_steam_id_for_peer_id(peer_id)

func get_peer_id_for_steam(peer: MultiplayerPeer, user_id: int) -> int:
	return peer.get_peer_id_for_steam_id(user_id)

func _on_lobby_created(result: int, lobby_id: int) -> void:
	lobby_created_result.emit(result, lobby_id)

func _on_lobby_joined(lobby_id: int, _permissions: int, _locked: bool, response: int) -> void:
	lobby_joined_result.emit(lobby_id, response)

func _on_lobby_data_update(success: int, lobby_id: int, _member_id: int) -> void:
	if success == 1:
		lobby_data_changed.emit(lobby_id)

func _on_lobby_chat_update(lobby_id: int, changed_id: int, making_change_id: int, chat_state: int) -> void:
	lobby_member_changed.emit(lobby_id, changed_id, making_change_id, chat_state)

func _on_lobby_invite(inviter_id: int, lobby_id: int, _game_id: int) -> void:
	lobby_invited.emit(inviter_id, lobby_id)

func _on_overlay_toggled(active: bool, _user_initiated: bool, _app_id: int) -> void:
	overlay_changed.emit(active)

func _on_lobby_match_list(lobbies: Array) -> void:
	lobby_search_completed.emit(lobbies)
