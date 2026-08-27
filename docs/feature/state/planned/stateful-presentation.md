# Stateful Presentation — vérité autoritaire et représentation locale

## Statut

**Proposal.** Ce chantier part d'un problème visible dans le multiplayer Interaction — le joueur prédit immédiatement une exécution longue, mais le monde attend encore la mutation puis la réplication du `StatefulComponent` — et l'extrait volontairement hors d'Interaction.

Le besoin est plus général : un mur de niveau, un ascenseur déclenché par une `Area3D`, une salle qui s'allume ou s'éteint, une alimentation qui bascule, un objet piloté à distance ou n'importe quel script gameplay peuvent connaître localement la représentation attendue avant que la vérité autoritaire correspondante ait atteint ce peer.

Le système proposé appartient donc à [`stateful_plugin`](../../../../addons/stateful_plugin), avec une intégration optionnelle depuis Interaction. La dépendance reste dans le même sens qu'aujourd'hui : Interaction peut connaître Stateful par son bridge ; Stateful ne connaît jamais Interaction.

## Problème

[`StatefulComponent`](../stateful.md) a aujourd'hui un contrat volontairement étroit et utile :

> `Stateful.State` répond à « qu'est-ce qui est vrai dans le monde ? ».

En multiplayer, cela implique qu'un client ne voit une transition d'état qu'après :

```text
intention locale
  → commande vers le serveur
  → validation / mutation autoritaire
  → réplication du Stateful
  → StateChangedPresentation
  → réaction visuelle
```

Interaction masque déjà une partie de ce délai pour son propre protocole : la progression d'une exécution longue est prédite localement à partir de `InteractionActionExecutor.ComputeInteractionDuration()`, puis recalée par l'ACK `InteractionStarted`. Cela rend la barre immédiate, mais pas les conséquences visibles de l'action.

Exemple : le joueur appuie sur un bouton qui met un mur en `raising`. La barre peut commencer à `t = 0`, alors que l'animation du mur attend encore que le serveur applique `raising` puis que le `MultiplayerSynchronizer` le réplique.

Le problème n'est pourtant pas propre à une interaction :

- `LeverWall` possède déjà son propre `StatefulComponent` et n'est pas lui-même un `InteractiveComponent` ;
- un ascenseur peut être déclenché par une simple zone sans aucune interaction explicite ;
- une salle peut passer `powered` / `unpowered` depuis une logique de level ou de puzzle ;
- une porte, une lumière, une alarme ou une machine peuvent être pilotées à distance par plusieurs systèmes différents.

Faire de `StatefulComponent` lui-même un état prédictif résoudrait le symptôme au mauvais niveau.

## Invariant central : la vérité ne devient jamais prédictive

`StatefulComponent` ne change pas de sens.

```text
Stateful.State
    = vérité locale autoritaire
    = valeur répliquée
    = valeur persistable
    = valeur lue par le gameplay
```

En particulier, **aucune API `SetPredictedState()` n'est ajoutée à `StatefulComponent`**.

Rules, quêtes, save, collision, navigation, réservations et logique gameplay continuent de lire uniquement `Stateful.State` et ses signaux actuels. `SetState()` reste autoritaire.

La prédiction introduit un second read model, exclusivement destiné à ce que ce peer doit présenter :

```text
StatefulComponent
    « what is true? »

StatefulPresentation
    « what should this peer currently show? »
```

Cela conserve la propriété la plus importante du framework Stateful : lire `Stateful.State` ne demande jamais de savoir si une valeur est confirmée, spéculative, locale ou en cours de réconciliation.

## Proposition : `StatefulPresentation`

Ajouter un composant optionnel dans `stateful_plugin` :

```text
Door / Wall / Room / Elevator
├── StatefulComponent
│   └── MultiplayerSynchronizer
├── StatefulPresentation
├── Gameplay
└── VisualPresentation
```

`StatefulPresentation` référence explicitement un `StatefulComponent` et expose une valeur effective :

```text
PresentedState = local presentation override ?? Stateful.State
```

Sans override, il est un simple miroir de la vérité Stateful. Avec un override, il permet à la présentation locale d'anticiper temporairement cette vérité sans la modifier.

### Esquisse d'API

Le nom exact des petits types peut évoluer à l'implémentation, mais le contrat visé est :

```csharp
[GlobalClass]
public partial class StatefulPresentation : Node
{
    [Export]
    public StatefulComponent? Stateful { get; set; }

    public StringName PresentedState { get; }
    public bool HasLocalOverride { get; }

    public StatePresentationHandle OverrideState(StringName state);
    public void ClearOverride(StatePresentationHandle handle);
}
```

