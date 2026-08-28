# Interaction Presentation — refresh par frame, distance et progressions

## Problème

Trois manques distincts, qui se règlent au même endroit.

**1. Le prompt n'est pas rafraîchi par frame.** `InteractionPresenter._Process` appelle `RefreshIndications()`,
donc chaque cible indiquée est rebindée chaque frame avec un snapshot frais. Le prompt de la cible focus,
lui, n'est rebindé que par `Refresh()`, appelé sur changement de focus ou de statut. Tout champ qui varie
en continu serait donc frais pour les indications et périmé pour le prompt : un champ qui marche selon le
widget qui le lit.

**2. Aucune progression n'atteint les widgets.** `InteractionInteractor` expose déjà les deux :

```text
TryGetGestureProgress(out inputActionName, out progress)     // seuil de hold local
TryGetExecutionProgress(out actionId, out progress)          // exécution longue prédite
```

Les deux sont publiques et calculées chaque frame, mais rien ne les fait descendre dans
`InteractionActionPresentation`. Un widget qui veut une barre de hold doit aujourd'hui aller chercher
l'interacteur lui-même, ce qui court-circuite le contrat de présentation.

**3. Le widget ne peut pas connaître sa distance.** `InteractionTargetPresentation` porte `Interactive`
et `IsFocused`. Le widget atteint donc la position monde de l'ancre, mais pas celle de l'observateur : il
lui manque la caméra ou l'interacteur. Or la distance est une donnée que la couche spatiale calcule déjà
pour son scoring et jette ensuite.

## Ce qu'on expose, et où

La distinction porte sur la portée de la donnée, pas sur sa nature.

| Donnée | Où | Source |
| --- | --- | --- |
| `Distance` (unités monde) | `InteractionTargetPresentation` | couche spatiale / détecteur |
| `IsHoldable` | `InteractionActionPresentation` | `HoldThreshold > 0` |
| `HasTimedExecution` | `InteractionActionPresentation` | `ComputeInteractionDuration(context) > 0` |
| `HoldProgress` (0..1) | `InteractionActionPresentation` | `TryGetGestureElapsed` / seuil de l'action |
| `HoldElapsed` (secondes) | `InteractionActionPresentation` | `TryGetGestureElapsed` |
| `ExecutionProgress` (0..1) | `InteractionActionPresentation` | `TryGetExecutionProgress` |

Les deux progressions sont **par action** et pas par cible : un hold vise le seuil d'une action précise,
et une exécution longue appartient à une action précise. Les mettre sur la cible obligerait chaque widget
d'action à refiltrer par `ActionId`.

`InteractiveComponent.GetPresentation(interactor, isFocused)` reçoit déjà l'interacteur, donc il peut lire
les deux progressions sans nouvelle dépendance. La distance a besoin de la position de l'observateur, qui
est aujourd'hui privée (`_resolvedInteractionOrigin`) : soit l'interacteur expose un accesseur, soit le
détecteur remplit le champ au passage (voir [`interaction-detector.md`](./interaction-detector.md)).

## Ne pas confondre les deux barres

C'est la confusion que le §18.1 de l'architecture V2 interdit explicitement, et exposer les deux champs
côte à côte la rend facile à commettre.

- **`HoldProgress`** est la **sélection** : le `HoldThreshold` d'une `InteractionActionDefinition`, un
  geste 100 % local qui sert uniquement à départager plusieurs actions sur un même input. Il n'atteint
  jamais la commande autoritaire. Une action seule sur son input a un seuil de zéro et donc jamais de
  progression.
- **`ExecutionProgress`** est l'**action** : le hack, la fouille, l'activation longue. Le chrono est
  autoritaire et vit sur le target ; ce que le client dessine est une prédiction bâtie sur
  la query `ComputeInteractionDuration` de l'executor, recalée par l'acquittement `InteractionStarted`
  (`ExpectedDuration` est supprimée par P2.1 de [interaction-v3](interaction-v3.md)).

