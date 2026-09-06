# Stateful

## Purpose

`addons/stateful_plugin` owns **world truth** independently from Interaction or Gameplay Action: a door
may be `closed/open/locked`, a room `dry/flooded/draining`, a power source
`powered/unpowered/overloaded`.

`StatefulComponent` answers only “what is true in the world?”. It does not decide whether a player may
perform an action, who requested a mutation, how a transition is executed, or how consequences should
be presented.

## Runtime model

`StatefulComponent` owns one authoritative `StringName` state. `StateSchema : Resource` optionally
declares valid values for validation; without a schema, any non-empty state is accepted. A schema is not
a finite-state machine: it defines no transitions, guards, entry/exit effects or hierarchy.

`StatefulSavedState` is a detached versioned persistence snapshot. The addon knows how to validate and
restore semantic state but owns no files, service locator or save backend.

## Authority and replication

`SetState()` is server-authoritative. A peerless/offline game counts as its own authority. A client
cannot successfully mutate the component.

A child `MultiplayerSynchronizer` transports the private technical `ReplicatedState` property. The
receiving side applies the server value **without revalidating its local schema**: authority has already
validated the mutation, and a schema mismatch between builds must not make a client silently diverge
from authoritative truth.

Three notifications expose the same transition with different scopes:

- `StateChanged` — every peer;
- `StateChangedAuthority` — offline/server authority only;
- `StateChangedPresentation` — every presentation-capable peer, excluding dedicated server.

All carry `(oldState, newState, isSynchronization)`.

### Synchronization vs lived change

`isSynchronization` distinguishes catching up to existing truth from witnessing a new event:

| Origin | `isSynchronization` |
| --- | --- |
| authoritative `SetState()` | `false` |
| subsequent replicated transition | `false` |
| first replicated state / late join | `true` |
| `LoadState()` | `true` |

The transition is emitted in both cases so state-driven geometry/pose converges. Consumers use the flag
to suppress one-shot feedback such as audio, particles or notifications when a peer is only catching up.
On synchronization, `oldState` is not guaranteed to be the world's previous historical state; only
`newState` is authoritative truth.

## Mutation and dispatch

State mutation follows one boundary:

```text
validate
→ ApplyStateCore()          # mutation only
→ DispatchStateTransition() # signals after state is coherent
```

No signal, RPC or arbitrary callback defines a partially-mutated state. The internal split is directly
testable without enlarging the public API.

## Schema and persistence validation

- `Schema == null`: state values are free.
- With a schema, `SetState()` rejects undeclared values without mutation.
- An invalid authored `InitialState` is reported but never silently replaced.
- `LoadState()` rejects unsupported versions and states not declared by the current schema.
- Restoring even the currently visible state can dispatch a synchronization transition, because the
  consumer still needs to converge to truth that existed before this process/session.

`StatefulValidator` provides the corresponding editor diagnostics for empty/duplicate schema values and
invalid initial configuration.

## Integrations

Stateful has no dependency on Interaction or Gameplay Action. Dependencies point **toward** Stateful:

- `StatefulStateInteractionRule` reads state for Interaction availability;
- Gameplay Action's optional Stateful integration provides generic state-mutating executors such as
  `SetStateGameplayActionExecutor`, `TransitionStateGameplayActionExecutor` and
  `TimedTransitionStateGameplayActionExecutor`.

Rules read state; executors mutate it. `InteractiveComponent` does not own or interpret a Stateful
reference. The old Interaction-specific `InteractionStateful` lifecycle is gone; `StatefulComponent` is
the single reusable world-state primitive.

## Architecture decisions

### AD-01 — State is a free `StringName`, not a framework enum

The original Interaction lifecycle enum predicted a small set of generic states and immediately failed
for doors, rooms and power systems. Stateful instead stores domain-authored names and assigns them no
universal semantics.

### AD-02 — Schema validation is optional and is not a state machine

`StateSchema` catches typos and invalid authoring while keeping the core agnostic to legal transitions.
Transition policy belongs to gameplay actions/executors or a future dedicated system, not to the value
container.

### AD-03 — One authoritative truth, replication as transport

Only authority mutates gameplay state. Replication applies that truth verbatim rather than re-running
client-side business validation. This avoids two peers deriving different worlds from the same server
mutation.

### AD-04 — Peerless play is authoritative

Offline mode is not a special fake-client path. With no `MultiplayerPeer`, the process is its own server,
so the same mutation API works in single-player, listen-server and dedicated-server contexts.

### AD-05 — Synchronization is explicit in notifications

A state transition and a player-observed event are not equivalent. The `isSynchronization` bit lets
simulation/presentation consumers converge their durable state while suppressing one-shot effects for
late join and save restoration.

### AD-06 — Notification scopes are separate but structurally identical

Universal, authority and presentation signals share one signature. Consumers choose responsibility by
channel rather than by reconstructing multiplayer role inside every handler.

### AD-07 — Mutation precedes dispatch

The component was designed around `mutate → notify`, not reentrant signal-driven mutation. This keeps
state invariants deterministic and portable across C#, C++ or stricter native implementations.

### AD-08 — Persistence is detached from storage

The component exports versioned semantic snapshots but no global save system. State ownership and save
orchestration remain different responsibilities.

### AD-09 — Stateful replaced Interaction state instead of depending on Interaction

Generic world state was extracted into its own addon. Interaction and Gameplay Action consume it through
optional integration adapters, preserving a one-way dependency graph.

### AD-10 — Current truth and predicted consequences are different models

`StatefulComponent.State` remains the non-predictive source of truth. The still-planned
[`stateful-presentation.md`](planned/stateful-presentation.md) proposes a separate consequence/presentation
layer rather than polluting authoritative state with “where this execution is heading”.

## Remaining planned work

[`planned/stateful-presentation.md`](planned/stateful-presentation.md) is intentionally retained: no
`StatefulPresentation`/consequence model exists in runtime today. It explores how UI can answer questions
such as “this running action will end in `open`” without changing the authoritative state contract above.

A generic FSM, transition graph and entry/exit effect framework remain explicit non-goals until a real
gameplay requirement justifies a separate system.
