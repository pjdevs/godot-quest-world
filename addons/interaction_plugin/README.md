# Interaction Plugin

A composable Godot 4.7 C# interaction framework for offline and authoritative multiplayer games.

## Philosophy

Interaction separates five questions:

1. A **detector** finds and ranks possible targets.
2. An **interactive** declares what a target offers.
3. A **rule** answers whether one action is allowed, blocked, or hidden.
4. One **executor** performs the accepted gameplay mutation.
5. A **presenter** renders local, replaceable UI.

The owning client predicts focus and prevalidates an intention. The server resolves the target and action again, rechecks detection and rules, reserves the target, then calls exactly one executor. Signals only report what already happened.

> A rule describes a world condition. A running execution describes who is engaged with a target right now.

Do not set and clear an “is interacting” flag from an executor and read it from a rule. That duplicates the built-in reservation and leaks when a player disconnects, releases a sustained input, or leaves range.

## Quick start

Enable `interaction_plugin` in **Project Settings > Plugins** for Inspector validation. The runtime creates no autoload and no Input Map action.

### 1. Configure an interactor

Add one `InteractionInteractor` to each character, add a detector below it, and assign `Detector`. Set:

- `OwnerPeerId`: peer that owns focus, input, and presentation.
- `ServerPeerId`: authoritative peer receiving reliable RPCs; normally `1`.
- Detector `ViewOrigin`: camera or aiming transform.
- Detector `InteractionOrigin`: optional body/reach origin; otherwise the nearest `Node3D` ancestor.

The interactor never samples input. Forward the relevant project actions from local character code:

```csharp
foreach (StringName input in _interactor.GetRelevantInputs())
{
    if (Input.IsActionJustPressed(input))
        _interactor.TryStartInteractionInput(input);
    else if (Input.IsActionJustReleased(input))
        _interactor.TryEndInteractionInput(input);
}
```

`GetRelevantInputs()` includes presentable actions of the focused target and any consumed or sustained input still awaiting release. Once a press starts an interaction or hold, another start on that input is ignored until its release.

### 2. Build an interactive target

```text
Door
├── InteractionArea          Area3D; required
│   └── CollisionShape3D
├── IndicationArea           optional wider Area3D
│   └── CollisionShape3D
├── InteractionAnchor        Marker3D
├── Interactive              InteractiveComponent
│   └── OpenAction           InteractionAction
│       └── OpenExecutor     InteractionActionExecutor
└── StatefulComponent        optional world state
```

For the normal authoring path, leave the reference overrides empty: direct `InteractionArea3D`,
`IndicationArea3D`, `InteractionAnchor3D`, and `InteractionAction` children are composed by type. An
action composes its unique direct `InteractionActionExecutor` child. Explicit references remain
available under the `Overrides` Inspector group for nodes outside the local composition or for an
intentional ordering override. Composition never recurses and never uses node names. The composition
is resolved once in `_Ready()` and the runtime keeps the resulting references; `Resolve...` helpers are
for inspection and setup-time queries.

`InteractionAnchor` is the single world point used for distance, focus, LOS, and UI projection. `InteractionArea` is currently required for every target: the area detector consumes its body overlaps, the aim detector casts against its collision shape, and the proximity detector ignores its geometry. Configure collision layers/masks accordingly.

Do not subclass `InteractiveComponent` for gameplay. It has no gameplay hook: compose rules and executors, then use its signals only as notifications. Put target-specific behavior in an executor or an adjacent gameplay node.

### 3. Define an action

An action has two layers:

- `InteractionActionDefinition` is reusable static data: stable `Id`, label, description, input, hold threshold, and release behavior.
- `InteractionAction` is one occurrence on one target: executor, rules, priority, concurrency group, and optional automatic trigger.

Keep `Id` stable across builds because it crosses the network, and declare `InputActionName` in the project Input Map. `HoldThreshold` only selects between actions sharing an input; it is not execution duration. `CancelOnInputReleased` makes a running execution depend on that input.

