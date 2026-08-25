# Godot private exports remain visible in the Inspector

## Finding

In Godot C#, changing an exported property from `public` to `private` does not remove the `Editor` usage flag. A private property marked `[Export]` therefore remains visible in the Inspector.

For a technical property that must stay registered with Godot, such as `InteractionStateful.ReplicatedState` used by `MultiplayerSynchronizer`, keep `[Export]` and clear only `PropertyUsageFlags.Editor` in `_ValidateProperty()`.

## Regression guard

`InteractionBehaviorTest.ReplicatedStateIsAvailableToGodotButHiddenFromTheEditor` verifies both parts of the contract:

- Godot can still discover the property;
- the Inspector does not expose it.

Do not remove `_ValidateProperty()` as unrelated cleanup while this transport property exists.
