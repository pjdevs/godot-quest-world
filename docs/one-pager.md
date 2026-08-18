# Quest World

### Narrative Systemic Prototype — Godot Vertical Slice

**Genre** — First-person narrative systemic puzzle / light immersive sim\
**Format** — Démo courte et rejouable (\~30–60 min, cible à préciser)\
**But projet** — Tester en conditions réelles les frameworks QuestWorld : **Interaction, Stateful, Inventory, Dialog, Quest/Flow, Save**, puis potentiellement Multiplayer. Le jeu doit rester suffisamment cohérent et fun pour que ces systèmes soient éprouvés comme de vraies mécaniques, pas comme une tech demo.

> **High Concept**\
> Une recrue entre dans une installation expérimentale pour récupérer un prototype selon une procédure de routine. Une crise énergétique transforme la mission en problème de triage : le joueur doit reconfigurer physiquement la station, choisir ce qui mérite de rester alimenté et vivre avec les conséquences — avant de comprendre que le « prototype » qu’il tente de sauver remet en cause la notion même de continuité d’une personne.

---

## PLAYER FANTASY

**Comprendre un système → le reconfigurer → exploiter son nouvel état → découvrir les conséquences → réévaluer ses choix.**

Le joueur n'ouvre pas une succession de portes : **il modifie le système qui produira ses prochains problèmes.**

Pas de choix abstraits `A / B`.\
Les décisions sont faites **dans le monde** : breakers, switches, batteries, portes, terminaux, objets transportés.

---

## DESIGN PILLARS

### 1 — THE WORLD IS A SYSTEM

Tout interactable répond aux mêmes règles cohérentes.\
Une porte n’a pas de script narratif spécial : elle dépend de `MainPower`, `Security`, d’un état ou d’une ressource.

---

### 2 — ACTIVATION ≠ PROGRESS

Toute action utile peut avoir un coût.

Rétablir le courant ouvre la blast door… mais ferme une porte coupe-feu derrière le joueur.

**Chaque choix important possède :**

- une conséquence immédiate, visible/compréhensible ;
- une conséquence différée, parfois narrative et irréversible.

---

### 3 — INFORMATION IS A RESOURCE

Les Archives ne donnent presque aucun pouvoir immédiat : elles permettent de **comprendre les conséquences avant ou après les avoir provoquées**.

Ne pas explorer reste viable.\
Le joueur curieux ne découvre pas « la vraie histoire » : il comprend **plus profondément la même histoire**.

---

### 4 — NO CORRECT AUTHORITY

Les interlocuteurs sont de bonne foi mais ont des priorités incompatibles.

- **Ingénieur** — préserver la continuité active / le cooling.
- **Directrice** — récupérer le prototype, privilégier les états stables et la procédure.
- **Instructeur** — faire sortir les personnes présentes avant tout.

Personne n’est secrètement « le méchant qui ment ».

---

### 5 — TOGETHER, APART (COOP SYSTEMIC EXTENSION)

Le jeu est conçu pour **1 à 2 joueurs sans séparation de design**.

**Le second joueur augmente les possibilités, pas les prérequis.**

La coop permet :

- être à deux endroits simultanément ;
- observer immédiatement les conséquences d’une action distante ;
- transporter plusieurs ressources ;
- coordonner des interactions ;
- partager des informations partielles ;
- se mettre mutuellement dans la merde.

Mais **aucun verrou critique ne doit simplement dire “2 players required”**.

Le solo doit toujours disposer d’une solution systémique équivalente, même si elle est plus lente, plus coûteuse ou emprunte une autre route.

---

# CORE LOOP

**Explore**\
↓\
Identifier une dépendance / une route bloquée\
↓\
**Reconfigure** la station : énergie, état, objet, bypass\
↓\
Le monde change physiquement\
↓\
**Traverse / exploite** le nouvel état\
↓\
Découvre ressource, information ou conséquence\
↓\
**Décide ce qui peut être sacrifié**\
↺

Le plaisir vient progressivement de la maîtrise du graphe :

