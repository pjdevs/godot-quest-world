# Interaction Offers and Targeted Owned Actions Implementation Plan

Je partirais sur **5 gros blocs** :

```text
A. Gameplay Action sait cibler
        ↓
B. Interaction sait offrir une action possédée ailleurs
        ↓
C. Interaction sait réserver la target
        ↓
D. Take / Drop prouvent le modèle en jeu
        ↓
E. Door mixed ownership + migration + destruction de l'ancien modèle
```

Avec D et E réservé au maintainer du repo pour tester l'ergo du framework.

La [spec](./owned-vs-offered-targeted-actions.md) fixe déjà très bien le contrat final : l’Interactive décrit des **offers**, l’action garde un propriétaire unique, la target est une donnée d’invocation, les règles target/action restent séparées et les deux scopes de concurrence restent orthogonaux.

---

# Règles de travail du chantier

Pour **chaque tâche de code** :

1. écrire/adapter le test qui expose le comportement attendu ;
2. vérifier qu’il échoue pour la bonne raison ;
3. faire la modification minimale ;
4. lancer la plus petite suite impactée ;
5. `task format`;
6. `task build`;
7. update docs et décisions durable ADR style si besoin ;
8. commit.

On garde `task test:gameplay`, `task test:interaction`, `task test:network`, etc. comme gates intermédiaires et on réserve la grosse CI/full suite à la fin ou aux checkpoints réellement transverses. Le dépôt a déjà les suites qu’il faut pour faire ça proprement.

---

# PHASE A — Faire de `Target` une primitive de Gameplay Action

## Task 1 — `GameplayActionContext.Target`

### But

Faire en sorte qu’une invocation générique sache représenter :

```text
Owner = Player
Action = Take
Target = Battery
```

sans qu’Interaction soit impliqué.

### Fichiers principaux

- `addons/gameplay_action_plugin/runtime/GameplayActionTypes.cs`
- `addons/gameplay_action_plugin/runtime/actions/GameplayActionComponent.cs`
- `addons/gameplay_action_plugin/tests/GameplayActionExecutionTest.cs`
- éventuellement `GameplayActionComponentTest.cs`
- `TestGameplayActionExecutor.cs`

### Modifs

Ajouter :

```csharp
Node? Target
```

dans `GameplayActionContext`, puis :

```csharp
GetTarget<T>()
```

Faire accepter un `target = null` optionnel aux chemins génériques :

```text
CreateContext()
EvaluateAction()
ExecuteAction()
ExecuteRequestedAction()
```

Le point important : **Target doit entrer dans `ActiveExecution`**.

Aujourd’hui les callbacks terminaux reconstruisent leur contexte depuis l’exécution réservée. Si la target n’y est pas conservée, `OnExecutionCompleted`, `OnExecutionCancelled`, `OnExecutionFailed`, etc. la perdent. 

### Tests

- une rule reçoit la target ;
- l’executor reçoit exactement la même instance ;
- une execution `Running` conserve la target ;
- son callback terminal retrouve cette target ;
- une action non ciblée fonctionne toujours avec `Target == null`;
- `ExecuteAction("foo", target)` programmatique fonctionne.

### Non-goal

Aucun réseau. Aucun Interaction. Aucun binding.

### Commit

```text
feat(gameplay-action): add invocation target to action context
```

---

## Task 2 — Faire transporter la target par un `GameplayActionBinding`

### But

Permettre à deux bindings de représenter :

```text
E -> Player.Take(Target=BatteryA)

E -> Player.Take(Target=BatteryB)
```

tout en visant **la même occurrence de `Player.Take`**.

### Fichiers

- `runtime/bindings/GameplayActionBinding.cs`
- `runtime/bindings/GameplayActionBindingStore.cs`
- `runtime/runner/GameplayActionRunner.cs`
- `GameplayActionBindingTest.cs`
- `GameplayActionRunnerTest.cs`

Aujourd’hui le binding sait déjà « action réelle + source de cleanup + input + presentation context », mais n’a aucune target. 

### Modifs

`GameplayActionBinding` gagne :

```csharp
Node? Target
```

Et :

```csharp
Runner.BindAction(...)
```

accepte une target optionnelle.

Important :

```text
Source != Target
```

Exemple Battery :

```text
Source = Battery.Interactive
    -> qui doit supprimer le binding au focus loss

Target = Battery
    -> ce sur quoi l'invocation agit
```

On ne fusionne surtout pas ces deux notions.

### Evaluation locale

