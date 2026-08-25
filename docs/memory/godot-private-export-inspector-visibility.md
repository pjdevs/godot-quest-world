# Godot private exports stay visible in the Inspector

## Finding

In Godot C#, changing an exported property from `public` to `private` does not remove its `Editor`
usage flag, so a private `[Export]` property is still shown in the Inspector.

Clearing `PropertyUsageFlags.Editor` in `_ValidateProperty()` does **not** fix it either: the property
keeps appearing in the Inspector. That override was added for `InteractionStateful.ReplicatedState`
in `a8d52f4` and removed again in `4413901`; only the removal survived, together with a documentation
claim and a regression test that never existed in the working tree.

## Consequence for this project

A replication transport property is kept as a private `[Export]` property and is accepted as visible
in the Inspector:

- `InteractionStateful.ReplicatedState`
- `StatefulComponent.ReplicatedState`

Do not reintroduce `_ValidateProperty()` for that purpose. The real contract is behavioral, not
visual: gameplay mutates the authoritative value through `SetState()`, and the `MultiplayerSynchronizer`
uses the `.:ReplicatedState` path. Both are covered by tests.

## Workflow lesson

A memory note must describe the code as it is committed. Before trusting a note that promises a
regression guard, grep for the test it names.
