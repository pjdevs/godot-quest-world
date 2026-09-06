# Detached Godot test nodes must use `Free`

`Node.QueueFree()` schedules destruction through the scene tree. A node that was never added to the
tree, or that was removed from its parent before cleanup, therefore remains alive and is reported by
Godot/GdUnit as an orphan.

For test fixtures that are deliberately detached, use `Free()` in a `finally` block or register the
node with GdUnit's `AutoFree`. For a fixture with several nodes, make one root own the complete node
hierarchy and register/free that root once. Keep `QueueFree()` for nodes that are still in the scene
tree and should be destroyed at the end of the current frame.