Quand le Runner calcule l’availability :

```text
binding.Target
    ↓
GameplayActionComponent.EvaluateAction(... target)
```

La prediction initiale doit également utiliser cette target.

### Test bonus important

Deux bindings peuvent référencer :

```text
same Component
same ActionId
different Target
different Source
```

sans créer deux Gameplay Actions.

### Commit

```text
feat(gameplay-action): carry invocation target through bindings
```

---

## Task 3 — Corriger la vraie sémantique de `AccessProviderId`

C’est une tâche petite en code mais **énorme conceptuellement**.

Aujourd’hui :

```csharp
if (component == OwnedActionComponent)
{
    return true;
}
```

Donc une action owned contourne complètement les access providers. C’est incompatible avec :

```text
Player.Take(Battery)
```

car `Take` appartient bien au Player mais son accès doit quand même être validé par Interaction. 

### Matrice finale

```text
owned + no provider
    => accessible

owned + provider
    => provider decides

external + provider
    => provider decides

external + no provider
    => inaccessible through Runner
```

### Fichiers

- `runtime/access/IGameplayActionAccessProvider.cs`
- `runtime/actions/GameplayAction.cs` si nécessaire pour rendre le provider authorable sur une action générique
- `runtime/runner/GameplayActionRunner.cs`
- `GameplayActionRunnerTest.cs`

### `GameplayActionAccessContext`

Ajouter :

```text
Target
```

Et corriger les docs actuelles qui parlent d’« externally owned action ».

Ça devient :

> domain-specific access to this player request

### Tests

Exactement les quatre cases de la matrice ci-dessus + :

- provider reçoit la target ;
- provider absent alors qu’un id est demandé => refuse ;
- provider d’une action owned est réellement appelé.

### Commit

```text
refactor(gameplay-action): make request access independent from ownership
```

---

## Task 4 — Transport réseau de la target

Là on ferme la première grosse boucle :

```text
Binding.Target
   ↓
client request
   ↓
TargetPath
   ↓
server resolve
   ↓
AccessContext.Target
   ↓
GameplayActionContext.Target
```

### Fichiers

- `GameplayActionRequestPipeline.cs`
- `GameplayActionRunner.cs`
- éventuellement structures internes de request
- `GameplayActionRunnerNetworkTest.cs`
- `GameplayActionExecutionNetworkTest.cs`

Aujourd’hui le protocole requester transporte uniquement :

```text
ComponentPath
ActionId
```

Il faut passer à :

```text
ComponentPath
ActionId
TargetPath?
```

Le serveur **résout la target lui-même**. Jamais de confiance dans un objet fourni tel quel par le client. 

### Point subtil : sustained executions

Une `GameplayActionRequestedExecution` doit conserver sa target.

Sinon :

```text
Take(BatteryA)
  ↓
execution running
  ↓
ValidateSustainedExecutions()
```

réévaluerait ensuite l’access sans savoir quelle Battery était concernée.

Je **ne changerais pas** encore la key :

```text
(Component, ActionId)
```

en :

```text
(Component, ActionId, Target)
```

car la spec diffère explicitement les multiples executions simultanées d’un même ActionId.

On conserve simplement la target comme **invocation data**, pas comme identité de l’occurrence.

### Autre point subtil : presentation

Je ferais également en sorte que la requester presentation conserve la target de l’execution.

Sinon scénario foireux :

```text
Player.Take(BatteryA) Running
focus passe sur BatteryB
presenter regarde Player.Take
=> pourrait afficher BatteryB comme "currently taking"
```

Il faut pouvoir corréler :

```text
execution Player.Take
Target = BatteryA
```

avec l’offer actuellement affichée.

Pas besoin de résoudre la future replicated-execution observability ici : juste conserver la target sur la présentation requester/predicted existante.

### Tests

- client demande action ciblée, executor serveur reçoit la target autoritative ;
- target path invalide => reject ;
- action non ciblée continue de fonctionner ;
- sustained access retrouve la même target ;
- prediction/requester presentation conserve la target ;
- aucune target client n’est utilisée comme preuve d’autorité.

### Gate Phase A

```bash
task test:gameplay
task test:network
task format
task build
```

Puis mise à jour de :

```text
docs/feature/gameplay_action/gameplay-action.md
```

avec le nouveau contrat target/access.

### Checkpoint A

À ce stade le Gameplay Action framework sait faire :

```text
Player.Attack(Enemy)
Player.Heal(Ally)
Player.Take(Battery)
```