> « Si je coupe Transit, je récupère assez de puissance pour Security ; je passe cette porte, récupère ma batterie, puis je peux remettre Cooling. »

---

# THE STATION — POWER AS WORLD STATE

La station manque volontairement de capacité : **tout ne peut pas fonctionner simultanément.**

Circuits principaux, à garder peu nombreux et lisibles :

- **TRANSIT** — portes, ascenseurs, accès directs.
- **ARCHIVES** — logs scientifiques, administratifs et personnels.
- **SECURITY** — caméras, localisation, certaines portes / fail-safes.
- **PROTOTYPE / COOLING** — objectif officiel ; conséquences critiques si sacrifié.

Les breakers reconfigurent réellement le level design.

**Exemples**

- Archives OFF → contenu inaccessible maintenant, information potentiellement perdue plus tard.
- Security OFF → changement immédiat de portes/caméras ; plus tard, impossible de localiser quelqu’un.
- Cooling OFF → température/alarme immédiate ; plus tard, perte du processus actif et restauration possible.
- Transit OFF → route principale perdue ; oblige à trouver maintenance hatch / détour / ressource.

---

# COOP SYSTEM EXAMPLES (EMERGENT ONLY)

### Cause / conséquence distribuée

A est dans Electrical.\
B est devant une blast door.

A coupe Security.

Chez B :

*CLUNK.*

> « Euh… ma porte vient de se verrouiller. »

A :

> « Ah. »

---

### Information spatialement séparée

A lit dans Archives :

> `Emergency cooling can be rerouted through Environmental Bus.`

B est devant le panneau :

`ENVIRONMENTAL BUS — 8 kW`

A doit lui transmettre l’info.

Solo : lecture → déplacement → action.\
Coop : discussion → coordination → action.

---

### Séparation systémique naturelle

Un joueur coupe Transit pour alimenter Cooling.

Une porte fail-safe se ferme.

Les joueurs se retrouvent séparés sans script spécifique.

---

# PROGRESSION — VERTICAL SLICE

### 01 — PROCEDURE

Mission annoncée comme banale : **entrer → récupérer le prototype → ressortir.**

Porte entrouverte. Bouton :

`OPEN BLAST DOOR`\
**NO POWER**

Interaction bloquée par règle système, sans scripting spécifique.

Le joueur trouve le générateur → **interaction longue** → démarrage.

Lumières, ventilation, écrans, nouveaux interactables.

Retour au bouton → blast door ouverte.

**CLANG derrière lui : fire door verrouillée.**

Premier contrat avec le joueur :

> changer l’état du monde a des conséquences.

---

### 02 — FIRST TRIAGE

Premier panneau électrique.

Pas assez de puissance pour tout alimenter.

Le joueur choisit librement ce qui lui semble utile selon les informations présentes.

**Archives en premier** → accès immédiat à du contexte.\
**Transit en premier** → progression spatiale plus simple.\
**Cooling en premier** → respect de la procédure officielle.

Aucune option n’est présentée comme « mauvaise ».

---

### 03 — THE BATTERY

Plus loin : **batterie auxiliaire / power cell**.

Le jeu suggère naturellement de l’utiliser pour poursuivre via Transit.

Mais le joueur peut :

- l’utiliser pour avancer ;
- la brancher ailleurs ;
- revenir sur ses pas ;
- réalimenter partiellement les Archives ;
- la détourner comme outil systémique.

Premier moment où l’Inventory devient une **exception physique aux règles du réseau**, pas une collection de clés.

---

### 04 — CONTRADICTORY ORDERS

La crise s’aggrave. Les voix commencent à se contredire.

> — Coupe le labo, on n’a plus besoin de cette section.\
> — Non ! Si tu coupes le labo, le confinement local saute.\
> — Le confinement tient sur batterie.\
> — Pendant huit minutes.\
> — On sera sortis avant.\
> — Vous ne savez même pas où est le prototype.

Les dialogues fournissent des **intentions et informations**, jamais un waypoint vers « le bon choix ».

