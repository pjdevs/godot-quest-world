# Interaction composition scene migration

When migrating a Godot scene to composition-first interaction authoring, remove obsolete subresources
and external resources together with the exported references that used them. A stale subresource can
refer to an already removed `ExtResource` and make `GD.Load<PackedScene>()` return null, which surfaces
later as an unrelated null reference during test setup.

Code that needs composed actions before the node's `_Ready()` callback must use
`InteractiveComponent.ResolveActions()`. The compatibility `Actions` array is populated from direct
children during `_Ready()`, so reading it during scene construction is intentionally empty when the
scene leaves the array override unset.

## Parent spatial des marqueurs composés

Les `InteractionArea3D` et `InteractionAnchor3D` doivent rester sous un parent spatial. `InteractiveComponent`
est donc un `Node3D`, et les scènes qui lui attachent directement ces enfants doivent déclarer leur nœud
comme `type="Node3D"`. Un `Node` intermédiaire peut casser l'héritage de transform par rapport au propriétaire
de la scène et déplacer visuellement le marker/les areas loin de l'objet.