**sans connaître Interaction du tout**.

---

# PHASE B — `InteractionOffer`

Là seulement on attaque Interaction.

---

## Task 5 — Créer le modèle authored `InteractionOffer`

### Nouveau fichier probable

```text
addons/interaction_plugin/runtime/offers/InteractionOffer.cs
```

éventuellement :

```text
InteractionOfferResolver.cs
```

### Shape V1

Je partirais directement sur la forme finale :

```text
InteractionOffer
├── Source
├── ActionId
├── BindingConfig
├── Rules
├── TargetConcurrencyGroup
├── WhenReservedBySelf
└── WhenReservedByOther
```

Même si les trois derniers ne sont utilisés qu’en Phase C.

### Source

```csharp
enum InteractionActionSource
{
    Target,
    Instigator
}
```

ou `InteractionOfferSource`, qui est peut-être encore plus clair.

### `InteractiveComponent`

Ajouter :

```text
Offers : ordered Array<InteractionOffer>
```

`ActionComponent` devient :

```text
optional
```

et n’est nécessaire que lorsqu’au moins une offer utilise :

```text
Source = Target
```

### Resolver unique

Très important : **une seule fonction doit savoir résoudre les owners**.

Conceptuellement :

```text
ResolveOffer(interactive, interactor, offer)
    -> GameplayActionComponent
    -> GameplayAction
```

Cas Target :

```text
interactive.ActionComponent
    -> ActionId
```

Cas Instigator :

```text
interactor.Runner.OwnedActionComponent
    -> ActionId
```

Je créerais probablement un petit résultat :

```text
InteractionOfferResolution
{
    Component
    Action
}
```

Pas de logique dupliquée dans focus / presenter / access provider / network.

### Tests

`InteractionConfigurationTest` :

- Target offer résolue ;
- Instigator offer résolue ;
- ActionId absent => invalid ;
- Target source sans target ActionComponent => invalid ;
- Instigator source n’exige pas de target ActionComponent ;
- ordre des offers conservé.

### Legacy

`InteractionAction` peut rester physiquement présent **temporairement**, mais le nouveau chemin ne doit pas être construit dessus.

Pas de compat permanente.

### Commit

```text
feat(interaction): introduce authored interaction offers
```

---

## Task 6 — Séparer réellement les règles Interaction des règles Gameplay Action

Aujourd’hui `InteractionRule` hérite du système Gameplay Action et l’Interactive injecte/adapte ses target rules dans les règles de l’action. C’était logique quand :

```text
target == action owner
```

mais complètement mauvais avec un `Player.Take` partagé entre 25 batteries. 

### Nouveau pipeline

```text
Offer config
    ↓
resolve endpoint
    ↓
Interactive.TargetRules
    ↓
Offer.Rules
    ↓
GameplayAction rules
```

Plus tard on ajoutera la reservation entre les deux domaines.

### `InteractionContext`

Je partirais sur quelque chose du style :

```text
Interactor
Interactive
Offer
Resolved Component
Resolved Action
```

Une InteractionRule connaît donc **la vraie target et la vraie action**, sans prétendre être une GameplayActionRule.

### `InteractionRule`

Finalement :

```text
InteractionRule : Resource
```

et :

```text
Evaluate(in InteractionContext)
```

Pas :

```text
InteractionRule : GameplayActionRule
```

### NodePaths

Très important :

```text
Interaction rule NodePath resolution
    relative to Interactive target
```

et jamais relativement au Player qui possède `Take`.

### Tests

- TargetRule Hidden ;
- OfferRule Blocked ;
- GameplayActionRule Blocked ;
- les trois sont indépendants ;
- focus BatteryA puis BatteryB ne modifie jamais `Player.Take.Rules`;
- Stateful interaction rule continue à résoudre sa stateful component sur la target.

### Commit

```text
refactor(interaction): separate offer rules from owned action rules
```

---

## Task 7 — Basculer focus et bindings sur les Offers

Aujourd’hui `InteractionInteractor` parcourt les `InteractionAction` du target ActionComponent et bind directement celles-ci. 

On remplace :

```text
interactive.Actions
```

par :

```text
interactive.Offers
```

### Flow local final

```text
focus target
    ↓
foreach offer
    ↓
resolve actual action owner
    ↓
evaluate offer
    ↓
Runner.BindAction(
    resolvedComponent,
    actionId,
    source = interactive,
    target = interactive target,
    config = offer.BindingConfig
)
```

