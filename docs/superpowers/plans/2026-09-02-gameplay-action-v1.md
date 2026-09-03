# Gameplay Action System V1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the generic action runtime from Interaction, prove owned and external actions independently, then rebuild Interaction as a spatial integration without behavior loss.

**Architecture:** `GameplayActionComponent` becomes the authoritative action host and `GameplayActionRunner` becomes the requester/input/network boundary. Interaction keeps detection, access, target-level rules, contextual bindings, and world presentation while delegating every execution lifecycle to the generic add-on.

**Tech Stack:** Godot 4.7 C#, .NET 10, GdUnit4, MultiplayerSynchronizer

**Spec:** `docs/feature/gameplay_action/planned/gameplay-action-system-v1.md`

## Global Constraints

- `gameplay_action_plugin` must not depend on Interaction, Inventory, Stateful, Character, Quest, Dialog, or persistence.
- One active execution maximum per host and ActionId; concurrency groups remain host-local.
- Bindings are local references and never grants, ownership transfers, or authoritative permissions.
- Player requests validate sender, address, access, rules, reservations, then executor in that order.
- Programmatic execution bypasses access but preserves rules and reservations.
- Automatic bindings are invalidation-driven and latched per continuous eligibility window.
- No production behavior is added without a failing behavior test first.
- Every code checkpoint runs `csharpier format .`, `dotnet build`, and `GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot dotnet test`.

---

### Task 1: Generic contracts and authoritative host

**Deliverable:** A standalone generic component can register authored/runtime actions, evaluate rules, execute programmatically, enforce ActionId/group reservations, and retire a running action safely.

**Files:**

- Create: `addons/gameplay_action_plugin/plugin.cfg`
- Create: `addons/gameplay_action_plugin/editor/GameplayActionEditorPlugin.cs`
- Create: `addons/gameplay_action_plugin/runtime/GameplayActionTypes.cs`
- Create: `addons/gameplay_action_plugin/runtime/actions/GameplayActionDefinition.cs`
- Create: `addons/gameplay_action_plugin/runtime/actions/GameplayAction.cs`
- Create: `addons/gameplay_action_plugin/runtime/actions/GameplayActionExecutor.cs`
- Create: `addons/gameplay_action_plugin/runtime/actions/GameplayActionComponent.cs`
- Create: `addons/gameplay_action_plugin/runtime/rules/GameplayActionRule.cs`
- Create: `addons/gameplay_action_plugin/tests/GameplayActionComponentTest.cs`
- Create: `addons/gameplay_action_plugin/tests/TestGameplayActionExecutor.cs`
- Modify: `project.godot`
- Create/update: `docs/feature/gameplay_action/gameplay-action.md`

- [x] Write failing tests for registration, duplicate/missing IDs, multi-host ownership, rule order, programmatic execution, ActionId uniqueness, same-host/different-host concurrency, and retirement.
- [x] Implement the smallest generic contracts and component needed to pass them, keeping mutation and dispatch separate.
- [x] Run the mandatory format/build/full-test gate and document the delivered public API. The pre-existing `DoorSynchronizationConvergesPresentationWithoutReplayingUnlockAudio` failure remains; all other tests pass.
- [ ] Commit the independently usable authoritative-host checkpoint.

### Task 2: Generic execution lifecycle, progress, timing, and replication

**Deliverable:** The generic host owns all instant/running/terminal behavior and every execution presentation mode currently buried in `InteractiveComponent`.

**Files:**

- Create: `addons/gameplay_action_plugin/runtime/execution/GameplayActionExecutionProgressState.cs`
- Create: `addons/gameplay_action_plugin/runtime/execution/GameplayActionExecutionPresentationStore.cs`
- Create: `addons/gameplay_action_plugin/runtime/execution/TimedExecution.cs`
- Create: `addons/gameplay_action_plugin/runtime/actions/TimedGameplayActionExecutor.cs`
- Create: `addons/gameplay_action_plugin/runtime/execution/GameplayActionExecutionSynchronizer.cs`
- Create: `addons/gameplay_action_plugin/tests/GameplayActionExecutionTest.cs`
- Create: `addons/gameplay_action_plugin/tests/GameplayActionExecutionNetworkTest.cs`
- Modify: Task 1 generic action/component/type files
- Update: `docs/feature/gameplay_action/gameplay-action.md`

- [x] Add failing tests for completed/running/rejected/failed/cancelled outcomes, terminal callback uniqueness, timed and discrete progress, visibility, stale samples, and late join.
- [x] Move the V4 execution read model and timing policy into the generic host without introducing Interaction types.
- [x] Run the mandatory gate and document the complete host-side lifecycle checkpoint. The
  pre-existing `DoorSynchronizationConvergesPresentationWithoutReplayingUnlockAudio` failure remains;
  all GameplayAction tests pass.
