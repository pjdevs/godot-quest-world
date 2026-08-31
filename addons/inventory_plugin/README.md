# Inventory Plugin

Small Godot C# port of the Unreal `InventoryPlugin` from `pjdevs/QuestWorld` on
`test/flow_graph_experimentations`.

The port keeps the useful original contracts:

- catalog-backed item identifiers;
- authoritative add and clamped remove operations;
- quantity-change signals;
- a versioned persistence snapshot;
- replicated inventory state.

It deliberately excludes player lookup, shared-inventory lookup, Interaction integration, UI, pickups,
equipment, slots, weight, and stacking rules. Those are consumers or later specializations, not core inventory.

## Authoring

1. Create `InventoryItemDefinition` resources with stable, non-empty `Id` values.
2. Put them in an `InventoryCatalog` resource.
3. Add an `InventoryComponent` node and assign the catalog.
4. For multiplayer, add an `InventoryReplicationSynchronizer` node and assign the component to `Inventory`.
5. Call `AddItem`, `RemoveItem`, `GetItemCount`, or `GetEntries` from authoritative gameplay code.

Static definition resources exist on every peer and are never networked. Runtime state contains only item IDs
and integer quantities.

## Replication

`InventoryReplicationSynchronizer` owns two self-rooted technical properties:

```text
SpawnSnapshot   spawn = true    replication mode = Never
DeltaBatch      spawn = false   replication mode = OnChange
```

`SpawnSnapshot` is a computed property: when synchronization starts for a peer, its getter captures the
current component state and Godot sends that temporary dictionary. The synchronizer does not retain a second
complete inventory, and a late joiner receives the present state without replaying past changes.

Live mutations are consolidated by item during the gameplay frame and replace `DeltaBatch` once in
`_Process()`. Godot's default `DeltaInterval = 0` observes on-change properties every network process frame
and sends that complete, small dictionary reliably. Godot does not calculate the collection diff: the
synchronizer automatically transports the batch that this node prepared.

There is no custom revision or resynchronization RPC. One reliable ordered on-change stream carries live
batches, while native spawn synchronization owns late join, reconnection, and visibility re-entry.

The authority never accepts catalog or definition resources from clients. A replicated snapshot is trusted
as server state and sanitized to non-empty IDs with positive quantities before presentation signals run.

## Signals and synchronization

`ItemQuantityChanged(itemId, oldQuantity, newQuantity, isSynchronization)` runs for each changed stack.
A complete spawn or restored snapshot uses `isSynchronization = true`; ordinary live batches and local
mutations use `false`. `InventorySynchronized(isInitialSynchronization)` also runs after a complete network
or restored snapshot, including an empty one.
