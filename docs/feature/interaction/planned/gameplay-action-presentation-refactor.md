# Gameplay Action Presentation Refactor — Implementation Plan

**Goal:** faire de `gameplay_action_plugin` l'unique propriétaire de l'availability, de la présentation d'une action et de la présentation du geste d'input ; `interaction_plugin` ne conserve que la présentation du target interactif.

**Architecture:** `GameplayActionPresentation` représente une action telle qu'elle est offerte par un binding local. Le `GameplayActionRunner` possède déjà les bindings et le gesture resolver ; il devient donc aussi la source du `HoldProgress/HoldElapsed`. `InteractionPresenter` compose ce read model générique dans `InteractionTargetPresentation`, tandis qu'un nouveau `GameplayActionPresenter` affiche uniquement les bindings du `OwnedActionComponent`. Le runner sait déjà distinguer actions owned et bindings externes via `binding.Component`, donc aucune dépendance vers `InteractionAction` n'est nécessaire. 

**Invariant final :**

```text
gameplay_action
  Action / Definition
  Binding / Input
  Availability
  Gesture state
  Execution presentation
  Action presentation
  Default action widget/presenter

interaction
  Detection / Focus
  Interaction-specific access
  Interactive target
  Target presentation
  Target projection / indication
```

Et après **chaque task code** : `csharpier format .`, `dotnet build`, puis la suite complète `dotnet test` avec `GODOT_BIN`, conformément aux règles du repo. 

---

## Task 1 — Supprimer la deuxième availability

**Files principaux**
- `addons/gameplay_action_plugin/runtime/GameplayActionTypes.cs`
- `addons/interaction_plugin/runtime/InteractionTypes.cs`
- `addons/interaction_plugin/runtime/rules/InteractionRule.cs`
- `addons/interaction_plugin/runtime/interactive/InteractiveComponent.cs`
- `addons/interaction_plugin/integration/stateful/StatefulStateInteractionRule.cs`
- règles/examples/tests Interaction concernés

### API cible

```csharp
public abstract GameplayActionAvailability Evaluate(
    in InteractionContext context
);
```

et :

```csharp
public GameplayActionAvailability EvaluateAvailability(
    InteractionInteractor interactor,
    InteractionAction action
);
```

Ajouter aussi côté générique :

```csharp
public enum GameplayActionUnavailableKind
{
    Hidden,
    Blocked,
}
```

avec conversion vers `GameplayActionHidden` / `GameplayActionBlocked`.

### Étapes

- [ ] Écrire/adapter les tests pour exprimer que `InteractionRule` retourne directement `GameplayActionAvailability`.
- [ ] Vérifier qu'ils échouent tant que les anciens types existent.
- [ ] Migrer `InteractionAllowed/Blocked/Hidden` → `GameplayActionAllowed/Blocked/Hidden`.
- [ ] Migrer `InteractionUnavailableKind` → `GameplayActionUnavailableKind`.
- [ ] Supprimer `InteractionAvailability`, ses trois records et **toutes les conversions aller-retour**.
- [ ] Conserver `"Interaction unavailable."` uniquement comme adaptation de message Interaction si on veut préserver ce wording ; surtout pas comme nouveau type d'availability.
- [ ] Vérifier les comportements importants : Hidden disparaît, Blocked reste présent avec raison, Allowed reste requestable.
- [ ] Mettre à jour `gameplay-action.md` + `interaction.md`.
- [ ] Full validation.
- [ ] Commit : `refactor(gameplay-action): unify action availability`

Ce task élimine notamment le chemin actuel `GameplayActionAvailability -> InteractionAvailability -> ...` visible dans `InteractiveComponent`. 

---

## Task 2 — Généraliser le read model et le widget d'action

**Create**
- `addons/gameplay_action_plugin/presentation/GameplayActionPresentation.cs`
- `addons/gameplay_action_plugin/presentation/ui/IGameplayActionWidget.cs`
- `addons/gameplay_action_plugin/presentation/ui/GameplayActionPromptWidget.cs`
- `addons/gameplay_action_plugin/scenes/GameplayActionPrompt.tscn`
- `addons/gameplay_action_plugin/tests/GameplayActionPresentationTest.cs`

**Modify**
- `InteractionTypes.cs`
- `InteractionPresenter.cs`
- `InteractionSceneTest.cs`
- `InteractionConfigurationTest.cs`
- `.tscn` utilisant actuellement `InteractionActionPrompt.tscn`

### API cible

```csharp
public readonly record struct GameplayActionPresentation(
    StringName ActionId,
    string Label,
    string Description,
    StringName InputActionName,
    GameplayActionAvailability Availability,
    GameplayActionActivationMode ActivationMode,
    float? HoldProgress = null,
    float? HoldElapsed = null
)
{
    public bool IsAllowed => Availability is GameplayActionAllowed;

    public bool IsAutomatic =>
        ActivationMode == GameplayActionActivationMode.Automatic;

    public bool IsHoldable =>
        ActivationMode == GameplayActionActivationMode.Hold;

    public string BlockReason =>
        Availability is GameplayActionBlocked blocked
            ? blocked.Reason
            : string.Empty;
}
```

