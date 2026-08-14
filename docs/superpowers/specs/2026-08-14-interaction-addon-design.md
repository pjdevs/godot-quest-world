# Interaction Addon — Design

## 1. Statut

Cette spécification décrit la première version du port Godot de l’`InteractionPlugin` Unreal présent dans `C:/Users/pjmorel/Projects/Perso/QuestWorld/Plugins/InteractionPlugin`.

Le design a été validé le 14 août 2026. La V1 est un addon Godot réutilisable placé directement dans `addons/interaction_plugin`. Elle conserve l’architecture et les responsabilités éprouvées du plugin Unreal, avec des adaptations ciblées aux primitives Godot et une petite extension data-driven fondée sur des règles d’interaction.

## 2. Objectifs

La V1 doit :

- fonctionner en offline, listen server, client et serveur dédié avec le multiplayer high-level Godot ;
- conserver les concepts `Interactor`, `Interactive`, `Stateful`, statut, phases longues, callbacks serveur/client et présentation remplaçable ;
- garder les RPC directement dans l’Interactor, comme le plugin Unreal assume directement le framework réseau Unreal ;
- permettre d’ajouter Interaction à un personnage ou Interactive à un objet existant par composition ;
- fournir une scène interactive de base prête à dériver ;
- séparer le gameplay de la présentation à l’aide de signaux Godot ;
- tester un pipeline data-driven minimal avec des `InteractionRule` réutilisables ;
- utiliser les union types C# 15 pour représenter le statut autorisé ou bloqué ;
- prévoir une frontière de sauvegarde/restauration sans choisir ni implémenter de système de persistance ;
- rester indépendant des futurs addons Character, Inventory, Quest, Dialog et Network Foundation.

Le résultat doit pouvoir être copié dans un autre projet Godot compatible sans importer le reste de Quest World.

## 3. Hors périmètre

La V1 ne comprend pas :

- un framework réseau indépendant de `SceneMultiplayer` ;
- la prédiction ou le rollback des interactions ;
- un backend Nakama, Steam ou propriétaire ;
- le stockage effectif des sauvegardes ;
- les intégrations Inventory, Quest et Dialog ;
- une bibliothèque exhaustive de règles métier ;
- des expressions de règles imbriquées `AnyOf`, `Not` ou des graphes de prédicats ;
- plusieurs verbes simultanés sur un même interactable ;
- l’occlusion des prompts par raycast ;
- une interface 3D cliquable rendue par `SubViewport` ;
- des outils d’édition spécialisés ;
- l’extraction de Character ou Network Foundation en addons.

Ces exclusions ne doivent pas fermer les points d’extension nécessaires à leur ajout ultérieur.

## 4. Principes architecturaux

### 4.1 Port structurel fidèle

Le port conserve les frontières du plugin Unreal au lieu de le redessiner autour d’un command bus ou d’un transport abstrait :

| Unreal | Godot |
|---|---|
| `UIPInteractorComponent` | `InteractionInteractor : Node` |
| `UIPInteractiveComponent` | `InteractiveComponent : Node` |
| `AIPInteractiveActor` | `InteractiveActor.tscn` et script de base |
| `UIPStatefulComponent` | `InteractionStateful : Node` |
| `FIPInteractionStatus` | union `InteractionStatus` |
| `IIPInteractionHandler` | `IInteractionHandler` |
| `IIPStateHandler` | `IInteractionStateHandler` |
| `UWidgetComponent` en screen-space | présentateur local et projection d’un `Marker3D` |
| RPC `Server` sur l’Interactor | méthodes `[Rpc]` sur `InteractionInteractor` |
| propriété `ReplicatedUsing` | `MultiplayerSynchronizer` et application centralisée de l’état |
| SPUD optionnel | capture/restauration factice d’un snapshot |

### 4.2 Direction des dépendances

L’addon Interaction ne dépend que des API Godot et de C# :

```text
Interaction Addon ──> Godot / SceneMultiplayer

Character Addon ────> Interaction Addon (composition optionnelle)
Inventory Integration ──> InteractionRule
Quest Integration ───────> InteractionRule
Dialog Integration ──────> InteractionRule
Network Foundation ──────> orchestre session, identité et spawn
```

