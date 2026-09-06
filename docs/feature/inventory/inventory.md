# Inventory

## Purpose

`addons/inventory_plugin` provides a deliberately small server-authoritative inventory: catalog-backed
item IDs, integer quantities, replication snapshots/deltas and detached persistence data.

It is now integrated into the QuestWorld demo. The project `Character` owns an `InventoryComponent` and
`InventoryReplicationSynchronizer`; the Battery flow adds/removes the `battery` entry and derives the
player-owned `Drop Battery` gameplay action from that replicated inventory truth.

The runtime consists of:

- `InventoryItemDefinition`, stable ID plus local presentation data;
- `InventoryCatalog`, the explicit set of IDs authoritative gameplay may store;
- `InventoryComponent`, quantities, mutation authority, signals and versioned save snapshots;
- `InventoryReplicationSynchronizer`, optional spawn snapshot + consolidated live changes;
- `InventoryEntry` and `InventorySavedState`, detached query/persistence contracts.

The component owns counts, not physical meaning. A battery may be a world object, an inventory entry and
later a socketed object without Inventory deciding what “carried”, “installed” or “key” means.

## Runtime contract

- Peerless/offline and server copies are authoritative; clients cannot mutate successfully.
- IDs must exist in the assigned `InventoryCatalog` and mutation quantities must be positive.
- Removing more than is held removes only the held quantity; zero stacks are absent from storage.
- `GetEntries()` returns a detached, deterministically ordered snapshot.
- Item definitions and icons are local assets and never enter replicated state.
- `SaveState()` produces a versioned detached snapshot; `LoadState()` validates version, catalog
  membership, positive quantities and duplicate IDs before replacing authoritative state.

## Replication

`InventoryReplicationSynchronizer` is optional transport around the component. It does not change the
inventory mutation API and does not keep a second authoritative collection.

The synchronizer sends:

- a complete spawn snapshot for a newly synchronized peer;
- consolidated changed quantities for live updates.

Live changes are coalesced by item ID before publication. Godot's `MultiplayerSynchronizer` owns reliable
ordered transport, spawn delivery and visibility; the plugin owns the semantic dictionary being sent.
Applying a spawn snapshot emits synchronization-aware quantity changes plus `InventorySynchronized`;
live batches emit normal remote quantity changes.

The project Character is recursively owned by its player peer, while inventory truth remains
server-owned. Its `InventoryReplicationSynchronizer` therefore explicitly uses server multiplayer
authority rather than inheriting the Character's player authority.

## Gameplay Action integration

Inventory does not own or replicate action nodes. `ItemActionGrant` is project integration code that
observes inventory quantity changes and derives player-owned `InputGameplayAction` occurrences such as
`Drop Battery`.

This ordering is intentional on clients:

```text
authoritative inventory changes
→ snapshot/delta applied locally
→ ItemQuantityChanged
→ local action grant/remove
→ GameplayActionRunner binding
→ GameplayActionPresenter
```

The server adding a dynamic action node is not expected to replicate that node. Every peer reconstructs
the derived action only after receiving the inventory truth from which it follows.

The inverse Battery interaction is equally domain-specific: an interaction executor mutates the
Character inventory and world object. Inventory itself never depends on Interaction or Gameplay Action.

## Architecture decisions

### AD-01 — Stable `StringName` IDs instead of an asset-manager identity system

The Unreal prototype used Primary Asset IDs. Godot has no need for an equivalent global asset-manager
layer here: `InventoryItemDefinition.Id` is the stable gameplay identity and `InventoryCatalog` explicitly
declares the IDs valid for one inventory domain.

### AD-02 — The catalog validates identity; the component owns quantities

Definitions are reusable presentation/configuration assets. Runtime state is only the component's
`ItemId → quantity` mapping. This prevents shared Resources from accidentally carrying per-owner state.

### AD-03 — Inventory stores abstract possession, not world semantics

The component deliberately does not model sockets, hands, pickups, keys or equipment slots. Those are
systems integrating with the same stable item identity. This keeps Battery useful as a real test without
baking that one use case into Inventory.

### AD-04 — Authority lives in the domain component

All gameplay mutations are server-authoritative, with peerless play treated as its own authority.
Replication is an observer/transport layer around that truth rather than a second mutable model.

### AD-05 — Spawn snapshot plus coalesced reliable changes

The inventory is small enough that a complete spawn snapshot and changed-quantity batches are simpler
than recreating a FastArray-like protocol. No revision/resync layer is added while a single reliable
ordered stream satisfies the actual requirements.

### AD-06 — Definitions are never replicated

Peers already own the catalog/resources. Replication transports stable IDs and quantities only, avoiding
asset duplication and keeping wire state independent from local icons/text.

### AD-07 — Persistence is a detached versioned contract

Inventory knows how to serialize/validate its semantic state but does not own save files or a global
persistence service. A future save system receives `InventorySavedState` and decides where/when it is
stored.

### AD-08 — Dynamic gameplay actions are derived state

Inventory-granted actions are reconstructed from replicated inventory instead of replicating action
Nodes. This preserves Gameplay Action's V1 boundary: dynamic grants remain local derived state until a
real cross-domain grant replication requirement appears.

## Deferred work

- reusable inventory UI/presentation beyond the demo action prompt;
- project-wide/shared inventory lookup once a real ownership use case needs it;
- integration with the future persistence owner;
- richer inventory domains such as equipment/slots only if the playable slice requires them;
- a custom transport/resynchronization protocol only if the reliable ordered snapshot+delta model proves
  insufficient in practice.
