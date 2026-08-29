# Interaction V4 — implementation specification

> **Status: Impl 1 delivered; Impl 2 and Impl 3 pending.** Cette spec transforme
> [`interaction-v4-architecture.md`](./interaction-v4-architecture.md) en trois tranches de
> réalisation exécutables. Le document d'architecture reste le contrat d'intention ; celui-ci fixe
> les APIs, la migration, le transport réseau, les tests et les critères de fin. Aucun plan séparé
> n'est nécessaire après validation de cette spec.

## 1. Goal

Livrer V4 sans cut massif et sans état intermédiaire ambigu :

1. **Impl 1 — execution read model foundation** : séparer action et execution, formaliser le slot
   unique, adapter les widgets, conserver temporairement le timing V3 interne ;
2. **Impl 2 — generic progress, timing and requester prediction** : retirer toute sémantique de
   durée du core, introduire le timed helper, les producers de progression et le slot requester ;
3. **Impl 3 — replication and visibility** : synchroniser les executions world-observable,
   supporter late join et prouver les producers custom.

À la fin des trois tranches :

- `ActionPresentation` ne décrit que ce qu'un interactor peut demander ;
- `ExecutionPresentation` décrit ce que le peer peut observer sur l'Interactive ;
- `Running()` ne transporte aucune durée ;
- `Progress` est optionnel et ne révèle pas sa provenance ;
- le timing est une feature spécialisée ;
- l'autorité possède le lifecycle ;
- la visibilité décide seulement qui reçoit le read model ;
- aucun shim V3 de durée ou de progression ne subsiste.

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
| Visibility authoring | Per `InteractionAction`, default `RequesterOnly` |
| Requester transport | Existing reliable owner RPC channel |
| World transport | Dedicated `MultiplayerSynchronizer`, reliable on-change snapshot |
| Interest management | Native synchronizer visibility/filter APIs |
| Correlation | `(target, ActionId)` while pending; authoritative `ExecutionId` after ACK |
| Failure after start | Add `FailExecution(executionId, reason)` |
| Public terminal contract | `Completed`, `Cancelled`, or `Failed`; never a terminal progress value |

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
- renderers must treat `ExecutionId` as opaque and must not infer a visual predicted state from zero ;
- a confirmed execution always has a non-zero authority-allocated identifier ;
- the record has no terminal state: it disappears on completion, cancellation or failure.

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
using the same action definition may therefore present their executions differently. The default
preserves V3 privacy: only the requester receives client presentation.

### 3.4 Interactive queries

```csharp
public IReadOnlyList<InteractionExecutionPresentation> GetExecutionPresentations();

public bool TryGetExecutionPresentation(
    StringName actionId,
    out InteractionExecutionPresentation presentation
);
```

The returned list is a fresh immutable-facing snapshot ordered by `InteractiveComponent.Actions`,
not by start time. Deterministic action order keeps prompts, replication and tests stable.

`GetPresentation(interactor, isFocused)` continues returning action presentation only.

### 3.5 Presentation invalidation

`InteractiveComponent` adds one structural signal:

```csharp
[Signal]
public delegate void ExecutionPresentationChangedEventHandler(StringName actionId);
```

It is emitted locally when the visible slot is created, receives a published/synchronized snapshot,
changes authoritative identity, or disappears. It is not emitted every frame for derived progress.
A continuous renderer pulls the query from `_Process`; an event-driven renderer pulls after the
signal.

### 3.6 Widget binding

```csharp
public interface IInteractionActionWidget
{
    void Bind(
        in InteractionActionPresentation action,
        InteractionExecutionPresentation? execution
    );
}
```

`InteractionPresenter` performs the join:

```text
for each action in target presentation
    execution = target.TryGetExecutionPresentation(action.ActionId)
    widget.Bind(action, execution?)
```

The default widget continues rendering label, input, availability and hold. It does not gain an
execution progress bar merely because the data is available. Game-specific widgets may use the
second argument. World-space consumers query the Interactive directly and need no interactor.

## 4. Runtime model and invariants

### 4.1 Authoritative execution and local presentation are separate

Keep two internal concepts:

```text
AuthoritativeExecution
    server/offline gameplay reservation
    Interactor + Action node references
    ConcurrencyGroup
    lifecycle ownership

LocalExecutionPresentationSlot
    per-peer presentation state keyed by ActionId
    authoritative, requester or replicated origin
    optional published/derived progress state
```

