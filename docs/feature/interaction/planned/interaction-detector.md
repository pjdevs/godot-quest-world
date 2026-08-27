# Interaction Detector — couche de détection remplaçable

## Pourquoi

La façon dont un jeu décide « quel objet je peux interagir avec » dépend du jeu, pas du framework.

- Un Skyrim fait un raycast unique depuis la vue : précis, mais viser devient pénible.
- Un Borderlands 4 / Dying Light 2 laisse viser approximativement et pardonne l'angle. C'est le modèle
  dont ce framework est parti, et c'est pour ça qu'il repose sur des `Area3D` par cible.

Chaque dev pèse le pour et le contre pour son jeu. Aujourd'hui ce choix est **codé en dur** dans
`InteractionInteractor` : les `Area3D`, le filtre distance/angle et le scoring y sont mélangés au reste
(commandes, RPC, exécutions, présentation).

## L'observation qui structure tout

Les quatre approches envisagées ne sont pas quatre systèmes. C'est **un seul pipeline en trois étages
dont seul le premier change** :

```text
1. source de candidats   ← varie selon le jeu
2. prédicats             distance, angle, LOS
3. sélection             score → 1 focus + N indiqués
```

- Area par cible → source = les signaux d'entrée/sortie des areas
- Area sur l'interacteur → source = une area unique côté joueur
- Registre + distance → source = la liste des interactibles enregistrés
- Cast depuis la vue → source = les hits du cast

Les étages 2 et 3 sont **invariants**. La preuve : même l'approche « cast » a besoin d'un calcul d'angle
dès qu'un cast élargi touche plusieurs objets — celui qui est devant n'est pas forcément celui qu'on vise
le plus au centre. Le layer remplaçable n'est donc pas « le système d'interaction », c'est **la source de
candidats**. C'est beaucoup plus petit que la question ne le laissait croire.

## Contrat

Un **Node abstrait** exporté sur l'interacteur, exactement comme `InteractionActionExecutor`. Pas
d'interface : conforme au §25 de l'architecture V2, portable GDExtension. Node et pas Resource parce qu'un
détecteur a besoin d'enfants (une `Area3D`), de signaux et d'un `_PhysicsProcess`.

```csharp
public enum InteractionDetectionKind { None, Indicated, Interactible }

public abstract partial class InteractionDetector : Node
{
    // Seul membre obligatoire. Appelé par frame côté client propriétaire,
    // et tel quel côté serveur pour valider une commande.
    public abstract InteractionDetectionKind Detect(InteractiveComponent interactive);

    // Défaut : le registre global. Surchargé seulement par un détecteur qui possède sa propre source.
    protected virtual IEnumerable<InteractiveComponent> GetCandidates();

    // Défaut : alignement / distance, le scoring actuel.
    protected virtual float Score(InteractiveComponent interactive);

    // Prédicats partagés fournis à tous, exports compris.
    protected bool IsWithinRange(InteractiveComponent i, float maxDistance, float maxAngleDegrees);
    protected bool HasLineOfSight(InteractiveComponent i);
}
```

La boucle de base est unique : itérer `GetCandidates()`, appeler `Detect`, `Indicated` alimente l'ensemble
indiqué, `Interactible` l'ensemble éligible, le meilleur `Score` devient le focus.

### Pourquoi `GetCandidates` reste surchargeable

Un prédicat seul ne suffit pas : pour appeler `Detect` chaque frame il faut une liste sur laquelle boucler,
et cette liste est précisément ce qui varie. Un détecteur d'area n'a pas d'opinion sur un objet qu'il n'a
jamais vu entrer — il a un ensemble, pas une fonction. Le défaut (registre global) couvre les détecteurs
qui n'ont pas de source propre, ce qui est le cas de la majorité.

### Pourquoi un enum et pas un booléen

Il y a deux paliers aujourd'hui : `IndicationArea` (large, « il y a un truc là-bas ») et `InteractionArea`
(serré, « tu peux agir »). Le Presenter dépend des deux.