Donc **pas** de `IsAutomatic`/`IsHoldable` stockés : `ActivationMode` est la source de vérité.

Widget :

```csharp
public interface IGameplayActionWidget
{
    void Bind(
        in GameplayActionPresentation action,
        GameplayActionExecutionPresentation? execution
    );
}
```

### Étapes

- [ ] Test-first sur les propriétés dérivées Allowed/Blocked/Hidden + Hold/Automatic.
- [ ] Créer `GameplayActionPresentation`.
- [ ] Faire devenir :

```csharp
InteractionTargetPresentation.Actions
```

un :

```csharp
IReadOnlyList<GameplayActionPresentation>
```

- [ ] Renommer/déplacer `IInteractionActionWidget` → `IGameplayActionWidget`.
- [ ] Déplacer `InteractionActionPromptWidget` et sa scène dans `gameplay_action_plugin` sans changement visuel.
- [ ] Migrer `InteractionPresenter` sur les types génériques.
- [ ] Modifier les références `.tscn` : Battery, Button, Door, `LongActionExample`, etc. Elles pointent aujourd'hui directement vers la scène Interaction.  
- [ ] Supprimer `InteractionActionPresentation`, `IInteractionActionWidget`, ancien widget et ancienne scène.
- [ ] Full validation.
- [ ] Commit : `refactor(gameplay-action): own action presentation`

À ce stade, Interaction possède encore temporairement le calcul du hold, mais **le type qui le transporte est déjà générique**.

---

## Task 3 — Remonter entièrement le gesture/hold dans le Runner

C'est le cleanup qu'on ne laisse plus optionnel.

**Modify**
- `GameplayActionGestureResolver.cs`
- `GameplayActionRunner.cs`
- éventuellement `GameplayActionBindingStore.cs` pour une lookup propre
- `InteractionInteractor.cs`
- `InteractiveComponent.cs`
- `GameplayActionRunnerTest.cs`
- tests Interaction de hold

### Problème actuel

`GameplayActionGestureResolver` possède déjà le vrai `Elapsed` et les `CandidateIds`, mais n'expose qu'un `TryGetGestureProgress()` qui prend arbitrairement un gesture puis normalise sur le hold restant le plus long. 

Interaction doit alors reconstituer l'elapsed en allant rechercher `GetLongestHoldThreshold()` sur le target. C'est exactement la fuite qu'on veut supprimer.

### API cible

Je ferais **par binding**, pas par input :

```csharp
public bool TryGetBindingHoldProgress(
    ulong bindingId,
    out float progress,
    out float elapsed
);
```

Et une lookup générique utile :

```csharp
public bool TryGetBinding(
    GameplayActionComponent component,
    StringName actionId,
    GodotObject source,
    out GameplayActionBinding? binding
);
```

### Sémantique importante

Le resolver doit vérifier que `bindingId` fait partie des `CandidateIds` capturés **au début du gesture**.

Donc une action bindée après le press :

```text
press
↓
gesture captures A/B
↓
binding C appears
```

ne reçoit **aucun** hold progress. Elle n'appartenait pas au gesture.

Pour deux holds sur le même input :

```text
A threshold = 0.5 s
B threshold = 1.0 s
elapsed = 0.25 s
```

on expose :

```text
A: progress = 0.50, elapsed = 0.25
B: progress = 0.25, elapsed = 0.25
```

C'est la donnée correcte pour chaque action.

### Tests first

- [ ] Deux bindings Hold partageant l'input mais avec thresholds différents donnent des progress différents.
- [ ] Deux gestures sur deux inputs différents sont requêtables indépendamment — plus aucun "premier gesture arbitraire".
- [ ] Un binding ajouté après le début du gesture ne reçoit pas de progress.
- [ ] Un binding `Press`/`Release` retourne false.
- [ ] Un binding retiré pendant le gesture ne produit plus de présentation.

Puis :

- [ ] Implémenter la query par `bindingId` dans `GameplayActionGestureResolver`.
- [ ] L'exposer sur `GameplayActionRunner`.
- [ ] Faire consommer cette API par la construction de `GameplayActionPresentation`.
- [ ] Supprimer `InteractionProgress`.
- [ ] Supprimer `InteractiveComponent.GetLongestHoldThreshold()`.
- [ ] Supprimer `InteractionInteractor.TryGetGestureProgress()`.
- [ ] Supprimer `InteractionInteractor.TryGetGestureElapsed()`.
- [ ] Migrer les tests Interaction pour vérifier directement :

```csharp
presentation.HoldProgress
presentation.HoldElapsed
```

au lieu d'interroger l'interactor.

