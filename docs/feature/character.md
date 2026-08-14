# Character

## Purpose

Reusable Godot 4.7 C# `CharacterBody3D` for gameplay prototyping. The scene combines physical movement, camera-relative input, FPS/TPS camera behavior, jump/fall physics and AnimationTree locomotion without gameplay-system dependencies.

## Files

- `quest_world/character/Character.tscn` — reusable character scene.
- `quest_world/character/ual_character.tscn` — UAL mannequin scene.
- `quest_world/character/scripts/Character.cs` — controller and exported configuration.

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

## Validation

Validated with:

- `dotnet build quest-world.sln` — success, 0 warnings, 0 errors.
- Final camera feel is validated manually by playing the test world in FPS and TPS.
