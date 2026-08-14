# Camera effects belong after SpringArm3D placement

The spring-arm owns the transform of its direct child. Cosmetic first-person and third-person effects must therefore be applied below a spring-arm-owned anchor, not directly to the camera child.

Current hierarchy:

```text
SpringArm3D
└─ CameraAnchor       # positioned by the spring-arm
   └─ CameraEffects   # bob, sway and impulses
      └─ Camera3D
```

This keeps collision/distance handling separate from camera feel and prevents head bob or landing effects from being overwritten by `SpringArm3D`.
