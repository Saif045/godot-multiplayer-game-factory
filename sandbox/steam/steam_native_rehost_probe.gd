extends Node

# Manual engine-level regression smoke for the patched GodotSteam dependency.
# It intentionally bypasses GameFactory's C# session, adapter, lobby, and
# MultiplayerApi layers so a failure identifies the dependency boundary.
func _ready() -> void:
	# print("[native-test] Steam singleton=", Engine.has_singleton("Steam"))
	# print("[native-test] SteamMultiplayerPeer=", ClassDB.class_exists("SteamMultiplayerPeer"))

	# The project's SteamPlatform autoload already initializes Steam once.
	# Reinitializing here duplicates GodotSteam's process_frame callback.
	var local_steam_id := Steam.getSteamID()
	print("[native-test] initialized local_steam_id=", local_steam_id)
	if local_steam_id <= 0:
		push_error("[native-test] SteamPlatform did not initialize Steam")
		get_tree().quit(1)
		return

	await _run_rehost_smoke()

func _run_rehost_smoke() -> void:
	var peer1 := SteamMultiplayerPeer.new()
	print("[native-test] create host 0 #1")
	var result1 := peer1.create_host(0)
	print("[native-test] result1=", result1)

	if result1 != OK:
		get_tree().quit(1)
		return

	print("[native-test] close #1")
	peer1.close()
	# Keep the closed object alive while a different peer owns the new listener.
	if peer1.get_connection_status() != MultiplayerPeer.CONNECTION_DISCONNECTED:
		push_error("[native-test] closed peer was not disconnected")
		get_tree().quit(1)
		return

	var peer2 := SteamMultiplayerPeer.new()
	print("[native-test] create host 0 #2")
	var result2 := peer2.create_host(0)
	print("[native-test] result2=", result2)
	if result2 != OK:
		get_tree().quit(1)
		return
	peer2.close()
	# Reopening the same retained object must initialize a fresh lifecycle.
	var result3 := peer1.create_host(0)
	print("[native-test] result3=", result3)
	peer1.close()
	if result3 != OK:
		get_tree().quit(1)
		return
	print("[native-test] PASS retained closed peer + new peer + same-object reopen")
	get_tree().quit(0)
