# Interaction — authoring polish

> **Status: planned.** Petit pass d'ergonomie après V4. Aucun nouvel invariant gameplay : le but est de supprimer la plomberie d'Inspector et de rendre les scènes lisibles par leur composition.

## Goal

Le chemin normal d'authoring doit être **composition-first** : si la scène exprime déjà la relation parent/enfant, l'auteur ne doit pas recopier cette relation dans une référence exportée.

```text
InteractiveComponent
├── InteractionArea3D
│   └── CollisionShape3D
├── IndicationArea3D
│   └── CollisionShape3D
├── OpenAction
│   └── Executor
└── CloseAction
    └── Executor
```

Les références explicites restent disponibles uniquement comme **override** pour les cas où la cible vit ailleurs dans l'arbre.

## 1. Child composition by default

Résolution locale attendue :

- `InteractiveComponent` découvre ses `InteractionAction` enfants directs ;
- `InteractionAction` utilise son unique `InteractionActionExecutor` enfant direct ;
- `InteractionExecutionSynchronizer`, interaction area, indication area et anchor suivent le même principe lorsqu'une composition locale non ambiguë existe ;
- aucune recherche récursive ou heuristique par nom : enfant direct / type explicite seulement ;
- zéro ou plusieurs candidats lorsque exactement un est requis produit un diagnostic clair.

Les anciennes propriétés de référence peuvent rester pour les cas externes, mais deviennent des overrides :

```text
resolved value = explicit override ?? composed child/default
```

Dans l'Inspector, les références d'override doivent être regroupées dans un header repliable du type :

```text
Overrides
    Interaction Area
    Indication Area
    Interaction Anchor
    Actions
    Executor
    ...
```

Le chemin commun doit donc montrer surtout les données gameplay, pas le wiring.

## 2. Stateful integration helpers

L'intégration Stateful applique la même règle :

```text
explicit Stateful override
    ?? StatefulComponent du scope local de l'Interactive
```

Une `StatefulStateInteractionRule`, `SetStateInteractionExecutor` ou transition helper ne doit pas demander trois fois la même référence lorsqu'ils pilotent le Stateful de l'objet courant.

Une référence/path explicite reste nécessaire et prioritaire pour lire ou muter un autre objet du level.

Interaction core ne gagne aucune dépendance à Stateful : cette résolution reste entièrement dans `integration/stateful`.

## 3. Small state-transition authoring utilities

Ajouter de petites façades d'authoring construites sur les primitives existantes, sans nouveau state-machine framework.

Cas minimum :

```text
StatefulTransitionAction
    From = [closed]
    To = opened
```

qui représente conceptuellement :

```text
StatefulStateInteractionRule(From)
+
SetStateInteractionExecutor(To)
```

Pour une transition longue :

```text
StatefulRunningTransitionAction
    From = [closed]
    Running = opening
    Completed = opened
    Cancelled = closed
```

La fin reste externe par défaut ; une variante/helper timed peut composer `TimedExecution` quand le temps est réellement la policy de completion.

Les helpers peuvent également couvrir le motif fréquent :

```text
AvailableStates
BlockedStates + BlockReason
OtherStates = Hidden
```

pour éviter d'empiler plusieurs rules Stateful répétitives uniquement afin de présenter correctement une transition en cours.

## Non-goals

- pas de découverte récursive magique ;
- pas de graph/state-machine générique ;
- pas de suppression des `InteractionRule` ou executors custom ;
- pas de couplage Stateful dans Interaction core ;
- pas de `AND/OR` générique tant qu'un vrai cas Inventory/gameplay ne le justifie ;
- pas de refactor V4 du lifecycle ou networking.

## Success criterion

La porte de démo doit pouvoir s'authorer approximativement comme :

```text
Door
├── StatefulComponent
└── InteractiveComponent
    ├── InteractionArea3D
    │   └── CollisionShape3D
    ├── Open : StatefulTransitionAction
    │   From = [closed]
    │   To = opened
    ├── Close : StatefulTransitionAction
    │   From = [opened]
    │   To = closed
    └── Unlock : StatefulTransitionAction
        From = [locked]
        To = closed
```

sans `Actions` array, sans `Executor` NodePath, sans Stateful path répété et sans références d'areas/anchor visibles dans le chemin normal d'Inspector.

Si un auteur veut sortir de cette composition locale, il ouvre simplement **Overrides** et renseigne la référence explicite.