# Interaction V4 — implementation specification

> **Status: V4 delivered (Impl 1–3).** Cette spec transforme
> [`interaction-v4-architecture.md`](./interaction-v4-architecture.md) en trois tranches de
> réalisation exécutables. Le document d'architecture reste le contrat d'intention ; celui-ci fixe
> les APIs, la migration, le transport réseau, les tests et les critères de fin.
>
> **Impl 3 est aussi la tranche de fermeture de V4.** Elle n'est pas acceptée si le runtime fonctionne
> mais qu'il reste un second modèle V3, un transport transitoire devenu permanent, une ancienne API
> documentée comme courante, un commentaire décrivant l'ancien deadline model, ou un exemple qui
> enseigne encore l'architecture précédente.

## 1. Goal

Livrer V4 sans cut massif et sans état intermédiaire ambigu :

1. **Impl 1 — execution read model foundation** : séparer action et execution, formaliser le slot
   unique, adapter les widgets, conserver temporairement le timing V3 interne ;
2. **Impl 2 — generic progress, timing and requester prediction** : retirer toute sémantique de
   durée du core, introduire le timed helper, les producers de progression et le slot requester ;
3. **Impl 3 — replication, visibility and closeout** : synchroniser les executions world-observable,
   supporter late join, prouver les producers custom et supprimer les dernières traces actives du
   modèle V3.

À la fin des trois tranches :

- `ActionPresentation` ne décrit que ce qu'un interactor peut demander ;
- `ExecutionPresentation` décrit ce que le peer peut observer sur l'Interactive ;
- `Running()` ne transporte aucune durée ;
- `Progress` est optionnel et ne révèle pas sa provenance ;
- le timing est une feature spécialisée et strictement opt-in ;
- l'autorité possède le lifecycle ;
- la visibilité décide seulement qui reçoit le read model ;
- un `Replicated` n'entretient pas deux transports permanents de progress ;
- aucun shim, nom d'API, commentaire ou documentation **active** V3 de durée/progression ne subsiste.

Les documents V2/V3 explicitement historiques peuvent évidemment conserver leur vocabulaire s'ils
sont clairement marqués comme tels. La contrainte porte sur le code livré, les commentaires du code
courant, la documentation utilisateur courante et les proposals encore présentés comme applicables.

---

## 2. Decisions fixed by this spec

| Concern | Decision |
| --- | --- |
| Action read model | Remove `HasTimedExecution` and `ExecutionProgress` |
| Execution read model | `ExecutionId`, `ActionId`, nullable `Progress` |
| Prediction flag | No public `IsPredicted` |
| Prediction identity | `ExecutionId = 0` is reserved internally for an unconfirmed local slot |
| Query ownership | Separate queries on `InteractiveComponent` |
| Widget composition | Presenter joins action and execution by `ActionId` |
| Cardinality | One active or pending slot per `ActionId` and Interactive |
| Completion | Remove presentation immediately; observing `Progress = 1` is not guaranteed |
| Published progress | Authority reports nullable normalized snapshots by `ExecutionId` |
| Derived progress | Local Godot `Callable`; no mandatory C# interface |
| Timed progress | Linear local extrapolation from sparse progress samples |
| Timed authoring | A timed executor requires a finite positive duration; open-ended work uses generic `Running()` |
| Timed clock | Authority timeout and presentation extrapolation use one consistent monotonic time policy |
| Visibility authoring | Per `InteractionAction`, default `RequesterOnly` |
| Requester transport | Existing reliable owner RPC channel, only for requester-only presentation after start |
| World transport | Dedicated `MultiplayerSynchronizer`, reliable on-change snapshot |
| Replicated requester | ACK may seed/reconcile immediately; synchronizer owns later progress corrections |
| Interest management | Native synchronizer visibility/filter APIs |
| Correlation | `(target, ActionId)` while pending; authoritative `ExecutionId` after ACK |
| Failure after start | `FailExecution(executionId, reason)` |
| Public terminal contract | `Completed`, `Cancelled`, or `Failed`; never a terminal progress value |
| V4 closeout | Current docs/comments/examples contain no live V3 execution-duration model |

---

## 3. Final public model

### 3.1 Action presentation

```csharp
public readonly record struct InteractionActionPresentation(
    StringName ActionId,
    string Label,
    string Description,
    StringName InputActionName,
    InteractionAvailability Availability,
    bool IsAutomatic = false,
    bool IsHoldable = false,
    float? HoldProgress = null,
    float? HoldElapsed = null
);
```