Le critère pour ajouter un palier : **est-ce que l'interacteur se comporte différemment ?**
`Interactible` décide de l'éligibilité au focus, de la validité d'une commande et de l'enregistrement
auprès de la cible. `Indicated` décide qu'un widget existe. Un palier « proche / moyen / loin » ne change
rien à ce que fait l'interacteur : c'est du visuel, il appartient au widget, alimenté par la `Distance`
exposée dans la présentation (voir
[`presentation-progress-and-distance.md`](./presentation-progress-and-distance.md)). L'enum reste donc à
trois valeurs, et la soupape pour les cas custom est de la donnée, pas un palier de plus.

## Client / serveur

Le détecteur ne tourne en entier que sur le client propriétaire, comme le `_Process` actuel gardé par
`IsLocallyControlled`. Le serveur n'appelle que `Detect` sur une cible unique, à la place du
`IsWithinInteractionRange` d'aujourd'hui. **Deux rythmes, un seul code** : la divergence client/serveur
devient impossible par construction, ce qui est tout l'enjeu d'une couche remplaçable.

### La règle qui en découle : fenêtre, jamais test de collision

Un test d'angle est une **fenêtre** : 30° de tolérance absorbent les ~100 ms entre le clic du client et la
validation serveur, qui voit une transform un peu périmée mais toujours dans la fenêtre. Un cast est
**binaire** : un joueur qui tourne la souris à vitesse normale a bougé de plusieurs degrés en 100 ms,
assez pour que le cast du serveur manque une cible que le client visait parfaitement. Le refus est alors
dû au ping seul, et il est invisible à débugger.

D'où l'invariant : **le cast appartient à `GetCandidates` (une source, cliente), `Detect` reste toujours
une fenêtre tolérante.** Le serveur ne rejoue jamais le cast. Rien n'interdit à la fenêtre serveur d'être
plus large que celle du client.

### Le détecteur est obligatoire

Un interacteur sans détecteur assigné ne détecte rien, et aucun fallback implicite n'est fourni : deviner
le modèle de détection voulu par le jeu est exactement ce que ce chantier refuse de faire. C'est une
erreur de configuration, signalée par l'`InteractionValidator` et par un `GD.PushError` au `_Ready`, comme
`InteractionAction` le fait déjà pour son `Definition` et son `Executor`.

## Validation continue pendant une exécution

Le §18.1 promet que portée et LOS restent validés pendant toute l'exécution. C'est trop fort : ça dépend
du gameplay, et il y a **deux axes indépendants** que le modèle actuel confond.

- **Soutenue par l'input** : relâcher annule. C'est `CancelOnInputReleased`, déjà porté par la Definition.
- **Soutenue par la présence** : sortir de portée ou perdre le LOS annule.

Le premier axe implique le second — tenir la touche et partir doit annuler — mais l'inverse est faux :
« reste près du terminal pendant le download » est un channel sans touche enfoncée. `CancelOnInputReleased`
ne peut donc pas servir de discriminant à lui seul.

Les deux cas à couvrir :

| Cas | Exemple | Perdre le LOS ou la portée |
| --- | --- | --- |
| Action longue soutenue | hack en tenant `E` | annule |
| One-shot déclenchant un processus long | machine qu'on lance et qu'on quitte | n'annule rien |

Le second **ne fonctionne pas aujourd'hui** : `InteractionInteractor.RemoveInteractive` appelle
`CancelOwnedExecutions` sans condition dès que l'interacteur quitte l'`InteractionArea`, donc une porte à
moitié ouverte revient à son état annulé si le joueur s'éloigne. C'est le comportement observable de
`LongActionExample`. Ce chantier ne fait donc pas qu'ajouter le LOS, il change ce comportement.

Décidé : une propriété virtuelle `RequiresInteractorPresence` sur `InteractionActionExecutor`, dans la
même veine que `ComputeInteractionDuration` — seul l'executor sait si son exécution est un channel lié au joueur ou
un processus lié au monde. Une action dont la Definition déclare `CancelOnInputReleased` l'implique
d'office.

