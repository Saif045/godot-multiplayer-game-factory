extends "res://addons/maaacks_game_template/examples/scenes/windows/pause_menu.gd"

var return_button: Button

func _ready() -> void:
	super._ready()
	return_button = Button.new()
	return_button.text = "Return to Lobby"
	var buttons = get_node("ContentContainer/BoxContainer/MenuButtonsMargin/MenuButtons")
	buttons.add_child(return_button)
	buttons.move_child(return_button, 1)
	return_button.pressed.connect(_on_return_to_lobby_pressed)
	return_button.visible = false

func open() -> void:
	return_button.visible = GameShell.CanReturnToLobby()
	return_button.disabled = false
	if not is_opened:
		GameShell.PauseOpened()
	super.open()

func close() -> void:
	if is_opened:
		GameShell.PauseClosed()
	super.close()

func _on_return_to_lobby_pressed() -> void:
	return_button.disabled = true
	GameShell.ReturnToLobby()

func _on_main_menu_confirmation_confirmed() -> void:
	GameShell.LeaveGame()

func _on_exit_confirmation_confirmed() -> void:
	GameShell.LeaveGame()