It contains no execution membership, progress, duration, deadline or timed capability.

`HoldProgress` remains action/interactor presentation because hold is a local selection gesture. It is
not execution progress and must never be merged with it.

### 3.2 Execution presentation

```csharp
public readonly record struct InteractionExecutionPresentation(
    ulong ExecutionId,
    StringName ActionId,
    float? Progress = null
);
```

Rules:

- absence from the collection means no observable active execution ;
- `Progress = null` means the execution has no generic presentable progress ;
- a non-null progress is clamped to `[0, 1]` ;
- `ExecutionId = 0` is used only by an unconfirmed local requester slot ;
- renderers treat `ExecutionId` as opaque and do not infer a predicted visual from zero ;
- a confirmed execution always has a non-zero authority-allocated identifier ;
- the record has no terminal state: it disappears on completion, cancellation or failure ;
- nothing in this record reveals whether progress is timed, discrete or gameplay-derived.

### 3.3 Execution visibility

```csharp
public enum InteractionExecutionVisibility
{
    AuthorityOnly,
    RequesterOnly,
    Replicated,
}
```

`InteractionAction` exposes:

```csharp
[Export]
public InteractionExecutionVisibility ExecutionVisibility { get; set; }
    = InteractionExecutionVisibility.RequesterOnly;
```

The property belongs to the action occurrence, not the shared definition or executor. Two targets
using the same reusable action definition may therefore expose their transient execution differently.

Visibility controls **presentation membership**, never command authority or lifecycle ACK delivery.

### 3.4 Interactive queries

```csharp
public IReadOnlyList<InteractionExecutionPresentation> GetExecutionPresentations();

public bool TryGetExecutionPresentation(
    StringName actionId,
    out InteractionExecutionPresentation presentation
);
```

The returned list is a fresh immutable-facing snapshot ordered by `InteractiveComponent.Actions`, not
by start time. Deterministic action order keeps prompts, replication and tests stable.

`GetPresentation(interactor, isFocused)` continues returning action presentation only.

### 3.5 Presentation invalidation

```csharp
[Signal]
public delegate void ExecutionPresentationChangedEventHandler(StringName actionId);
```

It is emitted locally when the visible slot is created, receives a published/synchronized snapshot,
changes authoritative identity, changes discrete state, or disappears. It is not emitted every frame
for locally derived continuous progress.

A continuous renderer pulls from `_Process`; an event-driven renderer pulls after the signal.

### 3.6 Widget composition

```csharp
public interface IInteractionActionWidget
{
    void Bind(
        in InteractionActionPresentation action,
        InteractionExecutionPresentation? execution
    );
}
```

`InteractionPresenter` performs the deterministic join:

```text
for each action in target presentation
    execution = target.TryGetExecutionPresentation(action.ActionId)
    widget.Bind(action, execution?)
```

The default widget is not forced to draw an execution bar. Game-specific widgets may use the second
argument. World-space consumers query the Interactive directly and need no interactor or presenter.

---

## 4. Runtime model and invariants

### 4.1 Authoritative execution and local presentation are separate

Keep two concepts:

```text
AuthoritativeExecution
    server/offline gameplay reservation
    Interactor + Action node references
    ConcurrencyGroup
    lifecycle ownership

LocalExecutionPresentationSlot
    per-peer presentation state keyed by ActionId
    authoritative, requester or replicated origin
    optional progress state
```

Prediction never enters the authoritative execution collection. Replication never gains gameplay
mutation methods. The public query projects local presentation slots.

On authority/offline, every active authoritative execution has a local presentation slot regardless
of network visibility. On a remote peer, only requester or replicated visibility creates one.

### 4.2 Slot cardinality

For one Interactive:

```text
ActionId -> zero or one authoritative execution
ActionId -> zero or one local presentation slot
```

Before reserving, authority checks in this order:

1. configuration, target rules and action rules ;
2. active execution with the same `ActionId` ;
3. active execution in the requested concurrency group.

Action uniqueness and concurrency groups remain orthogonal:

```text
Hack + Hack       -> impossible: same ActionId
Hack + Repair     -> depends on concurrency group
Hack + Inspect    -> possible when groups differ
```

A cooperative `Repair` is one execution/session with several participants owned by gameplay, not
several independent `Repair` executions.

### 4.3 Lookup implementation

