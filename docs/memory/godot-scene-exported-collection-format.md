# Godot scene format for exported collections

## Node reference arrays

A C# `[Export] Godot.Collections.Array<TNode>` is stored in `.tscn` as a plain array of
`NodePath`, and the property name must also appear in the node's `node_paths` list:

```text
[node name="Interactive" type="Node" parent="." node_paths=PackedStringArray("InteractionArea", "Actions")]
Actions = [NodePath("ActivateAction")]
```

Do **not** hand-write `Array[ExtResource("<script>")]([NodePath("...")])` for node arrays: the scene
parses without error but the property stays empty at runtime, so the component silently loses its
references. That typed-array syntax is only how Godot serializes **Resource** arrays:

```text
Rules = Array[ExtResource("5_ll77h")]([SubResource("Resource_ll77h")])
```

## How to confirm a serialization instead of guessing

Build the node graph from a throwaway `SceneTree` script, pack it, save it, and read the result:

```gdscript
extends SceneTree

func _initialize() -> void:
    var root := Node.new()
    # build nodes, set properties through set()/get() to avoid GDScript parse-time typing on C# exports
    var packed := PackedScene.new()
    packed.pack(root)
    ResourceSaver.save(packed, "res://probe.tscn")
    quit()
```

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . -s probe.gd
```

Godot only writes non-default values, so the probe also shows which properties a scene may omit.
Delete the probe script and scene afterwards.

## Renaming or adding exported properties

- A renamed export (for example `InteractionRules` to `TargetRules`) is dropped silently from every
  scene that still uses the old name. Update the `.tscn` files in the same change.
- New scripts have no `.cs.uid` file until Godot imports them. Run
  `/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --editor --quit --log-file .godot/headless.log`
  once, then reference the generated uid in hand-written `ext_resource` entries.
- A scene-level assertion in a test (`Actions.Count`, `Definition.Id`) is the cheapest guard against a
  silently unbound export.