Un override retourne un **handle/génération**. Seul le handle encore actif peut le retirer. Un callback retardé appartenant à une ancienne intention ne doit jamais rollback une présentation plus récente.

Une seule valeur peut être présentée à la fois, donc le composant n'a pas besoin d'une pile de predictions : un nouvel override remplace le précédent. Le token sert à protéger le lifecycle, pas à empiler plusieurs vérités concurrentes.

Le composant expose aussi un signal de changement de **valeur présentée**, distinct de `Stateful.StateChanged*`. Il doit permettre au consommateur de distinguer au minimum une valeur actuellement issue d'un override local d'une valeur issue du Stateful, et préserver l'information `isSynchronization` quand le changement vient du Stateful. La forme exacte du signal peut rester légère ; le point essentiel est qu'une réconciliation qui ne change pas `PresentedState` ne rejoue pas la transition visuelle.

## Réconciliation

L'override est temporaire et l'autorité gagne toujours.

### Prediction qui se confirme

```text
client                        serveur

State = idle
Presented = idle

Override(activating)
Presented = activating
                              SetState(activating)

< réplication activating
State = activating
override supprimé
Presented = activating
```

La dernière étape ne change pas la valeur effective. Le composant retire donc l'override **sans émettre une seconde transition `activating → activating`**. Une animation commencée immédiatement ne redémarre pas quand le réseau confirme ce que le joueur voyait déjà.

### Prediction refusée

```text
State = idle
Override(activating)
Presented = activating

< refus / failure
ClearOverride(handle)
Presented = idle
```

La présentation rollback vers la vérité actuellement connue. `Stateful.State` n'a jamais bougé.

### L'autorité choisit autre chose

N'importe quel vrai changement de `Stateful.State` **supersède l'override local courant**.

```text
State = idle
Presented = activating   // override local

< authoritative locked

State = locked
Presented = locked
```

Il n'est pas nécessaire que l'état autoritaire reçu soit exactement celui qui avait été anticipé. Cela couvre naturellement un serveur qui refuse implicitement la transition attendue, un autre joueur qui gagne une race, ou une transition intermédiaire que le peer n'a jamais reçue.

Cette règle évite aussi de bloquer un override sur `activating` si la réplication observable saute directement à `activated`.

## Pourquoi un override de présentation, pas un « predicted state »

L'API générique ne doit pas supposer que l'override est toujours spéculatif.

Une intégration peut l'utiliser :

- **avant** une réponse serveur : représentation réellement prédite ;
- **après** un ACK autoritaire mais **avant** que la réplication Stateful correspondante arrive : représentation déjà confirmée, seulement en avance sur le canal de réplication ;
- depuis un système non-Interaction qui possède son propre protocole de prediction/réconciliation.

`StatefulPresentation` ne décide donc ni pourquoi l'override existe, ni quand il faut le créer. Il fournit uniquement le mécanisme local et la règle de convergence vers `Stateful.State`.

## Frontière simulation / présentation

Un consommateur choisit explicitement le niveau qu'il observe.

| Besoin | Source |
| --- | --- |
| Rules / gameplay / quêtes | `Stateful.State` |
| Collision / navigation / simulation | `Stateful.State` |
| Save | `Stateful.State` |
| Réplication | `Stateful.State` |
| Mesh, matériau, lumière, son, VFX, UI | `StatefulPresentation.PresentedState` |
| Feedback local anticipé | `StatefulPresentation.PresentedState` |

Cela implique une limite volontaire : **on ne prédit pas la physique en faisant de la présentation**.

Le `LeverWall` actuel est un bon cas test. Son `AnimationPlayer` déplace aussi la collision ; il écoute donc volontairement `StateChanged` et doit continuer à faire tourner cette partie autoritairement. Pour obtenir un mouvement visuel immédiat sans déplacer la collision en avance, la géométrie de présentation et la géométrie/collision autoritaire doivent être séparables. `StatefulPresentation` rend cette séparation possible, il ne prétend pas prédire la simulation.

Même sans prediction Interaction, le mur peut alors devenir un consommateur normal du système : sa logique/collision observe le Stateful, son rendu observe le StatefulPresentation. Le mur n'a toujours aucune raison d'être interactif lui-même.

## Interaction reste un consommateur optionnel

Le problème initial demande malgré tout un petit point d'extension dans le core Interaction.

Aujourd'hui, une `InteractionAction` possède principalement :

```text
InteractionAction
├── Definition
├── Executor
└── Rules
```

La proposition est d'autoriser un comportement local optionnel distinct de l'executor :

