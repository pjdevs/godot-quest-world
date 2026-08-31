# Inventory

## Scope

The inventory spike ports the small Unreal Inventory plugin from `pjdevs/QuestWorld`, branch
`test/flow_graph_experimentations`, into `addons/inventory_plugin` without integrating it into the demo.

The runtime consists of:

- `InventoryItemDefinition`, containing a stable ID and local presentation data;
- `InventoryCatalog`, validating which IDs authoritative gameplay may store;
- `InventoryComponent`, owning quantities, authority checks, signals, and save snapshots;
- `InventoryReplicationSynchronizer`, optionally transporting a full spawn snapshot and consolidated live
  batches;
- `InventoryEntry` and `InventorySavedState`, detached data contracts for queries and persistence.

The component intentionally owns counts, not physical meaning. A battery can therefore be removed from a
world socket, carried as one inventory entry, and later installed by an Interaction integration without the
inventory treating it as a key or knowing anything about the station. This matches the demo's systemic
inventory role in the one-pager.

## Unreal-to-Godot mapping

| Unreal | Godot port |
|---|---|
| `FInventoryItemId` / Primary Asset ID | `StringName` declared by `InventoryItemDefinition.Id` |
| `UInventoryItemDataAsset` | `InventoryItemDefinition : Resource` |
| Asset Manager lookup | Explicit `InventoryCatalog : Resource` |
| `FInventoryItemEntry` | `InventoryEntry` snapshot plus runtime dictionary entry |
| `UInventoryComponent` | `InventoryComponent : Node` |
| `FFastArraySerializer` | Spawn snapshot plus per-frame changed quantities through `MultiplayerSynchronizer` |
| SPUD save fields | Versioned `InventorySavedState` handed to a future persistence owner |
| Blueprint lookup statics | Out of scope until player/shared inventory ownership exists |

## Runtime rules

- Offline, listen-server, and dedicated-server instances are authoritative.
- Clients cannot call successful mutations.
- IDs must exist in the assigned catalog and quantities must be positive.
- Removing more than is held clamps to the held quantity and deletes an empty stack.
- Item definitions and icons never enter replicated state.
- Save restoration rejects unsupported versions, unknown items, non-positive quantities, and duplicates.

## Replication spike

Add an `InventoryReplicationSynchronizer`, assign its `InventoryComponent`, and let its code-owned replication
configuration transport two self-rooted properties:

- `SpawnSnapshot` is a computed property marked for spawn synchronization and otherwise uses
  `ReplicationMode.Never`. Its getter captures the component's current state only when Godot reads it for a
  peer, so the synchronizer retains no second complete inventory.
- `DeltaBatch` is not sent on spawn and uses `ReplicationMode.OnChange`. Quantity changes are coalesced by ID
  and the final values replace this property once per gameplay frame.

The synchronizer's default `DeltaInterval = 0` evaluates on-change properties every network process frame.
There is no public pre-serialization callback; `_Process()` publishes the prepared batch, which is observed
that network cycle or the next depending on poll ordering. The maximum extra latency is one frame.

This is not an automatic collection diff: the plugin prepares the changed-quantity dictionary and Godot owns
its reliable ordered transport, spawn delivery, authority, and peer visibility. A single stream needs no
revision or explicit resynchronization protocol. Snapshot application emits synchronization signals; live
batches emit ordinary remote quantity changes.

## Deferred work

- demo/player ownership and shared inventory lookup;
- pickup, socket, and Interaction rules/executors;
- UI and presentation;
- live multiplayer coverage for the chosen dictionary Variant contract;
- persistence-system integration;
- custom unreliable transport or resynchronization protocol if future requirements invalidate the single
  reliable ordered stream.
