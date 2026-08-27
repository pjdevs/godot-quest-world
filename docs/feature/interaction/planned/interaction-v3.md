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
- Un dialogue ou un shop **autoritaire** possède sa session réseau ; Interaction lui donne un `ExecutionId`, mais ne devient pas un framework de fenêtres ou de conversations. Une fenêtre **non bloquante** n’a besoin d’aucune session : l’acquittement autoritaire de sa propre requête suffit à l’ouvrir et à la refermer.
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
| `BP_DoFeedback` / `OnStateChangedClient` | abonnement à `StateChangedPresentation`, `isSynchronization` distinguant le rattrapage |
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

`ChestFeedback` applique une première fois la pose correspondant à `Stateful.State` dans `_Ready`, puis écoute `StateChangedPresentation(oldState, newState, isSynchronization)` pour jouer les transitions. La pose vient de l’état courant, y compris à l’arrivée d’un late join, qui reçoit son état comme une transition depuis `InitialState` avec `isSynchronization = true` : le feedback applique alors la pose et garde ses one-shots pour un changement vécu. Voir « P0 bis » plus bas.

Le chemin est vérifié dans le code :

- `SetState` ne mute que sur l’autorité ;
- le setter privé `ReplicatedState` applique la valeur reçue sur les clients ;
- `StateChangedPresentation` est émis sur les clients, le listen host et le jeu offline, jamais sur un dedicated server ;
- `StateChangedAuthority` est émis sur le serveur ;
- `StateChanged` est émis sur chaque peer qui applique la valeur.

Il ne faut pas faire jouer le même feedback cosmétique par l’executor et par le state : le listen host le jouerait deux fois et les clients distants ne verraient que la seconde voie. L’executor commande ; la présentation observe.

Une animation qui déplace aussi une collision n’est pas purement cosmétique. Elle observe `StateChanged` afin de tourner également sur le dedicated server, mais seul le serveur décide de l’état final. `LeverWall` suit déjà ce modèle.

Le test qui manquait existe désormais : `InteractionNetworkTest` fait tourner un vrai serveur et deux vrais clients dans un seul process, avec un `MultiplayerSynchronizer` par branche, et prouve qu’une transition répliquée déclenche exactement une fois le feedback de chaque peer — `StateChanged` et `StateChangedPresentation` partout, `StateChangedAuthority` sur le seul listen host. Il montre aussi la limite qui justifie la règle ci-dessus : deux transitions dans la même frame n’arrivent au client que comme la dernière valeur, une propriété répliquée portant une valeur et non un historique.

## Callbacks Interaction : contrat autorité et acquittement client

| Notification | Où | Sens |
| --- | --- | --- |
| `InteractionRequested` | Client propriétaire, localement | Intention envoyée, pas acceptation serveur |
| `InteractionActionStarted` | Autorité seulement | L’executor a accepté la commande |
| `InteractionActionCompleted` | Autorité seulement | L’action acceptée a terminé |
| `InteractionActionCancelled` | Autorité seulement | L’action acceptée a été interrompue |
| `InteractionActionRejected` | Autorité seulement | Le target a refusé avant exécution |
| `InteractionStarted` | Autorité → propriétaire | Commande acceptée, avec son `ExecutionId` et sa durée |
| `InteractionCompleted` | Autorité → propriétaire | L’action acquittée a terminé |
| `InteractionCancelled` | Autorité → propriétaire | L’action acquittée a été interrompue, avec sa raison |
| `InteractionFailed` | Autorité → propriétaire | L’action acquittée a échoué après acceptation |
| `InteractionRejected` | Client propriétaire | Prévalidation locale ou refus autoritaire — n’a jamais démarré |

Un signal Godot est local : les quatre notifications de l’`InteractiveComponent` ne traversent pas le
réseau. Le client demandeur n’avait donc génériquement ni acceptation ni fin ; il ne possédait qu’une
prédiction, un éventuel state répliqué et un RPC de rejet. Les cinq dernières lignes de la table sont
la réponse, livrée dans la Task 14 de [`interaction.md`](../interaction.md) :

```text
Requested(target, action)                        local, aucune garantie
Started(target, action, executionId, duration)   autorité → demandeur
Completed(target, action)                        autorité → demandeur
Cancelled(target, action, reason)                autorité → demandeur
Failed(target, action, reason)                   autorité → demandeur, toujours après Started
Rejected(target, action, reason)                 autorité → demandeur, jamais après Started
```

Les invariants qui font tenir le protocole :

- exactement un terminal (`Completed | Cancelled | Failed | Rejected`) par requête acceptée ou refusée ;
- `Started` précède les trois premiers, jamais `Rejected` — une action instantanée est acquittée
  `Started` puis `Completed`, miroir exact de l’autorité, donc un seul lifecycle à apprendre ;