Actions sharing a `ConcurrencyGroup` are mutually exclusive on their own target. The default group makes all actions of a target exclusive. `Automatic` actions request themselves when focused and do not appear as input prompts.

## Write a rule

Subclass `InteractionRule` when availability depends on gameplay:

```csharp
public partial class HasKeyRule : InteractionRule
{
    public override InteractionAvailability Evaluate(in InteractionContext context)
    {
        return HasKey(context.Interactor)
            ? new InteractionAllowed()
            : new InteractionBlocked("A key is required.");
    }
}
```

Add shared rules to `InteractiveComponent.TargetRules`; add action-specific rules to `InteractionAction.Rules`. Target rules run first, then action rules, stopping at the first non-allowed result.

| Member | Comes from | Called on | Rhythm / constraint |
| --- | --- | --- | --- |
| `Evaluate(context)` | `InteractionRule` | Owning client and authoritative server | Potentially several times per presented action per frame on the client, then again per command on the server. Must be synchronous, gameplay-pure, and cheap. |

Return:

- `InteractionAllowed`: action may be requested.
- `InteractionBlocked(reason)`: keep it visible and explain why.
- `InteractionHidden`: omit it entirely.

Resources may be shared between targets. Never store mutable runtime state in a rule; read nodes or services through `context.Interactor`, `context.Interactive`, and `context.Action`.

### Provided rules

| Rule | Use | Cost / trade-off |
| --- | --- | --- |
| `AlwaysBlockedInteractionRule` | Fixed authored refusal | Constant work; mainly an example and content switch |
| `InteractorGroupInteractionRule` | Require a Godot node group | Constant group lookup; simple but groups are a coarse permission model |
| `StatefulStateInteractionRule` | Match one `StatefulComponent` against expected states | Resolves a `NodePath` and scans `ExpectedStates` on every evaluation; keep the list short. Supports cross-object state without coupling the core. |

## Write an executor

Subclass `InteractionActionExecutor` for the one object that mutates gameplay:

```csharp
public partial class OpenDoorExecutor : InteractionActionExecutor
{
    public override InteractionExecutionResult Execute(
        in InteractionExecutionContext context
    )
    {
        OpenDoor();
        return new InteractionExecutionCompleted();
    }
}
```

| Member | Required | Called on | When |
| --- | --- | --- | --- |
| `Execute(context)` | Yes | Authority only | Once, synchronously, after rules pass and the execution is reserved |
| `TimedInteractionExecutor.ComputeTimedDuration(context)` | No | Authority and owning client | Pure timed-feature query; must return a positive finite duration |
| `RequiresInteractorPresence` | No | Authority | Read after a running result; default `true` |
| `OnExecutionCompleted(context)` | No | Authority only | Once when a previously running execution completes |
| `OnExecutionCancelled(context, reason)` | No | Authority only | Once when a previously running execution is cancelled |
| `OnExecutionFailed(context, reason)` | No | Authority only | Once when a previously running execution fails |

`Execute` returns one of:

- `InteractionExecutionCompleted`: mutation finished now.
- `InteractionExecutionRunning`: keep the reservation. Return the payload-free result through the protected `Running()` helper. A timed executor uses `RunningTimed(context)` to register its authoritative clock and linear presentation sample; a generic executor leaves completion to gameplay.
- `InteractionExecutionRejected(reason)`: nothing started. Use this rarely; ordinary conditions belong in rules.
- `InteractionExecutionFailed(reason)`: it started but failed, so observers receive started then failed.

For an event-driven action such as dialogue, return `Running()`, keep `context.ExecutionId`, and later call `context.Interactive.CompleteExecution(id)`, `CancelExecution(id)` or `FailExecution(id, reason)` from authoritative gameplay.

