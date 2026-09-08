# Game Session

## Responsibility

The generic `game_session` addon sits above `network_session`. It owns persistent participant
identity, server admission, `PlayerState` lifecycle, and the persistent roots used for current-world
travel. It does not know about Characters, Pawns, teams, loadouts, match rules, or project gameplay.

## Current implementation

`GameSession` is explicitly initialized after its authored references are available and before networking
starts. It validates the `NetworkSession`, persistent `Players`, `PlayerStateScene`,
`PlayerStateSpawner`, empty `WorldContainer`, and `WorldSpawner` dependencies, including both authored
spawner paths. Initialization configures callbacks and subscriptions while the session remains `Idle`;
the subsequent `NetworkSession.Connected` signal starts the runtime session and enters `Active`.

`PlayerState` receives a positive `ParticipantId` and current `PeerId` before it enters the scene tree.
The server allocates participant IDs monotonically. A dedicated participant registry is the single source
of truth for the ordered list and both identity indexes. Removal is identity-safe: a delayed callback from
an old replica cannot unregister a newer instance that happens to reuse the same IDs in a fresh runtime
session.
Offline sessions create the local participant; dedicated servers and clients do not author a fake
participant locally.

On an active server, peer admission allocates the next participant ID and sends `{ participant_id,
peer_id }` through the persistent `PlayerStateSpawner`. The same factory constructs the state on
remote peers, so identity is initialized before `_Ready()` and derived `PlayerStateScene` roots are
supported. The server keeps both peer and participant indexes, disconnects peers refused by
`CanJoin`, and mirrors admission intent through `NetworkSession.SetAcceptingConnections`; disconnects
remove registry membership synchronously before deferred node cleanup, so travel barriers never release
with a departed participant still visible.

`PlayerJoined` and `PlayerLeft` describe local replica lifecycle. Every process emits them exactly once
when its local `PlayerState` enters or leaves the registry; server-only gameplay readiness continues to use
`PlayerWorldReady`.

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
entry, and `WorldLoadFailed` removes only the reporting participant. The one active global travel is held
by an explicit state object containing its ID, resource path, local-ready flag, and pending peer set.
Admission is refused while that state is active.

When a peer joins an active world, the persistent spawners reconstruct both `PlayerState` and the current
world. The joining process sends `CurrentWorldReady(CurrentTravelId)` after its local `WorldLoaded`
event; the server then emits `PlayerWorldReady` only for that participant. Existing participants remain
`Active` throughout late-join reconstruction. The feature test suite covers host/client, dedicated
server, disconnect release, late join, and isolated client failure paths.

QuestWorld now has a persistent `quest_world/game/Game.tscn` root. It owns `NetworkSession`,
`GameSession`, the derived `QuestWorldPlayerState` scene, the player integration, and the optional local
controller. Its bootstrap order is `GameSession.Initialize()`, project integration initialization, then
`NetworkSession.Start()`, ensuring no synchronous connection signal can be missed. `World` scenes are
world-only content: they retain their project spawners and authority
initialization but no longer start networking or own player integration. Character carry behavior finds
its `IWorldSpawner` ancestor, so it no longer depends on `SceneTree.CurrentScene` being the world.