- corrélation par `(target, actionId)` et non par numéro de requête, ce qui est suffisant **parce que**
  le demandeur ne garde qu’une prédiction et une entrée soutenue par input : au plus une requête d’une
  paire donnée est en vol. Tolérer plusieurs requêtes concurrentes sur la même paire exigerait d’abord
  un `RequestId` ;
- délivré exactement une fois au propriétaire, **listen host inclus**, par appel local direct plutôt que
  par `CallLocal` ;
- jamais diffusé : les autres peers observent par Stateful ou par le système métier. C’est late-join-safe
  là où un acquittement transitoire ne l’est pas, et ça ne divulgue pas une action qui leur est cachée.

L’`ExecutionId` est une donnée utile — adresser une session downstream — pas la clé de corrélation.

### Deux canaux de présentation

La présentation a deux sources, et les confondre est ce qui produit le double feedback :

| Canal | Portée | Late join | Ce qu’il porte |
| --- | --- | --- | --- |
| `StatefulComponent` répliqué | tous les peers | sûr | ce qui est vrai dans le monde |
| Acquittement autoritaire | demandeur seul | non | ce qui n’est vrai que pour lui, son UI locale |

Une UI ouverte par l’acquittement ne doit donc pas l’être aussi par l’état répliqué, exactement comme un
feedback cosmétique ne doit pas être joué par l’executor **et** par le state.

### Dialogue et vendeur

Trois voies existent, aucune n’est imposée. Le choix se fait sur ce que le système downstream doit
posséder, pas sur la taille de la fenêtre.

**Popup non bloquant** — aucun système réseau downstream. L’executor commande côté serveur ; un script de
présentation local du demandeur s’abonne à l’acquittement.

```text
Client demande l’interaction
→ serveur accepte, executor Running() ou Completed()
→ Started acquitté au seul demandeur
→ le client ouvre son menu
→ Completed / Cancelled / Failed le referment
```

Ce que le menu **engage** reste autoritaire par ailleurs : chaque achat est sa propre commande validée
par le serveur. La fenêtre n’est qu’un feedback client.

**Dialogue autoritaire bloquant** — le système Dialog possède sa session réseau et ses données.

```text
Client demande l’interaction
→ serveur accepte
→ Execute appelle le système Dialog serveur, qui valide et ouvre une session pour ce peer
→ SessionStarted au client, avec ses données
→ le client ouvre son UI

Client choisit ou ferme
→ requête au système Dialog serveur → validation métier
→ CompleteExecution / CancelExecution
→ SessionCompleted / SessionCancelled au client
```

L’acquittement Interaction reste le lifecycle générique et ne remplace pas ces données.

**RPC propre à l’executor** vers le peer demandeur, pour le cas intermédiaire : une fenêtre qui a besoin
d’un payload sans mériter un système de sessions.

Dans les trois cas, ouvrir l’UI sur `InteractionRequested` serait optimiste : le serveur peut encore
refuser. C’est précisément ce que l’acquittement remplace.

## Trous V3 confirmés

### P0 — protocole et tests réseau

1. ~~**Pas d’acknowledgement autoritaire côté client.**~~ Livré : voir le protocole ci-dessus. La
   corrélation est `(target, actionId)` et non `request/execution`, ce qui suffit tant qu’une seule
   requête d’une paire est en vol.
2. ~~**Le rejet ne réconcilie pas la prédiction.**~~ Livré, avec une correction du point tel qu’il était
   écrit : nettoyer aussi la requête automatique mémorisée **suffisait à créer un flot**, puisque la
   frame suivante réémettait la même requête. La paire refusée est donc retenue comme backoff et
   relâchée dès que la situation change — focus qui bouge, action qui quitte les choix offerts, ou
   gameplay qui invalide la cible.
3. ~~**`Failed` devient `Rejected` sur le client.**~~ Livré : `TryStartInteractionAuthoritatively`
   retourne l’`InteractionExecutionResult` au lieu d’un `bool` + `out string` qui écrasait quatre issues
   en une. Seul un `Rejected` produit un refus.
4. ~~**Aucun vrai test Interaction à plusieurs peers.**~~ Livré : `InteractionNetworkTest` monte un vrai
   serveur et deux vrais clients dans un seul process (une `MultiplayerApi` et un pair ENet par
   sous-arbre, `MultiplayerSynchronizer` compris) et couvre l’acquittement, la concurrence et les
   feedbacks Stateful. Le montage exige que les chemins réseau soient nommés relativement à
   `SceneMultiplayer.RootPath` et que chaque branche soit peuplée après l’attachement de son API ; voir
   [`godot-multiplayer-in-process-peers.md`](../../memory/godot-multiplayer-in-process-peers.md).

Couverts par `InteractionNetworkTest` :

