# Interaction

## Purpose

`interaction_plugin` is the spatial, target-oriented integration layer built on top of
[Gameplay Action](../gameplay_action/gameplay-action.md). It answers:

- which world objects are relevant to this player;
- which target is focused;
- which actions this target should offer to this interactor;
- whether spatial access is still valid;
- how the target and its actions are projected to local presentation.

It does **not** own a second execution engine. Authoritative execution, reservations, input gestures,
request networking, acknowledgements and transient execution presentation belong to
`gameplay_action_plugin`.

## Current topology

A target authors one generic action host next to its Interaction adapter:

```text
Interactive object
├── GameplayActions                    # optional target-owned GameplayActionComponent
│   ├── OpenAction                     # ordinary GameplayAction
│   │   └── Executor
│   └── CloseAction
│       └── Executor
├── GameplayActionExecutionSynchronizer # optional, only for Replicated executions
├── InteractiveComponent               # ActionComponent + ordered Offers
│   ├── TargetRules
│   └── Offers                         # Target- or Instigator-sourced invocations
├── InteractionTargetReservationSynchronizer # optional, for target claims
├── InteractionArea / anchor
└── StatefulComponent                  # optional world truth
```

Authored actions are direct children of the `GameplayActionComponent` and are listed in its `Actions`
collection. `InteractiveComponent.Offers` is the ordered interaction-facing contract. Each offer
resolves an `ActionId` either from the target's `ActionComponent` (`Target`) or from the requesting
runner's `OwnedActionComponent` (`Instigator`). The offer supplies the local binding and invocation
target; it never transfers action ownership. The older `InteractionAction` projection remains only as
a migration bridge for existing scenes and tests.

The requester side composes the same generic runner with Interaction:

```text
Gameplay actor
├── GameplayActions                    # owned actions, if any
├── GameplayActionRunner
├── InteractionInteractor
│   └── InteractionDetector
├── InteractionPresenter               # optional
└── GameplayActionPresenter             # optional owned-action UI
```

`InteractionInteractor.Runner` is explicit. The game samples `GameplayActionRunner.GetRelevantInputs()`
and sends press/release edges to the runner; Interaction only refreshes its focused external bindings
before a press.

## Actions, rules and availability

`InteractionOffer : Resource` is the target-specific authoring unit. It contains an action source,
stable `ActionId`, optional `BindingConfig`, ordered offer rules, and an optional
`TargetConcurrencyGroup` with self/other presentation outcomes. It resolves to an ordinary
`GameplayAction`, so a player-owned `Take` and a door-owned `Open` use the same adapter.
For one interactor, authored offers on the same Interactive must resolve to distinct
`(GameplayActionComponent, GameplayAction)` endpoints. The editor reports duplicate endpoints within
one authored source, while runtime resolution rejects any remaining ambiguity (including a Target and
Instigator offer that happen to resolve to the same component and action).

Availability is evaluated in this order:

```text
offer configuration and endpoint resolution
→ spatial access
→ Interactive.TargetRules
→ InteractionOffer.Rules
→ target reservation group
→ resolved GameplayAction rules
→ action-owner concurrency
```

Offer and target rules receive an `InteractionContext` containing the interactor, Interactive target,
offer, resolved action and resolved action component. They are evaluated directly and are never copied
into or injected into a player-owned action's `Rules` collection. Rules are synchronous queries: they
read gameplay state but do not mutate it. The first result that is not `Allowed` wins.

`InteractionAction` and its target-rule adapter remain a compatibility path for target-owned content
that has not migrated to `Offers`; new code must author the offer path.

A programmatic `GameplayActionComponent.ExecuteAction()` deliberately bypasses spatial access because it
is not a player request, but still runs the same target/action rules and host reservations.

### Concurrency and relation

Availability, execution and local execution relation are separate facts:

```text
Availability = should this action be offered to this interactor?
Execution    = what is currently running?
Relation     = did this peer request that execution or merely observe it?
```

`InteractionAction.WhenExecutingBySelf` and `WhenExecutingByOther` independently choose `Hidden` or
`Blocked` for a busy concurrency group. Both default to `Blocked` for compatibility.

Examples:

```text
Hack        self Blocked / other Blocked
Dialogue    self Hidden  / other Blocked
Silent      self Hidden  / other Hidden
```

