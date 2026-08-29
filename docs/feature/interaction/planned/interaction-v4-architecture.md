# Interaction Framework V4 — Execution lifecycle & presentation proposal

> **Status: Proposal.** Ce document fixe les intentions, invariants et frontières visées pour une V4 du framework d'interaction. Il ne constitue pas encore un plan d'implémentation final : les formes exactes de certaines APIs, de la réplication et de la source de progression restent volontairement ouvertes.
>
> La V4 ne cherche pas à redessiner V2/V3 pour le plaisir. Elle part d'une friction apparue en utilisant réellement le framework sur des cas plus riches : interactions longues, feedback monde, progression visible par plusieurs joueurs, exécutions pilotées par un système métier, et UI où la progression peut être affichée dans le prompt ou directement sur l'objet.

## Goal

Préserver les garanties acquises de V2/V3 — commande autoritaire, executor unique, réservation, concurrence, ACK, cancellation, rules pures — tout en retrouvant une propriété essentielle de l'ancien framework Unreal :

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

## 2.3 Core execution has no timing semantics

Le primitive d'une action longue est :

```text
Running
```

qui signifie seulement :

> l'exécution reste réservée jusqu'à ce qu'un système la complète, l'annule ou la fasse échouer.

Le core ne suppose jamais qu'une exécution possède une durée.

Il ne doit pas avoir besoin de `Duration`, `Elapsed`, `Remaining`, deadline ou timer pour gérer :

- la réservation ;
- les groupes de concurrence ;
- le busy ;
- le maintien lié à l'input ou à la présence ;
- la cancellation ;
- la completion ;
- la failure ;
- les ACK réseau.

## 2.4 Timed execution is a first-class optional feature

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

## 2.5 Progress is optional and generic; timing is not the public abstraction

Une exécution peut fournir une progression normalisée de présentation :

```text
Progress = 0..1
```

Cette progression ne signifie pas « timer ».

Elle peut provenir de :

```text
TimedExecution      → elapsed / duration
HackSession         → downloaded / total
RepairSystem        → repaired / required
CraftSystem         → work / target
Dialogue            → no progress
Carry interaction   → no progress
```

Le core et les renderers ne doivent jamais tester :

```text
executor is TimedInteractionExecutor
```

Ils ne connaissent que la présence éventuelle d'une progression présentable.

## 2.6 Execution existence and execution progress are separate replication concerns

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

Une feature timed peut synchroniser les informations minimales nécessaires à reconstruire son chrono localement. Un système métier custom peut répliquer ses propres données. Les deux peuvent ensuite fournir `Progress` au même read model local.

## 2.7 Execution visibility is a policy, not an assumption

Toutes les exécutions n'ont pas la même portée de présentation.

Au minimum, la V4 doit pouvoir représenter les intentions suivantes :

```text
AuthorityOnly / no client execution presentation
RequesterOnly
Replicated / observable by other peers
```

Les noms exacts et le niveau d'authoring restent ouverts.

Une implémentation Godot doit préférer s'appuyer sur les mécanismes natifs de réplication et de visibilité (`MultiplayerSynchronizer`, peer visibility / interest management) plutôt que recréer un système parallèle de fan-out RPC si cela reste compatible avec les invariants.

La politique d'existence de l'exécution et la stratégie de synchronisation de sa progression restent deux axes séparés.

## 2.8 Multiple executions remain structurally supported

L'Interactive expose une collection `0..N` d'exécutions.

Le framework ne fige pas une relation :

```text
ActionId -> exactly one execution
```

même si les actions longues ordinaires seront souvent exclusives par groupe de concurrence.

Une UI action-centric peut choisir une exécution correspondant à son `ActionId`; cette décision de présentation ne devient pas un invariant du modèle.

## 2.9 Prediction may create local execution presentation, but the Interactor does not own the read model

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
→ predicted execution presentation on target
→ authoritative acknowledgement / replication
→ reconcile

or

