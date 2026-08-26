# Interaction Plugin

This addon ports the Unreal interaction boundaries to Godot 4.7 C# without an autoload or hidden input actions.

The addon is namespaced under `QuestWorld.Interaction` and has no Character dependency. Add `InteractionInteractor` to a locally controlled character and assign its `ViewOrigin` explicitly in the Inspector. Add `InteractiveComponent` to an object with an `Area3D`, assign `InteractionArea` and `InteractionAnchor`, add one `InteractionAction` per offered action with its `InteractionActionDefinition` and its required `InteractionActionExecutor`, and compose custom `InteractionRule` resources for gameplay conditions. The executor is the single owner of the gameplay mutation; the `InteractionActionStarted`, `InteractionActionCompleted`, `InteractionActionCancelled`, and `InteractionActionRejected` signals are notifications only. World state lives in the separate `stateful_plugin` addon: add a `StatefulComponent` when the object needs replicated, persistable state, read it from availability with `StatefulStateInteractionRule`, and write it with `SetStateInteractionExecutor`. The interaction runtime holds no reference to it and interprets no state value. A long interaction returns a running execution and later calls `InteractiveComponent.CompleteExecution` or `CancelExecution`.

## Execution or rule?

The one distinction to learn before writing anything:

> An **execution** says *"this interactor is engaged with this target, right now."*
> A **rule** says *"the world is in a state where this action is (un)available."*

They are not two ways of doing the same thing, even though they look interchangeable in single player. Talking to an NPC is an execution: the executor opens the dialogue and returns `InteractionExecutionRunning()` with no duration, and whatever owns the conversation calls `CompleteExecution(id)` when it closes. Do not write an "is in dialogue" rule for this — the reservation already knows *who* is engaged, so another player is told someone else is busy with that NPC rather than being blocked by a global flag, and the framework ends the execution by itself when the player walks out of range, releases a sustained input, or leaves the tree.

The test for having picked wrong: **if a rule reads a flag that one of your executors sets and clears, you have re-implemented the reservation by hand**, without its safety net — your flag stays set when the player disconnects mid-dialogue.

Rules own the other two scopes. A condition about the player — *"I am already busy"* — is a rule reading `context.Interactor`. A condition about the world — *"this terminal has no power"*, *"this door is already open"* — is a rule reading state, usually `StatefulStateInteractionRule`. Rules must stay pure: they are re-evaluated every frame while a target is focused, and they run on both the requesting client and the authoritative server.

While an execution is running, its action is presented as **blocked** rather than allowed, for its own interactor too: a prompt keeps somewhere to explain the situation, but never offers an action the target would immediately refuse.

## Input

`InteractionInteractor.TryStartInteractionInput(inputActionName)` and `TryEndInteractionInput(inputActionName)` are the only input entry points. They resolve the pressed input into one action of the focused target, and the reliable RPC carries `targetPath + actionId` so the server re-resolves and re-evaluates that action against its own scene. The node keeps server multiplayer authority while `OwnerPeerId` identifies the client responsible for local focus, UI and input. Offline/listen-server mode uses the authoritative path directly; clients locally prevalidate and send reliable RPC intentions which the server revalidates against its own candidates, distance, angle, state and rules.

`InteractionPresenter` is optional. Assign its `Interactor`, `Camera` and `PromptContainerScene` exports explicitly; it consumes interactor signals and projects configured `Control` scenes from the component's `Marker3D` anchor. The focused target is presented as one container implementing `IInteractionPromptContainer`, carrying the target name and stacking one instance of the component's `ActionPromptScene` per presented action; each action widget implements `IInteractionActionWidget` and shows its own allowed or blocked state. Indications stay one widget per target and implement `IInteractionWidget`.

The addon does not resolve configuration from node names, parents, siblings or recursive tree searches. Required references produce editor configuration warnings and are guarded again at runtime. `NodePath` is reserved for network RPC identities; no focus target is represented by an artificial presentation.

When the addon is enabled, `editor/InteractionEditorPlugin.cs` registers an Inspector plugin. `InteractionValidator` centralizes the configuration warnings for `InteractiveComponent`, `InteractionInteractor`, `InteractionPresenter` and `TransitionStateInteractionExecutor`; the runtime scripts do not need `[Tool]` for validation.

The runtime has no Quest, Inventory, Dialog, Character or Network Foundation dependency. The interaction runtime persists nothing at all: a running execution is transient and server-only, and world-state snapshots belong to `StatefulComponent` in `stateful_plugin`. The host owns storage.

`integration/stateful/examples/LongActionExample.tscn` is the small duplicable starting point with detection areas, an interaction anchor, default widgets, a `StatefulComponent` and one long action. Its root carries no script: the action is owned entirely by its `TransitionStateInteractionExecutor`, a generic node that applies a running state, lets the target hold the execution for the duration authored on the action, then applies the end state. Replace it with your own executor when the object does more than move between states.
