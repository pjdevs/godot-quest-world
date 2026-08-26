# Multiplayer.IsServer() requires an assigned peer

- `MultiplayerApi.IsServer()` is `get_unique_id() == 1`, and `get_unique_id()` with no
  `MultiplayerPeer` assigned pushes `No multiplayer peer is assigned. Unable to get unique ID.` and
  returns `0`. So the call does not merely log: it **answers no**.
- Every `if (!Multiplayer.IsServer()) return;` guard therefore refuses itself in a peerless run, and
  during a `_ExitTree` after a session closed. Symptoms are a game that silently stops mutating state,
  plus one error per frame from a `_Process` guard.
- Guard the access instead: `Multiplayer is null || Multiplayer.MultiplayerPeer is null ||
  Multiplayer.IsServer()`. Offline is authority — a game without a session is its own server. A node
  outside the tree has a null `Multiplayer`, which is also how a test reaches that branch.
- Found in `interaction_plugin`, `stateful_plugin` and `LeverWall` at once; the guard is duplicated per
  class on purpose so the addons stay independent of each other.
