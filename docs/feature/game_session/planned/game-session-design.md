# Generic Game Session

> **Status: planned.** This proposal defines the generic `game_session` addon that sits above
> `network_session`. It owns persistent participants, `PlayerState` lifecycle, synchronized world travel
> and the load barrier required before gameplay resumes. It deliberately stops before generic GameMode,
> GameState, Pawn spawning or project-specific match rules.

## Motivation

The current network refactor successfully isolated transport/session lifecycle into
`addons/network_session`. `NetworkSession` now owns only the local `MultiplayerPeer` lifecycle and emits
peer/session signals. QuestWorld-specific player spawning, ownership and possession currently live in
`QuestWorldNetworkPlayers`.

That split revealed the next stable boundary.

A connected network peer is not the same thing as a persistent player in a game, and a persistent player
is not the same thing as the Character/Pawn currently representing that player in one world.

The required lifetimes are different:

```text
Network peer
    connection lifetime

PlayerState / participant
    game-session lifetime
    survives world travel

World
    travel-to-travel lifetime

Character / Pawn
    world-local incarnation
```

Real multiplayer flows need this distinction almost immediately:

- a player selects a character, team or loadout in a lobby and that choice must survive travel;
- the server must stop accepting players while a travel is in progress or after the project decides the
  match is closed;
- all connected participants must load the next world before the server starts world gameplay;
- player Characters must be recreated from persistent participant data after travel rather than moved from
  the previous world;
- host, dedicated-server, client and offline modes should use the same conceptual flow;
- the networking layer should not know what Character, Pawn, inventory, team, score or match rules mean.

These concerns are common enough to justify a reusable addon directly rather than a QuestWorld-only spike.

## Relationship to `network_session`

`network_session` remains the lower-level boundary and should not absorb these responsibilities.

```text
NetworkSession
    create / close MultiplayerPeer
    connection state
    local peer id
    peer connected / disconnected
    host / client / dedicated / offline

        ↓ consumed by

GameSession
    admitted participants
    PlayerState lifecycle
    persistent game-session state
    world travel
    world-load barrier
```

The central invariant is:

> `NetworkSession` knows peers. `GameSession` knows participants.

`NetworkSession` does not spawn `PlayerState`, decide whether a peer is allowed into the game, coordinate
world loading or know about gameplay state.

## Goals

The V1 addon must provide:

1. a persistent `GameSession` node that composes an existing `NetworkSession`;
2. one server-authoritative `PlayerState` per admitted participant;
3. automatic participant creation from network peer lifecycle;
4. a small admission hook with default automatic behavior;
5. project-defined `PlayerState` scenes for persistent game-specific data;
6. server-authoritative world travel using a `PackedScene` resource path;
7. a deterministic travel ID and load acknowledgement protocol;
8. a barrier that completes only after the server and every still-connected participant have loaded the
   new world;
9. normal Godot node/scene lifecycle for worlds instantiated under a persistent `WorldContainer`;
10. signals that let game code compose world-specific Character/Pawn spawning and match logic without
    subclassing `GameSession` for ordinary integration;
11. parity across offline, host, dedicated-server and client modes.

## Non-goals

V1 does **not** provide:

- generic `GameMode` or `GameState`;
- generic lobby, warmup, playing, finished or round states;
- Pawn/Character spawning;
- PlayerController behavior;
- possession;
- team, loadout, score or character-selection schemas;
- matchmaking;
- authentication;
- reconnect/reassociation logic;
- seamless migration of world-local nodes;
- save/profile/account persistence across game sessions;
- client-side authority over `PlayerState`;
- a generic async loading framework;
- travel timeout policy;
- server migration;
- different client/server world assets or a world-ID registry;
- arbitrary RPC-based mutation of game-specific `PlayerState` properties.

Project code remains responsible for those concepts unless later repeated implementations prove another
stable generic boundary.

## Architectural principle: composition first

The addon should fit Godot's scene composition model.

The normal integration path is through authored node references and signals, not inheritance.

`GameSession` may expose small protected virtual hooks where inheritance is materially simpler than a
policy object. In V1, admission is the intended escape hatch. This must not turn the class into a broad
`QuestWorldGameSession : GameSession` override surface.

Signals are the primary public integration contract for:

- participant joined/left;
- travel started;
- local world loaded;
- travel completed;
- travel failure.

A project should be able to use the generic session without subclassing it.

## Persistent root topology

The expected game structure is a persistent main scene, not an Autoload-driven architecture.

Conceptual topology:

```text
Game.tscn
├── NetworkSession
├── GameSession
│   ├── Players
│   │   ├── PlayerState_1
│   │   └── PlayerState_2
│   └── PlayerStateSpawner
├── PlayerController             # optional, project-owned
└── WorldContainer
    └── Facility.tscn            # current world, replaced by travel
```

`Game.tscn` survives for the lifetime of the gameplay session. Travel replaces only the current child of
`WorldContainer`.

The framework should not require `GameSession` to be an Autoload. Keeping it in a persistent scene gives it
normal authored references, explicit ownership and testable lifecycle while still surviving world changes.

`SceneTree.CurrentScene` therefore remains the persistent game root during gameplay. A world must not rely
on `GetTree().CurrentScene` meaning "current gameplay world". Project code should obtain the current world
from its explicit game/session context or receive the dependency directly.

## Lifetime invariants

The following lifetimes are normative.

### `NetworkSession`

Lives for the current network connection/session.

It may start and stop independently of individual worlds.

### `GameSession`

Lives for the current game session and normally shares the persistent `Game.tscn` lifetime.

It is not destroyed during world travel.

### `PlayerState`

Created when a peer is admitted as a participant.

Destroyed when that participant leaves in V1.

It survives every world travel while the participant remains admitted.

### World

The current world is the child managed under `WorldContainer`.

It is removed from the tree and freed on successful replacement.

The replacement world is freshly instantiated from a `PackedScene`.

### Character / Pawn

Not owned by `GameSession`.

It belongs to the current world and may be destroyed/recreated on every travel or respawn.

A project uses persistent `PlayerState` data to decide what world-local incarnation to create.

## `GameSessionState`

The generic state machine should describe **technical session/travel state only**.

Recommended V1 states:

```text
Idle
Active
Traveling
Failed
```

Semantics:

- `Idle`: `GameSession` is not currently attached to an active game flow or has been reset/shut down;
- `Active`: participant management is active and no world travel barrier is running;
- `Traveling`: one authoritative travel is in progress;
- `Failed`: a local unrecoverable session/travel failure prevents normal continuation.

Do not add `Lobby`, `Playing`, `Finished`, `RoundEnd`, `Warmup` or similar gameplay concepts to this enum.
Those belong to project/world logic.

## Expected `GameSession` configuration

The concrete API may follow surrounding C# conventions, but the implementation must expose the following
conceptual dependencies and state:

```text
GameSession
    NetworkSession       : NetworkSession
    WorldContainer       : Node
    PlayerStateScene     : PackedScene
    PlayerStateSpawner   : MultiplayerSpawner

    State                : GameSessionState
    IsAcceptingPlayers   : bool
    CurrentWorld         : Node?
    CurrentTravelId      : positive integer / zero when none
    Players              : stable container / enumerable PlayerState set
```

The authored `PlayerStateSpawner.SpawnPath` must point at the stable persistent `Players` container.

`GameSession` should validate required references during initialization and fail predictably rather than
silently operating with partial configuration.

## Initialization and ordering

The design must not depend on scene-node `_Ready()` ordering for correctness.

The persistent game integration should explicitly initialize/start the network and game session. Whatever
exact method names are chosen, `GameSession` initialization must:

1. validate `NetworkSession`, `WorldContainer`, `PlayerStateScene` and `PlayerStateSpawner`;
2. configure the custom player-state spawn function;
3. subscribe to `NetworkSession` lifecycle signals;
4. reconcile the **current** `NetworkSession` state after subscribing.

The reconciliation step is required because offline/host/dedicated `NetworkSession.Start()` may already
have emitted `Connected` before `GameSession` subscribes.

Therefore both sequences must be safe:

```text
NetworkSession.Start()
GameSession.Initialize()
```

and, if later convenient:

```text
GameSession.Initialize()
NetworkSession.Start()
```

The first sequence is the expected initial QuestWorld integration.

## Participant model

A participant is represented by a persistent `PlayerState : Node`.

The base `PlayerState` should be deliberately tiny.

Required generic identity:

```text
ParticipantId
PeerId
```

No gameplay fields belong in the base class.

### `ParticipantId`

`ParticipantId` is the stable identity of the participant **within one `GameSession`**.

V1 requirements:

- assigned by the server;
- positive;
- monotonically allocated or otherwise guaranteed unique for the game-session lifetime;
- not derived from `PeerId`;
- not reused after a participant leaves during the same game session;
- stable across world travels;
- used for the stable `PlayerState` node name, e.g. `PlayerState_<ParticipantId>`.

