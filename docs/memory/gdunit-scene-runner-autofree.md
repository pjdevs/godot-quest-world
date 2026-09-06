# GdUnit scene runners must opt into scene cleanup

- `ISceneRunner.Load` defaults `autoFree` to `false` in GdUnit4Net 5.x.
- `Dispose()` removes the test scene from the tree, but only frees its node hierarchy when
  `autoFree` is `true`.
- Synthetic test worlds containing physics bodies must therefore call
  `ISceneRunner.Load(root, autoFree: true)`; otherwise Jolt bodies, shapes and child nodes remain
  alive until Godot exits and appear as orphan/leaked objects.
- If a test root is already registered with `AutoFree`, use one owner only: remove that wrapper and
  let the scene runner own the root when the runner is configured with `autoFree: true`.