→ rejection
→ remove prediction
```

La corrélation exacte, le nombre de predictions simultanées et l'identité utilisée pour réconcilier restent des détails d'implémentation à spécifier.

## 2.10 Gameplay progress remains gameplay-owned when it has gameplay meaning

Si « hack = 63 % » affecte réellement :

- la sauvegarde ;
- la reprise après interruption ;
- la coopération ;
- une quête ;
- une pénalité ;
- une simulation ;

alors cette progression appartient au système métier (`HackSession`, `RepairSystem`, etc.). Interaction peut la présenter, mais ne devient pas sa source de vérité.

Le helper timed n'est approprié que lorsque le timer est réellement la sémantique suffisante du processus.

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

La forme exacte reste à adapter à l'existant, mais `ExecutionProgress` et toute donnée de lifecycle d'exécution en sortent.

`HoldProgress` reste ici : le hold est un geste de sélection relatif à l'interactor, pas une exécution du target.

## 3.2 Execution presentation

Première forme minimale envisagée :

```csharp
public readonly record struct InteractionExecutionPresentation(
    ulong ExecutionId,
    StringName ActionId,
    float? Progress = null,
    bool IsPredicted = false
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

`IsPredicted` est lui-même à confirmer : il peut être utile à certains renderers et à la réconciliation, mais il ne doit pas contaminer la vérité autoritaire du serveur.

## 3.3 Target presentation

Deux directions restent possibles :

```text
A. InteractionTargetPresentation contient Actions + Executions

B. Interactive expose séparément GetPresentation(interactor)
   et GetExecutionPresentation()
```

Le choix est principalement ergonomique.

L'invariant est que `Actions` et `Executions` restent deux read models conceptuellement indépendants même s'ils sont transportés dans un même snapshot pratique.

Pour les consumers qui ne connaissent aucune zone d'interaction et veulent seulement observer un objet monde, une query directement sur l'Interactive doit rester possible.

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

La donnée de progression reste execution-owned ; le prompt n'en est qu'un renderer.

## 4.3 Feedback monde

Un terminal peut référencer son `InteractiveComponent` et lire :

```text
Executions
→ find hack execution
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
ExecutionStarted
ExecutionEnded
ExecutionPresentationChanged
```

Un consumer qui a besoin d'une valeur continue peut ensuite pull `Progress` chaque frame.

Le framework ne doit pas émettre un signal de progression réseau ou local à chaque tick par défaut.

---

# 5. Core execution lifecycle

## 5.1 Primitive

La forme conceptuelle cible devient :

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
FailExecution(executionId, reason)    // forme exacte à confirmer
```

Le comportement exact de `Failed` après un `Running()` et son API publique méritent un passage dédié pendant l'implémentation ; V3 possède déjà la distinction ACK `Failed` vs `Rejected` qui doit être préservée.

## 5.3 Input and presence lifetime remain orthogonal

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
TimedExecution ─┐
HackSession    ─┼─→ ExecutionPresentation.Progress
RepairSystem   ─┘
```

Le renderer ne connaît jamais la provenance.

---

# 7. Progress source — open implementation point

La frontière est décidée ; l'API exacte ne l'est pas encore.

Le besoin :

> Un système associé à une exécution doit pouvoir fournir localement une progression normalisée sans forcer le core à connaître sa nature.

Plusieurs directions sont à comparer pendant le spike.

## 7.1 Setter on Interactive

```csharp
Interactive.SetExecutionProgress(executionId, progress);
```

### Pros

- trivial ;
- très explicite ;
- facile depuis n'importe quel système.

### Risks

- pousse naturellement vers un setter par frame ;
- mélange stockage du read model et ownership de la valeur ;
- nécessite de définir qui clear la valeur et comment éviter un vieux producer qui écrit sur une nouvelle execution.

## 7.2 Registered provider / source

```text
Interactive execution
└── optional ProgressSource
```

ou conceptuellement :

```csharp
SetExecutionProgressSource(executionId, source);
```

La présentation pull la valeur du provider.

### Pros

- pas de setter par frame ;
- ownership explicite ;
- le timed helper et un système métier utilisent exactement le même contrat.

### Risks

- API de lifetime à définir ;
- attention à ne pas introduire une interface C# comme extension point obligatoire ;
- la forme doit rester naturelle en GDScript / GDExtension.

## 7.3 Query on executor

L'executor pourrait fournir une query de progression pour ses propres executions.

### Pros

- aucune registration supplémentaire ;
- lien naturel action → executor.

### Risks

- un executor n'est pas forcément le propriétaire réel du processus après le start ;
- le système métier peut être ailleurs dans la scene ;
- rapproche à nouveau les executors du rôle de presentation provider.

### Current leaning

Préférer une forme de **provider/source Godot-friendly** plutôt qu'un setter poussé chaque frame, sans figer l'API avant un prototype concret avec :

1. `TimedInteractionExecutor` ;
2. un `HackSession` custom ;
3. un renderer monde ;
4. un prompt action + execution.

---

# 8. Networking and visibility

## 8.1 Authoritative storage

Le serveur garde la vérité des executions actives de l'Interactive.

Aucun client ne crée autoritairement, complète ou annule une execution via le read model de presentation.

## 8.2 Replicated execution read model

Quand la politique choisie l'autorise, les peers reçoivent un snapshot de membership suffisant pour savoir :

```text
execution #42 exists
ActionId = hack
```

La forme exacte peut être une propriété synchronisée, une collection compacte ou un petit composant dédié ; à décider en fonction des contraintes Godot de réplication des collections et du late join.

## 8.3 Visibility modes to support

Cas à supporter conceptuellement :

### Authority-only / none

Aucun client n'a besoin de connaître l'exécution elle-même.

Un système métier répliqué peut être la seule source de feedback monde.

### Requester-only

Le demandeur doit voir sa propre exécution — typiquement un prompt/action progress personnel — mais les autres peers n'ont pas à la connaître.

Les ACK et la prediction locale peuvent suffire ; aucune diffusion monde n'est requise.

### Replicated / world-observable

Les clients autorisés par la visibilité réseau de l'objet doivent pouvoir observer l'exécution, même s'ils ne sont pas dans les zones d'interaction du target.

Cas type : terminal de hack avec écran monde visible par tous les joueurs présents dans la zone réseau pertinente.

## 8.4 Prefer Godot-native visibility

Si `MultiplayerSynchronizer` et sa visibilité permettent de porter proprement le snapshot des executions, préférer cette voie à une nouvelle couche maison.

Interaction doit exprimer l'intention de visibilité sans réimplémenter un interest-management engine.

Le détail important : « replicated » ne doit pas nécessairement signifier « envoyé à absolument tous les peers de la session » ; la visibilité native de la node/scene reste applicable.

## 8.5 Progress synchronization is producer-owned

Une execution replicated peut avoir `Progress == null`.

Une `TimedExecution` choisit comment synchroniser son temps : par exemple duration + anchor/elapsed snapshot puis extrapolation locale.

Un `HackSession` custom choisit ses propres données : blocs téléchargés, work units, replicated state, etc.

Interaction ne standardise pas leur protocole tant qu'un besoin commun réel ne le justifie pas.

---

# 9. Prediction and reconciliation

La prediction V3 est une solution spécifique au float de timer local. V4 doit la refondre autour du nouveau read model.

## 9.1 Desired model

```text
requester presses input
→ local request is created
→ target may expose predicted execution presentation immediately

server accepts
→ authoritative execution is acknowledged / replicated
→ predicted presentation is reconciled with authoritative one

server rejects
→ predicted presentation is removed
```

## 9.2 Ownership

L'interactor déclenche la prediction parce qu'il possède l'intention locale et le protocole de requête.

L'Interactive expose la prediction parce qu'il possède le read model de ses executions.

Cette distinction évite de recréer un `Interactor._prediction` utilisé comme source de presentation parallèle.

## 9.3 Cardinality

La V3 garde une seule `_prediction` locale. V4 ne doit pas prendre cette limitation comme invariant.

Il faut envisager au minimum :

```text
one pending prediction per action
```

ou plus généralement une collection corrélée aux requêtes en vol.

Le nombre final dépendra de la politique de request concurrency retenue.

## 9.4 Correlation

V3 corrèle aujourd'hui par `(target, actionId)` et documente que cela suffit parce qu'au plus une requête de cette paire est en vol.

V4 peut :

- conserver explicitement cet invariant ;
- ou introduire une identité de requête permettant plusieurs predictions concurrentes sur une même paire.

Ne pas décider avant d'avoir listé les vrais cas d'usage de requêtes simultanées.

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
→ exposes Progress
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

## 10.4 Arc Raiders-style action prompt progress

Le target expose :

```text
ActionPresentation(hack)
ExecutionPresentation(hack, progress=0.63)
```

Le prompt fait la jointure :

```text
Bind(action, matchingExecution)
```

La progression apparaît à même le prompt sans réintroduire `ExecutionProgress` dans `InteractionActionPresentation`.

## 10.5 World feedback visible outside interaction areas

Un autre joueur n'est ni focused, ni indicated, ni dans `InteractionArea`.

Son replica du terminal reçoit néanmoins l'exécution selon la visibilité réseau configurée :

```text
Terminal.Interactive.Executions
→ hack running
→ Progress = 0.63
```

L'écran monde continue donc d'afficher la progression.

## 10.6 Long-running process with no meaningful progress

```text
Dialogue / carry / machine waiting for external event
→ Running
→ Progress = null
```

La présentation peut afficher `busy`, jouer un son ou ne rien dessiner.

Aucune fausse progression n'est inventée.

## 10.7 Multiple concurrent executions

Deux actions appartenant à deux groupes de concurrence indépendants peuvent rester actives simultanément :

```text
Interactive.Executions
├── execution A
└── execution B
```

Le read model et la réplication restent une collection même si ce cas est rare dans le gameplay courant.

---

# 11. ADR notes

Ces notes enregistrent les alternatives envisagées afin que l'implémentation future ne réouvre pas les mêmes questions sans contexte.

## ADR-001 — Remove execution progress from action presentation

**Decision:** accepted.

`ExecutionProgress` quitte `InteractionActionPresentation`.

**Reason:** une action proposée et une execution active ont des ownerships, lifetimes, cardinalités et audiences différentes.

**Rejected alternative:** conserver le champ dans l'action parce que le prompt actuel le consomme.

Le renderer ne doit pas dicter la structure du read model.

## ADR-002 — Interactive owns execution presentation

**Decision:** accepted.

L'Interactive expose les executions en cours. L'interactor n'est pas leur miroir.

**Reason:** le feedback monde appartient au target et peut être nécessaire à des peers sans aucun contexte d'interaction local.

## ADR-003 — Core execution is indefinite by default

**Decision:** accepted.

Le primitive long-running est `Running()` jusqu'à terminaison explicite.

**Rejected alternative:** `RunningForDuration()` comme résultat compris directement par le core.

Même présenté comme du « sucre », un résultat portant une durée oblige le core à conserver et interpréter une sémantique temporelle.

## ADR-004 — Keep a built-in timed path

**Decision:** accepted.

Le framework doit offrir une solution temporisée sans boilerplate.

Direction privilégiée : `TimedInteractionExecutor` spécialisé, possiblement construit sur un helper `TimedExecution` interne.

**Rejected alternative:** supprimer tout timer du plugin et forcer le gameplay à recréer `Timer + CompleteExecution` pour chaque action simple.

## ADR-005 — Prefer inheritance for the author-facing timed executor

**Decision:** accepted as current direction.

Deux choix d'authoring suffisent :

```text
InteractionActionExecutor
TimedInteractionExecutor
```

La composition reste possible à l'intérieur de l'implémentation timed mais n'est pas imposée à chaque scene.

**Reason:** il n'existe pas de besoin identifié de combiner simultanément plusieurs stratégies de timing built-in ; l'héritage exprime directement l'intention et minimise le wiring.

## ADR-006 — Expose generic Progress, not Timing

**Decision:** accepted.

La presentation connaît éventuellement `Progress`, pas `Duration/Elapsed/Remaining`.

**Reason:** une progression utile peut venir d'un timer ou d'un système non temporel.

**Rejected alternative:** `InteractionExecutionTimingPresentation` comme capability générique.

Cette forme faisait revenir le timer au centre du modèle.

## ADR-007 — Server-owned, optionally observable executions

**Decision:** accepted.

La mutation et le lifecycle restent autoritaires serveur. Leur read model peut être requester-only ou replicated.

**Rejected alternative:** toutes les executions restent server-only et tout feedback distant doit obligatoirement passer par Stateful ou un système métier.

Cette règle rend inutilement coûteux le cas où « l'action est en cours » est exactement l'information transitoire que le monde doit afficher.

## ADR-008 — Do not replicate progress every frame by default

**Decision:** accepted.

La réplication d'une execution et la synchronisation de sa progression sont séparées.

**Reason:** un timer est reconstructible à partir d'informations bien plus compactes, et un système métier possède déjà son propre protocole.

## ADR-009 — Prompt joins action and execution models explicitly

**Decision:** accepted.

Un widget peut recevoir :

```text
ActionPresentation + matching ExecutionPresentation?
```

Cela supporte aussi bien un prompt minimal qu'une UI type Arc Raiders sans fusionner les deux modèles.

## ADR-010 — Refactor prediction around execution presentation

**Decision:** accepted in principle; implementation open.

La `_prediction` V3 dédiée au float n'est pas conservée telle quelle.

La prediction devient une forme locale du read model d'exécution du target, créée depuis l'intention de l'interactor puis réconciliée avec l'autorité.

## ADR-011 — Keep union-style execution outcomes

**Decision:** accepted.

`Rejected(reason)` et `Failed(reason)` justifient toujours des variants porteurs de données. La V4 ne remplace pas artificiellement le résultat par un enum nu.

---

# 12. Implementation spikes / attention points

## P0 — Prove the split with two real consumers

Avant de figer l'API de `ProgressSource`, implémenter au moins :

1. une action basée sur `TimedInteractionExecutor` ;
2. un `HackSession` custom avec sa propre progression ;
3. un feedback monde lisant `ExecutionPresentation` ;
4. un prompt qui joint `ActionPresentation` et `ExecutionPresentation`.

Si ces quatre cas nécessitent des special cases de type, la frontière n'est pas encore bonne.

## P0 — Replication shape

Tester la forme la plus Godot-native pour synchroniser une collection compacte d'executions :

```text
ExecutionId
ActionId
```

avec :

- start ;
- end ;
- late join ;
- deux executions concurrentes ;
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
- absence de double completion.

Le détail de clock sync peut rester interne au helper.

## P1 — Progress source lifetime

Comparer au minimum :

```text
setter
provider/source
executor query
```

avec attention particulière à :

- stale ExecutionId ;
- source freed ;
- provider remplacé ;
- clear sur end ;
- GDScript interoperability ;
- future GDExtension implementation ;
- allocations / calls per frame.

## P1 — Prediction correlation

Décider si V4 conserve :

```text
one pending request per (target, actionId)
```

ou introduit un request identity.

Le choix doit être motivé par un use case réel et non uniquement par la possibilité théorique de concurrence.

## P1 — Visibility authoring

Décider où vit la politique :

```text
InteractionActionDefinition?
InteractionAction?
Executor?
Interactive default + action override?
replication component?
```

Critère principal : la visibilité décrit qui peut **observer l'exécution**, pas qui peut demander l'action.

Elle ne doit donc pas se retrouver accidentellement couplée aux zones de détection ou à l'availability.

---

# 13. Non-goals

V4 ne cherche pas à :

- faire d'Interaction un framework de hack, crafting, dialogue ou animation ;
- standardiser toutes les formes de progression gameplay ;
- prédire la simulation physique ;
- remplacer Stateful pour les vérités persistantes du monde ;
- imposer qu'une execution ait une progress bar ;
- imposer qu'une execution longue soit timed ;
- rendre les executions autoritaires côté client ;
- diffuser chaque execution à chaque peer indépendamment de l'interest management ;
- créer une abstraction C# que GDScript ne peut pas consommer naturellement.

---

# 14. Migration direction from V3

Ordre conceptuel, pas encore plan de tâches définitif :

```text
1. Introduce InteractionExecutionPresentation
2. Remove ExecutionProgress / HasTimedExecution from ActionPresentation
3. Make active executions queryable from Interactive
4. Reduce core Running result to no timing semantics
5. Extract current Duration/Elapsed clock into timed feature
6. Implement TimedInteractionExecutor on top of Running()
7. Introduce optional generic Progress source
8. Adapt prompt to optionally join action + execution presentation
9. Add execution visibility / replication modes
10. Refactor V3 predicted float into predicted execution presentation
11. Harden with real multiplayer tests and late join
```

`HasTimedExecution` doit disparaître pour la même raison que `ExecutionProgress`: la présentation générique ne doit pas connaître la nature timed de l'executor. Un renderer s'intéresse à une execution active et éventuellement à `Progress`.

---

# 15. Success criteria

La V4 est réussie si les scénarios suivants sont tous naturels sans contourner le framework :

```text
instant action
simple 3-second built-in action
custom animation-driven action
custom replicated hack session
hold-to-select then long execution
progress in prompt
progress on world-space terminal
remote player sees terminal progress outside interaction range
requester-only progress
long action with no progress
multiple concurrent execution groups
late join during a world-observable execution
```

Et surtout si l'on peut expliquer le framework sans exception :

> **ActionPresentation décrit ce qu'un joueur peut demander.**
>
> **Interactive possède les executions qui tournent sur lui.**
>
> **ExecutionPresentation décrit ce qu'un peer peut observer de ces executions.**
>
> **Running signifie seulement que l'execution continue jusqu'à sa terminaison.**
>
> **TimedInteractionExecutor est un helper spécialisé qui produit éventuellement Progress et termine automatiquement une execution.**
>
> **Tout système métier reste libre de posséder sa propre durée, progression, réplication et logique, puis de terminer la même execution générique.**

Si un nouveau cas d'usage respecte ces phrases sans demander au core de savoir s'il s'agit d'un timer, d'un hack, d'une animation ou d'un dialogue, la frontière est probablement au bon endroit.