V1 does not implement reconnection, but keeping this identity distinct avoids hard-coding the future
assumption that one network connection is the player identity.

### `PeerId`

`PeerId` identifies the participant's current Godot multiplayer connection.

In V1 it remains fixed for the participant lifetime because reconnect/reassociation is out of scope.

A future reconnect feature may update `PeerId` while preserving `ParticipantId` and the same
`PlayerState`.

### Authority

`PlayerState` remains **server authoritative**.

Do not call:

```text
PlayerState.SetMultiplayerAuthority(PlayerState.PeerId)
```

simply because a participant logically owns the player state.

The peer ID is ownership metadata, not Godot multiplayer authority for this node. The server must remain the
source of truth for persistent game-session state.

This is important for character selection, teams, loadouts, score and similar data: clients request
changes through project-defined APIs; authoritative project code validates and mutates the server-owned
`PlayerState`; replication then publishes the accepted state.

## Project-specific `PlayerState`

The addon exposes a `PackedScene PlayerStateScene` rather than forcing every game into one fixed schema.

Example QuestWorld scene:

```text
QuestWorldPlayerState
├── script : QuestWorldPlayerState : PlayerState
│   ├── CharacterDefinitionId
│   └── LoadoutId
└── MultiplayerSynchronizer
```

A competitive project could instead add:

```text
TeamId
SelectedHeroId
Score
Kills
Ready
```

The generic addon does not inspect these properties.

The project owns their validation and replication configuration. A child `MultiplayerSynchronizer` is the
preferred Godot-native solution for simple persistent replicated properties.

The invariant is:

> `game_session` replicates the `PlayerState` lifecycle and generic identity. The project replicates the
> contents it adds to that state.

## PlayerState spawn replication

Use a dedicated `MultiplayerSpawner` custom spawn for participant lifecycle.

This is preferable to custom create/delete RPCs because Godot already provides authoritative spawn/despawn
replication and late-join reconstruction.

The server should call the spawner with spawn data containing only generic identity, conceptually:

```text
{
    participant_id,
    peer_id
}
```

The configured `spawn_function` runs on each peer and must:

1. instantiate `PlayerStateScene`;
2. validate that the instantiated root is a `PlayerState`;
3. assign `ParticipantId` and `PeerId` before the node enters the tree;
4. assign a deterministic stable name from `ParticipantId`;
5. return the node **without** calling `AddChild()`.

`MultiplayerSpawner` owns adding the returned node under its `SpawnPath` and replicating that lifecycle.

The identity data should not require a `MultiplayerSynchronizer`: it is part of spawn construction and
must already be correct before `_EnterTree()`/`_Ready()` on the `PlayerState` tree.

The implementation should preserve one factory/data path across host, client and offline behavior as far as
Godot's no-peer mode permits. If a small offline adapter is required by engine behavior, it must reuse the
same spawn-data factory rather than introduce a second participant initialization model.

Reference: Godot `MultiplayerSpawner.spawn()` custom spawning calls `spawn_function` on peers and adds the
returned node automatically under `spawn_path`.

## Participant registry invariants

`GameSession` must maintain deterministic lookups for participants.

At minimum it should be possible to resolve:

```text
ParticipantId -> PlayerState
PeerId        -> PlayerState
```

Required invariants:

- no duplicate `ParticipantId`;
- no two current participants share one `PeerId`;
- a `PlayerState` is registered exactly once when its replicated node becomes part of the persistent player
  collection;
- a leaving `PlayerState` is removed from all indexes;
- project code consumes `PlayerState` objects rather than rebuilding participant identity from node names.

The stable node name is a replication/path convention, not the primary lookup API.

## Admission flow

Admission is server-owned and automatic by default.

Normal flow:

```text
NetworkSession.PeerConnected(peerId)
    ↓
GameSession technical admission check
    ↓
CanJoin(peerId)
    ↓ accepted
allocate ParticipantId
    ↓
PlayerStateSpawner.Spawn(identity data)
    ↓
PlayerState registered
    ↓
PlayerJoined(PlayerState)
```

A client never creates its own authoritative `PlayerState`.

### Local host/offline participant

When `GameSession` reconciles an already-active session:

- offline: create one local participant for `NetworkSession.LocalPeerId`;
- host: create the local host participant if it does not already exist;
- dedicated server: do **not** create a participant for server peer ID `1` merely because the server is
  locally connected;
- client: do not create a local participant; the server's `MultiplayerSpawner` will replicate it.

This preserves the current distinction between a listen host and a dedicated server.

### `IsAcceptingPlayers`

`GameSession` exposes a project-controlled `IsAcceptingPlayers` intent.

