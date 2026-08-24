# Native interaction migration spike

## Purpose

`NativeInteractionStateful` is a side-by-side GDExtension probe for the existing C#
`InteractionStateful`. It validates that an interaction node implemented with `godot-cpp`
can expose Inspector properties, methods, enum constants, and signals to the Godot project.
It does not replace or modify the C# implementation.

## Native API

The node exposes:

- `initial_state`: editable enum applied when the node becomes ready;
- `state`: read-only current enum value;
- `set_state(state)`: returns `true` only when the value changes;
- `interaction_state_changed(old_state, new_state)`: emitted after a state change;
- `IDLE`, `ACTIVATING`, `ACTIVATED`, and `DEACTIVATING`: enum constants matching the
  numeric values of the C# `InteractionState` enum.

The probe deliberately excludes multiplayer authority, replication, specialized signals,
and persistence. Those require a separate design if the native migration is accepted.

## GDScript usage

```gdscript
var stateful := NativeInteractionStateful.new()
stateful.initial_state = NativeInteractionStateful.ACTIVATING
stateful.interaction_state_changed.connect(
    func(old_state: int, new_state: int): print(old_state, " -> ", new_state)
)
add_child(stateful)
stateful.set_state(NativeInteractionStateful.ACTIVATED)
```

## C# usage

GDExtension classes are not generated as compile-time GodotSharp types. C# can still use
the node through Godot's dynamic object API:

```csharp
GodotObject stateful = ClassDB.Instantiate("NativeInteractionStateful");
stateful.Set("initial_state", 1);
stateful.Connect(
    "interaction_state_changed",
    Callable.From<int, int>((oldState, newState) => GD.Print($"{oldState} -> {newState}"))
);
GetTree().Root.AddChild((Node)stateful);
bool changed = stateful.Call("set_state", 2).AsBool();
```

A thin C# wrapper would be required if compile-time typing and C# events are mandatory for
the final plugin API.

## Build and smoke test

Build from `extensions/interaction_plugin`:

```powershell
uv run scons platform=windows target=template_debug
```

Run from the repository root:

```powershell
godot --headless --path quest-world --script res://addons/interaction_plugin_extension/tests/native_interaction_stateful_smoke.gd --log-file .godot/native-interaction-stateful-smoke.log
```

The smoke test dynamically discovers and instantiates the class, verifies `initial_state`,
changes `state`, observes the signal payload, and checks that assigning the current state is
a no-op.
