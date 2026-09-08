# Generic Game Session

> **Status: planned.** This proposal defines the generic `game_session` addon that sits above
> `network_session`. It owns persistent participants, `PlayerState` lifecycle, synchronized world travel
> and the world-load barrier required before gameplay resumes. It deliberately stops before generic
> GameMode, GameState, Pawn spawning or project-specific match rules.

## Motivation

The current network refactor successfully isolated transport/session lifecycle into
`addons/network_session`. `NetworkSession` now owns only the local `MultiplayerPeer` lifecycle and emits
peer/session signals. QuestWorld-specific player spawning, ownership and possession currently live in
`QuestWorldNetworkPlayers`.

That split reveals the next stable boundary:

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

A connected peer is not a persistent player, and a persistent player is not the Character/Pawn currently
representing that player in one world.

Real multiplayer flows need this distinction immediately:

- lobby selections such as character, team or loadout must survive travel;
- the server must decide whether a new peer becomes a game participant;
- the current world must change on every peer without destroying persistent session state;
- all required participants must finish loading before world gameplay resumes;
- late join must reconstruct both persistent `PlayerState` nodes and the current world safely;
- project code must remain free to decide what Pawn/Character a participant receives.

These concerns are generic enough to justify a reusable addon directly.

## Relationship to `network_session`

`network_session` remains the lower-level boundary:

```text
NetworkSession
    MultiplayerPeer lifecycle
    local peer id
    connected / disconnected
    peer connected / disconnected
    host / client / dedicated / offline

        ↓ consumed by

GameSession
    participant admission
    PlayerState lifecycle
    persistent participant registry
    world lifecycle
    synchronized travel
    world-ready barrier
```

The central invariant is:

> `NetworkSession` knows peers. `GameSession` knows participants and the current game world.

`NetworkSession` must not know about `PlayerState`, worlds, Characters, teams, loadouts or match flow.

## Goals

V1 must provide:

1. a persistent `GameSession : Node` that composes an existing `NetworkSession`;
2. one server-authoritative `PlayerState` per admitted participant;
3. automatic participant creation/removal from peer lifecycle;
4. project-defined `PlayerStateScene` for game-specific persistent data;
5. a persistent `MultiplayerSpawner` for `PlayerState` lifecycle and late join;
6. a persistent `MultiplayerSpawner` for the **current world**;
7. server-authoritative `Travel(PackedScene)` using the world scene resource path as spawn data;
8. deterministic `TravelId` correlation;
9. a ready barrier completed only after the server and all still-required remote participants have the new
   world ready;
10. late join reconstruction of the current world through native spawner replication rather than a
    hand-written catch-up scene RPC;
11. signals for project code to compose Pawn spawning, possession and world-specific game flow;
12. parity across offline, host, dedicated-server and client modes.

## Non-goals

V1 does **not** provide:

- generic `GameMode` or `GameState`;
- generic lobby/playing/finished/round states;
- Character/Pawn spawning;
- PlayerController behavior or possession;
- team/loadout/score/character-selection schemas;
- matchmaking or authentication;
- reconnect/reassociation;
- save/profile persistence across game sessions;
- seamless migration of world-local nodes;
- generic async asset streaming;
- travel timeout policy;
- server migration;
- world-ID registries or different client/server assets;
- client authority over `PlayerState`;
- arbitrary generic mutation APIs for project-specific `PlayerState` data.

## Architectural principle: composition first

The addon follows Godot scene composition.

Normal integration is through authored references and signals. A project should be able to use
`GameSession` without subclassing it.

One narrow inheritance escape hatch is allowed for admission because a full policy interface/resource is
unnecessary plumbing in V1:

```text
protected virtual CanJoin(peerId, out reason)
```

Do not grow a broad virtual lifecycle surface such as `CreatePawn`, `StartMatch`, `OnWorldLoaded`, etc.
Those integrations belong to signals/composition.

## Persistent root topology

The expected topology is a persistent gameplay root scene:

```text
Game.tscn
├── NetworkSession
├── GameSession
│   ├── Players
│   │   ├── PlayerState_1
│   │   └── PlayerState_2
│   ├── PlayerStateSpawner
│   ├── WorldContainer
│   │   └── Facility                 # runtime-spawned current world
│   └── WorldSpawner
└── PlayerController                 # optional, project-owned
```

