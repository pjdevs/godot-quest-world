# Character

## Purpose

The reusable Godot 4.7 C# `CharacterBody3D` lives in `addons/dummy_character_plugin` under
`QuestWorld.Character`. It provides camera-relative movement, FPS/TPS views, directional locomotion,
sprint, jump/fall/landing, turn-in-place, upper-body pitch and procedural camera effects.

The game keeps a project `Character` subclass in `quest_world/character/Character.cs`. That subclass
composes Inventory, Gameplay Action and Interaction around the generic character instead of making the
addon depend on those systems.

## Architecture

The reusable character is an orchestration facade around focused components:

```text
CharacterPlayerController
        ↓
CharacterInputFrame              # one raw local input sample
        ↓
CharacterSimulationInput         # camera-independent simulation command
        ↓
Character / Simulate             # composition/orchestration boundary
        ↓
CharacterMovement / Simulate     # movement motor + MoveAndSlide
        ↓
CharacterFrameState              # immutable post-move snapshot
        ├──────────────→ CharacterAnimationController
        └──────────────→ CharacterCameraEffects
```

`CharacterCameraRig` owns camera configuration and view transforms. `CharacterLookPitchModifier` remains
a post-animation skeleton modifier.

Movement configuration is serialized once on the Character root. The motor receives a plain
`CharacterMovementSettings` snapshot; it does not duplicate exported configuration in another Resource
or component.

The project subclass composes:

- `InventoryComponent` + `InventoryReplicationSynchronizer`;
- `GameplayActionComponent` + `GameplayActionRunner`;
- `InteractionInteractor` + detector + `InteractionPresenter`;
- `GameplayActionPresenter` for owned actions such as `Drop Battery`.

The runner is the single gameplay-action input boundary. The project Character samples
`GetRelevantInputs()` and forwards press/release to the runner. Before a press it refreshes Interaction's
focused bindings, but owned actions remain usable when no interactive target is focused.

## Possession and controls

`CharacterPlayerController` samples movement input and submits one frame to its possessed character
before character physics. Possession explicitly transfers input authority and active camera; losing
possession clears incompatible transient state.

Current project controls include WASD movement, Space jump, Shift sprint, mouse look and any Input Map
actions returned by `GameplayActionRunner.GetRelevantInputs()`.

## Movement and orientation

- First-person visual yaw follows the camera rig in character-local space.
- Third-person forward movement faces local movement direction; lateral/backward movement remains
  camera-facing for the directional blend space.
- World directions are converted through the character root basis before assigning visual yaw, so
  rotated scene instances remain correct.
- Sprint is currently forward/forward-diagonal because the AnimationTree has a forward sprint clip.
- Assigning `CurrentViewMode` follows the same transition path as `SetViewMode`, resetting incompatible
  camera/turn state.

## Landing and animation

Initial floor contact establishes state and is not a gameplay landing. A real landing requires minimum
air time and impact speed. Landing strength is derived from downward velocity sampled immediately before
`MoveAndSlide`; animation and camera effects consume the same resulting frame event.

Animation priority is:

```text
airborne > turn-in-place > locomotion
```

Jumping or losing the floor cancels a turn override immediately. View-mode changes, movement and loss of
possession likewise clear incompatible turn state.

## Look pitch

`CharacterLookPitchModifier` distributes clamped additive pitch across the configured spine/neck/head
chain, including the UAL visual-axis correction. Missing bones are reported explicitly; an entirely
missing chain is treated as unusable.

## Architecture decisions

### AD-01 — The root is an orchestrator, not a movement monolith

The Character keeps the stable Godot-facing `Simulate` facade while movement, input, animation, camera
and skeleton responsibilities live in focused units. Extracting responsibilities does not mean inventing
an abstraction for every line; each component owns a coherent reason to change.

### AD-02 — Input is sampled once, simulation consumes explicit data

Raw local input becomes a `CharacterInputFrame`, then a camera-independent simulation command. The motor
and presentation do not re-read global input, which keeps direct simulation and tests deterministic.

### AD-03 — Movement configuration has one serialized source of truth

Inspector-facing movement settings stay on the Character root. `CharacterMovementSettings` is only the
plain snapshot passed to the motor, preventing two authoring surfaces from drifting while keeping the
motor independently testable.

### AD-04 — Post-move state is a shared immutable snapshot

Animation and camera effects consume the same `CharacterFrameState` produced by movement. They do not
independently infer floor transitions, landing impacts or movement state from Godot globals.

### AD-05 — The camera rig owns camera configuration

View offsets, distance, sensitivity and pitch limits belong to `CharacterCameraRig`. The Character owns
which view mode is active and orchestrates the rig without duplicating its serialized settings.

### AD-06 — Possession owns input authority

A Character can exist and simulate without being controlled. `CharacterPlayerController` explicitly
possesses/unpossesses a pawn and transfers camera/input authority rather than making every character poll
local input conditionally.

### AD-07 — Project systems are composed in the project subclass

The reusable character addon does not depend on Interaction, Inventory or Gameplay Action. QuestWorld's
subclass adds those nodes and project-specific input forwarding, preserving the addon as a reusable
character rather than a game framework root.

### AD-08 — GameplayActionRunner is the project action-input boundary

Interaction no longer owns the Character's action loop. The runner reports every relevant owned or
external binding; Interaction only updates the focused bindings that it contributes. This is what lets
an inventory-granted action and a focused world interaction share input without coupling Character to
either action type.

### AD-09 — Presentation ownership follows gameplay ownership

`InteractionPresenter` renders target-oriented UI; `GameplayActionPresenter` renders owned player
actions. Both consume the same generic action presentation model instead of the Character maintaining a
custom prompt path.

## Regression coverage

Focused Character tests protect the brittle truths rather than every implementation detail: initial
floor contact is not a landing, impact strength scales, rotated instances keep yaw correct, possession
transfers authority/camera, airborne state cancels turn-in-place, and direct simulation obeys explicit
view yaw without sampling the local camera.

The landing-strength test advances one physics tick at a time and then waits for the corresponding
process frame before sampling presentation. It measures the persistent peak of the camera impulse,
rather than relying on observing the one-tick `Landed` flag; a render-frame sampler can otherwise
miss that transient state. Every test scene runner passes `autoFree: true`, because GdUnit4Net's
`ISceneRunner.Load` default is `false` and would leave the synthetic physics nodes alive at teardown.

The multiplayer-specific movement/replication contract is documented separately in
[`replication.md`](replication.md).
