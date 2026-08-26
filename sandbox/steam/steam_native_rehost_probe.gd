extends Node

# Manual engine-level regression smoke for the patched GodotSteam dependency.
# It intentionally bypasses GameFactory's C# session, adapter, lobby, and
# MultiplayerApi layers so a failure identifies the dependency boundary.
const DEVELOPMENT_APP_ID := 480

func _ready() -> void:
	print("[native-test] Steam singleton=", Engine.has_singleton("Steam"))
	print("[native-test] SteamMultiplayerPeer=", ClassDB.class_exists("SteamMultiplayerPeer"))

	var initialization := Steam.steamInitEx(DEVELOPMENT_APP_ID, true)
	var status: int = int(initialization.get("status", -1))
	print("[native-test] init status=", status)
	if status != 0:
		push_error("[native-test] Steam initialization failed: " + str(initialization))
		return

	await _run_rehost_smoke()

func _run_rehost_smoke() -> void:
	var peer1 := SteamMultiplayerPeer.new()
	print("[native-test] create host 0 #1")
	var result1 := peer1.create_host(0)
	print("[native-test] result1=", result1)

	if result1 != OK:
		return

	print("[native-test] close #1")
	peer1.close()
	peer1 = null

	await get_tree().process_frame
	await get_tree().process_frame

	var peer2 := SteamMultiplayerPeer.new()
	print("[native-test] create host 0 #2")
	var result2 := peer2.create_host(0)
	print("[native-test] result2=", result2)
	if result2 == OK:
		peer2.close()