`Game.tscn` remains `SceneTree.CurrentScene` during gameplay. Travel replaces only the world child under
`WorldContainer`.

The framework does not require `GameSession` to be an Autoload. Keeping it in a persistent scene preserves
normal authored references, explicit ownership, Remote SceneTree visibility and testable lifecycle.

Code must therefore stop using `GetTree().CurrentScene` as a synonym for the gameplay world. The current
world comes from `GameSession.CurrentWorld` or an explicitly injected project context.

## Lifetime invariants

### `NetworkSession`

Connection lifetime. It can start/stop independently of individual worlds.

### `GameSession`

Game-session lifetime. It survives every world travel.

### `PlayerState`

Created when a peer is admitted; destroyed when that participant leaves in V1. It survives world travel.

### World

Exactly zero or one current world is managed beneath the persistent `WorldContainer`. The authoritative
server spawns it through `WorldSpawner`; clients receive the corresponding native replicated spawn.

### Character / Pawn

World-local and outside `game_session`. It may be destroyed/recreated on travel or respawn.

## `GameSessionState`

Technical state only:

```text
Idle
Active
Traveling
Failed
```

- `Idle`: initialized game flow not currently active or reset;
- `Active`: participant management is active and no global travel barrier is running;
- `Traveling`: one server-initiated global world transition is in progress;
- `Failed`: local unrecoverable state prevents normal continuation.

Never add `Lobby`, `Playing`, `Warmup`, `Finished`, etc. to this enum. Those are project/world concerns.

## Expected configuration

Conceptual authored dependencies:

```text
GameSession
    NetworkSession       : NetworkSession
    Players              : Node
    PlayerStateScene     : PackedScene
    PlayerStateSpawner   : MultiplayerSpawner
    WorldContainer       : Node
    WorldSpawner         : MultiplayerSpawner

    State
    IsAcceptingPlayers
    CurrentWorld
    CurrentWorldPath
    CurrentTravelId
```

Requirements:

- `PlayerStateSpawner.SpawnPath` points to persistent `Players`;
- `WorldSpawner.SpawnPath` points to persistent `WorldContainer`;
- both spawners are persistent and therefore have identical stable paths on every peer before network
  replication begins;
- `WorldContainer` is empty when `GameSession` initializes and later contains at most one managed world;
- every managed world originates from the persistent `WorldSpawner`.

## Explicit initialization

Correctness must not depend on sibling `_Ready()` ordering.

`GameSession.Initialize()` (exact name may follow surrounding conventions) must:

1. validate required references;
2. configure `PlayerStateSpawner.SpawnFunction`;
3. configure `WorldSpawner.SpawnFunction`;
4. subscribe to both spawners' spawn/despawn lifecycle needed for indexes/readiness;
5. subscribe to `NetworkSession` signals;
6. remain `Idle` until the subscribed `NetworkSession.Connected` transition starts a fresh runtime
   session.

There is one supported bootstrap order. Authored integrations subscribe before network startup:

```text
GameSession.Initialize()
QuestWorldNetworkPlayers.Initialize()
NetworkSession.Start(options)
```

Initializing after an already-running network session is invalid; `GameSession` does not reconstruct
arbitrary peer history. The persistent `Game` root owns this ordering.

When `NetworkSession.Disconnected` fires, the initialized `GameSession` clears participants, current
world, active travel, and late-join readiness state, then returns to `Idle`. The authored node remains
initialized and subscribed, so a later `NetworkSession.Start()` begins a fresh runtime session.

Transport ownership remains inside `network_session`:

```text
GameSession admission intent -> NetworkSession.SetAcceptingConnections(...)
GameSession rejection/failure -> NetworkSession.DisconnectPeer(...)
```

`GameSession` never mutates `MultiplayerPeer.RefuseNewConnections` or calls
`MultiplayerPeer.DisconnectPeer` directly.

# Participants and PlayerState

## Base `PlayerState`

`PlayerState : Node` is deliberately tiny:

```text
ParticipantId
PeerId
```

No score, team, ready state, loadout or selected character exists in the generic base.

