# Godot headless physics sampling order

- Awaiting `SceneTree.physics_frame` from a diagnostic script does not guarantee that every node's `_PhysicsProcess()` has already completed when the script resumes.
- To inspect post-physics state reliably in a headless audit, await both `physics_frame` and the following `process_frame`, or sample through a node whose process priority is explicitly later.
- A one-tick event such as `CharacterFrameState.Landed` can still be missed when the test samples on
  render/process frames: more than one physics tick may occur between observations. For a landing
  presentation test, observe the resulting camera impulse over its recovery window and assert on its
  peak instead of making the assertion depend on seeing that transient flag.
- An `AnimationNodeOneShot` request and its `parameters/<node>/active` flag can become observable on different frames. Inspect the triggering side effect as well; for the Character spawn landing, `CameraEffects.position.y` changed on frame 1 while `LandOneShot/active` became true on frame 2.
- Validate spawn behavior against the real level scene, not only a synthetic floor, because scene registration and first-contact timing matter.
- An `Area3D` overlap can also land after a node already ran its `_Process` for that same frame, so a
  test that asserts a per-frame consequence of an overlap (interaction focus, for instance) must drive
  that pipeline explicitly instead of simulating more frames and hoping. Simulating N frames made the
  assertion pass in isolation and fail in the full suite.
