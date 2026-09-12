extends Camera3D

func _ready() -> void:
	current = get_parent().get_node("Input").is_multiplayer_authority()