## `ParticipantId`

Stable identity **within one GameSession**:

- assigned server-side;
- positive;
- unique for the game-session lifetime;
- not derived from `PeerId`;
- never reused in the same game session after leave;
- stable across travel;
- used for a stable node name such as `PlayerState_<ParticipantId>`.

V1 does not implement reconnect, but this prevents the future assumption that a network connection is the
player identity.

## `PeerId`

Current Godot network peer ID for that participant. In V1 it is fixed until disconnect.

A future reconnect feature may preserve `ParticipantId` while assigning a new `PeerId`.

## Authority

`PlayerState` remains server authoritative.

Do **not** call:

```text
PlayerState.SetMultiplayerAuthority(PlayerState.PeerId)
```

The peer logically owns the identity, not Godot authority over the persistent state. Character selection,
loadout, team, score, etc. should follow:

```text
client request
    ↓
server project validation
    ↓
server mutates PlayerState
    ↓
project-authored MultiplayerSynchronizer replicates accepted data
```

## Project-specific PlayerState scene

`GameSession` takes a project-provided `PackedScene PlayerStateScene`.

Example:

```text
QuestWorldPlayerState
├── QuestWorldPlayerState : PlayerState
│   ├── CharacterDefinitionId
│   └── LoadoutId
└── MultiplayerSynchronizer
```

The generic addon never inspects those fields.

The invariant is:

> `game_session` replicates `PlayerState` lifecycle and identity; the project replicates the additional
> persistent contents it owns.

## PlayerState spawning

Use a dedicated persistent `MultiplayerSpawner` custom spawn.

Server spawn data is conceptually:

```text
{
    participant_id,
    peer_id
}
```

The spawn function on every peer must:

1. instantiate `PlayerStateScene`;
2. verify root type `PlayerState`;
3. assign `ParticipantId` and `PeerId` before tree entry;
4. assign the deterministic node name;
5. return the node without calling `AddChild()`.

`MultiplayerSpawner` owns adding the node under `Players`, replicating spawn/despawn and reconstructing
existing `PlayerState` nodes for late joins.

Identity is spawn construction data, not a `MultiplayerSynchronizer` concern.

Offline behavior may need a tiny engine-specific adapter when no peer exists, but it must reuse the same
spawn-data/factory path rather than inventing a second initialization model.

## Participant registry

`GameSession` maintains indexes:

```text
ParticipantId -> PlayerState
PeerId        -> PlayerState
```

Invariants:

- no duplicate participant ID;
- no two current participants share one peer ID;
- registration happens exactly once when the spawned `PlayerState` enters the persistent collection;
- removal clears every index;
- public project code never needs to parse node names.

## Admission

Server-owned and automatic by default:

```text
PeerConnected(peerId)
    ↓
effective admission open?
    ↓
CanJoin(peerId)
    ↓ accepted
allocate ParticipantId
    ↓
PlayerStateSpawner.Spawn(identity)
    ↓
register PlayerState
    ↓
PlayerJoined(PlayerState)
```

### Fresh runtime session startup

When the initialized `GameSession` receives `NetworkSession.Connected`:

- offline: create one participant for local peer ID `1`;
- host: create the host participant if absent;
- dedicated server: do not create a fake player for server peer ID `1`;
- client: never authoritatively create a local `PlayerState`; wait for server replication.

## `IsAcceptingPlayers`

Project-controlled intent:

```text
Lobby      -> true
Match lock -> false
```

Effective admission is conceptually:

```text
IsAcceptingPlayers && State == Active
```

During a global travel, admission is technically blocked regardless of the stored intent. On completion,
effective admission returns to the project's existing `IsAcceptingPlayers` value.

Where appropriate, the server mirrors effective admission into Godot's `RefuseNewConnections` to reject
transport connections early. A race-delivered `PeerConnected` still goes through admission and is rejected
without creating `PlayerState`.

## `CanJoin` escape hatch

Protected virtual hook only, defaulting to effective admission. A project can specialize max participant
count or other game-specific rules without a policy object.

If a fully connected peer is rejected, the server disconnects it and does not create a transient
`PlayerState`.

Rich pre-login handshakes/refusal UX remain out of scope.

## Disconnect

