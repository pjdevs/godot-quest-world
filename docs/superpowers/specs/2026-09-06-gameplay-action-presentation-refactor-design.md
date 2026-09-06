# Gameplay Action Presentation Refactor — Design

**Status:** Approved for implementation on 2026-09-06.

**Goal:** Make `gameplay_action_plugin` the sole owner of gameplay-action availability, action
presentation, input gesture state, and the default action widget/presenter. Keep
`interaction_plugin` responsible only for detection, focus, interaction access, target presentation,
and target projection.

## Boundaries

`GameplayActionPresentation` is the read model of one local binding. It carries the action identity,
player-facing text, input binding, availability, activation mode, and optional per-binding hold
progress. `GameplayActionRunner` owns the binding registry and gesture resolver, so it is the only
place that computes the elapsed hold and threshold-normalized progress.

`InteractionPresenter` remains the target-level presenter. `InteractiveComponent` builds
`InteractionTargetPresentation` by projecting interaction actions into the generic
`GameplayActionPresentation` model, while retaining target detection and interaction-specific
access rules. A new `GameplayActionPresenter` displays only bindings whose component is the runner's
`OwnedActionComponent`; it must not classify actions using `InteractionAction` or
`PresentationContext`.

The default action widget moves to `gameplay_action_plugin` and implements the generic widget
contract. Existing target scenes may continue selecting their action widget scene through
`ActionPromptScene`; only the script and scene ownership change.

## Availability

There is one availability union:

```text
GameplayActionAllowed
GameplayActionBlocked(reason)
GameplayActionHidden
```

`InteractionRule` returns `GameplayActionAvailability` directly. The Interaction-specific
availability union, records, enum, and conversion extensions are removed. The authoring enum for a
rule's unavailable result becomes `GameplayActionUnavailableKind`, with the generic conversion to
`GameplayActionHidden` or `GameplayActionBlocked`.

Rules remain ordered and side-effect free. Configuration and rules still run before concurrency;
concurrency must not make an action that was already hidden resurface as blocked.

## Gesture contract

The resolver exposes hold state by captured binding identity:

```csharp
bool TryGetBindingHoldProgress(
    ulong bindingId,
    out float progress,
    out float elapsed
);
```

Only bindings present in the candidate set at gesture start may report progress. Press/release
bindings and bindings removed during the gesture report no progress. Two hold bindings sharing an
input normalize against their own thresholds while sharing the same elapsed duration.

Interaction no longer exposes gesture queries or derives elapsed time from target configuration.

## Presenter lifecycle

Both presenters use a frame refresh for changing data and structural reconciliation for widget
creation/removal. The generic presenter indexes widgets by `GameplayActionBinding.Id`, not
`ActionId`, so multiple input bindings for one action remain distinct. Removed bindings are collected
before dictionary mutation. A non-local runner clears the generic presenter.

## Scope and non-scope

The migration includes runtime types, presenters, default widget scenes, existing scene references,
tests, feature documentation, and removal of the Quest World presenter prototype. The separate draft
`docs/feature/interaction/planned/interaction-execution-presentation-self-vs-other.md` is explicitly
out of scope and must not be modified during this chantier; it will be updated later from the final
frozen API.

## Verification

Each code task follows the repository gate:

```text
csharpier format .
dotnet build
GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot dotnet test
```

The final migration also checks the demo scene headlessly and confirms that no runtime occurrence of
the removed Interaction presentation/gesture symbols remains.
