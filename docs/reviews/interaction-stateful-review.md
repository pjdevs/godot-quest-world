## Priorité haute

1. Le protocole réseau ne réconcilie jamais la prédiction client

Le client démarre immédiatement `_prediction` et mémorise l’input, mais le serveur ne répond qu’en cas de rejet. Or [`ClientInteractionRejected`](C:/Users/pjmorel/Projects/Perso/quest-world/addons/interaction_plugin/runtime/interactor/InteractionInteractor.cs:894) ne nettoie ni `_prediction`, ni `_sustainedInputs`, ni la requête automatique mémorisée.

Conséquences :

- une action rejetée peut continuer d’afficher une fausse progression ;
- une action instantanée avec un `ExpectedDuration > 0` affiche quand même une barre ;
- une durée retournée dynamiquement par `InteractionExecutionRunning(duration)` modifie le serveur, jamais le client ;
- plusieurs groupes peuvent s’exécuter simultanément côté serveur, mais le client ne possède qu’une seule `_prediction`.

Il manque un vrai retour `Accepted/Ended`, avec identifiant et durée autoritaires. Les prédictions devraient être indexées par exécution, pas stockées dans un slot unique.

2. Les réservations sont invisibles sur un client distant

[`EvaluateAvailability`](C:/Users/pjmorel/Projects/Perso/quest-world/addons/interaction_plugin/runtime/interactive/InteractiveComponent.cs:361) utilise `_activeExecutions` pour afficher « déjà en cours » ou « utilisé par quelqu’un d’autre ». Mais cette liste n’existe que sur l’instance serveur et n’est jamais répliquée.

Cela contredit directement la philosophie « ne dupliquez pas la réservation avec un state ». Sans `StatefulComponent` spécifique, le client continue à présenter l’action comme autorisée et peut la redemander, pendant que le serveur la refuse.

Je répliquerais une vue minimale des réservations — groupe, propriétaire et éventuellement progression — sans exposer tout l’objet d’exécution.

3. `InteractionExecutionFailed` devient un `InteractionRejected` côté client

Le target émet correctement `Started` puis `Cancelled` pour un échec accepté, mais [`TryStartInteractionAuthoritatively`](C:/Users/pjmorel/Projects/Perso/quest-world/addons/interaction_plugin/runtime/interactor/InteractionInteractor.cs:938) transforme ensuite cet échec en `false`. Le serveur envoie donc `ClientInteractionRejected`.

Même événement, deux histoires différentes :

- serveur : l’action a démarré puis échoué ;
- client : elle n’a jamais démarré.

Il faut une réponse distincte `ClientInteractionFailed`, ou mieux un résultat réseau exhaustif.

4. La complexité réelle des détecteurs est sous-estimée

Le pipeline déduplique avec [`_detectionBuffer.Contains`](C:/Users/pjmorel/Projects/Perso/quest-world/addons/interaction_plugin/runtime/interactor/InteractionInteractor.cs:390), puis recherche encore chaque target suivi dans cette même `List` à la ligne 465.

Donc, en pire cas :

- Area : `O(overlaps²)`, pas `O(overlaps)` ;
- Proximity : `O(all interactives²)` en CPU, plus les raycasts ;
- Aim : borné par `MaxHits` dans le pipeline, mais chaque collider appelle [`FindByArea`](C:/Users/pjmorel/Projects/Perso/quest-world/addons/interaction_plugin/runtime/interactive/InteractiveComponent.cs:290), qui parcourt tous les interactives : `O(hits × all interactives)`.

Le contrat exige déjà des candidats distincts : soit supprimer la déduplication défensive, soit utiliser un `HashSet` compagnon. Pour Aim, il faut un dictionnaire `Area3D → InteractiveComponent`.

Cela signifie que les complexités indiquées dans le README que je viens d’écrire sont encore trop optimistes et devront être corrigées.

5. Proximity fait exactement ce que son résumé prétend éviter

[`ProximityInteractionDetector`](C:/Users/pjmorel/Projects/Perso/quest-world/addons/interaction_plugin/runtime/detection/ProximityInteractionDetector.cs:7) annonce « without any physics at all », puis appelle `HasLineOfSight` avant le moindre rejet par distance.

Avec le masque par défaut, il entretient donc un raycast par interactive enregistré et par physics frame, y compris pour ceux situés très loin. Il requiert en plus indirectement un `InteractionArea`, car le target générique le bloque sans cela.

Ordre minimal : calculer d’abord la distance maximale d’indication, puis seulement le LOS. Et soit rendre l’Area dépendante du détecteur, soit retirer les promesses « no area/no physics ».

6. Aim tourne sur tous les peers

Le commentaire promet une source exécutée uniquement par le propriétaire, mais [`_PhysicsProcess`](C:/Users/pjmorel/Projects/Perso/quest-world/addons/interaction_plugin/runtime/detection/AimInteractionDetector.cs:127) n’a aucun garde d’ownership. Chaque copie distante force donc son shapecast, même si son interactor ne consomme jamais `_hits`.

Autre surprise : `AimRadius`, `CollisionMask` et `MaxHits` sont exportés comme modifiables, mais seulement copiés dans le `ShapeCast3D` au `_Ready`. Seul `MaxDistance` reste réellement dynamique.

## API et notifications

7. Le dernier fix ne supprime que les doublons des frames stables

Lors d’un changement de focus, l’interactor émet à la fois `FocusedInteractiveChanged` et `InteractionStatusChanged`. Le presenter est abonné aux deux et les deux handlers appellent [`Refresh()`](C:/Users/pjmorel/Projects/Perso/quest-world/addons/interaction_plugin/presentation/ui/InteractionPresenter.cs:139).