V1 is deliberately immediate:

```text
PeerDisconnected(peerId)
    ↓
remove any travel-ready requirement for peer
    ↓
resolve PlayerState
    ↓
authoritative despawn/free
    ↓
clear indexes
    ↓
PlayerLeft(...)
```

No reconnect grace period or detached state.

# World replication and travel

## Why the world itself uses MultiplayerSpawner

The current world is a network-replicated scene-tree root and should use the same native lifecycle
mechanism as other replicated scenes.

A **persistent** `WorldSpawner` solves two problems at once:

1. global travel: the server selects the next world once and all peers instantiate the same scene from the
   same spawn data;
2. late join: a new peer automatically receives the current world spawn from Godot's existing spawn
   history, instead of needing a bespoke catch-up scene RPC.

This also ensures the persistent replication root exists before any `MultiplayerSpawner` or synchronizer
nested inside the world can become relevant on the joining peer.

The generic addon therefore must not implement travel by sending `ChangeSceneToFile()` or by manually
calling `AddChild()` on each client from a `BeginTravel(resourcePath)` RPC.

## World spawn data

Conceptual custom spawn data:

```text
{
    travel_id,
    resource_path
}
```

The `WorldSpawner.SpawnFunction` must:

1. validate a non-empty resource path;
2. synchronously load the `PackedScene`;
3. instantiate it;
4. return the root node without calling `AddChild()`.

`MultiplayerSpawner` then adds the returned node under persistent `WorldContainer`.

The resource path is the V1 travel identity. Client and server builds are assumed to contain the same
resource.

No world registry/ID abstraction is added until a real need appears.

## Stable world naming

The managed world should have a deterministic name that does not depend on its scene-authored root name.

Recommended convention:

```text
World_<TravelId>
```

or another deterministic equivalent set from spawn data before tree entry.

The important invariant is identical path construction across peers and no collision with an old managed
world during replacement.

## `TravelId`

Monotonically increasing positive server-issued ID.

It correlates:

- world spawn data;
- local world readiness;
- client `WorldReady` acknowledgement;
- global barrier completion.

Stale IDs are ignored, future/unknown IDs rejected/ignored, and only one global travel runs at a time.

## Global travel public API

Conceptually:

```text
Travel(PackedScene worldScene)
```

Only server/authority may initiate.

Requirements before starting:

- `State == Active`;
- `worldScene` is non-null;
- `worldScene.ResourcePath` is non-empty/loadable;
- no other global travel is active.

The server should preflight load/instantiate enough to detect ordinary resource/type failure **before**
retiring the old world or telling clients a new travel has started.

## Authoritative global travel sequence

Conceptual sequence:

```text
server Travel(scene)
    ↓
preflight scene
    ↓
allocate TravelId
State = Traveling
block admission
snapshot required remote participant peers
    ↓
retire authoritative old world
    ↓
WorldSpawner.Spawn({TravelId, ResourcePath})
    ↓
Godot replicates world spawn to peers
    ↓
world enters tree on each peer
    ↓
local GameSession observes managed world ready
    ↓
server sets local-ready
remote clients send reliable TravelReady(TravelId)
    ↓
server waits for local-ready + all required remote peers
    ↓
reliable CompleteTravel(TravelId) to remote participants
    ↓
State = Active
TravelCompleted
```

The world spawn itself is not a custom travel RPC. Godot's `MultiplayerSpawner` is the authoritative
replication mechanism.

## Retiring the old world

There must never be two active gameplay worlds longer than required by the replacement operation.

On server global travel:

1. preflight the candidate scene first;
2. once travel is committed, remove/free the old authoritative world;
3. let `WorldSpawner` replicate despawn;
4. spawn the new world.

Clients follow replicated despawn/spawn ordering.

A normal preflight failure therefore leaves the old world intact.

An unexpected failure after old-world retirement is unrecoverable locally and moves `GameSession` to
`Failed`; V1 does not implement distributed rollback.

## Detecting local `WorldLoaded`

`WorldLoaded` is a **local** semantic event:

> the managed world for this travel exists under `WorldContainer` and its normal Godot ready lifecycle has
> completed on this process.

The implementation must not infer readiness merely from receipt of spawn data before `_Ready()`.

