# Interaction Framework V4 — Execution lifecycle & presentation proposal

> **Status: Architecture accepted and delivered.** Ce document fixe les intentions, invariants et frontières V4.
> La réalisation concrète, les APIs finales, le transport Godot et les trois tranches de migration
> sont fixés séparément dans
> [`interaction-v4-implementation-spec.md`](./interaction-v4-implementation-spec.md).
>
> La V4 ne cherche pas à redessiner V2/V3 pour le plaisir. Elle part d'une friction apparue en utilisant réellement le framework sur des cas plus riches : interactions longues, feedback monde, progression visible par plusieurs joueurs, exécutions pilotées par un système métier, progression discrète/non temporelle, et UI où une même exécution peut être affichée dans le prompt ou directement sur l'objet.

## Goal

Préserver les garanties acquises de V2/V3 — commande autoritaire, executor unique, réservation, groupes de concurrence, ACK, cancellation, rules pures — tout en retrouvant une propriété essentielle de l'ancien framework Unreal :

> Interaction décide quand une exécution commence, qui la possède et quand elle se termine ; le gameplay reste libre de décider ce qui se passe pendant cette exécution.

Le framework doit rendre trivial le cas commun :

```text
interaction longue de 5 secondes
→ busy / reserved
→ progression générique
→ completion automatique
```

sans transformer cette forme courante en sémantique fondamentale de toute interaction.

Il doit tout autant rendre naturel :

```text
interaction démarre
→ terminal lance son propre système de hack répliqué
→ progression arbitraire du terminal
→ terminal décide quand terminer l'exécution
```

ou :

```text
interaction démarre
→ système termine étape A
→ Progress = .33
→ système termine étape B
→ Progress = .66
→ système termine étape C
→ CompleteExecution
```

Le résultat doit rester naturel dans Godot, exploitable depuis C#, GDScript ou une future implémentation GDExtension, et ne jamais exiger qu'un renderer soit une progress bar ou même une UI.

---

# 1. Why V4 exists

## 1.1 Le problème n'est pas « afficher une progress bar »

`InteractionActionPresentation` expose aujourd'hui `ExecutionProgress`. Cette donnée a été ajoutée pour rendre observable la prediction locale d'une interaction longue.

Cela a résolu le besoin immédiat du prompt, mais mélange deux read models différents :

```text
InteractionActionPresentation
    « que peut faire cet interactor sur cette cible ? »

Interaction execution
    « qu'est-ce qui est actuellement en train de s'exécuter sur cette cible ? »
```

Une action proposée est relative à un interactor : elle peut être Allowed, Blocked ou Hidden, avoir un input, un hold threshold et une présentation locale différente pour deux joueurs.

Une exécution active appartient au target. Elle peut continuer alors que l'action correspondante n'est plus présentable, être visible par un joueur qui n'est dans aucune zone d'interaction, ou alimenter un shader, un son, une animation ou un widget monde sans aucun prompt.

`ExecutionProgress` doit donc quitter `InteractionActionPresentation`.

## 1.2 Le timer a pris trop de place dans le core

Le modèle actuel encode directement une durée dans `InteractionExecutionRunning`, conserve `Duration` et `Elapsed` dans l'exécution autoritaire, expose `ComputeInteractionDuration()` sur tout `InteractionActionExecutor`, puis duplique une représentation temporelle prédite dans l'interactor client.

Ce modèle est pratique pour une action simplement temporisée, mais il donne au timer un statut plus fondamental qu'il ne mérite.

Si toute UI et toute progress bar disparaissent, le core Interaction a toujours besoin de savoir :

```text
execution accepted
execution remains active
execution completed / cancelled / failed
```

Il n'a pas besoin de connaître :

```text
duration
elapsed
remaining
deadline
normalized timer progress
```

Le timer est donc une **politique de terminaison et de progression très commune**, pas le primitive de l'exécution.

## 1.3 Le cas custom doit redevenir aussi simple que dans l'ancien framework

L'ancien plugin Unreal exposait essentiellement :

```text
OnStartInteractionInput
StartInteractionPhase
...
EndInteractionPhase
OnEndInteractionInput
```

Un terminal pouvait donc :

```text
interaction starts
→ terminal starts its replicated timer / hack session

interaction ends or is cancelled
→ terminal stops / rolls back its own system
```

Le nouveau framework apporte de meilleures garanties — `ExecutionId`, executor unique, autorité, réservation, groupes de concurrence, ACK, rejection et cancellation — mais ne doit pas acheter ces garanties au prix d'une perte d'escape hatch.

Le chemin custom doit redevenir un chemin de première classe, pas le contournement d'une architecture pensée pour les timers.

## 1.4 Une exécution est server-owned, pas nécessairement server-only

V3 a volontairement gardé `_activeExecutions` server-only et envoyé les ACK uniquement au demandeur. Les autres peers observaient `Stateful` ou le système métier.

Le terminal de hack révèle une troisième catégorie légitime :

> une exécution est une vérité transitoire de l'Interactive qui peut devoir être observable par des peers n'ayant aucune relation d'interaction locale avec lui.

Le serveur reste le seul propriétaire de la mutation et du lifecycle autoritaire. En revanche, les autres peers peuvent recevoir un read model de cette exécution.

Cette distinction devient un invariant V4 :

```text
authoritative ownership
    !=
observability
```

## 1.5 Plusieurs exécutions du target ne veut pas dire plusieurs exécutions d'une même action

V3 représente les executions avec une liste parce que plusieurs actions de groupes de concurrence différents peuvent tourner sur une même cible.

Cela ne signifie pas qu'une même action doit pouvoir avoir plusieurs occurrences simultanées.

En pratique, l'implémentation actuelle interdit déjà ce cas : une `InteractionAction` possède un seul `ConcurrencyGroup`, et une seconde exécution de cette même action entre nécessairement en conflit avec la première puisqu'elle appartient au même groupe.

V4 peut donc formaliser un invariant déjà réel et simplifier son modèle :

```text
for one Interactive:
    one ActionId -> zero or one active execution
```

Les concurrency groups restent utiles pour une autre question : quelles **actions différentes** peuvent tourner en même temps ?

---

# 2. Architectural invariants

Ces règles sont plus importantes que les classes exactes. Une implémentation V4 peut changer les noms ou les petits types tant qu'elle préserve ces frontières.

## 2.1 Action presentation and execution presentation are separate read models

`InteractionActionPresentation` répond uniquement à :

> Que peut actuellement faire cet interactor sur cette cible ?

`InteractionExecutionPresentation` répond uniquement à :

> Qu'est-ce qui est actuellement en train de s'exécuter sur cette cible, pour ce peer ?

La progression d'une exécution n'appartient jamais à `InteractionActionPresentation`.

Une UI qui veut afficher la progression dans son prompt joint explicitement les deux read models par `ActionId`.

## 2.2 The Interactive owns executions

L'`InteractiveComponent` est la source conceptuelle des exécutions qui tournent sur lui.

```text
InteractiveComponent
├── actions
└── executions
```