```text
InteractionAction
├── Definition
├── Executor          // authoritative execution semantics + gameplay mutation
├── Prediction?       // optional owning-client presentation prediction
└── Rules
```

Nom de travail : `InteractionActionPrediction`.

Le core Interaction connaît **quand** une intention locale est prédite, acceptée, refusée, complétée ou annulée. Le predictor sait **quel feedback local** produire pour cette action. Le core n'apprend rien sur Stateful, les animations ou les objets du jeu.

Une action sans predictor conserve exactement le comportement actuel.

### Deux predictions différentes, à ne pas fusionner

Le mécanisme existant de durée reste séparé :

```text
PredictedExecution
    = prediction générique du lifecycle temporel de l'interaction

InteractionActionPrediction
    = prediction optionnelle des conséquences / feedbacks locaux
```

`InteractionActionExecutor.ComputeInteractionDuration(context)` **reste sur l'executor**.

La durée est une sémantique de l'exécution autoritaire : le serveur en a besoin pour réserver et terminer l'exécution. Le client est simplement autorisé à exécuter la même query pure afin d'anticiper ce contrat. La déplacer dans un objet nommé `Prediction` rendrait le dedicated server dépendant d'une abstraction de présentation et recréerait une seconde source de vérité.

Le predictor peut recevoir la durée calculée comme donnée read-only s'il veut synchroniser une animation, mais il ne la définit jamais.

Invariant :

> Prediction never defines authoritative execution semantics. It may only anticipate them or provide local presentation feedback.

## Intégration `interaction/stateful`

Le premier consommateur générique du nouveau hook serait une prediction Stateful dans [`addons/interaction_plugin/integration/stateful`](../../../../addons/interaction_plugin/integration/stateful).

Elle relie :

```text
Interaction lifecycle
        ↓
Stateful interaction prediction
        ↓
StatefulPresentation.OverrideState(...)
        ↓
local visuals
```

Elle ne fait jamais `Stateful.SetState()` côté client.

### Lifecycle souhaité

Pour un `TransitionStateInteractionExecutor` :

```text
local request
    → override RunningState immédiatement

InteractionStarted ACK
    → conserver l'override si le Stateful n'a pas déjà rattrapé

Stateful réplique RunningState
    → l'override est absorbé sans redémarrer la présentation

InteractionCompleted ACK
    → si nécessaire, présenter CompletedState immédiatement
       jusqu'à la réplication correspondante

InteractionCancelled ACK
    → même principe avec CancelledState

Rejected / Failed avant transition autoritaire
    → clear de l'override possédé par cette prediction
```

Pour un `SetStateInteractionExecutor`, l'action peut être instantanée et avoir une durée d'exécution nulle : le predictor reste utile. Il présente `TargetState` dès la requête puis laisse la réplication autoritaire l'absorber.

Cela traite le cas important que la prediction temporelle actuelle ne peut pas traiter : **une action instantanée peut n'avoir aucune barre à prédire mais énormément de feedback monde à rendre immédiat**.

### Pas de duplication des states

L'intégration ne doit pas demander à l'auteur de configurer :

```text
Executor.TargetState = open
Prediction.PredictedState = open
```

ou :

```text
Executor.RunningState = activating
Prediction.RunningState = activating
```

Les states prédits sont les mêmes sémantiques que celles déjà définies par l'executor autoritaire. Le predictor doit donc les lire depuis la configuration de cet executor — directement avec des adapters spécialisés au départ, ou via une petite query commune dans l'intégration si cela devient utile.

Le choix d'API peut attendre l'implémentation ; l'invariant est fixé : **une seule authoring source pour les states de l'exécution**.

## Cas d'usage hors Interaction

### `LeverWall`

Le bouton peut rester l'Interactive et le mur rester un objet Stateful indépendant.

```text
Button Interaction
    → prediction locale du StatefulPresentation du mur
    → serveur SetState(rising)
    → Stateful du mur se réplique
    → presentation converge
```

La logique du mur n'est pas déplacée dans Interaction. Une autre source peut changer exactement le même mur sans passer par le bouton.

### Ascenseur déclenché par une zone

```text
player local entre dans Area3D
    → système de trigger local : OverrideState(moving/opening)
    → commande / validation serveur propre au système de level
    → serveur SetState(...)
    → réplication
```

Aucune `InteractionAction` n'est nécessaire. Le système de trigger utilise directement `StatefulPresentation` comme primitive de feedback local.

### Salle alimentée / désalimentée

Une salle peut posséder :

```text
Room
├── StatefulComponent       powered / unpowered / emergency
├── StatefulPresentation
├── LightsPresentation
├── AudioPresentation
└── RoomGameplay
```

