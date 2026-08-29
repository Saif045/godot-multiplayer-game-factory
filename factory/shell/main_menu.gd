extends "res://addons/maaacks_game_template/examples/scenes/menus/main_menu/main_menu.gd"

func _ready() -> void:
	super._ready()
	new_game_button.text = "Host Game"

func new_game() -> void:
	GameShell.GameStartRequested()
	super.new_game()