For timed authoring, inherit `TimedInteractionExecutor` and return `RunningTimed(context)`. When another
executor hierarchy is already required, compose the same policy directly: keep one `TimedExecution`, call
`Start(context.Interactive, context.ExecutionId, duration)` and return `Running()` only when the helper
started. Forward the three terminal callbacks to `TimedExecution.Stop(context.ExecutionId)`. One helper
owns at most one active clock and refuses reuse instead of abandoning its first execution. Its `Start`
result distinguishes an active helper, invalid duration, stale execution, and missing scene tree.

Timed duration is a strict contract: zero, negative, NaN, and infinity fail the accepted execution.
Use `InteractionActionExecutor` (or `TransitionStateInteractionExecutor`) for open-ended gameplay. The
clock uses monotonic real time on authority and presentation peers, so pausing one node's processing
does not give the lifecycle and its extrapolated bar different time semantics.

A timed running action delegates its clock to a composed `TimedExecution`, which publishes sparse linear samples and completes the generic execution on the authority. A presence-bound running action is also revalidated once per server process frame through its detector. Set `RequiresInteractorPresence = false` for work handed to the world; `CancelOnInputReleased` always keeps it presence-bound.

### Provided executors

| Executor | Use | Cost / trade-off |
| --- | --- | --- |
| `SetStateInteractionExecutor` | Apply one state instantly | Constant, event-driven work; fails on a no-op, so pair it with a rule that hides or blocks the already-applied state |
| `TransitionStateInteractionExecutor` | Apply running/completed/cancelled states until gameplay ends the execution | No timer or progress producer; requiring presence adds one detector validation per server frame. Failure restores the cancelled state. |
| `TimedTransitionStateInteractionExecutor` | Apply the same three-state transition with timed completion | Composes `TimedExecution`; exports duration and sparse correction interval. |
| `TimedExecution` | Add authoritative timing to a custom generic executor | Plain composable helper; owns one active execution clock, local derived progress, sparse samples, and automatic completion. |

## Choose or write a detector

A detector is a `Node` because it may own physics children, signals, and frame state. Assign exactly one to each interactor.

| Member | Required | Called on | Rhythm |
| --- | --- | --- | --- |
| `Detect(target)` | Yes | Owning client and server | Once per local candidate per process frame; once per server command; once per server process frame for each presence-bound execution |
| `GetCandidates()` | Yes | Owning client only | Once per process frame; return a reusable, distinct candidate sequence |
| `Score(target)` | No | Owning client only | Once per interactible candidate with a visible action; greatest score wins |
| `OnEnteredTargetArea` / `OnExitedTargetArea` | No | Every peer receiving target area overlaps | Event-driven; useful for area-backed sources |
| `Forget(target)` | No | Every peer | When a target leaves the tree |
| `_PhysicsProcess(delta)` | Only if needed | Every peer where the detector processes | Call `base._PhysicsProcess(delta)` to keep cached LOS current |

Minimal custom source:

```csharp
public partial class MyDetector : InteractionDetector
{
    private readonly HashSet<InteractiveComponent> _candidates = new();

    public override InteractionDetectionKind Detect(InteractiveComponent target) =>
        _candidates.Contains(target) && IsWithinRange(target, 3.0f, 30.0f)
            ? InteractionDetectionKind.Interactible
            : InteractionDetectionKind.None;

    protected internal override IEnumerable<InteractiveComponent> GetCandidates() =>
        _candidates;

    protected internal override void OnEnteredTargetArea(
        InteractiveComponent target,
        InteractionDetectionKind kind
    )
    {
        if (kind == InteractionDetectionKind.Interactible)
        {
            _candidates.Add(target);
        }
    }

    protected internal override void OnExitedTargetArea(
        InteractiveComponent target,
        InteractionDetectionKind kind
    )
    {
        if (kind == InteractionDetectionKind.Interactible)
        {
            _candidates.Remove(target);
        }
    }
}
```

