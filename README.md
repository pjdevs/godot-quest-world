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

## État actuel

Le montage aplati compile et charge proprement la séquence positive sur la
baseline actuelle. Les références de scripts et de textures utilisent les
chemins locaux ; les anciens UID de l'autre projet ont été retirés pour que le
MRP reste reproductible après un clone propre. Les fichiers `.uid` et
`.godot/` sont générés localement et ignorés.

Le crash historique n'est pas reproduit par cette baseline aplatie : les
sources et l'assemblage externes qui le déclenchaient dans l'expérience
précédente ont été supprimés avec le projet original. La suite de
l'investigation consiste donc à réintroduire les composants ou groupes de
sources un par un dans ce projet local, en conservant le probe load-only.

Le crash à documenter, lorsqu'il est présent, se produit à la sortie du
processus et contient typiquement :

```text
handle_crash: Program crashed with signal 11
std::__1::recursive_mutex::lock()
FinalizerThread::FinalizeAllObjects()
```

Le code de retour peut être `0` ou `134`; il faut classifier la sortie sur la
présence de `handle_crash: Program crashed` plutôt que sur le seul code retour.
