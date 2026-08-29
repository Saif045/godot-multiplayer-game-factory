extends "res://addons/maaacks_game_template/examples/scenes/windows/pause_menu.gd"

func _on_main_menu_confirmation_confirmed() -> void:
	GameShell.LeaveGame()

func _on_exit_confirmation_confirmed() -> void:
	GameShell.LeaveGame()