It means:

> outside temporary technical blocks, should new network peers be admitted as game participants?

Examples of project usage:

```text
Lobby      -> true
Match lock -> false
```

This flag is not a gameplay-state enum. Projects decide when to change it.

Where practical, the server should reflect effective admission into Godot's
`MultiplayerPeer.RefuseNewConnections` so clearly closed sessions reject new transport connections early.

### Travel admission block

Travel adds a temporary technical admission block regardless of `IsAcceptingPlayers`.

Conceptually:

```text
CanAcceptNow = IsAcceptingPlayers && State == Active
```

Starting travel must refuse new connections/participants until that travel finishes or fails.

Completing travel restores effective admission from the existing `IsAcceptingPlayers` intent. Travel must
not silently overwrite the project's choice.

### `CanJoin` escape hatch

V1 should not introduce a policy interface/resource for one small admission decision.

Instead `GameSession` may expose a protected virtual hook conceptually equivalent to:

```text
CanJoin(peerId, out refusalReason)
```

Default implementation:

- accept when effective admission is open;
- reject otherwise.

A project that really needs custom validation may subclass only for this hook.

Possible project checks include:

- gameplay max participants smaller than transport max clients;
- password/token already validated by project code;
- team capacity;
- project-specific match lock.

This is an escape hatch, not the preferred integration mechanism for general game-session behavior.

If a peer has already completed the transport connection and `CanJoin` rejects it, the server should
disconnect that peer rather than create a transient `PlayerState`.

V1 does not provide a rich pre-login handshake or guaranteed client-visible refusal reason.

## Participant leave / disconnect

V1 keeps disconnect behavior deliberately simple.

```text
NetworkSession.PeerDisconnected(peerId)
    ↓
resolve PlayerState by PeerId
    ↓
authoritative PlayerState despawn/free
    ↓
remove indexes
    ↓
PlayerLeft(PlayerState identity/event data)
```

The participant is removed immediately.

No reconnect grace period, detached `PlayerState`, account identity or reassociation exists in V1.

If the participant leaves while a travel barrier is running, its peer is removed from that barrier so the
remaining participants can still complete the travel.

## Why PlayerState persists instead of Characters

Persistent player choices belong on `PlayerState`, not on a world-local Character.

Example:

```text
Lobby
    PlayerState.CharacterDefinitionId = "manny_red"

Travel to Facility
    Lobby destroyed
    PlayerState survives

Facility ready
    project world integration reads PlayerState
    -> spawns selected Character definition
```

This prevents travel from needing to move physical gameplay actors between unrelated scene trees.

It also keeps respawn semantics clean:

```text
score / team / selected character -> PlayerState
health / transform / animation    -> Character/Pawn
```

Each project decides which runtime data is session-persistent versus incarnation-local.

## World travel model

`GameSession` owns the **mechanics** of synchronized world travel but no world-specific gameplay.

Travel is server-authoritative.

Public conceptual API:

```text
Travel(PackedScene worldScene)
```

Only the authority/server may successfully initiate it.

V1 requires `worldScene.ResourcePath` to be a valid project resource path. Runtime-generated anonymous
`PackedScene` instances are not valid travel targets because clients need to load the same resource.

The same client/server build and asset set are assumed.

No world registry or abstract `TravelTargetId` is introduced in V1.

## Travel ID

Every server-initiated travel receives a monotonically increasing positive `TravelId`.

The ID exists to make late/out-of-order acknowledgements harmless.

All travel messages include this ID.

Required behavior:

- acknowledgements for an older ID are ignored;
- acknowledgements for a future/unknown ID are rejected/ignored;
- `CompleteTravel` applies only to the currently active travel ID;
- only one travel may be active at a time.

## Reliable travel protocol

Travel control messages are critical state transitions and must use reliable RPC delivery.

Conceptual protocol:

```text
Server Travel(scene)
    ↓
validate/preflight local scene
    ↓
TravelId++
State = Traveling
block new joins
snapshot expected participants
    ↓
reliable BeginTravel(TravelId, resourcePath)
    ↓
server performs local world replacement
clients perform local world replacement
    ↓
each client -> reliable TravelReady(TravelId)
server local load -> local-ready flag
    ↓
server waits for local ready + every still-connected expected participant
    ↓
reliable CompleteTravel(TravelId)
    ↓
all peers State = Active
TravelCompleted
```

RPCs must validate authority/sender expectations. A client cannot cause another peer to begin or complete
travel by directly invoking a receiver method.