The authoritative collection may remain a small list. Direct helpers by `ExecutionId`, `ActionId` and
concurrency group are enough; do not introduce a list of executions per action.

Presentation keeps one dictionary keyed by `ActionId` plus deterministic projection through authored
action order. A replicated action id absent from local authoring is ignored with one warning.

Authority allocation reserves `0` for prediction and stays within `1..long.MaxValue`, the lossless
Godot Variant transport range. Identifiers are not wrapped/reused within a session.

### 4.4 Lifecycle

Final core result union:

```text
Completed
Running
Rejected(reason)
Failed(reason)
```

`Running` has no payload. Long-running terminal APIs are:

```csharp
bool CompleteExecution(ulong executionId);
bool CancelExecution(ulong executionId, string reason = "");
bool FailExecution(ulong executionId, string reason);
```

All three are authority-only, return `false` for stale/unknown identifiers, release the slot before
external callbacks, and produce exactly one terminal notification.

Executor callbacks:

```csharp
OnExecutionCompleted(context)
OnExecutionCancelled(context, reason)
OnExecutionFailed(context, reason)
```

Failure is distinct from cancellation in authority signals and requester ACKs. Immediate
`InteractionExecutionFailed` is still accepted-then-failed: `Started` then `Failed`, never `Rejected`.

### 4.5 Terminal progress

Completion removes the slot immediately. `Progress = 1` is not a lifecycle prerequisite.

```text
ReportProgress(.66)
CompleteExecution(id)
```

is valid. Renderers may see `.66` followed directly by absence. Completion animation comes from slot
removal or terminal lifecycle, never from comparing progress to one.

---

## 5. Progress production

### 5.1 Published snapshots

```csharp
public bool ReportExecutionProgress(ulong executionId, float? progress);
```

Semantics:

- authority/offline only ;
- `null` clears the published value ;
- finite values clamp to `[0, 1]` ;
- `NaN` and infinities fail with warning ;
- stale ids return `false` safely ;
- unchanged normalized values are no-ops ;
- a linear timed producer cannot be overwritten by a published producer ;
- the method changes presentation only, never gameplay truth or lifecycle ;
- network routing follows `ExecutionVisibility`.

Use it for discrete/event-driven progress such as:

```text
0 -> .33 -> .66 -> 1
```

It is not a supported per-frame streaming API.

### 5.2 Derived local source

```csharp
public bool SetExecutionProgressSource(ulong executionId, Callable source);
public bool ClearExecutionProgressSource(ulong executionId);
```

The source is local presentation state. It returns `Nil` or a numeric normalized value. `Callable` is
chosen instead of a mandatory C# interface so C#, GDScript and future GDExtension producers can bind
naturally.

Rules:

- one source per confirmed execution ;
- `ExecutionId = 0` uses the internal action-aware prediction path instead ;
- invalid/freed callable is cleared ;
- exception/non-numeric/non-finite values warn once and fall back ;
- source lifetime ends with the slot ;
- source state itself never replicates ; the owning gameplay system synchronizes whatever it derives
  from ;
- polling happens only while presentation is queried.

### 5.3 Progress-state encapsulation

Linear extrapolation is a **presentation transport concern**, not Interaction lifecycle semantics.
Do not let `InteractiveComponent` regrow a timer model through raw timing fields and branching spread
across its lifecycle code.

The preferred internal shape is conceptually:

```text
InteractionExecutionPresentationSlot
    ExecutionId
    ActionId
    ProgressState

InteractionExecutionProgressState
    optional PublishedSnapshot
    optional LocalCallable
    optional LinearSample
    Revision
    Resolve(now)
```

The exact class/struct split is internal, but these invariants are fixed:

- lifecycle code does not reason about duration/deadline/elapsed ;
- linear sample resolution is encapsulated in presentation/progress machinery ;
- transport revision ordering is owned next to progress transport state ;
- renderers still receive only nullable normalized `Progress`.

Resolution order remains:

```text
valid local Callable
    else extrapolated transport sample
    else published snapshot
    else null
```

### 5.4 Internal linear sample

Timed presentation transports:

```text
ProgressBase
ProgressPerSecond
SampleRevision
```

A receiving peer records receipt time and resolves:

```text
clamp(ProgressBase + ProgressPerSecond * localSecondsSinceReceipt, 0, 1)
```

A strictly newer revision wins. ACK and synchronizer may arrive in either order without rewinding the
same `(ActionId, ExecutionId)` slot.

---

## 6. Timed execution feature