Prediction never enters the authoritative execution collection. Replication never gains gameplay
mutation methods. The public query projects local presentation slots.

On authority/offline, every active authoritative execution has a local presentation slot regardless
of network visibility. On a remote peer, only requester or replicated transports create slots.

### 4.2 Slot cardinality

For one Interactive:

```text
ActionId -> zero or one authoritative execution
ActionId -> zero or one local presentation slot
```

Before reserving, the authority checks in this order:

1. configuration, target rules and action rules ;
2. an active execution with the same `ActionId` ;
3. an active execution in the requested concurrency group.

Rule results keep priority over busy feedback: an action already hidden by gameplay stays hidden.
`ReserveExecutionCore` repeats the two reservation checks so internal/direct execution paths cannot
bypass cardinality or concurrency after availability was evaluated.

The existing editor validator already reports duplicate action ids. Impl 1 locks that behavior with
a regression test and adds the runtime `ActionId` guard because mutable scene configuration or direct
calls must not rely on editor validation.

### 4.3 Lookup implementation

The authoritative active list may remain a list because targets have few actions. Add direct helper
lookups by `ExecutionId`, `ActionId` and concurrency group. Do not introduce a collection of executions
per action.

The presentation layer keeps one dictionary keyed by `ActionId` plus deterministic projection through
the declared action order. A replicated action id absent from the local action configuration is
ignored with one warning: network payload does not create authoring data.

Authority allocation reserves `0` for prediction and stays within `1..long.MaxValue`, the lossless
Godot Variant transport range. Exhaustion is a fatal session error rather than a wrapped/reused id.

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

All three are authority-only, return `false` for a stale/unknown identifier, release the slot before
external callbacks, and produce exactly one terminal notification.

Executor callbacks become:

```csharp
OnExecutionCompleted(context)
OnExecutionCancelled(context, reason)
OnExecutionFailed(context, reason)
```

Failure is distinct from cancellation in authority signals and requester ACKs. Existing immediate
`InteractionExecutionFailed` uses the same public started-then-failed contract.

Authority notification adds:

```csharp
[Signal]
public delegate void InteractionActionFailedEventHandler(
    InteractionInteractor interactor,
    InteractionAction action,
    string reason
);
```

An immediate failed result emits `Started` then `Failed`, not `Cancelled`. `OnExecutionFailed` is
called only when an execution previously left running is failed later.

### 4.5 Terminal progress

Completion removes the slot in the same authoritative operation. Code may report `Progress = 1`
while intentionally keeping an execution active, but this is never required before completion.

Therefore:

```text
ReportProgress(.66)
CompleteExecution(id)
```

is valid. Local and remote renderers may observe `.66` followed directly by absence. End animation
comes from the terminal signal or slot disappearance, not from equality with one.

## 5. Progress production

### 5.1 Published snapshots

Public API:

```csharp
public bool ReportExecutionProgress(ulong executionId, float? progress);
```

Semantics:

- authority/offline only ;
- `null` clears the published value ;
- finite values are clamped to `[0, 1]` ;
- `NaN` and infinities warn and return `false` ;
- stale ids return `false` without warning, so delayed callbacks are safe ;
- unchanged normalized values are no-ops ;
- a timed linear sample already owning this execution rejects published progress with one warning ;
- the method changes presentation only, never gameplay truth or lifecycle ;
- a change emits `ExecutionPresentationChanged` and routes the snapshot according to visibility.

Use it for discrete/event-driven progress. Calling it every frame is unsupported authoring, not a
network streaming API.

### 5.2 Derived local source

Public API:

```csharp
public bool SetExecutionProgressSource(ulong executionId, Callable source);
public bool ClearExecutionProgressSource(ulong executionId);
```

The source is local presentation state and may be registered on authority or clients for an existing
slot. It returns either `null`/Nil or a numeric normalized value. `Callable` is chosen over a C#
interface so C#, GDScript and a future GDExtension can provide it naturally.

Rules:

- one source per execution ; setting another replaces it ;
- `executionId = 0` is rejected by this public API because several predicted action slots may share
  that sentinel; prediction sources are installed through the action-aware internal hook ;
- a freed/invalid callable is cleared and resolves to no derived value ;
- a thrown call or non-numeric Variant warns once, clears the source and falls back ;
- source lifetime ends automatically with the slot ;
- source changes do not replicate ; the owning gameplay system must already synchronize the data
  from which each peer derives progress ;
