# Interaction Core — decouple mutation from callbacks

## Goal

Refactor the interaction runtime so that:

* core state mutation is deterministic and self-contained;
* no external handler, signal, RPC, UI callback or cross-node callback runs while a core object is being mutated;
* mutation returns explicit effects/results describing what must happen next;
* effects are dispatched only after mutation has completed;
* the public behavior stays equivalent;
* the architecture can later be ported to Rust without fighting nested `bind_mut()` / reentrant borrows;
* C#, C++ and Rust implementations can share the same conceptual lifecycle.

This is **not** a Rust-specific refactor. Rust borrow constraints merely expose coupling already present in the current design.

---

## 1. Core rule

No mutation method may directly call code outside its own state boundary.

Avoid:

```csharp
private bool ApplyState(InteractionState newState)
{
    var oldState = _state;
    _state = newState;

    EmitSignal(...);
    handler.OnInteractionStateChangedAuthority(...);
    _interactive.NotifyStatusChanged();

    return true;
}
```

Target:

```csharp
private StateTransitionResult ApplyState(InteractionState newState)
{
    var oldState = _state;

    if (oldState == newState)
        return StateTransitionResult.Unchanged;

    _state = newState;

    return new StateTransitionResult(
        Changed: true,
        OldState: oldState,
        NewState: newState
    );
}
```

Then:

```csharp
var result = ApplyState(newState);

DispatchStateTransition(result);
```

The mutation finishes **before** any callback is invoked.

---

## 2. Separate three responsibilities

Every relevant operation should conceptually have three phases:

```text
validate / compute
        ↓
mutate core state
        ↓
dispatch effects
```

### Mutation layer

Responsible only for:

* changing fields;
* maintaining invariants;
* collections;
* active/focused references;
* state transitions;
* calculating results.

It must not:

* emit Godot signals;
* call gameplay handlers;
* invoke presentation code;
* send RPCs;
* traverse unrelated nodes;
* trigger another component's mutation indirectly.

### Effect layer

Represents what happened.

Example:

```csharp
public readonly record struct InteractionStateTransition(
    InteractionState OldState,
    InteractionState NewState
);
```

Or, when several outcomes are possible:

```csharp
public abstract record InteractionEffect;

public sealed record StateChanged(
    InteractionState OldState,
    InteractionState NewState
) : InteractionEffect;

public sealed record StatusChanged : InteractionEffect;

public sealed record InteractionStarted(
    InteractionInteractor Interactor
) : InteractionEffect;
```

Don't over-engineer this immediately. Prefer typed result structs when one operation naturally has one result.

### Dispatch layer

Responsible for side effects:

* Godot signals;
* `IInteractionHandler`;
* `IInteractionStateHandler`;
* inter-component notifications;
* presentation;
* RPC;
* logging.

---

## 3. `InteractionStateful` first

This is the best first target because it currently combines state mutation with external callbacks.

Current conceptual flow:

```text
SetState
 ↓
ApplyState
 ├─ mutate _state
 ├─ EmitSignal
 ├─ handler authority callback
 ├─ handler presentation callback
 └─ Interactive.NotifyStatusChanged
```

Target:

```text
SetState
 ↓
ApplyState
 └─ mutate state + return transition
 ↓
DispatchStateTransition
 ├─ EmitSignal
 ├─ authority callback
 ├─ presentation callback
 └─ notify Interactive
```

Suggested API:

```csharp
private InteractionStateTransition? ApplyState(
    InteractionState state,
    bool force = false
);
```

Then:

```csharp
public bool SetState(InteractionState state)
{
    if (!CanMutateState())
        return false;

    var transition = ApplyState(state);

    if (transition is null)
        return false;

    DispatchStateTransition(transition.Value);
    return true;
}
```

Important invariant:

> `ApplyState()` must be safe to call without executing arbitrary external code.

---

## 4. Interaction start/end lifecycle

Apply the same pattern to:

```text
StartInteractionPhase
EndInteractionPhase
ReleaseInteractionInput
StartInteraction
```

Today, starting an interaction can cascade from `InteractiveComponent.StartInteraction()` into the handler, then into `InteractionStateful`, then back into `InteractiveComponent.NotifyStatusChanged()`.

That recursive object graph should become explicit.

Instead of:

```csharp
handler.OnStartInteractionInput(context);
```

inside the mutation path, prefer:

```csharp
InteractionStartResult result = BeginInteraction(interactor);

if (!result.Started)
    return false;

DispatchInteractionStarted(result);
```

Possible result:

```csharp
public readonly record struct InteractionStartResult(
    bool Started,
    InteractionContext Context
);
```

The dispatch can then call:

```csharp
handler.OnStartInteractionInput(result.Context);
```

without the `InteractiveComponent` itself being held in a mutable operation.

---

## 5. Keep validation pure when possible

Functions such as:

```csharp
EvaluateStatus(...)
CalculateInteractionScore(...)
IsWithinInteractionRange(...)
```

should remain query-like and ideally have no state mutation or callbacks.

`EvaluateStatus()` currently asks rules and eventually calls the owner's custom handler.

That's acceptable **only if it is semantically a query**.

Make the contract explicit:

```csharp
InteractionStatus EvaluateCustomInteractionStatus(
    in InteractionContext context
);
```

must be:

* side-effect free;
* non-mutating;
* safe to call multiple times;
* independent of callback ordering.

Document that rule.

If custom status evaluation needs to mutate state, it is no longer status evaluation and should become a command.

---

## 6. Commands vs queries

Establish this convention throughout the API.

### Queries

Examples:

```csharp
EvaluateStatus()
GetPresentation()
CalculateInteractionScore()
IsWithinInteractionRange()
```

Rules:

* return values;
* no mutation;
* no callbacks;
* no RPC;
* no signal emission.

### Commands

Examples:

```csharp
StartInteraction()
EndInteraction()
SetState()
ReleaseInteractionInput()
```

Rules:

* mutate;
* return explicit outcome/effects;
* dispatch happens after mutation.

This makes the whole framework substantially easier to reason about and test.

---

## 7. Separate state transition from presentation notification

Currently `InteractionStateful.ApplyState()` directly invokes both authority and presentation callbacks.

Keep the distinction, but move it to dispatch:

```csharp
private void DispatchStateTransition(
    InteractionStateTransition transition
)
{
    EmitSignal(...);

    DispatchAuthorityStateChanged(transition);

    if (!OS.HasFeature("dedicated_server"))
        DispatchPresentationStateChanged(transition);

    _interactive?.NotifyStatusChanged();
}
```

Later this can be moved again into a dedicated dispatcher without changing state logic.

---

## 8. Do not build one giant generic event bus

Avoid turning this into:

```csharp
List<IInteractionEvent>
InteractionEventBus
IInteractionEventDispatcher
IInteractionEventListener
...
```

unless the complexity actually requires it.

Start with local typed result values:

```csharp
StateTransitionResult
InteractionStartResult
InteractionReleaseResult
FocusChangeResult
```

This gives Rust-compatible architecture without introducing framework ceremony.

---

## 9. Interactor focus mutation

`InteractionInteractor.RecalculateFocus()` currently mutates `_focusedInteractive`, emits a signal and immediately emits status.

Refactor to:

```csharp
private FocusChangeResult RecalculateFocusCore()
```

Example:

```csharp
public readonly record struct FocusChangeResult(
    InteractiveComponent? Previous,
    InteractiveComponent? Current,
    bool Changed
);
```

Then:

```csharp
public bool RecalculateFocus()
{
    var result = RecalculateFocusCore();

    DispatchFocusChange(result);

    return result.Changed;
}
```

Core:

```text
candidate evaluation
→ choose best
→ update _focusedInteractive
→ return result
```

Dispatch:

```text
FocusedInteractiveChanged signal
→ status update
→ optional automatic interaction request
```

Especially important: **automatic interaction should not start recursively from inside focus mutation**.

Instead of:

```text
RecalculateFocus()
  ↓
mutate focus
  ↓
TryStartInteractionInput()
```

do:

```text
RecalculateFocusCore()
  ↓
FocusChangeResult
  ↓
DispatchFocusChange()
  ↓
if auto → TryStartInteractionInput()
```

---

## 10. Collection mutations follow the same pattern

Methods:

```csharp
AddInteractive()
RemoveInteractive()
AddInteractiveIndication()
RemoveInteractiveIndication()
```

currently mutate collections and can immediately trigger focus/status work.

Prefer:

```text
collection mutation
→ result
→ downstream recalculation
```

For example:

```csharp
private bool AddInteractiveCore(
    InteractiveComponent interactive
);
```

then:

```csharp
if (AddInteractiveCore(interactive))
    RecalculateFocus();
```

This doesn't need an effect type if a `bool` is sufficient.

The principle matters more than the abstraction.

---

## 11. Cross-node references are handles, not ownership

Architecturally treat:

```text
Interactor → Interactive
Interactive → Stateful
Stateful → Interactor
```

as **references to external objects**, never owned nested mutable state.

This prepares directly for Rust:

```rust
Gd<Interactive>
Gd<Interactor>
Gd<InteractionStateful>
```

instead of trying to model:

```rust
&'a mut Interactive
```

Long-lived borrows should never become part of the architecture.

---

## 12. No callback while another object is borrowed/mutating

Establish this as an explicit project invariant:

> Before calling any external component, handler, signal, RPC, `Callable`, or user-provided code, the current core mutation must have completed.

That includes:

```text
IInteractionHandler
IInteractionStateHandler
InteractionRule
Godot signals
RPC
UI callbacks
custom scripted hooks
```

For rules, queries are fine as long as they stay pure.

This one rule maps almost perfectly to the Rust constraint later.

---

## 13. Tests

Every core transition should become testable without relying primarily on emitted signals.

Example:

```csharp
var result = stateful.ApplyStateForTest(
    InteractionState.Activating
);

Assert.That(result.OldState, Is.EqualTo(InteractionState.Idle));
Assert.That(result.NewState, Is.EqualTo(InteractionState.Activating));
Assert.That(stateful.State, Is.EqualTo(InteractionState.Activating));
```

Then separately test dispatch:

```text
transition
→ correct signal emitted
→ authority callback called once
→ presentation callback called once
→ status invalidated
```

This produces two levels:

```text
core state tests
integration / Godot dispatch tests
```

That will be extremely useful when comparing C# and Rust implementations later.

---

## 14. Public API impact

Try to preserve the current external API initially.

Keep:

```csharp
SetState(...)
StartInteractionPhase(...)
EndInteractionPhase(...)
TryStartInteractionInput(...)
```

Refactor their internals.

Don't expose `Effects` publicly unless consumers genuinely need them.

Internally:

```text
public command
    ↓
private Core mutation
    ↓
private Dispatch
```

Example:

```csharp
public bool SetState(InteractionState state)
{
    var result = SetStateCore(state);

    if (!result.Changed)
        return false;

    Dispatch(result);
    return true;
}
```

That means existing gameplay code does not need to change just because the internals become Rust-friendly.

---

## 15. Desired final dependency direction

Target:

```text
Interaction API
     │
     ▼
Core mutation / state
     │
     ▼
typed results
     │
     ▼
Godot integration / dispatch
     │
     ├─ signals
     ├─ handlers
     ├─ RPC
     └─ presentation
```

Never:

```text
Core
 ↓
handler
 ↓
Stateful
 ↓
Interactive
 ↓
Interactor
 ↓
Core again
```

inside one synchronous mutation chain.

---

## 16. Scope of the first refactor

I'd keep the first pass extremely targeted:

1. `InteractionStateful.ApplyState()` → transition result + dispatch.
2. `StartInteractionPhase`, `EndInteractionPhase`, `ReleaseInteractionInput` → mutation first, callbacks second.
3. `InteractionInteractor.RecalculateFocus()` → focus result + dispatch.
4. prevent automatic interaction from firing from inside focus mutation.
5. audit all `EmitSignal`, handler calls and cross-component mutating calls.
6. mark query APIs as conceptually pure.
7. add tests for state/focus results independently from dispatch.

Don't refactor networking, refs, editor tooling and naming in the same pass unless needed.

### Definition of done

After the refactor, this grep-style rule should roughly hold:

```text
core mutation methods:
    no EmitSignal
    no Rpc/RpcId
    no IInteractionHandler callback
    no IInteractionStateHandler callback
    no presentation call
```

And external callbacks should live in clearly identifiable `Dispatch*`, `Notify*` or boundary methods.

That gives you a C# implementation that is already **less coupled, less reentrant, more deterministic and more testable**, while making a Rust spike mostly a mechanical translation rather than an architectural rewrite.
