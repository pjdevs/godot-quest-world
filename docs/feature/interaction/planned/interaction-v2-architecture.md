# Interaction Framework V2 — Architecture & Implementation Spec

> Ce document définit l’architecture cible et l’ordre de migration du framework d’interaction QuestWorld. Il ne décrit pas seulement les features à ajouter : il fixe les responsabilités, invariants et frontières que l’implémentation doit préserver.

## Goal

Faire évoluer le framework V1 d’un système centré autour de :

```text
focus target
→ validate target
→ emit input signal
→ arbitrary gameplay subscriber
→ InteractionState lifecycle
```

vers un système générique de :

```text
target selection
→ action resolution
→ pure availability evaluation
→ authoritative command
→ explicit single executor
→ execution lifecycle
→ after-the-fact notifications
```

Tout en séparant complètement :

1. **l’état persistant du monde** ;
2. **la disponibilité d’une action** ;
3. **l’exécution temporaire d’une interaction** ;
4. **la présentation/UI**.

Le résultat doit rester naturel dans Godot, fonctionner offline et en multiplayer autoritaire, être testable sans dépendre des signaux, et préparer une future implémentation GDExtension sans exposer une architecture fondamentalement C#-specific.

---

# 1. Why this refactor exists

La V1 a volontairement un modèle simple.

`InteractiveComponent` connaît une éventuelle `InteractionStateful`, considère `Idle` comme disponible et les autres états comme bloquants. 

`InteractionStateful` stocke et persiste :

```text
Idle
Activating
Activated
Deactivating
```

et émet directement ses signaux pendant `ApplyState()`. 

L'exécution gameplay elle-même n'est pas possédée par le core : `InteractiveComponent.StartInteraction()` émet `InteractionInputStarted`, puis n'importe quel subscriber peut produire la mutation réelle. 

Le `Button` actuel illustre exactement cette frontière : il s'abonne à `InteractionInputStarted` et change lui-même le state d'un autre objet. 

Cela entraîne trois problèmes structurels.

### 1.1 Interaction lifecycle != world state

Une porte veut parler :

```text
Closed
Opening
Open
Closing
Locked
```

Une alimentation :

```text
Powered
Unpowered
Overloaded
```

Une pièce :

```text
Dry
Flooded
Draining
```

Ces états existent indépendamment de la manière dont le joueur les modifie.

Le framework ne doit pas demander au gameplay de traduire artificiellement ces concepts vers `Idle / Activating / Activated`.

### 1.2 Signals cannot own commands

Un broadcast ne peut pas répondre à :

> Qui est responsable d'effectuer cette action ?

Avec plusieurs subscribers, le core ne sait pas :

- si quelqu'un a réellement traité la commande ;
- si elle a réussi ;
- si plusieurs handlers ont muté le gameplay ;
- si un subscriber n'était qu'une notification UI/audio/quest.

L'exécution doit donc avoir **un unique propriétaire explicitement configuré**.

### 1.3 Multiple actions make target-level status insufficient

Pour une porte fermée :

```text
Open  → Allowed
Close → Hidden
Kick  → Blocked
```

Le target lui-même n'est ni simplement `Allowed`, ni simplement `Blocked`.

La disponibilité doit devenir **action-level**.

---

# 2. Architectural invariants

Ces règles sont plus importantes que les classes exactes.

Toute implémentation doit les préserver.

## 2.1 World state and interaction execution are independent

Un objet ne devient pas interactible parce que son `StatefulComponent.State == "idle"`.

Le core d'interaction ne donne aucune signification universelle aux valeurs d'un Stateful.

L'état du monde peut influencer une action uniquement à travers une **rule explicite**.

```text
StatefulComponent
       │
       ▼
InteractionRule
       │
       ▼
Action availability
```

Jamais :

```text
InteractiveComponent
→ if Stateful != Idle
→ blocked
```

## 2.2 One action has exactly one executor

Une action configurée doit avoir un propriétaire unique de sa mutation gameplay.

```text
InteractionAction
└─ Executor
```

Il n'existe aucun fallback :

```text
no executor
→ emit signal
→ maybe someone handles it
```

Une action sans executor est une erreur de configuration.

## 2.3 Signals are notifications, never the supported command path

Les signaux publics décrivent uniquement des événements ayant déjà eu lieu :

```text
ActionStarted
ActionCompleted
ActionCancelled
ActionRejected
StateChanged
FocusChanged
```

Ils ne constituent jamais le mécanisme officiel permettant d'effectuer l'action.

## 2.4 Queries are pure

Les opérations suivantes doivent être synchrones, répétables et sans side effect :

```text
EvaluateAvailability
EvaluateRule
CalculateInteractionScore
IsWithinInteractionRange
ResolveActionForInput
GetPresentation
```

Elles ne doivent :

- muter aucun state ;
- appeler aucun RPC ;
- émettre aucun signal ;
- déclencher aucune action ;
- appeler aucun callback gameplay mutable.

## 2.5 Mutation completes before external code runs

Le projet adopte l'invariant :

> Aucun signal, RPC, handler gameplay, Callable ou callback externe ne doit être appelé pendant qu'un objet core est au milieu d'une mutation.

Pattern obligatoire :

```text
validate
↓
mutate local core
↓
finish mutation
↓
external call / executor
↓
mutate result
↓
dispatch notifications
```

Ce principe reprend directement le chantier `non-mutable-pure-architecture`. 

## 2.6 Client input is not the authoritative command

Le client peut résoudre :

```text
"E" → action "open"
```

mais le serveur reçoit :

```text
targetPath + actionId
```

et réévalue entièrement l'action.

Le serveur ne fait jamais confiance à :

- l'Allowed calculé par le client ;
- l'action actuellement affichée ;
- l'état local du target ;
- l'identité de l'executor envoyée par le client.

