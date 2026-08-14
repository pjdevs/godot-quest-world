# Network client-authority spike — workflow findings

- Les arguments applicatifs lancés après `--` doivent être lus avec `OS.GetCmdlineUserArgs()` ; `OS.GetCmdlineArgs()` ne permettait pas de distinguer correctement les options Godot et les options du projet pendant le smoke test.
- Dans `Customize Run Instances`, les arguments propres à une instance sont ignorés si `override_args` reste à `false`. Il faut activer `Override Main Run Args` pour chaque instance qui doit recevoir un mode différent (`--host`, `--client`, etc.).
- Les `peer_id` ENet des clients sont de grands entiers aléatoires, pas des index de tableau utilisables directement pour un placement spatial. Le prototype les réduit à un slot borné.
- Le `MultiplayerSpawner` doit surveiller le conteneur `Players` et le Character doit avoir un `MultiplayerSynchronizer` avec les propriétés de pose marquées `spawn = true` pour que les joueurs soient visibles aux late joins.
- Le `PlayerController` ne peut plus recevoir un `InitialPawnPath` statique dans la scène réseau : `NetworkSession` attend le `Player_<localPeerId>` spawné par le serveur puis possède uniquement ce nœud.
- Les proxies ne passent pas par `_PhysicsProcess()` car ils ne sont pas autoritaires. Toute présentation dérivée de l’état simulé local, comme l’`AnimationTree`, doit donc être appliquée séparément depuis les propriétés synchronisées (`velocity`, `NetworkIsGrounded`, etc.).
- Dans l’environnement Windows managé, `dotnet format`, `dotnet build` et `dotnet test` peuvent nécessiter une exécution élevée à cause de l’accès au `NuGet.Config` utilisateur. Les erreurs de certificat root et les fuites de ressources au shutdown Godot sont présentes dans les smokes/tests existants mais n’empêchent pas le code retour 0.