Valeur par défaut à `true`, ce qui préserve le comportement actuel et couvre la majorité des cas : un
executor de hack qui oublierait le drapeau garde le joueur devant sa cible, alors que le défaut inverse le
laisserait partir au milieu d'un hack — un bug de gameplay silencieux. On décoche la propriété pour les
cas où l'exécution longue n'est qu'un état ou une transition d'état du monde : la machine qu'on lance, la
porte qui finit de s'ouvrir, le processus qui charge pendant que le joueur est déjà ailleurs.
`TransitionStateInteractionExecutor` gagne donc un export pour ça, puisqu'il sert précisément les deux
usages.

Côté serveur, la validation continue se réduit à appeler `Detect(target) == Interactible` par frame, mais
**uniquement pour les exécutions soutenues**. Le coût est donc borné par le nombre de channels en vol, pas
par le nombre de candidats. Le contrat tient sans exception : le serveur n'appelle jamais `GetCandidates`,
donc même un détecteur d'aim, dont la source est cliente, se valide correctement — sa fenêtre d'angle et
son LOS sont évaluables côté serveur.

## Les détecteurs

```csharp
// A — comportement actuel, zéro migration de scène.
public partial class AreaInteractionDetector : InteractionDetector
{
    // Possède sa source : les signaux enter/exit des areas alimentent deux ensembles.
    protected override IEnumerable<InteractiveComponent> GetCandidates() => _tracked;

    public override InteractionDetectionKind Detect(InteractiveComponent i)
    {
        if (!_indicated.Contains(i) && !_candidates.Contains(i)) return None;
        if (!HasLineOfSight(i)) return None;
        return _candidates.Contains(i) && IsWithinRange(i, MaxDistance, MaxAngle)
            ? Interactible : Indicated;
    }
}

// C — pas de physique pour la découverte, portée authorable par objet.
public partial class ProximityInteractionDetector : InteractionDetector
{
    // Pas de GetCandidates : le registre par défaut suffit.
    public override InteractionDetectionKind Detect(InteractiveComponent i)
    {
        if (!HasLineOfSight(i)) return None;
        if (IsWithinRange(i, i.InteractionRadius, MaxAngle)) return Interactible;
        return IsWithinRange(i, i.IndicationRadius, 180.0f) ? Indicated : None;
    }
}

// D — le cast est une source, pas un filtre.
public partial class AimInteractionDetector : InteractionDetector
{
    protected override IEnumerable<InteractiveComponent> GetCandidates() => _lastCastHits;
    protected override float Score(InteractiveComponent i) => AngleTo(i);  // viser gagne sur être près
    public override InteractionDetectionKind Detect(InteractiveComponent i) => /* fenêtre + LOS */;
}
```

Conséquence d'authoring : avec le détecteur de proximité, la portée devient un `float` sur la cible. Un
objet qui veut une **forme** plutôt qu'un rayon ne revient pas à bricoler — il utilise le détecteur
d'area, qui est fait pour ça. Le choix se fait par scène, et même par interacteur.

## Line of sight

Le LOS n'est pas un détecteur, c'est un **prédicat de la classe de base** disponible pour tous. Y compris
pour le détecteur d'aim, dont le focus l'obtient gratuitement (le cast s'arrête sur le mur) mais dont
l'ensemble **indiqué** en a besoin : un objet à gauche, indiqué, derrière un mur, ne doit pas être indiqué.

- **Layer d'occlusion dédiée** (géométrie de niveau), pas « tout ». Sinon un objet d'un tas de loot occlut
  l'ancre de son voisin — physiquement correct, gameplay faux.
- Ray du `ViewOrigin` vers l'ancre, en excluant le corps de l'interacteur et les colliders de la cible.
- Évalué en `_PhysicsProcess` : l'accès au `DirectSpaceState` hors frame de physique est fragile dès que
  la physique est threadée.
- Hystérésis sur le résultat, sinon l'indication clignote derrière un poteau.

Une Area découpée à la main pour ne pas traverser un mur reste un outil légitime — c'est un volume de
visibilité authoré, gratuit et totalement contrôlé, et il permet même d'autoriser l'interaction à travers
une grille. Ce qu'il ne fait pas : les occluders **dynamiques** (porte qui s'ouvre, destructible, loot qui
roule), et il ne compose pas — une pièce à quatre murs, c'est quatre découpes par objet, et c'est du
savoir invisible porté par le level designer. Le LOS ne supprime pas ce contrôle, il évite d'avoir à
l'exercer dans la grande majorité des cas.