### 6.1 Base executor cleanup

`InteractionActionExecutor` has no:

- `ComputeInteractionDuration` ;
- `RunningForDuration` ;
- `RunningUntilCompleted` compatibility helper ;
- duration/deadline presentation contract.

It keeps payload-free:

```csharp
protected static InteractionExecutionResult Running();
```

`InteractiveComponent` owns no authoritative `Duration`, `Elapsed`, timer `_Process`, deadline or
`TryGetExecutionProgress` API.

### 6.2 TimedExecution and TimedInteractionExecutor

`TimedExecution` is a composable policy, not another Interaction execution. It owns:

```text
finite positive duration
authoritative timing anchor
sparse linear presentation samples
automatic CompleteExecution(executionId)
cleanup on every terminal path
```

`TimedInteractionExecutor` is the author-facing inheritance shortcut and composes one helper.

```csharp
[GlobalClass]
public abstract partial class TimedInteractionExecutor : InteractionActionExecutor
{
    [Export]
    public float Duration { get; set; }

    [Export]
    public float CorrectionInterval { get; set; } = 0.5f;

    public virtual float ComputeTimedDuration(in InteractionContext context);

    protected InteractionExecutionResult RunningTimed(
        in InteractionExecutionContext context
    );
}
```

### Strict timed contract

A timed path means timed work. `ComputeTimedDuration()` must return a finite positive value.

```text
finite duration > 0
    -> start TimedExecution
    -> Running()

0 / negative / NaN / infinity
    -> configuration/runtime failure
    -> no silent fallback to an open-ended timed execution
```

Open-ended or externally completed work uses `InteractionActionExecutor` / generic `Running()` (for
example `TransitionStateInteractionExecutor`). A zero-duration `TimedInteractionExecutor` must not be
a hidden compatibility path back to V3 semantics.

The validator reports invalid authored durations. Runtime still validates computed/custom durations.

### 6.3 TimedExecution start result

The helper must not collapse all failed starts into one boolean meaning "already active". Internal
start result distinguishes at least:

```text
Started
AlreadyActive
InvalidExecution
InvalidDuration
```

Exact type is private/internal. The purpose is correct failure handling and diagnostics, not public API.

A stale/invalid target or execution must never be reported as "timed executor already active".

### 6.4 One clock policy

Authority timeout and client/remote presentation extrapolation must use the same elapsed-time
semantics. Do not accumulate authoritative `process delta` while remote peers extrapolate an unrelated
wall-clock basis.

V4 default direction:

```text
monotonic elapsed time owns timing
ProcessFrame is only a wake-up/check mechanism
sparse samples re-anchor remote presentation
```

If pause-sensitive game-time timers are needed later, they belong to the timed feature as an explicit
clock policy. Interaction core still does not gain time semantics.

### 6.5 Timing correction

Authority publishes a linear sample at start and every `CorrectionInterval` while active. Default
`0.5 s` bounds drift without per-frame float traffic.

Server alone decides completion. A remote peer reaching visual `1` cannot complete gameplay.

For `Replicated`, `CorrectionInterval <= 0` is invalid because late join/current peers would not get
bounded correction. The validator reports it. Requester-only may choose looser correction policy if
explicitly allowed by the timed helper.

---

## 7. Requester prediction and ACK reconciliation

### 7.1 Generic prediction hook

`InteractionActionExecutor` exposes only an internal producer-agnostic prediction hook. Generic
executors return none. Timed executors can provide an initial linear sample from their pure duration
query.

The interactor never type-tests `TimedInteractionExecutor`.

### 7.2 Pending slot

Every remote request records:

```text
(target, ActionId)
```

as a pending marker even if no visible prediction exists. If the executor supplies a prediction
sample, the target additionally exposes a local slot:

```text
ActionId
ExecutionId = 0
Progress = locally derived prediction
```

One pending/confirmed slot exists per action. Different actions may predict concurrently when their
concurrency groups permit it.

### 7.3 ACK reconciliation

Public requester lifecycle remains:

```text
InteractionStarted(interactive, actionId, executionId)
InteractionCompleted(...)
InteractionCancelled(...)
InteractionFailed(...)
InteractionRejected(...)
```

No public duration/deadline returns.

Started ACK may carry an **internal optional progress sample** to immediately reconcile requester
presentation. This is presentation transport data, not an execution deadline contract.

Rules:

