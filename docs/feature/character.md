# Character

## Purpose

Reusable Godot 4.7 C# `CharacterBody3D` for gameplay prototyping. The scene combines physical movement, camera-relative input, FPS/TPS camera behavior, jump/fall physics and AnimationTree locomotion without gameplay-system dependencies.

## Files

- `quest_world/character/Character.tscn` — reusable character scene.
- `quest_world/character/ual_character.tscn` — UAL mannequin scene.
- `quest_world/character/scripts/Character.cs` — controller and exported configuration.
- `quest_world/character/scripts/CharacterLookPitchModifier.cs` — additive upper-body look-pitch modifier.

## Controls

- `WASD`: camera-relative movement.
- `Space`: jump.
- `Shift`: sprint.
- Mouse: camera yaw/pitch while captured.
- `Escape`: release the mouse.
- Left click: recapture the mouse.

Action names are exported on `Character.cs` and default to the project actions in `project.godot`.

## Orientation

- First-person: the `SpringArm3D` pivot is offset slightly forward along its local `-Z` so the imported head does not intersect the view; the visual body follows camera yaw.
- Third-person: forward movement aligned with camera forward turns the visual body toward movement; lateral, backward and diagonal movement keeps the body camera-facing and uses backward/strafe blend directions.

## Camera effects

The camera hierarchy is `SpringArm3D → CameraAnchor → CameraEffects → Camera3D`. The spring-arm owns distance and collision placement; `CameraEffects` owns cosmetic offsets so Godot does not overwrite them.

- Head bob follows real horizontal speed, with stronger amplitude during sprint and reduced amplitude in third-person.
- Sprint smoothly increases FOV from `75` to `82` degrees.
- Mouse sway adds a small damped roll, capped at `1` degree.
- Jump and landing add softly amortized positional/pitch impulses.
- Crouch is intentionally not implemented.

## AnimationTree

The state machine starts in `Locomotion` at zero blend (`Idle`) and travels through `Jump` and `Fall` based on physical state. `Jump_Land` is a `OneShot` overlay on top of the base locomotion state: it fades in for `0.05` seconds at landing, waits `LandingBlendOutDelay` (`0.15` seconds by default), then fades out over `0.25` seconds while locomotion continues underneath. The whole tree is driven through an `AnimationNodeTimeScale`, with `AnimationPlaybackSpeed` defaulting to `1.5`; the imported `AnimationPlayer` remains the animation library. The imported UAL animation names are `Idle`, `Jog_Fwd`, `Jog_Bwd`, `Jog_Left`, `Jog_Right`, `Sprint`, `Jump_Start`, `Jump`, `Jump_Land`, `Turn90_L` and `Turn90_R`.

In first-person, turn-in-place is only triggered while grounded and essentially idle. Camera yaw is accumulated until it reaches `20` degrees; the accumulator survives the short braking window up to `0.5 m/s`, then the matching 90-degree turn clip plays while normal visual orientation smoothing continues. While a turn is active, another `25` degrees of camera yaw is queued and starts immediately when the current clip finishes, avoiding an animation cut during fast rotations. The turn playback speed ramps from the normal `AnimationPlaybackSpeed` (`1.5`) up to `TurnAnimationMaxPlaybackSpeed` (`2.5`) as pending yaw grows, so fast rotations do not wait as long for the fixed-length clip; locomotion returns to `1.5` outside the turn. Movement cancels it without a forced final snap. Sprint is available only while grounded and stops immediately after takeoff.

### Look pitch

The upper body follows the camera's vertical look direction through a post-animation `SkeletonModifier3D` layer. This keeps the locomotion, jump, landing and turn animations as the base pose, then adds the look response on top of the current skeleton pose.

The data flow is:

- Read the camera forward vector from `CameraYaw/CameraPitch`.
- Convert its vertical component to a symmetric camera pitch angle. This avoids relying on Euler-angle decomposition for the sign of the look direction.
- Clamp the angle in camera space: `32` degrees up and `18` degrees down.
- Apply the UAL rig sign correction after clamping. The `UALCharacter` visual is rotated `180` degrees around Y, so its local X pitch axis is opposite to the camera pitch axis.
- Smooth the result with `SmoothingSpeed = 14`.
- Distribute the final additive pitch across the upper-body chain:

  - `spine_01`: `10%`
  - `spine_02`: `20%`
  - `spine_03`: `25%`
  - `neck_01`: `20%`
  - `Head`: `25%`

