# Gameplay Action System V1 Review

Verdict global : oui, c’est très proche de ce que j’imaginais — environ 85–90 %. Ce n’est pas un renommage d’Interaction : le système générique existe réellement, les responsabilités importantes ont été extraites, et vous n’avez pas recréé GAS en douce.

Je l’appellerais volontiers une V1, mais avec quelques corrections avant de figer durablement l’API. Le principal manque est que l’entrée par input du jeu reste encore centrée sur Interaction.

## Points importants à corriger

### 1. La boucle d’input réelle n’est pas encore générique

Le `GameplayActionRunner` sait résoudre une action possédée, une interaction externe, un hold, etc. Mais le Character énumère uniquement `InteractionInteractor.GetRelevantInputs()` puis appelle `TryStartInteractionInput()`.

Or `TryStartInteractionInput()` refuse si aucun interactive n’est focus :

- :chatgpt-content-reference{index="0"}
- :chatgpt-content-reference{index="1"}

Donc une potion ou `DropBattery` peut être exprimée et exécutée en appelant directement le Runner, mais elle ne passera pas encore naturellement par la boucle d’input du jeu.

Je déplacerais `GetRelevantInputs()` vers `GameplayActionRunner` :

- tous les bindings non automatiques ;
- tous les inputs consommés ou soutenus ;
- le Character appelle directement `Runner.TryStartActionInput/TryEndActionInput` ;
- Interaction ne fait plus qu’ajouter/retirer ses bindings.

C’est le dernier endroit où Interaction reste encore accidentellement la façade du système générique.

### 2. Les exceptions des callbacks terminaux cassent le lifecycle

L’exception de `Execute()` est correctement transformée en `Failed`. En revanche, les callbacks :

- `OnExecutionCompleted`
- `OnExecutionCancelled`
- `OnExecutionFailed`

ne sont pas protégés.

Dans :chatgpt-content-reference{index="2"}, un callback terminal qui throw arrive après la libération de la réservation, mais avant :

- la finalisation d’une action retirée ;
- le signal terminal ;
- l’ACK au requester.

On peut donc laisser une action dans `_retiringActions`, ne jamais prévenir le client et conserver son ID inutilisable.

Je sécuriserais tous ces callbacks avec une politique unique, idéalement en garantissant par `finally` la finalisation et la notification. C’est le point de robustesse le plus sérieux.

### 3. Une exécution world-owned conserve son requester après sa destruction

Le Runner laisse correctement survivre les exécutions dont `RequiresRequesterPresence == false`. Mais le composant conserve quand même le `GameplayActionRunner` dans `ActiveExecution`.

À la terminaison, il tentera toujours :

```csharp
runner.NotifyExecutionCompleted(...)
```

même si ce Runner a quitté l’arbre ou a été libéré.

Cela peut arriver pour une action world-owned exécutée sur une machine/porte après la disparition du personnage. Le test actuel appelle `_ExitTree()` mais ne libère pas véritablement le Runner puis ne termine pas l’action après sa disparition.

Il faudrait détacher le requester des exécutions world-owned lors du teardown, ou au minimum vérifier `GodotObject.IsInstanceValid(runner)` avant toute notification. Un test devrait réellement `QueueFree()` le Runner, puis terminer l’exécution.

### 4. Le snapshot du geste ne capture pas réellement l’availability

Le plan capture les IDs au press, ce qui protège correctement contre les nouveaux bindings. Mais au moment du hold/release, :chatgpt-content-reference{index="3"} relit l’availability actuelle dans le store.

Donc une invalidation pendant le hold peut :

- rendre éligible une action qui ne l’était pas au press ;
- masquer l’action capturée ;
- changer le winner de la pression déjà engagée.

C’est contraire à notre décision « la pression capture son plan, le serveur revalide ensuite ». Je capturerais le candidat complet — binding et availability — pas seulement son ID. La disparition physique du binding peut toujours l’annuler.

### 5. Interaction s’approprie l’`Instigator` du Runner

`InteractionInteractor.SyncRunnerConfiguration()` force continuellement :

```csharp
Runner.Instigator = this;
```

et la scène Character pointe également l’instigator sur `InteractionInteractor`.

C’est pratique pour adapter `InteractionContext`, mais lorsqu’on ajoutera `Heal` ou `DropBattery` sur le même Runner, leur instigator sera l’Interactor et non le Character.

Ce n’est pas bloquant aujourd’hui, mais c’est précisément le genre de frontière qui deviendra étrange dès ton test « de nulle part ». Je garderais l’instigator générique sur le Character. L’adapter Interaction peut retrouver son interactor depuis le requester/access provider déjà enregistré, sans ajouter une nouvelle abstraction publique façon GAS.

### 6. `CancelOnInputReleased` encode également une politique de présence

Dans :chatgpt-content-reference{index="4"} :

```csharp
RequiresInteractorPresence
|| InteractionAction?.InteractionDefinition?.CancelOnInputReleased == true
```

C’est volontaire et testé, mais la propriété porte désormais deux sémantiques :

- annuler lorsque l’input est relâché ;
- annuler lorsque l’accès spatial/requester disparaît.

Notre modèle générique distinguait précisément ces deux lifecycles. Soit il faut retirer ce `||`, soit renommer la propriété Interaction en quelque chose comme `RequiresContinuousEngagement` qui assume explicitement les deux comportements.

## Écarts plus petits

- `Press` et `Release` sur le même input ne respectent pas totalement leurs edges. La présence d’un binding `Release` reporte aussi le `Press` jusqu’au release, puis les deux sont arbitrés ensemble. Seul un `Hold` devrait normalement différer un `Press`, ou alors il faut documenter cette nouvelle sémantique et la tester.

