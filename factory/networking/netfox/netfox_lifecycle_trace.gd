extends RefCounted
## Opt-in observations only; never owns Netfox lifecycle or RPC routing.
## Enable with --netfox-lifecycle-trace on both participants.

static var _rpc_counts: Dictionary = {}
static var _last_samples: Dictionary = {}

static func enabled() -> bool:
	return not Engine.is_editor_hint() and (OS.get_cmdline_args().has("--netfox-lifecycle-trace") or OS.get_cmdline_user_args().has("--netfox-lifecycle-trace"))

static func record(node: Node, event: String, fields: Dictionary = {}) -> void:
	if not enabled():
		return
	var mp := node.multiplayer
	var has_peer := mp.has_multiplayer_peer()
	var data := fields.duplicate()
	data.merge({
		"event": event, "utc": Time.get_datetime_string_from_system(true, true),
		"unix_seconds": Time.get_unix_time_from_system(), "monotonic_us": Time.get_ticks_usec(),
		"node": str(node.get_path()), "instance": node.get_instance_id(),
		"multiplayer": mp.get_instance_id(), "has_peer": has_peer,
		"local_peer": mp.get_unique_id() if has_peer else 0,
		"peers": Array(mp.get_peers()) if has_peer else [], "is_server": mp.is_server() if has_peer else false,
		"sender": mp.get_remote_sender_id(), "authority": node.get_multiplayer_authority()
	}, true)
	print("NFTRACE " + JSON.stringify(data))

static func rpc_sample(node: Node, event: String, target: int, fields: Dictionary = {}) -> void:
	if not enabled():
		return
	var key := "%s:%s:%s:%s" % [node.get_instance_id(), node.get_path(), event, target]
	_rpc_counts[key] = int(_rpc_counts.get(key, 0)) + 1
	var now := Time.get_ticks_msec()
	if now - int(_last_samples.get(key, -1000)) >= 1000:
		_last_samples[key] = now
		var data := fields.duplicate()
		data["target"] = target
		data["count"] = _rpc_counts[key]
		record(node, event, data)

static func flush(node: Node) -> void:
	if not enabled():
		return
	record(node, "rpc_counts", {"counts": _rpc_counts.duplicate()})
	_rpc_counts.clear()
	_last_samples.clear()

static func autoloads(node: Node) -> void:
	if not enabled():
		return
	var names := ["NetworkTime", "NetworkEvents", "NetworkTimeSynchronizer", "NetworkRollback", "NetworkPerformance"]
	var instances: Dictionary = {}
	var tree_root := node.get_tree().root
	for singleton_name in names:
		var singleton := tree_root.get_node(NodePath(singleton_name))
		var matches: Array = []
		for child in tree_root.get_children():
			if child.get_script() == singleton.get_script():
				matches.append({"node": str(child.get_path()), "instance": child.get_instance_id()})
		instances[singleton_name] = {"count": matches.size(), "nodes": matches, "script": singleton.get_script().resource_path}
	record(node, "autoloads", {"instances": instances})