## Coût

Mesuré au bon ordre de grandeur, pas au ressenti : un `intersect_ray` coûte de l'ordre de la microseconde
sur une scène de cette taille. Le pire cas réaliste — un dedicated server, 10 joueurs, 15 candidats
chacun, soit ~150 rays par frame — reste petit. **La perf n'est pas le critère de décision de ce
chantier** ; la testabilité et le choix laissé au designer le sont. Si un chiffre réel est nécessaire, un
spike suffit : une scène de 200 objets, 150 rays par frame, lecture du profileur.

## Ce qui bouge

Sortent de `InteractionInteractor` : `_interactiveCandidates`, `_indicatedInteractives`,
`RecalculateFocusCore`, `CalculateInteractionScore`, `IsWithinInteractionRange`, et les callbacks d'area.

Restent : `_focusedInteractive`, les signaux, `RegisterInteractor` / `UnregisterInteractor`, le push de
statut, `GetRelevantInputs`, le geste, les commandes, les RPC, les exécutions.

**L'API publique de l'interacteur ne change pas.** C'est le test que le joint est au bon endroit : si
ajouter un détecteur oblige à modifier l'interacteur, le découpage est faux.

## Test

Un détecteur factice qui retourne un ensemble fixe permet de tester focus, présentation et commandes
**sans physique ni areas**. C'est un gain de testabilité indépendant de la valeur produit du chantier.

## Questions ouvertes

- Le registre : un groupe Godot est l'idiome §25, mais `GetNodesInGroup` alloue à chaque appel. Une liste
  statique interne au plugin est plus honnête et reste portable GDExtension.
  **Tranchée provisoirement par le spike**, dans le sens de la liste statique interne : le détecteur de
  proximité en a eu besoin, et un `GetNodesInGroup` par frame allouait pour rien. `GetCandidates` reste
  `abstract` — le registre est un membre statique de `InteractiveComponent`, pas un défaut de la classe de
  base, donc rien n'est figé si on veut le groupe Godot plus tard.
- Qui remplit la `Distance` de la présentation : le détecteur (il la calcule déjà pour son score) ou un
  accesseur public sur l'interacteur ?
  **Hors périmètre**, elle appartient à [`presentation-progress-and-distance.md`](./presentation-progress-and-distance.md).
  Ce chantier ne la ferme pas mais l'oriente : les origines vivent maintenant sur le détecteur, donc
  l'accesseur ne peut plus être sur l'interacteur seul.
- Une exécution `RequiresInteractorPresence == false` garde-t-elle un lien vers son interacteur ? Elle lui
  survit par définition — le joueur part, et peut aussi se déconnecter ou quitter l'arbre, ce que
  `CancelOwnedExecutions` traite aujourd'hui comme une annulation. La réponse la plus propre est sans doute
  qu'elle devienne possédée par le monde dès son démarrage, ce qui règle du même coup qui prédit sa
  progression : personne.
  **Tranchée dans ce sens, et de la façon la plus simple possible** : l'interacteur ne l'enregistre pas
  du tout. Ne pas la suivre *est* ce qui la rend possédée par le monde, et aucune des trois voies
  d'annulation (fenêtre perdue, input relâché, sortie d'arbre) n'a alors de prise sur elle.

## Impact roadmap

