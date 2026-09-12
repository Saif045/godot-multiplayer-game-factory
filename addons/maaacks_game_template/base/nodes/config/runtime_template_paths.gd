## Runtime-safe path access for Maaack's Game Template scenes.
##
## The editor plugin owns setup and editor integration, but exported games
## cannot resolve an EditorPlugin global class. Runtime scenes use this helper
## to preserve the plugin's path-override behavior without depending on it.
extends RefCounted

const PROJECT_SETTINGS_PATH := "maaacks_game_template/"

static func _get_path(setting_name: String, override_path: String = "") -> String:
	if not override_path.is_empty() and FileAccess.file_exists(override_path):
		return override_path
	return ProjectSettings.get_setting(PROJECT_SETTINGS_PATH + setting_name, override_path)

static func get_main_menu_path(override_path: String = "") -> String:
	return _get_path("main_menu_scene_path", override_path)

static func get_game_path(override_path: String = "") -> String:
	return _get_path("game_scene_path", override_path)

static func get_ending_scene_path(override_path: String = "") -> String:
	return _get_path("ending_scene_path", override_path)

static func get_loading_scene_path(override_path: String = "") -> String:
	return _get_path("loading_scene_path", override_path)