### Première preuve importante

Faire fonctionner tout de suite un cas **Source=Target** équivalent à l’ancien modèle.

Par exemple :

```text
Door.Open
```

ou une fixture simple.

Ça permet de vérifier qu’on n’a pas cassé tout Interaction avant même d’introduire Take.

### Tests

- target-owned offer crée le binding attendu ;
- focus lost supprime le binding ;
- ordinary `GameplayAction` peut être exposée : pas besoin d’`InteractionAction`;
- offer Hidden ne crée/presente pas correctement ;
- ordre des offers respecté.

### Commit

```text
refactor(interaction): project focused offers into action bindings
```

---

## Task 8 — Refaire l’Interaction access provider autoritatif

C’est **la tâche sécurité** du chantier.

Le client pourra demander :

```text
Player.Take
Target = Battery
```

mais le serveur doit reconstruire :

```text
Target resolves
    ↓
Target contains/is a live Interactive
    ↓
Interactive really offers Instigator.take
    ↓
Offer resolves server-side
    ↓
resolved endpoint == requested endpoint
    ↓
spatial access valid
    ↓
target rules valid
    ↓
offer rules valid
```

Puis seulement Gameplay Action valide ses propres rules/concurrency.

### Forgery à tester

```text
Player.Take(ObjectAcrossMap)
Player.Take(Door)
Player.Force(DoorThatOnlyOffersOpen)
Player.Take(BatteryUsingSomeOtherActionComponent)
```

Tous rejetés.

### Provider ID

La constante `"interaction"` ne doit plus appartenir à `InteractionAction`.

Elle appartient au **domaine Interaction access**.

Exemple :

```text
InteractionInteractor.InteractionAccessProviderId
```

ou classe dédiée.

### Sustained access

Même reconstruction avec la target d’origine conservée par Task 4.

### Tests

Principalement :

- `InteractionNetworkTest.cs`
- `InteractionNetworkBehaviorTest.cs`
- éventuellement `InteractionAckTest.cs`

### Commit

```text
feat(interaction): validate targeted offers authoritatively
```

---

## Task 9 — Migrer le Presenter vers les Offers

### But

Permettre :

```text
Door UI

[E] Open      -> Door.GameplayActions.Open
[F] Force     -> Player.GameplayActions.Force
```

dans le même panel.

### Le Presenter prend de l’offer

```text
order
binding
target availability
busy target relation
```

### Il prend de l’action réelle

```text
Definition
Label
Description
Execution presentation
Action rules state
```

### Invariant essentiel

```text
UI container owner != execution owner
```

Battery peut afficher :

```text
Taking...
```

alors que l’execution existe sur :

```text
Player.GameplayActions.Take
```

### Test indispensable

```text
Take(BatteryA) running
focus BatteryB
```

ne doit **jamais** faire apparaître BatteryB comme la target de l’execution.

C’est pour ça qu’on a conservé la target dans la requester presentation en Phase A.

### Tests

- mixed action owners affichés ensemble ;
- ordre authored ;
- labels venant des GameplayActionDefinition réelles ;
- execution lookup sur le vrai component ;
- aucune confusion entre deux targets d’une même action.

### Gate Phase B

```bash
task test:interaction
task test:gameplay
task test:network
task format
task build
```

### Checkpoint B

Ici, avant même les reservations, le framework sait déjà exprimer proprement :

```text
Door.Open()
Player.Force(Door)
Player.Take(Battery)
```

via **le même Interaction adapter**.

---

# PHASE C — Target reservation

Et là on attaque le vrai morceau velux MDR.

Il y a un petit point d’implémentation que la spec laisse volontairement abstrait et qu’il faudra résoudre proprement.

Le Runner fait actuellement grosso modo :

```text
CanAccess()
    ↓
ExecuteRequestedAction()
```

Or la target claim doit être acquise :

```text
APRÈS validation
AVANT executor.Execute()
```

On ne peut donc pas simplement faire la claim dans `CanRequest()`, parce que ce check doit rester pur pour l’availability locale.

---

## Task 10 — Introduire un petit lifecycle de request reservation

Je **ne ferais pas un generic cross-host reservation framework**. La spec le diffère explicitement.

Par contre il faut un mécanisme minimal permettant à un access provider de dire :

> ce request est validé ; avant de démarrer l’action, j’ai une ressource temporaire à acquérir et à relâcher avec cette execution.

### Modèle recommandé

Un petit lease/token interne au request pipeline.

Conceptuellement :

