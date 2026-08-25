# Stateful Plugin

This addon owns generic world state as a plain Godot node, without any interaction, quest or character dependency.

`StatefulComponent` holds one authoritative `StringName` value such as `closed`, `open`, `powered` or `flooded`. The runtime is namespaced under `QuestWorld.State`, creates no autoload and adds no input action. The component gives no universal meaning to any value: gameplay, presentation scripts and interaction rules interpret the value themselves.

Add `StatefulComponent` beside the object it describes and set `InitialState`. Assign the optional `StateSchema` resource to declare the accepted values; the component then rejects any other value in `SetState` and reports a configuration warning in the Inspector. Without a schema every value is accepted.

Mutation is authoritative: `SetState` is server-only and returns `false` for a non-server peer, an undeclared value, or an unchanged value. State is replicated through the technical `ReplicatedState` property, so a `MultiplayerSynchronizer` targeting `.:ReplicatedState` mirrors the server value to clients; gameplay always calls `SetState` instead of assigning that property.

Three signals separate the consumer scopes: `StateChanged` everywhere, `StateChangedAuthority` only where the peer has authority, and `StateChangedPresentation` everywhere except a dedicated server.

The mutation and the notification are separate steps. `ApplyStateCore` only mutates and returns the resulting transition, then `DispatchStateTransition` emits the signals, so no external code ever runs while the component is half-mutated.

Persistence is only a boundary: `SaveState` returns a versioned `StatefulSavedState`, and `LoadState` restores it, re-emits signals even for an unchanged value, and rejects an unsupported version or a value the schema does not declare. The addon stores nothing itself.

`StateSchema` is deliberately not a state machine. It declares values for validation and documentation; transitions, guards, entry and exit effects belong to gameplay.

When the addon is enabled, `editor/StatefulEditorPlugin.cs` registers an Inspector plugin and `StatefulValidator` centralizes the configuration warnings for `StatefulComponent` and `StateSchema` without making the runtime scripts `[Tool]`.