Empiler les deux est légal (tenir pour choisir, puis tenir pendant que ça s'exécute) et produit deux
barres successives. Un widget qui n'en veut qu'une doit choisir laquelle, pas les additionner.

## Refresh par frame

Le prompt doit être rebindé chaque frame, sinon `HoldProgress` ne peut rien remplir. Concrètement :
appeler le chemin de bind depuis `_Process` comme le fait déjà `RefreshIndications`, ce qui coûte un
`GetPresentation` par frame pour la cible focus — on en paie déjà un pour **chaque** cible indiquée, donc
la dépense est cohérente avec l'existant.

Le risque à vérifier : le rebind ne doit **que** rappeler `Bind`, jamais ré-instancier les widgets
d'action. Si le nombre ou l'ordre des actions présentées est stable, la liste de widgets reste en place ;
seule une action qui apparaît ou disparaît recompose la liste. Un test doit couvrir « le widget d'action
n'est pas recréé entre deux frames de hold ».

## Invariant

N'exposer que des grandeurs physiques nommées, jamais le score brut de la couche spatiale. Le score d'un
détecteur d'aim est un angle, celui d'un détecteur de proximité un ratio : un widget qui lirait `Score`
casserait au changement de détecteur. `Distance` veut dire la même chose partout, `Alignment` aussi le
jour où quelqu'un en aura besoin. Pas de sac de métriques génériques.

## Hors périmètre

- Aucun nouveau signal : la présentation reste tirée (`GetPresentation`), pas poussée.
- Aucun changement du contrat réseau : les trois champs sont dérivés d'états déjà locaux ou déjà répliqués.
- Le rythme d'update des indications ne change pas, il est déjà par frame.

## Questions tranchées

- **`Distance` est mesurée depuis l'`InteractionOrigin`**, le corps. C'est la grandeur que `IsWithinRange`
  applique déjà, donc un widget qui s'anime dessus est d'accord avec le moment où l'interaction devient
  réellement possible ; la mesurer depuis la caméra ferait annoncer 4 m à un widget là où la portée en
  compte 2,5. Elle est remplie par le détecteur (`GetInteractionDistance`), seul porteur des origines
  depuis la Task 10 : l'interacteur ne gagne aucun accesseur.
- **L'absence est représentée par l'absence de valeur** : `float?` pour les deux progressions, comme
  `GetInteractionPresentation()` retourne déjà `InteractionTargetPresentation?` pour l'absence de focus.
  Zéro veut donc dire zéro, et une barre qui s'anime distingue « pas de barre » de « vient de commencer »
  grâce aux capacités `IsHoldable` et `HasTimedExecution`, disponibles même au repos.
- **Le rebind par frame est livré ici**, pas séparément : c'est lui qui rend `HoldProgress` observable, et
  le livrer à part aurait posé un prérequis sans consommateur.

## État

**Livré.** `InteractionTargetPresentation` porte `Distance`, `InteractionActionPresentation` porte
`HoldProgress` et `ExecutionProgress`, et `InteractionPresenter._Process` appelle `Refresh()` au lieu du
seul `RefreshIndications()`. Trois constats de l'implémentation :

1. **Le problème n°1 était déjà masqué, pas absent.** `RecalculateFocus` émet `InteractionStatusChanged`
   à *chaque* frame focusée — le retry des actions automatiques de la Task 7 en dépend — et le presenter
   rebind sur ce signal : le prompt était donc frais par accident. Le rebind explicite depuis `_Process`
   rend la fraîcheur indépendante de ce signal — et permet du même coup de le **gater**, ce que la
   pain-point « réintroduire `NotifyStatusChanged` ? » envisageait. Les deux chemins ont d'abord coexisté,
   ce qui doublait tout : `Refresh()` appelle aussi `RefreshIndications()`, donc c'était 2 × (1 + N)
   snapshots par frame, pas un de plus. Plutôt que dédupliquer par une garde de frame — un pansement —
   le push par frame a été supprimé : le signal n'est plus émis que sur un vrai changement, et le
   presenter ne rafraîchit qu'une fois par frame en régime stable.
2. **`HoldProgress` est normalisé sur le seuil de chaque action**, et la source n'est donc pas
   `TryGetGestureProgress` mais un nouvel accesseur `TryGetGestureElapsed` sur l'interacteur. La première
   version réutilisait la progression unique du geste, normalisée sur le seuil le plus long de l'input :
   une barre dessinée autour de la touche n'atteignait alors jamais un sur la plus courte de deux actions,
   ce qui ne tient pas pour l'UI. Exposer l'elapsed brut était le prix, et il est petit. `HoldElapsed`
   descend aussi dans la présentation, parce qu'un widget qui veut afficher des secondes ne peut pas les
   reconstruire depuis le ratio : le seuil qu'il faudrait multiplier n'y est pas. Une action sans seuil ne
   rapporte toujours rien, ce qui est la phrase de ce document : « une action seule sur son input a un
   seuil de zéro et donc jamais de progression ».
3. **Le prompt d'action par défaut affiche le hold.** Sa barre est visible dès que `IsHoldable` est vrai,
   avec `HoldProgress ?? 0`; l'exécution reste exposée pour les widgets de jeu via `HasTimedExecution` et
   `ExecutionProgress`, sans rendu imposé par le plugin.