The modifier is fully active in first-person and uses `ThirdPersonInfluence = 45%` in third-person, keeping the full-body silhouette from over-bending at a distance. Bone lookup supports the imported UAL names and common case/format aliases. The effect is visual only: it does not rotate the character collider or move the camera.

The modifier runs after the `AnimationTree`/`AnimationMixer`, so each frame starts from the active animation pose and receives one additive look-pitch pass. The implementation uses Godot 4.7's `_ProcessModificationWithDelta(double)` callback and lets `SkeletonModifier3D` handle its own influence blending.


## Validation

Validated with:

- `dotnet build quest-world.sln` — success, 0 warnings, 0 errors.
- Final camera feel is validated manually by playing the test world in FPS and TPS.

## Audit status (2026-08-14)

The checked-in scenes load successfully in Godot 4.7.1 Mono and the C# solution builds with 0 warnings and 0 errors. Headless startup checks pass for `Character.tscn` and `test_world.tscn`. This validates compilation, scene loading and the current AnimationTree contract, but it does not replace gameplay-transition tests.

The audit found no critical loading or data-safety issue. The following behavioral and reuse issues remain open:

- A character instantiated directly on the test-world floor triggers the landing camera impulse on the first physics contact and activates `LandOneShot` on the following frame. Initial floor sampling must not be treated as a gameplay landing.
- Landing intensity is constant. Measured contacts around `-1.80 m/s` and `-9.80 m/s` produced the same first camera offset and the same 19-frame one-shot duration. Landing needs minimum airborne/impact thresholds and an impact-derived intensity.
- Turn-in-place has lower priority than it should: an active turn is canceled by movement input, but not immediately by takeoff, losing the floor, disabling the feature or switching view mode. While the override remains active it masks jump/fall animation selection.
- Visual orientation mixes world-space target yaw with `Visual.Rotation.Y`, which is local. A character root instantiated at 90 degrees was measured with camera yaw at 90 degrees and visual global yaw near 180 degrees.
- Physical sprint accepts every movement direction, while the blend space contains only a forward sprint point; lateral/backward sprint therefore has no matching run animation.
- `Character.cs` and `CharacterLookPitchModifier.cs` depend on the exact UAL hierarchy. Turn and landing clips are required even when their features are disabled, and missing look-pitch bones can fail silently because only the bone-index array length is checked.
- Every instance captures global mouse input and owns a current camera. The controller needs an explicit possessed/input-enabled boundary before NPCs, dialogue UI or multiple character instances are introduced.
- Runtime view mode can be mutated directly through `CurrentViewMode` without applying the camera rig; consumers must currently call `SetViewMode()` for a consistent change.
- There is no automated behavioral coverage for spawn contact, rotated instances, jump-during-turn, landing severity, view switching, missing rig data or directional blend output.

Recommended decomposition keeps deterministic orchestration on the root rather than giving every child an independent physics loop:

```text
CharacterInputFrame
        |
        v
Character.cs (motor + MoveAndSlide)
        |
        v
CharacterFrameState / events
        |--------------------|
        v                    v
CharacterAnimation      CharacterCameraRig
                             |
                             v
                    CharacterCameraEffects
```

- Keep `Character.cs` as the `CharacterBody3D` facade, movement authority and ordered frame orchestrator.
- Move AnimationTree paths, validation, locomotion blend, airborne priority, landing one-shot and turn timing into `CharacterAnimation`.
- Move mouse look, view mode, spring-arm configuration and camera ownership into `CharacterCameraRig`.
- Move bob, sway, FOV and jump/landing impulses into `CharacterCameraEffects`, updated from a post-move snapshot.
- Keep `CharacterLookPitchModifier` separate, but bind it through typed/exported references and validate resolved bones explicitly.
- Extract an input-source abstraction only when possession, AI, replay or tests require it.
