# Interaction V3 — hardening et catalogue de cas d’usage

Ce document ne redessine pas le framework. Interaction V2 et Stateful couvrent déjà la démo : V3 doit sécuriser le réseau, réduire les coûts inutiles et préserver un chemin d’authoring aussi direct que l’ancien plugin Unreal.

## Frontières validées

| Pièce | Responsabilité |
| --- | --- |
| `InteractionAction` et ses rules | Décrire ce que le joueur peut demander maintenant |
| `InteractionActionExecutor` | Exécuter une commande autoritaire et posséder son éventuel lifetime |
| `StatefulComponent` | Conserver et répliquer ce qui est vrai dans le monde |
| Script métier | Posséder la simulation spécifique : générateur, corde, inventaire, dialogue, etc. |
| FlowGraph / Quest / Facts | Observer les états et événements métier pour faire progresser le jeu |
| Présentation | Réagir localement aux états répliqués sans commander le gameplay |

Décisions acquises :

- Les exécutions restent transitoires et server-only. Un état métier répliqué comme `activating` est le reflet durable de la réservation lorsqu’elle importe aux autres joueurs.
- Un hold annulé au relâchement exige aussi que le joueur reste présent. Le déplacement libre commence après l’interaction, comme pour une corde devenue objet transporté.
- Dialogue et Shop possèdent leur session réseau. Interaction leur donne un `ExecutionId`, mais ne devient pas un framework de fenêtres ou de conversations.
- Une quête observe de préférence `generator = repaired` plutôt que la seule action `repair`. Le même résultat peut alors provenir d’une interaction, d’un script ou d’une restauration de sauvegarde.
- Des `CompletionEffects` restent un spike possible si de vraies conséquences liées à l’action se répètent. Ils ne sont pas planifiés avant le système Facts.

## Le chemin minimal : commande serveur, feedback sur tous les clients

L’ancien plugin Unreal séparait déjà deux responsabilités :

