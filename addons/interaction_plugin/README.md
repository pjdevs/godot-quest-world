# Interaction Plugin

This addon ports the Unreal interaction boundaries to Godot 4.7 C# without an autoload or hidden input actions.

Add `InteractionInteractor` to a locally controlled character and assign its `ViewOriginPath`. The Quest World `Character.tscn` already includes the interactor and presenter, with `CharacterPlayerController` forwarding the `interact` action. Add `InteractiveComponent` and `InteractionStateful` to an object with an `Area3D`, and implement `IInteractionHandler` on the object's owner. The handler evaluates custom conditions and decides whether a long phase should call `StartInteractionPhase` and `EndInteractionPhase`.

`InteractionInteractor.TryStartInteractionInput()` and `TryEndInteractionInput()` are the only input entry points. Offline/listen-server mode uses the authoritative path directly; clients send reliable RPC intentions which the server revalidates against its own candidates, distance, angle, state and rules.

`InteractionPresenter` is optional. It consumes interactor signals and projects configured `Control` scenes from the component's `Marker3D` anchor. A widget implements `IInteractionWidget` to receive the typed `InteractionPresentation` model.

The runtime has no Quest, Inventory, Dialog, Character or Network Foundation dependency. Save/load is deliberately only a versioned `InteractionSavedState` boundary; the host owns storage.

`scenes/InteractiveActor.tscn` is the small duplicable starting point with detection areas, an interaction anchor, default widgets and a long activation example handler. Replace or extend its owner script for the concrete object behavior.
