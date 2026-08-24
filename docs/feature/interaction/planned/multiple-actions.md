# Interaction Core — Handle multiple actions with different input types

```
Action Open       → autorisée seulement si Closed
Action Close      → autorisée seulement si Open
Action Toggle     → toujours autorisée
Action TakeAll    → autorisée tant que l’inventaire n’est pas vide
```

Do we keep the using script to respond to events and do server actions or do we code this actions/effects
into an InteractionAction resource with the input key, input type etc ? 

On conserve donc le Stateful, mais on retire progressivement l’interprétation universelle Idle = interactible / Activated = bloqué.
Le seul point architectural à clarifier est que InteractionState est aujourd’hui nommé comme un état de cycle d’interaction (Activating, Activated, etc.), pas comme un état métier générique. Ce n’est pas forcément urgent, mais il faut décider si InteractionStateful est :

- un petit composant d’état générique répliqué ;
- ou un composant de lifecycle interaction standard.

c.f. [generic statefuls](./generic-stateful.md).

## Brainstorm 1

La bonne séparation n’est probablement pas « Resource ou owner », mais « définition ou instance runtime ».

Une `Resource` est parfaite pour décrire une action réutilisable, mais mauvaise pour porter l’état d’une occurrence précise. Même si elle peut exécuter du code, elle peut être partagée entre plusieurs boutons et n’a pas naturellement le lifecycle, les références de scène, la réplication ou la persistance d’un `Node`.

Je recommande donc :

```text
InteractionActionDefinition : Resource
├─ ActionId
├─ label / description
├─ input
├─ type d’activation
└─ rules

InteractionActionBinding : Node par instance
├─ Definition
├─ Executor optionnel
├─ références de scène
└─ état runtime éventuel

InteractionActionExecutor : Node
└─ Execute(context)
```

Le owner reste l’escape hatch universel lorsqu’il n’y a pas d’exécuteur.

### Application au Button

Le comportement actuel de [Button.cs](C:/Users/pjmorel/Projects/Perso/quest-world/quest_world/interactibles/button/Button.cs) pourrait devenir une brique :

```text
Button
├─ InteractiveComponent
└─ ActivateAction
   ├─ Definition = Activate [E]
   └─ SetStateExecutor
      ├─ Target = LeverWall
      └─ TargetState = Activating
```

`SetStateExecutor` est un `Node` par instance. Il peut donc :

- référencer directement le `LeverWall` ;
- avoir des paramètres propres à ce bouton ;
- conserver `InteractionCount` ;
- recevoir les callbacks Godot ;
- utiliser un `MultiplayerSynchronizer` si nécessaire ;
- déléguer sa persistance à un Stateful ou au système de Save.

Si le compteur n’est qu’un détail temporaire, un champ sur l’exécuteur suffit. S’il représente un vrai fait du monde devant être sauvegardé/répliqué, il doit vivre dans un composant d’état dédié, pas dans la définition de l’action.

### Les différents niveaux de réutilisation

Cette architecture couvre les trois familles sans devoir prédire tous les use cases :

- Action déclarative courante : `SetStateExecutor`, `PlayVfxExecutor`, `TakeAllExecutor`.
- Action réutilisable mais stateful : un Executor Node spécialisé avec son état par instance.
- Action totalement custom : aucun Executor, le owner reçoit `InteractionActionRequested` et fait ce qu’il veut.

Exemple custom :

```csharp
private void OnInteractionActionRequested(
    InteractionActionDefinition action,
    InteractionContext context
)
{
    switch (action.Id)
    {
        case "reroute_power":
            // comportement complètement spécifique
            break;
    }
}
```

### Où placer les Action Effects ?

Je ne construirais pas encore un système complet d’`InteractionEffect` Resources. Il paraît élégant sur le papier, mais on ne sait pas encore si les comportements réels seront suffisamment atomiques et composables.

Je suivrais plutôt ton approche naturelle :

1. Les actions multiples dispatchent vers les owners.
2. On implémente les vrais use cases du jeu.
3. Lorsqu’un comportement se répète, on l’extrait en Executor Node.
4. Si les Executors contiennent ensuite beaucoup de petites commandes stateless répétées, on introduit des `InteractionEffect` Resources à l’intérieur.

On obtiendrait alors éventuellement :

```text
Action Binding
└─ CompositeExecutor
   ├─ SetStateEffect
   ├─ PlaySoundEffect
   └─ IncrementCounterEffect
```