```text
Access provider pure check
    CanRequest(context)

Authority start
    TryAcquireRequestReservation(context)
        -> optional lease

lease acquired
    ↓
ExecuteRequestedAction()
```

Puis :

```text
Rejected / Failed sync
    -> Release()

Completed sync
    -> Release()

Running
    -> store lease alongside GameplayActionRequestedExecution

Completed/Cancelled/Failed later
    -> Release()
```

Le lease est **idempotent**.

### Pourquoi dans le Runner ?

Parce que le Runner possède déjà :

```text
authoritative requester start
requested execution lifetime
terminal notifications
cancellation
disconnect cleanup
```

Interaction ne devient donc pas un second execution engine.

Elle ne fournit qu’une reservation attachée à son access domain.

### Test hyper important

Un executor test vérifie que :

```text
target is already claimed
```

au moment où `Execute()` est appelé.

Et tests rollback :

- executor Reject => released ;
- executor Failed sync => released ;
- Completed sync => released ;
- Running => kept ;
- terminal => released.

### Commit

```text
feat(gameplay-action): support request access reservation leases
```

C’est probablement **la seule modification générique supplémentaire** introduite spécifiquement par la target concurrency.

---

## Task 11 — Implémenter `TargetConcurrencyGroup`

### `InteractiveComponent`

Il possède maintenant son état de claim.

Conceptuellement :

```text
Dictionary<StringName, InteractionTargetReservation>
```

Reservation :

```text
Group
Action endpoint
Execution identity
Instigator
Requester
```

Pour l’identité de l’execution, ne pas utiliser seulement :

```text
ExecutionId
```

mais bien quelque chose comme :

```text
(Component, ExecutionId)
```

car l’id appartient au domaine d’un GameplayActionComponent.

### Acquisition

Offer :

```text
TargetConcurrencyGroup = "door_operation"
```

ou :

```text
"take"
```

vide :

```text
no target claim
```

### Relation

La claim sait répondre :

```text
Unclaimed
ReservedBySelf
ReservedByOther
```

### Availability

Pipeline devient réellement :

```text
offer config
resolve endpoint
spatial access
TargetRules
OfferRules
TargetReservation
GameplayAction rules
Action owner concurrency
```

### Mixed ownership

Le test critique :

```text
Door.Open
owner = Door

Player.Force(Door)
owner = Player

both TargetConcurrencyGroup = door_operation
```

et :

```text
Open blocks Force
Force blocks Open
```

L’action owner concurrency reste présente **en plus**.

### Tests

`InteractionConcurrencyTest.cs` :

- Player1.Take Battery bloque Player2.Take Battery ;
- une autre target reste accessible ;
- deux groups différents peuvent coexister ;
- target-owned et instigator-owned partagent le même group ;
- host concurrency reste indépendante.

### Commit

```text
feat(interaction): add target-side interaction reservations
```

---

## Task 12 — Busy self/other + réplication de la claim

Maintenant on replace proprement :

```text
WhenExecutingBySelf
WhenExecutingByOther
```

par :

```text
WhenReservedBySelf
WhenReservedByOther
```

sur l’Offer.

`InteractionAction` porte aujourd’hui encore ces responsabilités, ce qui confirme qu’elles migrent naturellement ici. 

### Presentation outcomes

```text
self  Hidden / Blocked
other Hidden / Blocked
```

### Réseau

Pas besoin de generic replicated execution observability.

Juste un petit snapshot transient par Interactive :

```text
claimed?
group
relation identity
```

Réutiliser autant que possible le transport/snapshot déjà employé par Interaction, plutôt que d’ajouter un nouveau composant authored partout.

### Late join

Un nouvel arrivant doit directement voir :

```text
Battery currently claimed
```

et non :

```text
Battery free
... attendre prochain event ...
```

### Tests

- requester voit `self`;
- autre peer voit `other`;
- claim start répliquée ;
- release répliquée ;
- late join reçoit claim actuelle ;
- target destruction ne laisse aucun état zombie ;
- failed/cancelled execution libère partout.

Suites :

```text
InteractionNetworkStateTest
InteractionNetworkLateJoinTest
InteractionNetworkBehaviorTest
```

### Gate Phase C

```bash
task test:interaction
task test:network
task test:runtime
task format
task build
```

### Checkpoint C

Le framework est alors architecturalement complet.

Tout ce qui suit est **proof by real gameplay + cleanup**.

---

