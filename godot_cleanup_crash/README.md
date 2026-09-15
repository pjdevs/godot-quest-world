# Godot Mono cleanup-crash MRP

This directory contains the smallest reproduction currently confirmed for the
Godot Mono shutdown crash observed in the Quest World project.

## Reproduction

Environment used for the confirmed runs:

- macOS
- Godot `4.7.2.stable.mono.official.ed1daf0bf`
- .NET `10.0.401`

From the repository root:

```sh
dotnet build godot_cleanup_crash/godot_cleanup_crash.csproj --nologo
/Applications/Godot_mono.app/Contents/MacOS/Godot \
  --headless --path godot_cleanup_crash \
  --script res://ProbeExact.gd --quit-after 1
```

`ProbeExact.gd` performs synchronous `PackedScene` loads and then calls
`quit()`. It does not instantiate the scenes and does not run GdUnit tests.
The default load order is:

1. `mrp/MinimalBaseGroundedActionPair.tscn`
2. `mrp/LeverScriptOnly.tscn`
3. `mrp/LongActionWithOfferDetails.tscn`

The process may report exit code `0` or `134`; classify a run as a crash when
the output contains `handle_crash: Program crashed with signal 11`.

The loader also accepts a comma-separated scene list after `--`, which is
useful for controls and load-order experiments.

## Current findings

The positive sequence was reproduced repeatedly at 10/10 before and after
cache cleanup. Single scenes and several scene pairs are stable. The crash is
at process teardown, after the synchronous loads, with the characteristic
Godot Mono backtrace entering `std::__1::recursive_mutex::lock()` and the
CoreCLR finalizer thread. Verbose runs reported leaked `CSharpScript`
resources, including gameplay-action, inventory, carry and interaction-offer
scripts.

The scene graph needed by the positive sequence is the reduced Character
action/inventory/carry graph plus the stateful long-action offer chain. In the
current assembly experiment, the following parent-project source groups are
also required to preserve the positive result:

- the GdUnit adapter and `gdUnit4.api` package;
- game-session and network-session runtime sources;
- `GameSessionTestFixtures.cs`;
- the gameplay-action, editor and interaction test source groups, except
  `InteractionInputMigrationTest.cs`;
- `StatefulBehaviorTest.cs`;
- `Game.cs`, `PlayerCharacterSpawnManager.cs` and `PlayerNetworkIdentity.cs`;
- the world and door source groups.

These files are linked from the parent project by the standalone `.csproj`.
That is intentional for the current assembly-composition repro, but means this
is not yet a fully self-contained upstream MRP. Copying those sources into the
standalone project changes the assembly/resource composition and was stable in
the experiments.

The tests are therefore not the execution trigger, but their compiled type
graph contributes to the current project-dependent reproduction. The next
step for an upstream issue is to replace the parent-project links with a
small, self-contained assembly while retaining the same crash.
