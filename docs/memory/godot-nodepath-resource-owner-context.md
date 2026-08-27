# Godot NodePath picker context for nested Resource properties

When a `NodePath` is exported by a `Resource` stored inside an `InteractionAction`, Godot's Inspector
node picker serializes the path from the action node that owns the resource property. The runtime must
resolve that path from the same action node; resolving it from the parent `InteractiveComponent` adds
or removes one `..` and produces `Interaction is not configured.` even though the user selected a valid
node in the editor.

For `StatefulStateInteractionRule`, `StatefulPath` is therefore relative to the owning
`InteractionAction`. Target-level rules remain relative to the `InteractiveComponent`, and executor
references keep their own executor-node context.
