#ifndef INTERACTION_PLUGIN_REGISTER_TYPES_H
#define INTERACTION_PLUGIN_REGISTER_TYPES_H

#include <godot_cpp/godot.hpp>

void initialize_gdextension_types(godot::ModuleInitializationLevel p_level);
void uninitialize_gdextension_types(godot::ModuleInitializationLevel p_level);

#endif // INTERACTION_PLUGIN_REGISTER_TYPES_H
