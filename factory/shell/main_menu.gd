extends "res://addons/maaacks_game_template/examples/scenes/menus/main_menu/main_menu.gd"

func _ready() -> void:
	super._ready()
	new_game_button.text = "Host Game"
	var join_button := Button.new()
	join_button.text = "Join Game"
	join_button.pressed.connect(GameShell.OpenJoinMenu)
	new_game_button.get_parent().add_child(join_button)
	GameShell.MainMenuShown()

func new_game() -> void:
	GameShell.GameStartRequested()
	super.new_game()