`Detect` is the shared predicate used by client and server, so keep it tolerant of network transform lag. `GetCandidates` is only a local source; the server never needs to reproduce a client-only cast.

The base class provides `IsWithinRange`, `HasLineOfSight`, and default look-plus-distance scoring. With a non-zero `OcclusionMask`, LOS performs an immediate ray on first use, then one ray per recently evaluated target per physics frame. Set the mask to `0` to disable all LOS rays. Only deliberate occluders should carry the configured layer.

### Provided detectors and performance

| Detector | Candidate source and behavior | Performance trade-off | Best fit |
| --- | --- | --- | --- |
| `AreaInteractionDetector` | Target-owned interaction and indication overlaps; authored shapes can express irregular reach. Angle/range still filter focus. | Local scan is **O(overlapped targets)** per process frame. Physics also maintains the target areas and, with LOS enabled, casts up to one ray per recently evaluated overlap per physics frame. Large indication volumes increase both counts. | Production baseline; dense worlds where authored volumes are acceptable |
| `ProximityInteractionDetector` | Walks the static registry of every `InteractiveComponent` in the scene tree; target radii replace overlap shapes. Indication is omnidirectional. | Local scan is **O(all interactives in the scene tree) per locally controlled interactor, every process frame**. Because LOS is checked before distance, a non-zero mask can also cause up to one ray per registered target per physics frame. This scales badly. | Small scenes and quick authoring spikes only |
| `AimInteractionDetector` | One forced sphere shapecast from the view, capped by `MaxHits`; its radius is clamped to at least `0.001`. It reports areas, while LOS handles walls. The server validates with the tolerant window instead of replaying the cast. | One shape query per physics frame, then **O(unique hits)** local evaluation and up to one LOS ray per recent hit. The current implementation has no owner guard, so its shapecast runs on every peer instance that still processes, even though only the owning interactor consumes the list. | Precise crosshair interaction with a small, bounded hit set |

`ProximityInteractionDetector` and `AimInteractionDetector` are spikes with smoke coverage, not hardened contracts. Profile them in the real scene before choosing them; disable remote Aim detector processing explicitly if replicated characters keep one.

## Configure input and execution behavior

When several actions share an input:

1. The hold threshold selects the longest threshold reached.
2. Allowed beats blocked.
3. Higher `Priority` wins.
4. The stable action id breaks the final tie.

A tap may therefore select a zero-threshold action while a hold selects another. For “hold while hacking,” normally use a zero selection threshold, a running executor with a duration, and `CancelOnInputReleased = true`. Combining a hold threshold with execution duration creates two consecutive waits.

The reliable client RPC carries only `targetPath + actionId`. The server checks the owning peer, resolves the target and action from its scene, validates `Detect`, evaluates rules, and only then executes. Do not call the RPC methods directly; use `TryStartInteractionInput` and `TryEndInteractionInput`.

## Build presentation

`InteractionPresenter` is optional. Assign its local `Interactor`, `Camera`, and optional `PromptContainerScene`.

The target supplies `ActionPromptScene` and `IndicationScene`; leaving one unset simply omits that visual.

- A prompt container implements `IInteractionPromptContainer.Bind(targetPresentation)` and exposes `ActionsContainer`.
- One action widget implements `IInteractionActionWidget.Bind(in actionPresentation, executionPresentation?)`.
- A target-level indication implements `IInteractionWidget.Bind(targetPresentation)`.

`Bind` runs locally every presentation frame so hold progress, execution presentation, rules, and projection remain current. Keep it allocation-free and side-effect free. The presenter reuses widget instances; it only recreates them when the target, scene, or presented action count changes.

### Provided presentation