Target reservation is a separate scope from action-host concurrency. Offers sharing a
`TargetConcurrencyGroup` claim the same Interactive even when their actions belong to different
`GameplayActionComponent` instances, such as a door-owned `Open` and a player-owned `Force`. A claim
is acquired only after request access validation and remains held through the generic request lease
until the execution reaches a terminal state.

On the authority, “self” is attributed through the execution instigator. On a client,
`RequestedLocally` means self and `Observed` means other. The query applies to the whole host concurrency
group, not only the same `ActionId`.

## Detection and focus

`InteractionDetector` is a replaceable Node that separates **candidate sourcing** from shared spatial
predicates and focus selection. Three implementations are provided:

- `AreaInteractionDetector` for target-authored areas;
- `ProximityInteractionDetector` for registry + distance windows;
- `AimInteractionDetector` for a view/cast-oriented source.

Only the source of candidates changes. `Detect(interactive)` is the common single-target predicate and
`Score(interactive)` selects one focus among eligible targets.

The owning client walks the detector candidates each frame. The authority never tries to reconstruct a
client-only candidate source: when validating a request or a sustained interaction it asks the same
detector to `Detect()` the single target. `Detect()` therefore remains a tolerant distance/angle/LOS
window rather than a binary cast whose result would diverge merely because the server sees an older
transform.

A detector is required. Missing detector/view configuration is reported by runtime/editor validation;
there is no hidden default detection model.

### Line of sight

LOS is a shared detector predicate, not a separate interaction subsystem. The ray goes from
`ViewOrigin` to the interaction anchor using `OcclusionMask`, excluding the interactor body and target
geometry. Occlusion is authored on the **occluder** through a dedicated physics layer; an interactable
does not carry a “see through walls” exception.

Rays are refreshed in physics and cached with one-sided loss hysteresis. Regaining sight is immediate;
losing it waits for `LineOfSightLossGrace`, preventing indicators and sustained interactions from
flapping behind thin obstacles. A target queried before it has a cached sample is cast immediately so
server validation cannot reject a legitimate one-shot request just because the next physics frame has
not happened yet.

## Request access and sustained interactions

Focus resolves every visible offer and creates a generic binding whose cleanup source and invocation
target are both the focused Interactive. Focus loss removes those bindings without touching the real
action occurrence. The runner registers Interaction as an `IGameplayActionAccessProvider`; the
authoritative peer resolves its own target, finds the exact offer that maps to the requested endpoint,
re-validates spatial access and evaluates target/offer rules before execution. After that pure check,
the Interaction provider acquires the optional target reservation through the generic runner request
lease; failed, cancelled and completed requests release it. A client-supplied target or action endpoint
is never accepted as proof of access.

`InteractionActionExecutor` adapts `GameplayActionContext` into `InteractionExecutionContext`. An
interaction executor requires both an `InteractionAction` hosted by an `InteractiveComponent` and an
interactor resolved from the generic instigator. An execution with no interactor is generic gameplay,
not an interaction, and is refused by this adapter rather than silently inventing one.

`RequiresInteractorPresence` defaults to `true`. It feeds the generic
`RequiresRequesterPresence` policy, together with a binding whose `InputRequirement` is `Pressed`.
Channel-like work is therefore cancelled when the player releases/leaves; world-owned work can opt out
and continue after the requester has gone. An executor that becomes world-owned at a gameplay commit
calls `GameplayActionContext.ReleaseRequesterDependency()` at that commit; target disappearance is not
treated as valid access before that transition.

Timed completion is generic Gameplay Action policy. Interaction executors that need a timer compose or
specialize the generic timed execution primitives; hold duration remains only the local gesture used to
select an action.

## Stateful integration

Interaction owns no world-state component. Persistent/replicated truth belongs to
[`StatefulComponent`](../state/stateful.md).

`StatefulStateInteractionRule` is the Interaction-side read adapter for state-based availability. Generic
state-mutating executors such as `SetStateGameplayActionExecutor`, `TransitionStateGameplayActionExecutor`
and `TimedTransitionStateGameplayActionExecutor` live in Gameplay Action's optional Stateful integration.
The dependency direction remains from action/interaction integration toward Stateful, never the reverse.