- [ ] Commit the complete host-side lifecycle checkpoint after user review.

### Task 3: Runner, bindings, gestures, access, prediction, and acknowledgements

**Deliverable:** Owned and externally hosted actions share one local input and authoritative request pipeline independently of Interaction.

**Files:**

- Create: `addons/gameplay_action_plugin/runtime/bindings/GameplayActionBinding.cs`
- Create: `addons/gameplay_action_plugin/runtime/bindings/GameplayActionBindingConfig.cs`
- Create: `addons/gameplay_action_plugin/runtime/access/IGameplayActionAccessProvider.cs`
- Create: `addons/gameplay_action_plugin/runtime/runner/GameplayActionRunner.cs`
- Create focused internal runner helpers when gesture, pending-request, or acknowledgement state would otherwise make the public node carry unrelated algorithms.
- Create: `addons/gameplay_action_plugin/tests/GameplayActionBindingTest.cs`
- Create: `addons/gameplay_action_plugin/tests/GameplayActionRunnerTest.cs`
- Create: `addons/gameplay_action_plugin/tests/GameplayActionRunnerNetworkTest.cs`
- Update: `docs/feature/gameplay_action/gameplay-action.md`

- [x] Add failing tests for bind/unbind/source cleanup, all four activation modes, gesture snapshots, conflict order, input requirements, automatic invalidation/latching, owned/external access, fabricated bindings, prediction, ACKs, and teardown.
- [x] Implement local bindings and deterministic gesture resolution before adding the request/ACK transport.
- [x] Add the minimal typed access registry and the single reliable request protocol addressed by host path plus ActionId.
- [x] Run the mandatory gate; 290/291 tests pass and the pre-existing
  `DoorSynchronizationConvergesPresentationWithoutReplayingUnlockAudio` failure remains. Leave the
  commit to user review as requested.

### Task 4: Rebuild Interaction as a generic-action integration

**Deliverable:** Interaction retains its full spatial behavior and presentation while all action execution flows through `GameplayActionComponent` and `GameplayActionRunner`.

**Files:**

- Modify: `addons/interaction_plugin/runtime/actions/InteractionActionDefinition.cs`
- Modify: `addons/interaction_plugin/runtime/actions/InteractionAction.cs`
- Create: `addons/interaction_plugin/runtime/actions/InteractionActionBindingConfig.cs`
- Modify: `addons/interaction_plugin/runtime/interactive/InteractiveComponent.cs`
- Modify: `addons/interaction_plugin/runtime/interactor/InteractionInteractor.cs`
- Modify: `addons/interaction_plugin/runtime/InteractionTypes.cs`
- Modify: `addons/interaction_plugin/presentation/ui/InteractionPresenter.cs`
- Modify: all Interaction tests while retaining their behavior-level assertions
- Update: `docs/feature/interaction/interaction.md`

- [x] Add/adjust failing integration tests for focus bindings, focus cleanup, out-of-range authority rejection, programmatic distance bypass with rules, sustained access cancellation, and generic presentation reads.
- [x] Make Interaction action/definition types thin specializations and register the interactor as their access provider.
- [x] Move target rules, contextual binding construction/invalidation, and spatial presentation adaptation behind Interaction-only APIs.
- [x] Run every existing Interaction suite plus the mandatory full gate. Leave the functional-equivalence checkpoint uncommitted for user review.

### Task 5: Migrate authoring/integrations and remove the parallel lifecycle

**Deliverable:** Scenes, Stateful integration, diagnostics, and docs teach only the final architecture; no Interaction-owned generic execution pipeline remains.

**Files:**

- Move/rename generic Stateful executors into `addons/gameplay_action_plugin/integration/stateful/`
- Modify: `addons/interaction_plugin/editor/InteractionValidator.cs`
- Create: `addons/gameplay_action_plugin/editor/GameplayActionValidator.cs`
- Modify: Interaction sample scenes and `quest_world` scenes containing interaction actions
- Remove superseded generic files from `addons/interaction_plugin/runtime/actions/` and `runtime/interactive/`
- Update: `addons/interaction_plugin/README.md`
- Finalize: `docs/feature/gameplay_action/gameplay-action.md`
- Update: `docs/feature/interaction/interaction.md`

- [ ] Add failing configuration/scene tests for the final topology and every required diagnostic.
- [ ] Migrate `.tscn` resources and Stateful helpers without compatibility aliases for generic primitives.
- [ ] Delete the old host/runner lifecycle only after all consumers use the generic path.
- [ ] Search for obsolete Interaction execution types, paths, comments, and two-pipeline adapters; remove every active occurrence.
- [ ] Run formatter, build, the complete macOS Godot test command, and a headless project smoke check.
- [ ] Re-read the spec acceptance criteria line by line, update feature docs, and commit the V1 closeout.