L'interactor demande une action, prédit éventuellement son résultat local et reçoit les ACK/refus. Il n'est pas le read model des exécutions du monde.

Sur le serveur, l'Interactive possède la vérité autoritaire des exécutions.

Sur un client, la copie de ce même Interactive expose les exécutions que sa politique de visibilité lui permet d'observer.

Il n'existe pas de miroir d'exécution possédé par l'interactor pour la présentation : un renderer monde doit pouvoir lire l'Interactive directement.

## 2.3 One ActionId owns at most one active execution

Pour un `InteractiveComponent` donné :

```text
ActionId -> 0..1 active execution
```

Cette unicité est indépendante des groupes de concurrence.

Les deux contraintes répondent à deux questions différentes :

```text
Action uniqueness
    « cette action est-elle déjà en cours ? »

ConcurrencyGroup
    « une autre action incompatible est-elle en cours ? »
```

Exemple :

```text
Hack       group = machine
Repair     group = machine
Inspect    group = inspect
```

Alors :

```text
Hack + Hack       impossible : même ActionId
Hack + Repair     impossible : même concurrency group
Hack + Inspect    possible   : groupes différents
```

Cette règle permet une API et un stockage action-centric sans supprimer la possibilité de plusieurs exécutions sur un même Interactive :

```text
Interactive.Executions
├── Hack    -> execution #42
└── Inspect -> execution #43
```

`ExecutionId` reste nécessaire même si `ActionId` identifie le slot. Les deux identités n'ont pas le même rôle :

```text
ActionId
    = quel slot logique / quelle action ?

ExecutionId
    = quelle occurrence précise de cette action ?
```

Ainsi, un callback retardé appartenant à `Hack execution #41` ne peut pas compléter accidentellement le nouveau `Hack execution #52`.

### Cooperative work is not multiple copies of the same execution

Le contre-exemple principal serait : deux joueurs réparent simultanément le même générateur via la même action `Repair`.

V4 ne modélise pas cela comme :

```text
Repair execution by A
Repair execution by B
```

mais comme une seule exécution/processus partagé :

```text
Repair execution
└── RepairSession
    ├── participant A
    ├── participant B
    └── shared Progress
```

Interaction peut servir à rejoindre/quitter cette session ou à maintenir la réservation, mais la coopération est une propriété du système métier partagé.

Cette direction évite de payer dans tout le framework le coût de plusieurs predictions, plusieurs executions et plusieurs progressions concurrentes pour une même action alors qu'aucun use case actuel ne l'exige.

Si un futur gameplay démontre le besoin de plusieurs occurrences réellement indépendantes de la même action sur le même target, cet invariant devra être réouvert explicitement ; il n'est pas laissé ouvert par défaut « au cas où ».

## 2.4 Core execution has no timing semantics

Le primitive d'une action longue est :

```text
Running
```

qui signifie seulement :

> l'exécution reste réservée jusqu'à ce qu'un système la complète, l'annule ou la fasse échouer.

Le core ne suppose jamais qu'une exécution possède une durée.

Il ne doit pas avoir besoin de `Duration`, `Elapsed`, `Remaining`, deadline ou timer pour gérer :

- la réservation ;
- l'unicité par `ActionId` ;
- les groupes de concurrence ;
- le busy ;
- le maintien lié à l'input ou à la présence ;
- la cancellation ;
- la completion ;
- la failure ;
- les ACK réseau.

## 2.5 Timed execution is a first-class optional feature

Le plugin fournit un chemin built-in très simple pour les actions temporisées, mais cette feature reste spécialisée.

Deux chemins sont de première classe :

```text
custom / externally driven
    InteractionActionExecutor
    → return Running()
    → gameplay eventually CompleteExecution / CancelExecution

built-in timed
    TimedInteractionExecutor
    → framework-provided timing machinery
    → completion automatique au timeout
```

Un executor custom qui veut le timing built-in peut hériter de `TimedInteractionExecutor`.

`TimedInteractionExecutor` peut utiliser en interne une composition avec un helper `TimedExecution`; cette composition est un détail d'implémentation et ne doit pas imposer de boilerplate d'authoring à chaque executor timed.

## 2.6 Progress is optional and generic; timing is not the public abstraction

Une exécution peut fournir une progression normalisée de présentation :

```text
Progress = 0..1
```

Cette progression ne signifie pas « timer » et n'implique même pas qu'elle soit continue.

Elle peut provenir de :

```text
TimedExecution      → elapsed / duration
HackSession         → downloaded / total
RepairSystem        → repaired / required
ThreeStepProcess    → 0 → .33 → .66 → 1
CraftSystem         → work / target
Dialogue            → no progress
Carry interaction   → no progress
```

Le core et les renderers ne doivent jamais tester :

```text
executor is TimedInteractionExecutor
```

Ils ne connaissent que la présence éventuelle d'une progression présentable.

Une progression peut donc être :

```text
continuous and locally derived
step-based and published only when it changes
computed from a replicated gameplay system
absent
```

sans modifier le contrat consommé par la présentation.

## 2.7 Execution existence and execution progress are separate replication concerns

Répliquer qu'une exécution existe ne signifie pas répliquer sa progression à chaque frame.

Le framework doit pouvoir rendre observable :

```text
ExecutionId
ActionId
active execution membership
```

sans envoyer un stream réseau de :

```text
0.37
0.38
0.39
...
```

Mais « ne pas répliquer un float par frame » ne signifie pas « ne jamais répliquer Progress ».

Une progression discrète :

```text
0
→ .33
→ .66
→ 1
```

est au contraire un excellent candidat à une propriété publiée/répliquée lorsqu'elle change.

Une feature timed peut synchroniser les informations minimales nécessaires à reconstruire son chrono localement. Un système métier custom peut répliquer ses propres données. Un processus discret peut publier directement quelques snapshots de `Progress`. Les trois alimentent ensuite le même read model local.

## 2.8 Execution visibility is a policy, not an assumption

Toutes les exécutions n'ont pas la même portée de présentation.

Au minimum, la V4 doit pouvoir représenter les intentions suivantes :

```text
AuthorityOnly / no client execution presentation
RequesterOnly
Replicated / observable by other peers
```

La spec de réalisation fixe les noms `AuthorityOnly`, `RequesterOnly`, `Replicated` et place
l'authoring sur chaque occurrence `InteractionAction`.

Une implémentation Godot doit préférer s'appuyer sur les mécanismes natifs de réplication et de visibilité (`MultiplayerSynchronizer`, peer visibility / interest management) plutôt que recréer un système parallèle de fan-out RPC si cela reste compatible avec les invariants.

La politique d'existence de l'exécution et la stratégie de synchronisation de sa progression restent deux axes séparés.

Exemples :

```text
RequesterOnly + derived timed progress
    → prompt personnel type Arc Raiders

Replicated + derived timed progress
    → terminal monde avec barre continue visible par tous

Replicated + published step progress
    → puzzle à trois étapes visible par tous

Replicated execution + no Interaction progress
    → le monde sait que l'action tourne, un système métier affiche sa propre donnée
```