---

### 05 — SOMETHING COGNITIVE

Critical path obligatoire :

`COGNITIVE PROCESS RECOVERY`\
`CURRENT PROCESS: DEGRADED`\
`LAST STABLE STATE: XX:XX`

Le joueur comprend progressivement que le prototype n’est probablement pas un simple objet.

---

### 06 — CONTINUITY FAILURE

Cooling tombe — par choix ou événement systémique.

`PRIMARY PROCESS LOST`\
`RECOVERY INSTANCE ONLINE`

Une voix revient :

> « J’ai perdu la télémétrie pendant une seconde. Où en êtes-vous ? »

Puis :

> — Ce n’est pas la même instance.\
> — Il est opérationnel. On continue.

Le concept philosophique entre dans la critical path.

---

### 07 — EXTRACTION

Le « prototype » n’est pas simplement dans une boîte.

C’est un **processus cognitif distribué** utilisant plusieurs systèmes de la station.

Selon les états :

- processus actif instable ;
- snapshot stable mais incomplet ;
- plusieurs continuités valides ;
- mémoire altérée par les systèmes sacrifiés.

Dernier problème :

`MULTIPLE VALID CONTINUITY STATES DETECTED`

Le dernier puzzle est philosophique :

> qu’est-ce que « sauver le prototype » signifie réellement ?

---

# THE PROTOTYPE — DESIGNER SPOILER

Technologie hard-SF crédible : **continuité cognitive multi-substrat**.

Le système capture suffisamment d’état pour restaurer un processus avec mémoire, personnalité et continuité subjective.

Le problème n’est pas l’échec.

**C’est le succès.**

Un snapshot restauré se considère comme la continuité légitime.

Si plusieurs instances existent :

> laquelle est la personne ?

---

# ARCHIVES — OPTIONAL DEPTH

Les logs ne sont jamais obligatoires.

Ils servent à :

- recontextualiser une mécanique ;
- complexifier une décision future ;
- révéler des contradictions.

Le joueur peut finir sans eux.\
Mais il comprendra moins bien *ce qu’il a réellement manipulé*.

---

# FRAMEWORK → GAME DESIGN

**Interaction** → manipuler physiquement le système.\
**InteractionRule** → rendre les dépendances cohérentes et data-driven.\
**Stateful** → mémoriser les conséquences dans le monde.\
**Inventory** → transporter des exceptions aux contraintes.\
**Dialog** → informations contradictoires et réactions aux états réels.\
**Quest / Flow Graph** → orchestrer une histoire non linéaire.\
**Save** → rendre les sacrifices persistants.\
**Multiplayer (1–2 players)** → coordination émergente sur un même système.

---

# LEVEL & NARRATIVE RULES

- Le monde doit être reconfigurable, pas scripté.
- Les systèmes doivent se croiser (pas de silos).
- Le backtracking doit être utile.
- Pas de choix UI abstraits.
- Pas de lore obligatoire.
- Les conséquences doivent être traçables.
- Le mystère part du technique vers l’existentiel.
- La coop ne crée jamais de contenu exclusif : elle révèle des stratégies.

---

# TONE / DIRECTION

**Début** — installation technique crédible, procédure routinière.\
**Milieu** — incident systémique, urgence contenue, contradictions humaines.\
**Fin** — malaise existentiel produit par une technologie parfaitement rationnelle.

---

### Références

- Prey (Arkane) — station interconnectée et systemic design
- SOMA (Frictional Games) — identité et continuité
- Cyberpunk 2077 — construct de personnalité
- Messy Potions One-Pager — densité de design document

---

## SCOPE PROMISE

Quest World n’a pas besoin de prouver qu’il peut contenir beaucoup de contenu.

La démo doit prouver qu’un petit nombre de systèmes génériques peuvent produire un monde cohérent, des choix émergents et une histoire réactive.

Si une feature ne renforce pas :

**Reconfiguration → Conséquence → Compréhension → Nouveau choix**

elle n’est pas prioritaire.
