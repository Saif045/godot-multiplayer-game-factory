class_name GameFactoryHealthAttributeSet
extends AttributeSet

var Health: AttributeData = AttributeData.new(100.0)

func pre_attribute_change(attribute_name: String, proposed_value: float) -> float:
	if attribute_name == "Health":
		return clampf(proposed_value, 0.0, 100.0)
	return proposed_value