## 2.9 Multiple executions remain supported across different actions

L'Interactive expose toujours une collection `0..N` d'exécutions, mais cette collection est indexable sans ambiguïté par `ActionId`.

```text
Interactive
├── action A -> 0..1 execution
├── action B -> 0..1 execution
└── action C -> 0..1 execution
```

Plusieurs entrées peuvent coexister lorsque leurs groupes de concurrence le permettent.

La cardinalité est donc :

```text
Interactive -> 0..N executions
ActionId    -> 0..1 execution
```

Ce modèle est volontairement plus strict que « N executions quelconques » parce qu'il simplifie :

- availability / already-running ;
- lookup de présentation ;
- jointure action ↔ execution ;
- prediction ;
- réplication ;
- reconciliation ;
- tests réseau.

## 2.10 Prediction may create local execution presentation, but the Interactor does not own the read model

L'interactor reste le composant qui sait qu'une requête locale vient d'être envoyée et peut donc déclencher une prediction immédiate.

Mais la forme V3 :

```text
Interactor._prediction
    → float progress
```

n'est plus le modèle cible.

Une prediction V4 doit être représentable comme une **execution presentation locale prédite sur l'Interactive**.

```text
local request
→ predicted execution presentation in action slot
→ authoritative acknowledgement / replication
→ reconcile same action slot

or

→ rejection
→ clear predicted slot
```

L'unicité par `ActionId` rend naturel un modèle de prediction par action : une action ne doit pas pouvoir accumuler plusieurs predictions concurrentes pendant qu'une requête de ce slot est déjà en vol.

Aucun `RequestId` distinct n'est introduit : une seule requête de `(target, ActionId)` peut rester en
vol. Cette décision devra être rouverte si un futur protocole autorise retry ou batching avant réponse
terminale.

## 2.11 Gameplay progress remains gameplay-owned when it has gameplay meaning

Si « hack = 63 % » affecte réellement :

- la sauvegarde ;
- la reprise après interruption ;
- la coopération ;
- une quête ;
- une pénalité ;
- une simulation ;

alors cette progression appartient au système métier (`HackSession`, `RepairSystem`, etc.). Interaction peut la présenter, mais ne devient pas sa source de vérité.

Le helper timed n'est approprié que lorsque le timer est réellement la sémantique suffisante du processus.

Un système métier peut néanmoins choisir de **publier un snapshot** de sa progression dans le read model Interaction afin que tous les renderers génériques la consomment. Publier une représentation ne transfère pas l'ownership gameplay.

---

# 3. Target model

## 3.1 Action presentation

Le read model existant reste centré sur l'offre faite à un interactor :

```csharp
public readonly record struct InteractionActionPresentation(
    StringName ActionId,
    string Label,
    string Description,
    StringName InputActionName,
    InteractionAvailability Availability,
    bool IsAutomatic,
    bool IsHoldable,
    float? HoldProgress,
    float? HoldElapsed
);
```

La spec de réalisation adopte cette forme et retire `ExecutionProgress` ainsi que toute donnée de
lifecycle d'exécution.

`HoldProgress` reste ici : le hold est un geste de sélection relatif à l'interactor, pas une exécution du target.

## 3.2 Execution presentation

Première forme minimale envisagée :

```csharp
public readonly record struct InteractionExecutionPresentation(
    ulong ExecutionId,
    StringName ActionId,
    float? Progress = null
);
```

Cette forme est volontairement petite.

Ne pas ajouter par défaut :

```text
Duration
Elapsed
Remaining
Timer
Deadline
```

Ces notions appartiennent au provider timed éventuel, pas au contrat générique.

Le statut `Predicted / Confirmed` reste un détail interne du slot local et de la réconciliation. Il
n'est pas exposé dans le read model public tant qu'un renderer réel ne démontre pas qu'il doit
présenter ces deux états différemment.

Une presentation n'existe que tant que l'execution est active. La completion retire immédiatement
le slot ; le framework ne garantit donc pas qu'un renderer ou un peer observe une dernière valeur
`Progress = 1`. La completion, le signal de fin ou la disparition du slot constituent le contrat
terminal.

## 3.3 Action slot lookup

L'invariant `ActionId -> 0..1 execution` rend une API directe possible :

```csharp
bool TryGetExecutionPresentation(
    StringName actionId,
    out InteractionExecutionPresentation presentation
);
```

ou toute forme Godot-friendly équivalente.

Un renderer action-centric n'a pas à demander « laquelle des executions de Hack dois-je afficher ? ».

Le `ExecutionId` reste exposé dans la présentation pour identifier l'occurrence et pour les consumers qui ont besoin de corréler un lifecycle précis.

## 3.4 Target presentation

L'Interactive expose séparément les deux read models :

```text
GetPresentation(interactor, isFocused)
    -> actions offertes à cet interactor

GetExecutionPresentations()
TryGetExecutionPresentation(actionId)
    -> executions observables par ce peer
```

`InteractionTargetPresentation` ne transporte pas les executions. Cette séparation permet à un
consumer monde de lire directement l'Interactive sans inventer de contexte d'interactor, et évite
qu'un snapshot d'offre devienne le propriétaire pratique du lifecycle des executions.

Un consumer qui a besoin des deux modèles les compose explicitement par `ActionId`.

---

# 4. Presentation composition

## 4.1 Prompt classique

Un prompt qui ne montre que le choix :

```text
ActionPresentation
→ label / input / availability / hold
```

Il ignore complètement les exécutions.

## 4.2 Prompt avec execution progress

Un jeu de type Arc Raiders peut joindre explicitement :

```csharp
Bind(
    InteractionActionPresentation action,
    InteractionExecutionPresentation? execution
)
```

Le widget affiche alors par exemple :

```text
Hack [E]
████████░░ 80 %
```

Avec l'unicité par action, la jointure est déterministe :

```text
action.ActionId
    ↓
TryGetExecutionPresentation(action.ActionId)
```

La donnée de progression reste execution-owned ; le prompt n'en est qu'un renderer.

## 4.3 Feedback monde

Un terminal peut référencer son `InteractiveComponent` et lire :

```text
TryGetExecutionPresentation("hack")
→ Progress
```

Puis choisir librement :

```text
world-space ProgressBar
shader_parameter "hack_progress"
light intensity
animation blend
sound pitch
VFX
```

Aucune interface C# de renderer n'est requise par le core.

Une API Godot-native doit privilégier queries, properties et signaux structurels sur des `Node` / objets enregistrables dans ClassDB.

## 4.4 Event-driven plus pull continu

Les changements structurels peuvent être signalés :

```text
ExecutionStarted(actionId)
ExecutionEnded(actionId)
ExecutionPresentationChanged(actionId)
```

Un consumer qui a besoin d'une valeur continue dérivée localement peut ensuite pull `Progress` chaque frame.

Une progression publiée/discrète peut au contraire ne provoquer un changement que lorsque sa valeur passe par exemple de `.33` à `.66`.