Interaction ne dépend jamais de Character, Inventory, Quest, Dialog ou de la session réseau concrète du projet.

### 4.3 Pas d’Input Map cachée

L’addon ne crée et ne lit aucune action d’input. Le contrôleur du projet appelle :

```text
InteractionInteractor.TryStartInteractionInput()
InteractionInteractor.TryEndInteractionInput()
```

L’Interactor peut recevoir un `StringName InteractionActionName` uniquement afin que la présentation puisse afficher l’action configurée par le projet.

## 5. Organisation de l’addon

```text
addons/interaction_plugin/
├── plugin.cfg
├── InteractionPlugin.cs
├── runtime/
│   ├── interactor/
│   ├── interactive/
│   ├── rules/
│   ├── state/
│   └── persistence/
├── presentation/
│   └── ui/
├── scenes/
├── examples/
├── tests/
└── README.md
```

Le plugin ne déclare ni autoload, ni action d’input, ni singleton de session. Son activation rend ses types et scènes disponibles. Les fonctionnalités runtime restent utilisables par composition.

## 6. Topologie des scènes

### 6.1 Personnage interacteur

```text
Character
├── InteractionViewOrigin       Marker3D ou Camera3D
└── InteractionInteractor
```

`InteractionViewOrigin` fournit la position et la direction utilisées par le scoring. `InteractionInteractor` reçoit cette référence explicitement et ne connaît ni le Character, ni son CameraRig, ni son contrôleur.

### 6.2 Objet composé manuellement

```text
Door
├── InteractionArea             Area3D
├── IndicationArea              Area3D facultative
├── InteractionAnchor           Marker3D facultatif
├── InteractiveComponent
└── InteractionStateful
```

Les références vers les zones et l’ancre sont explicites. Le runtime ne dépend pas de noms de nœuds magiques. L’ancre n’est requise que si une présentation projetée est utilisée.

### 6.3 Scène prête à dériver

```text
InteractiveActor.tscn
└── InteractiveActor
    ├── InteractionArea
    ├── IndicationArea
    ├── InteractionAnchor
    ├── InteractiveComponent
    └── InteractionStateful
```

Cette scène remplace le confort de `AIPInteractiveActor`. Un objet existant peut néanmoins utiliser les composants sans hériter de cette scène.

## 7. Contrats principaux

### 7.1 InteractionContext

Le contexte est une valeur runtime non sérialisée qui transporte explicitement les participants :

```csharp
public readonly record struct InteractionContext(
    InteractionInteractor Interactor,
    InteractiveComponent Interactive,
    Node InteractionOwner);
```

Des données supplémentaires ne seront ajoutées que lorsqu’un usage réel les exige. Les règles accèdent au `SceneTree` ou au monde par les nœuds du contexte ; elles ne conservent pas de référence mutable vers le monde.

### 7.2 InteractionStatus en union type

Le statut possède deux cas fermés :

```csharp
public sealed record InteractionAllowed
{
    public static InteractionAllowed Instance { get; } = new();

    private InteractionAllowed() { }
}

public sealed record InteractionBlocked(string Reason);

public union InteractionStatus(
    InteractionAllowed,
    InteractionBlocked);
```

Le cas autorisé réutilise un singleton parce qu’il ne transporte aucune donnée. Le cas bloqué transporte sa raison. Cette représentation fait partie du contrat V1 : un statut est soit autorisé, soit bloqué avec une raison. Les consommateurs utilisent un pattern matching exhaustif.

`InteractionStatus` reste local : il n’est ni exporté, ni sauvegardé, ni envoyé dans un RPC, ni synchronisé comme `Variant` Godot.

### 7.3 IInteractionHandler

Le propriétaire métier de l’interactable implémente :

```csharp
public interface IInteractionHandler
{
    InteractionStatus EvaluateCustomInteractionStatus(
        in InteractionContext context);

    void OnStartInteractionInput(
        in InteractionContext context);

    void OnEndInteractionInput(
        in InteractionContext context);
}
```

`EvaluateCustomInteractionStatus` remplace `GetExtraInteractionStatusForActor`. Son nom explicite qu’il s’agit du dernier hook du pipeline, réservé aux conditions propres à l’objet.