| Implementation | Behavior | Cost / trade-off |
| --- | --- | --- |
| `InteractionPresenter` | Projects one focused container plus one indication per non-focused detected target | Each local process frame rebuilds snapshots, re-evaluates every action of the focused and indicated targets, binds widgets, and projects them: O(total actions across presented targets). Stable frames use this single refresh path; status events no longer trigger a duplicate refresh, and the stale-indication buffer is reused. |
| `InteractionPromptWidget` | Target name and action container | Minimal default; no styling or input glyph resolution |
| `InteractionActionPromptWidget` | Input and label, or blocked reason | Constant bind cost; text-only |
| `InteractionIndicatorWidget` | Target name | Constant bind cost; allowed/blocked appearance comes from the selected scene |

## Technical reference

### Client/server rhythm

| Work | Owning client | Server |
| --- | --- | --- |
| Enumerate and score focus candidates | Every process frame | Never |
| Evaluate rules for visible UI | Every presentation/focus refresh | Listen host only when it also presents |
| Validate a request | Prevalidation | Always, authoritatively |
| Run executor and own reservations | Never | Always |
| Time running executions | Predicts through the executor hook | Composed `TimedExecution` helper |
| Validate sustained presence | Never | Once per running, presence-bound execution per process frame |
| Render widgets | Every local presentation frame | Never on a dedicated server |

Offline and listen-server play take the authoritative path directly. Execution presentation is queried from the
`InteractiveComponent` through one generic record: timed slots extrapolate a linear sample, published slots
carry discrete values, and a local `Callable` has priority when registered. A requester creates a local
`ExecutionId = 0` prediction when the executor exposes an initial sample, then reconciles it with the started
acknowledgement; requester-only corrections use a reliable owner RPC. `InteractionAction.ExecutionVisibility`
selects who receives that transient slot:

- `RequesterOnly` (default) keeps it on the authority and requesting peer;
- `Replicated` publishes it to visible peers through an explicitly wired child
  `InteractionExecutionSynchronizer`, including late joiners;
- `AuthorityOnly` keeps it on the authority while lifecycle acknowledgements still reach the requester.

The synchronizer replicates presentation samples, not execution authority. Keep persistent world truth in a
`StatefulComponent`; use execution replication only for transient, world-observable feedback. Native
`MultiplayerSynchronizer` visibility controls which peers may observe replicated execution slots.

### Notifications

`InteractionActionStarted`, `Completed`, `Cancelled`, `Failed`, and `Rejected` are authoritative notifications on the target. They are never commands. Local interactor signals report focus, presentation invalidation, requests, refusals, and indication changes.

`InteractionStatusChanged` is an event, not a per-frame push: it fires when focus moves, a target enters detection, or gameplay explicitly invalidates its status. A rule changing by itself emits nothing; consumers needing continuous freshness must pull a new presentation snapshot. The provided presenter already does so once per local process frame.

Mutations and reservations are complete before executors or signal listeners run. A started action is followed by exactly one completion, cancellation, or failure; a rejected action never emits started.

### Explicit configuration and validation

Required references are never guessed from parents, siblings, names, or recursive searches. Network requests identify targets with `NodePath`; ordinary scene wiring uses explicit Inspector references, except for the deliberately shareable `StatefulStateInteractionRule`. The editor plugin validates interactors, detectors, targets, actions, definitions, presenters, Stateful rules, and provided Stateful executors; runtime guards remain in place.

### Scope and example

The core is namespaced under `QuestWorld.Interaction` and has no Quest, Inventory, Dialog, Character, Stateful, persistence, or transport abstraction dependency. The optional integration depends on the [`stateful_plugin`](../stateful_plugin/README.md), never the reverse.

[`integration/stateful/examples/LongActionExample.tscn`](integration/stateful/examples/LongActionExample.tscn) is the duplicable reference scene: explicit areas and anchor, default widgets, a replicated `StatefulComponent`, pure state rules, a replicated action with `InteractionExecutionSynchronizer`, and a `TimedTransitionStateInteractionExecutor`, with no script on the scene root. Instant project templates such as Door and Button keep the default requester-only execution presentation because their durable result is already carried by Stateful.
