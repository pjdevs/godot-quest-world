# Interaction Plugin

This addon ports the Unreal interaction boundaries to Godot 4.7 C# without an autoload or hidden input actions.

The addon is namespaced under `QuestWorld.Interaction` and has no Character dependency. Add `InteractionInteractor` to a locally controlled character and assign its `ViewOrigin` explicitly in the Inspector. Add `InteractiveComponent` to an object with an `Area3D`, assign `InteractionArea` and `InteractionAnchor`, subscribe gameplay code to `InteractionInputStarted(interactor, action)` and `InteractionInputEnded(interactor, action)`, and compose custom `InteractionRule` resources for gameplay conditions. `InteractionStateful` is optional and can also be used standalone; subscribe to its universal, authority-only, or presentation-only state signals as needed. A long interaction calls `InteractiveComponent.StartInteractionPhase` and `EndInteractionPhase`.

`InteractionInteractor.TryStartInteractionInput(inputActionName)` and `TryEndInteractionInput(inputActionName)` are the only input entry points. They resolve the pressed input into one action of the focused target, and the reliable RPC carries `targetPath + actionId` so the server re-resolves and re-evaluates that action against its own scene. The node keeps server multiplayer authority while `OwnerPeerId` identifies the client responsible for local focus, UI and input. Offline/listen-server mode uses the authoritative path directly; clients locally prevalidate and send reliable RPC intentions which the server revalidates against its own candidates, distance, angle, state and rules.

`InteractionPresenter` is optional. Assign its `Interactor`, `Camera` and `PromptContainerScene` exports explicitly; it consumes interactor signals and projects configured `Control` scenes from the component's `Marker3D` anchor. The focused target is presented as one container implementing `IInteractionPromptContainer`, carrying the target name and stacking one instance of the component's `ActionPromptScene` per presented action; each action widget implements `IInteractionActionWidget` and shows its own allowed or blocked state. Indications stay one widget per target and implement `IInteractionWidget`.

The addon does not resolve configuration from node names, parents, siblings or recursive tree searches. Required references produce editor configuration warnings and are guarded again at runtime. `NodePath` is reserved for network RPC identities; no focus target is represented by an artificial presentation.

When the addon is enabled, `editor/InteractionEditorPlugin.cs` registers an Inspector plugin. `InteractionValidator` centralizes the configuration warnings for `InteractiveComponent`, `InteractionInteractor`, `InteractionStateful`, `InteractionPresenter` and the example `InteractiveActor`; the runtime scripts do not need `[Tool]` for validation.

The runtime has no Quest, Inventory, Dialog, Character or Network Foundation dependency. Save/load is deliberately only a versioned `InteractionSavedState` boundary; the host owns storage.

`scenes/InteractiveActor.tscn` is the small duplicable starting point with detection areas, an interaction anchor, default widgets and a long activation example. Replace or extend its owner script for the concrete object behavior.