Le framework ne doit pas émettre un signal de progression réseau ou local à chaque tick par défaut.

La fin d'une execution retire immédiatement sa presentation. Un consumer ne doit pas attendre une
dernière valeur `Progress = 1` : il réagit au signal terminal ou à la disparition du slot.

---

# 5. Core execution lifecycle

## 5.1 Primitive

La forme conceptuelle cible reste :

```csharp
public abstract InteractionExecutionResult Execute(
    in InteractionExecutionContext context
);
```

avec les mêmes issues sémantiques qu'aujourd'hui :

```text
Completed
Running
Rejected(reason)
Failed(reason)
```

L'union reste justifiée : `Rejected` et `Failed` portent une raison, et les issues ne sont pas de simples valeurs d'un état mutable.

La V4 ne cherche donc pas à remplacer ce résultat par un enum uniquement pour simplifier la syntaxe.

## 5.2 Running

`Running` ne transporte plus de durée.

Il signifie :

> La commande a été acceptée et l'Interactive garde cette execution active jusqu'à une terminaison explicite.

Les primitives existantes de fin restent centrales :

```text
CompleteExecution(executionId)
CancelExecution(executionId, reason)
FailExecution(executionId, reason)
```

Le comportement exact de `Failed` après un `Running()` et son API publique méritent un passage dédié pendant l'implémentation ; V3 possède déjà la distinction ACK `Failed` vs `Rejected` qui doit être préservée.

## 5.3 Reservation checks

Avant de réserver une nouvelle exécution, le core vérifie conceptuellement deux contraintes :

```text
1. no active execution for this ActionId
2. no active execution in this action's ConcurrencyGroup
```

La première protège l'unicité logique de l'action.

La seconde protège l'exclusivité entre actions différentes.

L'implémentation peut utiliser un dictionnaire par `ActionId`, un index de groupe ou une simple itération selon le nombre réel d'actions ; l'invariant public ne dépend pas de la structure choisie.

## 5.4 Input and presence lifetime remain orthogonal

Les axes déjà distingués restent valides :

```text
CancelOnInputReleased
RequiresInteractorPresence
```

Ils ne dépendent pas du timing.

Une execution timed peut exiger la présence ou non. Une execution sans timer aussi.

---

# 6. Built-in timed execution

## 6.1 Why keep it

Forcer chaque interaction de fouille, soin, déverrouillage ou hack simple à recréer :

```text
Timer
start
stop
cancel
CompleteExecution
network sync
late join
progress calculation
```

serait une régression ergonomique inutile.

La V4 garde donc un chemin built-in riche, mais l'enferme hors du core générique.

## 6.2 Proposed hierarchy

Direction privilégiée :

```text
InteractionActionExecutor
└── TimedInteractionExecutor
```

Un auteur choisit simplement :

```csharp
class MyExecutor : InteractionActionExecutor
```

ou :

```csharp
class MyTimedExecutor : TimedInteractionExecutor
```

Le framework peut aussi fournir un executor timed concret pour le cas où aucune mutation spécifique n'est nécessaire au démarrage.

## 6.3 Internal composition

`TimedInteractionExecutor` peut déléguer la machinerie à un helper interne ou composable :

```text
TimedInteractionExecutor
└── TimedExecution machinery
```

`TimedExecution` peut être un composant, une structure runtime ou un helper interne selon ce qui s'intègre le mieux à Godot et à la réplication.

Ce détail ne doit pas forcer chaque scene timed à brancher manuellement un composant supplémentaire si l'héritage suffit à exprimer l'intention.

## 6.4 Responsibilities of the timed feature

La feature timed peut posséder :

```text
duration configuration
server authoritative clock
timeout → CompleteExecution
cleanup on cancel / fail / completion
minimal timing synchronization
late-join reconstruction
local extrapolation
optional prediction/reconciliation
normalized Progress provider
```

Ces responsabilités ne remontent pas dans le core Interaction.

## 6.5 No `is TimedInteractionExecutor` in presentation

Le provider timed alimente la même abstraction de progression que n'importe quel système custom.

```text
TimedExecution ─────┐
HackSession ────────┼─→ ExecutionPresentation.Progress
ThreeStepProcess ───┤
RepairSystem ───────┘
```

Le renderer ne connaît jamais la provenance.

---

# 7. Progress production — implementation strategies

La frontière est décidée. La spec de réalisation fixe les APIs exactes ; cette section conserve le
raisonnement qui justifie les deux stratégies.

Le besoin :

> Un système associé à une exécution doit pouvoir fournir une progression normalisée sans forcer le core ou le renderer à connaître sa nature.

La discussion fait désormais apparaître **deux formes de production réellement différentes** qu'une API finale doit pouvoir couvrir proprement.

## 7.1 Published / snapshot progress

Cas naturel : une progression change par événements métier.

```text
0
→ .33
→ .66
→ 1
```

L'API de réalisation retenue est :

```csharp
Interactive.ReportExecutionProgress(executionId, 0.33f);
```

Le système ne pousse une nouvelle valeur que lorsque sa progression logique change.

### Example

```text
ThreeStepHack

stage A completed
→ ReportProgress(.33)

stage B completed
→ ReportProgress(.66)

stage C completed
→ CompleteExecution(id)
```

Pour une execution `Replicated`, cette valeur peut naturellement faire partie du snapshot répliqué de l'exécution et être envoyée seulement lorsqu'elle change.

### Pros

- trivial à comprendre ;
- excellent pour les progressions discrètes ;
- naturel à répliquer ;
- late join récupère immédiatement la dernière valeur connue ;
- aucun provider lifetime à maintenir pour les cas simples.

### Risks

- une API nommée `SetProgress` peut inciter un auteur à l'appeler chaque frame ;
- stale `ExecutionId` doit devenir un no-op ou une erreur claire ;
- le core doit définir qui possède/clear le snapshot sur terminaison ;
- il faut éviter que « presentation progress » devienne par accident la source gameplay d'un système métier.

Le point important : **le setter n'est pas intrinsèquement mauvais**. Il devient mauvais si on l'utilise comme transport continu d'un timer. Pour une progression événementielle, c'est probablement l'API la plus naturelle.

## 7.2 Derived / local progress source

Cas naturel : la progression évolue continuellement mais peut être reconstruite localement sans réplication de chaque valeur.

```text
TimedExecution
    replicated duration + timing anchor
        ↓
local clock
        ↓
Progress = .3726...
```

ou un système métier déjà répliqué :

```text
HackSession replicated fields
        ↓
local query
        ↓
Progress = downloaded / total
```

Conceptuellement :

```text
Interactive execution
└── optional ProgressSource
```

La réalisation retient une source locale Godot-native :

```csharp
SetExecutionProgressSource(executionId, source);
```

`source` est un `Callable`. La présentation pull sa valeur localement ; le système gameplay reste
responsable de synchroniser les données dont ce callable dépend.

### Pros