Rules read state; executors mutate state. Interaction never interprets a universal state such as
`Idle == interactible`.

## Presentation

Interaction presentation is target-level. `InteractionTargetPresentation` carries the focused/indicated
target information and an ordered list of generic `GameplayActionPresentation` entries. Hidden actions
are absent; blocked actions remain present with their reason.

`InteractionPresenter` projects the target anchor to screen space and composes generic action widgets.
The generic `GameplayActionPresenter` separately renders bindings owned by the runner. This keeps one
action presentation vocabulary without pretending a world target and an inventory-granted action have
the same container UX.

Target distance is a named physical quantity measured from the detector's `InteractionOrigin`; raw focus
score is never exposed to UI. Hold progress belongs to the binding presentation and execution progress
to the generic execution slot. They are different phases and are never added together.

## Authoring

For a normal interactive object:

1. Add ordinary `GameplayAction` occurrences as direct children of a target-owned
   `GameplayActionComponent` only for capabilities intrinsically owned by the object.
2. Add `InteractiveComponent`, assign its optional target `ActionComponent`, interaction area/anchor
   and optional indication area, then author ordered `Offers`. Use `Source = Target` for target-owned
   actions and `Source = Instigator` for actions on the requesting runner.
3. Put shared target conditions in `TargetRules` and offer-specific conditions in `Offer.Rules`.
4. Use a Stateful component only when the object owns durable world truth. State-dependent availability
   belongs in `StatefulStateInteractionRule`; state mutations belong in an executor.
5. Choose `ExecutionVisibility.RequesterOnly` by default. Use `Replicated` only when other peers must see
   the transient execution and wire a `GameplayActionExecutionSynchronizer`; persistent state still
   belongs to Stateful. Add an `InteractionTargetReservationSynchronizer` when a non-empty
   `TargetConcurrencyGroup` must be visible to other peers or late joiners.
6. On the actor, wire one `GameplayActionRunner`, one `InteractionInteractor` and one detector. Feed
   relevant inputs to the runner; refresh focused Interaction bindings before the press edge.
7. Add `InteractionPresenter` only when target UI is wanted. Presentation is optional to the runtime.

The current topology is deliberately explicit. The remaining
[`planned/interaction-authoring-polish.md`](planned/interaction-authoring-polish.md) explores making direct
child composition the default while retaining explicit references as overrides; that work is not
implemented yet.

## Test hygiene

The interaction tests explicitly own every synthetic Godot node. Detached validator fixtures use
`AutoFree` (or `Free` in a `finally` block), while scene runners load their root with `autoFree: true`.
This prevents detached nodes and removed network participants from being reported as orphans.

## Architecture decisions

### AD-01 — Interaction is an adapter over Gameplay Action

The original Interaction lifecycle was extracted rather than generalized in place. Detection, focus,
spatial policy and target presentation stay here; execution, input and requester networking are shared
generic infrastructure. Production has one execution path.

### AD-02 — Multiple actions are explicit occurrences

A target may offer several independently available actions with stable IDs. Shareable metadata lives in
a definition; the occurrence and its executor remain Nodes with per-instance scene context. Labels are
presentation, never identity.

### AD-03 — Exactly one executor owns the command

The V0 event-broadcast execution model could not tell which subscriber performed gameplay or whether two
subscribers mutated it twice. An action now has one explicit executor. Lifecycle signals are past-tense
notifications for UI, VFX, quests or analytics.

### AD-04 — World state was extracted from Interaction

The old interaction-specific lifecycle enum could not represent doors, rooms, power systems and other
world facts. `StatefulComponent` now owns arbitrary world truth; Interaction only queries or mutates it
through explicit adapters.

### AD-05 — Mutation completes before external dispatch

Core mutation follows `validate/compute → mutate → dispatch`. Signals, RPCs and cross-node callbacks do
not define partially-mutated states. Rules stay pure and repeatable. This also keeps the conceptual
model portable to implementations with stricter borrow/reentrancy constraints.

### AD-06 — Candidate source is replaceable, selection is shared

Area, proximity and aim interactions are not separate interaction frameworks. They are different sources
feeding the same detection tiers and focus selection. This keeps game-specific feel replaceable without
forking request validation or presentation.

### AD-07 — Server validates a target, not the client's candidate source