Même chose à l’entrée en détection : `InteractiveIndicationAdded` rafraîchit les indications, puis `InteractionStatusChanged` rafraîchit tout.

Puisque le presenter pull déjà chaque frame, je supprimerais ses refresh événementiels ou les transformerais en simple dirty flag consommé une fois par frame.

8. `InteractionStatusChanged` n’est pas réellement une invalidation générale

Une règle qui change de résultat n’émet rien, et `NotifyStatusChanged` est `internal`. Un système gameplay externe ne peut donc pas annoncer proprement « mes règles ont peut-être changé ».

Le nom suggère un contrat général, mais le signal représente surtout les changements de focus, détection et exécution connus du core. Deux options cohérentes :

- le renommer en fonction de ces causes précises ;
- exposer une vraie `InvalidatePresentation()` publique, avec coalescing.

9. Les deux axes de lifetime annoncés comme indépendants ne le sont pas

`CancelOnInputReleased` et `RequiresInteractorPresence` sont décrits comme deux notions distinctes. Pourtant [`RequiresInteractorPresence`](C:/Users/pjmorel/Projects/Perso/quest-world/addons/interaction_plugin/runtime/interactor/InteractionInteractor.cs:994) les combine par un `OR`.

Il est donc impossible d’exprimer « annuler au relâchement, mais ne pas annuler en quittant la zone ». Un enum ou deux politiques réellement séparées seraient plus honnêtes : suivi de l’input d’un côté, validation spatiale de l’autre.

10. La durée a deux sources et `0` a deux sens voisins mais différents

- `ExpectedDuration == 0` signifie « aucune deadline ».
- `InteractionExecutionRunning(0)` signifie « reprendre `ExpectedDuration` ».
- Une durée positive retournée remplace seulement l’horloge serveur.

En plus, le XML parle encore de durée « authored on the action » alors qu’elle appartient désormais à l’executor, et `InteractionActionDefinition` référence un inexistant `InteractionAction.Duration`.

Des factories explicites seraient plus lisibles : `RunningUntilCompleted()` et `RunningFor(seconds)`.

11. Le validator interdit une configuration que le resolver sait résoudre

[`InteractionValidator`](C:/Users/pjmorel/Projects/Perso/quest-world/addons/interaction_plugin/editor/InteractionValidator.cs:112) affirme que deux actions ayant le même input et seuil « cannot be told apart ». Pourtant le resolver les départage par disponibilité, `Priority`, puis id — et [`InteractionBehaviorTest`](C:/Users/pjmorel/Projects/Perso/quest-world/addons/interaction_plugin/tests/InteractionBehaviorTest.cs:1112) le teste explicitement.

Le test de configuration à la ligne 330 grave même la contradiction dans la suite. Ce warning devrait seulement exister si les actions restent réellement indiscernables selon le contrat choisi.

## Stateful et intégration

12. `SetState` retourne un booléen trop pauvre

[`SetState`](C:/Users/pjmorel/Projects/Perso/quest-world/addons/stateful_plugin/runtime/StatefulComponent.cs:98) retourne `false` pour trois causes très différentes :

- absence d’autorité ;
- valeur hors schéma ;
- valeur déjà appliquée.

Les executors transforment ensuite tout cela en messages génériques comme « Nothing happens ». Un `StateMutationResult` exhaustif rendrait l’API bien plus exploitable.

13. `TransitionStateInteractionExecutor` ignore l’échec de l’état final

Le passage au `RunningState` échoue explicitement si `SetState` retourne `false`. En revanche, [`OnExecutionCompleted`](C:/Users/pjmorel/Projects/Perso/quest-world/addons/interaction_plugin/integration/stateful/TransitionStateInteractionExecutor.cs:116) et l’annulation ignorent leur résultat.

L’exécution peut donc être annoncée terminée alors que `CompletedState` n’a jamais été appliqué. Comme la réservation est déjà libérée avant le callback, l’executor n’a aucun moyen de convertir cela en échec.

14. `StateSchema` ressemble à une contrainte, mais n’en est qu’une sur certains chemins

Le type dit déclarer les valeurs que le composant est « allowed to hold ». Pourtant :

- un `InitialState` invalide est signalé puis appliqué ;
- une valeur répliquée invalide est appliquée ;
- `SetState` la refuse ;
- `LoadState` lève une exception.

C’est documenté, donc pas caché, mais le nom et le contrat restent ambigus. Soit le schéma est un invariant, soit il faut le présenter comme validation d’authoring et de mutation locale uniquement.

15. L’intégration « optionnelle » ne l’est pas au niveau packaging

Le core Interaction ne dépend effectivement pas de Stateful, mais le dossier du plugin contient des `.cs` qui l’importent directement, et le validator référence également ces types. Copier `interaction_plugin` seul dans un projet C# ne compile donc pas sans `stateful_plugin`.

Même souci pour les unions preview : leur shim runtime est dans [`quest_world/compatibility/CSharpUnionRuntime.cs`](C:/Users/pjmorel/Projects/Perso/quest-world/quest_world/compatibility/CSharpUnionRuntime.cs:1), hors du plugin.

Si ces addons doivent être réellement réutilisables, je séparerais le bridge Stateful dans un troisième addon et déplacerais la compatibilité union dans le package qui l’utilise.

Enfin, la couverture actuelle est bonne pour l’offline et les mutations pures, mais elle ne crée aucun vrai couple client/serveur. C’est précisément pourquoi les problèmes de réservation, d’acknowledgement, de rejet et d’Aim distant passent sous le radar. Le test `ProximitySpikeDetectsWithoutAnyPhysics` crée même une `Area3D` et traverse le chemin LOS : son nom valide la promesse, pas le comportement réel.
