#include "native_interaction_stateful.h"

#include <godot_cpp/core/class_db.hpp>

void NativeInteractionStateful::_bind_methods() {
	ClassDB::bind_method(D_METHOD("set_initial_state", "state"), &NativeInteractionStateful::set_initial_state);
	ClassDB::bind_method(D_METHOD("get_initial_state"), &NativeInteractionStateful::get_initial_state);
	ClassDB::bind_method(D_METHOD("get_state"), &NativeInteractionStateful::get_state);
	ClassDB::bind_method(D_METHOD("set_state", "state"), &NativeInteractionStateful::set_state);

	ADD_PROPERTY(PropertyInfo(Variant::INT, "initial_state", PROPERTY_HINT_ENUM, "Idle,Activating,Activated,Deactivating"), "set_initial_state", "get_initial_state");
	ADD_PROPERTY(PropertyInfo(Variant::INT, "state", PROPERTY_HINT_ENUM, "Idle,Activating,Activated,Deactivating", PROPERTY_USAGE_EDITOR | PROPERTY_USAGE_READ_ONLY), "", "get_state");

	ADD_SIGNAL(MethodInfo("interaction_state_changed", PropertyInfo(Variant::INT, "old_state"), PropertyInfo(Variant::INT, "new_state")));

	BIND_ENUM_CONSTANT(IDLE);
	BIND_ENUM_CONSTANT(ACTIVATING);
	BIND_ENUM_CONSTANT(ACTIVATED);
	BIND_ENUM_CONSTANT(DEACTIVATING);
}

void NativeInteractionStateful::_ready() {
	state = initial_state;
}

void NativeInteractionStateful::set_initial_state(InteractionState p_state) {
	initial_state = p_state;
}

NativeInteractionStateful::InteractionState NativeInteractionStateful::get_initial_state() const {
	return initial_state;
}

NativeInteractionStateful::InteractionState NativeInteractionStateful::get_state() const {
	return state;
}

bool NativeInteractionStateful::set_state(InteractionState p_state) {
	if (state == p_state) {
		return false;
	}

	InteractionState old_state = state;
	state = p_state;
	emit_signal("interaction_state_changed", old_state, state);
	return true;
}
