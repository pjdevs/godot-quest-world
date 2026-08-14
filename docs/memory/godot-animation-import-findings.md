# Godot animation import findings

- The UAL GLB source names looped clips with an `_Loop` suffix, but Godot's imported `AnimationPlayer` exposes the names without that suffix. For example, `Idle_Loop` becomes `Idle`, `Jog_Fwd_Loop` becomes `Jog_Fwd`, `Sprint_Loop` becomes `Sprint` and `Jump_Loop` becomes `Jump`.
- Before configuring an AnimationTree, inspect the actual runtime list with `AnimationPlayer.GetAnimationList()`/`get_animation_list()` and validate every configured name with `HasAnimation()`.
- In Godot 4.7, `AnimationNodeStateMachine` starts through a `Start -> Locomotion` transition; there is no usable `start_node` property in the GDScript API used here.
- `AnimationNodeBlendSpace2D.add_blend_point()` takes an integer point index as its optional third argument, not a point name. Generated scenes may therefore serialize blend points with numeric names.