A focused generic mechanism is required, for example a deferred readiness check after `WorldSpawner.Spawned`
or an equivalent signal ordering that guarantees the spawned root has completed ready lifecycle. The exact
mechanism may follow Godot behavior/tests, but the semantic contract is fixed.

No generic async gameplay initialization is included. If a specific world needs streaming/backend work
after `_Ready()`, that project owns an additional readiness layer.

## `WorldLoaded` vs `TravelCompleted`

They are intentionally distinct:

```text
WorldLoaded
    this process has the new world ready

TravelCompleted
    the server released the barrier because every required process is ready
```

A client must not globally start gameplay merely because its own `WorldLoaded` fired.

## Global travel barrier

At travel start the server snapshots the **remote participant peers** required to acknowledge.

Barrier state distinguishes:

```text
server local world readiness
pending remote participant peer IDs
```

Rules:

- server local world readiness is always required;
- host participant peer `1` is satisfied by server local readiness, not a duplicate network ACK;
- dedicated server has no local `PlayerState` but still requires its own world ready;
- every remote participant present at travel start must ACK;
- joins are blocked during global travel;
- disconnect removes that peer from the pending set;
- duplicate ACK is idempotent;
- barrier completes once local-ready is true and pending set is empty.

Do not extract a generic `PeerBarrier` in V1.

## Client `WorldReady`

When a remote participant's process fires local `WorldLoaded` for the current travel ID, it sends a
reliable acknowledgement to server:

```text
WorldReady(TravelId)
```

Server validates:

- RPC sender is a current participant;
- ID matches the current world/travel;
- sender is expected by the active global barrier, or is pending readiness as a late join;
- duplicate is harmless.

For an active global travel, the acknowledgement removes the sender from that travel's pending set. For
a late join while no global travel is active, it clears only that participant's late-join pending state
and emits `PlayerWorldReady`. Stale, duplicate, and unexpected acknowledgements are ignored. Clients
cannot mark another peer ready or complete the barrier.

## Global travel completion

When barrier reaches zero:

1. server marks the travel complete once;
2. server `State = Active`;
3. effective admission returns to stored `IsAcceptingPlayers`;
4. server emits `TravelCompleted(TravelId, CurrentWorld)`;
5. server reliably notifies remote participants with `CompleteTravel(TravelId)`;
6. each client validates the ID, enters `Active` and emits its local `TravelCompleted` once.

After server `TravelCompleted`, project code may safely spawn world-local Pawns/Characters because every
participant required by that global travel already owns the world tree that will receive those spawns.

## Offline travel

Same semantic path without network transport:

```text
Travel
 -> State Traveling
 -> retire old local world
 -> instantiate via same world spawn factory/path
 -> WorldLoaded
 -> local barrier satisfied
 -> State Active
 -> TravelCompleted
```

If `MultiplayerSpawner` cannot perform a no-peer custom spawn directly in the intended way, the offline
adapter must still call the exact same world-spawn factory and lifecycle registration used by network mode.
There must not be a second public travel model.

## Host travel

Host uses authoritative world spawn locally and waits only for remote participant ACKs.

## Dedicated server travel

Dedicated server spawns/loads the authoritative world despite having no local player participant. Local
world readiness still gates completion.

# Late join while a world is active

## Native current-world reconstruction

Late join while `State == Active` and `IsAcceptingPlayers` permits it.

After admission, the new peer receives through persistent spawners:

1. existing `PlayerState` nodes;
2. its newly created `PlayerState`;
3. the current world spawn, if one exists.

This is why the world lifecycle uses persistent `WorldSpawner`: the joining peer does not need a bespoke
`BeginTravel(CurrentWorldPath)` catch-up RPC that could race nested world replication.

## Late-join readiness handshake

A late join still needs a readiness boundary before project code spawns that participant's world-local Pawn
or otherwise assumes the joining peer can receive world-local replicated nodes.

This is **not** a global travel and must not put existing peers into `Traveling`.

Server tracks a small per-participant pending-world-ready state for the joining peer when `CurrentWorld`
exists.

Conceptual flow:

```text
peer admitted
    ↓
PlayerState replicated
current WorldSpawner state replicated
    ↓
joining client observes current world ready
    ↓
reliable WorldReady(currentTravelId)
    ↓
server interprets sender from authoritative pending state
    ↓
PlayerWorldReady(PlayerState)
```

Existing participants continue playing normally throughout.

If there is no current world yet, admission completes with `PlayerJoined` only; project code can later use
the next global `TravelCompleted` boundary.

The same `WorldReady` RPC serves global travel and late join. The server's authoritative active-travel and
pending-late-join sets determine the meaning; a late-join acknowledgement must not mutate global
`GameSessionState` or the active global barrier.

## `PlayerWorldReady` signal

Provide a server-side semantic signal:

```text
PlayerWorldReady(PlayerState)
```

Meaning:

> this participant's peer has the current world ready and may safely receive world-local replicated
> incarnation/spawn state.

Emission rules:

- after a **global travel**, emit once for every participant whose peer is ready once the global barrier is
  released (host/local participant included as appropriate);
- after a **late join into an already active world**, emit only for that new participant once its current
  world ready ACK arrives;
- do not emit when no current world exists;
- do not emit twice for duplicate acknowledgements.

This gives project code one participant-oriented composition hook for Pawn spawning without putting Pawn
logic into `game_session`.

For a game that wants to spawn all Pawns together after global travel, it may still simply use
`TravelCompleted` and iterate `PlayerState`. `PlayerWorldReady` exists primarily to make late join equally
safe without a second project-specific network protocol.

# Failures

## Server preflight failure

If requested scene cannot be loaded/instantiated during preflight:

- keep old world;
- do not enter `Traveling` permanently;
- do not spawn/despawn replicated world state;
- emit `TravelFailed` diagnostics;
- do not emit `TravelCompleted`.

## Unrecoverable server replacement failure

If failure occurs after old world was retired and the session cannot continue:

- `State = Failed`;
- emit failure;
- never claim completion;
- project decides whether to restart/exit/menu.

## Client world load/spawn failure

If a client cannot instantiate the world scene from authoritative spawn data, it cannot safely remain in
that world.

The client reports a reliable diagnostic failure containing current world/travel identity; server validates
sender and disconnects/removes that participant. Remaining peers are not rolled back.

If failure happened during global travel, removing the participant also removes its pending ACK and barrier
may continue.

If failure happened during late-join current-world reconstruction, only that joining participant is
removed.

## Timeout

No generic timeout policy in V1. Expose enough state/signals for later project or generic policy if a real
requirement appears.

# Signals and queries

## Participant signals

```text
PlayerJoined(PlayerState)
PlayerLeft(identity data or still-valid PlayerState)
PlayerWorldReady(PlayerState)          # server semantic readiness for current world
```

`PlayerLeft` must not hand consumers an already-freed node.

## World/travel signals

```text
TravelStarted(TravelId, ResourcePath)
WorldLoaded(TravelId, CurrentWorld)    # local process
TravelCompleted(TravelId, CurrentWorld)
TravelFailed(TravelId, reason)
```

Signals fire exactly once per semantic transition.

## Public queries

At minimum:

```text
State
IsAcceptingPlayers
CurrentTravelId
CurrentWorld
CurrentWorldPath
PlayerStates / Players
TryGetPlayerStateByPeerId(peerId)
TryGetPlayerStateByParticipantId(participantId)
```

No consumer should need scene-tree scans or name parsing for participant lookup.

# GameMode / GameState boundary

The generic framework ends here.

A world may compose project-specific logic such as:

```text
Facility
├── FacilityGameMode
└── FacilityGameState
```

or any other shape. `game_session` neither requires nor models it.

The generic contract ends at:

```text
participant exists persistently
current world exists everywhere required
participant/world readiness is known
```

What the world does next is game-specific.

# Persistent PlayerController boundary

A project-specific local PlayerController may live in the persistent game root beside `GameSession` because
its lifetime can also span worlds. It remains outside the addon.

# QuestWorld integration target

The feature should eliminate `QuestWorldNetworkPlayers` as the owner of generic peer-to-player lifecycle.

Moves into generic `GameSession`:

- peer admission/removal;
- persistent `PlayerState` lifecycle;
- participant indexes;
- current world replicated lifecycle;
- global travel barrier;
- late-join current-world readiness.

