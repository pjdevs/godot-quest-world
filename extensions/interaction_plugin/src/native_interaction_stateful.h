#pragma once

#include <godot_cpp/classes/node.hpp>

using namespace godot;

class NativeInteractionStateful : public Node {
	GDCLASS(NativeInteractionStateful, Node)

public:
	enum InteractionState {
		IDLE,
		ACTIVATING,
		ACTIVATED,
		DEACTIVATING,
	};

protected:
	static void _bind_methods();

public:
	void _ready() override;

	void set_initial_state(InteractionState p_state);
	InteractionState get_initial_state() const;
	InteractionState get_state() const;
	bool set_state(InteractionState p_state);

private:
	InteractionState initial_state = IDLE;
	InteractionState state = IDLE;
};

VARIANT_ENUM_CAST(NativeInteractionStateful::InteractionState)