- polling happens only when a consumer asks for presentation.

Progress resolution order:

```text
valid local Callable
    else extrapolated transport sample
    else published snapshot
    else null
```

### 5.3 Internal linear sample

Timed execution uses an internal transport capability:

```text
ProgressBase
ProgressPerSecond
SampleRevision
```

A receiving peer records local receipt time and resolves:

```text
clamp(ProgressBase + ProgressPerSecond * localSecondsSinceReceipt, 0, 1)
```

This is presentation interpolation, not core lifecycle timing. The public record still exposes only
the resulting `Progress`. Published progress is the same shape with `ProgressPerSecond = 0`.

Revision is monotonic per authoritative execution. ACK, requester RPC and replicated snapshots apply
only a strictly newer revision, preventing an older transport channel from rewinding a merged slot.

## 6. Timed execution feature

### 6.1 Base executor cleanup

`InteractionActionExecutor` removes:

- `ComputeInteractionDuration` ;
- `RunningForDuration` ;
- duration documentation.

It keeps a protected `Running()` helper returning payload-free `InteractionExecutionRunning`.

`InteractiveComponent` removes authoritative `Duration`, `Elapsed`, timer `_Process`,
`TryGetExecutionProgress` and duration application. Reservation, input release, presence validation,
completion, cancellation and failure remain unchanged.

### 6.2 TimedInteractionExecutor

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

`ComputeTimedDuration` defaults to the exported `Duration`, remains a pure query, and is only part of
the timed feature. `RunningTimed` clamps negative duration to zero.

- positive duration: register authoritative clock and linear progress sample, return `Running()` ;
- zero duration: return generic `Running()` with no clock or progress ;
- timeout: call `CompleteExecution(executionId)` ;
- completion/cancellation/failure: clear clock and progress state before subclass callback ;
- stale timer callback: no-op through the execution id guard.

The helper processes only while it owns a positive-duration execution. Core Interaction never checks
`executor is TimedInteractionExecutor`.

`TransitionStateInteractionExecutor` migrates to this base. Existing `Duration = 0` continues to mean
the gameplay system completes the execution externally.

### 6.3 Timing correction

The authority publishes a linear sample at start and every `CorrectionInterval` while the execution
is active. Default `0.5 s` bounds late-join error and drift without a per-frame float stream.

The server clock alone decides timeout. A client reaching visual progress one clamps there but cannot
complete anything. Authoritative slot removal ends the presentation.

`CorrectionInterval <= 0` disables periodic correction after the initial sample. It is allowed for
short requester-only interactions but does not satisfy replicated late-join acceptance criteria.
The validator reports a replicated timed action configured this way.

## 7. Requester prediction and ACK reconciliation

### 7.1 Generic prediction hook

`InteractionActionExecutor` gains an internal polymorphic hook returning an optional initial linear
progress sample. Default returns none. `TimedInteractionExecutor` returns `0` and `1 / duration` for a
positive locally computable duration.

The interactor invokes the hook without type testing. Custom executors may opt in later; renderers
never know the producer type.

### 7.2 Pending slot

On a local request with a prediction sample:

```text
target + ActionId
    -> local slot ExecutionId = 0
    -> Progress starts immediately
```

Every request also creates an internal pending marker keyed by target/action, even when no prediction
sample exists and therefore no public slot is visible yet. The marker prevents duplicate requests and
participates in local concurrency checks until rejection or started ACK.

Only one pending/confirmed slot is allowed for that pair. Predictions for different action ids may
coexist when concurrency allows them.

No `RequestId` is added. The client cannot send a second request for the same target/action until the
first receives rejection or started ACK. `ExecutionId` protects callbacks after confirmation.

### 7.3 ACK shape

The public requester signal becomes:

```text
InteractionStarted(interactive, actionId, executionId)
```

Duration leaves the signal and RPC contract. Internal started ACK may carry an optional presentation
sample (`base`, `rate`, `revision`) but the public consumer receives the normal target read model.

Reconciliation:

- rejection removes only pending slot `ExecutionId = 0` ;
- accepted running replaces zero with authoritative id ;
- accepted non-timed running creates a confirmed slot with `Progress = null` ;
- instant completion/failure may start and end inside one network turn without a rendered slot ;
- completion/cancellation/failure removes matching authoritative id immediately ;
- stale terminal ACK never removes a newer id in the same action slot.

