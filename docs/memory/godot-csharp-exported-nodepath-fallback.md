# Godot C# exported NodePath fallback

- In this Godot 4.7.1 Mono project, exported `NodePath` auto-properties on C# nodes can arrive empty when a scene is loaded or spawned, even when the `.tscn` contains the property.
- Runtime components must keep a semantic fallback instead of relying only on the exported path. For interaction, sibling names cover simple scenes and a recursive typed lookup covers a nested `Camera3D` or `InteractionInteractor` on the Character.
- Headless validation should inspect the scene log after loading both the standalone Character and the network-spawned `test_world` Character; a code-0 exit alone is not sufficient.