### 7.4 IInteractionStateHandler

Le propriétaire d’un `InteractionStateful` peut recevoir deux callbacks distincts :

```text
OnInteractionStateChangedAuthority(oldState, newState)
OnInteractionStateChangedPresentation(oldState, newState)
```

Le premier contient les conséquences gameplay autoritaires. Le second contient l’animation, l’audio et les effets cosmétiques.

## 8. Règles data-driven

### 8.1 Contrat

```csharp
public abstract partial class InteractionRule : Resource
{
    public abstract InteractionStatus Evaluate(
        in InteractionContext context);
}
```

`InteractiveComponent` expose une collection ordonnée de règles. Elles peuvent être des sous-ressources propres à une scène ou des fichiers `.tres` partagés.

### 8.2 Pipeline d’évaluation

```text
1. Vérifier l’état général et l’exclusivité
2. Évaluer InteractionRules dans l’ordre
3. S’arrêter à la première règle bloquante
4. Appeler EvaluateCustomInteractionStatus
5. Autoriser si toutes les étapes sont valides
```

La première règle bloquante détermine la raison présentée. L’ordre visible dans l’inspecteur est donc l’ordre de priorité des messages.

### 8.3 Contraintes des règles

Une règle :

- est sans effet de bord ;
- ne modifie pas l’état ;
- ne consomme pas d’objet ;
- ne déclenche pas de quête ou de dialogue ;
- ne joue aucune présentation ;
- ne conserve pas d’état runtime dans une `Resource` partagée ;
- doit produire le même verdict côté client et serveur à partir d’un état équivalent.

La V1 fournit seulement la règle abstraite, une règle toujours bloquante utile aux tests et une règle générique fondée sur les groupes Godot. Les futurs addons pourront dériver `InteractionRule` sans ajouter de dépendance inverse.

La liste représente un `AND`. Les combinateurs plus complexes sont repoussés jusqu’à l’apparition d’un usage concret.

## 9. Détection et sélection

Les zones appartiennent à l’interactable, comme dans Unreal :

```text
body entre dans IndicationArea
→ InteractiveComponent trouve l’InteractionInteractor associé
→ AddInteractiveIndication(this)

body entre dans InteractionArea
→ AddInteractive(this)
```

L’Interactor maintient :

- les interactables indiqués ;
- les interactables à portée ;
- la cible actuellement la plus pertinente.

Le scoring initial conserve la formule Unreal :

```text
score = alignement du regard / (1 + distance × coefficient)
```

Les limites de distance et d’angle restent configurables. La fonction de scoring sera isolée afin de pouvoir être remplacée plus tard sans modifier les zones ou le réseau.

Le focus local est recalculé lorsque les candidats changent et pendant le traitement lorsque des candidats existent. Les proxies distants n’exécutent aucune présentation. Le serveur maintient les ensembles issus de la physique afin de valider les cibles proposées par les clients.

Une interaction automatique suit le même pipeline qu’une interaction explicite : lorsqu’elle devient la cible pertinente locale, l’Interactor appelle `TryStartInteractionInput`. En réseau, cette intention est validée par le serveur.

## 10. Réseau

### 10.1 Portée du support

L’addon assume `SceneMultiplayer` et le système RPC high-level Godot. Il reste indépendant du transport concret tant que celui-ci implémente `MultiplayerPeer` et utilise les conventions high-level Godot.

### 10.2 Identité et chemins

Chaque `InteractionInteractor` possède un `OwnerPeerId`, assigné par le système de spawn du projet. La valeur offline est le peer serveur local.

Les RPC Godot exigent des chemins identiques sur les peers. Les personnages et interactables créés dynamiquement doivent donc être spawné avec des noms stables, normalement par `MultiplayerSpawner`. Les objets placés dans une scène partagent naturellement leur chemin si la scène est identique.

### 10.3 Démarrage d’une interaction

Le client envoie la cible qu’il propose :

```text
ServerTryStartInteraction(targetPath)
```

Le serveur :

