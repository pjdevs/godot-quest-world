# Interaction Presentation — refresh, distance et progressions

> **Superseded by V4.** Ce document conserve la décision de présentation livrée par la Task 13, mais
> son ancien modèle de progression temporelle a été remplacé par
> `InteractionExecutionPresentation` et les producteurs génériques V4.

## Contrat courant

La présentation expose uniquement des grandeurs nommées et stables :

| Donnée | Portée | Source |
| --- | --- | --- |
| `Distance` | `InteractionTargetPresentation` | détecteur, depuis `InteractionOrigin` |
| `HoldProgress` / `HoldElapsed` | `InteractionActionPresentation` | geste local de sélection |
| `Progress` nullable | `InteractionExecutionPresentation` | producteur de l'exécution active |

Le hold et l'exécution sont deux phases distinctes. Le hold choisit une action parmi plusieurs actions
partageant un input ; l'exécution représente le travail lancé. Les afficher ensemble est légal, mais un
widget ne les additionne jamais.

Une progression d'exécution n'est pas une propriété de l'action au repos. Elle existe uniquement dans
le slot actif joint au widget par `ActionId`. Elle peut provenir d'une valeur publiée, d'un `Callable`
local ou d'un sample linéaire révisionné. `null` signifie qu'aucun producteur ne souhaite exposer de
progression ; zéro reste une vraie valeur.

## Visibilité V4

`InteractionAction.ExecutionVisibility` décide quels peers reçoivent le slot transitoire :

- `RequesterOnly` : autorité et demandeur ;
- `Replicated` : autorité et peers visibles par un `InteractionExecutionSynchronizer` explicitement
  relié au target, late join compris ;
- `AuthorityOnly` : autorité seulement, sans supprimer les ACK lifecycle du demandeur.

Le synchronizer transporte des snapshots de présentation, jamais l'autorité d'exécution ni l'état
persistant du monde. Ce dernier reste la responsabilité de Stateful.

## Refresh par frame

`InteractionPresenter._Process` relit les snapshots et rappelle `Bind` à chaque frame locale. Il ne
réinstancie les widgets que si la cible, la scène ou la liste des actions présentées change. Cela garde
le hold, la progression, les règles et la projection à jour sans transformer les signaux en flux par
frame.

`InteractionStatusChanged` reste une notification d'événement. Un consommateur qui exige une fraîcheur
continue tire un nouveau snapshot, comme le presenter fourni.

## Invariants conservés

- La distance est mesurée depuis `InteractionOrigin`, comme la portée gameplay.
- Le score brut d'un détecteur n'est jamais exposé à l'UI.
- L'absence de progression est une absence de valeur, pas zéro.
- Une liste de widgets stable n'est pas reconstruite pendant une animation.
- La présentation transitoire et la vérité durable du monde utilisent deux canaux distincts.

## État

**Livré et migré vers V4.** Le comportement de refresh et les grandeurs spatiales restent ceux de la
Task 13. La progression d'exécution vit désormais dans un read model séparé, extensible et
éventuellement répliqué.
