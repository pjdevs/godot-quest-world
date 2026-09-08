# Network Session

## Responsibility

The Network Session feature owns the lifecycle of the local Godot multiplayer peer. It supports
offline, listen-server, dedicated-server and client launch modes while keeping gameplay ownership
outside the generic session layer.

## Current architecture

`addons/network_session/runtime/NetworkSession.cs` is the generic runtime boundary. It exposes:

- `Start(NetworkLaunchOptions options)` and `Stop()`;
- `SetAcceptingConnections(bool)` and `DisconnectPeer(long)` as narrow, defensive server controls;
- `LocalPeerId`, `IsServer`, `IsDedicatedServer` and `State`;
- peer-connected and peer-disconnected signals;
- connected, disconnected and failed session signals.

It is responsible for creating and closing `ENetMultiplayerPeer`, wiring Godot multiplayer signals,
transitioning `SessionState`, controlling server admission at the transport level, and disconnecting
remote peers on request. It does not know about participants, characters, spawners, controllers or
QuestWorld gameplay. Consumers never mutate or invoke the underlying `MultiplayerPeer` directly.

`quest_world/game/Game.tscn` is now the persistent project root. Its `Game` script parses the project
command line, initializes `GameSession` and `PlayerCharacterSpawnManager`, starts `NetworkSession`, then
requests the initial world on the server. `PlayerCharacterSpawnManager` consumes `GameSession` participant and readiness signals to
spawn world-local Characters; it no longer owns transport peer lifecycle directly.

World scenes are content-only. Their authored spawners remain available to project gameplay, while
network startup and the optional local controller live under the persistent `Game` root.

`PlayerNetworkIdentity` remains QuestWorld-owned. Its player naming, peer-name parsing and
spawn-position conventions are not part of the generic addon contract.

## Launch modes

`NetworkLaunchOptions` parses the project arguments after Godot's `--` separator:

- `--offline`
- `--host`
- `--server`
- `--client`
- `--connect <address>` or `--connect=<address>`
- `--port <port>` or `--port=<port>`
- `--max-players <count>` or `--max-players=<count>`

Without an explicit mode, the session starts offline. Offline and host sessions use local peer ID
`1`; clients receive their ENet peer ID when the server connection completes.

## Architecture decisions

### Generic session owns peer lifecycle only

The addon emits peer and session lifecycle signals instead of spawning gameplay nodes. Integrations
subscribe to those signals and decide what a peer means in their own domain. Server-side consumers use
the narrow `SetAcceptingConnections` and `DisconnectPeer` controls; both return `false` when the local
session lacks server authority or an applicable live peer. `DisconnectPeer` also rejects IDs outside
Godot's positive 32-bit peer-ID range before narrowing the public `long` signal identity. Admission
policy and participant identity stay outside this transport boundary.

### QuestWorld keeps player ownership outside the session

The authored `NetworkSession` node uses the generic addon script. A separate persistent `NetworkPlayers`
node connects `GameSession` to QuestWorld's character and world-spawner concepts without adding those
concepts to the transport layer. The `Player_<peerId>` node name remains an integration convention used
by the glue to identify remote spawned copies; it is not interpreted by the Character itself. Only the
server reacts to participant departure by destroying a Character; client indexes follow replicated
Character tree entry and exit.