- [ ] Mettre à jour docs + enlever la remarque devenue obsolète dans `docs/feature/gameplay_action/review.md` sur le gesture arbitraire.
- [ ] Full validation.
- [ ] Commit : `refactor(gameplay-action): expose per-binding hold presentation`

**Après ce commit, Interaction ne sait plus comment fonctionne un gesture.**

---

## Task 4 — Ajouter le presenter générique des actions owned

**Create**
- `addons/gameplay_action_plugin/presentation/ui/GameplayActionPresenter.cs`
- `addons/gameplay_action_plugin/tests/GameplayActionPresenterTest.cs`

Il suit le même modèle de refresh que `InteractionPresenter` : widget créé quand la structure change, mais `Bind()` rappelé chaque frame pour availability/progress. `InteractionPresenter` fonctionne déjà ainsi. 

### Sélection

```csharp
binding.Component == ActionRunner.OwnedActionComponent
```

puis :

```csharp
availability is not GameplayActionHidden
&& binding.ActivationMode != GameplayActionActivationMode.Automatic
```

**Aucun :**

```csharp
action is not InteractionAction
```

**Aucun usage de `PresentationContext` pour classifier l'action.**

### Identité UI

Le dictionnaire interne est :

```csharp
Dictionary<ulong, Control> // binding.Id
```

et surtout pas `ActionId`.

Deux bindings :

```text
Drop Battery / Q
Drop Battery / Gamepad X
```

sont deux bindings distincts même s'ils partagent le même `ActionId`.

### Tests

- [ ] Une owned action visible crée un widget.
- [ ] Un binding externe est ignoré, même si son action n'est pas une `InteractionAction`.
- [ ] `Blocked` reste présenté et affiche sa raison.
- [ ] `Hidden` n'est pas présenté.
- [ ] `Automatic` n'est pas présenté comme prompt joueur.
- [ ] Deux bindings du même `ActionId` créent deux widgets.
- [ ] Hold progress change → même widget instance, nouveau `Bind`.
- [ ] Binding supprimé → widget supprimé sans modifier le dictionary pendant son enumeration.
- [ ] Runner non local → présentation clear.

- [ ] Full validation.
- [ ] Commit : `feat(gameplay-action): add owned action presenter`

---

## Task 5 — Remplacer le proto QuestWorld et supprimer tout le legacy

**Modify**
- `quest_world/character/Character.tscn`
- `docs/feature/gameplay_action/gameplay-action.md`
- `docs/feature/interaction/interaction.md`
- `docs/feature/character/character.md`

**Delete**
- `quest_world/character/ActionPresenter.cs`
- son `.uid`
- tous les anciens fichiers action-presentation Interaction qui auraient survécu aux tasks précédents

Le `Character.tscn` utilise aujourd'hui explicitement le presenter jeu + `InteractionActionPrompt.tscn`. 

Le remplacement devient simplement :

```text
GameplayActionPresenter
  Runner = ../GameplayActionRunner
  Container = ...
  ActionScene = GameplayActionPrompt.tscn
```

### Final assertions

- [ ] Take Battery affiche toujours son interaction.
- [ ] Take Battery ajoute Drop Battery à l'inventory/player.
- [ ] Drop Battery apparaît via le **generic GameplayActionPresenter**.
- [ ] Drop Battery exécute correctement et disparaît quand son action/binding disparaît.
- [ ] Une interaction focusée ne se retrouve jamais dans le presenter owned.
- [ ] Les prompts Interaction utilisent exactement le même widget générique.
- [ ] Les holds Interaction continuent à progresser avec leur threshold individuel.
- [ ] Les actions bloquées restent visibles avec leur raison.

Puis recherche globale : **zéro occurrence runtime** de :

```text
InteractionAvailability
InteractionAllowed
InteractionBlocked
InteractionHidden
InteractionActionPresentation
IInteractionActionWidget
InteractionProgress
TryGetGestureProgress
TryGetGestureElapsed
GetLongestHoldThreshold
quest_world/character/ActionPresenter
```

Enfin :

```bash
csharpier format .
dotnet build
GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot dotnet test
godot --headless --path . \
  --scene res://quest_world/levels/test_world.tscn \
  --quit-after 3
```

- [ ] Commit : `refactor(gameplay-action): complete generic presentation migration`

---

### End state

Le flow Drop Battery devient enfin tout bête :

```text
Inventory grants InputGameplayAction
        ↓
GameplayActionRunner auto-binds owned action
        ↓
GameplayActionBinding
        ├── availability
        ├── input config
        └── gesture state
        ↓
GameplayActionPresentation
        ↓
GameplayActionPresenter
```

Et Interaction devient seulement :

```text
Detection / Focus
        ↓
bind external InteractionAction into Runner
        ↓
GameplayActionPresentation
        ↓
InteractionTargetPresentation
        ↓
InteractionPresenter
```