- pas de setter par frame ;
- pas de stream de floats réseau ;
- ownership du calcul explicite ;
- `TimedExecution` et un système métier peuvent utiliser le même contrat de sortie.

### Risks

- API de lifetime à définir ;
- source freed / replaced ;
- attention à ne pas introduire une interface C# comme extension point obligatoire ;
- la forme doit rester naturelle en GDScript / GDExtension ;
- il faut préciser si un provider est purement local ou s'il porte aussi sa propre réplication.

## 7.3 Published and derived progress may coexist as implementation strategies

Le contrat public consommé reste :

```text
InteractionExecutionPresentation.Progress?
```

L'implémentation peut ensuite résoudre cette valeur depuis :

```text
published snapshot
or
local derived source
or
none
```

La résolution retenue est :

```text
if local ProgressSource exists:
    Progress = source.Progress
else if transport sample exists:
    Progress = extrapolated sample
else:
    Progress = PublishedProgress
```

Cette distinction évite de chercher une abstraction unique qui soit simultanément optimale pour un timer continu et un processus à trois étapes.

## 7.4 Query on executor

L'executor pourrait fournir une query de progression pour ses propres executions.

### Pros

- aucune registration supplémentaire ;
- lien naturel action → executor.

### Risks

- un executor n'est pas forcément le propriétaire réel du processus après le start ;
- le système métier peut être ailleurs dans la scene ;
- rapproche à nouveau les executors du rôle de presentation provider ;
- moins naturel pour un process partagé ou un composant monde indépendant.

### Decision

La réalisation couvre les deux chemins :

```text
PublishedProgress
    pour snapshots discrets / événementiels

DerivedProgressSource
    pour continu reconstructible localement
```

Les tests doivent prouver que ces deux chemins convergent vers la même
`ExecutionPresentation.Progress` sans special case dans les renderers.

---

# 8. Networking and visibility

## 8.1 Authoritative storage

Le serveur garde la vérité des executions actives de l'Interactive.

Aucun client ne crée autoritairement, complète ou annule une execution via le read model de presentation.

L'unicité `ActionId -> 0..1` s'applique d'abord à cette vérité autoritaire.

## 8.2 Replicated execution read model

Quand la politique choisie l'autorise, les peers reçoivent un snapshot de membership suffisant pour savoir :

```text
execution #42 exists
ActionId = hack
```

Une progression publiée peut éventuellement faire partie de ce même snapshot :

```text
execution #42
ActionId = hack
Progress = .66
```

La spec de réalisation retient une collection compacte Variant synchronisée on-change par un
`InteractionExecutionSynchronizer` dédié, avec spawn sync pour le late join.

## 8.3 Visibility modes to support

Cas à supporter conceptuellement :

### Authority-only / none

Aucun client n'a besoin de connaître l'exécution elle-même.

Un système métier répliqué peut être la seule source de feedback monde.

### Requester-only

Le demandeur doit voir sa propre exécution — typiquement un prompt/action progress personnel — mais les autres peers n'ont pas à la connaître.

Les ACK et la prediction locale peuvent suffire ; aucune diffusion monde n'est requise.

Cas type : interaction longue dont la barre n'existe que dans le prompt du joueur qui agit.

### Replicated / world-observable

Les clients autorisés par la visibilité réseau de l'objet doivent pouvoir observer l'exécution, même s'ils ne sont pas dans les zones d'interaction du target.

Cas type : terminal de hack avec écran monde visible par tous les joueurs présents dans la zone réseau pertinente.

## 8.4 Visibility and progress transport are orthogonal axes

La visibilité répond à :

> Qui sait que l'exécution existe ?

Le transport de progression répond à :

> Comment ce peer obtient-il `Progress` ?

Exemples :

| Execution visibility | Progress strategy | Use case |
| --- | --- | --- |
| RequesterOnly | derived timed | prompt personnel long-running |
| Replicated | derived timed | terminal monde avec timer continu |
| Replicated | published snapshots | puzzle / hack à étapes |
| Replicated | derived from gameplay system | réparation complexe |
| Replicated | none | objet simplement busy |
| AuthorityOnly | none / gameplay-owned | process sans présentation Interaction |

Ne pas créer un enum unique qui essaierait d'encoder le produit cartésien de ces deux axes.

## 8.5 Prefer Godot-native visibility

Si `MultiplayerSynchronizer` et sa visibilité permettent de porter proprement le snapshot des executions, préférer cette voie à une nouvelle couche maison.

Interaction doit exprimer l'intention de visibilité sans réimplémenter un interest-management engine.

Le détail important : « replicated » ne doit pas nécessairement signifier « envoyé à absolument tous les peers de la session » ; la visibilité native de la node/scene reste applicable.

## 8.6 Timed synchronization is producer-owned

Une `TimedExecution` choisit comment synchroniser son temps : par exemple duration + anchor/elapsed snapshot puis extrapolation locale.

Le core Interaction n'a pas besoin de connaître ces champs pour gérer son lifecycle.

Le résultat final local est simplement :

```text
ExecutionPresentation.Progress
```

## 8.7 Discrete progress can be directly replicated

Pour :

```text
0 → .33 → .66 → 1
```

il n'y a aucun intérêt à inventer une clock ou une extrapolation.

Le serveur ou le système gameplay autoritaire publie la nouvelle valeur lorsqu'une étape se termine ; la réplication propage ce snapshot aux peers concernés.

Le late join reçoit directement la dernière valeur :

```text
joins while stage B completed
→ execution exists
→ Progress = .66
```

Cette propriété est particulièrement intéressante pour les feedbacks monde : aucun historique des étapes ratées n'est requis si le renderer ne veut que représenter la progression courante.

## 8.8 Gameplay system may remain the replicated source

Si un `HackSession` possède déjà :

```text
CurrentStage
CompletedBlocks
TotalBlocks
Participants
```

Interaction ne doit pas forcément dupliquer ces données.

Deux voies restent légitimes :

```text
HackSession replicates its state
→ local ProgressSource derives .66
→ Interaction presentation exposes .66
```

ou :

```text
HackSession owns gameplay truth
→ reports .66 snapshot to Interaction
→ Interaction replicates presentation progress
```

Le choix dépend du coût de duplication et de la réutilisation attendue de la présentation générique.

---

# 9. Prediction and reconciliation

La prediction V3 est une solution spécifique au float de timer local. V4 doit la refondre autour du nouveau read model.

## 9.1 Desired model

```text
requester presses input
→ local request is created
→ action execution slot may expose predicted presentation immediately

server accepts
→ authoritative execution is acknowledged / replicated
→ predicted presentation is reconciled in the same ActionId slot

server rejects
→ predicted presentation is removed
```

## 9.2 Ownership

L'interactor déclenche la prediction parce qu'il possède l'intention locale et le protocole de requête.

L'Interactive expose la prediction parce qu'il possède le read model de ses executions.

Cette distinction évite de recréer un `Interactor._prediction` utilisé comme source de presentation parallèle.