- rejection removes only matching pending prediction ;
- accepted running replaces `ExecutionId = 0` with authority id ;
- non-progress running may expose `Progress = null` depending on visibility ;
- terminal ACK removes only matching authority id ;
- stale terminal ACK cannot remove a newer execution in the same action slot ;
- linear reconciliation preserves already visible progress and adjusts remaining extrapolation rather
  than visibly rewinding.

### 7.4 Visibility-aware requester behavior

Lifecycle ACK is always delivered to the requester, including `AuthorityOnly`. Presentation slot
creation is visibility-dependent:

```text
AuthorityOnly
    lifecycle ACK yes
    requester execution slot no
    progress sample transport no

RequesterOnly
    lifecycle ACK yes
    requester execution slot yes
    initial sample in ACK when available
    later published/timed corrections via targeted owner RPC

Replicated
    lifecycle ACK yes
    requester prediction/ACK slot yes for immediate responsiveness
    initial sample in ACK when available
    later progress corrections through InteractionExecutionSynchronizer only
```

**Do not keep the requester progress RPC active for a `Replicated` execution after start.** ACK and
synchronizer are allowed to overlap only as a reconciliation boundary: ACK gives immediate local
confirmation; replicated current state then becomes the presentation transport. Revisions make the
merge idempotent and monotonic.

Terminal requester ACK may remove the local slot before the replicated snapshot disappearance arrives.
The later synchronizer absence must be idempotent, not recreate or double-notify the slot.

---

## 8. Replication and visibility

### 8.1 InteractionExecutionSynchronizer

Add a `[GlobalClass]` node derived from `MultiplayerSynchronizer`:

```text
InteractionExecutionSynchronizer
    explicit InteractiveComponent reference
    self-owned replicated snapshot property
    SceneReplicationConfig: spawn + OnChange
```

It owns transport only and exposes no gameplay mutation.

The synchronized property is Variant-safe, rooted on the synchronizer itself and contains no Object,
Resource, instance id or RID. The replication-only property is hidden from ordinary Inspector
authoring.

An Interactive requires this node only if at least one action is `Replicated`. Missing/mistargeted
configuration is diagnosed; gameplay authority still functions without world presentation replication.

Native `MultiplayerSynchronizer` visibility/filter APIs remain the interest-management mechanism.
Interaction does not build a second interest system.

### 8.2 Snapshot wire shape

Each replicated entry carries current presentation state only:

```text
action_id            StringName
execution_id         signed 64-bit encoding of authority ulong
progress_present     bool
progress_base        float
progress_per_second  float
revision             signed 64-bit integer
```

Requester-only and authority-only entries are never included.

Collection order follows action declaration order. It is rebuilt on structural change, discrete
published progress change or sparse timed correction. On-change replication is reliable; spawn sync
provides current state to late joiners.

Applying a snapshot diffs by `(ActionId, ExecutionId)`. Older revisions are ignored. Network payload
never creates missing local authoring data.

### 8.3 Visibility behavior

| Mode | Authority/offline | Requester client | Other visible clients | Progress transport after start |
| --- | --- | --- | --- | --- |
| `AuthorityOnly` | slot visible | no slot | no slot | none |
| `RequesterOnly` | slot visible | slot visible | no slot | targeted owner RPC |
| `Replicated` | slot visible | ACK slot -> merged sync slot | slot visible | synchronizer |

For listen host, authoritative local presentation is the source. Do not replay RPC/synchronizer state
onto the same instance in a way that emits duplicate structural changes.

For `Replicated`, synchronizer state merging into the requester slot must never create a second slot or
reset progress backwards.

### 8.4 Late join

A late join receives directly:

- active replicated membership ;
- current discrete published progress ;
- latest timed linear sample ;
- no historical start/progress events ;
- future current-state changes and terminal disappearance.

Late join must not depend on requester ACK history, Stateful, focus, detection or interaction areas.

---

## 9. Three implementation slices

### 9.1 Impl 1 — read model foundation (delivered)

Delivered responsibilities:

- `InteractionExecutionPresentation` introduced ;
- execution/timing data removed from action presentation ;
- `ActionId -> 0..1 execution` enforced at runtime ;
- concurrency between different action ids retained ;
- execution lookup and widget two-model binding introduced ;
- V3 timing kept temporarily internal only for the migration tranche.

### 9.2 Impl 2 — progress, timing and requester (delivered)

Delivered responsibilities:

- payload-free `Running()` ;
- duration/elapsed removed from authoritative Interaction execution ;
- published and Callable progress paths ;
- generic local progress sample model ;
- `FailExecution` lifecycle ;
- composable `TimedExecution` and author-facing timed executor ;
- non-timed `TransitionStateInteractionExecutor` plus timed specialization ;
- prediction moved from one interactor float to target/action execution slots ;
- duration removed from public requester lifecycle contract ;
- requester-only sample update channel established.

Impl 3 may refactor internals delivered here, but must not restore any V3 semantic dependency.

### 9.3 Impl 3 — replication, visibility, proofs and closeout

#### Runtime/network scope

- add `InteractionExecutionSynchronizer` ;
- add `InteractionExecutionVisibility` to action authoring, default `RequesterOnly` ;
- implement Variant snapshot encoding and diff application ;
- enable `Replicated` membership/progress ;
- integrate native peer visibility/filtering ;
- route published and timed corrections by visibility ;
- keep targeted progress RPC **only** for `RequesterOnly` ;
- make `AuthorityOnly` ACK lifecycle without requester presentation slot ;
- merge requester predicted/ACK slot with later replicated current state ;
- make terminal ACK + later replicated absence idempotent ;
- ensure revision ordering prevents ACK/sync rewind.

#### Timed/progress closeout scope

- enforce finite positive duration for timed executors ;
- remove zero-duration/non-finite fallback to generic open-ended timing ;
- make `TimedExecution.Start` diagnostics distinguish failure causes ;
- use one consistent monotonic elapsed-time policy for authoritative timeout and sample extrapolation ;
- keep sparse correction out of core lifecycle ;
- encapsulate linear/published/source resolution so `InteractiveComponent` lifecycle does not regrow
  timer-specific state-machine logic.

#### Real producer proofs

Add at least:

- a discrete three-step process publishing `0/.33/.66` (and optionally `1`) ;
- a fake replicated `HackSession` whose local `Callable` derives progress from gameplay-owned state ;
- a world feedback consumer querying the Interactive directly ;
- a prompt proof binding action + matching execution ;
- a shared fake repair/session process with participants outside Interaction but one shared execution
  progress.

These are architecture proofs, not production gameplay systems.

#### Documentation and migration closeout scope

Impl 3 explicitly owns cleanup of **current-facing** documentation and comments:

- update `addons/interaction_plugin/README.md` ;
- update `docs/feature/interaction/interaction.md` to the delivered V4 model ;
- update XML docs/comments in `InteractiveComponent`, `InteractionInteractor`, executor/progress code
  so they describe samples/slots rather than V3 duration/deadline prediction ;
- update or mark [`presentation-progress-and-distance.md`](./presentation-progress-and-distance.md)
  **Superseded by V4**; it must no longer claim `ExecutionProgress`, `HasTimedExecution`,
  `TryGetExecutionProgress` or `ComputeInteractionDuration` are current APIs ;
- update [`../../state/planned/stateful-presentation.md`](../../state/planned/stateful-presentation.md)
  wherever it describes Interaction prediction through the old duration API ;
- ensure current examples use `TimedInteractionExecutor` / composed `TimedExecution` or generic
  `Running()`, never a compatibility helper ;
- mark V4 proposal/spec status delivered when final acceptance passes ;
- historical V2/V3 documents may retain old API names only when visibly historical/superseded.

No deprecation shim is required for this internal project migration. Old V3 APIs are deleted rather
than forwarded.

#### Impl 3 acceptance tests

Network/presentation:

- replicated start/end reaches requester and another visible peer once ;
- requester-only and authority-only entries never leak into replicated snapshot ;
- `AuthorityOnly` requester receives lifecycle ACK but no execution presentation ;
- `RequesterOnly` uses owner progress RPC and no synchronizer entry ;
- `Replicated` uses started ACK for immediate reconcile then synchronizer for subsequent progress ;
- no `ClientInteractionProgress`-style correction is sent for a replicated execution after start ;
- native synchronizer visibility hides/shows the full replicated target snapshot ;
- requester ACK plus sync state merges into one slot ;
- stale sync revision cannot rewind newer ACK/sync state ;
- terminal ACK followed by replicated absence does not double-remove/double-notify ;
- late join sees active membership and current discrete progress ;
- late join sees extrapolating timed progress near authority value ;
- two different actions in separate groups replicate concurrently ;
- same action still cannot run twice ;
- world feedback works outside every interaction detection area ;
- listen host emits each structural presentation change once ;
- dedicated server owns timeout/lifecycle without UI.

