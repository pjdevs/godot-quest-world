# Godot headless physics sampling order

- Awaiting `SceneTree.physics_frame` from a diagnostic script does not guarantee that every node's `_PhysicsProcess()` has already completed when the script resumes.
- To inspect post-physics state reliably in a headless audit, await both `physics_frame` and the following `process_frame`, or sample through a node whose process priority is explicitly later.
- An `AnimationNodeOneShot` request and its `parameters/<node>/active` flag can become observable on different frames. Inspect the triggering side effect as well; for the Character spawn landing, `CameraEffects.position.y` changed on frame 1 while `LandOneShot/active` became true on frame 2.
- Validate spawn behavior against the real level scene, not only a synthetic floor, because scene registration and first-contact timing matter.
