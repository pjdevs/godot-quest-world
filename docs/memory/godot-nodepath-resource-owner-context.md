# Godot NodePath picker context for nested Resource properties

When a `NodePath` is exported by a `Resource` stored inside a gameplay action or interaction offer,
Godot's Inspector node picker serializes the path from the node that owns the resource property. The
runtime must resolve that path from the same authored context; resolving it from a different parent adds
or removes one `..` and produces `Interaction is not configured.` even though the user selected a valid
node in the editor.

For `StatefulStateInteractionRule`, `StatefulPath` is resolved from the generic invocation target when
one is supplied, then the gameplay host or action. Target and offer rules therefore normally use the
Interactive's target-relative path, while executor references keep their own executor-node context.
