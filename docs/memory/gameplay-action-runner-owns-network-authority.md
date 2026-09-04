# Le runner possède l'autorité de ses RPC

Quand la racine d'un Character est assignée au peer joueur, `SetMultiplayerAuthority()` propage cette
autorité à ses descendants. Un `GameplayActionRunner` placé sous ce Character peut donc se retrouver
avec l'autorité du client, alors que ses acquittements `ClientActionStarted`, `ClientActionCompleted`,
etc. sont des RPC `Authority` envoyés par le serveur.

La correction doit vivre dans `GameplayActionRunner` : il applique `SetMultiplayerAuthority(ServerPeerId)`
à son propre nœud pendant `_Ready`, et l'`InteractionInteractor` délègue simplement `IsLocallyControlled`
au runner. Changer l'autorité de l'interactor ne corrige pas un runner frère et mélange la détection
spatiale avec le transport réseau. Cette frontière couvre autant une action possédée par le joueur
(`drop battery`) qu'une action obtenue par focus (`take battery`), car les deux passent par le même
pipeline du runner.

Le test `RunnerClaimsServerAuthorityWhenInheritedFromPlayerRoot` reproduit l'héritage d'autorité du
Character et protège cette responsabilité.