Progress/timing:

- timed correction is sparse, never per-frame network progress ;
- timed executor rejects/diagnoses zero, negative and non-finite durations ;
- generic open-ended executor remains valid with `Running()` and `Progress = null` ;
- custom discrete and gameplay-derived progress bind identically to timed progress ;
- final completion may follow `.66` directly without requiring a reported `1` ;
- timeout cannot double-complete after cancel/fail ;
- timed authority/sample clock semantics do not diverge when process wake-up is delayed.

Architecture proofs:

- world consumer reads `InteractiveComponent` directly without interactor/presenter ;
- prompt joins action + execution by `ActionId` ;
- one shared repair/session owns participants outside Interaction while one execution exposes shared
  progress ;
- no renderer branches on `TimedInteractionExecutor` or producer type.

---

## 10. Migration and compatibility

### After Impl 1

- source-breaking presentation split complete ;
- old timer temporarily internal ;
- remote requester temporarily lacked new generic execution slot.

### After Impl 2

- duration left core result/storage/public lifecycle ;
- requester presentation restored through execution slots ;
- timed authoring moved to specialized helper/base ;
- old duration/prediction APIs deleted rather than deprecated.

### After Impl 3

There is one model only:

```text
ActionPresentation
ExecutionPresentation
Generic execution lifecycle
Optional generic Progress
Optional TimedExecution policy
Visibility-aware requester or replicated transport
```

Specifically:

- `Replicated` is functional and late-join-safe ;
- execution presentation does not depend on Stateful ;
- no timer-specific public presentation field exists ;
- no renderer type-checks timed producers ;
- no core execution duration/deadline/elapsed exists ;
- no zero-duration timed compatibility path exists ;
- no duplicated persistent progress transport exists for `Replicated` ;
- no V3 compatibility shim remains ;
- no current-facing documentation or comment teaches the V3 model.

---

## 11. Files expected to change

Primary runtime:

- `addons/interaction_plugin/runtime/InteractionTypes.cs` ;
- `addons/interaction_plugin/runtime/interactive/InteractiveComponent.cs` ;
- `addons/interaction_plugin/runtime/interactor/InteractionInteractor.cs` ;
- `addons/interaction_plugin/runtime/actions/InteractionAction.cs` ;
- `addons/interaction_plugin/runtime/actions/InteractionActionExecutor.cs` ;
- `TimedExecution.cs` ;
- `TimedInteractionExecutor.cs` ;
- new `InteractionExecutionSynchronizer.cs` ;
- optional internal progress-state helper extracted beside presentation/runtime code.

Integration and presentation:

- `TransitionStateInteractionExecutor.cs` ;
- `TimedTransitionStateInteractionExecutor.cs` ;
- `InteractionPresenter.cs` ;
- `IInteractionActionWidget.cs` ;
- relevant example scenes/consumers.

Validation and tests:

- `InteractionValidator.cs` ;
- `InteractionConfigurationTest.cs` ;
- `InteractionBehaviorTest.cs` ;
- `InteractionAckTest.cs` ;
- `InteractionNetworkTest.cs` ;
- `InteractionSceneTest.cs` ;
- focused producer/world-feedback helpers.

Documentation closeout:

- `addons/interaction_plugin/README.md` ;
- `docs/feature/interaction/interaction.md` ;
- `docs/feature/interaction/planned/presentation-progress-and-distance.md` ;
- `docs/feature/state/planned/stateful-presentation.md` ;
- this implementation spec ;
- V4 architecture status if needed ;
- any current XML docs/comments found by the legacy sweep.

---

## 12. Verification contract

After each code tranche, from repository root on Windows:

```powershell
csharpier format .
dotnet build
$env:GODOT_BIN = (Get-Command godot).Source
dotnet test
```

Additionally for Impl 3:

- run the real in-process server / two-client / late-join suite while iterating ;
- run focused requester-only, authority-only and replicated transport tests ;
- run focused timed/discrete/Callable producer tests ;
- final acceptance is always the full command sequence above.

### Legacy sweep

Before marking V4 delivered, search current-facing code/docs for obsolete V3 symbols:

```powershell
rg -n "ComputeInteractionDuration|RunningForDuration|RunningUntilCompleted|HasTimedExecution|TryGetExecutionProgress|PredictedExecution|ExpectedDuration" `
  addons/interaction_plugin `
  quest_world `
  addons/interaction_plugin/README.md `
  docs/feature/interaction/interaction.md `
  docs/feature/interaction/planned/presentation-progress-and-distance.md `
  docs/feature/state/planned/stateful-presentation.md
```

