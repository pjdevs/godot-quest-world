# Interaction Plugin

This addon ports the Unreal interaction boundaries to Godot 4.7 C# without an autoload or hidden input actions.

The addon is namespaced under `QuestWorld.Interaction` and has no Character dependency. Add `InteractionInteractor` to a locally controlled character and assign its `ViewOrigin` explicitly in the Inspector. Add `InteractiveComponent` and `InteractionStateful` to an object with an `Area3D`, assign `InteractionArea`, `Stateful` and `InteractionOwner`, and implement `IInteractionHandler` on the owner node. The handler evaluates custom conditions and decides whether a long phase should call `StartInteractionPhase` and `EndInteractionPhase`.

`InteractionInteractor.TryStartInteractionInput()` and `TryEndInteractionInput()` are the only input entry points. The node keeps server multiplayer authority while `OwnerPeerId` identifies the client responsible for local focus, UI and input. Offline/listen-server mode uses the authoritative path directly; clients locally prevalidate and send reliable RPC intentions which the server revalidates against its own candidates, distance, angle, state and rules.

`InteractionPresenter` is optional. Assign its `Interactor` and `Camera` exports explicitly; it consumes interactor signals and projects configured `Control` scenes from the component's `Marker3D` anchor. A widget implements `IInteractionWidget` to receive the typed `InteractionPresentation` model.

The addon does not resolve configuration from node names, parents, siblings or recursive tree searches. Required references produce editor configuration warnings and are guarded again at runtime. `NodePath` is reserved for network RPC identities; no focus target is represented by an artificial presentation.

When the addon is enabled, `editor/InteractionEditorPlugin.cs` registers an Inspector plugin. `InteractionValidator` centralizes the configuration warnings for `InteractiveComponent`, `InteractionInteractor`, `InteractionStateful`, `InteractionPresenter` and the example `InteractiveActor`; the runtime scripts do not need `[Tool]` for validation.

The runtime has no Quest, Inventory, Dialog, Character or Network Foundation dependency. Save/load is deliberately only a versioned `InteractionSavedState` boundary; the host owns storage.

`scenes/InteractiveActor.tscn` is the small duplicable starting point with detection areas, an interaction anchor, default widgets and a long activation example handler. Replace or extend its owner script for the concrete object behavior.