- `InteractionAction.PrepareForInteractive()` retire et réinsère le rules adapter à chaque évaluation. Or `EvaluateAvailability()` l’appelle potentiellement plusieurs fois par action et par frame. Cela transforme une lecture annoncée pure en mutation régulière du tableau de rules. L’installation de l’adapter devrait être idempotente et limitée au setup.

- Un binding peut survivre à la suppression de son action avec une availability cachée devenue obsolète. L’exécution est finalement refusée car `ResolveAction()` retourne `null`, mais une UI lisant `GetBindings()` et `GetBindingAvailability()` peut encore montrer l’ancien binding. Pour Battery, l’intégration devra impérativement faire `Unbind`, puis `RemoveAction`, ou le store devrait automatiquement traiter une action disparue comme `Hidden`.

- `TryGetGestureProgress()` ne retourne qu’un geste arbitraire alors que le Runner autorise plusieurs inputs simultanément. C’est acceptable en V1 pour le presenter Interaction, mais à documenter avant un HUD générique.

- Les adapters Stateful placés dans `gameplay_action_plugin/integration/stateful` font dépendre physiquement l’add-on générique de Stateful, alors que sa documentation affirme le contraire. À terme, je mettrais ces classes dans un petit add-on d’intégration séparé.

## Ce qui est franchement réussi

- `GameplayActionComponent` est bien le host concret : aucun `ActionHost` abstrait ou node supplémentaire inutile.
- Ownership et binding sont clairement séparés. Une porte reste propriétaire de `Open`; le joueur reçoit seulement un binding local.
- Le chemin programmatique bypass correctement access/distance/binding tout en conservant rules et concurrence.
- Le serveur résout ses propres `componentPath + ActionId`, valide le sender, l’access provider, les rules et les réservations. Le client ne transmet aucune prétendue permission.
- Le retirement d’une action active est très bien pensé : disparition de la résolution immédiate, node conservé jusqu’au terminal, ID réservé.
- `Allowed / Blocked / Hidden`, la priorité absolue authored et le tie-break déterministe sont au bon niveau.
- `Press / Hold / Release / Automatic` et `None / Pressed` sont assez expressifs sans introduire de state machine générique prématurée.
- La présentation générique reste minimale : label/description, contexte opaque du binding et présentation d’exécution. Interaction conserve ses widgets et sa projection spatiale.
- La sortie du `GameplayActionExecutionPresentationStore` depuis le Component était la bonne correction : le host reste dense, mais il n’est plus un monolithe réseau/UI.
- Interaction utilise maintenant réellement les primitives génériques. Je n’ai trouvé aucun second pipeline d’exécution de production encore actif.
- Les limites V2 sont bonnes : pas de tags, cooldowns, attributes, payloads arbitraires ou réplication générique des grants pour l’instant.

## Ce qui peut être supprimé ou simplifié

Quelques restes donnent l’impression des itérations du spike :

- `InteractionActionBindingConfig` est une sous-classe vide utilisée seulement pour instancier la config générique.
- `InteractiveComponent.EvaluateRules()` est mort.
- Le booléen `sustained` de `GameplayActionRunner.CanAccess()` n’est plus utilisé.
- `GameplayActionRunnerIntegration.cs` contient uniquement l’exposition des consumed inputs et disparaîtrait naturellement avec un vrai `Runner.GetRelevantInputs()`.
- `TryGetFirstActiveExecution` et `ActiveAction/ActiveInteractor` réintroduisent une vision « une seule interaction active », alors que les concurrency groups autorisent plusieurs exécutions. Ces helpers internes sont désormais ambigus.
- `GameplayActionContext.Requester` pourrait être typé `GameplayActionRunner?`; les seules valeurs réellement stockées sont un Runner ou `null`.
- `docs/feature/gameplay_action/review.md` est un reliquat minuscule.
- La spec contient encore des contradictions historiques : elle mentionne un `InvocationKind` et un requester fourni programmatiquement alors que ces deux choix ont ensuite été volontairement retirés.

Les diagnostics éditeur méritent aussi une petite passe : le validator générique n’annonce pas réellement l’invariant « action authored = enfant direct », alors que la documentation affirme qu’il le fait. Inversement, le validator Interaction peut signaler qu’une action n’a pas d’`Interactive` alors que cette référence n’est installée qu’au runtime.

## Tests à ajouter avant de figer l’API

Je mettrais six régressions très ciblées :

1. action owned non-Interaction déclenchée par la même boucle d’input que les portes ;
2. availability modifiée pendant un hold sans réécrire le plan capturé ;
3. `Press` et `Release` partageant un input ;
4. action supprimée alors qu’un binding la référence encore ;
5. callback terminal qui throw sans perdre retirement, signal ou ACK ;
6. Runner réellement libéré pendant une exécution world-owned, puis terminaison de celle-ci.

Le dépôt contient actuellement 285 déclarations `[TestCase]`, dont 52 dans le nouvel add-on et 202 dans Interaction. La couverture présente est donc très sérieuse, notamment sur le réseau réel, la réplication, le late join, les ACK et la migration fonctionnelle.

Je n’ai cependant pas pu exécuter `dotnet build` ou `dotnet test` : cet environnement n’a ni `dotnet` ni `godot`. Le checkout est propre sur `main` au commit `5d8acee`. `git diff --check` relève seulement une ligne vide terminale dans `.csharpierignore`.

En résumé : la direction et 90 % des frontières sont excellentes. Je corrigerais impérativement l’entrée d’input générique et les deux trous de lifecycle autour des callbacks/requesters, puis le snapshot de geste. Après ça, oui : V1 propre, utile et suffisamment petite pour évoluer à partir de vrais besoins plutôt que de devenir GAS par anticipation.