1. récupère `Multiplayer.GetRemoteSenderId()` ;
2. vérifie que le sender correspond à `OwnerPeerId` ;
3. résout le `NodePath` en `InteractiveComponent` ;
4. vérifie que la cible appartient aux candidats serveur de l’Interactor ;
5. recalcule distance et angle ;
6. réévalue état, exclusivité, règles et hook custom ;
7. appelle le handler uniquement si tout est valide.

Envoyer la cible est une adaptation volontaire du plugin Unreal. Le serveur ne dépend pas d’un focus identique à celui du client, mais ne fait jamais confiance au chemin reçu.

Les RPC start/end sont fiables. Le canal pourra suivre ultérieurement une convention partagée de Network Foundation ; la V1 n’introduit pas de dépendance vers cet addon.

### 10.4 Offline et listen server

Le point d’entrée public choisit le chemin :

```text
Multiplayer.IsServer()
→ exécution autoritaire directe

sinon
→ RpcId(peer serveur, ServerTryStartInteraction, targetPath)
```

Aucun RPC n’est appelé en offline. Le même code autoritaire est utilisé dans tous les modes.

### 10.5 Fin d’une interaction

Le serveur mémorise l’interaction active. Le client envoie seulement `ServerTryEndInteraction()`. Le serveur termine uniquement la phase appartenant au même Interactor.

La sortie de `InteractionArea` côté serveur invoque le même callback de fin. Le handler décide s’il annule, termine ou ignore cette fin d’input.

### 10.6 Refus

Une validation échouée ne déclenche aucune logique métier. Le serveur répond au seul demandeur avec des arguments compatibles `Variant`, par exemple le chemin et la raison sous forme de chaîne. Le client transforme cette réponse en signal `InteractionRejected`.

Les identités falsifiées, chemins invalides et tentatives hors portée peuvent être journalisés côté serveur sans exposer de détails sensibles aux autres clients.

## 11. Interactions longues et états

### 11.1 États

```text
Idle
Activating
Activated
Deactivating
```

La sémantique cible est :

```text
Idle → Activating → Activated
Activated → Deactivating → Idle
```

Le port corrige l’incohérence du code Unreal où la documentation de `StartInteractionPhase` annonce `Activating` alors que l’implémentation assigne `Activated`.

### 11.2 Responsabilité de la durée

L’addon ne possède pas de timer universel. Le handler métier démarre et termine sa phase :

```text
OnStartInteractionInput
→ StartInteractionPhase immédiatement
→ animation, timer, dialogue ou opération métier
→ EndInteractionPhase(nextState)
```

Une interaction instantanée peut exécuter son action sans phase longue.

### 11.3 Réservation et concurrence

`StartInteractionPhase` :

- s’exécute uniquement sur l’autorité ;
- mémorise l’Interactor actif ;
- passe l’état à `Activating` ;
- rend l’objet indisponible aux autres ;
- déclenche la réplication et les callbacks.

Un handler long doit appeler `StartInteractionPhase` synchroniquement avant tout `await`, timer ou animation. Cette règle empêche deux requêtes successives de voir l’objet encore disponible.

`EndInteractionPhase(nextState)` libère d’abord l’Interactor actif, puis applique le prochain état. Cet ordre garantit que la notification provoquée par le nouvel état voit déjà l’objet disponible lorsque `nextState` vaut `Idle`. Une annulation ou la fin d’un dialogue retourne typiquement à `Idle`; une activation permanente termine à `Activated`.

Le release appelle `OnEndInteractionInput` mais ne choisit jamais automatiquement le prochain état. Cette décision appartient à l’objet métier.

## 12. Réplication de l’état

`InteractionStateful.SetState` est autorisé uniquement sur le serveur ou en offline. Une méthode d’application interne unique est utilisée par :

- la mutation autoritaire ;
- la réception de la réplication ;
- la restauration d’un snapshot.

Cette méthode garantit un ordre cohérent des changements, signaux et callbacks.

Un `MultiplayerSynchronizer` distribue l’état courant et couvre les late joins. L’Interactor actif reste serveur-only en V1 ; l’état répliqué suffit pour rendre l’objet indisponible et jouer sa présentation.

Comportement des callbacks :

| Mode | Callback autoritaire | Callback présentation |
|---|---:|---:|
| Offline | oui | oui |
| Listen server | oui | oui |
| Serveur dédié | oui | non |
| Client | non | oui |