Because Godot RPC resolution depends on matching node paths, `GameSession` must exist at the same stable
path in the persistent game scene on all network peers.

## Local world replacement

Travel does **not** call `SceneTree.ChangeSceneToFile()` for gameplay worlds.

Instead `GameSession` manages the current child under `WorldContainer`.

Required local replacement sequence:

1. synchronously load/resolve the requested `PackedScene` resource;
2. instantiate the candidate world **before destroying the current world**;
3. validate the candidate root sufficiently for the generic layer (non-null valid node);
4. remove the old world from `WorldContainer` so it immediately leaves the active scene tree;
5. queue/free the old world;
6. add the new world root under `WorldContainer`;
7. allow normal Godot `_EnterTree()` / `_Ready()` lifecycle to execute;
8. set `CurrentWorld` to the new root;
9. emit local `WorldLoaded` only after the root has completed the normal ready lifecycle expected from
   `AddChild()`.

Pre-instantiating the candidate before removing the old world ensures a normal resource-load or
instantiation failure can leave the old world intact.

The generic layer does not define asynchronous gameplay initialization after `_Ready()`. If a world needs
streaming, backend fetches or other domain-specific readiness, that remains project-specific until a real
second use case proves the need for a richer generic readiness contract.

Reference: Godot `PackedScene.Instantiate()` creates a node hierarchy that can be manually added anywhere
in the current scene tree.

## `WorldLoaded` versus `TravelCompleted`

These are distinct semantics and must remain distinct.

### `WorldLoaded`

Local event:

> This process has instantiated the new world, added it under `WorldContainer` and completed normal Godot
> ready lifecycle for that root.

A client emitting `WorldLoaded` does **not** mean gameplay may begin globally.

### `TravelCompleted`

Synchronized event:

> The server and every participant still required by this travel have loaded the same travel ID, and the
> server has released the barrier.

This is the normal point for project code to start world gameplay that assumes all participants are
present.

For QuestWorld, authoritative Character spawning after travel should be triggered from this synchronized
boundary rather than from raw `PeerConnected`.

## Travel barrier membership

At travel start, the server snapshots the participants that must acknowledge that travel.

Joins are blocked while traveling, so the set can only shrink through disconnect.

The barrier should distinguish:

```text
server local world readiness
remote participant peer acknowledgements
```

This matters for dedicated server mode: peer ID `1` is the server but has no `PlayerState` participant.

Recommended barrier semantics:

- server local world must always load successfully;
- host peer `1`, if represented by a local `PlayerState`, is satisfied by the server's local load and must
  not require a duplicate RPC acknowledgement;
- dedicated server local load is required even though there is no participant for peer `1`;
- every remote participant present at travel start must acknowledge the current `TravelId`;
- a disconnect removes that participant's peer from the pending set;
- completion occurs when local-ready is true and no required remote peer remains pending.

The barrier is travel-specific in V1. Do not prematurely extract a generic reusable `PeerBarrier` until a
second concrete feature needs the same primitive.

## Client travel acknowledgement

After a remote client completes local replacement and emits local `WorldLoaded`, it sends a reliable
`TravelReady(TravelId)` to the server.

The server must verify:

- sender corresponds to a current participant;
- sender was expected by the current barrier;
- ID equals the current `TravelId`;
- duplicate ready messages are idempotent.

A duplicate acknowledgement must not produce duplicate completion or signals.

## Travel completion

When the server's barrier reaches zero:

1. the server marks the travel complete exactly once;
2. server `State` returns to `Active`;
3. effective admission is restored from `IsAcceptingPlayers`;
4. server emits local `TravelCompleted`;
5. server sends reliable `CompleteTravel(TravelId)` to remote peers;
6. each client validates the current ID, returns to `Active` and emits its local `TravelCompleted`.

The project may begin authoritative world-local participant spawning immediately after server
`TravelCompleted`; all clients already have the world required to receive those replicated spawns.

## Offline travel

Offline mode follows the same conceptual lifecycle without network messages.

```text
Travel
    ↓
State = Traveling
    ↓
replace local world
    ↓
WorldLoaded
    ↓
local barrier immediately satisfied
    ↓
State = Active
TravelCompleted
```

Offline must not require a separate public API or project integration path.

## Host travel

Host mode uses the server's local world replacement for the host participant and waits only for remote
participant acknowledgements.

The host should not send an RPC to itself merely to make the code look symmetrical if a direct internal
call is clearer. Internal logic may unify both paths behind one local `BeginTravel` helper.

## Dedicated-server travel

Dedicated server participates in world loading even though it has no local player participant.