For timed ACK after local prediction, preserve the elapsed request time and retime so the remaining
visual duration equals the authoritative duration represented by the sample. This retains V3's
latency compensation: the bar does not rewind at ACK and does not finish one round trip before the
terminal ACK.

### 7.4 Requester-only progress updates

`RequesterOnly` published/correction samples use a reliable targeted RPC to the owner interactor.
They are not broadcast. The RPC names target, action id and execution id; mismatched/stale updates are
ignored.

The lifecycle ACK is always delivered to the requester, including `AuthorityOnly`, because it reports
the result of the request. Visibility controls creation of client execution presentation, not whether
the requester learns that its command started or ended.

## 8. Replication and visibility

### 8.1 InteractionExecutionSynchronizer

Add a `[GlobalClass]` node derived from `MultiplayerSynchronizer`:

```text
InteractionExecutionSynchronizer
    explicit reference to its InteractiveComponent
    self-owned replicated snapshot property
    SceneReplicationConfig configured for spawn + OnChange
```

It owns transport only. It exposes no gameplay mutation. The synchronized property is rooted on the
synchronizer itself and uses a Variant-compatible `Godot.Collections.Array<Dictionary>`; it contains
no `Object`, `Resource`, instance id or RID.

The node sets `root_path = "."` and configures `.:ReplicatedSnapshot` with spawn enabled and
`ReplicationMode.OnChange`. A scene test resolves this exact path from the configured root.
The replication-only exported property is hidden from the Inspector through `_ValidateProperty` and
cannot be used as a second gameplay mutation API.

An Interactive needs the node only when at least one action is `Replicated`. The editor validator
reports a missing or incorrectly targeted synchronizer. Runtime gameplay still executes if it is
missing, but pushes one configuration error and cannot provide world replication.

The inherited `PublicVisibility`, `SetVisibilityFor`, visibility filters and update mode remain the
interest-management API. Visibility applies to the Interactive's replicated execution snapshot as a
whole; requester-only and authority-only entries are never included.

### 8.2 Snapshot wire shape

Each dictionary contains Variant-safe values:

```text
action_id            StringName
execution_id         signed 64-bit transport encoding of ulong
progress_present     bool
progress_base        float
progress_per_second  float
revision             signed 64-bit integer
```

`execution_id` values are authority-generated and constrained to `1..long.MaxValue` so conversion is
lossless. Missing progress uses `progress_present = false`; it is not conflated with zero.

The whole small collection is rebuilt on structural change, published progress change or timed
correction. Entries follow action declaration order. On-change replication is reliable. Spawn sync
delivers the current collection to late joiners.

Applying a snapshot diffs by `ActionId` and `ExecutionId`, then emits local invalidation once per
changed action. A property carries current state, not transition history: start and end inside one
replication frame may be observed only as absence, which is valid for an instant execution.
Revision comparison resolves out-of-order ACK/synchronizer arrival for the requester.

### 8.3 Visibility behavior

| Mode | Authority/offline | Requester client | Other visible clients | Transport |
| --- | --- | --- | --- | --- |
| `AuthorityOnly` | slot visible | no slot | no slot | lifecycle ACK only |
| `RequesterOnly` | slot visible | slot visible | no slot | owner RPC |
| `Replicated` | slot visible | predicted/ACK then sync | slot visible | synchronizer |

For a listen host, the authoritative local slot is the presentation source. Do not replay local RPC or
replicated setter on the same instance; each structural event is emitted once.

For `Replicated`, the requester ACK confirms prediction immediately. Later synchronizer state merges
into the same `ActionId`/`ExecutionId` slot and must not create a duplicate.

### 8.4 Late join

A late join receives:

- active replicated membership ;
- current published progress snapshot ;
- latest timed base/rate sample ;
- no missed start/progress history ;
- future terminal disappearance.

With default correction interval, timed visual error at arrival is bounded by one correction interval
plus transport latency. Authority completion remains exact.

## 9. Three implementation slices

### 9.1 Impl 1 — read model foundation

### Code scope

- add `InteractionExecutionPresentation` ;
- remove `HasTimedExecution` and `ExecutionProgress` from action presentation ;
- keep `HoldProgress` and `HoldElapsed` unchanged ;
- enforce runtime uniqueness by `ActionId` before concurrency group ;
- preserve and test existing duplicate-id editor diagnostic ;
- add execution collection/lookup and structural invalidation ;
- expose authority/offline slots only ;
- project current V3 positive-duration clock as temporary authority/offline `Progress` ;
- project indefinite V3 execution as `Progress = null` ;
- adapt `IInteractionActionWidget` and `InteractionPresenter` to two-model binding ;
- update all built-in widgets and tests ;
- retain V3 duration result, clock, requester `_prediction`, duration ACK and network behavior
  internally until Impl 2.