Remains QuestWorld-specific:

- `QuestWorldPlayerState` fields;
- choosing Character definition from player state;
- spawn positions;
- Character/Pawn spawning/despawning;
- Character `OwnerPeerId` / authority configuration;
- local possession;
- current-world `Spawner` / `IWorldSpawner` integration.

Intended global travel flow:

```text
GameSession.TravelCompleted
    ↓ server
for each QuestWorldPlayerState
    choose Character definition
    spawn Character into CurrentWorld
    configure authority
    ↓
clients receive Character replication
    ↓
local project code possesses matching owner Character
```

Intended late-join flow:

```text
PlayerJoined
    ↓
PlayerState exists persistently
WorldSpawner reconstructs current world on joining client
    ↓
PlayerWorldReady(new PlayerState)
    ↓ server project integration
spawn only this participant's Character into CurrentWorld
```

A participant may validly exist without a Character between admission and world readiness.

## Current QuestWorld root migration

Current `World._Ready()` starts `NetworkSession` and initializes `QuestWorldNetworkPlayers`. That moves to a
persistent game root.

World scenes return to world-local concerns:

- authored spawners;
- authoritative world initialization;
- current-level gameplay.

They do not own network/game-session lifetime.

Any code using:

```text
GetTree().CurrentScene as IWorldSpawner
```

must be replaced by explicit current-world context/injection because `CurrentScene` becomes persistent
`Game.tscn`.

# Suggested addon boundary

```text
addons/game_session/
    scripts/
        GameSession.cs
        GameSessionState.cs
        PlayerState.cs
```

A small reusable `GameSession.tscn` is acceptable if it usefully authors:

```text
GameSession
├── Players
├── PlayerStateSpawner
├── WorldContainer
└── WorldSpawner
```

but a large prefab/framework hierarchy is not required.

Dependency direction:

```text
game_session -> network_session
```

It must not depend on QuestWorld, Character, Interaction, GameplayAction, Inventory or project-specific
Spawner abstractions.

# Expected API shape

This is a design contract, not an exact signature requirement:

```text
PlayerState : Node
    ParticipantId { get; }
    PeerId { get; }

GameSession : Node
    NetworkSession
    Players
    PlayerStateScene
    PlayerStateSpawner
    WorldContainer
    WorldSpawner

    State
    IsAcceptingPlayers
    CurrentWorld
    CurrentWorldPath
    CurrentTravelId

    Initialize()
    Travel(PackedScene worldScene)

    TryGetPlayerStateByPeerId(...)
    TryGetPlayerStateByParticipantId(...)
    GetPlayerStates()/Players

    protected virtual CanJoin(...)

    signal PlayerJoined
    signal PlayerLeft
    signal PlayerWorldReady
    signal TravelStarted
    signal WorldLoaded
    signal TravelCompleted
    signal TravelFailed
```

Do not add generic virtual `CreatePawn`, `StartMatch`, `OnTravelCompleted`, etc. Signals/composition own
those extensions.

# Authority rules

Server owns:

- participant creation/removal;
- ParticipantId allocation;
- admission;
- effective connection refusal;
- world spawn/despawn;
- TravelId allocation;
- global barrier membership/completion;
- per-late-join current-world readiness tracking.

Clients may:

- receive PlayerState/world replicated lifecycle;
- load/instantiate authoritative spawn data;
- ACK their own world readiness;
- report their own local world-instantiation failure;
- observe completion.

Clients may not:

- create/delete participants;
- initiate authoritative world travel;
- spawn authoritative current world;
- mark another participant ready;
- complete global barrier;
- mutate base PlayerState identity.

# Validation and error handling

Fail early on configuration errors:

- missing `NetworkSession`;
- missing `Players`;
- missing `PlayerStateScene`;
- missing/misconfigured `PlayerStateSpawner`;
- missing `WorldContainer`;
- missing/misconfigured `WorldSpawner`;
- `PlayerStateScene` root is not `PlayerState`;
- duplicate participant identities;
- more than one managed world in `WorldContainer`;
- invalid travel resource path;
- travel requested client-side;
- travel requested while already `Traveling`;
- unexpected/stale readiness sender/ID;
- current-world failure report from non-participant.

