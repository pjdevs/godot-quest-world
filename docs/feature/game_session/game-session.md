# Game Session

## Responsibility

The generic `game_session` addon sits above `network_session`. It owns persistent participant
identity, server admission, `PlayerState` lifecycle, and the persistent roots used for current-world
travel. It does not know about Characters, Pawns, teams, loadouts, match rules, or project gameplay.

## Current implementation

`GameSession` is explicitly initialized after its authored references are available. It validates the
`NetworkSession`, persistent `Players`, `PlayerStateScene`, `PlayerStateSpawner`, `WorldContainer`, and
`WorldSpawner` dependencies, then configures the player-state custom spawn function and reconciles an
already-active offline, host, or dedicated session.

`PlayerState` receives a positive `ParticipantId` and current `PeerId` before it enters the scene tree.
The server allocates participant IDs monotonically and maintains direct indexes by both identities.
Offline sessions create the local participant; dedicated servers and clients do not author a fake
participant locally.

On an active server, peer admission allocates the next participant ID and sends `{ participant_id,
peer_id }` through the persistent `PlayerStateSpawner`. The same factory constructs the state on
remote peers, so identity is initialized before `_Ready()` and derived `PlayerStateScene` roots are
supported. The server keeps both peer and participant indexes, disconnects peers refused by
`CanJoin`, and mirrors the admission intent into `MultiplayerPeer.RefuseNewConnections`; disconnects
despawn the state and remove every index.

World travel and readiness barriers are the next implementation layer. The design contract is recorded
in [`planned/game-session-design.md`](planned/game-session-design.md).
