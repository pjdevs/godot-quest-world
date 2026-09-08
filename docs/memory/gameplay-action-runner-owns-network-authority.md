# Le runner possède l'autorité de ses RPC

Quand la racine d'un Character est assignée au peer joueur, `SetMultiplayerAuthority()` propage cette
autorité à ses descendants. Un `GameplayActionRunner` placé sous ce Character peut donc se retrouver
avec l'autorité du client, alors que ses acquittements `ClientActionStarted`, `ClientActionCompleted`,
etc. sont des RPC `Authority` envoyés par le serveur.

`GameplayActionRunner` continue d'appliquer `SetMultiplayerAuthority(ServerPeerId)` à son propre nœud
pendant `_Ready`, et l'`InteractionInteractor` délègue simplement `IsLocallyControlled` au runner.
Cette reprise reste une protection locale idempotente ; elle ne doit pas compenser une configuration
tardive de la racine du Character.

La configuration primaire appartient à `PlayerCharacterSpawnManager` : `peer_id` et le transform local
sont transmis au `MultiplayerSpawner`, qui dérive le nom, instancie le Character et applique son
`OwnerPeerId` ainsi que son authority racine avant l'entrée dans l'arbre. Un appel récursif à
`SetMultiplayerAuthority()` après `_EnterTree()`/`_Ready()` écraserait à nouveau les authorities
serveur de `GameplayActionRunner`, `CarryComponent` et des synchronizers. Cette frontière couvre autant
une action possédée par le joueur (`drop battery`) qu'une action obtenue par focus (`take battery`),
car les deux passent par le même pipeline du runner.

Le test `RunnerClaimsServerAuthorityWhenInheritedFromPlayerRoot` reproduit l'héritage d'autorité du
Character et protège cette responsabilité.