Le slot peut conserver en interne son état `Predicted / Confirmed` pour la réconciliation, mais cet
état n'est pas un champ de `InteractionExecutionPresentation`. La presentation publique reste
identique avant et après confirmation tant qu'aucun besoin de renderer ne justifie cette distinction.

## 9.3 Cardinality

La cardinalité cible devient naturellement :

```text
one execution slot per ActionId
```

Un slot peut être conceptuellement :

```text
Empty
Predicted
Confirmed
```

et éventuellement porter l'information nécessaire pendant la transition de réconciliation.

Une seconde requête pour la même action ne doit pas créer un deuxième slot tant que la première est predicted ou confirmed.

Plusieurs predictions restent possibles sur un même Interactive **pour des ActionId différents** si le protocole et les concurrency groups le permettent.

## 9.4 Correlation

V3 corrèle aujourd'hui par `(target, actionId)` et documente que cela suffit parce qu'au plus une requête de cette paire est en vol.

L'invariant V4 `ActionId -> 0..1 execution` renforce cette direction : tant qu'une seconde requête du même slot est interdite avant la terminaison/refus de la première, `(target, actionId)` reste une corrélation naturelle.

Un `RequestId` peut néanmoins devenir utile pour d'autres raisons :

- réponses très retardées après destruction/recréation logique ;
- protocoles permettant retry avant terminal response ;
- diagnostics ;
- futures formes de batching.

Il ne doit pas être introduit uniquement pour supporter plusieurs executions simultanées de la même action, puisque V4 choisit explicitement de ne pas supporter ce modèle.

## 9.5 ExecutionId remains authoritative occurrence identity

Une prediction locale peut ne pas connaître l'`ExecutionId` final avant ACK.

Une fois confirmée :

```text
Predicted Hack slot
    ↓ ACK
Confirmed Hack execution #52
```

les callbacks et terminaisons utilisent `ExecutionId = 52` pour protéger l'occurrence précise.

---

# 10. Reference scenarios

Ces scénarios doivent tous rester simples dans l'API finale.

## 10.1 Simple instant action

```text
OpenAction
└── SetStateExecutor

Execute
→ mutate world
→ Completed
```

Aucune execution longue, aucun progress provider, aucune nouvelle complexité V4.

## 10.2 Simple built-in timed action

```text
SearchAction
└── TimedInteractionExecutor
    Duration = 3 s
```

Le helper :

```text
starts authoritative timing
→ Running
→ synchronizes minimal timing data
→ derives Progress locally
→ timeout
→ CompleteExecution
```

Selon la visibilité configurée, seule l'UI du demandeur ou plusieurs peers peuvent observer l'exécution.

## 10.3 Custom terminal-owned timer

```text
HackAction
└── HackExecutor

HackTerminal
├── HackSession / timer
├── Interactive
├── world GUI
└── material feedback
```

Executor :

```text
terminal.StartHack(executionId)
→ Running
```

Terminal :

```text
hack ends
→ Interactive.CompleteExecution(executionId)
```

Cancellation :

```text
OnExecutionCancelled
→ terminal.CancelHack()
```

Le terminal peut exposer sa propre progression au read model Interaction ou laisser son UI lire directement `HackSession` si Interaction n'est pas le bon consumer.

## 10.4 Discrete three-step process visible by everyone

```text
CalibrateAction
└── CalibrateExecutor
```

Le process démarre :

```text
Running
Progress = 0
```

Puis :

```text
step 1 done
→ ReportExecutionProgress(id, .33)

step 2 done
→ ReportExecutionProgress(id, .66)

step 3 done
→ CompleteExecution(id)
```

Pour une execution `Replicated`, tous les clients autorisés voient les mêmes snapshots.

Aucun timer n'existe. `Progress` exprime uniquement une quantité normalisée présentable.

Le dernier snapshot observable peut donc être `.66`. La completion retire immédiatement
l'execution ; ni le renderer local ni la réplication ne doivent dépendre de l'observation d'un
snapshot intermédiaire à `1`.

Ce scénario est un test architectural important : si l'implémentation de Progress suppose une duration ou un elapsed, la séparation V4 est ratée.

## 10.5 Arc Raiders-style action prompt progress

Le target expose :

```text
ActionPresentation(hack)
ExecutionPresentation(hack, progress=0.63)
```

Le prompt fait la jointure déterministe :

```text
execution = TryGetExecutionPresentation(action.ActionId)
Bind(action, execution)
```

La progression apparaît à même le prompt sans réintroduire `ExecutionProgress` dans `InteractionActionPresentation`.

## 10.6 World feedback visible outside interaction areas

Un autre joueur n'est ni focused, ni indicated, ni dans `InteractionArea`.

Son replica du terminal reçoit néanmoins l'exécution selon la visibilité réseau configurée :

```text
Terminal.Interactive
→ Hack execution #42
→ Progress = 0.63
```

L'écran monde continue donc d'afficher la progression.

La détection d'interaction et la visibilité de l'exécution sont explicitement indépendantes.

## 10.7 Long-running process with no meaningful progress

```text
Dialogue / carry / machine waiting for external event
→ Running
→ Progress = null
```

La présentation peut afficher `busy`, jouer un son ou ne rien dessiner.

Aucune fausse progression n'est inventée.

## 10.8 Multiple different actions concurrently

Deux actions appartenant à deux groupes de concurrence indépendants peuvent rester actives simultanément :

```text
Interactive.Executions
├── Hack    #42
└── Inspect #43
```

mais :

```text
Hack #42
Hack #44
```

n'est jamais un état valide sur le même Interactive.

## 10.9 Same-group actions remain exclusive

```text
Hack       group = machine
Repair     group = machine
```

Si Hack tourne :

```text
Repair request
→ blocked/rejected as already occupied
```

L'unicité par action ne remplace donc pas les concurrency groups.

## 10.10 Cooperative repair

Deux joueurs contribuent au même process :

```text
Repair action
→ one Repair execution #51
→ RepairSession
   ├── A joined
   ├── B joined
   └── Progress = .72
```

Le système métier décide comment les participants augmentent la progression et quand l'exécution se termine.

Interaction ne crée pas deux copies de `Repair`.

## 10.11 Late join during discrete progress

Serveur :

```text
Calibrate execution #61
Progress = .66
```

Un client rejoint :

```text
replicated execution snapshot
→ ActionId = calibrate
→ ExecutionId = 61
→ Progress = .66
```

Le renderer applique directement l'état courant, sans rejouer artificiellement `.33` puis `.66`.

---

# 11. ADR notes

Ces notes enregistrent les alternatives envisagées afin que l'implémentation future ne réouvre pas les mêmes questions sans contexte.

## ADR-001 — Remove execution progress from action presentation

**Decision:** accepted.

`ExecutionProgress` quitte `InteractionActionPresentation`.

**Reason:** une action proposée et une execution active ont des ownerships, lifetimes et audiences différentes.

**Rejected alternative:** conserver le champ dans l'action parce que le prompt actuel le consomme.

Le renderer ne doit pas dicter la structure du read model.

## ADR-002 — Interactive owns execution presentation

