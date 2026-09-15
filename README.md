# Godot Mono cleanup-crash MRP

Cette branche est un projet Godot Mono autonome dédié à la reproduction du
crash de shutdown observé dans Quest World. Le MRP est volontairement à la
racine du dépôt ; il ne dépend plus de fichiers situés dans un dépôt parent.

## Environnement

- macOS
- Godot `4.7.2.stable.mono.official.ed1daf0bf`
- .NET `10.0.401`

## Vérifier la baseline

Depuis la racine :

```sh
dotnet build godot_cleanup_crash.csproj --nologo
/Applications/Godot_mono.app/Contents/MacOS/Godot \
  --headless --path . \
  --script res://ProbeExact.gd --quit-after 1
```

`ProbeExact.gd` charge synchroniquement les trois scènes réduites puis quitte.
Il n'instancie pas les scènes et n'exécute pas de test GdUnit. La séquence par
défaut est :

1. `mrp/MinimalBaseGroundedActionPair.tscn`
2. `mrp/LeverScriptOnly.tscn`
3. `mrp/LongActionWithOfferDetails.tscn`

Une liste séparée par des virgules peut être passée après `--` pour tester une
autre combinaison de scènes :

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot \
  --headless --path . \
  --script res://ProbeExact.gd --quit-after 1 -- \
  'res://mrp/MinimalBaseGroundedActionPair.tscn,res://mrp/LeverScriptOnly.tscn'
```

## Résultat de l'investigation crash

La baseline aplatie reste stable. Le positif observé dans le projet complet
nécessitait le graphe C# réseau/session/world complet, l'assembly de test et la
scène `test_world.tscn` complète ; le MRP minimal n'a pas permis d'isoler une
cause unique.

Les tests ont confirmé que `.godot/` n'est pas indispensable et qu'aucune
correction de code production n'a été identifiée. L'investigation est donc
mise en pause ici ; les fichiers `.uid` et `.godot/` restent générés localement
et ignorés.

Le crash observé contient typiquement :

```text
handle_crash: Program crashed with signal 11
std::__1::recursive_mutex::lock()
FinalizerThread::FinalizeAllObjects()
```

Le code de retour peut être `0` ou `134`; il faut classifier la sortie sur la
présence de `handle_crash: Program crashed` plutôt que sur le seul code retour.