### Explicit non-goals

- no requester execution slot ;
- no published or Callable progress API ;
- no timed executor extraction ;
- no execution replication or visibility enum ;
- no world-space example.

### Acceptance tests

- action presentation exposes no execution/timed members ;
- same `ActionId` cannot reserve twice even if concurrency group changed ;
- different action ids in different groups coexist ;
- duplicate authored ids are diagnosed ;
- lookup returns correct execution id and nullable progress ;
- completion/cancellation removes lookup immediately ;
- widget receives matching execution or null ;
- remote clients receive null execution in this tranche ;
- all existing gameplay, ACK and network tests still pass after expected API updates.

### 9.2 Impl 2 — progress, timing and requester

### Code scope

- add published and Callable progress APIs ;
- add internal linear sample resolution ;
- add `FailExecution` and failed callback/signal ;
- make `InteractionExecutionRunning` payload-free ;
- remove duration/elapsed/process from Interactive core ;
- add `TimedInteractionExecutor` ;
- migrate `TransitionStateInteractionExecutor` and scripted test executors ;
- move requester prediction from one interactor float to per-target/action local slots ;
- remove duration from public ACK signal/RPC semantics ;
- add requester-only sample update RPC ;
- adapt prompt tests to consume execution presentation.

### Acceptance tests

- generic `Running()` stays active without timer ;
- timed helper completes authoritatively once ;
- cancel/fail before timeout prevents double completion ;
- timed progress appears through the generic record ;
- discrete `0/.33/.66` uses the same record ;
- stale progress report/source/terminal ids are safe ;
- invalid progress is rejected or clamped as specified ;
- freed/invalid Callable falls back safely ;
- requester sees predicted timed progress before ACK ;
- ACK confirms without public predicted flag or visible rewind ;
- rejection clears pending slot ;
- different action predictions may coexist ;
- another client sees no requester-only slot ;
- existing hold, input-release and presence semantics remain orthogonal.

### 9.3 Impl 3 — replication, visibility and real producers

### Code scope

- add `InteractionExecutionSynchronizer` ;
- add `InteractionExecutionVisibility` and the exported action property with `RequesterOnly` default ;
- implement Variant snapshot encoding/diff application ;
- enable `Replicated` action visibility ;
- integrate native peer visibility/filtering ;
- route published and timed corrections through the synchronizer ;
- merge requester ACK with later replicated snapshot ;
- add a discrete three-step test process ;
- add a fake replicated `HackSession` whose local Callable derives progress ;
- add a world feedback consumer reading the Interactive directly ;
- add a prompt proof binding both read models ;
- update example scenes only where they exercise replicated execution presentation.

### Acceptance tests

- replicated start/end reaches requester and other visible peer once ;
- requester-only and authority-only never leak into replicated snapshot ;
- native synchronizer visibility hides/shows the full target snapshot ;
- late join sees active membership and current discrete progress ;
- late join sees extrapolating timed progress near authority value ;
- two actions in separate groups replicate concurrently ;
- same action still cannot run twice ;
- timed correction sends sparse samples, not per-frame progress ;
- final completion may arrive after `.66` without requiring `1` ;
- custom gameplay-derived progress and timed progress bind identically ;
- world feedback works outside every interaction detection area ;
- listen host emits each presentation change once ;
- dedicated server owns timeout and lifecycle without presentation UI ;
- one shared fake repair process can own participants outside Interaction while one execution exposes
  shared progress.

## 10. Migration and compatibility

### After Impl 1

- source-breaking presentation API change is complete ;
- old timer remains internal ;
- remote requester temporarily loses generic execution progress because it no longer rides action
  presentation ;
- default widget has no regression because it never displayed execution progress.

### After Impl 2

- duration leaves core result, target storage and public ACK ;
- requester presentation is restored through execution slots ;
- all timed authoring uses `TimedInteractionExecutor` ;
- old `ComputeInteractionDuration`, `RunningForDuration`, `_prediction` float and
  `TryGetExecutionProgress` are deleted, not deprecated.

### After Impl 3

