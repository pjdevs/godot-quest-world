# Game session replication findings

- The world and travel RPC are both reliable, but the client can observe the native world spawn before
  `BeginTravel`. A client that is already locally ready must re-send `TravelReady` when `BeginTravel`
  arrives so the server barrier cannot remain pending.
- `MultiplayerSpawner.Spawned` is a local lifecycle signal and is not a safe source of current state for
  a persistent integration node. Re-scan the current world spawner root after binding so a Character
  that spawned before the signal subscription is still configured for authority and possession.
- A `PlayerWorldReady` emission is server semantic state, not a client signal. The server emits it after
  the global barrier or after a validated late-join acknowledgement; clients only report local readiness.
