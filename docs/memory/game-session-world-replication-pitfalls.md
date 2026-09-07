# Game session world replication pitfalls

- In a Godot `.tscn`, only actual `NodePath` properties belong in `node_paths`. Putting a `PackedScene`
  resource such as `PlayerStateScene` in that array silently leaves the exported resource unset at
  runtime.
- A world-local `MultiplayerSpawner` may already be disposed by the time a persistent integration node
  processes the replacement. Guard signal detachment with `GodotObject.IsInstanceValid` before touching
  the old spawner.
- World-local integration indexes must be cleared when the current-world spawner changes. Keeping the
  old peer-to-Character map prevents the new incarnation from spawning after travel.
- The persistent game root owns `SceneTree.CurrentScene`; world scenes must resolve project services by
  current-world ancestry or explicit injected context rather than casting `CurrentScene` to `IWorldSpawner`.
- Loading the persistent `Game.tscn` from a shared GdUnit process contaminates the existing Character/
  presenter runtime suite even after freeing the scene. Validate that composition with a standalone
  headless Godot smoke instead of adding the root scene to the shared runner.
