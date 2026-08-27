# Stateful

## Purpose

Addon Godot C# réutilisable qui possède l'**état du monde**, indépendamment de toute interaction : une porte est `closed`/`open`/`locked`, une salle `dry`/`flooded`/`draining`, une alimentation `powered`/`unpowered`/`overloaded`.

Le code runtime et ses contrats sont dans le namespace `QuestWorld.State`, sous [`addons/stateful_plugin`](../../../addons/stateful_plugin).

Cet addon répond uniquement à la question **« qu'est-ce qui est vrai dans le monde ? »**. Il ne répond ni à « le joueur peut-il faire ça ? » (`InteractionRule`), ni à « qui effectue la mutation ? » (`InteractionActionExecutor`).

## Delivered

- `StatefulComponent` possède une valeur `StringName` autoritaire, répliquée, persistable et observable. La valeur est libre : le composant ne donne aucune signification universelle à `open`, `flooded` ou `activating`.
- `StateSchema` est une `Resource` optionnelle qui déclare les valeurs acceptées. Elle sert à la validation runtime et editor, pas à décrire une machine à états : aucune transition, garde, entry/exit effect ni hiérarchie n'est modélisée. `Schema == null` accepte n'importe quelle valeur.
- `StatefulSavedState` est le snapshot versionné exposé au système de persistance du projet. L'addon ne stocke aucun fichier.
- Le plugin editor `StatefulEditorPlugin` enregistre `StatefulInspectorPlugin`, qui délègue ses warnings à `StatefulValidator`. Les scripts runtime ne sont pas `[Tool]`.

## Authority, replication and notifications

- `SetState()` est server-only. Il retourne `false` pour un peer non serveur, une valeur absente du `Schema` assigné, ou une valeur déjà appliquée. L'absence de `MultiplayerPeer` compte comme autorité — un jeu sans session est son propre serveur — parce que `Multiplayer.IsServer()` sans peer pousse une erreur *et* répond non, ce qui refusait toute mutation hors session. Voir [`godot-multiplayer-isserver-requires-peer.md`](../../memory/godot-multiplayer-isserver-requires-peer.md).
- La réplication passe par la propriété technique privée `ReplicatedState`. Un `MultiplayerSynchronizer` enfant du composant réplique le chemin `.:ReplicatedState`. Le gameplay n'assigne jamais cette propriété directement.
- Le setter répliqué applique la valeur autoritaire du serveur **sans** revalider le schema : le serveur fait autorité et un schema divergent entre builds ne doit pas désynchroniser un client.
- Trois signaux séparent les scopes consommateurs : `StateChanged` partout, `StateChangedAuthority` uniquement avec autorité (offline, listen host, dedicated server), `StateChangedPresentation` partout sauf sur un dedicated server.
- Les trois portent la **même signature** `(oldState, newState, isSynchronization)`, ce qui permet de brancher un même handler sur plusieurs canaux.

### `isSynchronization` — rattrapage ou événement vécu

`isSynchronization` répond à la seule question qu'une transition seule ne peut pas trancher : cet état **devient-il** vrai ici et maintenant, ou ce peer rattrape-t-il une vérité déjà établie ailleurs ?

| Origine | `isSynchronization` |
| --- | --- |
| `SetState()` autoritaire | `false` |
| Valeur répliquée reçue après la première | `false` |
| **Première** valeur répliquée reçue par un peer (late join) | `true` |
| **`LoadState()`** (restauration de sauvegarde) | `true` |

La transition est émise dans les deux cas, délibérément : une pose ou une animation pilotée par l'état converge ainsi sans rien savoir, et une porte trouvée déjà ouverte joue son ouverture donc finit avec la bonne collision. Ce que le flag permet, c'est de garder les **one-shots** — son, confettis, caméra, notification — pour un changement que le joueur a réellement vécu. Le pattern d'un feedback :

```cs
private void OnStateChangedPresentation(StringName old, StringName @new, bool isSynchronization)
{
    if (isSynchronization) { ApplyPose(@new); return; }
    PlayTransition(old, @new);
}
```

Trois points qui font tenir le contrat :

- le marqueur « première réplication reçue » est remis à zéro dans `_Ready`, parce que `ReplicatedState` est un `[Export]` que le chargement de scène écrit avant l'entrée dans l'arbre et qui le consommerait ;
- il est consommé même par une valeur **égale** à l'état courant. C'est le cas d'un peer qui rejoint un objet que personne n'a touché : le full sync du `MultiplayerSynchronizer` n'émet aucune transition, mais l'arrivée est dépensée, donc la première vraie transition suivante est bien rapportée comme vécue. Un test réseau le prouve dans les deux sens.
- `oldState` sur une synchronisation est l'`InitialState`, pas l'état réellement précédent : un arrivant peut recevoir `idle → activated` là où le monde a fait `idle → activating → activated`. Un consommateur ne doit donc pas supposer que la paire reçue est une arête de la machine, seulement que `newState` est vrai.