La Task 10 « LOS authoritative validation » devient « détecteur remplaçable + LOS » : le LOS n'est plus le
sujet, il est devenu un prédicat d'un sujet plus gros. Livrer d'abord le joint et
`AreaInteractionDetector` à comportement identique (zéro migration de scène), puis le LOS comme prédicat.
Les détecteurs de proximité et d'aim sont des ajouts purs, sans toucher au framework — et c'est
exactement le test qui prouve que le joint est bien placé.

## État

**Livré, LOS compris**, sauf les détecteurs de proximité / de visée qui restent des spikes.
`runtime/detection/` porte `InteractionDetector` et `AreaInteractionDetector` ;
`InteractionDetectionKind` vit dans `InteractionTypes.cs` aux côtés de `InteractionUnavailableKind`.
Voir la Task 10 de [`interaction.md`](../interaction.md) pour le détail.

Quatre décisions ont été prises pendant l'implémentation, dont une s'écarte de la lettre de ce
document.

1. **Le push des overlaps passe par deux virtuels no-op de la classe de base**
   (`OnEnteredTargetArea` / `OnExitedTargetArea`, plus `Forget`). Les areas appartiennent à la cible,
   seule capable de les posséder, donc c'est elle qui pousse — sur tous les pairs, ce qui donne au
   serveur le même overlap qu'au client sans dérouler la boucle. Un type-check sur
   `AreaInteractionDetector` côté composant aurait gardé la base pure mais n'aurait pas été
   retranscriptible en GDExtension (§25). `Forget` existe parce qu'une area ne rapporte jamais l'overlap
   qu'elle perd en étant libérée : c'est déjà pour cette raison que `InteractiveComponent._ExitTree`
   prévient ses interacteurs, et il prévient maintenant aussi ceux dont seul le détecteur la tient.
2. **`Detect` suit le snippet à la lettre**, y compris pour une cible dans l'area d'interaction mais
   hors fenêtre : elle est `Indicated`, `IndicationArea` authorée ou pas. Une première version stricte
   renvoyait `None` par souci de « comportement identique » — c'était le mauvais invariant à préserver.
   Le bon est qu'un objet ne disparaît pas parce qu'on tourne la tête : perdre la fenêtre coûte le
   focus, jamais l'existence. Les paliers sont donc monotones dans les deux sens.
3. **Les paliers sont cumulatifs pour l'indication.** `Interactible` implique `Indicated` côté
   interacteur : deux cibles utilisables et non focusées gardent leur widget. L'enum reste exclusif
   comme valeur de retour, la cumulativité est dans la lecture qu'en fait l'interacteur.
4. **`ViewOrigin`, `InteractionOrigin` et `DistanceScoreCoefficient` déménagent sur le détecteur.**
   C'est la seule façon pour `IsWithinRange` — et demain `HasLineOfSight` — d'être des helpers de la
   classe de base. L'API de commande de l'interacteur ne change pas, mais ses exports de détection, si :
   la scène du personnage migre d'une ligne.

La validation continue est livrée avec le joint et non avec la présence, parce qu'elle *remplace* le
`RemoveInteractive → CancelOwnedExecutions` que le joint supprime. Le second commit n'ajoute donc que
l'axe de sortie (`RequiresInteractorPresence`).

Les détecteurs C et D existent en **spike** (`ProximityInteractionDetector`, `AimInteractionDetector`) :
un smoke test chacun, à garder ou à jeter après essai en scène. Ils ont été écrits **sans modifier une
ligne du framework** — seuls le registre et les deux rayons par cible s'ajoutent à l'existant — ce qui est
la vérification que ce document annonçait pour le placement du joint. Deux points relevés à l'écriture :

- Le LOS manque déjà. Sans lui, C rend une cible interactible à travers un mur ; c'est le prédicat qui
  fait la différence entre « portée » et « portée utile ».
