# Interaction — authoring polish

> **Status: planned.** Ergonomics-only pass over the current Gameplay Action topology. No execution,
> networking, availability or ownership invariant changes.

## Current baseline

The runtime topology is now explicit and stable:

```text
Door
├── StatefulComponent
├── GameplayActions                         # GameplayActionComponent
│   ├── OpenAction                          # InteractionAction
│   │   └── OpenExecutor                    # GameplayActionExecutor
│   └── CloseAction
│       └── CloseExecutor
├── GameplayActionExecutionSynchronizer?    # optional
└── InteractiveComponent
    ├── InteractionArea
    ├── IndicationArea?
    └── InteractionAnchor
```

`GameplayActionComponent` is the sole owner of action occurrences. `InteractiveComponent.Actions` is
already a derived projection of the `InteractionAction` occurrences hosted there; this plan does **not**
bring back a second Actions array or an Interaction execution synchronizer.

The remaining authoring pain is reference plumbing: the scene already expresses obvious local
relationships, but the Inspector still asks the author to repeat many of them explicitly.

## Goal

Make the normal path **composition-first, override-friendly**:

```text
resolved dependency = explicit override ?? unique local composed dependency
```

The runtime ownership model remains explicit and inspectable. Composition inference is only an authoring
shortcut for relationships that are local, unique and unambiguous.

## 1. Local composition inference

Candidate relationships to infer when the exported override is empty:

- `InteractiveComponent.ActionComponent` → the unique sibling `GameplayActionComponent` in the same
  authored object scope;
- `GameplayAction.Executor` → the unique direct-child `GameplayActionExecutor`;
- `GameplayActionExecutionSynchronizer.Component` → the unique local action host when unambiguous;
- `InteractiveComponent` area/indication/anchor references → unique direct local children of the
  corresponding type/role;
- Stateful integration executors/rules → the unique local `StatefulComponent` when the integration is
  clearly authored against the same object.

Constraints:

- no recursive tree search;
- no lookup by node name;
- no global registry or service locator;
- no silent choice between several candidates;
- an explicit Inspector reference always wins;
- zero/multiple candidates for a required dependency produce a clear validator/runtime diagnostic.

The exact “local object scope” should stay deliberately small (same parent/direct child relationships),
so moving nodes in a large scene cannot silently retarget gameplay.

## 2. Inspector presentation

Explicit references remain available for non-local composition, but should read as overrides rather than
mandatory plumbing:

```text
Overrides
    Action Component
    Executor
    Stateful
    Interaction Area
    Indication Area
    Interaction Anchor
    Execution Synchronizer Component
```

The common Inspector path should primarily expose gameplay data:

- action definition;
- input/binding policy;
- rules;
- concurrency and self/other busy outcome;
- execution visibility;
- executor-specific gameplay configuration.

## 3. Stateful authoring — only remove repeated references

The current generic Stateful executors already cover the important semantics:

- `SetStateGameplayActionExecutor`;
- `TransitionStateGameplayActionExecutor`;
- `TimedTransitionStateGameplayActionExecutor`.

They are **not** future work and this pass must not wrap them in another Interaction lifecycle.

The remaining polish is only dependency resolution. When an executor/rule targets the local object's
Stateful, it should not need the same NodePath repeated on every action. Explicit references remain
necessary and higher priority for remote targets such as a button controlling a separate wall.

Interaction core still gains no Stateful dependency; all such resolution stays in the existing optional
integration packages.

## Non-goals

- no recursive “magic” discovery;
- no second action collection on `InteractiveComponent`;
- no Interaction-specific execution or execution synchronizer;
- no new state-machine/transition framework;
- no replacement for custom `GameplayActionRule` / `InteractionRule` / executors;
- no generic AND/OR rule graph without a concrete gameplay need;
- no lifecycle, requester, prediction or networking refactor.

## Success criterion

A normal door should remain structurally honest while requiring little Inspector wiring:

```text
Door
├── StatefulComponent
├── GameplayActions
│   ├── Open : InteractionAction
│   │   └── TransitionExecutor
│   └── Close : InteractionAction
│       └── TransitionExecutor
└── InteractiveComponent
    ├── InteractionArea
    └── InteractionAnchor
```

The author configures gameplay semantics, not duplicate paths. If a dependency intentionally lives
elsewhere, they open **Overrides** and assign it explicitly.