- `Replicated` is functional and late-join-safe ;
- no execution presentation depends on Stateful ;
- no timer-specific presentation field or renderer type check remains ;
- no V3 compatibility shim remains.

## 11. Files expected to change

Primary runtime:

- `addons/interaction_plugin/runtime/InteractionTypes.cs` ;
- `addons/interaction_plugin/runtime/interactive/InteractiveComponent.cs` ;
- `addons/interaction_plugin/runtime/interactor/InteractionInteractor.cs` ;
- `addons/interaction_plugin/runtime/actions/InteractionAction.cs` ;
- `addons/interaction_plugin/runtime/actions/InteractionActionExecutor.cs` ;
- new `TimedInteractionExecutor.cs` ;
- new `InteractionExecutionSynchronizer.cs`.

Integration and presentation:

- `TransitionStateInteractionExecutor.cs` ;
- `InteractionPresenter.cs` ;
- `IInteractionActionWidget.cs` ;
- `InteractionActionPromptWidget.cs` ;
- relevant example scenes.

Validation and tests:

- `InteractionValidator.cs` ;
- `InteractionConfigurationTest.cs` ;
- `InteractionBehaviorTest.cs` ;
- `InteractionAckTest.cs` ;
- `InteractionNetworkTest.cs` ;
- `InteractionSceneTest.cs` ;
- focused test helpers/producers introduced beside these suites.

Documentation:

- maintain `docs/feature/interaction/interaction.md` after each implementation tranche ;
- update this spec status after each accepted tranche ;
- add a `docs/memory/` entry only for a newly discovered workflow pitfall or hard correction.

## 12. Verification contract

After each code tranche, from repository root on Windows:

```powershell
csharpier format .
dotnet build
$env:GODOT_BIN = (Get-Command godot).Source
dotnet test
```

Additionally:

- Impl 1 must run focused behavior, configuration and UI scene tests while iterating ;
- Impl 2 must run focused behavior and ACK tests while iterating ;
- Impl 3 must run the real in-process server/two-client/late-join network suite while iterating ;
- final acceptance is always the full mandated command sequence, not focused tests alone.

## 13. Performance and safety constraints

- no progress RPC or synchronized property write per render frame ;
- timed corrections default to at most two writes per second per active timed execution ;
- full replicated collection rebuild is acceptable because an Interactive has few action slots ;
- all network payloads use action ids and execution ids, never Node instance ids ;
- every received target path resolves relative to `SceneMultiplayer.RootPath` ;
- every authority guard treats a peerless/offline game as authority ;
- synchronizers must enter a subtree only after its MultiplayerAPI is attached in in-process tests ;
- replicated property paths are relative to synchronizer `root_path` and verified by a scene test ;
- stale ids cannot mutate, complete, cancel, fail or update a newer occurrence ;
- current-state replication, not missed transitions, drives late-join presentation.

## 14. Final definition of done

The three-tranche implementation is complete only when:

- every success scenario in the V4 architecture has an automated test or named example ;
- the same renderer consumes timed, published and gameplay-derived progress without a producer type
  branch ;
- action and execution read models remain separately queryable ;
- action widgets receive both through explicit composition ;
- remote world feedback does not require focus, indication or interaction overlap ;
- timed late join extrapolates from sparse state and ends on authority ;
- visibility modes behave as specified on offline, listen host, remote client and dedicated server ;
- documentation describes the delivered code rather than future intent ;
- formatting, build and full tests pass.

## 15. References

- V4 intent: [`interaction-v4-architecture.md`](./interaction-v4-architecture.md)
- Delivered feature history: [`../interaction.md`](../interaction.md)
- In-process peers: [`../../../memory/godot-multiplayer-in-process-peers.md`](../../../memory/godot-multiplayer-in-process-peers.md)
- Synchronizer property paths: [`../../../memory/godot-multiplayer-synchronizer-root-path.md`](../../../memory/godot-multiplayer-synchronizer-root-path.md)
- Offline authority guard: [`../../../memory/godot-multiplayer-isserver-requires-peer.md`](../../../memory/godot-multiplayer-isserver-requires-peer.md)
- Godot 4.7 `MultiplayerSynchronizer`: https://docs.godotengine.org/en/4.7/classes/class_multiplayersynchronizer.html
- Godot 4.7 `SceneReplicationConfig`: https://docs.godotengine.org/en/4.7/classes/class_scenereplicationconfig.html