# Dernier point à vérifier une fois les tests fait par le maintainer : Migrer le reste vers InteractionOffer, Supprimer `InteractionAction` et fermer le chantier

Quand plus aucune scène/test n’en dépend :

### Delete

Probablement :

```text
InteractionAction.cs
InteractionTargetRulesAdapter.cs
```

`InteractionAction` porte aujourd’hui précisément :

- Interaction provider ;
- default binding ;
- busy self/other ;
- Interactive ;
- target-rule adapter.

Tout a désormais une maison plus naturelle, donc je ne lui garderais pas une « utilité nostalgique ». 

### `InteractionActionExecutor`

À réévaluer à ce moment-là.

Deux résultats acceptables :

**A.** Il ne fait plus rien d’utile :

```text
delete
```

**B.** Il reste un convenience executor réellement pratique :

```text
GameplayActionContext
    Instigator
    Target
        ↓
Interaction execution convenience
```

Dans ce cas il ne doit plus dépendre d’un `InteractionAction`.

Ce que je ne veux surtout pas :

```text
InteractionAction survives because InteractionActionExecutor expects it
```

ça serait garder la vieille abstraction juste pour nourrir la vieille abstraction 😭.

---

# Docs finales

Mettre les décisions durables dans :

```text
docs/feature/gameplay_action/gameplay-action.md
docs/feature/interaction/interaction.md
```

Et une fois le chantier implémenté :

```text
delete docs/feature/interaction/planned/owned-vs-targeted-actions.md
```

puisque le repo demande explicitement de ne pas garder les plans implémentés comme archives.

Les docs carry animation existantes devront également être relues : plusieurs hypothèses auront été invalidées par le nouveau ownership.

---

# Validation finale

Je mapperais explicitement les 16 comportements de la spec :

| Spec | Prouvé dans |
|---|---|
| Target-owned Door.Open | T7 / T15 |
| Player.Take(Battery) | T13 |
| One Take, many batteries | T13 |
| Target dies, execution survives | T13 |
| Player owner concurrency | T13 |
| Two players / one Battery | T11 + T13 |
| Mixed Open + Force | T15 |
| Open ↔ Force target blocking | T11 + T15 |
| Target rules + action rules | T6 |
| Forged request rejected | T8 |
| Cancel/failure releases claim | T10–11 |
| Sync terminal no leak | T10 |
| Target removal no leak | T11 |
| Mixed presentation | T9 / T15 |
| Self/other relation | T12 |
| Network + late join claim | T12 |

Puis :

```bash
task test:gameplay
task test:interaction
task test:game
task test:runtime
task test:network
task format
task build
task ci
```

---

# Les 5 checkpoints que je ferais

Ça donne une histoire de commits/PR super lisible :

```text
CHECKPOINT A
Targeted Gameplay Actions
T1 → T4

CHECKPOINT B
Interaction Offers
T5 → T9

CHECKPOINT C
Target Reservations
T10 → T12

CHECKPOINT D
Real Carry
T13 → T14

CHECKPOINT E
Mixed Ownership + Cleanup
T15 → T17
```

Et surtout chaque checkpoint raconte quelque chose.

**A :** « Gameplay Action sait exécuter une capability contre une target. »

**B :** « Interaction n’est plus propriétaire des actions qu’elle expose. »

**C :** « Des actions appartenant à des actors différents peuvent coordonner l’accès à la même target. »

**D :** « Take/Drop ne trichent plus sur ownership/grants. »

**E :** « L’ancien modèle a disparu et Door prouve que ce n’était pas un refactor spécifique au carry. »

---

## Deux points où je serais particulièrement parano

Le premier, c’est la **claim entre access validation et `Executor.Execute()`**. C’est la seule frontière où je vois un vrai petit choix d’API supplémentaire nécessaire. Le lease de request me paraît de très loin la solution la plus propre : pure availability d’un côté, acquisition autoritative juste avant start de l’autre, Runner propriétaire du lifecycle.

Le second, c’est la **corrélation execution ↔ target côté présentation**. Dès qu’un `Player.Take` unique peut successivement viser plusieurs objets, il ne suffit plus de dire « Take est Running ». Il faut savoir « Take(BatteryA) est Running », sinon on crée exactement le genre de bug UI débile qui n’apparaît qu’après le gros refactor.

À part ces deux points, je trouve que le chantier est maintenant étonnamment déterministe. C’est massif, mais quasiment toutes les tâches ont une **frontière naturelle + un test naturel + un état green naturel**. Je partirais exactement dans cet ordre.