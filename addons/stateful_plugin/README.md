# Stateful Plugin

`StatefulComponent` owns one authoritative fact about the world: `closed`, `open`, `powered`, `flooded`, etc.

## Philosophy

Stateful answers **“what is true?”** and nothing else.

- It stores one `StringName`, optionally constrained by a `StateSchema`.
- It does not decide which transitions are legal, who may trigger them, or what they do.
- It replicates and exposes snapshots, but it does not provide a save backend.
- Gameplay writes on the server; every peer may react to the applied value.

Prefer composition over subclassing: add a `StatefulComponent` beside the object it describes. A door script, interaction executor, quest system, or presentation script gives the values their meaning.

## Create a state

1. Optionally create a `StateSchema` resource and fill `States` with every accepted value.
2. Add a `StatefulComponent` node to the object.
3. Assign `Schema` and `InitialState` in the Inspector.
4. For multiplayer, add a `MultiplayerSynchronizer` below the component and replicate `.:ReplicatedState` from authority.

Without a schema, every value is accepted. A schema only validates names; it is not a state machine.

```text
Door
├── StatefulComponent        InitialState = closed
│   └── MultiplayerSynchronizer
├── DoorGameplay
└── DoorPresentation
```

Enable `stateful_plugin` in **Project Settings > Plugins** to get Inspector warnings. The runtime itself has no autoload and no per-frame callback.

## Read and change state

Read `State` anywhere. Call `SetState` only from authoritative gameplay:

```csharp
if (!_stateful.SetState("open"))
{
    // Non-authority, undeclared value, or already open.
}
```

`SetState` returns `true` only when a different, declared value was applied. Offline play counts as authority.

Choose the narrowest signal for reactions:

| Signal | Runs on | Use for |
| --- | --- | --- |
| `StateChanged` | Server and clients | Logic that genuinely belongs on every peer |
| `StateChangedAuthority` | Offline host, listen host, dedicated server | Authoritative gameplay effects |
| `StateChangedPresentation` | Offline host, listen host, clients | Animation, audio and UI |

Signals run after the new value is fully applied. `_Ready()` applies `InitialState` without emitting them, so initialize presentation once from `State` when connecting.

## Save and restore

`SaveState()` returns a versioned `StatefulSavedState`; the host project decides where and when to store it. Collect the server copy for an authoritative save.

`LoadState(snapshot)` is server-only. It rejects an unknown version or a state absent from the current schema, and deliberately re-emits signals even when the restored value equals the current value.

## Use it from Interaction

The optional bridge lives in `interaction_plugin/integration/stateful`:

- `StatefulStateInteractionRule` reads state to allow, block, or hide an action.
- `SetStateInteractionExecutor` applies one state instantly.
- `TransitionStateInteractionExecutor` applies running, completed, and cancelled states around a long action.

The dependency points from Interaction to Stateful; Stateful knows nothing about interactions.

## Technical reference

### Lifecycle and cost

| Path | Side | Frequency and cost |
| --- | --- | --- |
| `_Ready()` | Every peer | Once; validates and copies `InitialState` |
| `SetState()` | Authority only | On demand; constant work plus signal listeners |
| Replicated setter | Clients | Only when replication applies a value |
| `SaveState()` / `LoadState()` | Local read / authority write | On demand |
| `_Process()` / `_PhysicsProcess()` | — | Not implemented: zero polling cost |

Replication sends changes through the technical `ReplicatedState` property. Gameplay must never assign that property directly. A replicated server value is trusted on clients and is not revalidated against their local schema.

### Validation behavior

- An undeclared `InitialState` produces an error but is preserved; world state is never silently corrected.
- `SetState` rejects an undeclared or unchanged value with `false`.
- `LoadState` throws for an unsupported snapshot version, missing authority, or an undeclared value.
- `StateSchema.Contains` and `StatefulComponent.IsStateDeclared` are synchronous, pure queries.

### Mutation boundary

Internally, `ApplyStateCore` finishes the mutation before `DispatchStateTransition` invokes external signal listeners. These methods are intentionally `internal`; consumers use `SetState`, `LoadState`, replication, and the public signals.

### Scope

The runtime is namespaced under `QuestWorld.State`, creates no input action, and has no Interaction, Quest, Inventory, Dialog, Character, or storage dependency. `StatefulValidator` supplies editor diagnostics without making runtime scripts `[Tool]`.
