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

## AnimationTree

The state machine starts in `Locomotion` at zero blend (`Idle`). It travels through `Jump` and `Fall` based on physical state. The imported UAL animation names are `Idle`, `Jog_Fwd`, `Jog_Bwd`, `Jog_Left`, `Jog_Right`, `Sprint`, `Jump_Start` and `Jump`.

## Validation

Validated with:

- `dotnet build quest-world.sln` — success, 0 warnings, 0 errors.
- `godot --headless --path . --scene res://quest_world/character/Character.tscn --quit-after 2 --log-file .godot/character-runtime.log` — success.
- `godot --headless --path . --scene res://quest_world/levels/test_world.tscn --quit-after 2 --log-file .godot/test-world-runtime.log` — success.
- For environment-specific CLI details, see `docs/memory/godot-cli-headless-workflow.md` and `docs/memory/godot-animation-import-findings.md`.

The local Godot environment still reports a non-fatal root certificate-store warning during headless startup.
