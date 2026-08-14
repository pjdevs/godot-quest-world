# Interaction

## Purpose

`Interaction` sera un addon Godot C# réutilisable pour les interactions offline et réseau : sélection d’une cible, conditions data-driven, interaction instantanée ou longue, état autoritaire répliqué et présentation locale remplaçable.

Le port conserve l’architecture du plugin Unreal existant (`Interactor`, `Interactive`, `Stateful`, handlers et UI découplée) tout en adoptant les primitives Godot : nœuds composables, `Area3D`, signaux, RPC high-level, `MultiplayerSynchronizer` et UI projetée depuis un `Marker3D`.

## Status

La conception V1 est validée. L’implémentation n’a pas commencé.

La spécification complète est disponible dans [`docs/superpowers/specs/2026-08-14-interaction-addon-design.md`](../superpowers/specs/2026-08-14-interaction-addon-design.md).

## V1 scope

- addon autonome sous `addons/interaction_plugin` ;
- offline et multiplayer high-level Godot ;
- RPC directs dans l’Interactor avec revalidation serveur ;
- interactions courtes, longues, automatiques et exclusives ;
- états `Idle`, `Activating`, `Activated`, `Deactivating` ;
- union type C# 15 pour le statut ;
- règles data-driven minimales et hook custom ;
- présentation découplée par signaux ;
- frontière save/load factice, sans stockage ;
- exemple autonome et tests offline/réseau.

## Deferred

La persistance réelle, les intégrations Quest/Dialog/Inventory, les combinateurs de règles, les UI 3D cliquables et les transports hors `SceneMultiplayer` sont volontairement reportés.