---

# 3. Target architecture

```text
InteractionInteractor
        │
        │ local input
        │
        ▼
InteractiveComponent
        │
        ├── InteractionAction "open"
        │      ├── Definition
        │      ├── Rules
        │      └── Executor
        │
        ├── InteractionAction "close"
        │      ├── Definition
        │      ├── Rules
        │      └── Executor
        │
        └── InteractionAction "inspect"
               ├── Definition
               ├── Rules
               └── Executor

              authoritative execution
                       │
                       ▼
             gameplay / systems
                       │
              ┌────────┴────────┐
              ▼                 ▼
       StatefulComponent     Inventory
       Quest / machine       Dialogue
       etc.                  etc.
```

`InteractiveComponent` orchestre l'interaction.

Il ne représente plus l'état métier de l'objet.

---

# 4. Generic world state

Le `InteractionStateful` actuel doit progressivement laisser place à un composant générique, idéalement indépendant de `interaction_plugin`.

Proposition :

```text
addons/stateful_plugin/
└── runtime/
    ├── StatefulComponent.cs
    ├── StateSchema.cs
    └── StatefulTypes.cs
```

Namespace cible :

```csharp
QuestWorld.State
```

## 4.1 `StatefulComponent`

Responsabilité unique :

> Posséder une valeur d'état autoritaire, réplicable, persistable et observable.

API conceptuelle :

```csharp
[GlobalClass]
public partial class StatefulComponent : Node
{
    [Export]
    public StateSchema? Schema { get; set; }

    [Export]
    public StringName InitialState { get; set; }

    public StringName State { get; }

    public bool SetState(StringName state);

    public StatefulSavedState SaveState();

    public void LoadState(StatefulSavedState state);
}
```

La valeur runtime est un `StringName`, pas un enum universel.

Exemples :

```text
"closed"
"open"
"locked"

"powered"
"unpowered"

"dry"
"flooded"
```

### Why `StringName`

Cela donne :

- identité stable et légère ;
- serialization simple ;
- usage naturel depuis Godot ;
- absence de dépendance à un enum C# ;
- surface future compatible GDScript/GDExtension ;
- possibilité de créer des états métier sans modifier le plugin.

## 4.2 `StateSchema`

Resource optionnelle :

```csharp
[GlobalClass]
public partial class StateSchema : Resource
{
    [Export]
    public Godot.Collections.Array<StringName> States { get; set; }
}
```

Elle sert essentiellement :

- à la validation editor ;
- à documenter les valeurs possibles ;
- à détecter les typos ;
- à améliorer plus tard l'Inspector.

Elle ne doit pas devenir immédiatement une FSM universelle.

En particulier, **ne pas ajouter maintenant** :

```text
allowedTransitions
transition graph
entry effects
exit effects
guards
hierarchical states
```

Ce serait un autre système.

Un `StatefulComponent` est d'abord un **value holder autoritaire**, pas une state machine complète.

`Schema == null` signifie qu'une valeur libre est acceptée.

## 4.3 State mutation pattern

Le nouveau composant doit appliquer immédiatement le refactor non-mutable :

```csharp
private StateTransition? ApplyStateCore(StringName state);
private void DispatchStateTransition(in StateTransition transition);
```

Conceptuellement :

```text
SetState
  ↓
authority validation
  ↓
ApplyStateCore
  └─ mutate only
  ↓
DispatchStateTransition
  ├─ StateChanged
  ├─ StateChangedAuthority
  └─ StateChangedPresentation
```

Aucun signal dans `ApplyStateCore`.

## 4.4 Persistence

Le snapshot devient :

```csharp
public readonly record struct StatefulSavedState(
    int Version,
    StringName State
);
```

Le framework de state ne stocke toujours aucun fichier lui-même.

Il expose uniquement un snapshot versionné, comme la V1 actuelle.

---

# 5. Interaction action model

Créer un nouveau sous-système :

```text
addons/interaction_plugin/runtime/actions/
├── InteractionAction.cs
├── InteractionActionDefinition.cs
├── InteractionActionExecutor.cs
├── InteractionActionTypes.cs
└── executors/
    └── SetStateInteractionExecutor.cs
```

## 5.1 Definition vs runtime instance

La séparation est :

```text
Resource = reusable static definition
Node     = scene instance / runtime binding
```

### `InteractionActionDefinition : Resource`

Contient uniquement des données partageables.

```csharp
[GlobalClass]
public partial class InteractionActionDefinition : Resource
{
    [Export]
    public StringName Id { get; set; }

    [Export]
    public string Label { get; set; }

    [Export(PropertyHint.MultilineText)]
    public string Description { get; set; }

    [Export]
    public StringName InputActionName { get; set; } = "interact";
}
```

`Id` est l'identité réseau et gameplay stable :

```text
"open"
"close"
"take_all"
"reroute_power"
```

Le label n'est jamais utilisé comme identité.

## 5.2 `InteractionAction : Node`

Représente l'occurrence de l'action sur un target particulier.

```csharp
[GlobalClass]
public partial class InteractionAction : Node
{
    [Export]
    public InteractionActionDefinition? Definition { get; set; }

    [Export]
    public InteractionActionExecutor? Executor { get; set; }

    [Export]
    public Godot.Collections.Array<InteractionRule> Rules { get; set; }

    [Export]
    public int Priority { get; set; }

    [Export]
    public StringName ConcurrencyGroup { get; set; } = "default";

    [Export]
    public bool Automatic { get; set; }

    [Export]
    public bool CancelOnInputReleased { get; set; }
}
```

La Definition est statique.

Le Node porte les choix propres à cette instance :

- executor ;
- rules ;
- priorité locale ;
- concurrency ;
- références de scène via l'executor ;
- comportement automatique.

