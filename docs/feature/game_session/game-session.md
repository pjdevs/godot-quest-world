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

The offline travel slice now validates and preflights a resource path before retiring the current
world, allocates a monotonic `CurrentTravelId`, and asks the persistent `WorldSpawner` to construct
`World_<travelId>` from `{ travel_id, resource_path }`. A normal ready-frame check emits `WorldLoaded`
once and completes the local barrier; failed preflight leaves the previous world intact. The current
world remains beneath `WorldContainer`, so the session and `PlayerState` nodes survive replacement.

Networked travel uses a reliable `BeginTravel` RPC plus the native `WorldSpawner`. The server snapshots
remote peers present at travel start, waits for their `TravelReady(CurrentTravelId)` acknowledgements and
its own local readiness, then emits `TravelCompleted` once and reliably releases clients with
`CompleteTravel`. Duplicate or stale acknowledgements are ignored, disconnects release their pending
entry, and `WorldLoadFailed` removes only the reporting participant. Admission is refused while the
global barrier is active.

When a peer joins an active world, the persistent spawners reconstruct both `PlayerState` and the current
world. The joining process sends `CurrentWorldReady(CurrentTravelId)` after its local `WorldLoaded`
event; the server then emits `PlayerWorldReady` only for that participant. Existing participants remain
`Active` throughout late-join reconstruction. The feature test suite covers host/client, dedicated
server, disconnect release, late join, and isolated client failure paths.

QuestWorld now has a persistent `quest_world/game/Game.tscn` root. It owns `NetworkSession`,
`GameSession`, the derived `QuestWorldPlayerState` scene, the player integration, and the optional local
controller. `World` scenes are world-only content: they retain their project spawners and authority
initialization but no longer start networking or own player integration. Character carry behavior finds
its `IWorldSpawner` ancestor, so it no longer depends on `SceneTree.CurrentScene` being the world.