- [`OnStartInteractionInput`](https://github.com/pjdevs/QuestWorld/blob/main/Plugins/InteractionPlugin/Source/InteractionPlugin/Public/IPInteractionHandler.h) exécutait la commande sur l’autorité ;
- `SetState` répliquait `State`, puis [`OnRep_State`](https://github.com/pjdevs/QuestWorld/blob/main/Plugins/InteractionPlugin/Source/InteractionPlugin/Private/IPStatefulComponent.cpp) appelait `OnStateChangedClient` sur les clients ;
- le listen host appelait explicitement le callback serveur et le callback client, tandis qu’un dedicated server ne jouait aucun feedback cosmétique.

Le modèle Godot actuel est le même, décomposé en pièces génériques :

| Ancien Unreal | Godot actuel |
| --- | --- |
| `BP_Interact` / `OnStartInteractionInput` | `InteractionActionExecutor.Execute` sur l’autorité |
| `State`, `ReplicatedUsing=OnRep_State` | `StatefulComponent` + `MultiplayerSynchronizer` sur `.:ReplicatedState` |
| `BP_DoFeedback` / `OnStateChangedClient` | abonnement à `StateChangedPresentation` |
| logique serveur sur changement d’état | abonnement à `StateChangedAuthority` |
| logique identique sur tous les peers | abonnement à `StateChanged` |

Un coffre basique ne nécessite donc aucun executor spécifique :

```text
Chest
├── StatefulComponent                  closed / open
│   └── MultiplayerSynchronizer        .:ReplicatedState
├── Interactive
│   └── OpenAction
│       └── SetStateInteractionExecutor → open
└── ChestFeedback                      StateChangedPresentation
```

`ChestFeedback` applique une première fois la pose correspondant à `Stateful.State` dans `_Ready`, puis écoute `StateChangedPresentation(oldState, newState)` pour jouer les transitions. Cela couvre aussi un late join : la pose stable vient de l’état courant, tandis que les sons et autres one-shots ne sont joués que sur une vraie transition reçue.

Le chemin est vérifié dans le code :

- `SetState` ne mute que sur l’autorité ;
- le setter privé `ReplicatedState` applique la valeur reçue sur les clients ;
- `StateChangedPresentation` est émis sur les clients, le listen host et le jeu offline, jamais sur un dedicated server ;
- `StateChangedAuthority` est émis sur le serveur ;
- `StateChanged` est émis sur chaque peer qui applique la valeur.

Il ne faut pas faire jouer le même feedback cosmétique par l’executor et par le state : le listen host le jouerait deux fois et les clients distants ne verraient que la seconde voie. L’executor commande ; la présentation observe.

Une animation qui déplace aussi une collision n’est pas purement cosmétique. Elle observe `StateChanged` afin de tourner également sur le dedicated server, mais seul le serveur décide de l’état final. `LeverWall` suit déjà ce modèle.

Ce qui manque encore est un test avec un vrai serveur et deux clients prouvant que chaque transition répliquée déclenche exactement une fois le feedback de chaque peer. Le test unitaire actuel appelle directement le setter répliqué et la scène vérifie seulement la configuration du `MultiplayerSynchronizer`.

## Callbacks Interaction : contrat actuel et manque client

| Notification | Où aujourd’hui | Sens |
| --- | --- | --- |
| `InteractionRequested` | Client propriétaire, localement | Intention envoyée, pas acceptation serveur |
| `InteractionRejected` | Client propriétaire | Prévalidation locale ou rejet renvoyé par le serveur |
| `InteractionActionStarted` | Autorité seulement | L’executor a accepté la commande |
| `InteractionActionCompleted` | Autorité seulement | L’action acceptée a terminé |
| `InteractionActionCancelled` | Autorité seulement | L’action acceptée a été interrompue |
| `InteractionActionRejected` | Autorité seulement | Le target a refusé avant exécution |

Un signal Godot est local : les quatre notifications de l’`InteractiveComponent` ne traversent pas le réseau. Le client demandeur ne sait donc pas génériquement que le serveur a accepté ou terminé son action. Il ne possède qu’une prédiction, un éventuel state répliqué et un RPC de rejet.

V3 doit fournir au client propriétaire un lifecycle autoritaire corrélé à sa requête :

```text
Requested(requestId, target, action)
Started(requestId, executionId, authoritativeDuration)
Completed(executionId)
Cancelled(executionId, reason)
Failed(requestId/executionId, reason)
Rejected(requestId, reason)
```

Ce protocole sert à nettoyer la prédiction, suivre plusieurs groupes simultanés et informer une UI locale. Il ne doit pas diffuser les callbacks à tous les clients : les autres joueurs observent le résultat monde par Stateful ou par le système métier. Cela évite des événements transitoires non late-join-safe et la divulgation d’actions cachées.

### Dialogue et vendeur

Le système downstream reste la source de vérité de son UI :

```text
Client demande l’interaction
→ serveur accepte
→ executor ouvre une session Dialog/Shop pour ce peer
→ le système envoie SessionStarted au client
→ le client ouvre son UI

Client choisit ou ferme
→ requête au serveur du système Dialog/Shop
→ validation métier
→ CompleteExecution / CancelExecution
→ SessionCompleted / SessionCancelled au client
```

Ouvrir l’UI sur `InteractionRequested` serait optimiste : le serveur peut encore refuser. Les futurs callbacks Interaction pourront fournir un acknowledgement générique, mais ne remplacent pas les données et résultats propres au dialogue ou au commerce.

## Trous V3 confirmés

### P0 — protocole et tests réseau

1. **Pas d’acknowledgement autoritaire côté client.** Le serveur ne renvoie que les rejets. Il manque la corrélation request/execution, la durée réellement choisie et les fins `Completed/Cancelled/Failed`.
2. **Le rejet ne réconcilie pas la prédiction.** `ClientInteractionRejected` émet un signal mais ne nettoie pas `_prediction`, `_sustainedInputs` ni la requête automatique mémorisée.
3. **`Failed` devient `Rejected` sur le client.** Le serveur notifie `Started` puis `Cancelled`, mais `TryStartInteractionAuthoritatively` retourne `false` et envoie ensuite le RPC de rejet.
4. **Aucun vrai test Interaction à plusieurs peers.** Les tests doivent lancer un serveur, un client A et un client B avec `LongActionExample`.

Scénarios réseau minimaux :

- A démarre : serveur, A et B voient `activating`; B présente l’action comme busy.
- B demande après réplication : aucune seconde exécution ne démarre.
- A et B demandent avant réplication : une seule exécution démarre et le perdant nettoie immédiatement sa prédiction.
- Completion : chaque peer voit `activated` et joue une seule fois son feedback.
- Release, sortie de zone ou déconnexion : chaque peer revient à `idle`, puis B peut commencer.
- Late join pendant `activating` et après `activated` : état et pose visuelle corrects sans rejouer les one-shots passés.
- Dialogue : l’UI ne s’ouvre qu’après `SessionStarted`, puis sa fermeture termine ou annule la bonne exécution sur le serveur.

La liste `_activeExecutions` n’a pas besoin d’être répliquée pour ces scénarios : `activating` en est déjà la projection métier. Une action longue sans aucun état ou système métier répliqué conserve volontairement une présentation distante optimiste jusqu’au refus serveur.

### P1 — coût des détecteurs et de la présentation

1. Le pipeline commun déduplique puis réconcilie encore avec des `List.Contains`, soit un pire cas quadratique dans le nombre de candidats.
2. `ProximityInteractionDetector` parcourt tout le registre et appelle le LOS avant d’éliminer les objets hors de la distance d’indication. Avec le masque par défaut, il entretient donc un raycast par objet récemment demandé, même lointain.
3. `AimInteractionDetector` force un shapecast sur chaque copie distante, puis résout chaque hit avec `FindByArea`, qui parcourt actuellement tous les interactives. Ses paramètres de cast ne sont copiés qu’au `_Ready`.
4. Le presenter pull chaque frame mais plusieurs de ses callbacks événementiels appellent aussi `Refresh()` immédiatement. Ils devraient seulement marquer la présentation dirty ou être supprimés si le pull reste la stratégie retenue.

`AreaInteractionDetector` reste le détecteur de production : ses overlaps sont event-driven et stockés dans des `HashSet`. Avec le LOS actif, le vrai coût principal est un raycast par cible récemment évaluée et par frame physique ; les appels `Detect` lisent ensuite le cache. Le polish attendu porte sur les structures du pipeline, les diagnostics de layers/masks/areas et un profil dense réel, pas sur un nouveau modèle de détection.

### P2 — cohérence d’API et packaging

1. `InteractionExecutionRunning(0)` signifie « reprendre `ExpectedDuration` », tandis que `ExpectedDuration == 0` signifie « aucune deadline ». Des factories `RunningUntilCompleted()` et `RunningFor(seconds)` rendraient le contrat explicite.
2. Des commentaires référencent encore une durée portée par l’action alors qu’elle appartient à l’executor.
3. Le validator interdit deux actions partageant input et seuil alors que le resolver sait les départager par disponibilité, priorité puis identifiant.
4. Le dossier `interaction_plugin` compile directement son bridge Stateful et dépend d’un shim union situé dans le projet. Si l’addon doit être distribué seul, ces dépendances doivent rejoindre leurs packages ou une intégration séparée.

`SetState` retournant un booléen pauvre, le schéma permissif sur les valeurs reçues du serveur et les callbacks de fin sans résultat restent des angles à surveiller, mais pas des travaux V3 sans consommateur concret démontrant un échec.

## Spikes différés

### Completion Effects

Le chemin recommandé est d’abord :

```text
Interaction → mutation métier / Stateful → FlowGraph, Quest et Facts observent
```

Il évite de coupler chaque moyen d’obtenir un résultat à toutes ses conséquences. Si plusieurs actions doivent malgré tout composer des conséquences indépendantes spécifiquement liées à leur completion, un futur spike pourra tester `un executor + N CompletionEffects` autoritaires, synchrones et non transactionnels. Rien n’est planifié avant l’existence de Facts et de duplications réelles.

### Proximity et Aim

Ils restent des prototypes de feeling. Les optimiser ou stabiliser n’est utile que si Area ne convient pas à un gameplay réel.

### Contribution coopérative

Un `GeneratorComponent` peut posséder une progression autoritaire répliquée et un ensemble de contributeurs :

```text
Executor commence  → Generator.AddContributor(token)
Executor annulé    → Generator.RemoveContributor(token)
Generator._Process → progresse selon le nombre de contributeurs, régresse à zéro contributeur
Generator terminé  → chaque executor complète son ExecutionId
```

L’executor ne devrait pas incrémenter directement la progression dans son propre `_Process` : le générateur peut calculer une seule fois, gérer la limite, la régression et la réplication sans dépendre de l’ordre des executors. Avec plusieurs `InteractiveComponent`, le token doit inclure l’executor ou l’interactive, car les `ExecutionId` ne sont uniques que dans leur target.

Le gameplay fonctionne aujourd’hui si le générateur expose plusieurs points physiques de réparation, chacun avec son interactive et son executor, tous branchés au même composant métier. Un seul interactive/action ne peut pas accepter deux contributeurs simultanés : son `ConcurrencyGroup` est exclusif. Une future demande pourrait introduire `MaxConcurrentExecutions` ou une clé de concurrence dynamique.

La progression partagée ne correspond pas non plus à la prédiction locale à durée fixe du presenter. Elle doit être présentée depuis la propriété répliquée du générateur ; un `ProgressProvider` ne devient utile que si cette valeur doit apparaître dans le prompt générique.

## Catalogue d’interactives

| Cas | Décomposition | Support actuel |
| --- | --- | --- |
| Porte ou coffre open/close | Deux actions, rules sur state, `SetStateInteractionExecutor`, feedback sur state | Complet |
| Bouton distant, levier, breaker | Action locale, executor qui change le state d’un autre objet | Complet |
| Démarrage long d’une machine | `TransitionStateInteractionExecutor`: idle → activating → activated | Complet |
| Borne multi-actions façon Borderlands | Ouvrir le shop, vendre la camelotte, tout vendre avec hold ; une rule et un executor par commande | Complet, transactions métier à écrire |
| Dialogue ou vendeur ouvert | Executor `Running()` + session downstream + completion/cancellation par signal serveur | Complet, exemple réseau manquant |
| Corde transportable | Prendre change state/propriété ; le déplacement est gameplay ; rendre est une seconde action avec rule d’inventaire | Complet avec Inventory |
| Batterie, fusible ou power cell | Prendre/insérer/retirer ; rules d’inventaire et states de la socket | Complet avec Inventory |
| Noyau ou réacteur multi-étapes | States `off`, `primed`, `fed`, `unstable`, `detonated`; actions visibles selon la phase | Complet ; script métier au-delà des transitions simples |
| Terminal multi-étapes type Helldivers | Session ou state machine ; chaque écran/étape expose une commande autoritaire | Complet en mono-contributeur |
| Sanctuaire ou checkpoint | Activation longue ou instantanée, state persistent ; voyage/respawn et quête observent ce state | Complet avec systèmes downstream |
| Journal, collectible ou archive | Action instantanée ; state `collected/read`; codex et FlowGraph observent | Complet avec systèmes downstream |
| Réanimation | Hold presence-bound ; système Health termine ou annule l’exécution | Complet en mono-contributeur |
| Générateur coopératif type Dead by Daylight | Plusieurs executors alimentent une progression métier partagée et régressive | Plusieurs points physiques : complet ; une action partagée : concurrence future |
| Réseau électrique de la démo | Breakers mutent les circuits ; portes, cooling, archives et FlowGraph observent | Aligné avec le one-pager |

### Machine multi-actions

Les commandes ne doivent pas être fusionnées dans un executor géant :

- `open_shop` ouvre une session et reste running jusqu’à sa fermeture ;
- `sell_junk` exécute une transaction inventaire/économie instantanée ;
- `sell_all` partage la même famille de transaction mais utilise un `HoldThreshold` pour être sélectionnée intentionnellement.

Une transaction retire les objets et crédite la monnaie atomiquement. Ce ne sont pas deux effects indépendants.

### Corde

```text
coiled
└── take → unrolled/carried
             └── return [rule: interactor owns rope] → coiled
```

L’exécution se termine dès la prise. La corde, son rendu physique et son propriétaire vivent ensuite dans le système métier ; aucune interaction ne reste artificiellement ouverte pendant le déplacement.

### Noyau multi-états

Chaque phase stable expose les actions pertinentes. Une phase longue conserve son exécution uniquement pendant l’engagement réel du joueur. Le réacteur possède ensuite ses timers, ressources, alarmes et transitions autonomes. FlowGraph observe les états significatifs plutôt que les boutons utilisés pour les atteindre.

### Générateur coopératif

Ce cas valide une frontière importante : Interaction suit qui tient une commande ; `GeneratorComponent` suit la progression du travail. La difficulté future n’est pas la state machine, mais l’acceptation de plusieurs exécutions de la même commande sur un target unique et l’exposition d’une progression métier dans la présentation générique.

## Ordre de suite recommandé

1. Ajouter le protocole client autoritaire et les tests serveur + deux clients, y compris les feedbacks Stateful.
2. Corriger les hot paths des détecteurs et garder Area comme baseline de production.
3. Ajouter des exemples corde, dialogue/vendor, machine multi-actions, noyau multi-états et générateur coop à points multiples.
4. Réduire le coût d’authoring du coffre/porte minimal avec une scène exemple et, seulement si la duplication apparaît, un presenter d’animation stateful générique.
5. Évaluer Effects, Proximity/Aim stabilisés et concurrence multi-contributeurs uniquement à partir d’un besoin de jeu réel.

V3 est réussie si un coffre basique reste aussi direct que `commande serveur + feedback OnRep`, si le client demandeur connaît son lifecycle autoritaire, et si deux joueurs peuvent observer et disputer une action longue sans désynchronisation ni feedback dupliqué.
