# Un serveur et deux clients dans un seul process Godot

- Godot associe une `MultiplayerApi` à un **sous-arbre**, pas au process : `tree.SetMultiplayer(api,
  rootPath)`. Trois pairs peuvent donc vivre dans un même `SceneTree` — une branche `Server`, une
  `ClientA`, une `ClientB` — chacune avec sa propre `MultiplayerApi.CreateDefaultInterface()` et son
  propre `ENetMultiplayerPeer` en loopback sur `127.0.0.1`. Ça marche en headless sous gdunit4 : la
  connexion des deux clients tient en une centaine de millisecondes, pompée par `SimulateFrames`.
- `node.Multiplayer` résout bien l'API de sa branche, et `((SceneMultiplayer)api).RootPath` rend le
  chemin de cette branche. C'est ce qui rend le montage utilisable : chaque pair est autoritaire ou non
  selon son API, sans que le code runtime sache qu'il partage un process.
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
- Faire assert au harnais qu'il est vraiment distribué (`serverApi.IsServer()` vrai, les deux autres
  faux, et l'executor qui n'a tourné que sur l'autorité). Sans ces gardes, une régression sur l'autorité
  ferait dégénérer toute la suite en appels locaux, verte et sans valeur.