Expected result: **no current API description or executable code reference**. A superseded document may
mention an old symbol only inside an explicit historical explanation/banner pointing to V4.

Also inspect wording rather than symbols only: requester comments must describe generic progress
samples/reconciliation, not an "execution duration/deadline carried by ACK" model.

---

## 13. Performance and safety constraints

- no progress RPC or synchronized property write per render frame ;
- timed corrections default to at most two writes per second per active timed execution ;
- replicated requester gets no duplicate targeted correction stream ;
- full replicated collection rebuild is acceptable because an Interactive has few action slots ;
- all network payloads use action ids and execution ids, never Node instance ids ;
- received target paths resolve relative to `SceneMultiplayer.RootPath` ;
- every authority guard treats peerless/offline game as authority ;
- synchronizers enter test subtrees only after their MultiplayerAPI is attached ;
- replicated property paths are relative to synchronizer `root_path` and scene-tested ;
- stale ids cannot mutate, complete, cancel, fail or update a newer occurrence ;
- current-state replication, not missed transitions, drives late join ;
- public/core lifecycle contains no timer-specific branching ;
- timed helper never silently converts invalid timing configuration into generic open-ended work.

---

## 14. Final definition of done

V4 is complete only when all of the following are true.

### Architecture

- every architecture success scenario has an automated test or named proof example ;
- action and execution read models remain separately queryable ;
- `ActionId -> 0..1 execution` is enforced ;
- concurrency groups still govern different actions ;
- generic `Running()` works indefinitely without a timer ;
- timing remains optional policy, not core execution semantics ;
- same renderer consumes timed, published and gameplay-derived progress without producer type branch.

### Networking

- visibility modes work on offline, listen host, remote client and dedicated server ;
- requester lifecycle ACK is independent from presentation visibility ;
- replicated execution current state is late-join-safe ;
- replicated requester transitions from ACK-seeded presentation to synchronizer state without duplicate
  slot, rewind or permanent duplicate progress transport ;
- remote world feedback requires no focus, indication or interaction overlap.

### Timing/progress

- timed executor accepts only valid positive timing configuration ;
- authoritative timeout and local extrapolation share one documented clock policy ;
- sparse timed samples are not per-frame writes ;
- published `.33/.66/...` progress is first-class ;
- progress `1` is never required to terminate an execution ;
- gameplay-owned progress remains gameplay truth even when Interaction presents it.

### Migration hygiene

- V3 duration/prediction APIs are deleted, not deprecated or forwarded ;
- no current code comment describes the obsolete duration/deadline ACK architecture ;
- plugin README and delivered feature docs describe V4 ;
- old progress/distance proposal is updated or visibly superseded ;
- Stateful presentation proposal no longer references removed Interaction APIs as current ;
- examples teach only generic `Running()` or explicit timed policy ;
- repository legacy sweep has no unexplained current-facing hits ;
- this spec/status is marked delivered after acceptance.

### Quality

- formatting passes ;
- build passes ;
- full tests pass ;
- no known V4 migration TODO is left merely because the runtime path already works.

---

## 15. References

- V4 intent: [`interaction-v4-architecture.md`](./interaction-v4-architecture.md)
- Delivered feature history: [`../interaction.md`](../interaction.md)
- Superseded progress work: [`presentation-progress-and-distance.md`](./presentation-progress-and-distance.md)
- Stateful presentation proposal: [`../../state/planned/stateful-presentation.md`](../../state/planned/stateful-presentation.md)
- In-process peers: [`../../../memory/godot-multiplayer-in-process-peers.md`](../../../memory/godot-multiplayer-in-process-peers.md)
- Synchronizer property paths: [`../../../memory/godot-multiplayer-synchronizer-root-path.md`](../../../memory/godot-multiplayer-synchronizer-root-path.md)
- Offline authority guard: [`../../../memory/godot-multiplayer-isserver-requires-peer.md`](../../../memory/godot-multiplayer-isserver-requires-peer.md)
- Godot 4.7 `MultiplayerSynchronizer`: https://docs.godotengine.org/en/4.7/classes/class_multiplayersynchronizer.html
- Godot 4.7 `SceneReplicationConfig`: https://docs.godotengine.org/en/4.7/classes/class_scenereplicationconfig.html
