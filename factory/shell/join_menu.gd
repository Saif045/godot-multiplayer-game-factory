extends Control

func _ready() -> void:
	var list := $Panel/VBox/Friends
	GameShell.PopulateJoinLobbies(list)

func _on_back_pressed() -> void:
	get_tree().change_scene_to_file("res://factory/shell/main_menu.tscn")
