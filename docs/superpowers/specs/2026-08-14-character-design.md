# Character Godot 4.7 Design

## Goal

Build a drop-in gameplay character template for Godot 4.7 C# that behaves like a minimal Unreal third-person template while supporting first-person view through the same scene and controller. The character is intended for gameplay prototyping only: movement, camera, locomotion animation, jumping and falling.

## Constraints

- Godot 4.7 with the .NET/C# build.
- Root node is `CharacterBody3D`.
- Animations are in-place; the controller never consumes root motion.
- `AnimationTree` owns animation playback and blending.
- C# owns physical movement, camera input and the real movement state.
- The controller contains no interaction, dialogue, quest, combat or gameplay-system dependencies.
- No external runtime dependency is added.
- The controller stays compact and disposable.
- Project input defaults use WASD physical key codes.
- No diagonal animation slots are required in the first version.

## Feature layout

```text
quest_world/character/
├── Character.tscn
├── ual_character.tscn
└── scripts/
    └── Character.cs
```

`ual_character.tscn` remains a separate mannequin asset scene and is instanced by `Character.tscn`.

## Scene architecture

```text
Character : CharacterBody3D
├── CollisionShape3D
├── Visual : Node3D
│   └── UALCharacter
├── AnimationTree
└── CameraYaw : Node3D
    └── CameraPitch : Node3D
        └── SpringArm3D
            └── CameraAnchor : Node3D
                └── CameraEffects : Node3D
                    └── Camera3D
```

Responsibilities:

- `Character` owns collision, velocity, gravity, jump, input and physical state.
- `Visual` is the mannequin container and is the only node whose yaw is changed for locomotion; the `Character` root keeps its physical frame.
- `AnimationTree` drives all locomotion/jump/fall animation playback.
- `CameraYaw` rotates horizontally from mouse input and remains independent of `Visual`.
- `CameraPitch` rotates vertically and clamps between approximately `-70` and `+70` degrees.
- `SpringArm3D` provides third-person camera collision handling.
- `CameraAnchor` is the spring-arm-owned endpoint and remains free of cosmetic animation.
- `CameraEffects` applies head bob, mouse sway and jump/landing impulses after spring-arm placement.
- The camera pivot is placed near eye height so both view modes use the same orbit origin.

Orientation policy:

- In `FirstPerson`, `Visual` follows `CameraYaw` continuously so the body turns with the camera. Movement is evaluated in body-local space and naturally selects forward, backward and strafe animations.
- In `ThirdPerson`, forward movement that is sufficiently aligned with the camera forward direction turns `Visual` toward the movement direction. Backward, lateral and diagonal movement keeps `Visual` facing the camera and uses the corresponding backward/strafe blend-space directions. The alignment threshold is exported as `ThirdPersonForwardAlignmentThreshold` and defaults to `0.75`.

The scene uses fixed internal node paths for the rig. Tunable gameplay and camera values are exported in C#; consumers do not need to wire node references after instancing the scene.

## View modes

The controller exposes one view mode enum:

```csharp
public enum ViewMode
{
    ThirdPerson,
    FirstPerson
}
```

The selected mode is exported for Inspector configuration and applied during `_Ready()`. Runtime consumers can call:

```csharp
public void SetViewMode(ViewMode mode)
```

Third-person mode uses an exported `ThirdPersonDistance` through `SpringArm3D.spring_length` and defaults to `4.0`. First-person mode sets the spring length to zero and applies the exported `FirstPersonCameraOffset` to `SpringArm3D.position`; the default offset is `Vector3(0, 0, -0.2)`, placing the camera pivot slightly forward of the imported head to prevent head intersection. The camera child remains at zero local position because `SpringArm3D` owns its child placement. Third-person mode restores the spring-arm and camera positions to zero. The full mannequin remains visible in both modes. The camera never becomes a child of `Visual`.

Mouse behavior:

- Capture the mouse on startup.
- Horizontal `InputEventMouseMotion` rotates `CameraYaw`.
- Vertical mouse motion rotates `CameraPitch`.
- Clamp pitch between the exported minimum and maximum values.
- `Esc` switches to visible mouse mode.
- A click recaptures the mouse.
- Mouse motion is ignored while the mouse is visible.

Camera effects:

- Head bob follows real horizontal speed while grounded, with stronger amplitude during sprint and a `0.35` scale in third-person.
- Sprint interpolates the camera FOV from `75` to `82` degrees.
- Mouse motion produces a small damped roll, capped at `1` degree in first-person.
- Jump and landing produce softly amortized positional and pitch impulses.
- Effects are applied to `CameraEffects`, after `SpringArm3D` has placed `CameraAnchor`, so the spring-arm cannot overwrite them.
- Crouch is explicitly out of scope.

## InputMap

The following project actions are added to `project.godot`:

```text
move_forward    W
move_backward   S
move_left       A
move_right      D
jump            Space
sprint          Shift
```

The project InputMap remains the source of truth for key bindings. The six action names are exported string properties on `Character.cs`, defaulting to the names above, so a consumer can point the scene at differently named project actions. `Character` validates the configured action names at startup so a bad project configuration produces an explicit error.

The exported action properties are `MoveForwardAction`, `MoveBackwardAction`, `MoveLeftAction`, `MoveRightAction`, `JumpAction` and `SprintAction`.

## Movement behavior

The physics flow is:

```text
Input.GetVector()
    ↓
direction relative to CameraYaw yaw
    ↓
target horizontal velocity
    ↓
MoveToward() acceleration
    ↓
gravity / jump
    ↓
Velocity
    ↓
MoveAndSlide()
    ↓
orientation and animation parameters
```

Details:

- `Input.GetVector(MoveLeftAction, MoveRightAction, MoveForwardAction, MoveBackwardAction)` provides the 2D input.
- The input is transformed using `CameraYaw.GlobalBasis`.
- Vertical direction is removed before normalization.
- Holding `sprint` selects `RunSpeed`; otherwise `WalkSpeed` is used.
- Horizontal velocity converges with `MoveToward()` rather than snapping.
- Ground movement uses `Acceleration`.
- Air movement uses `AirAcceleration`.
- Gravity comes from Godot's `GetGravity()` settings.
- Jump sets `Velocity.Y` to `JumpVelocity` only when `IsOnFloor()` and `jump` was just pressed.
- `Visual` rotates smoothly according to the FPS/TPS orientation policy above.
- No coyote time, jump buffering, crouch, dash, combat, aim or dedicated strafe mode is implemented.

The main exported movement parameters are:

```text
WalkSpeed       3.0
RunSpeed        6.0
Acceleration    15.0
AirAcceleration 5.0
JumpVelocity    5.0
RotationSpeed   10.0 smooth-turn speed
```

The main exported camera parameters are:

```text
ViewMode
MouseSensitivity 0.002
PitchMin         -70 degrees
PitchMax         +70 degrees
ThirdPersonDistance 4.0
FirstPersonCameraOffset Vector3(0, 0, -0.2)
ThirdPersonForwardAlignmentThreshold 0.75
```

The main exported camera-effect parameters are `CameraEffectsEnabled`, `HeadBobEnabled`, `HeadBobWalkAmplitude`, `HeadBobSprintAmplitude`, `HeadBobFrequency`, `ThirdPersonCameraEffectsScale`, `CameraSwayStrengthDegrees`, `CameraSwaySmoothSpeed`, `DefaultFov`, `SprintFov`, `FovTransitionSpeed`, `JumpCameraOffset`, `LandingCameraOffset`, `JumpCameraPitchDegrees`, `LandingCameraPitchDegrees`, `CameraImpulseResponseSpeed` and `CameraImpulseRecoverySpeed`.

## AnimationTree contract

The `AnimationTree` root is an `AnimationNodeStateMachine` with three states:

```text
Locomotion ─────► Jump
     ▲              │
     │              ▼
     └──────────── Fall
```

Transitions use short crossfades of approximately `0.15` seconds. C# calls `Travel()` only when the requested state differs from the previously requested state.

The state machine starts in `Locomotion`. The runtime parameter paths are `parameters/playback` for the state-machine playback object and `parameters/Locomotion/blend_position` for the locomotion blend position. The zero blend position selects `Idle`.

`Locomotion` is an `AnimationNodeBlendSpace2D` with cardinal direction slots. The first version does not require diagonal slots. The current UAL asset contract is:

```text
Idle
Jog_Fwd
Jog_Bwd
Jog_Left
Jog_Right
Sprint
Jump_Start
Jump
```