The server must instantiate the same authoritative world scene under its persistent `WorldContainer` and
must not complete travel until that local load succeeds.

## Travel failure behavior

Distributed rollback is intentionally out of scope.

V1 should instead make failure semantics explicit and conservative.

### Server preflight/load failure

If the server cannot load or instantiate the requested scene before replacing the current world:

- do not broadcast `BeginTravel`;
- keep the current world active;
- remain/return `Active`;
- emit/report travel failure;
- do not increment externally visible completion state as if travel succeeded.

The implementation may allocate the next internal travel ID only after successful preflight to keep logs
simple, or allocate then mark it failed; whichever convention is chosen must be deterministic and tested.

### Server unrecoverable replacement failure

If an unexpected failure occurs after the old world has been removed and normal continuation is not
possible:

- transition `GameSession` to `Failed`;
- emit failure information;
- do not claim `TravelCompleted`;
- leave project code responsible for deciding whether to return to menu, restart or terminate.

### Client load failure

If a client cannot load/instantiate the instructed resource:

```text
client TravelFailed(TravelId, reason)
    ↓
server validates sender + current TravelId
    ↓
server disconnects/removes that participant
    ↓
participant removed from travel barrier
    ↓
remaining participants may still complete
```

Do not attempt to roll every other peer back to the old world.

The failure reason is diagnostic data, not trusted gameplay input.

### Timeout

No generic travel timeout is required in V1.

The addon should expose enough state/signals for project code or a later generic extension to observe a
travel that remains pending. Do not invent timeout duration/policy without a concrete game requirement.

## Player disconnect during travel

A disconnect during travel must not deadlock the barrier.

Required sequence:

```text
PeerDisconnected
    ↓
remove pending TravelReady requirement
    ↓
remove PlayerState
    ↓
PlayerLeft
    ↓
re-evaluate barrier
    ↓
complete travel if no remaining peers are pending
```

The order of internal removal and signal emission may vary, but externally the participant must no longer
block completion once disconnected.

## New connection during travel

New connections should be refused while `State == Traveling`.

If a transport race still delivers `PeerConnected` after the travel block is engaged, admission must reject
the peer and it must not be added to the current barrier or participant set.

V1 does not support joining a travel already in progress.

## Late join while active

When `State == Active` and admission is open, a new participant may join normally.

The dedicated `MultiplayerSpawner` for `PlayerState` must ensure the joining peer receives existing
persistent player states as well as its own newly created state.

World-state late join behavior beyond this persistent participant registry remains the responsibility of
world spawners/synchronizers and project networking.

## Signals / composition surface

Exact C# signal signatures may adapt to Godot Variant constraints, but the addon should expose the
following semantic events.

### Participant lifecycle

```text
PlayerJoined(PlayerState)
PlayerLeft(...)
```

For `PlayerLeft`, code that needs identity after the node is queued for deletion must receive stable
identity values or receive the signal before the object becomes invalid. Do not expose a signal contract
that hands consumers an already-freed node.

### Travel lifecycle

```text
TravelStarted(TravelId, resourcePath)
WorldLoaded(TravelId, currentWorld)
TravelCompleted(TravelId, currentWorld)
TravelFailed(TravelId, reason)
```

Optionally expose a server-side per-peer ready signal if implementation/testing demonstrates real value,
but it is not a required public abstraction.

Signals should be emitted exactly once per semantic transition.

## Public queries

Game/project code should be able to query without scanning the scene tree:

```text
CurrentWorld
Players / PlayerStates
TryGetPlayerStateByPeerId(peerId)
TryGetPlayerStateByParticipantId(participantId)
State
CurrentTravelId
IsAcceptingPlayers
```

Do not require consumers to parse `PlayerState_<id>` names.

## Match/gameplay state boundary

`GameSession` does not become a generic match rules engine.

A project may have world-local components such as:

```text
FacilityWorld
├── FacilityGameMode
└── FacilityGameState
```

or:

```text
DeathmatchWorld
├── DeathmatchRules
└── DeathmatchState
```

The addon does not require those classes or names.

The generic boundary ends at:

```text
participants persisted
world loaded everywhere
travel completed
```

What the world does next is game-specific.

## Persistent PlayerController boundary

A project's local `PlayerController` may live beside `GameSession` in the persistent game root because its
lifetime can also span world travel.

This proposal does not move `PlayerController` into `game_session` and does not require every game to use
one.

A project may listen for the new world's player Character to appear and possess it using its own controller.

## QuestWorld integration target