**Decision:** accepted.

L'Interactive expose les executions en cours. L'interactor n'est pas leur miroir.

**Reason:** le feedback monde appartient au target et peut être nécessaire à des peers sans aucun contexte d'interaction local.

## ADR-003 — One active execution per ActionId

**Decision:** accepted.

Pour un Interactive :

```text
ActionId -> 0..1 active execution
```

**Reason:** l'implémentation actuelle le garantit déjà implicitement par le concurrency group de l'action ; aucun use case actuel ne nécessite plusieurs occurrences indépendantes de la même action ; le coût de généralisation toucherait stockage, prediction, réplication, reconciliation et présentation.

**Counter-example considered:** plusieurs joueurs réparent le même objet.

**Resolution:** représenter un processus/session coopératif unique avec plusieurs participants, pas plusieurs executions identiques.

**Revisit condition:** un gameplay réel démontre plusieurs occurrences indépendantes et simultanées de la même action sur le même Interactive.

## ADR-004 — Concurrency groups remain orthogonal to action uniqueness

**Decision:** accepted.

L'unicité par `ActionId` interdit `Hack + Hack`.

Le concurrency group interdit ou autorise `Hack + Repair`, `Hack + Inspect`, etc.

**Rejected alternative:** supprimer les concurrency groups une fois l'unicité par action adoptée.

Ils répondent à un autre besoin : l'exclusivité entre actions différentes.

## ADR-005 — Core execution is indefinite by default

**Decision:** accepted.

Le primitive long-running est `Running()` jusqu'à terminaison explicite.

**Rejected alternative:** `RunningForDuration()` comme résultat compris directement par le core.

Même présenté comme du « sucre », un résultat portant une durée oblige le core à conserver et interpréter une sémantique temporelle.

## ADR-006 — Keep a built-in timed path

**Decision:** accepted.

Le framework doit offrir une solution temporisée sans boilerplate.

Direction privilégiée : `TimedInteractionExecutor` spécialisé, possiblement construit sur un helper `TimedExecution` interne.

**Rejected alternative:** supprimer tout timer du plugin et forcer le gameplay à recréer `Timer + CompleteExecution` pour chaque action simple.

## ADR-007 — Prefer inheritance for the author-facing timed executor

**Decision:** accepted as current direction.

Deux choix d'authoring suffisent :

```text
InteractionActionExecutor
TimedInteractionExecutor
```

La composition reste possible à l'intérieur de l'implémentation timed mais n'est pas imposée à chaque scene.

**Reason:** il n'existe pas de besoin identifié de combiner simultanément plusieurs stratégies de timing built-in ; l'héritage exprime directement l'intention et minimise le wiring.

## ADR-008 — Expose generic Progress, not Timing

**Decision:** accepted.

La presentation connaît éventuellement `Progress`, pas `Duration/Elapsed/Remaining`.

**Reason:** une progression utile peut venir d'un timer, d'un système métier continu ou d'un processus discret `.33/.66/1`.

**Rejected alternative:** `InteractionExecutionTimingPresentation` comme capability générique.

Cette forme faisait revenir le timer au centre du modèle et ne représentait pas naturellement une progression par étapes.

`Progress` décrit seulement une execution active. La completion peut retirer le slot sans rendre
observable une dernière valeur égale à `1`.

## ADR-009 — Progress may be published or locally derived

**Decision:** accepted at the conceptual level; exact API open.

Deux stratégies légitimes doivent converger vers le même `ExecutionPresentation.Progress` :

```text
published snapshot
    → excellent for discrete/event-driven progress

local derived source
    → excellent for continuous/reconstructible progress
```

**Rejected alternative:** imposer un setter par frame pour tout, ou imposer un provider complexe pour les trois changements de valeur d'un process discret.

## ADR-010 — Server-owned, optionally observable executions

**Decision:** accepted.

La mutation et le lifecycle restent autoritaires serveur. Leur read model peut être requester-only ou replicated.

**Rejected alternative:** toutes les executions restent server-only et tout feedback distant doit obligatoirement passer par Stateful ou un système métier.

Cette règle rend inutilement coûteux le cas où « l'action est en cours » est exactement l'information transitoire que le monde doit afficher.

## ADR-011 — Do not replicate continuous progress every frame by default

**Decision:** accepted.

La réplication d'une execution et la synchronisation de sa progression sont séparées.

**Reason:** un timer est reconstructible à partir d'informations bien plus compactes, et un système métier peut déjà posséder son propre protocole.

**Clarification:** cette décision n'interdit pas de répliquer des snapshots de progression lorsqu'ils changent peu fréquemment, par exemple `.33 → .66 → 1`.

## ADR-012 — Prompt joins action and execution models explicitly

**Decision:** accepted.

Un widget peut recevoir :

```text
ActionPresentation + matching ExecutionPresentation?
```

L'unicité par `ActionId` rend cette jointure déterministe.

Cela supporte aussi bien un prompt minimal qu'une UI type Arc Raiders sans fusionner les deux modèles.

L'Interactive garde des queries séparées pour actions et executions. Le presenter effectue la
jointure puis bind le widget d'action avec les deux valeurs :

```text
Bind(ActionPresentation, matching ExecutionPresentation?)
```

## ADR-013 — Refactor prediction around per-action execution presentation

**Decision:** accepted in principle; implementation open.

La `_prediction` V3 dédiée au float n'est pas conservée telle quelle.

La prediction devient une forme locale du slot d'exécution de l'action sur le target, créée depuis l'intention de l'interactor puis réconciliée avec l'autorité.

`Predicted / Confirmed` reste un état interne de ce slot ; aucun `IsPredicted` public n'est ajouté sans
use case de presentation concret.

## ADR-014 — Keep union-style execution outcomes

**Decision:** accepted.

`Rejected(reason)` et `Failed(reason)` justifient toujours des variants porteurs de données. La V4 ne remplace pas artificiellement le résultat par un enum nu.

---

# 12. Implementation proof points

## P0 — Prove action-slot cardinality

Adapter les tests pour rendre explicites les invariants :

```text
same ActionId twice             → impossible
same group, different ActionId  → impossible
different groups/actions        → possible
```

Vérifier que cela permet de simplifier :

- storage lookup ;
- `AlreadyRunning` ;
- presentation lookup ;
- prediction bookkeeping ;
- ACK reconciliation.

L'implémentation ne doit pas maintenir une structure N-per-action « par précaution » si l'API interdit déjà ce cas.

Valider aussi la configuration : deux actions déclarées sur le même Interactive ne doivent pas
partager le même `ActionId`, même si leurs concurrency groups diffèrent. La garde runtime par
`ActionId` reste nécessaire en plus du diagnostic d'authoring.

## P0 — Prove progress with three different producers

Avant de figer l'API de progression, implémenter au moins :

1. `TimedInteractionExecutor` avec progression continue dérivée ;
2. `ThreeStepProcess` publiant `0 → .33 → .66`, puis complétant l'execution ;
3. `HackSession` custom avec sa propre progression gameplay ;
4. un feedback monde lisant `ExecutionPresentation` ;
5. un prompt qui joint `ActionPresentation` et `ExecutionPresentation`.