A client may use casts or local overlap sets to discover candidates, but the authority validates the
requested target with the same tolerant predicate. Replaying a binary client cast on a one-ping-old
server transform would create latency-only refusals.

### AD-08 — LOS is an occluder predicate

Visibility is shared by every detector and authored on blocking geometry through a dedicated layer. A
small hysteresis belongs to the predicate itself so every detection model gets stable behavior and
server validation uses the same semantics.

### AD-09 — Availability is per action; focus remains per target

`Allowed | Blocked | Hidden` is evaluated for each action. A target whose actions are all hidden is not a
focus candidate; a blocked action may still be shown and explained. Interaction does not add a separate
target-level allowed/blocked state.

### AD-10 — Target rules precede action rules, concurrency comes last

Common target and offer policy is evaluated directly before the resolved generic action rules. The
legacy target-rule adapter remains only for `InteractionAction` migration. Busy state is applied only
after the rules, so concurrency never makes a deliberately hidden action resurface.

### AD-11 — Hold selects; execution runs

Input gesture handling moved to `GameplayActionRunner`. Hold thresholds arbitrate bindings on a press;
they are not gameplay deadlines. Long-running execution and optional progress are generic execution
concerns.

### AD-12 — Explicit topology replaced migration bridges

The final architecture removed implicit compatibility installation and legacy aliases. Scenes author the
generic host/runner and Interaction adapters they use. This makes ownership visible and keeps relative
NodePaths stable. More ergonomic composition inference remains future authoring polish, not hidden runtime
magic.

### AD-13 — Self/other busy policy uses local execution relation

A requester may want its prompt hidden while observers still see “someone else is using this”. Rather
than creating a new availability model or network field, executions expose local
`RequestedLocally | Observed` relation and each action chooses the busy outcome for self and other.

### AD-14 — Generic action presentation, Interaction target presentation

Action availability, input gesture and execution presentation belong to Gameplay Action. Interaction
adds only the spatial target shell, projection and indication. Owned actions and interactions therefore
share action widgets without leaking Interaction types into the generic presenter.

### AD-15 — Generic instigator is the integration seam

Interaction resolves an `InteractionInteractor` from the generic gameplay instigator through one
internal resolution seam. The current descendant traversal is an implementation choice and may later be
replaced by caching/registration without changing rules or executors. The scene invariant is at most one
interactor per gameplay instigator.

### AD-16 — Offers describe invocations, not ownership

`InteractiveComponent.Offers` resolves either a target-owned or instigator-owned `GameplayAction`
occurrence. The offer owns contextual input binding, target-side rules and invocation target data;
focus never grants, clones or transfers the action.

### AD-17 — Authority reconstructs the offer endpoint

Interaction access validation compares the requested component/action/target against the authoritative
offer that maps to it, then repeats spatial, target and offer rules. Local bindings and client target
paths express intent only.

### AD-18 — Target claims are offer-scoped and transient

`TargetConcurrencyGroup` coordinates offers on one Interactive independently from the resolved action
owner's host concurrency. The claim is an authority-side request lease, projected through an optional
versioned snapshot for self/other availability and late join; it is not persistent world state.

### AD-19 — Offer endpoint resolution is unambiguous per interactor

An Interactive cannot rely on first-match ordering to distinguish two offers that resolve to the same
action endpoint for one interactor. Authored duplicates within one source are reported by editor
validation, and the runtime resolver rejects any dynamic duplicate endpoint before focus or authority
request validation can use it. The requester protocol remains unchanged and carries no OfferId.

## Remaining planned work

The only Interaction-core design document that is still intentionally planned is
[`interaction-authoring-polish.md`](planned/interaction-authoring-polish.md). It proposes composition-first
Inspector ergonomics and small Stateful authoring helpers without changing the lifecycle above.

State consequence prediction is a separate Stateful concern and remains proposed in
[`../state/planned/stateful-presentation.md`](../state/planned/stateful-presentation.md).

## Current hardening note

`InteractionPresenter` deliberately pulls fresh presentation every frame so rules, distance and progress
stay current without turning signals into per-frame streams. That keeps the notification contract clean,
but `GetPresentation()` still rebuilds snapshots and re-evaluates rules; optimize only if profiling shows
this allocation/query cost matters.