The proposal should remove the need for `QuestWorldNetworkPlayers` as the owner of generic peer-to-player
lifecycle.

Responsibilities move as follows.

### Moves into generic `GameSession`

- listen to peer connected/disconnected;
- decide whether a peer becomes a participant;
- persistent participant registry;
- create/despawn persistent `PlayerState`;
- survive world travel;
- travel/load coordination.

### Remains QuestWorld-specific

- `QuestWorldNetworkIdentity` if still useful for Character naming;
- choosing Character/Pawn scene/definition from `QuestWorldPlayerState`;
- selecting spawn transforms;
- spawning/despawning world-local Characters;
- setting Character `OwnerPeerId` and multiplayer authority;
- local possession;
- resolving the current world's `Spawner`/`IWorldSpawner` integration.

Those remaining responsibilities should preferably live at the QuestWorld game/world integration boundary,
not in a persistent component named `NetworkPlayers` that treats a peer connection as a permanent
Character.

After travel, the intended QuestWorld flow becomes:

```text
GameSession.TravelCompleted
    ↓ server
current QuestWorld World is ready everywhere
    ↓
for each QuestWorldPlayerState
    select Character definition / spawn point
    spawn Character into current world
    configure network ownership
    ↓ clients
replicated Character appears
    ↓ local project integration
possess Character whose OwnerPeerId matches local participant
```

A peer may therefore exist in the session without currently having a Character, which is an intended
capability rather than an error.

## Persistent game root migration pressure

The current `World._Ready()` starts `NetworkSession` and initializes `QuestWorldNetworkPlayers`. That
responsibility must move upward into the persistent game root when this proposal is implemented.

World scenes should return to world concerns:

- authored spawners;
- current-world gameplay;
- world-local initialization.

They should not own the lifetime of `NetworkSession` or `GameSession`.

Likewise, code such as:

```text
GetTree().CurrentScene as IWorldSpawner
```

will no longer be a valid way to find the current gameplay world because `CurrentScene` is the persistent
game root. Project integration must resolve/inject the actual current world explicitly.

This is an intentional architectural improvement, not a travel regression.

## Suggested addon boundary

Expected future runtime location:

```text
addons/game_session/
    scripts/
        GameSession.cs
        GameSessionState.cs
        PlayerState.cs
```

A small authored reusable `.tscn` may be provided if it materially simplifies setting up the stable
`Players` container and `MultiplayerSpawner`, but the addon should not require a large prefab hierarchy.

`game_session` may depend on `network_session`.

It must not depend on:

- QuestWorld game code;
- dummy character plugin;
- interaction;
- gameplay actions;
- inventory;
- project `Spawner` abstractions.

## Expected API shape

This is a design contract, not a required byte-for-byte signature, but implementations should remain close
enough that reviewers can explain deviations.

Conceptually:

```text
PlayerState : Node
    ParticipantId { get; }
    PeerId { get; }

GameSession : Node
    NetworkSession
    WorldContainer
    PlayerStateScene
    PlayerStateSpawner

    State
    IsAcceptingPlayers
    CurrentWorld
    CurrentTravelId

    Initialize()
    Shutdown()/Reset()                 # exact lifecycle API may follow existing addon style
    Travel(PackedScene worldScene)

    TryGetPlayerStateByPeerId(...)
    TryGetPlayerStateByParticipantId(...)
    GetPlayerStates()/Players

    protected virtual CanJoin(...)

    signal PlayerJoined
    signal PlayerLeft
    signal TravelStarted
    signal WorldLoaded
    signal TravelCompleted
    signal TravelFailed
```

Do not add broad virtual lifecycle methods such as `OnWorldLoaded`, `OnPlayerSpawned`, `CreatePawn`,
`StartMatch`, etc. merely to imitate Unreal-style inheritance. Signals/composition own those extensions.

## State mutation rules

Server authority must own:

- participant creation/removal;
- `ParticipantId` allocation;
- admission;
- `IsAcceptingPlayers` effective transport policy;
- travel initiation;
- current authoritative `TravelId`;
- travel barrier membership/completion.

Clients may:

- receive replicated `PlayerState` lifecycle;
- load an instructed world;
- acknowledge their own completed travel;
- report local travel load failure;
- observe `TravelCompleted` after the server releases the barrier.

Clients may not:

- create participants;
- delete another `PlayerState`;
- initiate authoritative travel;
- mark another peer ready;
- complete the barrier;
- mutate base `PlayerState` identity.

## Error handling / validation

Configuration errors should fail early with actionable Godot errors.

Validate at minimum:

- missing `NetworkSession`;
- missing `WorldContainer`;
- missing `PlayerStateSpawner`;
- missing `PlayerStateScene`;
- `PlayerStateScene` root is not `PlayerState`;
- invalid/empty travel resource path;
- travel requested by non-server;
- travel requested while already `Traveling`;
- duplicate participant identities;
- unexpected TravelReady sender;
- stale/future travel IDs;
- client-reported failure from a non-participant.

Expected/rejected requests should not necessarily crash or put the session into `Failed`; reserve `Failed`
for local unrecoverable runtime state.

## Testing requirements

This feature changes peer lifecycle, replication and cross-scene runtime behavior, so tests must include
both focused runtime tests and real network integration tests.

The implementation plan should follow the repository test strategy: smallest relevant suites first, then
`task test:network` / `task test:runtime` as appropriate, and only use the full suite when cross-cutting
validation justifies it.

The final implementation must prove at least the following behavior.

### Participant lifecycle

1. offline initialization creates exactly one local `PlayerState`;
2. host initialization creates exactly one host `PlayerState`;
3. dedicated server creates no fake player for server peer ID `1`;
4. client does not authoritatively create its own `PlayerState`;
5. server connection admits a remote peer and replicates one `PlayerState` everywhere;
6. a late join receives all existing `PlayerState` nodes;
7. disconnect removes the matching `PlayerState` and indexes;
8. `ParticipantId` and `PeerId` remain distinct and correctly initialized before `PlayerState._Ready()`;
9. `PlayerState` remains server-authoritative even when `PeerId` is a client ID;
10. subclassed/project-specific `PlayerStateScene` is instantiated rather than the generic base scene.

### Admission

11. `IsAcceptingPlayers = false` prevents new participants without removing existing ones;
12. custom `CanJoin` rejection creates no `PlayerState`;
13. travel temporarily rejects new joins even if `IsAcceptingPlayers` is true;
14. successful travel restores admission from the previous project intent.

### Local world lifecycle

15. first travel instantiates the requested world under `WorldContainer`;
16. `WorldLoaded` fires after normal world ready lifecycle;
17. a second travel removes/exits/frees the old world and instantiates a fresh replacement;
18. `GameSession` and existing `PlayerState` nodes remain in the scene tree across both travels;
19. `SceneTree.CurrentScene` / persistent game root is not replaced by gameplay travel;
20. a local preflight load failure leaves the old world intact and does not emit `TravelCompleted`.

### Network travel

21. only the server can initiate travel;
22. host + one client both load the same resource path;
23. server does not emit synchronized completion until the remote client acknowledges;
24. client `WorldLoaded` can occur before `TravelCompleted` without starting global gameplay;
25. duplicate `TravelReady` is idempotent;
26. stale previous-travel acknowledgement is ignored;
27. disconnect of a pending peer removes it from the barrier and allows remaining peers to complete;
28. dedicated server local world readiness is required even without a local `PlayerState`;
29. offline travel resolves the same lifecycle without RPCs;
30. client load failure is reported, participant is removed/disconnected, and remaining peers are not
    rolled back;
31. `TravelCompleted` fires exactly once per peer for one successful travel ID.

### Persistent game-specific state

32. a project-specific synchronized property set on `PlayerState` before travel remains present on the same
    persistent node after at least two world replacements;
33. newly created world-local Character/Pawn nodes are not required to survive travel for that state to
    persist.

## Success criteria

The proposal is successfully implemented when a minimal project can express this flow without custom
session plumbing:

```text
start host
    ↓
GameSession creates host PlayerState
    ↓
client connects
    ↓
GameSession creates + replicates client PlayerState
    ↓
project modifies persistent PlayerState selection data
    ↓
server Travel(Facility)
    ↓
all peers replace only WorldContainer child
    ↓
all peers report loaded
    ↓
server releases barrier
    ↓
TravelCompleted
    ↓
project spawns world-local Characters from persistent PlayerStates
```

The generic addon must remain unaware of what those Characters are or what gameplay begins after the
barrier.

The resulting architecture should preserve this final boundary:

```text
persistent
────────────────────────────────
NetworkSession   connection lifecycle
GameSession      participants + travel
PlayerState      persistent participant data
PlayerController project-local optional controller

travel boundary
────────────────────────────────

world-local
────────────────────────────────
World            current scene content
GameMode/State   project-specific if needed
Character/Pawn   participant incarnation
Gameplay actors
```

That is the intended stopping point for generic framework work in this area. GameMode/GameState-like
abstractions remain specific until independent projects prove a reusable contract.