Après application d’un nouvel état, l’interactable notifie les Interactors présents afin qu’ils réévaluent leur statut et leur UI.

## 13. Présentation et UI

### 13.1 Frontière par signaux

Le cœur ne connaît aucune classe UI concrète. `InteractionInteractor` émet au minimum :

```text
FocusedInteractiveChanged
InteractionStatusChanged
InteractionRequested
InteractionRejected
```

Le présentateur par défaut écoute ces signaux. Un projet peut le remplacer sans modifier la détection, les règles ou le réseau.

Les paramètres des signaux restent compatibles avec `Variant` : références de nœuds, `NodePath`, booléens et chaînes. Le union type `InteractionStatus` n’est jamais un paramètre de signal Godot. Après une notification, le présentateur demande à l’Interactor son `InteractionPresentation` C# typé ; cela conserve à la fois l’intégration Godot et le pattern matching exhaustif.

### 13.2 Données de présentation

`InteractiveComponent` expose :

- nom ;
- description ;
- ancre monde facultative ;
- scène de prompt facultative ;
- scène d’indication facultative ;
- scène d’indication bloquée facultative.

L’absence de scène signifie qu’aucune UI ne doit être affichée. Une interaction automatique n’affiche pas de prompt.

### 13.3 Présentateur screen-space ancré dans le monde

La présentation par défaut instancie des `Control` sous un `CanvasLayer` local. Elle projette la position du `Marker3D` avec la caméra locale et masque le widget lorsque l’ancre se trouve derrière la caméra.

Ce modèle reproduit le `UWidgetComponent` configuré en `EWidgetSpace::Screen` dans le plugin Unreal. Un véritable écran 3D par `SubViewport` reste une présentation alternative hors V1.

### 13.4 Contrat des widgets

Une scène UI compatible implémente :

```csharp
public interface IInteractionWidget
{
    void Bind(in InteractionPresentation presentation);
}
```

Le modèle contient le nom, la description, l’action Input Map, le statut et la référence locale de l’interactable.

### 13.5 Politique d’affichage

```text
IndicationArea seulement
→ indication autorisée ou bloquée

InteractionArea et cible la plus pertinente
→ prompt principal

InteractionArea mais autre cible sélectionnée
→ indication facultative

Interaction automatique ou aucune scène
→ aucun prompt
```

`InteractiveComponent.NotifyStatusChanged()` demande aux Interactors présents de réévaluer la cible, le statut et les signaux de présentation. Les systèmes dont dépend une condition dynamique doivent appeler cette méthode sur les peers concernés après leur propre mise à jour répliquée.

## 14. Persistance factice

La V1 expose :

```csharp
public InteractionSavedState SaveState();
public void LoadState(InteractionSavedState savedState);
```

Le snapshot initial est versionné :

```csharp
public readonly record struct InteractionSavedState(
    int Version,
    InteractionState State);
```

`SaveState` capture uniquement l’état. `LoadState` est autoritaire et réutilise le chemin d’application commun afin de déclencher les mêmes signaux et callbacks qu’une mise à jour normale.

L’implémentation ne stocke aucun fichier et n’enregistre aucun service global. Elle contient un marqueur intentionnel `TODO(persistence)` indiquant que le projet hôte doit collecter, stocker et restaurer les snapshots. Ce marqueur est une limite de périmètre explicite, pas une exigence non définie.

## 15. Validation des configurations et erreurs

Chaque composant valide sa configuration dans `_Ready()`.

Erreurs bloquantes pour le composant concerné :

- Interactor sans origine de regard ;
- Interactive sans `InteractionArea` ;
- handler absent ou incompatible ;
- Stateful configuré avec une autorité incohérente.

Éléments facultatifs :

- `IndicationArea` ;
- ancre UI ;
- présentateur ;
- scènes de widgets ;
- règles ;
- collecte de persistance.

Une configuration invalide produit un message précis incluant le chemin du nœud, puis désactive uniquement le composant concerné. L’absence volontaire de présentation n’est jamais une erreur.

Les références faibles ou invalides sont purgées lorsque les nœuds quittent l’arbre. La destruction d’un Interactor actif provoque la libération ou l’annulation autoritaire de sa phase.