- A démarre : serveur, A et B voient `activating`, et B présente l’action comme busy par la seule rule
  d’état, sans avoir reçu le moindre événement d’interaction — pendant que l’acquittement n’est allé qu’à A.
- B demande pendant que A tient l’action : aucune seconde exécution, et B l’apprend seul.
- A et B demandent dans la même frame : une seule exécution démarre, un `started` et un `rejected`, et le
  perdant vide sa prédiction immédiatement pendant que le gagnant continue de dessiner la sienne.
- Completion, release, perte de la fenêtre autoritaire et départ de l’interacteur : chaque fin acquitte
  son demandeur seul, et B peut commencer ensuite.
- Deux cibles distinctes, et deux groupes de concurrence distincts d’une même cible : les deux démarrent.
- Chaque transition répliquée joue le feedback de chaque peer exactement une fois.
- Late join pendant `activating` et après `activated` : le nouvel arrivant lit l’état courant, ne voit
  jamais les états intermédiaires qu’il a manqués, et présente correctement comme busy une action déjà
  prise. Il reçoit son état d’arrivée comme une transition depuis `InitialState` marquée
  `isSynchronization`, ce qui est le contrat retenu en P0 bis.

Reste à couvrir :

- Dialogue autoritaire : l’UI ne s’ouvre qu’après `SessionStarted`, puis sa fermeture termine ou annule la bonne exécution sur le serveur.

La liste `_activeExecutions` n’a pas besoin d’être répliquée pour ces scénarios : `activating` en est déjà la projection métier. Une action longue sans aucun état ou système métier répliqué conserve volontairement une présentation distante optimiste jusqu’au refus serveur.

### P0 bis — ~~le late join ne se distingue pas d’une transition~~

Livré, et tranché autrement que les trois directions proposées ici : **le framework donne l’information,
la présentation décide**. Les trois signaux de `StatefulComponent` portent désormais
`(oldState, newState, isSynchronization)`, de signature identique pour qu’un même handler puisse être
branché sur plusieurs canaux.

`isSynchronization` est vrai quand ce peer **rattrape une vérité établie ailleurs** — première valeur
répliquée reçue (late join) ou restauration de sauvegarde — et faux pour tout changement vécu. La
transition est émise dans les deux cas, délibérément : c’est elle qui fait jouer son ouverture à une
porte trouvée déjà ouverte, donc qui amène la pose *et la collision* à la bonne valeur. Ce qu’un feedback
garde pour lui, c’est le one-shot :

```cs
private void OnStateChangedPresentation(StringName old, StringName @new, bool isSynchronization)
{
    if (isSynchronization) { ApplyPose(@new); return; }   // porte déjà ouverte : pose seule
    PlayTransition(old, @new);                            // ouverture vécue : anim + confettis
}
```

Pourquoi pas les autres directions : ne rien émettre à la première synchro laisse la porte fermée chez
l’arrivant, ou oblige chaque feedback à savoir appliquer une pose sans animation ; un quatrième signal
`StateSynchronized` force le consommateur courant — qui veut réagir aux **deux** — à deux abonnements et
deux handlers là où le flag coûte un `if` ; une propriété lue dans le handler sort l’information de la
signature et devient fausse dès qu’un script rappelle son handler depuis `_Ready`.

La restauration de sauvegarde est traitée comme une synchronisation par le même critère : restaurer une
save où le coffre est ouvert, c’est rattraper une vérité, pas ouvrir le coffre.

Le contrat résiduel, à connaître : `oldState` sur une synchronisation est l’`InitialState` et non l’état
réellement précédent — un arrivant reçoit `idle → activated` là où le monde a fait
`idle → activating → activated`. Un feedback ne suppose donc pas que la paire reçue est une arête de la
machine, seulement que `newState` est vrai.

Deux tests réseau tiennent les deux sens : l’arrivant sur une cible déjà `activated` reçoit
`idle > activated` avec le flag vrai ; l’arrivant sur une cible intacte ne reçoit rien à l’arrivée — le
full sync porte une valeur égale — puis vit sa première vraie transition avec le flag faux. Le détail qui
rend ce second cas correct est que l’arrivée est **dépensée par ce full sync silencieux** ; le marqueur
est par ailleurs remis à zéro dans `_Ready`, parce que `ReplicatedState` est un `[Export]` que le
chargement de scène écrit avant l’entrée dans l’arbre.

### P0 ter — ~~une déconnexion brutale fait fuiter la réservation~~

Livré, du côté plugin. `InteractionInteractor` s’abonne à `MultiplayerApi.PeerDisconnected` dans son
`_Ready` et annule ses exécutions quand le pair qui part est son `OwnerPeerId`. Le plugin ne dépend plus
d’une couche de spawn qu’il ne contrôle pas, et reste correct si le projet dépeuple aussi : l’exécution
est déjà terminée et un identifiant n’est jamais réutilisé.

