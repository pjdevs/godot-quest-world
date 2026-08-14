# Character

## Purpose

Reusable Godot 4.7 C# `CharacterBody3D` for gameplay prototyping. It provides camera-relative movement, FPS/TPS views, eight-direction locomotion, sprint, jump/fall/landing, turn-in-place, upper-body pitch and procedural camera effects.

## Architecture

The root is an ordered motor/orchestrator surrounded by focused components:

```text
CharacterPlayerController
        |
        v
CharacterInputFrame (immutable, one physics sample)
        |
        v
Character.cs (motor + MoveAndSlide)
        |
        v
CharacterFrameState (immutable post-move snapshot)
        |-----------------------------|
        v                             v
CharacterAnimationController   CharacterCameraEffects

CharacterCameraRig applies look/view configuration from the same input frame.
CharacterLookPitchModifier remains a post-animation skeleton modifier.
```

The components are:

- `Character.cs`: movement authority, floor transitions, local-space visual facing and deterministic orchestration.
- `CharacterPlayerController.cs`: global input sampling and explicit `Possess`/`Unpossess` authority.
- `CharacterInputFrame.cs`: move, look, jump and sprint sampled once per physics tick.
- `CharacterFrameState.cs`: shared movement, grounded, jump, landing, sprint and impact result.
- `CharacterAnimationController.cs`: AnimationTree validation, blend space, airborne priority, turn-in-place and landing one-shot.
- `CharacterCameraRig.cs`: yaw/pitch, clamps, FPS/TPS placement and active-camera ownership.
- `CharacterCameraEffects.cs`: render-frame bob, sway, FOV and impact-scaled impulses from the latest physics snapshot.
- `CharacterLookPitchModifier.cs`: additive upper-body look pitch after animation.

The current version is a clean, focused refactor: `Character.cs` remains the compact orchestration facade, while configuration and behavior live on the component that owns them. Presentation systems do not resample global input or independently infer floor transitions.

## Possession and controls

`CharacterPlayerController` owns the input actions and submits one frame to its possessed character before character physics. Switching pawn disables the previous camera and input authority, enables the new camera, and clears incompatible transient state.

- `WASD`: camera-relative movement.
- `Space`: jump.
- `Shift`: forward or forward-diagonal sprint.
- Mouse: camera yaw/pitch while captured.
- `Escape`: release the mouse.
- Left click: recapture the mouse if UI did not consume the click.

The test world contains one player controller with `InitialPawnPath = ../Character`. Additional characters remain simulated but unpossessed until `Possess(character)` is called.

## Movement and orientation

- First-person visual yaw follows the camera rig in character-local space.
- Third-person forward movement faces the local movement direction; lateral/backward movement stays camera-facing for the directional blend space.
- World directions are converted through the character root basis before assigning the local `Visual.Rotation.Y`, so rotated scene instances stay aligned.
- Sprint is gated by `SprintForwardInputThreshold` because the current AnimationTree only has a forward sprint clip. Sideways and backward input keep walk speed and directional jog clips.
- Assigning `CurrentViewMode` invokes the same backed transition as `SetViewMode`, resetting camera effects and incompatible turn state.

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

`quest_world/character/tests/CharacterBehaviorTest.cs` intentionally freezes only brittle truths:

- initial floor contact does not trigger landing;
- a stronger impact produces a stronger landing effect;
- a rotated character instance keeps visual and camera yaw aligned;
- possession transfers input authority and the active camera;
- airborne state cancels an active turn-in-place override.

Run them with:

```powershell
$env:GODOT_BIN = (Get-Command godot).Source
dotnet test quest-world.csproj
```

GdUnit4Net is referenced through NuGet; no GdUnit source tree is vendored in `addons/`. Its generated runtime bridge directory is ignored by Git.

## Validation

Validated on 2026-08-14 with Godot 4.7.1 Mono:

- `dotnet build quest-world.sln`: 0 warnings, 0 errors.
- Five focused GdUnit4Net C# tests pass.
- `test_world.tscn` runs headless for 120 frames without scene, C# or node-path errors.