## 5.3 `InteractiveComponent.Actions`

Ajouter :

```csharp
[Export]
public Godot.Collections.Array<InteractionAction> Actions { get; set; }
```

Les actions doivent idéalement être des enfants de scène du même actor, puis référencées explicitement dans cet array.

Conserver la philosophie actuelle du plugin :

> explicit Inspector references rather than hidden tree discovery.

---

# 6. Availability replaces target-wide Allowed/Blocked

La V1 utilise `InteractionAllowed | InteractionBlocked`. 

La V2 introduit trois états :

```csharp
public enum InteractionAvailabilityKind
{
    Hidden,
    Blocked,
    Allowed
}

public readonly record struct InteractionAvailability(
    InteractionAvailabilityKind Kind,
    string Reason = ""
);
```

**Note de dev** : je préfèrerais partir sur une Union type comme fait ailleurs dans le projet
avec une reason uniquement pour Blocked.

## Semantics

### Hidden

L'action ne fait pas partie des choix présentés actuellement.

Exemple :

```text
Close while door is Closed
```

### Blocked

L'action existe et peut être montrée, mais n'est pas executable.

Exemple :

```text
Open → Requires keycard
```

### Allowed

L'action peut être demandée.

---

# 7. Rules become action-aware

Le contexte devient :

```csharp
public readonly record struct InteractionContext(
    InteractionInteractor Interactor,
    InteractiveComponent Interactive,
    InteractionAction Action
);
```

`InteractionRule` reste une `Resource`.

C'est une bonne frontière aujourd'hui : les rules sont déjà explicitement définies comme synchrones, pures et sans état runtime mutable. 

Signature :

```csharp
public abstract InteractionAvailability Evaluate(
    in InteractionContext context
);
```

Une rule qui passe retourne `Allowed`.

Une rule peut aussi décider :

```text
Hidden
Blocked(reason)
```

## Evaluation order

`InteractiveComponent` possède éventuellement des `TargetRules`.

Chaque Action possède ses `Rules`.

Le pipeline est :

```text
target invariants
↓
TargetRules
↓
Action Rules
↓
concurrency availability
↓
final availability
```

L'évaluation reste ordonnée.

Le premier résultat non-`Allowed` gagne.

Cela préserve la simplicité actuelle du pipeline.

---

# 8. Generic Stateful rules

Pour connecter le nouveau Stateful au système d'interaction sans recréer un couplage direct, fournir une rule générique.

```text
StatefulStateInteractionRule
```

Concept :

```csharp
[GlobalClass]
public partial class StatefulStateInteractionRule : InteractionRule
{
    [Export]
    public NodePath StatefulPath { get; set; }

    [Export]
    public StringName ExpectedState { get; set; }

    [Export]
    public bool Invert { get; set; }

    [Export]
    public InteractionAvailabilityKind MismatchAvailability { get; set; }
        = InteractionAvailabilityKind.Hidden;

    [Export]
    public string BlockReason { get; set; }
        = "Action unavailable.";
}
```

`StatefulPath` est résolu relativement à l'`InteractiveComponent`.

La rule reste pure : elle lit le state, elle ne le modifie jamais.

Exemple :

```text
Action Open
└─ StateRule
   ├─ StatefulPath = ../Stateful
   ├─ ExpectedState = "closed"
   └─ mismatch = Hidden
```

**Note de dev**: jsp quoi penser de cette approche NodePath.
Et il faut pouvoir attendre plusieurs states pour moi.
Il faut se demander pour une porte ouvrir fermer, comment on implémente ça ?
A priori on attend opended ou closed comme ça on block sur opening et closing.
Mais si on a une rule par action pas besoin d'en avoir plusieurs à première vue :
open attend closed et close attend opened. Reste à voir pour les blocked.

---

# 9. Action resolution from local input

Le contrôleur du personnage continue à fournir les inputs.

Nouvelle API :

```csharp
public bool TryStartInteractionInput(StringName inputActionName);
public bool TryEndInteractionInput(StringName inputActionName);
```

Le resolver inspecte uniquement les actions du target focusé dont :

```text
Definition.InputActionName == inputActionName
```

Il ignore les `Hidden`.

Ordre de préférence :

```text
Allowed
before
Blocked
```

Puis :

```text
higher Priority first
```

Cela permet volontairement :

```text
Open  [E] → Hidden while open
Close [E] → Allowed while open
```

sans changer les bindings du joueur.

S'il existe deux actions `Allowed` avec le même input et la même priorité, le résultat runtime doit rester déterministe, mais l'éditeur doit produire un warning de configuration.

Le fallback déterministe peut être :

```text
Priority DESC
ActionId ASC
```

**Note de dev**: Attention potentiellement c'est interactor qui va gérer un timer entre le start
et le end pour savoir si une input a été hold. C'est important de pouvoir supporter le hold,
et aussi d'avoir exactement à combien de pourcent sur l'attendu ça a été hold pour feedback UI. 

---

# 10. Networking contract

L'ancien flux transporte seulement le target. `InteractionInteractor` effectue aujourd'hui la validation client puis le RPC vers le serveur. 

La V2 transporte également l'identité de l'action.

```csharp
ServerTryStartInteraction(
    NodePath targetPath,
    StringName actionId
);
```

Flux serveur :

```text
receive targetPath + actionId
↓
validate sender
↓
resolve target
↓
validate candidate / distance / angle / LOS
↓
target.ResolveAction(actionId)
↓
evaluate availability again
↓
validate concurrency
↓
reserve execution
↓
execute
```

Le serveur **ne reçoit pas l'Executor**.

Il le résout depuis sa propre scène via `actionId`.

Le refus retourne idéalement :

```text
targetPath
actionId
reason
```

afin que l'UI puisse rattacher le refus à la bonne action.

