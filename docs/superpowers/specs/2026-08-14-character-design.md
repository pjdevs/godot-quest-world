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
- Project input defaults use ZQSD for AZERTY keyboards.
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
            └── Camera3D
```

Responsibilities:

- `Character` owns collision, velocity, gravity, jump, input and physical state.
- `Visual` is the mannequin container and rotates toward the movement direction.
- `AnimationTree` drives all locomotion/jump/fall animation playback.
- `CameraYaw` rotates horizontally from mouse input and remains independent of `Visual`.
- `CameraPitch` rotates vertically and clamps between approximately `-70` and `+70` degrees.
- `SpringArm3D` provides third-person camera collision handling.
- The camera pivot is placed near eye height so both view modes use the same orbit origin.

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

Third-person mode uses an exported `ThirdPersonDistance` through `SpringArm3D.spring_length`. First-person mode sets the spring length to zero and uses an exported `FirstPersonCameraOffset` for eye placement. The full mannequin remains visible in both modes. The camera never becomes a child of `Visual`.

Mouse behavior:

- Capture the mouse on startup.
- Horizontal `InputEventMouseMotion` rotates `CameraYaw`.
- Vertical mouse motion rotates `CameraPitch`.
- Clamp pitch between the exported minimum and maximum values.
- `Esc` switches to visible mouse mode.
- A click recaptures the mouse.
- Mouse motion is ignored while the mouse is visible.

## InputMap

The following project actions are added to `project.godot`:

```text
move_forward    Z
move_backward   S
move_left       Q
move_right      D
jump            Space
sprint          Shift
```

The action names are configurable in the project InputMap. The scene uses these action names by default and validates that they exist at startup so a bad project configuration produces an explicit error.

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

- `Input.GetVector("move_left", "move_right", "move_forward", "move_backward")` provides the 2D input.
- The input is transformed using `CameraYaw.GlobalBasis`.
- Vertical direction is removed before normalization.
- Holding `sprint` selects `RunSpeed`; otherwise `WalkSpeed` is used.
- Horizontal velocity converges with `MoveToward()` rather than snapping.
- Ground movement uses `Acceleration`.
- Air movement uses `AirAcceleration`.
- Gravity comes from Godot's `GetGravity()` settings.
- Jump sets `Velocity.Y` to `JumpVelocity` only when `IsOnFloor()` and `jump` was just pressed.
- The character rotates smoothly toward the horizontal movement direction.
- No coyote time, jump buffering, crouch, dash, combat, aim or strafe mode is implemented.

The main exported movement parameters are:

```text
WalkSpeed       3.0
RunSpeed        6.0
Acceleration    15.0
AirAcceleration 5.0
JumpVelocity    5.0
RotationSpeed   configurable smooth-turn speed
```

The main exported camera parameters are:

```text
ViewMode
MouseSensitivity 0.002
PitchMin         -70 degrees
PitchMax         +70 degrees
ThirdPersonDistance
FirstPersonCameraOffset
```

## AnimationTree contract

The `AnimationTree` root is an `AnimationNodeStateMachine` with three states:

```text
Locomotion ─────► Jump
     ▲              │
     │              ▼
     └──────────── Fall
```

Transitions use short crossfades of approximately `0.15` seconds. C# calls `Travel()` only when the requested state differs from the previously requested state.

`Locomotion` is an `AnimationNodeBlendSpace2D` with cardinal direction slots. The first version does not require diagonal slots. The target asset contract is:

```text
Idle
Walk_Fwd
Walk_Back
Walk_Left
Walk_Right
Run_Fwd
Run_Back
Run_Left
Run_Right
Jump_Start
Fall_Loop
```

The animation names are configured on the AnimationTree nodes in `Character.tscn`, not embedded in the movement algorithm. This keeps the controller independent from a particular animation pack. A missing animation is reported as an asset/configuration error; the controller does not silently substitute another animation.

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
- Smooth visual orientation.
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

Implementation follows red-green-refactor cycles. Tests or verification harnesses are written and run before the production behavior they cover.

The project has no external test framework requirement. A small Godot C# headless harness can validate scene and runtime behavior without adding a package dependency. Coverage includes:

- `Character.tscn` instantiates with the expected node tree.
- Required InputMap actions exist with ZQSD defaults.
- `SetViewMode(FirstPerson)` sets the spring arm to zero and `ThirdPerson` restores the configured distance.
- Camera-relative input produces the expected horizontal direction.
- Acceleration approaches the target speed without snapping.
- Sprint changes the target speed.
- Jump only starts from the floor and gravity produces a falling state.
- AnimationTree travel follows Locomotion/Jump/Fall state rules.
- `dotnet build quest-world.sln` succeeds after each code task.
- Godot headless scene validation succeeds with:

```bash
/Users/pjmorel/Documents/Godot_mono.app/Contents/MacOS/Godot \
  --headless \
  --path /Users/pjmorel/Projects/quest-world \
  --editor \
  --quit
```

## Definition of Done

- `Character.tscn` can be dropped into a scene with a floor.
- ZQSD moves the character relative to the camera.
- Mouse controls the camera in both view modes.
- Third-person spring-arm collision prevents gross wall clipping.
- First-person keeps the full body visible.
- Walk/run, acceleration and deceleration work.
- Jump, gravity and falling work.
- Locomotion, jump and fall are controlled by `AnimationTree`.
- The character rotates smoothly toward movement.
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
strafe mode
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
- The default keyboard is ZQSD, not WASD.
- The current UAL asset does not yet contain the full cardinal walk/run animation set. The target Character contract still assumes those cardinal animations will be supplied later; the controller must not be redesigned around the temporary asset limitation.
- Diagonal animation slots are intentionally not required.
