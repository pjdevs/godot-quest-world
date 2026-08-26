# Un serveur et deux clients dans un seul process Godot

- Godot associe une `MultiplayerApi` à un **sous-arbre**, pas au process : `tree.SetMultiplayer(api,
  rootPath)`. Trois pairs peuvent donc vivre dans un même `SceneTree` — une branche `Server`, une
  `ClientA`, une `ClientB` — chacune avec sa propre `MultiplayerApi.CreateDefaultInterface()` et son
  propre `ENetMultiplayerPeer` en loopback sur `127.0.0.1`. Ça marche en headless sous gdunit4 : la
  connexion des deux clients tient en une centaine de millisecondes, pompée par `SimulateFrames`.
- `node.Multiplayer` résout bien l'API de sa branche, et `((SceneMultiplayer)api).RootPath` rend le
  chemin de cette branche. C'est ce qui rend le montage utilisable : chaque pair est autoritaire ou non
  selon son API, sans que le code runtime sache qu'il partage un process.
- **Peupler chaque branche seulement après lui avoir attaché son API.** Un `MultiplayerSynchronizer` se
  lie à l'API de sa branche quand il entre dans l'arbre : ajouté avant `SetMultiplayer`, il reste lié au
  défaut sans peer et répète `The multiplayer instance isn't currently active` pour le reste du run,
  sans jamais rien répliquer. Les RPC, eux, résolvent leur nœud à l'appel et ne souffrent pas de
  l'ordre — c'est ce qui rend le symptôme trompeur : les commandes passent, l'état ne se réplique pas.
- **Les trois branches doivent être structurellement identiques.** Godot route un RPC par le chemin du
  nœud *relatif à la racine multijoueur* : `InteractorA` ne parle qu'à `InteractorA`.
- Le piège qui bloque tout : un payload applicatif qui nomme un nœud par un chemin **absolu**
  (`GetTree().Root.GetNodeOrNull(path)`). Le client envoie `/root/World/ClientA/Actor/Interactive` et le
  serveur, dans le même `SceneTree`, résout la copie du client au lieu de la sienne. Il faut nommer et
  résoudre relativement à `RootPath` — ce que `InteractionInteractor.GetNetworkPath` /
  `ResolveNetworkPath` font. Dans un jeu normal `RootPath` vaut `/root`, donc zéro changement.
- Les identifiants de pair ENet sont de grands entiers aléatoires connus seulement **après** connexion :
  tout `OwnerPeerId` doit être écrit après la boucle d'attente, pas à la construction de la scène.
- Utiliser un port différent par test et fermer les pairs en `finally` : sinon un `CreateServer` suivant
  échoue sur un port encore occupé.
- Une propriété répliquée porte **une valeur, pas un historique** : deux `SetState` dans la même frame
  n'arrivent au client que comme la dernière valeur. Un one-shot branché sur une transition que le
  client ne reçoit jamais ne joue simplement jamais — d'où la règle « la pose vient de l'état courant,
  seuls les sons et effets viennent des transitions ».
- Un pair qui rejoint en cours de session **reçoit bien la valeur courante** de chaque propriété
  répliquée, même pour un nœud statique de la scène et non spawné par un `MultiplayerSpawner`. Elle lui
  arrive par le setter normal, donc comme une transition depuis la valeur initiale locale : côté
  applicatif, rien ne distingue une arrivée d'un vrai changement, sauf à ajouter un marqueur de première
  synchronisation.
- Pour tester un late join, ajouter la branche du nouveau pair au monde déjà en cours, lui attacher son
  API **avant** de la peupler (même règle que ci-dessus), et brancher les observateurs avant d'attendre
  la poignée de main — sinon on rate exactement ce qu'on cherche à mesurer.
- Faire assert au harnais qu'il est vraiment distribué (`serverApi.IsServer()` vrai, les deux autres
  faux, et l'executor qui n'a tourné que sur l'autorité). Sans ces gardes, une régression sur l'autorité
  ferait dégénérer toute la suite en appels locaux, verte et sans valeur.