Une conséquence a été traitée avec : un acquittement ne part plus vers un pair perdu. `CanSendToOwner`
retient le départ, sinon la cancellation elle-même aurait nommé un pair inconnu comme cible de RPC.
L’état du monde est corrigé pour tous ; seul le destinataire disparu n’est plus adressé.

`ADroppedPeerReleasesItsExecutionOnTheAuthority` prouve maintenant la garantie là où il figeait le trou :
le pair A tombe, personne ne retire son nœud, et B démarre.

### P1 — ~~coût des détecteurs et de la présentation~~

Les quatre points sont livrés, sans changement de comportement observable : c’est le coût qui bouge, pas
le contrat.

1. **Pipeline commun.** `_detectionBuffer` est un `HashSet` comme l’ensemble suivi en face. Les deux
   côtés du reconcile posent une question d’appartenance, une fois par candidat puis une fois par cible
   suivie ; en listes cela restait quadratique dans le nombre de candidats, qui vaut *tout le registre*
   pour un détecteur dont la source est le registre.
2. **Proximity.** Les fenêtres de distance sont évaluées **avant** la ligne de vue, et les deux plutôt
   que la plus large — une cible peut déclarer une portée d’interaction supérieure à sa portée
   d’indication, et hors des deux est le seul cas qui ne mérite aucun rayon. Un objet lointain ne
   demande donc plus de LOS, donc n’entretient plus d’échantillon : le cache suit ce qu’on lui demande.
   Sémantique inchangée, y compris « occulté ⇒ `None` » pour les deux tiers.
3. **Aim.** Trois corrections. `InteractiveComponent` maintient un index `area → propriétaire`
   (`_areaOwners`, clé = instance id), donc `FindByArea` ne parcourt plus le registre à chaque hit ;
   l’index est rempli à l’enregistrement, comme les signaux que la cible connecte à ces mêmes areas —
   échanger une area à chaud était déjà hors contrat. Les paramètres de cast (`AimRadius`,
   `CollisionMask`, `MaxHits`) sont poussés depuis leurs setters, donc réglables sur une scène qui
   tourne ; `MaxDistance` l’était déjà, étant la longueur du sweep. Et le shapecast ne part plus sur les
   copies distantes : `InteractionDetector.IsCandidateSourceActive` est renseigné par l’interacteur —
   la propriété de l’ownership lui appartient, pas au détecteur — et vaut faux jusqu’à sa première
   frame, ce qui fait sauter une frame à un personnage qui vient d’apparaître plutôt que d’ouvrir une
   race. Prouvé par `OnlyTheOwningPeerIsToldToRunItsCandidateSource`.
4. **Presenter.** Le pull reste la stratégie, donc les abonnements à `FocusedInteractiveChanged` et
   `InteractionStatusChanged` sont **supprimés** : la frame rebindait déjà, et refaire toute la
   présentation sur le signal la faisait tourner deux fois sur les frames où quelque chose changeait
   vraiment. Restent les deux signaux d’indication, pour ce qu’eux seuls portent — l’ensemble des cibles
   indiquées. `InteractiveIndicationRemoved` libère toujours son widget sur le champ : la cible sort de
   l’ensemble que la frame parcourt, donc plus rien ne reviendrait vers elle.

Ce qui reste, et n’a pas été fait ici : les diagnostics de layers/masks/areas et un profil dense réel.

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
| Vendeur popup non bloquant | Executor `Running()` + fenêtre locale ouverte et refermée par le seul acquittement | Complet, couvert par un test |
| Dialogue autoritaire bloquant | Executor `Running()` + session downstream + completion/cancellation par signal serveur | Complet, exemple réseau manquant |
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

1. ~~Ajouter le protocole client autoritaire et les tests serveur + deux clients, y compris les feedbacks Stateful et le late join.~~ Livré, P0 bis et P0 ter compris : le late join est distingué par `isSynchronization` et une déconnexion
   ne fuite plus de réservation. Reste l’exemple de dialogue autoritaire.
2. ~~Corriger les hot paths des détecteurs et garder Area comme baseline de production.~~ Livré (P1) ; Area reste la baseline. Restent les diagnostics de layers/masks/areas et un profil dense réel.
3. Ajouter des exemples corde, dialogue/vendor, machine multi-actions, noyau multi-états et générateur coop à points multiples.
4. Réduire le coût d’authoring du coffre/porte minimal avec une scène exemple et, seulement si la duplication apparaît, un presenter d’animation stateful générique.
5. Évaluer Effects, Proximity/Aim stabilisés et concurrence multi-contributeurs uniquement à partir d’un besoin de jeu réel.

V3 est réussie si un coffre basique reste aussi direct que `commande serveur + feedback OnRep`, si le client demandeur connaît son lifecycle autoritaire, et si deux joueurs peuvent observer et disputer une action longue sans désynchronisation ni feedback dupliqué.