Expected refusal/stale messages should be rejected/ignored without forcing `Failed`. Reserve `Failed` for
local unrecoverable state.

# Testing requirements

This feature touches peer lifecycle, replication and cross-world runtime behavior. Tests must include
focused runtime coverage and real network integration.

Follow repository policy: smallest impacted suites first, then `task test:network` / `task test:runtime` as
appropriate; full suite only when cross-cutting validation justifies it.

Minimum behavior to prove:

## Participant lifecycle

1. offline initialization creates exactly one local `PlayerState`;
2. host initialization creates exactly one host `PlayerState`;
3. dedicated server creates no fake participant for peer `1`;
4. client never authors its own `PlayerState`;
5. server admits remote peer and replicates exactly one state everywhere;
6. late join receives existing `PlayerState` nodes;
7. disconnect removes state and indexes;
8. ParticipantId and PeerId are initialized before `PlayerState._Ready()`;
9. PlayerState remains server-authoritative for client participants;
10. project-specific derived `PlayerStateScene` is instantiated.

## Admission

11. `IsAcceptingPlayers=false` blocks new participants without removing existing ones;
12. custom `CanJoin` rejection creates no PlayerState;
13. global travel temporarily blocks joins;
14. completion restores effective admission from project intent.

## World lifecycle

15. first Travel spawns requested world under persistent `WorldContainer`;
16. host/client instantiate same resource path from `WorldSpawner` spawn data;
17. `WorldLoaded` fires only after normal ready lifecycle;
18. second Travel despawns old world and spawns fresh world;
19. GameSession and PlayerStates survive both travels;
20. `SceneTree.CurrentScene` remains persistent game root;
21. preflight failure keeps old world intact;
22. WorldSpawner is the network lifecycle owner, not bespoke create/delete RPCs.

## Global barrier

23. only server can initiate travel;
24. server waits for remote ACK before `TravelCompleted`;
25. local `WorldLoaded` may precede global completion;
26. duplicate ACK is idempotent;
27. stale ACK is ignored;
28. disconnect removes pending peer and can release barrier;
29. dedicated server local world readiness is required;
30. offline uses same semantic lifecycle without network RPCs;
31. client load failure removes/disconnects only that participant and does not rollback others;
32. `TravelCompleted` fires exactly once per successful travel ID per peer.

## Late join with active world

33. joining peer receives the current world through persistent `WorldSpawner` without a custom catch-up
    scene RPC;
34. existing peers remain `Active` and are not put through a second global travel;
35. joining peer does not produce `PlayerWorldReady` before its current world is locally ready;
36. valid readiness ACK emits `PlayerWorldReady` exactly once for that participant;
37. project can safely spawn the joining participant's world-local Pawn after that signal;
38. late-join world load failure removes only the joining participant.

## Persistent game-specific data

39. project-specific PlayerState property set before travel remains on the same persistent node across at
    least two world replacements;
40. this persistence does not require Character/Pawn nodes to survive travel.

# Success criteria

A minimal project can express:

```text
start host
 -> GameSession creates host PlayerState
 -> client joins
 -> server creates/replicates client PlayerState
 -> project stores lobby selections on PlayerState
 -> server Travel(Facility)
 -> WorldSpawner replaces current world everywhere
 -> all required peers report world ready
 -> TravelCompleted
 -> project spawns world-local Pawns from persistent PlayerStates
 -> later client joins
 -> PlayerStates + current world reconstruct natively
 -> PlayerWorldReady(new participant)
 -> project spawns only that late joiner's Pawn
```

The generic addon never knows what those Pawns are.

Final intended boundary:

```text
persistent
────────────────────────────────────
NetworkSession   connection lifecycle
GameSession      participants + replicated current world + readiness
PlayerState      persistent participant data
PlayerController project-local optional controller

travel boundary
────────────────────────────────────

world-local
────────────────────────────────────
World            current scene content
GameMode/State   project-specific if useful
Character/Pawn   participant incarnation
Gameplay actors
```

This is the stopping point for generic framework work in this area. GameMode/GameState-like abstractions
remain project-specific until independent implementations prove another reusable contract.