- Chez D, l'ensemble **indiqué** vient du cast élargi et non du registre. Le doc supposait qu'il aurait
  besoin du LOS pour ne pas indiquer un objet derrière un mur ; avec un cast comme source, ce que le cast
  arrête est déjà exclu, et le LOS ne redevient nécessaire que si la source s'élargit au registre.
  **Faux, corrigé en livrant le LOS** : le `ShapeCast3D` de D a `CollideWithBodies = false` — il ne
  rapporte que des areas, donc un mur ne l'arrête pas du tout et D visait à travers. Le snippet du doc
  (« fenêtre + LOS ») était le bon ; c'est la note du spike qui se trompait.

### LOS — livré comme prédicat

Cinq décisions, dont aucune ne s'écarte du doc.

1. **Le ray vit dans la frame de physique, la réponse pas forcément.** Le cache porte le résultat rapporté
   et se rafraîchit en `_PhysicsProcess` pour toute cible interrogée depuis le dernier passage (rétention
   0,5 s, pour que la boucle survive à un framerate de rendu inférieur au tick physique). Une cible
   **encore inconnue** est castée sur le champ : le pair autoritaire valide une commande one-shot hors de
   toute frame de physique, et répondre « occlus jusqu'à la prochaine » refuserait une commande légitime
   pour une raison invisible — exactement ce que la règle « fenêtre, jamais test de collision » interdit.
   Le coût courant reste un lookup de dictionnaire par candidat par frame.
2. **L'hystérésis est à sens unique.** Regagner la vue est immédiat, la perdre demande
   `LineOfSightLossGrace` (0,15 s) de perte continue. Symétrique, elle aurait fait réapparaître un objet
   150 ms trop tard ; asymétrique, elle supprime le clignotement derrière un poteau *et* absorbe le ping
   comme le fait la fenêtre d'angle.
3. **Perdre le LOS renvoie `None`, pas `Indicated`.** C'est l'asymétrie assumée avec la fenêtre de visée :
   perdre la fenêtre veut dire qu'on regarde ailleurs et l'objet est toujours là ; perdre le LOS veut dire
   qu'il n'y a rien à regarder. Le mur retire donc aussi l'indication, ce que ce document demandait
   explicitement pour l'objet indiqué à gauche derrière un mur.
4. **Un seul mask, sur le détecteur : c'est l'occluder qui décide.** `OcclusionMask` (défaut layer 2,
   nommée « Occluder » dans `project.godot`) est le réglage de projet, et rien n'est authoré sur la cible.
   Une première version donnait à `InteractiveComponent` un override de mask et un `IgnoreOcclusion`, au
   motif que le prédicat retirait à l'area découpée à la main sa promesse d'autoriser l'interaction à
   travers une grille. C'était le mauvais endroit : occluder est une propriété de la **géométrie**, pas de
   la cible. Un mur porte la layer, une grille qu'on veut traverser ne la porte pas, et aucun objet n'a
   d'exemption à déclarer — deux exports de moins sur chaque interactible, et l'intention lisible là où
   elle se voit, dans la scène de niveau. Le seul cas que l'override achetait — la même vitre qui occlut
   la cible A mais pas la cible B — n'a pas de client aujourd'hui, et le rajouter serait additif. Un mask
   à zéro désactive le prédicat, ce qui rend le comportement d'avant le chantier disponible sans branche
   de code.
5. **La cible n'est jamais son propre occluder.** Plutôt qu'énumérer les RID de ses colliders, le ray
   regarde à qui appartient ce qu'il a touché (`InteractiveComponent.OwnsCollider`) : s'arrêter sur la
   géométrie de la cible, c'est l'avoir atteinte. Même effet, robuste à une ancre authorée dans le mesh
   qui la porte. Le corps de l'interacteur, lui, est exclu par RID — c'est le premier ancêtre
   `CollisionObject3D` du détecteur, et il est posé sur le ray qu'il tire.

Côté scène, la géométrie de niveau de `test_world` (et le mur mobile du `LeverWall`, qui est
précisément l'occluder dynamique que ce document invoquait) passe en `collision_layer = 1|2`.
`facility_blockout` n'est pas migré : c'est un blockout, ses murs n'occluent rien tant qu'ils ne portent
pas la layer.
