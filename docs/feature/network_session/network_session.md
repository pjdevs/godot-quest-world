# Network Session

## Responsibility

The Network Session feature owns the lifecycle of the local Godot multiplayer peer. It supports
offline, listen-server, dedicated-server and client launch modes while keeping gameplay ownership
outside the generic session layer.

## Current architecture

`addons/network_session/scripts/NetworkSession.cs` is the generic runtime boundary. It exposes:

- `Start(NetworkLaunchOptions options)` and `Stop()`;
- `LocalPeerId`, `IsServer`, `IsDedicatedServer` and `State`;
- peer-connected and peer-disconnected signals;
- connected, disconnected and failed session signals.

It is responsible for creating and closing `ENetMultiplayerPeer`, wiring Godot multiplayer signals,
and transitioning `SessionState`. It does not know about characters, spawners, controllers or
QuestWorld gameplay.

`quest_world/network/QuestWorldNetworkPlayers.cs` is the QuestWorld integration glue. It references
the generic session, the authored `PlayerSpawner` and the local `CharacterPlayerController`. It
owns player spawning and despawning from peer lifecycle signals, resolves the local player by peer
ID, configures `OwnerPeerId` and multiplayer authority explicitly, and performs local possession.

`World._Ready()` parses the project command line, starts the generic session, initializes the player
glue, and then initializes authoritative world spawners.

`NetworkPlayerIdentity` remains QuestWorld-owned. Its player naming and spawn-position conventions
are not part of the generic addon contract.

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
subscribe to those signals and decide what a peer means in their own domain.

### QuestWorld keeps player ownership outside the session

The authored `NetworkSession` node uses the generic addon script. A separate `NetworkPlayers` node
connects the session to QuestWorld's character and spawner concepts without adding those concepts to
the transport layer. The `Player_<peerId>` node name remains an integration convention used by the
glue to identify remote spawned copies; it is not interpreted by the Character itself.
