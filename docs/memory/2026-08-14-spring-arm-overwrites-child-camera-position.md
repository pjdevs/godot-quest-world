# SpringArm3D owns the child camera position

## Pitfall

Applying the FPS offset directly to `Camera3D.position` has no visible effect when the camera is a child of `SpringArm3D`: the spring-arm recomputes its child placement from the arm endpoint and overwrites the child position.

## Rule

For a zero-length FPS spring-arm, apply `FirstPersonCameraOffset` to `SpringArm3D.position` and keep the child `Camera3D.position` at zero. Restore both positions when returning to third-person mode.