Ce contrat vaut aussi pour les gestes : voir §18.1, la couche de geste est locale et ne transporte rien de nouveau.

---

# 11. Explicit executor model

Créer :

```csharp
[GlobalClass]
public abstract partial class InteractionActionExecutor : Node
{
    public abstract InteractionExecutionResult Execute(
        in InteractionExecutionContext context
    );
}
```

Cette abstraction représente l'unique propriétaire supporté de la mutation gameplay d'une action.

Aucun signal n'est nécessaire pour trouver l'executor.

## Execution context

```csharp
public readonly record struct InteractionExecutionContext(
    ulong ExecutionId,
    InteractionInteractor Interactor,
    InteractiveComponent Interactive,
    InteractionAction Action
);
```

L'ExecutionId est alloué **avant** l'appel de l'executor.

---

# 12. Execution result and long-running actions

Ne pas confondre input gesture et durée de l'action.

Une pression peut démarrer une action longue.

Un hold peut confirmer une action instantanée.

La V2 doit modéliser le lifetime de l'exécution indépendamment.

```csharp
public enum InteractionExecutionDisposition
{
    Completed,
    Running,
    Rejected,
    Failed
}

public readonly record struct InteractionExecutionResult(
    InteractionExecutionDisposition Disposition,
    string Reason = ""
);
```