`Jump_Start` is used by the `Jump` state and `Jump` by the `Fall` state. The animation names are configured on the AnimationTree nodes in `Character.tscn`, not embedded in the movement algorithm. This keeps the controller independent from a particular animation pack. Godot's GLB importer removes the `_Loop` suffix from these imported animation names. A missing animation is reported as an asset/configuration error; the controller does not silently substitute another animation. Additional diagonal UAL clips exist but are intentionally not wired in the first blend-space version.

The blend position is derived from character-local horizontal velocity:

```text
blend.x = local_velocity.x / RunSpeed
blend.y = -local_velocity.z / RunSpeed
```

The blend vector is clamped to a magnitude of `1`. Idle is represented by a zero vector, walking is approximately half the run radius, and running reaches the outer radius.

The physical state drives visual state selection:

```text
not on floor and Velocity.Y > 0  → Jump
not on floor and Velocity.Y <= 0 → Fall
on floor                        → Locomotion
```

`Jump_Land` may exist in the asset but is outside the first version because a dedicated land state is not part of the MVP.

## C# responsibilities

`Character.cs` owns:

- Node references resolved from the fixed scene tree.
- Exported tuning parameters.
- Input validation and mouse mode handling.
- Mouse yaw/pitch updates.
- Camera mode application through `SetViewMode()`.
- Camera-relative movement direction.
- Ground/air acceleration.
- Gravity and jump.
- Smooth visual orientation according to the FPS/TPS policy.
- Cosmetic camera effects after spring-arm placement.
- Blend-space parameter updates.
- Explicit AnimationTree state travel with duplicate travel calls avoided.

`Character.cs` does not own animation playback calls such as `AnimationPlayer.Play()` and does not implement interaction, dialogue, quest or combat logic.

## Error handling and configuration contract

- Missing expected child nodes produce an explicit Godot scene error.
- Missing InputMap actions produce an explicit startup error naming the missing action.
- Missing animation names remain visible as AnimationTree/asset configuration errors.
- No silent fallback changes the visual behavior when assets are incomplete.
- The project-level InputMap remains the source of truth for key rebinding.

## Verification strategy

This prototype has no external test framework. Verification is performed by compiling the project and manually playing the test world. The manual checklist covers:

- `Character.tscn` instantiates with the expected node tree.
- Required InputMap actions exist with WASD defaults.
- `SetViewMode(FirstPerson)` sets the spring arm to zero and `ThirdPerson` restores the configured distance.
- Camera-relative input, FPS body rotation, TPS forward rotation and TPS strafe behavior feel correct.
- FPS/TPS head bob, sprint FOV, mouse sway and jump/landing impulses feel subtle and responsive.
- Acceleration approaches the target speed without snapping and sprint changes the target speed.
- Jump only starts from the floor and gravity produces a falling state.
- AnimationTree starts on `Locomotion` with the blend-space at `Idle` and travels through `Jump`/`Fall` correctly.
- `dotnet build quest-world.sln` succeeds after each code task.

## Definition of Done

- `Character.tscn` can be dropped into a scene with a floor.
- WASD moves the character relative to the camera.
- Mouse controls the camera in both view modes.
- Third-person spring-arm collision prevents gross wall clipping.
- First-person keeps the full body visible.
- Walk/run, acceleration and deceleration work.
- Jump, gravity and falling work.
- Locomotion, jump and fall are controlled by `AnimationTree`.
- The character visual rotates smoothly according to the FPS/TPS orientation policy.
- No root motion is consumed.
- Main parameters are exposed in the Inspector.
- No interaction, quest, dialogue or other out-of-scope gameplay code is added.

## Out of scope

```text
coyote time
jump buffering
land state
foot IK
stairs custom
root motion
motion matching
aiming
dedicated strafe mode
crouch
dash
combat
camera shoulder swap
animation events
complex gameplay FSM
diagonal animation slots
```

## Project corrections captured by this spec

- The project convention is `Character`, not `DevCharacter`.
- The default keyboard uses WASD physical key codes.
- The UAL asset contains the locomotion, strafe, sprint and jump clips used by the current Character contract; the AnimationTree references the concrete UAL names listed above.
- Diagonal animation slots are intentionally not required.