## 16. Exemple V1

L’addon fournit un exemple autonome qui n’utilise ni Quest, ni Inventory, ni Dialog :

- un interactable à activation longue ;
- une règle data-driven configurable ;
- une réservation empêchant un second Interactor ;
- une transition `Idle → Activating → Activated` ;
- un état synchronisé ;
- une présentation locale remplaçable ;
- un chemin offline et un chemin réseau identiques fonctionnellement.

L’exemple sert à documenter l’intégration et à exercer les frontières de l’addon, pas à devenir une dépendance runtime.

## 17. Stratégie de test

### 17.1 Statut et règles

- les deux cas du union type sont distingués par pattern matching ;
- la première règle bloquante gagne ;
- l’ordre des règles est stable ;
- le hook custom vient après les règles ;
- une `Resource` partagée ne conserve aucun état entre deux interactables ;
- la raison bloquante parvient au modèle de présentation.

### 17.2 Sélection

- entrée et sortie des zones d’indication et d’interaction ;
- ajout et purge des candidats ;
- rejet par distance et angle ;
- choix du meilleur score ;
- changement de focus et signaux correspondants ;
- interaction automatique utilisant le même pipeline.

### 17.3 Phases longues

- réservation immédiate et état `Activating` ;
- refus d’un second Interactor ;
- seul l’Interactor actif peut terminer ;
- release et sortie de zone suivent le callback de fin ;
- fin vers `Idle` ou `Activated` ;
- destruction de l’un des participants sans référence invalide persistante.

### 17.4 Offline et réseau

- offline appelle directement le chemin autoritaire ;
- un client transmet sa cible ;
- sender ne correspondant pas à l’owner refusé ;
- cible inexistante ou hors zone refusée ;
- statut client périmé revalidé par le serveur ;
- état distribué aux clients ;
- late join recevant l’état courant ;
- serveur dédié n’instanciant aucune présentation.

### 17.5 Présentation

- les signaux sont émis même sans présentateur ;
- un présentateur peut être remplacé ;
- widgets autorisé et bloqué reçoivent les bonnes données ;
- l’absence de widget est acceptée ;
- une ancre derrière la caméra masque le widget.

### 17.6 Persistance

- capture d’un snapshot versionné ;
- restauration autoritaire par le chemin commun ;
- callbacks et notification de statut après restauration ;
- version inconnue refusée explicitement.

## 18. Critères d’acceptation V1

La V1 est terminée lorsque :

- l’addon s’active sans configurer automatiquement le projet hôte ;
- l’exemple fonctionne offline, listen server, client et serveur dédié ;
- l’Interactor local sélectionne et présente correctement sa cible ;
- le serveur reste seul décisionnaire des interactions réseau ;
- une cible proposée par le client est systématiquement revalidée ;
- une interaction longue réserve l’objet et expose les quatre états ;
- le même état est visible par les clients présents et les late joins ;
- l’UI fournie peut être supprimée ou remplacée sans modifier le runtime ;
- les règles data-driven et le hook custom coexistent ;
- `InteractionStatus` utilise les union types C# 15 dans le runtime Godot ;
- la frontière save/load est testée sans système de stockage ;
- la documentation d’intégration et les tests couvrent les comportements fragiles ;
- `dotnet format quest-world.csproj`, `dotnet build` et les tests Godot passent conformément aux instructions du projet.

## 19. Évolutions consignées

Les pistes suivantes sont conservées pour une évolution future du framework, indépendamment du port Godot :

- définitions d’interaction data-driven plus complètes ;
- règles Inventory, Quest, Dialog et PersistentAction fournies par leurs addons respectifs ;
- combinateurs de règles ;
- plusieurs options d’interaction par cible ;
- présentations HUD, outline, VR et surface 3D ;
- visibilité/occlusion comme règle de validation ;
- réplication optionnelle de l’identité de l’Interactor actif et de la progression ;
- branchement facultatif sur des conventions de Network Foundation ;
- adaptateur de persistance réel ;
- extraction et réutilisation du Character.

Ces évolutions ne doivent être implémentées que lorsqu’un cas d’usage concret les justifie.