**Note de dev**: plutôt `InteractionExecutionStatus` ? et pareil un union type avec le reason
uniquement sur les types concernés (pas sur qu'on veuille expliquer pourquoi ça a bien été completed).

## Meaning

### Completed

L'action a terminé synchroniquement.

La réservation peut être immédiatement libérée.

### Running

L'action a commencé mais terminera plus tard.

Le core garde une `InteractionExecution` active.

### Rejected

L'executor a refusé l'action pour une condition découverte à la frontière d'exécution.

Ce cas doit rester rare : les conditions normales appartiennent aux Rules.

### Failed

Erreur gameplay ou technique après acceptation.

Ce n'est pas un simple "not allowed".

---

# 13. Active execution model

Créer un type interne :

```csharp
internal readonly record struct InteractionExecution(
    ulong Id,
    InteractionInteractor Interactor,
    InteractionAction Action,
    StringName ConcurrencyGroup
);
```

`InteractiveComponent` possède :

```text
_activeExecutions
```

et non plus uniquement :

```text
_activeInteractor
```

API serveur :

```csharp
public bool CompleteExecution(ulong executionId);
public bool CancelExecution(ulong executionId, string reason = "");
```

Un Executor ayant retourné `Running` conserve son `ExecutionId` et appelle plus tard une de ces méthodes.

---

# 14. Concurrency

**Note de dev**: comme expliqué après faire un truc simple. Il faut juste prendr en compte que ça gère : quelqu'un interact, je peux ou ne peux pas en même temps point barre normalement.
Sinon j'ai tendance à dire qu'en général les différentes actions sont exclusives entre elles toujours.
On fait soit E soit hold E, soit R mais pas plusieurs en même temps.
Sauf dans le cas des long running. Effectivement, on se voit pmal bloquer une inspection pendant un long hack.
Donc à valider mais dans un premier temps c'est possible qu'un simple `bool IsExclusive`
par action soit suffisant à moins qu'on trouve des contres exemples gameplay sympas.
Mais le group a l'air très flexible sans être une trop grosse machinerie donc ça peut être worth.

Ne pas construire un lock manager sophistiqué.

Introduire seulement :

```text
ConcurrencyGroup : StringName
```

Règle initiale :

> Deux executions actives appartenant au même `InteractiveComponent` et au même `ConcurrencyGroup` sont exclusives.

Default :

```text
"default"
```

Cela reproduit le comportement V1.

Exemple futur :

```text
Hack           → "controls"
Emergency Stop → "controls"
Inspect        → "inspection"
```

Une inspection pourrait donc rester disponible pendant un hack.

Le système ne doit pas encore gérer :

```text
shared locks
reader/writer
capacity N
cross-target locks
```

YAGNI.

---

# 15. Executor call boundary

L'appel à l'executor est du code externe arbitraire.

Le target doit être cohérent avant cet appel.

Flux obligatoire :

```text
EvaluateAction
↓
ReserveExecutionCore()
↓
mutation complete
↓
Executor.Execute(context)
↓
ApplyExecutionResultCore()
↓
mutation complete
↓
DispatchActionNotifications()
```

Il ne faut jamais faire :

```text
mutate _activeExecutions
↓
Executor.Execute()
↓
continue modifying same half-finished mutation
```

L'état avant l'executor doit déjà satisfaire tous les invariants.

---

# 16. Generic `SetStateInteractionExecutor`

Premier executor générique à fournir :

```csharp
[GlobalClass]
public partial class SetStateInteractionExecutor
    : InteractionActionExecutor
{
    [Export]
    public NodePath StatefulPath { get; set; }

    [Export]
    public StringName TargetState { get; set; }
}
```

`Execute()` :

```text
resolve Stateful
↓
SetState(TargetState)
↓
success → Completed
failure → Failed
```

Ce Node remplace directement beaucoup de scripts `Button` simples.

Mais il ne doit pas tenter de gérer :

```text
animations
delays
multiple effects
audio
quest update
inventory
```

Si un comportement est plus complexe, utiliser un Executor spécialisé.

Ne pas introduire encore un système universel `InteractionEffect[]`.

---

# 17. Presentation model

Le snapshot V1 représente essentiellement une seule action. 

Créer :

```csharp
public readonly record struct InteractionActionPresentation(
    StringName ActionId,
    string Label,
    string Description,
    StringName InputActionName,
    InteractionAvailability Availability
);
```

et :

```csharp
public readonly record struct InteractionTargetPresentation(
    InteractiveComponent Interactive,
    string DisplayName,
    string Description,
    IReadOnlyList<InteractionActionPresentation> Actions,
    bool IsFocused
);
```

`Hidden` n'est normalement pas inclus dans `Actions`.

`Blocked` reste inclus pour permettre :

```text
[E] Open
Requires maintenance keycard
```

## Focus

Un target :

- avec au moins une action Allowed ou Blocked peut être focusé ;
- dont toutes les actions sont Hidden doit être ignoré par le focus.

Le scoring géométrique reste target-level.

---

# 18. Input gestures are deliberately deferred

La V2 doit préparer plusieurs inputs grâce à :

```text
Definition.InputActionName
```

mais ne doit pas encore construire une taxonomie complète :

```text
Press
Hold
DoubleTap
Mash
TimedSequence
Chord
```

Le chantier "long interaction" doit d'abord utiliser le nouveau `Running` lifecycle.

Une future abstraction `InteractionInputTrigger` pourra être ajoutée indépendamment sans toucher au protocole réseau :

```text
local gesture
→ actionId
→ authoritative action command
```

C'est précisément cette séparation qu'il faut préserver.

**Note de dev**: Comme dit plus haut seul le Hold reste essentiel donc le livrera direct
ça permettre aussi de valider qu'on peut s'implémenter différents type d'action.

## 18.1 Input = sélection, jamais une garde

Décision prise après la Task 5. Ce cas est structurant, il doit être pris en compte avant d'écrire la Task 7.

### Règle

> L'input, geste compris, est un **mécanisme de sélection local**. Il ne garde rien.
>
> Tout ce qui doit être gardé est soit une **rule d'availability** — est-ce que j'ai le droit ? — soit une **durée d'exécution** — pendant combien de temps suis-je engagé ? Ces deux-là sont autoritaires.

Le contrat §10 ne change donc pas. Le client résout `(target, input, geste)` → `actionId` localement et envoie `targetPath + actionId`, exactement comme la Task 5 le fait déjà. La couche de geste est entièrement cliente.

### Pourquoi un geste forgé ne gagne rien

Un client qui prétend « j'ai fait hold E » au lieu de « tap E » ne fait que désigner une autre action. Le serveur la re-résout depuis sa propre scène et réévalue ses rules. Si `force_open` demande un pied-de-biche, c'est la rule qui garde, pas le geste.

Le seul gain d'un geste forgé est d'économiser le temps d'appui, ce qui n'est pas un privilège.

Rendre le seuil autoritaire coûterait un RPC de press supplémentaire à chaque interaction, un état de geste en attente par interactor, une tolérance de jitter et une divergence de prédiction entre la barre cliente et le chrono serveur — pour zéro gain de sécurité.

### Le vrai hold gameplay est une execution, pas un geste

Quand le joueur doit **rester engagé**, cinq secondes devant un terminal et vulnérable, l'autorité est dans l'exécution :

```text
client  : commande l'action
serveur : démarre une execution Running et possède le chrono
serveur : valide en continu portée, angle, LOS, availability
serveur : complète quand SON chrono est écoulé
client  : un end anticipé ne fait qu'annuler
```

Le serveur n'a pas besoin du `end` du client pour terminer : son propre chrono complète. Un client forgé ne peut donc ni raccourcir ni allonger la durée.

Résidu accepté : un client qui ne renvoie jamais son `end` reste « en train de tenir » sans tenir. Il ne gagne rien de plus, la portée et le LOS restant validés pendant toute l'exécution — il doit rester physiquement devant l'objet.

### Seuil de geste ≠ durée d'exécution

§12 pose déjà la distinction. Elle doit être appliquée strictement, sinon deux chronos font le même travail et la barre de progression redevient client-authoritative.

```text
seuil de hold     = sélection, local,   feedback local
Running execution = durée,     serveur, feedback autoritaire
```

Les combinaisons doivent être exprimables :

- « hold E 5 s pour hacker, barre de progression, annulé si je lâche » est une action **immédiate**, seuil `0`, avec une execution `Running` de 5 s et `CancelOnInputReleased`. La barre vient de l'exécution autoritaire, pas du timer d'input.
- « E ouvre, E maintenu 1 s défonce » est deux actions, seuil `1 s` local, exécutions instantanées.
- Composer les deux étages est possible mais **additionne** les durées : un seuil de 5 s devant un Running de 5 s fait 10 s pour le joueur. À n'utiliser que si c'est réellement voulu.

Le seuil n'existe donc que pour **départager** plusieurs actions sur un même input. Une action seule sur son input a un seuil de `0`.

### Une action soutenue doit partir au seuil

Une action `CancelOnInputReleased` est soutenue par **la même touche** que celle qui l'a sélectionnée. Si elle partait au relâchement, son start et son end arriveraient au même instant : elle naîtrait annulée.

```text
CancelOnInputReleased
→ déclenchement au seuil, jamais au relâchement
```

Cela tranche par la correction, et non par le feeling, le choix du moment de déclenchement : **au seuil**. Le multi-marche — 1 s ouvrir, 3 s défoncer — exigerait le relâchement et est donc incompatible avec les actions soutenues. Le retenir comme non-objectif tant qu'un besoin réel n'apparaît pas.

### Emplacement

Le chrono de geste et le ratio de progression appartiennent à `InteractionInteractor`, conformément à la note de dev de §9. Le contrôleur d'input du jeu continue d'appeler press et release sans rien connaître des gestes.

Deux actions `Allowed` sur le même couple `(input, trigger)` restent une erreur de configuration à signaler dans l'éditeur en Task 11 : le joueur n'a aucun moyen de les distinguer.

---

# 19. Line of sight belongs to spatial validation

Le LOS n'est pas une gameplay Rule optionnelle.

La distance et l'angle sont déjà vérifiés dans la couche Interactor côté serveur. 

L'occlusion doit être ajoutée au même pipeline :

```text
candidate
distance
angle
line of sight
```

Ainsi un designer ne peut pas accidentellement rendre un coffre interactible à travers un mur en oubliant une rule.

---

# 20. Refusal feedback

Le chantier `interaction-refusal-feedback.md` est essentiellement déjà fourni par `InteractionRejected`, aussi bien en prévalidation locale qu'après refus serveur. 

La V2 ne doit pas inventer un second système.

Il faut simplement rendre le refus action-aware :

```text
target
actionId
reason
```

---

# 21. Non-mutable refactor comes first

Avant de changer le modèle public, nettoyer les frontières de mutation actuelles.

## `InteractionStateful`

Passer de :

```text
ApplyState
├─ mutate
├─ signals
└─ callbacks indirectly
```

à :

```text
ApplyStateCore
└─ mutate + result

DispatchStateTransition
└─ signals
```

## `InteractionInteractor`

Extraire :

```csharp
FocusChangeResult RecalculateFocusCore();
void DispatchFocusChange(in FocusChangeResult result);
```

Aujourd'hui le focus est muté puis des signaux/status sont immédiatement dispatchés dans la même méthode. 

## `InteractiveComponent`

Les opérations :

```text
StartInteraction
StartInteractionPhase
EndInteractionPhase
ReleaseInteractionInput
```

doivent également séparer :

```text
core mutation
from
external dispatch
```

Ce refactor ne doit initialement changer aucun comportement externe.

---

# 22. Migration of current responsibilities

## Remove from `InteractiveComponent`

Ces concepts deviennent action-level ou disparaissent :

```text
InteractionActionName
AutomaticInteraction
ActivatedReason
BusyReason
Stateful-based Idle/Activated interpretation
```

`Stateful` ne doit plus être requis ou spécialement compris par `InteractiveComponent`.

## Keep on `InteractiveComponent`

```text
InteractionArea
IndicationArea
InteractionAnchor
DisplayName
Description
target-level rules
action collection
execution reservations
```

## Move to `InteractionActionDefinition`

```text
label
action description
input action name
action stable id
```

## Move to `InteractionAction`

```text
executor
action rules
priority
automatic
concurrency group
cancel-on-release policy
```

---

# 23. Concrete Door example

Target scene :

```text
Door
├─ StatefulComponent
│  ├─ Schema = DoorStates
│  └─ State = "closed"
│
├─ InteractiveComponent
│
├─ OpenAction
│  ├─ Definition = Open
│  │  ├─ Id = "open"
│  │  ├─ Label = "Open"
│  │  └─ Input = "interact"
│  │
│  ├─ Rules
│  │  └─ State == "closed"
│  │     mismatch → Hidden
│  │
│  └─ Executor
│     └─ SetState("open")
│
└─ CloseAction
   ├─ Definition = Close
   │  ├─ Id = "close"
   │  ├─ Label = "Close"
   │  └─ Input = "interact"
   │
   ├─ Rules
   │  └─ State == "open"
   │     mismatch → Hidden
   │
   └─ Executor
      └─ SetState("closed")
```

Le framework d'interaction ne sait jamais ce que `"open"` signifie.

---

# 24. Concrete Button → LeverWall migration

Actuellement :

```text
Button
→ subscribes InteractionInputStarted
→ finds IStatefulProvider
→ SetState(Activating)
```

Le nouveau modèle :

```text
Button
├─ InteractiveComponent
└─ ActivateAction
   ├─ Id = "activate"
   └─ SetStateInteractionExecutor
      ├─ StatefulPath = LeverWall/Stateful
      └─ TargetState = "raising"
```

Puis :

```text
LeverWall
└─ presentation script
   listens Stateful.StateChangedPresentation
```

`IStatefulProvider` devient inutile pour cette intégration.

Le gameplay utilise directement un objet Godot concret plutôt qu'une interface C# comme contrat public.

---

# 25. Cross-language contract

La surface publique à privilégier est :

```text
Node
Resource
StringName
NodePath
Godot Array
signals
virtual/bound methods
```

Éviter de faire reposer le framework sur des abstractions publiques telles que :

```text
IStatefulProvider
IInteractionExecutor
IInteractionSomething
```

Des interfaces internes restent acceptables en C#.

Mais le modèle architectural doit pouvoir être retranscrit en GDExtension sans nécessiter une sémantique propre au runtime .NET.

---

# 26. Editor validation

Étendre :

`addons/interaction_plugin/editor/InteractionValidator.cs`

Le validator existe déjà et centralise les warnings Inspector du plugin. 

Il doit détecter au minimum :

```text
Interactive without actions
null action reference
Action without Definition
empty ActionId
duplicate ActionId on one target
Action without Executor
invalid StatefulPath
state absent from assigned StateSchema
duplicate/conflicting input configuration
invalid concurrency group
```

Une configuration incorrecte doit idéalement être visible avant le lancement du jeu.

---

# 27. Implementation roadmap

L'implémentation doit se faire en plusieurs changements reviewables.

Chaque étape doit laisser le projet compilable et les tests verts.

---

## Task 1 — Enforce mutation/dispatch boundaries

**Modify:**

```text
addons/interaction_plugin/runtime/state/InteractionStateful.cs
addons/interaction_plugin/runtime/interactive/InteractiveComponent.cs
addons/interaction_plugin/runtime/interactor/InteractionInteractor.cs
addons/interaction_plugin/tests/InteractionBehaviorTest.cs
```

### Deliverable

Aucun comportement public ne change.

Mais les mutations importantes utilisent désormais :

```text
*Core()
→ typed result
→ Dispatch*
```

### Tests

Ajouter des tests démontrant séparément :

```text
core transition result
final state
dispatch signal count
focus transition result
```

### Gate

Grep mental/manuel :

```text
core mutation method
    contains no EmitSignal
    contains no Rpc/RpcId
    calls no arbitrary gameplay handler
```

---

## Task 2 — Introduce generic Stateful

**Create:**

```text
addons/stateful_plugin/runtime/StatefulComponent.cs
addons/stateful_plugin/runtime/StateSchema.cs
addons/stateful_plugin/runtime/StatefulTypes.cs
```

Ajouter des tests dédiés.

### Deliverable

Un Stateful peut exister sans Interaction.

Il supporte :

```text
StringName state
authority
replication
save/load
schema validation
universal/authority/presentation notifications
```

### Important

Ne pas encore supprimer `InteractionStateful`.

Faire coexister les deux pendant la migration.

---

## Task 3 — Introduce Action model and Availability

**Create:**

```text
addons/interaction_plugin/runtime/actions/InteractionAction.cs
addons/interaction_plugin/runtime/actions/InteractionActionDefinition.cs
addons/interaction_plugin/runtime/actions/InteractionActionTypes.cs
addons/interaction_plugin/runtime/actions/InteractionActionExecutor.cs
```

**Modify:**

```text
addons/interaction_plugin/runtime/InteractionStatus.cs
addons/interaction_plugin/runtime/interactive/InteractiveComponent.cs
addons/interaction_plugin/runtime/rules/InteractionRule.cs
```

### Deliverable

Un target peut contenir plusieurs actions.

Chaque action peut être :

```text
Hidden
Blocked
Allowed
```

Aucune action n'est encore forcément routée par le réseau à cette étape.

### Tests

Cas obligatoire :

```text
Closed door:
Open  Allowed
Close Hidden

Open door:
Open  Hidden
Close Allowed
```

---

## Task 4 — Action-aware presentation and focus

**Modify presentation runtime and tests.**

### Deliverable

`GetPresentation()` retourne une collection d'actions visibles.

Les targets sans aucune action visible ne prennent pas le focus.

Les actions Blocked restent présentables.

### Preserve

Le focus reste target-level.

Ne pas créer un focus indépendant par action.

---

## Task 5 — Action command routing and RPC

**Modify:**

```text
addons/interaction_plugin/runtime/interactor/InteractionInteractor.cs
addons/interaction_plugin/runtime/interactive/InteractiveComponent.cs
addons/interaction_plugin/tests/InteractionBehaviorTest.cs
```

### Client request

```text
TryStartInteractionInput(inputAction)
↓
resolve target + action
↓
local availability
↓
ServerTryStartInteraction(targetPath, actionId)
```

### Server

```text
validate sender
validate geometry
resolve own action
evaluate availability
execute
```

### Security test

Un client demandant :

```text
actionId = "open"
```

alors que l'action serveur est Blocked doit être rejeté même si le client pensait qu'elle était Allowed.

---

## Task 6 — Replace broadcast execution with explicit Executor

Modifier le pipeline afin que :

```text
InteractionInputStarted
```

ne déclenche plus officiellement le gameplay.

### Deliverable

Une action valide :

```text
→ exactly one Executor.Execute()
```

Une action sans executor :

```text
→ configuration failure
```

Les nouveaux signaux sont after-the-fact :

```text
InteractionActionStarted
InteractionActionCompleted
InteractionActionCancelled
InteractionActionRejected
```

### Test critical

Brancher plusieurs observers aux signaux ne doit jamais provoquer plusieurs exécutions.

---

## Task 7 — Introduce execution lifecycle

Ajouter :

```text
InteractionExecution
InteractionExecutionResult
ExecutionId
active execution storage
CompleteExecution
CancelExecution
ConcurrencyGroup
CancelOnInputReleased
```

### Hold

Livrer également le Hold spécifié en §18.1. La couche de geste est entièrement locale et ne touche pas au contrat réseau ; ce qui est autoritaire, c'est l'execution `Running` et sa validation continue. Une action `CancelOnInputReleased` se déclenche au seuil, jamais au relâchement. La mémoire locale `input → actionId` introduite en Task 5 disparaît ici, remplacée par l'`ExecutionId`.

### Replace

Progressivement retirer la dépendance conceptuelle à :

```text
StartInteractionPhase
EndInteractionPhase
```

### Compatibility

Ces méthodes peuvent temporairement rester comme wrappers si cela facilite la migration de l'exemple V1.

Elles doivent disparaître une fois tous les exemples migrés.

**Note de dev**: le projet est vide donc pas hésiter à casser les micros exemple et les migrer.

**État**: sans objet. `StartInteractionPhase` et `EndInteractionPhase` ont disparu dès la Task 6, sans passer par des wrappers, et les exemples sont déjà migrés (Tasks 6, 8 et 12).

### Automatic action retry

Trou identifié pendant la Task 12, à traiter ici.

`TryStartAutomaticInteraction` n'est appelé que sur *changement* de focus : `DispatchFocusChange` le gate sur `result.Changed`. Une action `Automatic` qui devient `Allowed` alors que le focus ne bouge pas ne part donc jamais d'elle-même, et le joueur doit re-focuser la cible.

Ce n'est pas une régression : le push `NotifyStatusChanged` supprimé en Task 12 était gaté de la même façon et ne la rejouait pas non plus.

Cette task doit décider où vit le retry :

```text
côté focus      → retenter quand l'availability de la cible focusée passe à Allowed
côté execution  → une action automatique est une execution comme une autre,
                  éligible tant qu'aucune n'est en cours
```

Le second est cohérent avec l'`ExecutionId` et la concurrence de cette étape, et évite de relancer une action déjà `Running`.

---

## Task 8 — Stateful integration primitives

**Create:**

```text
StatefulStateInteractionRule
SetStateInteractionExecutor
```

### Deliverable

Construire Open/Close/Button/LeverWall sans script glue basé sur `InteractionInputStarted`.

Migrer en priorité :

```text
quest_world/interactibles/button/Button.cs
quest_world/interactibles/lever_wall/LeverWall.cs
```

Le `Button.cs` actuel devient idéalement inutile ou réduit à un comportement réellement spécifique au jeu.

---

## Task 9 — Rule combinators

Seulement après stabilisation de l'Availability.

Créer :

```text
AllOfInteractionRule
AnyOfInteractionRule
NotInteractionRule
```

Le tableau actuel de rules reste implicitement `AllOf`.

Ne pas construire d'arbre de logique custom editor dans cette étape.

---

## Task 10 — LOS authoritative validation

Ajouter l'occlusion au même pipeline spatial que :

```text
distance
angle
```

Tester localement et côté serveur.

Une action derrière un obstacle doit échouer même si toutes ses gameplay rules passent.

---

## Task 11 — Editor polish

Étendre :

```text
InteractionValidator
InteractionInspectorPlugin
```

avec tous les diagnostics définis précédemment.

Le validator doit connaître :

```text
InteractiveComponent
InteractionAction
StatefulComponent integration nodes
```

sans rendre le runtime `[Tool]`.

Conserver le pattern editor/runtime déjà adopté par le projet.

---

## Task 12 — Remove V1 compatibility layer

Après migration des scènes et tests :

supprimer :

```text
InteractionState enum as world-state abstraction
InteractiveComponent.Stateful
Idle == interactible assumption
ActivatedReason
BusyReason
gameplay execution through InteractionInputStarted
IStatefulProvider usages made obsolete
StartInteractionPhase / EndInteractionPhase wrappers
```

Mettre à jour :

```text
docs/feature/interaction/interaction.md
example scenes
XML documentation
```

---

# 28. Testing strategy

Les tests existants sont actuellement répartis entre comportement, configuration et scènes. 

Conserver cette séparation.

## Core tests

Doivent tester directement :

```text
availability
action resolution
state transitions
execution reservations
concurrency
execution completion
focus result
```

sans dépendre principalement des signaux.

## Dispatch tests

Tester séparément :

```text
correct notification
exactly once
correct authority scope
correct presentation scope
```

## Multiplayer tests

Au minimum :

```text
spoofed peer rejected
unknown actionId rejected
out-of-range action rejected
blocked server action rejected
client stale availability rejected
valid action executor runs exactly once
```

## Scene tests

Vérifier une scène réelle :

```text
Closed door
[E] → Open

Open door
[E] → Close
```

et un exemple long-running.

---

# 29. Validation commands

À chaque milestone :

```powershell
dotnet format quest-world.csproj
dotnet build
dotnet test
```

Puis test headless :

```powershell
godot --headless `
  --path . `
  --scene res://addons/interaction_plugin/examples/InteractionDemo.tscn `
  --quit-after 2 `
  --log-file .godot/interaction-demo.log
```

Ces commandes correspondent au pipeline de validation déjà documenté pour la V1. 

**Note de dev**: écrire des tests non Godot quand possible et pas besoin de scène.

---

# 30. Explicit non-goals

Cette refonte ne doit pas devenir une tentative de créer immédiatement :

- une FSM universelle ;
- un event bus global ;
- un Gameplay Ability System ;
- un système d'Effects composables ;
- une task graph async ;
- un moteur de scripting visuel ;
- une réplication générique de n'importe quel objet ;
- un système d'input complet ;
- un système de save global ;
- une lock manager distribuée.

Ces fonctionnalités pourront émerger si des cas réels les justifient.

La V2 doit d'abord fournir une architecture solide pour les vrais use cases QuestWorld.

---

# 31. Definition of done

Le chantier architecture V2 est considéré terminé lorsque les affirmations suivantes sont toutes vraies.

### Interaction

```text
A target may expose N actions.
Every action has a stable ActionId.
Every action has exactly one executor.
Every action evaluates Hidden / Blocked / Allowed.
The server receives target + actionId.
The server revalidates the action.
Signals do not own gameplay commands.
```

### State

```text
World state is represented independently from interaction.
State values are generic StringNames.
Interaction does not interpret any state value universally.
State changes are replicated and persistable.
State can be consumed by interaction through pure rules.
```

### Execution

```text
Instant and long-running execution use the same pipeline.
Running executions have stable ids.
Concurrency is explicit.
Input release and action completion are separate concepts.
```

### Architecture

```text
Queries do not mutate.
Core mutation does not emit callbacks.
External code executes only at explicit boundaries.
Notifications happen after state is coherent.
Public architecture is based primarily on Godot concepts,
not C#-specific interfaces.
```

### Concrete proof

Le framework doit pouvoir exprimer sans custom glue :

```text
Door:
    Open if Closed
    Close if Open

Button:
    Set another object's state

Machine:
    Inspect always
    Repair only if Broken
    Repair may run for several seconds

Container:
    Open
    Take All when non-empty
```

Et une action complètement spécifique doit rester possible avec un custom `InteractionActionExecutor` sans modifier le core.

---

# Final architectural rule

Quand une future feature doit être ajoutée, déterminer d'abord dans quelle catégorie elle appartient :

```text
"What is true in the world?"
    → Stateful / gameplay state

"May the player do this?"
    → InteractionRule / Availability

"What command did the player choose?"
    → InteractionAction

"Who performs the mutation?"
    → InteractionActionExecutor

"Is that command still running?"
    → InteractionExecution

"What should the player see?"
    → Presentation

"What happened?"
    → Notification signal
```

Si une implémentation commence à mélanger deux de ces réponses dans la même abstraction, considérer cela comme un signal que la frontière architecturale est en train de dériver.