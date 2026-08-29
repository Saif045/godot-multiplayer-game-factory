extends "res://addons/maaacks_game_template/examples/scenes/windows/pause_menu.gd"

func open() -> void:
	if not is_opened:
		GameShell.PauseOpened()
	super.open()

func close() -> void:
	if is_opened:
		GameShell.PauseClosed()
	super.close()

func _on_main_menu_confirmation_confirmed() -> void:
	GameShell.LeaveGame()

func _on_exit_confirmation_confirmed() -> void:
	GameShell.LeaveGame()