Si ces cas nécessitent des special cases de type dans le renderer, la frontière n'est pas encore bonne.

## P0 — Replication shape

Prouver la forme Godot-native retenue par la spec pour synchroniser des slots d'executions :

```text
ActionId
ExecutionId
optional PublishedProgress
```

avec :

- start ;
- end ;
- late join ;
- progression `.33/.66`, puis completion ;
- deux actions concurrentes de groupes différents ;
- refus de deux occurrences de la même action ;
- visibilité requester-only vs world-observable ;
- listen host ;
- dedicated server.

Ne pas concevoir le protocole uniquement à partir du client demandeur.

## P0 — Timed synchronization

Le helper timed doit prouver :

- autorité du timeout ;
- feedback immédiat du requester si prediction souhaitée ;
- autres peers ;
- late join au milieu ;
- correction sans stream de progress floats ;
- cancellation avant timeout ;
- absence de double completion ;
- exposition de `Progress` identique à celle d'un producer discret.

Le détail de clock sync peut rester interne au helper.

## P1 — Published progress API

La spec retient `ReportExecutionProgress`.

Critères :

- intention claire de snapshot, pas tick API ;
- validation/clamp `0..1` ;
- stale `ExecutionId` ;
- comportement sur `Progress = null` / clear ;
- emission locale de changement ;
- réplication seulement lorsque nécessaire ;
- late join.

## P1 — Derived progress source lifetime

La spec retient un `Callable` local Godot-friendly, avec attention particulière à :

- source freed ;
- provider remplacé ;
- clear sur end ;
- stale ExecutionId ;
- GDScript interoperability ;
- future GDExtension implementation ;
- allocations / calls per frame.

Ne pas introduire une interface C# obligatoire uniquement parce qu'elle est ergonomique côté .NET.

## P1 — Prediction correlation

Direction de base :

```text
one pending/active slot per (target, ActionId)
```

Vérifier que le protocole interdit bien une seconde requête de cette paire avant terminal response/reconciliation.

N'introduire un `RequestId` que si un vrai besoin indépendant de la cardinalité d'exécution le justifie.

## P1 — Visibility authoring

La spec place la politique sur chaque occurrence `InteractionAction`, avec `RequesterOnly` par
défaut. Un `InteractionExecutionSynchronizer` optionnel transporte uniquement les actions
`Replicated`.

Critère principal : la visibilité décrit qui peut **observer l'exécution**, pas qui peut demander l'action.

Elle ne doit donc pas se retrouver accidentellement couplée aux zones de détection ou à l'availability.

## P1 — Cooperative process integration

Faire un spike volontairement simple :

```text
one Repair execution
multiple participants in RepairSession
shared Progress
```

Le but n'est pas de construire un framework coop, mais de vérifier que l'unicité par ActionId n'oblige pas Interaction à posséder la notion de participant d'un système métier.

---

# 13. Non-goals

V4 ne cherche pas à :

- faire d'Interaction un framework de hack, crafting, dialogue ou animation ;
- standardiser toutes les formes de progression gameplay ;
- modéliser plusieurs occurrences simultanées de la même action sur le même Interactive sans use case réel ;
- faire d'une coopération multi-joueur plusieurs copies artificielles de la même execution ;
- prédire la simulation physique ;
- remplacer Stateful pour les vérités persistantes du monde ;
- imposer qu'une execution ait une progress bar ;
- imposer qu'une execution longue soit timed ;
- rendre les executions autoritaires côté client ;
- diffuser chaque execution à chaque peer indépendamment de l'interest management ;
- répliquer une progression continue à chaque frame par défaut ;
- créer une abstraction C# que GDScript ne peut pas consommer naturellement.

---

# 14. Migration direction from V3

Ordre conceptuel, pas encore plan de tâches définitif :

```text
1. Introduce InteractionExecutionPresentation
2. Remove ExecutionProgress / HasTimedExecution from ActionPresentation
3. Formalize ActionId -> 0..1 execution per Interactive
4. Make active executions queryable from Interactive by ActionId
5. Reduce core Running result to no timing semantics
6. Extract current Duration/Elapsed clock into timed feature
7. Implement TimedInteractionExecutor on top of Running()
8. Introduce optional generic Progress presentation
9. Spike published progress + derived progress source
10. Adapt prompt to optionally join action + execution presentation
11. Add execution visibility / replication modes
12. Refactor V3 predicted float into per-action predicted execution presentation
13. Harden with real multiplayer, late-join and discrete-progress tests
```

`HasTimedExecution` doit disparaître pour la même raison que `ExecutionProgress`: la présentation générique ne doit pas connaître la nature timed de l'executor. Un renderer s'intéresse à une execution active et éventuellement à `Progress`.

La migration doit préserver les groupes de concurrence ; ils restent la couche qui exprime l'exclusivité **entre ActionId différents**.

La réalisation est découpée dans une spec séparée afin que ce document reste le contrat d'intention
V4. Cette spec couvre trois tranches : fondation locale/autoritaire, timing et requester, puis
réplication/visibilité. Elle constitue le document exécutable ; la liste ci-dessus reste seulement
l'ordre conceptuel de migration.

---

# 15. Success criteria

La V4 est réussie si les scénarios suivants sont tous naturels sans contourner le framework :

```text
instant action
simple 3-second built-in action
custom animation-driven action
custom replicated hack session
three-step progress 0/.33/.66 then completion
hold-to-select then long execution
progress in prompt
progress on world-space terminal
remote player sees terminal progress outside interaction range
requester-only progress
long action with no progress
same action cannot run twice
multiple different actions can run in separate concurrency groups
same-group actions stay exclusive
cooperative gameplay uses one shared process/session
late join during timed execution
late join during discrete-progress execution
```

Et surtout si l'on peut expliquer le framework sans exception :

> **ActionPresentation décrit ce qu'un joueur peut demander.**
>
> **Interactive possède les executions qui tournent sur lui.**
>
> **Une action possède au maximum une execution active ; les concurrency groups contrôlent les conflits entre actions différentes.**
>
> **ExecutionPresentation décrit ce qu'un peer peut observer de cette execution.**
>
> **Running signifie seulement que l'execution continue jusqu'à sa terminaison.**
>
> **Progress est une donnée de présentation optionnelle ; elle peut être publiée par étapes ou dérivée localement, et ne signifie jamais implicitement « timer ».**
>
> **TimedInteractionExecutor est un helper spécialisé qui synchronise son timing, produit Progress et termine automatiquement une execution générique.**
>
> **Tout système métier reste libre de posséder sa propre durée, progression, réplication, participants et logique, puis de présenter et terminer la même execution générique.**

Si un nouveau cas d'usage respecte ces phrases sans demander au core de savoir s'il s'agit d'un timer, d'un hack, d'une animation, d'un dialogue ou d'un processus à étapes, la frontière est probablement au bon endroit.