## Mutation and dispatch boundary

Le composant applique dès sa création l'invariant du chantier V2 : aucun signal, RPC ou callback externe pendant une mutation.

```text
SetState / LoadState / replication
  ↓ validation
ApplyStateCore   → mutate only, returns StateTransition?
  ↓ mutation complete
DispatchStateTransition(transition, isSynchronization)
  ├─ StateChanged
  ├─ StateChangedAuthority
  └─ StateChangedPresentation
```

`ApplyStateCore` et `DispatchStateTransition` sont `internal` : les tests vérifient la transition sans agrandir l'API publique de l'addon.

## Schema validation

- `Schema == null` : valeur libre.
- `Schema` assigné : `SetState()` refuse une valeur non déclarée, avec un warning, et ne mute ni n'émet rien.
- `InitialState` non déclaré : `_Ready()` publie une erreur mais **conserve** la valeur. Aucune correction silencieuse n'est appliquée à l'état du monde ; la configuration est signalée dans l'Inspector avant le lancement.
- `LoadState()` lève `ArgumentOutOfRangeException` pour une version inconnue et pour un état non déclaré par le schema courant. Une sauvegarde plus ancienne qu'une évolution de schema est un problème de migration explicite pour le projet hôte, pas une valeur à ignorer silencieusement.

## Persistence boundary

`StatefulSavedState` contient uniquement une version (`1`) et un `StringName`. `LoadState` réutilise le chemin commun de changement d'état et rejoue les signaux même lorsque la valeur restaurée est identique à la valeur courante, avec `isSynchronization = true` : le monde était déjà dans cet état avant que ce process existe, donc rien ne vient de se produire. Aucun fichier, service global ou backend n'est créé.

## Explicit configuration and validation

`StatefulValidator` couvre :

- `StatefulComponent` : `InitialState` vide, `InitialState` absent du `Schema` assigné ;
- `StateSchema` : aucune valeur déclarée, valeur vide, valeur dupliquée.

Le validator lit les propriétés via l'API Godot (`Get`) afin de fonctionner avec les placeholders editor, comme `InteractionValidator`.

## Single world-state component

`InteractionStateful` (enum `Idle/Activating/Activated/Deactivating`) et son `InteractionSavedState` sont supprimés par la Task 12 du chantier [interaction-v2-architecture](../interaction/planned/interaction-v2-architecture.md). `StatefulComponent` est désormais le seul composant d'état monde du projet, et `InteractiveComponent` ne le référence pas : l'interaction ne le lit qu'à travers des rules pures.

## Validation

```bash
csharpier format .
dotnet build
GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot dotnet test
```

Les tests couvrent la mutation core sans signal, le dispatch de chaque scope exactement une fois, l'application de `InitialState` sans signal au `_Ready`, l'application autoritaire, l'absence de changement pour une valeur identique, la valeur libre sans schema, le refus d'une valeur hors schema, la conservation d'un `InitialState` hors schema, l'application d'une valeur répliquée sans validation de schema, le snapshot/restauration y compris pour une valeur identique et son dispatch en synchronisation sur les trois canaux, le flag à `false` pour un changement autoritaire vécu, le refus d'une version inconnue et d'un état hors schema, la query pure du schema, l'application autoritaire hors arbre sans peer multijoueur, et l'ensemble des warnings du validator.

## Assumptions and deferred work

- La propriété `ReplicatedState` reste visible dans l'Inspector. Voir [`godot-private-export-inspector-visibility.md`](../../memory/godot-private-export-inspector-visibility.md).
- Aucune FSM, aucun graphe de transitions, aucun effect d'entrée/sortie : ce serait un autre système.
- Les primitives d'intégration interaction (`StatefulStateInteractionRule`, `SetStateInteractionExecutor`) sont livrées par la Task 8 du chantier V2 et vivent dans [`addons/interaction_plugin/integration/stateful`](../../../addons/interaction_plugin/integration/stateful) : la dépendance va de l'interaction vers le stateful, jamais l'inverse. Le premier consommateur est `LeverWall`, piloté à distance par `Button.tscn` sans aucun script de glue. Voir [interaction.md](../interaction/interaction.md).