Le puzzle d'alimentation, une Area, une interaction distante ou un script de mission peuvent tous muter le même Stateful côté autorité. Les lumières et sons n'ont besoin de connaître aucun de ces producteurs : ils observent uniquement la représentation effective.

C'est la séparation recherchée à l'origine par le framework State : l'état du monde existe indépendamment de la manière dont un joueur ou un système le déclenche, tout en restant intégrable facilement partout.

## Plan d'action

### 1. Livrer `StatefulPresentation` dans le core State

- composant optionnel avec référence explicite au `StatefulComponent` ;
- `PresentedState` et `HasLocalOverride` ;
- override local protégé par handle/génération ;
- un nouvel override remplace l'ancien ;
- clear d'un vieux handle = no-op ;
- toute vraie transition Stateful supersède l'override courant ;
- arrivée autoritaire égale à la valeur présentée = réconciliation silencieuse, sans rejouer la transition ;
- validation de l'override contre le `StateSchema` du Stateful ;
- aucune réplication, persistence ou boucle `_Process` propre.

Tests minimum : miroir sans override, override local, rollback, réconciliation identique sans double signal, autorité divergente qui gagne, stale handle incapable de rollback une prediction plus récente, state hors schema refusé, late join/synchronization correctement propagé.

### 2. Valider la frontière sur un vrai objet monde

Faire du `LeverWall` le premier cas d'école : distinguer ce qui doit rester piloté par `Stateful.State` parce que cela affecte collision/simulation, et ce qui peut écouter `StatefulPresentation` pour commencer immédiatement côté visuel.

Le but n'est pas forcément de refactorer toute sa géométrie dans la première task, mais de vérifier que l'API ne suppose jamais que son consommateur est un Interactive.

### 3. Ajouter le hook `InteractionActionPrediction`

- optionnel sur `InteractionAction` ;
- exécuté uniquement pour la prediction locale du peer propriétaire ;
- lifecycle request / started / rejected-or-failed / completed / cancelled ;
- aucune mutation gameplay autoritaire ;
- une action sans predictor reste inchangée ;
- le predictor peut lire la durée d'exécution calculée, jamais la définir.

La correlation réseau reste celle du protocole Interaction courant ; l'éventuelle introduction future d'un `RequestId` est un chantier indépendant et ne doit pas être cachée dans cette feature.

### 4. Ajouter l'intégration Stateful

Fournir le ou les predictors génériques nécessaires pour `SetStateInteractionExecutor` et `TransitionStateInteractionExecutor`.

Ils :

- ciblent un `StatefulPresentation` ;
- tirent leurs states depuis l'executor autoritaire sans duplication d'authoring ;
- ouvrent l'override dès la requête locale ;
- rollback sur refus/failure ;
- laissent la réplication Stateful absorber la prediction ;
- peuvent utiliser les ACK terminales pour présenter immédiatement `CompletedState` / `CancelledState` si le canal Stateful n'a pas encore rattrapé.

### 5. Tester la vraie séquence réseau

Ajouter des tests qui couvrent explicitement l'ordre temporel, pas seulement la valeur finale :

1. le feedback Stateful commence avant l'ACK serveur ;
2. un refus restaure la vérité autoritaire ;
3. une confirmation identique ne redémarre pas l'animation / le signal de présentation ;
4. une valeur autoritaire différente supersède la prediction ;
5. une action instantanée bénéficie de la prediction même avec `ExecutionDuration == 0` ;
6. `ComputeInteractionDuration` reste la source unique pour serveur et client ;
7. completion/cancellation ACK puis réplication Stateful ne produit pas de double transition visible.

## Hors périmètre

- aucune FSM ou règle de transition dans Stateful ;
- aucune mutation client de `Stateful.State` ;
- aucune prediction générique de collision, navigation ou physique ;
- aucune réplication de `StatefulPresentation` ;
- aucune persistence de l'override local ;
- aucun système global de rollback gameplay ;
- aucun clock sync supplémentaire ;
- aucun déplacement de `ComputeInteractionDuration` hors de `InteractionActionExecutor` ;
- aucun couplage de `stateful_plugin` vers `interaction_plugin`.

## Critère de réussite

Le système est réussi si ces deux phrases restent vraies simultanément :

> **`Stateful.State` est toujours la vérité du monde.**

> **Un peer peut présenter immédiatement la conséquence attendue d'une intention locale sans attendre que cette vérité lui revienne par le réseau.**

Interaction devient alors un producteur particulièrement important de cette anticipation, mais seulement un producteur parmi d'autres.