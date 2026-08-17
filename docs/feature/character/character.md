# Character

## Purpose

The reusable Godot 4.7 C# `CharacterBody3D` lives in the `dummy_character_plugin` addon under the `QuestWorld.Character` namespace. It provides camera-relative movement, FPS/TPS views, eight-direction locomotion, sprint, jump/fall/landing, turn-in-place, upper-body pitch and procedural camera effects.

The game keeps a global `Character` subclass at [`quest_world/character/Character.cs`](../../quest_world/character/Character.cs). Its scene composes the generic addon scene with the project's `InteractionInteractor` and `InteractionPresenter`, so the addon remains reusable without an interaction dependency.

## Architecture

The root is an ordered motor/orchestrator surrounded by focused components:

```text
QuestWorld.Character.CharacterPlayerController
        |
        v
CharacterInputFrame (raw local input, one physics sample)
        |
        v
CharacterSimulationInput (camera-independent command)
        |
        v
QuestWorld.Character.Character / Simulate (orchestration facade)
        |
        v
QuestWorld.Character.CharacterMovement / Simulate (motor + MoveAndSlide)
        |
        v
CharacterFrameState (immutable post-move snapshot)
        |-----------------------------|
        v                             v
CharacterAnimationController   CharacterCameraEffects

`QuestWorld.Character.CharacterCameraRig` applies look/view configuration from the same input frame.
`QuestWorld.Character.CharacterLookPitchModifier` remains a post-animation skeleton modifier.

`Character` owns the current view mode, while `CharacterCameraRig` owns the serialized camera configuration: first-person offset, third-person distance, mouse sensitivity and pitch limits. `Character` resolves and orchestrates the rig without duplicating its settings.

The global project `Character` subclass hosts `InteractionInteractor` and `InteractionPresenter`. The interactor uses the active camera as its view origin; the subclass samples the `interact` action and forwards press/release to the interactor.
```

The components are:

- `addons/dummy_character_plugin/scripts/Character.cs`: Godot composition root and orchestration facade. It resolves scene nodes, owns the exported character configuration, adapts camera/input state and applies presentation. Its public `Simulate` method remains as the stable entry point.
- `addons/dummy_character_plugin/scripts/CharacterMovement.cs`: plain C# movement motor. It owns deterministic movement state, floor transitions and `MoveAndSlide`; `Simulate` consumes `CharacterSimulationInput` plus a plain `CharacterMovementSettings` snapshot and returns `CharacterFrameState`.
- `addons/dummy_character_plugin/scripts/CharacterPlayerController.cs`: global movement input sampling and explicit `Possess`/`Unpossess` authority.
- `quest_world/character/Character.cs`: project-only interaction composition and `interact` input forwarding.
- The remaining `CharacterInputFrame`, `CharacterSimulationInput`, `CharacterFrameState`, animation, camera and pitch scripts stay in the addon namespace `QuestWorld.Character`.

The movement configuration stays serialized on `Character`; `CharacterMovementSettings` is only a plain value snapshot, not a `Resource` or a second set of exported properties. This keeps one inspector-facing source of truth while allowing the motor implementation to evolve independently and later support alternative movement modes. Presentation systems do not resample global input or independently infer floor transitions; local-only look deltas are passed directly to camera presentation.

## Possession and controls

`CharacterPlayerController` owns the input actions and submits one frame to its possessed character before character physics. Switching pawn disables the previous camera and input authority, enables the new camera, and clears incompatible transient state.

- `WASD`: camera-relative movement.
- `Space`: jump.
- `Shift`: forward or forward-diagonal sprint.
- Mouse: camera yaw/pitch while captured.
- `Escape`: release the mouse.
- Left click: recapture the mouse if UI did not consume the click.
- `E`: start/end the focused interaction while held; the action is configurable through `CharacterPlayerController.InteractionAction`.

The test world contains one player controller with the project `res://quest_world/character/Character.tscn` as its spawned scene. Additional characters remain simulated but unpossessed until `Possess(character)` is called.

## Movement and orientation

- First-person visual yaw follows the camera rig in character-local space.
- Third-person forward movement faces the local movement direction; lateral/backward movement stays camera-facing for the directional blend space.
- World directions are converted through the character root basis before assigning the local `Visual.Rotation.Y`, so rotated scene instances stay aligned.
- Sprint is gated by `SprintForwardInputThreshold` because the current AnimationTree only has a forward sprint clip. Sideways and backward input keep walk speed and directional jog clips.
- Assigning `CurrentViewMode` invokes the same backed transition as `SetViewMode`, resetting camera effects and incompatible turn state.

The camera rig is the single serialized owner of the view settings; the `Character` exposes the rig reference to presentation code that needs camera configuration.

## Landing

The first `MoveAndSlide` floor sample establishes initial state and is never emitted as a gameplay landing. A real landing requires both:

- at least `MinimumLandingAirTime` (`0.1 s` by default), and
- at least `MinimumLandingImpactSpeed` (`2 m/s` by default).

Landing strength is derived from the downward velocity immediately before `MoveAndSlide`, ramps to full strength at `FullLandingImpactSpeed`, and scales the procedural camera offset/pitch. Animation receives the same landing event and fires `Jump_Land` as a one-shot overlay.

## Animation priority

Animation priority is `airborne > turn-in-place > locomotion`. Losing the floor or jumping immediately cancels the turn override, so it cannot mask `Jump` or `Fall`. Movement, view-mode changes and loss of possession also cancel incompatible turn state.

Base UAL clips are always validated. `Jump_Land` is required only when landing animation is enabled, and `Turn90_L`/`Turn90_R` only when turn-in-place is enabled.

## Look pitch

`CharacterLookPitchModifier` distributes clamped additive pitch across `spine_01`, `spine_02`, `spine_03`, `neck_01` and `Head`. It applies the UAL 180-degree visual-axis correction, uses 45% influence in third-person and full influence in first-person.

Bone lookup supports common aliases, lists every missing requested bone explicitly and treats an all-missing chain as unusable.

## Focused regression tests

`addons/dummy_character_plugin/tests/CharacterBehaviorTest.cs` intentionally freezes only brittle truths of the generic Character:

- initial floor contact does not trigger landing;
- a stronger impact produces a stronger landing effect;
- a rotated character instance keeps visual and camera yaw aligned;
- possession transfers input authority and the active camera;
- airborne state cancels an active turn-in-place override;
- direct simulation follows its explicit view yaw without reading the local camera rig.

Run them with:

```powershell
$env:GODOT_BIN = (Get-Command godot).Source
dotnet test quest-world.csproj
```

GdUnit4Net is referenced through NuGet; no GdUnit source tree is vendored in `addons/`. Its generated runtime bridge directory is ignored by Git.

## Validation

Validated on 2026-08-14 with Godot 4.7.1 Mono:

- `dotnet build quest-world.sln`: 0 warnings, 0 errors.
- Six focused GdUnit4Net C# tests pass.
- `test_world.tscn` runs headless for 120 frames without scene, C# or node-path errors; the project subclass is validated through the project scene.
