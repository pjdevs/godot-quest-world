# Stateful Presentation — authoritative truth vs local representation

> **Status: planned.** No `StatefulPresentation` runtime model exists today.

## Problem

`StatefulComponent` deliberately answers one question only:

> **What is true in the world?**

That contract works for gameplay, replication and persistence, but multiplayer presentation may know
what a local intention is expected to do before the corresponding authoritative Stateful mutation has
replicated back to this peer.

Example:

```text
local action request
→ Gameplay Action prediction / requester acknowledgement
→ server mutates Stateful to raising
→ Stateful replication arrives
→ visual starts reacting to raising
```

Gameplay Action can already present the **execution** immediately (including predicted timing/progress
and `RequestedLocally | Observed` relation). It does not predict the **world-state consequence** of that
execution. Those are separate facts and should remain separate.

The same issue also exists without Interaction: an Area trigger, mission script, remote button or level
system may all mutate one Stateful object.

## Central invariant — truth never becomes predictive

`StatefulComponent` does not change meaning:

```text
Stateful.State
    = authoritative/local replicated truth
    = gameplay-readable truth
    = persistable truth
```

There is no `SetPredictedState()` on Stateful. Rules, collision, navigation, quests and save logic keep
reading `Stateful.State` and its existing signals.

Prediction adds a second optional **presentation-only read model**:

```text
StatefulComponent      “what is true?”
StatefulPresentation   “what should this peer currently show?”
```

## Proposed primitive

```csharp
[GlobalClass]
public partial class StatefulPresentation : Node
{
    [Export]
    public StatefulComponent? Stateful { get; set; }

    public StringName PresentedState { get; }
    public bool HasLocalOverride { get; }

    public StatePresentationHandle OverrideState(StringName state);
    public void ClearOverride(StatePresentationHandle handle);
}
```

Effective value:

```text
PresentedState = active local override ?? Stateful.State
```

A local override is protected by a handle/generation. Only the currently active handle may clear it; a
late callback from an older request must not roll back newer presentation.

One Stateful has one effective presented state, so this is not a stack of speculative truths. A new
override replaces the previous one.

The component also needs a lightweight presented-state change notification carrying enough information
to distinguish local override from Stateful-originated change and preserve Stateful's
`isSynchronization` information when relevant.

## Reconciliation

Authority always wins.

### Matching authoritative state

```text
truth      = idle
presented  = activating   // local override

replication: truth = activating

truth      = activating
presented  = activating   // override absorbed silently
```

Removing the matching override must not replay `activating → activating`; a visual started immediately
should not restart when network truth catches up.

### Refused/abandoned intention

```text
truth      = idle
presented  = activating

clear active handle

truth      = idle
presented  = idle
```

Only presentation rolls back. Authoritative state never moved locally.

### Authority diverges

Any genuine Stateful change supersedes the active override, even when it is not the predicted value:

```text
truth      = idle
presented  = activating

replication: truth = locked

truth      = locked
presented  = locked
```

This covers races, refusals represented as another state and skipped intermediate transitions.

## Simulation / presentation boundary

| Consumer | Source |
| --- | --- |
| gameplay rules / quests | `Stateful.State` |
| collision / navigation / simulation | `Stateful.State` |
| save / replication | `Stateful.State` |
| mesh / material / light / VFX / UI | `StatefulPresentation.PresentedState` when opted in |
| anticipated local feedback | `StatefulPresentation.PresentedState` |

This feature does **not** make predicted presentation into predicted physics. If an AnimationPlayer moves
both a mesh and an authoritative collider, the visual and simulation responsibilities may need to be
split before the visual side can safely anticipate state.

## Integration with Gameplay Action

The old proposal assumed Interaction owned its own execution/prediction protocol. That is no longer true:
request transport, timing prediction, execution relation and terminal acknowledgements now live in
`GameplayActionRunner` / `GameplayActionComponent`.

Therefore this proposal does **not** predefine an `InteractionActionPrediction` hook.

The first integration should be designed only after the Stateful primitive is validated. It must follow
these constraints:

- consequence prediction is optional and local-only;
- it never changes authoritative execution semantics or completion;
- it may react to request/start/reject/complete/cancel/fail lifecycle already exposed by Gameplay Action;
- it must not duplicate authored target states already owned by Stateful gameplay executors;
- Interaction may adapt the generic mechanism for target actions, but Stateful never depends on
  Interaction or Gameplay Action.

The existing executors are current architecture, not future work:

- `SetStateGameplayActionExecutor`;
- `TransitionStateGameplayActionExecutor`;
- `TimedTransitionStateGameplayActionExecutor`.

A future adapter should read their semantic state configuration rather than asking the author to enter
`TargetState`, `RunningState`, `CompletedState` or `CancelledState` twice.

## Useful non-Interaction cases

### Remote wall

A button may request an action that eventually mutates a separate wall Stateful. The wall's simulation
continues to follow `Stateful.State`; its visual may use a local presentation override while authority is
catching up.

### Area-triggered elevator

A level trigger can use the same primitive without any `InteractionAction`: local presentation begins,
the trigger's own server protocol validates/mutates Stateful, then authoritative replication absorbs or
supersedes the override.

### Room power presentation

Lights/audio may observe one `StatefulPresentation` while puzzle logic and replication continue to use
the underlying Stateful. Producers do not need to know which visual consumers exist.

## Implementation plan

### 1. Implement the Stateful-only primitive

- explicit `StatefulComponent` reference;
- `PresentedState` / `HasLocalOverride`;
- generation-protected local override;
- stale clear = no-op;
- Stateful change supersedes override;
- matching authority reconciliation is silent;
- validate override values against the assigned schema;
- no replication, persistence or `_Process` loop of its own.

Minimum tests:

- mirror with no override;
- local override and clear rollback;
- matching authoritative convergence without duplicate presented transition;
- divergent authority wins;
- stale handle cannot clear a newer override;
- invalid schema state is refused;
- Stateful synchronization metadata propagates correctly when no override masks it.

### 2. Validate on a real world object

Use a wall/door/room where visual presentation can be distinguished from authoritative simulation. The
primitive must make no assumption that its owner is interactive.

### 3. Design the generic action integration from the delivered Gameplay Action lifecycle

Only after the primitive proves useful, define the smallest local consequence-prediction adapter needed
by the demo. Prefer a generic Gameplay Action seam if both owned actions and Interaction actions need it;
otherwise keep the adapter in the consuming integration.

### 4. Add real network-sequence coverage

Verify ordering, not only final state:

1. local presentation may start before Stateful replication returns;
2. refusal/abandon restores current truth;
3. matching authority does not restart visual presentation;
4. divergent authority supersedes prediction;
5. instantaneous actions can still anticipate visible consequence;
6. consequence prediction never defines execution completion;
7. terminal acknowledgement followed by Stateful replication cannot double-play the same transition.

## Non-goals

- no FSM or transition graph in Stateful;
- no client mutation of `Stateful.State`;
- no generic collision/navigation/physics rollback;
- no replication or persistence of presentation overrides;
- no global prediction manager;
- no new clock synchronization;
- no coupling from `stateful_plugin` to Gameplay Action or Interaction;
- no duplicate Interaction execution/prediction protocol.

## Success criterion

Both statements remain true:

> **`Stateful.State` is always world truth.**

> **A peer may show the expected local consequence before that truth returns through replication.**