Mais seulement lorsque le besoin réel apparaît.

Le principe que je validerais avant de passer aux states génériques est donc :

> Resource = définition partageable.  
> Node = comportement et état par instance.  
> Owner = échappatoire pour tout comportement imprévisible.

## Réponse 1

Ok mais du coup j'ai l'impression qu'on revient à ce qu'on a i.e. attacher un script n'importe ou, référer l'interactive et exécuter les actions qu'on veut en réponse des clalbakcs. et la c'est ça le piège, on va toujours emettre les évènnement et on ne peut pas controler qui s'est abonner et s'il va faire une action gameplay ou juste une notif etc.

## Brainstorm 2

Exactement. Le piège n’est pas vraiment `Resource` contre `Node` : c’est **commande unique contre broadcast d’événements**.

Le mécanisme actuel :

```text
InteractiveComponent
→ émet InteractionInputStarted
→ zéro, un ou plusieurs abonnés
→ chacun peut muter le gameplay
→ aucun résultat central
```

Le plugin ne sait donc pas :

- qui est responsable de l’exécution ;
- si quelqu’un a réellement traité l’action ;
- si elle a réussi ;
- si plusieurs abonnés ont appliqué deux fois un effet ;
- si un subscriber ne faisait que du son, de l’analytics ou de l’UI.

Je retirerais ce rôle d’exécution aux signaux.

### Une action, un exécuteur explicite

Chaque action doit résoudre exactement un command handler :

```text
InteractionActionBinding
├─ Definition
└─ Executor
```

L’exécuteur peut être :

- le gameplay owner, pour un comportement complètement custom ;
- un Node générique comme `SetStateExecutor` ;
- éventuellement, plus tard, une Resource stateless capable d’exécuter un effet.

Mais il n’y en a qu’un, explicitement configuré et validé.

```csharp
InteractionExecutionResult Execute(
    in InteractionExecutionContext context
);
```

Le flux serveur devient :

```text
requête target + actionId
→ validation
→ réservation interne
→ appel direct de l’unique Executor
→ résultat
→ mise à jour du lifecycle
→ notifications
```

Le handler renvoie quelque chose comme :

```text
Completed
StartedLongRunning
Rejected(reason)
Failed(reason)
```

### Les événements ne servent plus à exécuter

Les signaux publics deviennent uniquement des notifications au passé :

```text
InteractionActionStarted
InteractionActionCompleted
InteractionActionCancelled
InteractionActionRejected
```

Le son, l’UI, les quests et l’analytics peuvent les observer. Ils peuvent techniquement toujours muter quelque chose — on ne peut pas empêcher du code utilisateur de faire n’importe quoi — mais ce n’est plus le contrat d’exécution supporté.

Je supprimerais notamment l’idée d’un fallback diffus :

```text
pas d’Executor
→ broadcast ActionRequested
→ peut-être que quelqu’un s’en occupe
```

Une action sans exécuteur serait une erreur de configuration.

### Le owner reste totalement flexible

Pour un objet custom, le owner est simplement l’exécuteur explicitement assigné :

```csharp
public InteractionExecutionResult Execute(
    InteractionAction action,
    in InteractionExecutionContext context
)
{
    return action.Id switch
    {
        "activate" => ActivateTarget(context),
        "reset" => ResetTarget(context),
        _ => InteractionExecutionResult.Unsupported,
    };
}
```

On retrouve la liberté de [Button.cs](C:/Users/pjmorel/Projects/Perso/quest-world/quest_world/interactibles/button/Button.cs), mais sans abonnement implicite :

```text
InteractiveComponent
→ référence explicitement Button comme Executor
→ Button est l’unique propriétaire des commandes
```

Son compteur, ses références et son état restent sur `Button`. Plus tard, si ce comportement se répète, on remplace `Button` par un `SetStateExecutor` réutilisable sans changer le pipeline du plugin.

### Conséquence importante

Je révise donc mon modèle précédent :

```text
Resource = définition d’action
Executor explicite = propriétaire unique de la mutation gameplay
Events = observation uniquement
```

Pas de fallback par signal. Pas de multiples subscribers responsables de l’effet. Le chantier `non-mutable` s’intègre aussi bien : le core termine sa mutation/réservation, appelle l’exécuteur à une frontière explicite, applique son résultat, puis notifie les observateurs.

## A trancher

La forme et le besoin du fameux stateful générique car au final ce chantier en a presque besoin.
